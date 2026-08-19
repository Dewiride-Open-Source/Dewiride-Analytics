using System.Net;
using System.Net.Http.Json;
using ClickHouse.Driver;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Infrastructure.Persistence;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Dashboard;

/// <summary>
/// Proves who may remove a website, and that removing one takes everything measured for it.
/// </summary>
/// <remarks>
/// <para>
/// This is the one action in the product that destroys data, and the promise made on the screen
/// before it runs is unqualified: what was measured is deleted and cannot be brought back. A
/// removal that emptied the control plane and left the telemetry behind would break that promise
/// in the worst possible direction — the rows would still be on the disk, and unreachable for
/// ever, because every read is scoped through a website that no longer exists.
/// </para>
/// <para>
/// The identifier in the path is not a secret; it is printed in the tracking snippet on every page
/// the website measures. What stands between somebody and another person's website is the owner's
/// role and the proof-of-origin pair, and both are pinned here.
/// </para>
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class RemoveSiteTests(AnalyticsStackFixture stack)
{
    [Fact]
    public async Task An_Owner_Can_Remove_A_Website()
    {
        var (going, _, browser) = await TwoWebsitesOwnedByOnePersonAsync();

        using (browser)
        {
            var response = await browser.DeleteAsync(Site(going.Id));

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
    }

    /// <summary>
    /// The list at the top of the screen is where a removal is seen to have happened, and a
    /// website still named there is a website somebody would try to open.
    /// </summary>
    [Fact]
    public async Task A_Removed_Website_Is_Gone_From_The_List()
    {
        var (going, staying, browser) = await TwoWebsitesOwnedByOnePersonAsync();

        using (browser)
        {
            await RemoveAsync(browser, going.Id);

            var listed = await ListAsync(browser);

            listed.Select(one => one.Id).Should().Equal(staying.Id);
        }
    }

    /// <summary>
    /// A grant, a key or a bookmark that outlived the website it names would be a row nothing can
    /// evaluate: the grant authorises nothing, the key reports for nowhere, and the bookmark marks
    /// a place in a history that has been deleted. They go by cascade rather than by four
    /// deletions here, and this is what proves the cascade is actually declared.
    /// </summary>
    [Fact]
    public async Task Everything_The_Control_Plane_Held_About_A_Website_Goes_With_It()
    {
        var (going, _, browser) = await TwoWebsitesOwnedByOnePersonAsync();

        using (browser)
        {
            await ControlPlaneSeed.AddServerKeyAsync(stack, going.Id);
            await BookmarkAsync(going);

            (await MembershipCountAsync(going.Id)).Should().BeGreaterThan(0);
            (await IngestKeyCountAsync(going.Id)).Should().BeGreaterThan(0);
            (await BookmarkCountAsync(going.Id)).Should().BeGreaterThan(0);

            await RemoveAsync(browser, going.Id);

            (await MembershipCountAsync(going.Id)).Should().Be(0);
            (await IngestKeyCountAsync(going.Id)).Should().Be(0);
            (await BookmarkCountAsync(going.Id)).Should().Be(0);
        }
    }

    /// <summary>
    /// The most important property in this file. Removing a website empties the telemetry store of
    /// everything belonging to it — the activity and the verdicts reached about it alike — and
    /// touches nothing belonging to any other website. Both tables are partitioned by month and
    /// sorted by website, so there is no partition to drop and the deletion is by predicate; a
    /// predicate that named the wrong column, or a table that was left off the list, would look
    /// exactly like success from every other test in the suite.
    /// </summary>
    [Fact]
    public async Task Removing_A_Website_Deletes_Everything_Measured_For_It()
    {
        var (going, staying, browser) = await TwoWebsitesOwnedByOnePersonAsync();
        var at = Now.AddDays(-1);

        using (browser)
        {
            await WriteAsync(
                Page(going.Id, "reader", at, "/"),
                Page(going.Id, "reader", at.AddMinutes(2), "/posts/hello"),
                Page(staying.Id, "neighbour", at, "/"),
                Page(staying.Id, "neighbour", at.AddMinutes(2), "/posts/hello"));

            await JudgeAsync(going);
            await JudgeAsync(staying);

            (await ActivityCountAsync(going.Id)).Should().Be(2);
            (await VerdictCountAsync(going.Id)).Should().Be(1);
            (await ActivityCountAsync(staying.Id)).Should().Be(2);
            (await VerdictCountAsync(staying.Id)).Should().Be(1);

            await RemoveAsync(browser, going.Id);

            // Read straight back. The statement waits until the rows have stopped answering
            // queries, which is what lets the answer to the request mean the telemetry is gone
            // rather than that the deletion has been accepted for later.
            (await ActivityCountAsync(going.Id)).Should().Be(0);
            (await VerdictCountAsync(going.Id)).Should().Be(0);

            (await ActivityCountAsync(staying.Id)).Should().Be(2);
            (await VerdictCountAsync(staying.Id)).Should().Be(1);
        }
    }

    /// <summary>
    /// The collector resolves a website out of a cache on every report, so until that entry is
    /// thrown away it goes on accepting reports for a website nothing can read — writing rows that
    /// the removal was supposed to be the end of.
    /// </summary>
    [Fact]
    public async Task The_Collector_Stops_Accepting_Reports_For_A_Removed_Website()
    {
        var (going, _, browser) = await TwoWebsitesOwnedByOnePersonAsync();

        using (browser)
        {
            // Resolved first, so the collector is holding the website when the removal lands.
            (await CollectorFindsAsync(going.Id)).Should().NotBeNull();

            await RemoveAsync(browser, going.Id);

            (await CollectorFindsAsync(going.Id)).Should().BeNull();
        }
    }

    /// <summary>
    /// Deleting everything a website ever measured is the owner's decision alone. Somebody who may
    /// change how it is measured may not end it.
    /// </summary>
    [Theory]
    [InlineData(SiteRole.Viewer)]
    [InlineData(SiteRole.Editor)]
    public async Task Somebody_Who_Does_Not_Own_A_Website_Cannot_Remove_It(SiteRole role)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedIn.AsAsync(stack, role, site.Id);

        using (browser)
        {
            var response = await browser.DeleteAsync(Site(site.Id));

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await ListAsync(browser)).Select(one => one.Id).Should().Equal(site.Id);
        }
    }

    /// <summary>
    /// A new website joins the organisation of one the person already owns, so somebody who gave
    /// up the last website they owned could never begin again. Their last one is kept, and the
    /// refusal names itself so the dashboard can say what to do instead.
    /// </summary>
    [Fact]
    public async Task The_Only_Website_Somebody_Owns_Is_Kept()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedIn.AsAsync(stack, SiteRole.Owner, site.Id);

        using (browser)
        {
            var response = await browser.DeleteAsync(Site(site.Id));

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await Refusal.ReasonsOfAsync(response)).Should().Contain("SiteIsOnlyOne");
            (await ListAsync(browser)).Select(one => one.Id).Should().Equal(site.Id);
        }
    }

    /// <summary>
    /// A website that does not exist and a website the caller has no role on answer identically,
    /// so this cannot be used to find out which identifiers on an install are real.
    /// </summary>
    [Fact]
    public async Task Somebody_Elses_Website_Answers_As_Though_It_Were_Not_There()
    {
        var (_, _, browser) = await TwoWebsitesOwnedByOnePersonAsync();
        var theirs = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());

        using (browser)
        {
            var existing = await browser.DeleteAsync(Site(theirs.Id));
            var invented = await browser.DeleteAsync(Site(Guid.NewGuid()));

            existing.StatusCode.Should().Be(HttpStatusCode.NotFound);
            invented.StatusCode.Should().Be(existing.StatusCode);
            (await StoredCountAsync(theirs.Id)).Should().Be(1);
        }
    }

    [Fact]
    public async Task Nobody_Signed_In_Cannot_Remove_A_Website()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        using var browser = await Browser.OpenAsync(stack);

        var response = await browser.DeleteAsync(Site(site.Id));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await StoredCountAsync(site.Id)).Should().Be(1);
    }

    /// <summary>
    /// A cookie the browser returns on its own is not proof that this page meant to send the
    /// request, and this is the request where that matters most: another site could otherwise
    /// cause a signed-in owner's browser to destroy everything one of their websites measured.
    /// </summary>
    [Fact]
    public async Task Removing_Without_Proof_Of_Where_It_Came_From_Is_Refused()
    {
        var (going, _, browser) = await TwoWebsitesOwnedByOnePersonAsync();

        using (browser)
        {
            var response = await browser.DeleteWithoutProofAsync(Site(going.Id));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await StoredCountAsync(going.Id)).Should().Be(1);
            (await ListAsync(browser)).Select(one => one.Id).Should().Contain(going.Id);
        }
    }

    /// <summary>
    /// The refusal that keeps somebody's last website is about the website they named, not about
    /// how many they happen to own. Answering it for a website they have no role on would tell
    /// them a site they cannot see is theirs, and would leave the outcome saying something untrue
    /// about the row it names.
    /// </summary>
    [Fact]
    public async Task A_Website_Somebody_Has_No_Role_On_Is_Not_Reported_As_Their_Last_One()
    {
        var mine = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var theirs = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var address = SignedIn.Address();
        var (created, account) = await ControlPlaneSeed.AddAccountAsync(stack, address, Passwords.Acceptable);

        created.Succeeded.Should().BeTrue();
        await ControlPlaneSeed.GrantAsync(stack, mine.Id, account.Id, SiteRole.Owner);

        var removal = await DirectlyRemoveAsync(account.Id, theirs.Id);

        removal.Outcome.Should().Be(SiteRemovalOutcome.NoSuchSite);
        (await StoredCountAsync(theirs.Id)).Should().Be(1);
    }

    /// <summary>
    /// The last-website rule is a count read and then acted on, so it only holds if the two halves
    /// cannot be split apart. Two removals arriving together against the two websites somebody owns
    /// would otherwise both read two, both pass the guard, and both delete — leaving an account
    /// owning none, which is the one state here with no way out: a new website joins an
    /// organisation the person already owns one in, so owning none means never being able to add
    /// one, and first-run claiming is spent.
    /// </summary>
    [Fact]
    public async Task Two_Removals_At_Once_Cannot_Take_Somebody_Down_To_None()
    {
        var first = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var second = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var (created, account) = await ControlPlaneSeed
            .AddAccountAsync(stack, SignedIn.Address(), Passwords.Acceptable);

        created.Succeeded.Should().BeTrue();
        await ControlPlaneSeed.GrantAsync(stack, first.Id, account.Id, SiteRole.Owner);
        await ControlPlaneSeed.GrantAsync(stack, second.Id, account.Id, SiteRole.Owner);

        // Started on separate threads rather than one after the other, so both are genuinely inside
        // the operation at the same moment and the guard is asked the question it exists for.
        var outcomes = await Task.WhenAll(
            Task.Run(() => DirectlyRemoveAsync(account.Id, first.Id)),
            Task.Run(() => DirectlyRemoveAsync(account.Id, second.Id)));

        outcomes.Select(one => one.Outcome)
            .Should()
            .BeEquivalentTo([SiteRemovalOutcome.Removed, SiteRemovalOutcome.OnlyOne]);

        (await StoredCountAsync(first.Id) + await StoredCountAsync(second.Id)).Should().Be(1);
    }

    private const string Sites = "/api/sites";

    private static string Site(Guid siteId) => $"{Sites}/{siteId}";

    /// <summary>The present moment, taken from the host's own clock rather than the machine's.</summary>
    private DateTimeOffset Now => stack.Services.GetRequiredService<TimeProvider>().GetUtcNow();

    private IClickHouseClient Telemetry => stack.Services.GetRequiredService<IClickHouseClient>();

    /// <summary>
    /// Two websites owned by one person, which is the smallest arrangement in which either of them
    /// can be removed at all.
    /// </summary>
    /// <returns>The one to remove, the one to keep, and the browser they are signed in on.</returns>
    private async Task<(Site Going, Site Staying, Browser Browser)> TwoWebsitesOwnedByOnePersonAsync()
    {
        var going = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var staying = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedIn.AsAsync(stack, SiteRole.Owner, going.Id, staying.Id);

        return (going, staying, browser);
    }

    private static async Task RemoveAsync(Browser browser, Guid siteId)
    {
        var response = await browser.DeleteAsync(Site(siteId));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// Asks the directory to remove a website, without the endpoint in front of it.
    /// </summary>
    /// <remarks>
    /// The endpoint settles the caller's role before it asks, so what the directory answers on its
    /// own is only reachable from here — and it is the answer the dashboard's sentence is chosen
    /// from, so it has to be right on its own terms rather than only in company.
    /// </remarks>
    /// <param name="userId">The person removing it.</param>
    /// <param name="siteId">The website they named.</param>
    /// <returns>What came of it.</returns>
    private async Task<SiteRemoval> DirectlyRemoveAsync(Guid userId, Guid siteId)
    {
        await using var work = stack.Services.CreateAsyncScope();

        return await work.ServiceProvider
            .GetRequiredService<ISiteDirectory>()
            .RemoveAsync(userId, siteId, Cancellation.Token);
    }

    private static async Task<IReadOnlyList<SiteSummary>> ListAsync(Browser browser)
    {
        var response = await browser.GetAsync(Sites);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var listed = await response.Content.ReadFromJsonAsync<IReadOnlyList<SiteSummary>>(Cancellation.Token);

        listed.Should().NotBeNull();

        return listed;
    }

    /// <summary>Whether the collector would still resolve this website.</summary>
    private async Task<SiteSnapshot?> CollectorFindsAsync(Guid siteId)
    {
        await using var work = stack.Services.CreateAsyncScope();

        return await work.ServiceProvider
            .GetRequiredService<ISiteCatalog>()
            .FindAsync(siteId, Cancellation.Token);
    }

    private Task<int> StoredCountAsync(Guid siteId) =>
        CountAsync(database => database.Sites.CountAsync(site => site.Id == siteId, Cancellation.Token));

    private Task<int> MembershipCountAsync(Guid siteId) =>
        CountAsync(database => database.SiteMemberships
            .CountAsync(membership => membership.SiteId == siteId, Cancellation.Token));

    private Task<int> IngestKeyCountAsync(Guid siteId) =>
        CountAsync(database => database.SiteIngestKeys
            .CountAsync(key => key.SiteId == siteId, Cancellation.Token));

    private Task<int> BookmarkCountAsync(Guid siteId) =>
        CountAsync(database => database.ClassificationProgress
            .CountAsync(progress => progress.SiteId == siteId, Cancellation.Token));

    private async Task<int> CountAsync(Func<ControlPlaneDbContext, Task<int>> rows)
    {
        await using var work = stack.Services.CreateAsyncScope();

        return await rows(work.ServiceProvider.GetRequiredService<ControlPlaneDbContext>());
    }

    private async Task<ulong> ActivityCountAsync(Guid siteId) =>
        await TelemetryStore.ScalarAsync<ulong>(
            Telemetry,
            "SELECT count() FROM events WHERE site_id = {site_id:UUID}",
            TelemetryStore.Bind("site_id", siteId));

    private async Task<ulong> VerdictCountAsync(Guid siteId) =>
        await TelemetryStore.ScalarAsync<ulong>(
            Telemetry,
            "SELECT count() FROM session_classifications WHERE site_id = {site_id:UUID}",
            TelemetryStore.Bind("site_id", siteId));

    private Task WriteAsync(params RawEvent[] events) =>
        stack.Services.GetRequiredService<IEventSink>().WriteBatchAsync(events, Cancellation.Token);

    /// <summary>
    /// Runs the engine over a website, so that there are verdicts to be deleted as well as
    /// activity.
    /// </summary>
    /// <param name="site">The website to judge.</param>
    /// <returns>A task that completes once it has been judged.</returns>
    private async Task JudgeAsync(Site site)
    {
        await using var work = stack.Services.CreateAsyncScope();

        await work.ServiceProvider
            .GetRequiredService<SessionClassifier>()
            .CatchUpAsync(site.Id, site.CreatedAt.AddDays(-2), Cancellation.Token);
    }

    /// <summary>Starts the bookmark the engine keeps for a website.</summary>
    /// <param name="site">The website.</param>
    /// <returns>A task that completes once the bookmark exists.</returns>
    private async Task BookmarkAsync(Site site)
    {
        await using var work = stack.Services.CreateAsyncScope();

        await work.ServiceProvider
            .GetRequiredService<IClassificationProgressStore>()
            .ResumeFromAsync(site.Id, RulesetVersion.Current, site.CreatedAt, Cancellation.Token);
    }

    private static RawEvent Page(Guid siteId, string visitor, DateTimeOffset at, string path) => new()
    {
        EventId = Guid.CreateVersion7(at),
        SiteId = siteId,
        Kind = EventKind.PageView,
        Surface = IngestSurface.BrowserTracker,
        ServerTimestamp = at,
        VisitorKey = visitor,
        Host = "example.com",
        Path = path,
        UserAgent = Chrome,
    };

    private const string Chrome =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/141.0.0.0 Safari/537.36";

    private static string Domain() => $"remove-{Guid.NewGuid():n}.example";
}
