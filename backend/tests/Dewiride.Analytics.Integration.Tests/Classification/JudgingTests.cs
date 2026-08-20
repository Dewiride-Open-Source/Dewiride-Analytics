using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Infrastructure.Persistence;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Classification;

/// <summary>
/// Proves the whole path: activity is collected, grouped into visits, judged, stored and read back.
/// </summary>
/// <remarks>
/// The engine's own suite proves what it concludes from a hand-written visit. This one proves that
/// the visit it is handed is the one that actually happened — which is where the grouping, the
/// three-state readings and the round trip through two stores can each go wrong without any of the
/// other suites noticing.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class JudgingTests(AnalyticsStackFixture stack)
{
    private const string Chrome =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/141.0.0.0 Safari/537.36";

    private const string Scanner = "python-requests/2.32.3";

    /// <summary>
    /// Alibaba's international network, which is where a live installation's hundred phantom
    /// readers turned out to be sitting.
    /// </summary>
    private const uint RentedNetwork = 45102;

    /// <summary>An Indian mobile carrier, and a network nobody rents a server on.</summary>
    private const uint HouseholdNetwork = 55836;

    /// <summary>The present moment, taken from the host's own clock rather than the machine's.</summary>
    private DateTimeOffset Now => stack.Services.GetRequiredService<TimeProvider>().GetUtcNow();

    /// <summary>
    /// Far enough in the past that every visit written here has been silent for longer than the
    /// idle timeout, so a run judges them rather than waiting for them to finish.
    /// </summary>
    private DateTimeOffset Yesterday => Now.AddDays(-1);

    [Fact]
    public async Task Somebody_Reading_Is_Not_Called_Automation()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Page(site.Id, "reader", at, "/", Chrome),
            Page(site.Id, "reader", at.AddMinutes(2), "/posts/hello", Chrome),
            Read(site.Id, "reader", at.AddMinutes(4), Chrome));

        await JudgeAsync(site);

        var visits = await VisitsAsync(site);

        visits.Should().ContainSingle();
        visits[0].Verdict.Category.Should().Be(TrafficCategory.LikelyHuman);
        visits[0].PageCount.Should().Be(2);
    }

    /// <summary>
    /// The case a live installation got wrong: a real browser reading a real page from a rented
    /// server. Everything the engine watches says person; only where it came from says otherwise,
    /// and that has to survive the whole round trip through both stores to reach the verdict.
    /// </summary>
    [Fact]
    public async Task Somebody_Reading_From_A_Rented_Server_Is_Not_Called_A_Person()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Rented(Page(site.Id, "scraper", at, "/", Chrome)),
            Rented(Page(site.Id, "scraper", at.AddMinutes(2), "/posts/hello", Chrome)),
            Rented(Read(site.Id, "scraper", at.AddMinutes(4), Chrome)));

        await JudgeAsync(site);

        var visits = await VisitsAsync(site);

        visits.Should().ContainSingle();
        visits[0].Verdict.Category.Should().NotBe(TrafficCategory.LikelyHuman);
        visits[0].Verdict.Supporting.Should().Contain(signal => signal.Code == SignalCodes.HostingNetwork);
    }

    /// <summary>
    /// The reading happened and is not thrown away for being inconvenient — it is carried through
    /// both stores and shown as the case against the verdict.
    /// </summary>
    [Fact]
    public async Task What_A_Rented_Server_Did_Is_Kept_As_Evidence_Against_The_Verdict()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Rented(Page(site.Id, "scraper", at, "/", Chrome)),
            Rented(Read(site.Id, "scraper", at.AddMinutes(4), Chrome)));

        await JudgeAsync(site);

        var visits = await VisitsAsync(site);

        visits[0].Verdict.Contradicting.Should().Contain(signal => signal.Code == SignalCodes.ReadTime);
        visits[0].Verdict.Strength.Should().NotBe(EvidenceStrength.Verified);
    }

    /// <summary>
    /// The shape a live installation was actually in: one program reading a site through a pool of
    /// rented addresses, every one of its reports arriving from a different one.
    /// </summary>
    /// <remarks>
    /// Counted by address it became a hundred visitors, and each report about a page landed under
    /// a different one of them — so the reading of a page sat in one visit and the page itself in
    /// another, and the visit holding the reading was told there was too little to go on. The keys
    /// here are derived by the running install's own factory rather than written by hand, because
    /// what is being proved is the derivation.
    /// </remarks>
    [Fact]
    public async Task A_Pool_Of_Rented_Addresses_Is_One_Visit_Rather_Than_A_Page_And_A_Reading()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        var arrived = RentedVisitor(site.Id, "47.238.1.1");
        var left = RentedVisitor(site.Id, "8.219.64.13");

        await WriteAsync(
            Rented(Page(site.Id, arrived, at, "/posts/hello", Chrome)),
            Rented(Read(site.Id, left, at.AddMinutes(4), Chrome, "/posts/hello")));

        await JudgeAsync(site);

        var visits = await VisitsAsync(site);

        visits.Should().ContainSingle();
        visits[0].PageCount.Should().Be(1);
        visits[0].Verdict.Category.Should().NotBe(TrafficCategory.InsufficientEvidence);
    }

    /// <summary>
    /// The same two addresses on a network that carries households are two visitors, because there
    /// an address is a home rather than a lease.
    /// </summary>
    [Fact]
    public async Task Two_Ordinary_Addresses_Are_Still_Two_Visitors()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        var one = HouseholdVisitor(site.Id, "203.0.113.7");
        var another = HouseholdVisitor(site.Id, "203.0.113.8");

        await WriteAsync(
            Page(site.Id, one, at, "/posts/hello", Chrome),
            Page(site.Id, another, at, "/posts/hello", Chrome));

        await JudgeAsync(site);

        var visits = await VisitsAsync(site);

        visits.Should().HaveCount(2);
    }

    /// <summary>
    /// The report announcing an arrival is sent first, on load, and is the one most easily lost.
    /// What follows it names the page it was measured on, and a tracker only measures a page from
    /// the page itself — so the visit read what its reports say it read, and is judged on that
    /// rather than answered with "too little to go on".
    /// </summary>
    [Fact]
    public async Task A_Reader_Whose_Arrival_Was_Never_Reported_Is_Still_Judged()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());

        await WriteAsync(Read(site.Id, "reader", Yesterday, Chrome, "/posts/hello"));

        await JudgeAsync(site);

        var visits = await VisitsAsync(site);

        visits.Should().ContainSingle();
        visits[0].PageCount.Should().Be(1);
        visits[0].Verdict.Category.Should().NotBe(TrafficCategory.InsufficientEvidence);
    }

    /// <summary>
    /// A tracker restates how long a page has held somebody every time it reports, so each report
    /// contains the last one with more on the end. Adding them together would credit a quarter of
    /// an hour's reading with an afternoon of it, and every excess minute points toward a person.
    /// </summary>
    [Fact]
    public async Task Reading_Time_Is_What_The_Page_Held_Somebody_Rather_Than_What_Was_Reported()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Page(site.Id, "reader", at, "/posts/hello", Chrome),
            Read(site.Id, "reader", at.AddSeconds(15), Chrome, "/posts/hello") with { EngagedMs = 15_000 },
            Read(site.Id, "reader", at.AddSeconds(45), Chrome, "/posts/hello") with { EngagedMs = 45_000 },
            Read(site.Id, "reader", at.AddSeconds(90), Chrome, "/posts/hello") with { EngagedMs = 90_000 });

        var found = await ReadSessionsAsync(site.Id);

        found.Should().ContainSingle();
        found[0].Evidence.PageCount.Should().Be(1);
        found[0].Evidence.EngagedMs.Should().Be(90_000);
    }

    /// <summary>
    /// A reader who comes back to an article later in the same visit was there twice, and each
    /// arrival holds its own reading.
    /// </summary>
    [Fact]
    public async Task Returning_To_A_Page_Is_A_Second_Arrival_With_Its_Own_Reading()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Page(site.Id, "reader", at, "/posts/hello", Chrome),
            Read(site.Id, "reader", at.AddSeconds(20), Chrome, "/posts/hello") with { EngagedMs = 20_000 },
            Page(site.Id, "reader", at.AddMinutes(1), "/pricing", Chrome),
            Page(site.Id, "reader", at.AddMinutes(2), "/posts/hello", Chrome),
            Read(site.Id, "reader", at.AddMinutes(2).AddSeconds(30), Chrome, "/posts/hello") with { EngagedMs = 30_000 });

        var found = await ReadSessionsAsync(site.Id);

        found.Should().ContainSingle();
        found[0].Evidence.PageCount.Should().Be(3);
        found[0].Evidence.EngagedMs.Should().Be(50_000);
    }

    /// <summary>
    /// A household network is not in the catalogue and produces nothing, so the same visit from a
    /// person's own connection is still a person.
    /// </summary>
    [Fact]
    public async Task The_Same_Visit_From_A_Household_Network_Is_Still_A_Person()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Page(site.Id, "reader", at, "/", Chrome) with { AutonomousSystem = HouseholdNetwork },
            Read(site.Id, "reader", at.AddMinutes(4), Chrome) with { AutonomousSystem = HouseholdNetwork });

        await JudgeAsync(site);

        var visits = await VisitsAsync(site);

        visits[0].Verdict.Category.Should().Be(TrafficCategory.LikelyHuman);
    }

    /// <summary>
    /// The clearest signal in the engine, and one only a surface in the request path can see. It
    /// has to survive being written to one store, grouped, judged and written to another.
    /// </summary>
    [Fact]
    public async Task A_Sweep_For_A_Way_In_Is_Recognised_From_What_It_Asked_For()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Probe(site.Id, "intruder", at, "/.env"),
            Probe(site.Id, "intruder", at.AddSeconds(1), "/wp-admin/setup-config.php"),
            Probe(site.Id, "intruder", at.AddSeconds(2), "/.git/config"));

        await JudgeAsync(site);

        var visits = await VisitsAsync(site);

        visits.Should().ContainSingle();
        visits[0].Verdict.Category.Should().Be(TrafficCategory.SecurityScanner);
        visits[0].Verdict.Supporting.Should().Contain(signal => signal.Code == SignalCodes.SensitivePaths);
    }

    /// <summary>
    /// Judging a visit twice must leave one verdict. That is what lets a run be interrupted, and
    /// what lets two instances of the engine work the same site without coordinating.
    /// </summary>
    [Fact]
    public async Task Judging_The_Same_Visit_Again_Leaves_One_Verdict()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(Page(site.Id, "returning", at, "/", Chrome));

        await JudgeAsync(site);
        await ResetBookmarkAsync(site.Id);
        await JudgeAsync(site);

        var breakdown = await BreakdownAsync(site);

        breakdown.Sum(group => group.Sessions).Should().Be(1);
    }

    /// <summary>
    /// The engine's bookmark stops at the earliest visit still under way rather than at the end of
    /// the stretch it just read, so a later window routinely opens part-way through a visit that
    /// has already been judged. What is left of that visit is not a visit: handing it back as one
    /// wrote the same reader down twice, the second time with too little left in them to say
    /// anything at all.
    /// </summary>
    [Fact]
    public async Task What_Is_Left_Of_A_Visit_Already_Under_Way_Is_Not_A_Second_Visit()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Page(site.Id, "straddling", at, "/", Chrome),
            Read(site.Id, "straddling", at.AddMinutes(6), Chrome),
            Read(site.Id, "straddling", at.AddMinutes(12), Chrome));

        var whole = await ReadSessionsAsync(site.Id, at.AddMinutes(-1));
        var remainder = await ReadSessionsAsync(site.Id, at.AddMinutes(3));

        whole.Should().ContainSingle()
            .Which.Evidence.StartedAt.Should().BeCloseTo(at, TimeSpan.FromSeconds(1));

        remainder.Should().BeEmpty();
    }

    /// <summary>
    /// The same thing end to end: a run that resumes from the middle of a visit it has already
    /// judged leaves the one verdict it reached the first time.
    /// </summary>
    [Fact]
    public async Task Resuming_Inside_A_Judged_Visit_Does_Not_Judge_It_Again()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Page(site.Id, "resumed", at, "/", Chrome),
            Read(site.Id, "resumed", at.AddMinutes(6), Chrome),
            Read(site.Id, "resumed", at.AddMinutes(12), Chrome));

        await JudgeAsync(site);

        await ResetBookmarkAsync(site.Id);
        await ResumePointAsync(site.Id, at.AddMinutes(3));
        await JudgeAsync(site);

        var visits = await VisitsAsync(site);

        visits.Should().ContainSingle().Which.PageCount.Should().Be(1);
    }

    /// <summary>
    /// A visitor who falls silent for longer than the idle timeout and comes back has made two
    /// visits, and each is judged on its own.
    /// </summary>
    [Fact]
    public async Task A_Long_Silence_Ends_One_Visit_And_Starts_Another()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Page(site.Id, "twice", at, "/", Chrome),
            Page(site.Id, "twice", at.AddHours(3), "/posts/later", Chrome));

        await JudgeAsync(site);

        var visits = await VisitsAsync(site);

        visits.Should().HaveCount(2);
        visits.Select(visit => visit.PageCount).Should().AllSatisfy(count => count.Should().Be(1));
    }

    /// <summary>
    /// A pause shorter than the idle timeout is somebody still reading, not somebody who left.
    /// </summary>
    [Fact]
    public async Task A_Short_Pause_Does_Not_Split_A_Visit()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Page(site.Id, "paused", at, "/", Chrome),
            Page(site.Id, "paused", at.AddMinutes(20), "/posts/second", Chrome));

        await JudgeAsync(site);

        var visits = await VisitsAsync(site);

        visits.Should().ContainSingle().Which.PageCount.Should().Be(2);
    }

    /// <summary>
    /// The difference between "nobody touched anything" and "nothing was watching" has to survive
    /// the round trip, because reading the second as the first is how every visit measured only by
    /// a server would be called automation.
    /// </summary>
    [Fact]
    public async Task A_Visit_Nobody_Could_Watch_Is_Not_Recorded_As_A_Visit_Nobody_Touched()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(Probe(site.Id, "unwatched", at, "/posts/hello") with { StatusCode = 200 });

        var found = await ReadSessionsAsync(site.Id);

        found.Should().ContainSingle();
        found[0].Evidence.HadPointerInteraction.Should().BeNull();
        found[0].Evidence.HadKeyboardInteraction.Should().BeNull();
        found[0].Evidence.DeclaredWebDriver.Should().BeNull();
    }

    [Fact]
    public async Task What_A_Browser_Reported_Survives_The_Round_Trip()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Page(site.Id, "measured", at, "/", Chrome),
            Read(site.Id, "measured", at.AddMinutes(1), Chrome));

        var found = await ReadSessionsAsync(site.Id);

        found.Should().ContainSingle();
        found[0].Evidence.EngagedMs.Should().Be(30_000);
        found[0].Evidence.MaxScrollDepthPercent.Should().Be(70);
        found[0].Evidence.HadPointerInteraction.Should().BeTrue();
        found[0].Evidence.HadKeyboardInteraction.Should().BeFalse();
        found[0].Evidence.ViewportWidth.Should().Be(1440);
        found[0].Evidence.Language.Should().Be("en-GB");
        found[0].Evidence.Surfaces.Should().Contain(IngestSurface.BrowserTracker);
    }

    /// <summary>
    /// A visit still in progress gets no verdict, because one reached half-way through would be
    /// replaced within the hour and would have been wrong in the meantime.
    /// </summary>
    [Fact]
    public async Task A_Visit_That_Has_Only_Just_Happened_Is_Left_Alone()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());

        await WriteAsync(Page(site.Id, "current", Now, "/", Chrome));

        var outcome = await JudgeAsync(site);

        outcome.Judged.Should().Be(0);
        (await VisitsAsync(site)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_Verdict_Reads_Back_With_The_Evidence_It_Was_Reached_On()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Probe(site.Id, "sweeper", at, "/.env"),
            Probe(site.Id, "sweeper", at.AddSeconds(1), "/.git/config"),
            Probe(site.Id, "sweeper", at.AddSeconds(2), "/wp-login.php"));

        await JudgeAsync(site);

        var visits = await VisitsAsync(site);
        var reasons = visits[0].Verdict.Supporting;

        visits[0].Verdict.RulesetVersion.Should().Be(RulesetVersion.Current);
        visits[0].Surfaces.Should().Contain(IngestSurface.CloudflareWorker);
        reasons.Should().NotBeEmpty();
        reasons.Should().OnlyContain(signal => !string.IsNullOrWhiteSpace(signal.Code));
        reasons.Single(signal => signal.Code == SignalCodes.SensitivePaths)
            .Parameters["attemptCount"].Should().Be("3");
    }

    /// <summary>
    /// Nothing a visitor wrote reaches a stored verdict. The paths that decided this one were
    /// written by whoever was probing, and they stay in the activity they came from.
    /// </summary>
    [Fact]
    public async Task Nothing_The_Visitor_Wrote_Travels_With_The_Verdict()
    {
        const string written = "cGFzc2VkLXRocm91Z2g";

        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Probe(site.Id, "hostile", at, $"/.env?{written}"),
            Probe(site.Id, "hostile", at.AddSeconds(1), $"/.git/config?{written}"),
            Probe(site.Id, "hostile", at.AddSeconds(2), $"/wp-login.php?{written}"));

        await JudgeAsync(site);

        var visits = await VisitsAsync(site);
        var verdict = visits[0].Verdict;

        verdict.Supporting.Should().NotBeEmpty();
        verdict.Supporting.Concat(verdict.Contradicting)
            .SelectMany(signal => signal.Parameters.Values)
            .Should().OnlyContain(value => !value.Contains(written, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_Breakdown_Counts_Visits_And_The_Pages_They_Took()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Probe(site.Id, "sweep-a", at, "/.env"),
            Probe(site.Id, "sweep-a", at.AddSeconds(1), "/.git/config"),
            Probe(site.Id, "sweep-a", at.AddSeconds(2), "/wp-login.php"),
            Probe(site.Id, "sweep-b", at, "/.env"),
            Probe(site.Id, "sweep-b", at.AddSeconds(1), "/.git/config"),
            Probe(site.Id, "sweep-b", at.AddSeconds(2), "/wp-login.php"));

        await JudgeAsync(site);

        var scanners = (await BreakdownAsync(site))
            .Single(group => group.Category == TrafficCategory.SecurityScanner);

        scanners.Sessions.Should().Be(2);
        scanners.PageViews.Should().Be(6);
    }

    [Fact]
    public async Task A_Verdict_Belongs_To_The_Site_It_Was_Reached_For()
    {
        var mine = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var theirs = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Page(mine.Id, "mine", at, "/", Chrome),
            Page(theirs.Id, "theirs", at, "/", Chrome));

        await JudgeAsync(mine);
        await JudgeAsync(theirs);

        (await VisitsAsync(mine)).Should().ContainSingle()
            .Which.SessionKey.Should().StartWith("mine");
    }

    /// <summary>
    /// Two instances of the engine starting on the same site at the same moment is the ordinary
    /// case for anybody running more than one. Neither may fail, and both have to work from the
    /// same starting point — which they do, because both derive it from when the site was added.
    /// </summary>
    [Fact]
    public async Task Several_Engines_Starting_Together_Agree_On_Where_To_Begin()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var from = site.CreatedAt.AddDays(-2);

        var starts = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => ResumePointAsync(site.Id, from)));

        // The control plane keeps an instant to the microsecond, so whichever engine wrote the row
        // reads its own value back one truncation coarser than the engines that lost the race. A
        // starting point is a window boundary measured in minutes; agreeing to the precision the
        // store keeps is the guarantee, and the whole of it.
        starts.Should().AllSatisfy(start => start.Should().BeCloseTo(from, TimeSpan.FromMicroseconds(1)));
    }

    private async Task<DateTimeOffset> ResumePointAsync(Guid siteId, DateTimeOffset from)
    {
        await using var scope = stack.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<IClassificationProgressStore>()
            .ResumeFromAsync(siteId, RulesetVersion.Current, from, Cancellation.Token)
            .ConfigureAwait(false);
    }

    private static string Domain() => $"{Guid.NewGuid():n}.example.com";

    /// <summary>
    /// Who the install itself says a request from a rented address is, using the same factory the
    /// collector uses.
    /// </summary>
    /// <param name="siteId">The site being visited.</param>
    /// <param name="address">The address the request arrived from.</param>
    /// <returns>The visitor key.</returns>
    private string RentedVisitor(Guid siteId, string address) =>
        VisitorKeyFor(siteId, address, RentedNetwork);

    /// <summary>The same, for an address on a network that carries households.</summary>
    /// <param name="siteId">The site being visited.</param>
    /// <param name="address">The address the request arrived from.</param>
    /// <returns>The visitor key.</returns>
    private string HouseholdVisitor(Guid siteId, string address) =>
        VisitorKeyFor(siteId, address, HouseholdNetwork);

    /// <summary>
    /// Derives a key the way the collector would.
    /// </summary>
    /// <remarks>
    /// Under the salt the install holds now, whatever moment the activity is written at. What
    /// these tests are about is whether two addresses reduce to the same visitor, which is a
    /// question about one day's salt rather than about which day it belongs to — and a fresh
    /// install has generated no salt for any day but today.
    /// </remarks>
    /// <param name="siteId">The site being visited.</param>
    /// <param name="address">The address the request arrived from.</param>
    /// <param name="autonomousSystem">The network that address is on.</param>
    /// <returns>The visitor key.</returns>
    private string VisitorKeyFor(Guid siteId, string address, uint autonomousSystem)
    {
        var derived = stack.Services.GetRequiredService<IVisitorKeyFactory>()
            .Derive(siteId, VisitorConnection.Identifying(address, autonomousSystem), Chrome, Now);

        derived.Should().NotBeNull("today's salt is the one a running install always holds");

        return derived;
    }

    private Task WriteAsync(params RawEvent[] events) =>
        stack.Services.GetRequiredService<IEventSink>().WriteBatchAsync(events, Cancellation.Token);

    /// <summary>
    /// Runs the engine over a site, starting far enough back to reach the activity the test wrote.
    /// </summary>
    /// <remarks>
    /// A real site starts from the moment it was added, because nothing can have been observed
    /// before then. These tests write activity in the recent past, so they hand the engine an
    /// earlier starting point — which is also what a rebuild of existing history would do.
    /// </remarks>
    private async Task<ClassificationOutcome> JudgeAsync(Site site)
    {
        await using var scope = stack.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<SessionClassifier>()
            .CatchUpAsync(site.Id, site.CreatedAt.AddDays(-2), Cancellation.Token)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Moves the bookmark back so the same activity is judged again, which is what an interrupted
    /// run or a second instance of the engine would do.
    /// </summary>
    private async Task ResetBookmarkAsync(Guid siteId)
    {
        await using var scope = stack.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        var bookmark = await database.ClassificationProgress
            .FirstAsync(progress => progress.SiteId == siteId, Cancellation.Token)
            .ConfigureAwait(false);

        database.Remove(bookmark);
        await database.SaveChangesAsync(Cancellation.Token).ConfigureAwait(false);
    }

    private Task<IReadOnlyList<ObservedSession>> ReadSessionsAsync(Guid siteId) =>
        ReadSessionsAsync(siteId, Now.AddDays(-2));

    private async Task<IReadOnlyList<ObservedSession>> ReadSessionsAsync(Guid siteId, DateTimeOffset from) =>
        await stack.Services.GetRequiredService<ISessionSource>()
            .ReadAsync(
                new SessionWindow
                {
                    SiteId = siteId,
                    From = from,
                    To = Now.AddMinutes(-30),
                    IdleTimeout = TimeSpan.FromMinutes(30),
                    MaxRequestsPerSession = 1000,
                },
                Cancellation.Token)
            .ConfigureAwait(false);

    private async Task<IReadOnlyList<JudgedSession>> VisitsAsync(Site site) =>
        (await stack.Services.GetRequiredService<ITelemetryQueries>()
            .GetJudgedSessionsAsync(Scope(site), new JudgedSessionsQuery(Window(), 50), Cancellation.Token)
            .ConfigureAwait(false)).Visits;

    private Task<IReadOnlyList<TrafficBreakdownRow>> BreakdownAsync(Site site) =>
        stack.Services.GetRequiredService<ITelemetryQueries>()
            .GetTrafficBreakdownAsync(Scope(site), new TrafficBreakdownQuery(Window()), Cancellation.Token);

    private static TenantScope Scope(Site site) =>
        new(site.Id, site.OrganizationId, SiteRole.Viewer, site.TimeZoneId);

    private TimeRange Window() =>
        new(Now.AddDays(-2), Now.AddMinutes(1));

    private static RawEvent Page(Guid siteId, string visitor, DateTimeOffset at, string path, string userAgent) =>
        new()
        {
            EventId = Guid.CreateVersion7(at),
            SiteId = siteId,
            Kind = EventKind.PageView,
            Surface = IngestSurface.BrowserTracker,
            ServerTimestamp = at,
            VisitorKey = visitor,
            Host = "example.com",
            Path = path,
            UserAgent = userAgent,
            Language = "en-GB",
            ViewportWidth = 1440,
        };

    private static RawEvent Read(
        Guid siteId,
        string visitor,
        DateTimeOffset at,
        string userAgent,
        string path = "/") =>
        Page(siteId, visitor, at, path, userAgent) with
        {
            Kind = EventKind.Exit,
            EngagedMs = 30_000,
            ScrollDepthPercent = 70,
            HadPointerInteraction = true,
            HadKeyboardInteraction = false,
            DeclaredWebDriver = false,
        };

    /// <summary>The same activity, arriving from a computer rented in a datacentre.</summary>
    private static RawEvent Rented(RawEvent observed) =>
        observed with { AutonomousSystem = RentedNetwork, NetworkOwner = "ALIBABA-CN-NET Alibaba US Technology Co., Ltd." };

    /// <summary>
    /// One request as a surface in the request path sees it: a status code, no viewport, and no
    /// reading of anything a browser would have reported.
    /// </summary>
    private static RawEvent Probe(Guid siteId, string visitor, DateTimeOffset at, string path) =>
        new()
        {
            EventId = Guid.CreateVersion7(at),
            SiteId = siteId,
            Kind = EventKind.PageView,
            Surface = IngestSurface.CloudflareWorker,
            ServerTimestamp = at,
            VisitorKey = visitor,
            Host = "example.com",
            Path = path,
            UserAgent = Scanner,
            StatusCode = 404,
        };
}
