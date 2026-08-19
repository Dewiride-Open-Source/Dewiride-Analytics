using System.Net;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Domain.Sites;

namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// Opens a browser that is signed in, holding a role on the sites it was given.
/// </summary>
/// <remarks>
/// Every account is created for the one test that uses it. The suite shares a single running
/// stack, so an address used twice would make two tests the same person, and a test that removed
/// that person's last site would decide what a test running beside it was allowed to do.
/// </remarks>
internal static class SignedIn
{
    /// <summary>
    /// Creates an account, grants it a role on each site named, and signs it in.
    /// </summary>
    /// <param name="stack">The running stack.</param>
    /// <param name="role">The role to grant on every one of them.</param>
    /// <param name="siteIds">The sites to grant it on.</param>
    /// <returns>The browser, signed in and holding a proof-of-origin token.</returns>
    public static async Task<Browser> AsAsync(
        AnalyticsStackFixture stack,
        SiteRole role,
        params Guid[] siteIds)
    {
        ArgumentNullException.ThrowIfNull(siteIds);

        var address = Address();
        var (created, account) = await ControlPlaneSeed
            .AddAccountAsync(stack, address, Passwords.Acceptable)
            .ConfigureAwait(false);

        created.Succeeded.Should().BeTrue();

        foreach (var siteId in siteIds)
        {
            await ControlPlaneSeed.GrantAsync(stack, siteId, account.Id, role).ConfigureAwait(false);
        }

        return await AsAccountAsync(stack, address).ConfigureAwait(false);
    }

    /// <summary>
    /// Signs an existing account in.
    /// </summary>
    /// <remarks>
    /// For the tests that need the account's identifier as well as a browser, which is anything
    /// that goes on to resolve something the engine would resolve for itself on a request.
    /// </remarks>
    /// <param name="stack">The running stack.</param>
    /// <param name="emailAddress">The address the account was registered under.</param>
    /// <returns>The browser, signed in and holding a proof-of-origin token.</returns>
    public static async Task<Browser> AsAccountAsync(AnalyticsStackFixture stack, string emailAddress)
    {
        var browser = await Browser.OpenAsync(stack).ConfigureAwait(false);
        var response = await browser
            .PostAsync("/api/session", new SignInRequest
            {
                EmailAddress = emailAddress,
                Password = Passwords.Acceptable,
            })
            .ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Re-read, because the token the engine issues is tied to whoever was signed in when it
        // was issued. The one this browser was opened with belongs to nobody.
        await browser.DescribeAsync().ConfigureAwait(false);

        return browser;
    }

    /// <summary>An address no other test in the run will use.</summary>
    /// <returns>The address.</returns>
    public static string Address() => $"{Guid.NewGuid():n}@example.com";
}
