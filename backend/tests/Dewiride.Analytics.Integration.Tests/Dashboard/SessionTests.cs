using System.Net;
using System.Text.Json;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Integration.Tests.Fixtures;

namespace Dewiride.Analytics.Integration.Tests.Dashboard;

/// <summary>
/// Proves what signing in accepts, refuses, and gives away.
/// </summary>
/// <remarks>
/// The tests that matter most here are the ones about what the answers do <em>not</em> say. An
/// address with no account, an address with the wrong password, and an account locked after too
/// many attempts must be indistinguishable from outside, or the sign-in form becomes a way of
/// finding out who has an account on somebody's install.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class SessionTests(AnalyticsStackFixture stack)
{
    private const string Session = "/api/session";
    private const string Password = Passwords.Acceptable;

    [Fact]
    public async Task Correct_Details_Sign_Somebody_In()
    {
        var address = Address();
        await ControlPlaneSeed.AddAccountAsync(stack, address, Password);
        using var browser = await Browser.OpenAsync(stack);

        var response = await browser.PostAsync(Session, new SignInRequest
        {
            EmailAddress = address,
            Password = Password,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var session = await browser.DescribeAsync();

        session.User.Should().NotBeNull();
        session.User.EmailAddress.Should().Be(address);
    }

    [Fact]
    public async Task An_Address_Is_Recognised_However_It_Is_Capitalised()
    {
        var address = Address();
        await ControlPlaneSeed.AddAccountAsync(stack, address, Password);
        using var browser = await Browser.OpenAsync(stack);

        var response = await browser.PostAsync(Session, new SignInRequest
        {
            EmailAddress = address.ToUpperInvariant(),
            Password = Password,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Word for word the same answer, so that the form cannot be used to list who has an account.
    /// </summary>
    [Fact]
    public async Task A_Wrong_Password_And_An_Unknown_Address_Are_Answered_Identically()
    {
        var address = Address();
        await ControlPlaneSeed.AddAccountAsync(stack, address, Password);
        using var browser = await Browser.OpenAsync(stack);

        var wrongPassword = await browser.PostAsync(Session, new SignInRequest
        {
            EmailAddress = address,
            Password = "quite the wrong passphrase",
        });

        var unknownAddress = await browser.PostAsync(Session, new SignInRequest
        {
            EmailAddress = Address(),
            Password = Password,
        });

        wrongPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unknownAddress.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await WordingAsync(wrongPassword)).Should().Be(await WordingAsync(unknownAddress));
    }

    /// <summary>
    /// Lockout is what stops passwords being guessed, and saying it has happened would confirm
    /// the address belongs to somebody — for an account the caller can lock on demand.
    /// </summary>
    [Fact]
    public async Task An_Account_Locked_By_Repeated_Guesses_Is_Answered_Like_Any_Other_Failure()
    {
        var address = Address();
        await ControlPlaneSeed.AddAccountAsync(stack, address, Password);
        using var browser = await Browser.OpenAsync(stack);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await browser.PostAsync(Session, new SignInRequest
            {
                EmailAddress = address,
                Password = "quite the wrong passphrase",
            });
        }

        var locked = await browser.PostAsync(Session, new SignInRequest
        {
            EmailAddress = address,
            Password = Password,
        });

        locked.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await locked.Content.ReadAsStringAsync(Cancellation.Token))
            .Should().NotContainEquivalentOf("locked");
    }

    [Fact]
    public async Task A_Sign_In_That_Cannot_Prove_Where_It_Came_From_Is_Refused()
    {
        var address = Address();
        await ControlPlaneSeed.AddAccountAsync(stack, address, Password);
        using var browser = await Browser.OpenAsync(stack);

        var response = await browser.PostWithoutProofAsync(Session, new SignInRequest
        {
            EmailAddress = address,
            Password = Password,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await browser.DescribeAsync()).User.Should().BeNull();
    }

    [Fact]
    public async Task Signing_Out_Ends_The_Session()
    {
        var address = Address();
        await ControlPlaneSeed.AddAccountAsync(stack, address, Password);
        using var browser = await Browser.OpenAsync(stack);

        await browser.PostAsync(Session, new SignInRequest { EmailAddress = address, Password = Password });
        await browser.DescribeAsync();

        var goodbye = await browser.SignOutAsync();

        goodbye.StatusCode.Should().Be(HttpStatusCode.OK);
        (await browser.DescribeAsync()).User.Should().BeNull();
    }

    /// <summary>
    /// Signing out hands back what is needed to sign in again, so the form on the screen the
    /// person lands on works on its first attempt rather than its second.
    /// </summary>
    [Fact]
    public async Task Somebody_Who_Signs_Out_Can_Sign_Straight_Back_In()
    {
        var address = Address();
        await ControlPlaneSeed.AddAccountAsync(stack, address, Password);
        using var browser = await Browser.OpenAsync(stack);

        await browser.PostAsync(Session, new SignInRequest { EmailAddress = address, Password = Password });
        await browser.DescribeAsync();
        await browser.SignOutAsync();

        var again = await browser.PostAsync(
            Session,
            new SignInRequest { EmailAddress = address, Password = Password });

        again.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Nobody_Signed_In_Is_Refused_The_List_Of_Sites()
    {
        using var browser = await Browser.OpenAsync(stack);

        var response = await browser.GetAsync("/api/sites");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// This process serves data and never a page, so it must never send anybody to a sign-in
    /// screen it does not have.
    /// </summary>
    /// <remarks>
    /// The address a person types first is the bare server address, which matches nothing. Left to
    /// its defaults the framework answers that with a redirect to a sign-in page that was never
    /// built, which is itself unauthenticated and redirects again, until the browser gives up and
    /// shows an error instead of anything useful.
    /// </remarks>
    [Theory]
    [InlineData("/")]
    [InlineData("/api/sites")]
    [InlineData("/somewhere-that-does-not-exist")]
    public async Task An_Address_Reached_Without_Signing_In_Is_Answered_Rather_Than_Redirected(string path)
    {
        using var browser = await Browser.OpenAsync(stack);

        var response = await browser.GetAsync(path);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);
        response.Headers.Location.Should().BeNull();
    }

    /// <summary>
    /// Nothing about who is signed in may be held anywhere between the server and the screen.
    /// </summary>
    [Fact]
    public async Task The_Answer_About_Who_Is_Signed_In_Is_Never_Cached()
    {
        using var browser = await Browser.OpenAsync(stack);

        var response = await browser.GetAsync("/api/session");

        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl.NoStore.Should().BeTrue();
    }

    /// <summary>
    /// Locking one account stops passwords being guessed for it. This is what stops the same
    /// person moving on to the next account instead, which lockout never sees.
    /// </summary>
    [Fact]
    public async Task An_Address_Guessing_Past_Its_Allowance_Is_Turned_Away()
    {
        using var throttled = stack.WithWebHostBuilder(builder =>
            builder.UseSetting(TestSettings.SignInAllowance, "3"));

        using var browser = await Browser.OpenAsync(throttled);
        var guess = new SignInRequest { EmailAddress = Address(), Password = Password };

        var spent = new List<HttpStatusCode>();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var used = await browser.PostAsync(Session, guess);
            spent.Add(used.StatusCode);
        }

        var refused = await browser.PostAsync(Session, guess);

        spent.Should().AllBeEquivalentTo(HttpStatusCode.Unauthorized);
        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    private static string Address() => $"session-{Guid.NewGuid():n}@example.com";

    /// <summary>
    /// Everything a refusal says, apart from the identifier that ties it to a line in the log.
    /// </summary>
    /// <remarks>
    /// That identifier is different on every request by design and tells a caller nothing about
    /// the account. What matters is that the rest of the document — the status, the heading and
    /// the sentence beneath it — is the same whichever way the attempt failed.
    /// </remarks>
    private static async Task<string> WordingAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(Cancellation.Token));

        var wording = document.RootElement.EnumerateObject()
            .Where(member => !string.Equals(member.Name, "traceId", StringComparison.Ordinal))
            .Select(member => $"{member.Name}={member.Value}");

        return string.Join('|', wording);
    }
}
