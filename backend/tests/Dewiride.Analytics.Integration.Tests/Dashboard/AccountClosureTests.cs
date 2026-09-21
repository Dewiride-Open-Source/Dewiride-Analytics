using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using ClickHouse.Driver;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Application.Accounts;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Persistence;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Dewiride.Analytics.Integration.Tests.Dashboard;

/// <summary>
/// Covers closing the account somebody belongs to, what a closed account can no longer do, and
/// bringing it back.
/// </summary>
/// <remarks>
/// <para>
/// Closing is reversible for thirty days, and that is the whole of what makes it safe to offer:
/// nothing is deleted, so what has to be proved is that a closed account is invisible on every
/// path that reads or writes it — the site list, every site screen, the account screen, an
/// invitation into it, and both collectors — and that bringing it back undoes all of that with
/// one press.
/// </para>
/// <para>
/// Which account is closed is never in the request. It is the one the caller owns, so the tests
/// about who may close are also tests that the right account was closed and nobody else's. The
/// one that carries the most weight is the person in two accounts: closing one of them must not
/// wall them off from the other, and must still leave them able to bring the closed one back.
/// </para>
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class AccountClosureTests(AnalyticsStackFixture stack)
{
    private const string Closure = "/api/organization/closure";
    private const string Organization = "/api/organization";
    private const string Invitations = "/api/organization/invitations";
    private const string Preview = "/api/invitations/preview";
    private const string Sites = "/api/sites";
    private const string Collect = "/collect";
    private const string ServerCollect = "/collect/server";

    /// <summary>A crawler that fetches HTML and runs nothing, which only a server-side reporter sees.</summary>
    private const string Crawler = "Mozilla/5.0 (compatible; ExampleBot/1.0; +https://example.test/bot)";

    /// <summary>
    /// The thirty days the terms promise a closed account is kept for.
    /// </summary>
    /// <remarks>
    /// Written as the number the customer was promised rather than read from the engine, so that
    /// a change to what the engine keeps breaks this test instead of quietly rewording a promise.
    /// </remarks>
    private static readonly TimeSpan PromisedRetention = TimeSpan.FromDays(30);

    /// <summary>
    /// What closing and restoring are sent with.
    /// </summary>
    /// <remarks>
    /// Neither endpoint reads a body: the account acted on is the caller's own, and the typed
    /// confirmation of its name happens on the screen. A browser still sends one.
    /// </remarks>
    private static readonly object EmptyBody = new();

    [Fact]
    public async Task An_Owner_Can_Close_The_Account_And_Is_Told_When_It_Will_Be_Deleted()
    {
        using var account = await AccountWithOwnerAsync().ConfigureAwait(true);
        var name = await NameOfAccountAsync(account.Owner).ConfigureAwait(true);
        var before = Now;

        using var response = await account.Owner.PostAsync(Closure, EmptyBody).ConfigureAwait(true);

        var session = await SessionInAsync(response).ConfigureAwait(true);

        session.User.Should().NotBeNull();
        session.Closure.Should().NotBeNull();
        session.Closure.Name.Should().Be(name);
        session.Closure.ClosedAt.Should().BeCloseTo(before, TimeSpan.FromMinutes(1));
        session.Closure.DeletionDue.Should().Be(session.Closure.ClosedAt + PromisedRetention);
        session.Closure.ClosedByYou.Should().BeTrue();
        session.Closure.CanRestore.Should().BeTrue();
        session.Closure.HasOpenAccount.Should().BeFalse();
    }

    /// <summary>
    /// Closing stops measurement for every website and takes the whole team's screens away, so
    /// it belongs to an owner alone. Somebody who helps run the account and somebody who merely
    /// belongs to it are both refused, and the account is exactly as it was.
    /// </summary>
    [Theory]
    [InlineData(OrganizationRole.Admin)]
    [InlineData(OrganizationRole.Member)]
    public async Task Somebody_Who_Does_Not_Own_The_Account_Cannot_Close_It(OrganizationRole role)
    {
        using var account = await AccountWithOwnerAsync().ConfigureAwait(true);
        using var other = await JoinAsync(account.Site.OrganizationId, role).ConfigureAwait(true);

        using var response = await other.Browser.PostAsync(Closure, EmptyBody).ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var unchanged = await other.Browser.GetAsync(Organization).ConfigureAwait(true);
        unchanged.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Somebody_Who_Belongs_To_No_Account_Is_Told_There_Is_Nothing_To_Close()
    {
        using var stranger = await SignedIn.AsAsync(stack, SiteRole.Viewer).ConfigureAwait(true);

        using var response = await stranger.PostAsync(Closure, EmptyBody).ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// A cookie the browser returns on its own is not proof that this page meant to send the
    /// request, and this is a request another site would most like to send on an owner's behalf.
    /// </summary>
    [Fact]
    public async Task Closing_Without_Proof_Of_Where_It_Came_From_Is_Refused()
    {
        using var account = await AccountWithOwnerAsync().ConfigureAwait(true);

        using var response = await account.Owner.PostWithoutProofAsync(Closure, EmptyBody).ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var unchanged = await account.Owner.GetAsync(Organization).ConfigureAwait(true);
        unchanged.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Nobody_Signed_In_Can_Close_Or_Bring_Back_An_Account()
    {
        using var browser = await Browser.OpenAsync(stack).ConfigureAwait(true);

        using var closing = await browser.PostAsync(Closure, EmptyBody).ConfigureAwait(true);
        using var restoring = await browser.DeleteAsync(Closure).ConfigureAwait(true);

        closing.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        restoring.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// A second press changes nothing, and says so by name so the screen can move the reader on.
    /// The instant the first press recorded stays, because the deletion date hangs off it.
    /// </summary>
    [Fact]
    public async Task Closing_A_Closed_Account_Is_Refused_By_Name_And_Moves_Nothing()
    {
        using var account = await AccountWithOwnerAsync().ConfigureAwait(true);
        var first = await CloseAsync(account.Owner).ConfigureAwait(true);

        using var again = await account.Owner.PostAsync(Closure, EmptyBody).ConfigureAwait(true);

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Refusal.ReasonsOfAsync(again).ConfigureAwait(true))
            .Should().Contain(ClosureCodes.AlreadyClosed);

        var session = await account.Owner.DescribeAsync().ConfigureAwait(true);

        session.Closure.Should().NotBeNull();
        session.Closure.ClosedAt.Should().Be(first.ClosedAt);
    }

    /// <summary>
    /// A closed account's websites answer as though they were not there: absent from the list,
    /// and every screen below it answers exactly as it does for a website the caller has no role
    /// on, so nothing about the account can be read while it is closed.
    /// </summary>
    [Fact]
    public async Task A_Closed_Account_Has_No_Websites_To_List_Or_Open()
    {
        using var account = await AccountWithOwnerAsync().ConfigureAwait(true);
        await CloseAsync(account.Owner).ConfigureAwait(true);

        using var listed = await account.Owner.GetAsync(Sites).ConfigureAwait(true);
        (await SitesInAsync(listed).ConfigureAwait(true)).Should().BeEmpty();

        using var settings = await account.Owner.GetAsync(Settings(account.Site.Id)).ConfigureAwait(true);
        using var overview = await account.Owner.GetAsync(Overview(account.Site.Id)).ConfigureAwait(true);

        settings.StatusCode.Should().Be(HttpStatusCode.NotFound);
        overview.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_Closed_Account_Cannot_Be_Read_Or_Renamed()
    {
        using var account = await AccountWithOwnerAsync().ConfigureAwait(true);
        await CloseAsync(account.Owner).ConfigureAwait(true);

        using var read = await account.Owner.GetAsync(Organization).ConfigureAwait(true);
        using var renamed = await account.Owner
            .PatchAsync(Organization, new RenameOrganizationRequest { Name = "Renamed while closed" })
            .ConfigureAwait(true);

        read.StatusCode.Should().Be(HttpStatusCode.NotFound);
        renamed.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// A link into a closed account is answered exactly like a spent one. Whoever holds it needs
    /// the same thing done — being asked again once the account is back — and a different answer
    /// would say, to anybody holding a link, whether an account still exists.
    /// </summary>
    [Fact]
    public async Task An_Invitation_Into_A_Closed_Account_Stops_Working()
    {
        await using var install = MailboxInstall.Start(stack);
        using var account = await AccountWithOwnerAsync(install).ConfigureAwait(true);
        var invited = SignedIn.Address();

        using var sent = await account.Owner
            .PostAsync(Invitations, new InvitePersonRequest { EmailAddress = invited, Role = "member" })
            .ConfigureAwait(true);

        sent.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var token = TokenSentTo(install, invited);
        using var browser = await Browser.OpenAsync(install).ConfigureAwait(true);

        using var usable = await browser
            .PostAsync(Preview, new InvitationTokenRequest { Token = token })
            .ConfigureAwait(true);

        usable.StatusCode.Should().Be(HttpStatusCode.OK);

        await CloseAsync(account.Owner).ConfigureAwait(true);

        using var refused = await browser
            .PostAsync(Preview, new InvitationTokenRequest { Token = token })
            .ConfigureAwait(true);

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Refusal.ReasonsOfAsync(refused).ConfigureAwait(true))
            .Should().Contain(OrganizationEndpointCodes.LinkNotUsable);
    }

    /// <summary>
    /// The collector resolves a website out of a cache on every report, so until that entry is
    /// thrown away it goes on accepting reports for an account whose measurement was supposed to
    /// have stopped. The website is resolved first, so the collector is holding it when the
    /// closure lands, and a report sent straight afterwards gets the same empty answer as a
    /// stored one while nothing is written.
    /// </summary>
    [Fact]
    public async Task The_Collector_Stops_Accepting_Reports_The_Moment_An_Account_Is_Closed()
    {
        using var account = await AccountWithOwnerAsync().ConfigureAwait(true);
        using var client = stack.CreateClient();
        var report = Report(account.Site.Id, $"https://{account.Site.Domain}/posts/hello");

        (await CollectorFindsAsync(account.Site.Id).ConfigureAwait(true)).Should().NotBeNull();

        using var before = await PostBeaconAsync(client, report, account.Site.Domain).ConfigureAwait(true);

        before.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StoredCountAsync(account.Site.Id).ConfigureAwait(true)).Should().Be(1);

        await CloseAsync(account.Owner).ConfigureAwait(true);

        (await CollectorFindsAsync(account.Site.Id).ConfigureAwait(true)).Should().BeNull();

        using var after = await PostBeaconAsync(client, report, account.Site.Domain).ConfigureAwait(true);

        after.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StoredCountAsync(account.Site.Id).ConfigureAwait(true)).Should().Be(1);
    }

    /// <summary>
    /// A server key still authorises the reporter — it was issued for the website and has not
    /// been withdrawn — but every observation it reports is turned away, and the count in the
    /// answer says so, which is the one place a reporter's operator can see that it happened.
    /// </summary>
    [Fact]
    public async Task A_Server_Reporting_For_A_Closed_Account_Has_Every_Observation_Rejected()
    {
        using var account = await AccountWithOwnerAsync().ConfigureAwait(true);
        var secret = await ControlPlaneSeed.AddServerKeyAsync(stack, account.Site.Id).ConfigureAwait(true);
        await CloseAsync(account.Owner).ConfigureAwait(true);

        using var client = stack.CreateClient();
        using var answer = await PostBatchAsync(
                client,
                secret,
                Batch(
                    "cloudflare-worker",
                    Observation(account.Site.Domain, "/posts/hello"),
                    Observation(account.Site.Domain, "/.env", status: 404)))
            .ConfigureAwait(true);

        answer.StatusCode.Should().Be(HttpStatusCode.OK);

        var counted = await CountedAsync(answer).ConfigureAwait(true);

        counted.Accepted.Should().Be(0);
        counted.Rejected.Should().Be(2);
        (await StoredCountAsync(account.Site.Id).ConfigureAwait(true)).Should().Be(0);
    }

    /// <summary>
    /// Everybody in a closed account is told what happened to it, from the session, before the
    /// dashboard draws anything. Somebody who does not own it is told who closed it and when it
    /// goes, and is not offered the button that only an owner may press.
    /// </summary>
    [Fact]
    public async Task Somebody_In_A_Closed_Account_Is_Told_About_It_And_Not_Offered_To_Bring_It_Back()
    {
        using var account = await AccountWithOwnerAsync().ConfigureAwait(true);
        using var member = await JoinAsync(account.Site.OrganizationId, OrganizationRole.Member).ConfigureAwait(true);
        var closed = await CloseAsync(account.Owner).ConfigureAwait(true);

        var session = await member.Browser.DescribeAsync().ConfigureAwait(true);

        session.User.Should().NotBeNull();
        session.Closure.Should().NotBeNull();
        session.Closure.Name.Should().Be(closed.Name);
        session.Closure.ClosedAt.Should().Be(closed.ClosedAt);
        session.Closure.DeletionDue.Should().Be(closed.DeletionDue);
        session.Closure.ClosedBy.Should().NotBeNullOrWhiteSpace();
        session.Closure.ClosedByYou.Should().BeFalse();
        session.Closure.CanRestore.Should().BeFalse();
        session.Closure.HasOpenAccount.Should().BeFalse();
    }

    /// <summary>
    /// Bringing an account back is one press, and everything closing took away comes back with
    /// it: the closure is gone from the session, the websites are listed again, the account can
    /// be read again, and the collector accepts reports for it at once.
    /// </summary>
    [Fact]
    public async Task An_Owner_Can_Bring_A_Closed_Account_Back_Exactly_As_It_Was()
    {
        using var account = await AccountWithOwnerAsync().ConfigureAwait(true);
        using var client = stack.CreateClient();
        var report = Report(account.Site.Id, $"https://{account.Site.Domain}/posts/hello");
        await CloseAsync(account.Owner).ConfigureAwait(true);

        using var refused = await PostBeaconAsync(client, report, account.Site.Domain).ConfigureAwait(true);

        refused.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StoredCountAsync(account.Site.Id).ConfigureAwait(true)).Should().Be(0);

        using var response = await account.Owner.DeleteAsync(Closure).ConfigureAwait(true);

        var session = await SessionInAsync(response).ConfigureAwait(true);

        session.User.Should().NotBeNull();
        session.Closure.Should().BeNull();

        using var listed = await account.Owner.GetAsync(Sites).ConfigureAwait(true);
        (await SitesInAsync(listed).ConfigureAwait(true)).Select(site => site.Id).Should().Equal(account.Site.Id);

        using var read = await account.Owner.GetAsync(Organization).ConfigureAwait(true);
        read.StatusCode.Should().Be(HttpStatusCode.OK);

        using var accepted = await PostBeaconAsync(client, report, account.Site.Domain).ConfigureAwait(true);

        accepted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StoredCountAsync(account.Site.Id).ConfigureAwait(true)).Should().Be(1);
    }

    [Theory]
    [InlineData(OrganizationRole.Admin)]
    [InlineData(OrganizationRole.Member)]
    public async Task Somebody_Who_Does_Not_Own_The_Account_Cannot_Bring_It_Back(OrganizationRole role)
    {
        using var account = await AccountWithOwnerAsync().ConfigureAwait(true);
        using var other = await JoinAsync(account.Site.OrganizationId, role).ConfigureAwait(true);
        await CloseAsync(account.Owner).ConfigureAwait(true);

        using var response = await other.Browser.DeleteAsync(Closure).ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await other.Browser.DescribeAsync().ConfigureAwait(true)).Closure.Should().NotBeNull();
    }

    [Fact]
    public async Task An_Open_Account_Cannot_Be_Brought_Back()
    {
        using var account = await AccountWithOwnerAsync().ConfigureAwait(true);

        using var response = await account.Owner.DeleteAsync(Closure).ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Refusal.ReasonsOfAsync(response).ConfigureAwait(true))
            .Should().Contain(ClosureCodes.NotClosed);
    }

    /// <summary>
    /// Two owners pressing the button together close the account once. The second is told it was
    /// already closed rather than closing it again, because a second closure would move the
    /// instant the deletion date and the message to every owner hang off.
    /// </summary>
    [Fact]
    public async Task Two_Owners_Closing_At_Once_Close_It_Once()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain()).ConfigureAwait(true);
        var first = await OwnerAddedAsync(site.OrganizationId).ConfigureAwait(true);
        var second = await OwnerAddedAsync(site.OrganizationId).ConfigureAwait(true);

        // Started on separate threads rather than one after the other, so both are genuinely inside
        // the operation at the same moment and the lock is asked the question it exists for.
        var outcomes = await Task.WhenAll(
                Task.Run(() => DirectlyCloseAsync(first.UserId)),
                Task.Run(() => DirectlyCloseAsync(second.UserId)))
            .ConfigureAwait(true);

        outcomes.Should().BeEquivalentTo([ClosureOutcome.Closed, ClosureOutcome.AlreadyClosed]);

        var stored = await StoredClosureAsync(site.OrganizationId).ConfigureAwait(true);

        stored.ClosedAt.Should().NotBeNull();
        stored.ClosedBy.Should().Be(outcomes[0] == ClosureOutcome.Closed ? first.UserId : second.UserId);
    }

    /// <summary>
    /// Access to websites already crosses accounts, so closing one account must not wall
    /// somebody off from another they still have. They keep the open account's websites, are told
    /// about the closed one beside their dashboard rather than instead of it, and can still bring
    /// it back.
    /// </summary>
    [Fact]
    public async Task Somebody_With_An_Open_Account_Keeps_It_While_Another_Is_Closed()
    {
        using var account = await AccountWithOwnerAsync().ConfigureAwait(true);
        var elsewhere = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain()).ConfigureAwait(true);

        await ControlPlaneSeed
            .GrantInOrganizationAsync(stack, elsewhere.OrganizationId, account.OwnerId, OrganizationRole.Member)
            .ConfigureAwait(true);

        var name = await NameOfAccountAsync(account.Owner).ConfigureAwait(true);
        var closed = await CloseAsync(account.Owner).ConfigureAwait(true);

        closed.Name.Should().Be(name);
        closed.HasOpenAccount.Should().BeTrue();
        closed.CanRestore.Should().BeTrue();

        using var listed = await account.Owner.GetAsync(Sites).ConfigureAwait(true);
        (await SitesInAsync(listed).ConfigureAwait(true)).Select(site => site.Id).Should().Equal(elsewhere.Id);

        using var restored = await account.Owner.DeleteAsync(Closure).ConfigureAwait(true);
        (await SessionInAsync(restored).ConfigureAwait(true)).Closure.Should().BeNull();

        using var again = await account.Owner.GetAsync(Sites).ConfigureAwait(true);
        (await SitesInAsync(again).ConfigureAwait(true))
            .Select(site => site.Id)
            .Should().BeEquivalentTo([account.Site.Id, elsewhere.Id]);
    }

    /// <summary>
    /// A standing in a closed account is not somewhere a new website can go, so it must not count
    /// as one either when the last website somebody owns is being protected. Somebody who helps
    /// run a closed account and owns one website in an open one is kept that website.
    /// </summary>
    [Fact]
    public async Task A_Standing_In_A_Closed_Account_Does_Not_Let_Somebody_Give_Up_Their_Only_Website()
    {
        var closing = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain()).ConfigureAwait(true);
        var owner = await OwnerAddedAsync(closing.OrganizationId).ConfigureAwait(true);
        using var admin = await JoinAsync(closing.OrganizationId, OrganizationRole.Admin).ConfigureAwait(true);
        var only = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain()).ConfigureAwait(true);

        await ControlPlaneSeed.GrantAsync(stack, only.Id, admin.UserId, SiteRole.Owner).ConfigureAwait(true);

        (await DirectlyCloseAsync(owner.UserId).ConfigureAwait(true)).Should().Be(ClosureOutcome.Closed);

        using var response = await admin.Browser.DeleteAsync($"{Sites}/{only.Id}").ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Refusal.ReasonsOfAsync(response).ConfigureAwait(true))
            .Should().Contain(ClosureCodes.SiteIsOnlyOne);

        using var listed = await admin.Browser.GetAsync(Sites).ConfigureAwait(true);
        (await SitesInAsync(listed).ConfigureAwait(true)).Select(site => site.Id).Should().Equal(only.Id);
    }

    /// <summary>
    /// Every owner is told, including the one who pressed the button: a closure nobody meant can
    /// only be noticed in time if it is announced. Each owner's message is a message of its own,
    /// because whatever delivers them treats one key as one message and would otherwise hand the
    /// second owner the first owner's answer, or nothing. The deletion day is in the message,
    /// because it is the one thing the reader has to act before.
    /// </summary>
    [Fact]
    public async Task Every_Owner_Is_Told_Once_When_The_Account_Is_Closed_And_Again_When_It_Is_Back()
    {
        await using var install = MailboxInstall.Start(stack);
        using var account = await AccountWithOwnerAsync(install).ConfigureAwait(true);
        var second = await OwnerAddedAsync(account.Site.OrganizationId).ConfigureAwait(true);

        var closed = await CloseAsync(account.Owner).ConfigureAwait(true);
        var deletionDay = closed.DeletionDue.UtcDateTime.ToString("d MMMM", CultureInfo.InvariantCulture);

        install.Mailbox.CountTo(account.OwnerAddress).Should().Be(1);
        install.Mailbox.CountTo(second.Address).Should().Be(1);

        var toFirst = install.Mailbox.LastTo(account.OwnerAddress);
        var toSecond = install.Mailbox.LastTo(second.Address);

        toFirst.Should().NotBeNull();
        toSecond.Should().NotBeNull();
        toFirst.IdempotencyKey.Should().NotBe(toSecond.IdempotencyKey);
        toFirst.PlainText.Should().Contain(deletionDay);
        toSecond.PlainText.Should().Contain(deletionDay);

        using var restored = await account.Owner.DeleteAsync(Closure).ConfigureAwait(true);

        restored.StatusCode.Should().Be(HttpStatusCode.OK);
        install.Mailbox.CountTo(account.OwnerAddress).Should().Be(2);
        install.Mailbox.CountTo(second.Address).Should().Be(2);

        var backToFirst = install.Mailbox.LastTo(account.OwnerAddress);
        var backToSecond = install.Mailbox.LastTo(second.Address);

        backToFirst.Should().NotBeNull();
        backToSecond.Should().NotBeNull();
        backToFirst.Subject.Should().Be("Your account is back");
        backToSecond.Subject.Should().Be("Your account is back");
        backToFirst.IdempotencyKey.Should().NotBe(toFirst.IdempotencyKey);
        backToFirst.IdempotencyKey.Should().NotBe(backToSecond.IdempotencyKey);
    }

    /// <summary>The present moment, taken from the host's own clock rather than the machine's.</summary>
    private DateTimeOffset Now => stack.Services.GetRequiredService<TimeProvider>().GetUtcNow();

    private IClickHouseClient Telemetry => stack.Services.GetRequiredService<IClickHouseClient>();

    private static string Settings(Guid siteId) => $"{Sites}/{siteId}/settings";

    private static string Overview(Guid siteId) => $"{Sites}/{siteId}/overview";

    private static string Domain() => $"closure-{Guid.NewGuid():n}.example";

    /// <summary>
    /// Closes the account the browser's owner belongs to, and reads back what they were told.
    /// </summary>
    private static async Task<ClosedAccountSummary> CloseAsync(Browser owner)
    {
        using var response = await owner.PostAsync(Closure, EmptyBody).ConfigureAwait(false);

        var session = await SessionInAsync(response).ConfigureAwait(false);

        session.Closure.Should().NotBeNull();

        return session.Closure;
    }

    /// <summary>
    /// Asks the engine to close an account, without the endpoint in front of it.
    /// </summary>
    /// <remarks>
    /// Two owners genuinely inside the operation at the same moment are arranged here rather than
    /// through two browsers, so that what is compared is the engine's own answer to each of them —
    /// the outcome the endpoint's answer is chosen from — rather than two status codes that
    /// something in front of the engine could have produced on its own.
    /// </remarks>
    private async Task<ClosureOutcome> DirectlyCloseAsync(Guid userId)
    {
        await using var work = stack.Services.CreateAsyncScope();

        return await work.ServiceProvider
            .GetRequiredService<IAccountClosure>()
            .CloseAsync(userId, Cancellation.Token)
            .ConfigureAwait(false);
    }

    private async Task<(DateTimeOffset? ClosedAt, Guid? ClosedBy)> StoredClosureAsync(Guid organizationId)
    {
        await using var work = stack.Services.CreateAsyncScope();

        var stored = await work.ServiceProvider
            .GetRequiredService<ControlPlaneDbContext>()
            .Organizations
            .AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => new { organization.ClosedAt, organization.ClosedByUserId })
            .SingleAsync(Cancellation.Token)
            .ConfigureAwait(false);

        return (stored.ClosedAt, stored.ClosedByUserId);
    }

    private static async Task<SessionResponse> SessionInAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var session = await response.Content
            .ReadFromJsonAsync<SessionResponse>(Cancellation.Token)
            .ConfigureAwait(false);

        session.Should().NotBeNull();

        return session;
    }

    private static async Task<string> NameOfAccountAsync(Browser browser)
    {
        using var response = await browser.GetAsync(Organization).ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var described = await response.Content
            .ReadFromJsonAsync<OrganizationResponse>(Cancellation.Token)
            .ConfigureAwait(false);

        described.Should().NotBeNull();

        return described.Name;
    }

    private static async Task<IReadOnlyList<SiteSummary>> SitesInAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var sites = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<SiteSummary>>(Cancellation.Token)
            .ConfigureAwait(false);

        return sites ?? [];
    }

    /// <summary>The secret from the most recent link sent to an address.</summary>
    private static string TokenSentTo(MailboxInstall install, string address)
    {
        var message = install.Mailbox.LastTo(address);

        message.Should().NotBeNull("an invitation has to reach the mailbox it was addressed to");

        return ResetLink.TokenIn(message);
    }

    /// <summary>Whether the collector would still resolve this website.</summary>
    private async Task<SiteSnapshot?> CollectorFindsAsync(Guid siteId)
    {
        await using var work = stack.Services.CreateAsyncScope();

        return await work.ServiceProvider
            .GetRequiredService<ISiteCatalog>()
            .FindAsync(siteId, Cancellation.Token)
            .ConfigureAwait(false);
    }

    private async Task<ulong> StoredCountAsync(Guid siteId) =>
        await TelemetryStore.ScalarAsync<ulong>(
                Telemetry,
                "SELECT count() FROM events WHERE site_id = {site_id:UUID}",
                TelemetryStore.Bind("site_id", siteId))
            .ConfigureAwait(false);

    private static Dictionary<string, object?> Report(Guid siteId, string url) =>
        new(StringComparer.Ordinal)
        {
            ["siteId"] = siteId,
            ["kind"] = "pageview",
            ["url"] = url,
            ["language"] = "en-GB",
            ["viewportWidth"] = 1440,
            ["viewportHeight"] = 900,
        };

    private static async Task<HttpResponseMessage> PostBeaconAsync(
        HttpClient client,
        Dictionary<string, object?> report,
        string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Collect)
        {
            Content = JsonContent.Create(report),
        };

        request.Headers.Add(HeaderNames.Origin, $"https://{origin}");

        return await client.SendAsync(request, Cancellation.Token).ConfigureAwait(false);
    }

    private static ServerCollectRequest Batch(string surface, params ServerObservation[] events) =>
        new() { Surface = surface, Events = events };

    private static ServerObservation Observation(string host, string path, short? status = 200) =>
        new()
        {
            Kind = "pageview",
            Url = $"https://{host}{path}",
            IpAddress = "198.51.100.42",
            UserAgent = Crawler,
            StatusCode = status,
        };

    private static async Task<HttpResponseMessage> PostBatchAsync(
        HttpClient client,
        string secret,
        ServerCollectRequest batch)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ServerCollect)
        {
            Content = JsonContent.Create(batch),
        };

        request.Headers.Add(HeaderNames.Authorization, $"Bearer {secret}");

        return await client.SendAsync(request, Cancellation.Token).ConfigureAwait(false);
    }

    private static async Task<ServerCollectResponse> CountedAsync(HttpResponseMessage answer)
    {
        var counted = await answer.Content
            .ReadFromJsonAsync<ServerCollectResponse>(Cancellation.Token)
            .ConfigureAwait(false);

        counted.Should().NotBeNull();

        return counted;
    }

    /// <summary>
    /// An account with one website and one owner, signed in on the host given.
    /// </summary>
    /// <remarks>
    /// Built for each test rather than shared. The suite runs against one stack, so two tests that
    /// shared an account would be one test closing the other's. The host is a parameter because
    /// the tests about what owners receive sign in on a copy of the product whose messages are
    /// kept, while every other test signs in on the stack itself.
    /// </remarks>
    private async Task<Account> AccountWithOwnerAsync(WebApplicationFactory<Program>? host = null)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain()).ConfigureAwait(false);
        var owner = await OwnerAddedAsync(site.OrganizationId).ConfigureAwait(false);
        var browser = await SignedInOnAsync(host ?? stack, owner.Address).ConfigureAwait(false);

        return new Account(site, owner.UserId, owner.Address, browser);
    }

    /// <summary>An account made an owner of an organisation, and not signed in anywhere.</summary>
    private async Task<Person> OwnerAddedAsync(Guid organizationId)
    {
        var address = SignedIn.Address();
        var (created, user) = await ControlPlaneSeed
            .AddAccountAsync(stack, address, Passwords.Acceptable)
            .ConfigureAwait(false);

        created.Succeeded.Should().BeTrue();

        await ControlPlaneSeed
            .GrantInOrganizationAsync(stack, organizationId, user.Id, OrganizationRole.Owner)
            .ConfigureAwait(false);

        return new Person(user.Id, address);
    }

    private async Task<Joined> JoinAsync(Guid organizationId, OrganizationRole role)
    {
        var address = SignedIn.Address();
        var (created, user) = await ControlPlaneSeed
            .AddAccountAsync(stack, address, Passwords.Acceptable)
            .ConfigureAwait(false);

        created.Succeeded.Should().BeTrue();

        await ControlPlaneSeed
            .GrantInOrganizationAsync(stack, organizationId, user.Id, role)
            .ConfigureAwait(false);

        var browser = await SignedInOnAsync(stack, address).ConfigureAwait(false);

        return new Joined(user.Id, browser);
    }

    private static async Task<Browser> SignedInOnAsync(WebApplicationFactory<Program> host, string address)
    {
        var browser = await Browser.OpenAsync(host).ConfigureAwait(false);

        using var response = await browser
            .PostAsync("/api/session", new SignInRequest { EmailAddress = address, Password = Passwords.Acceptable })
            .ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Re-read, because the token the engine issues is tied to whoever was signed in when it
        // was issued. The one this browser was opened with belongs to nobody.
        await browser.DescribeAsync().ConfigureAwait(false);

        return browser;
    }

    private sealed record Account(Site Site, Guid OwnerId, string OwnerAddress, Browser Owner) : IDisposable
    {
        public void Dispose() => Owner.Dispose();
    }

    private sealed record Person(Guid UserId, string Address);

    private sealed record Joined(Guid UserId, Browser Browser) : IDisposable
    {
        public void Dispose() => Browser.Dispose();
    }

    /// <summary>
    /// The codes closing and restoring refuse with, and the one a removal names.
    /// </summary>
    /// <remarks>
    /// Written out here rather than read from the endpoints, which are internal to the host. A
    /// test that read them from the same constant it is checking would pass whatever they were
    /// changed to, and these are what the dashboard looks its own sentences up by.
    /// </remarks>
    private static class ClosureCodes
    {
        public const string AlreadyClosed = "AccountAlreadyClosed";
        public const string NotClosed = "AccountNotClosed";
        public const string SiteIsOnlyOne = "SiteIsOnlyOne";
    }
}
