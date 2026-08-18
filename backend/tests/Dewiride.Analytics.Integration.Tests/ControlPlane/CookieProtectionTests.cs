using System.Net;
using Dewiride.Analytics.Api.Composition;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Infrastructure.Persistence;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.ControlPlane;

/// <summary>
/// Proves the keys that protect sign-in cookies outlive the process that made them.
/// </summary>
/// <remarks>
/// The framework's default is a folder inside the running container. Left that way, replacing the
/// container signs everybody out, and a second instance of the API cannot read a cookie the first
/// one issued — a failure that only shows itself on the day somebody deploys or scales up.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class CookieProtectionTests(AnalyticsStackFixture stack)
{
    private const string Password = Passwords.Acceptable;

    [Fact]
    public async Task The_Keys_Are_Written_To_The_Control_Plane_Database()
    {
        // Opening the dashboard makes the server protect something, which is what creates the key
        // ring if there is not one already.
        using var browser = await Browser.OpenAsync(stack);

        await using var scope = stack.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        var stored = await database.DataProtectionKeys.AsNoTracking().CountAsync(Cancellation.Token);

        stored.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// A second copy of the product, reading the same database, accepts a sign-in the first one
    /// issued. This is the property a restart and a second instance both depend on.
    /// </summary>
    [Fact]
    public async Task A_Second_Instance_Accepts_A_Sign_In_The_First_One_Issued()
    {
        var address = $"protected-{Guid.NewGuid():n}@example.com";
        await ControlPlaneSeed.AddAccountAsync(stack, address, Password);

        using var browser = await Browser.OpenAsync(stack);

        var signedIn = await browser.PostAsync(
            "/api/session",
            new SignInRequest { EmailAddress = address, Password = Password });

        signedIn.StatusCode.Should().Be(HttpStatusCode.OK);

        using var second = stack.WithWebHostBuilder(_ => { });
        using var elsewhere = second.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/sites", UriKind.Relative));
        request.Headers.Add("Cookie", SessionCookie(signedIn));

        var response = await elsewhere.SendAsync(request, Cancellation.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Picks the sign-in cookie out of the answer, as a browser would.
    /// </summary>
    private static string SessionCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie")
            .Select(header => header.Split(';')[0])
            .Single(pair => pair.StartsWith(
                $"{AuthenticationRegistration.SessionCookieName}=",
                StringComparison.Ordinal));
}
