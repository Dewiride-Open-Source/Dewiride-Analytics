using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Infrastructure.Persistence;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

    /// <summary>What a crawler says when it introduces itself under a name nobody has catalogued.</summary>
    private const string Nameless = "Mozilla/5.0 (compatible; NobodysBot/1.0; +https://example.com/bot)";

    /// <summary>
    /// What the largest search engine sends, which names no crawler at all. Taken from real
    /// traffic: no catalogue of names can reach this visitor.
    /// </summary>
    private const string HeadlessChrome =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "HeadlessChrome/141.0.0.0 Safari/537.36";

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

    /// <summary>
    /// Midday yesterday, by the clock visitor keys are renewed at — for a visit whose pages are
    /// left open, which near midnight would be read as still reporting under the next day's key.
    /// </summary>
    private DateTimeOffset YesterdayAtNoon => new(Now.UtcDateTime.Date.AddDays(-1).AddHours(12), TimeSpan.Zero);

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
    /// The whole point of checking addresses, end to end. The visitor renders pages from a rented
    /// server and says nothing but the name of an ordinary browser, so every other reading the
    /// engine takes points at either a person or anonymous automation — and it is the largest
    /// search engine indexing the site. Only the address settles it, and only if it survives the
    /// round trip through both stores.
    /// </summary>
    [Fact]
    public async Task A_Crawler_Recognised_By_Its_Address_Is_Named_Although_It_Said_Nothing()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Confirmed(Rented(Page(site.Id, "indexer", at, "/", HeadlessChrome)), "Microsoft"),
            Confirmed(Rented(Page(site.Id, "indexer", at.AddMinutes(2), "/posts/hello", HeadlessChrome)), "Microsoft"));

        await JudgeAsync(site);

        var visits = await VisitsAsync(site);

        visits.Should().ContainSingle();
        visits[0].Verdict.Category.Should().Be(TrafficCategory.KnownSearchCrawler);
        visits[0].Verdict.Strength.Should().Be(EvidenceStrength.Verified);

        var confirmed = visits[0].Verdict.Supporting
            .Single(signal => signal.Code == SignalCodes.ConfirmedCrawler);

        confirmed.Parameters["operator"].Should().Be("Microsoft");
    }

    /// <summary>
    /// A crawler asks for several pages and each is checked as it arrives, so one report settling
    /// the address settles the visit. Requiring every report to agree would lose an identity to a
    /// list that was republished halfway through a visit.
    /// </summary>
    [Fact]
    public async Task One_Report_Settling_The_Address_Settles_The_Whole_Visit()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Page(site.Id, "indexer", at, "/", HeadlessChrome),
            Confirmed(Page(site.Id, "indexer", at.AddMinutes(2), "/posts/hello", HeadlessChrome), "Microsoft"),
            Page(site.Id, "indexer", at.AddMinutes(4), "/about", HeadlessChrome));

        await JudgeAsync(site);

        var visits = await VisitsAsync(site);

        visits[0].Verdict.Category.Should().Be(TrafficCategory.KnownSearchCrawler);
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
    /// A departure report announces no arrival, and it reaches the collector under whatever key the
    /// network and the day produced — which is not the key that announced the visit if either has
    /// changed since. Judged as a visit of its own it becomes a second reader, and a convincing one:
    /// it carries a quarter of an hour of reading and a scroll to the bottom of the page.
    /// </summary>
    [Fact]
    public async Task A_Departure_Nothing_Ever_Arrived_At_Is_Not_Judged_As_A_Visit()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());

        await WriteAsync(Read(site.Id, "reader", Yesterday, Chrome, "/posts/hello"));

        await JudgeAsync(site);

        var visits = await VisitsAsync(site);

        visits.Should().BeEmpty();
    }

    /// <summary>
    /// A tab dismissed the following morning announces the page leaving hours after anybody was
    /// reading it. The reading it carries is the reading of the page it names, so it goes back to
    /// the visit that page was arrived at in rather than opening one of its own.
    /// </summary>
    [Fact]
    public async Task A_Departure_Reported_Hours_Later_Returns_Its_Reading_To_Its_Own_Visit()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Page(site.Id, "reader", at, "/posts/hello", Chrome),
            Read(site.Id, "reader", at.AddHours(4), Chrome, "/posts/hello"));

        var found = await ReadSessionsAsync(site);

        found.Should().ContainSingle();
        found[0].Evidence.PageCount.Should().Be(1);
        found[0].Evidence.EngagedMs.Should().Be(30_000);
        found[0].Evidence.StartedAt.Should().BeCloseTo(at, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// The same reader, with the window ending in the middle of the four-hour silence. A backlog is
    /// walked in stretches, and where one ends is an accident of when the engine was last run — so
    /// the visit is still judged on all of itself rather than on the part that fell inside.
    /// </summary>
    [Fact]
    public async Task A_Visit_Lying_Across_The_End_Of_A_Window_Is_Judged_On_All_Of_It()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Page(site.Id, "reader", at, "/posts/hello", Chrome),
            Read(site.Id, "reader", at.AddHours(4), Chrome, "/posts/hello"));

        var found = await ReadSessionsAsync(site, at.AddMinutes(-1), at.AddHours(2));

        found.Should().ContainSingle();
        found[0].IsClosed.Should().BeTrue();
        found[0].Evidence.EngagedMs.Should().Be(30_000);
        found[0].Evidence.EndedAt.Should().BeCloseTo(at.AddHours(4), TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// A visitor still reading is a visitor who has not left, whatever window they fall in. The
    /// instant a visit is treated as finished is the present moment less an idle timeout, and it is
    /// the only thing that decides this.
    /// </summary>
    [Fact]
    public async Task A_Visit_Still_Under_Way_Is_Not_Judged_Whatever_Window_It_Falls_In()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Now.AddMinutes(-5);

        await WriteAsync(Page(site.Id, "reader", at, "/posts/hello", Chrome));

        var found = await ReadSessionsAsync(site, at.AddMinutes(-1), Now);

        found.Should().ContainSingle();
        found[0].IsClosed.Should().BeFalse();
    }

    /// <summary>
    /// A report about a page this visitor was never seen arriving at says a page was delivered to
    /// somebody, and nothing about which visit it belonged to. Handing it to the nearest visit would
    /// credit a reader with a page they never asked for.
    /// </summary>
    [Fact]
    public async Task A_Report_About_A_Page_Nobody_Arrived_At_Joins_No_Visit()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Page(site.Id, "reader", at, "/posts/hello", Chrome),
            Read(site.Id, "reader", at.AddHours(4), Chrome, "/pricing"));

        var found = await ReadSessionsAsync(site);

        found.Should().ContainSingle();
        found[0].Evidence.PageCount.Should().Be(1);
        found[0].Evidence.Requests.Select(request => request.Path).Should().Equal("/posts/hello");
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

        var found = await ReadSessionsAsync(site);

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

        var found = await ReadSessionsAsync(site);

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
    /// The paths a sweep asks for are the paths a site's owner asks for too. The site answers the
    /// owner, and that answer has to survive the round trip through both stores to keep the owner
    /// off the scanner list.
    /// </summary>
    [Fact]
    public async Task A_Site_Owner_Opening_Their_Own_Administration_Pages_Is_Not_Called_A_Scanner()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Probe(site.Id, "owner", at, "/wp-login.php") with { UserAgent = Chrome, StatusCode = 200 },
            Probe(site.Id, "owner", at.AddSeconds(30), "/wp-admin/") with { UserAgent = Chrome, StatusCode = 302 });

        await JudgeAsync(site);

        var visits = await VisitsAsync(site);

        visits.Should().ContainSingle();
        visits[0].Verdict.Category.Should().NotBe(TrafficCategory.SecurityScanner);
        visits[0].Verdict.Supporting.Should().NotContain(signal => signal.Code == SignalCodes.SensitivePaths);
        visits[0].Verdict.Contradicting.Should().NotContain(signal => signal.Code == SignalCodes.SensitivePaths);
    }

    /// <summary>
    /// The commonest visit a website gets: one page, the tracker ran, nothing else was seen. The
    /// engine settles nothing about it, and the honest answer survives the round trip.
    /// </summary>
    [Fact]
    public async Task One_Page_With_The_Tracker_Running_And_Nothing_Else_Is_Not_Called_A_Person()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());

        await WriteAsync(Page(site.Id, "glance", Yesterday, "/", Chrome));

        await JudgeAsync(site);

        var visits = await VisitsAsync(site);

        visits.Should().ContainSingle();
        visits[0].Verdict.Category.Should().Be(TrafficCategory.Unknown);
        visits[0].Verdict.Strength.Should().NotBe(EvidenceStrength.Verified);
        visits[0].Verdict.Supporting.Should().Contain(signal => signal.Code == SignalCodes.ScriptExecuted);
    }

    /// <summary>
    /// A crawler introducing itself under a name nobody has catalogued is called a crawler, and the
    /// observation reaches the store and comes back carrying nothing the visitor wrote.
    /// </summary>
    [Fact]
    public async Task A_Crawler_Naming_Itself_Only_As_A_Crawler_Is_Called_One()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Probe(site.Id, "nameless", at, "/") with { UserAgent = Nameless, StatusCode = 200 },
            Probe(site.Id, "nameless", at.AddSeconds(2), "/posts/hello") with { UserAgent = Nameless, StatusCode = 200 });

        await JudgeAsync(site);

        var visits = await VisitsAsync(site);

        visits.Should().ContainSingle();
        visits[0].Verdict.Category.Should().Be(TrafficCategory.GenericWebCrawler);
        visits[0].Verdict.Supporting
            .Should().ContainSingle(signal => signal.Code == SignalCodes.DeclaredGenericCrawler)
            .Which.Parameters.Should().BeEmpty();
    }

    /// <summary>
    /// A run that has lost its bookmark reads the site again from the start, and leaves out every
    /// visit already judged on everything it holds: nothing is judged a second time.
    /// </summary>
    [Fact]
    public async Task Reading_A_Site_Again_From_The_Start_Judges_Nothing_It_Already_Judged()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(Page(site.Id, "returning", at, "/", Chrome));

        var first = await JudgeAsync(site);
        await ResetBookmarkAsync(site.Id);
        var again = await JudgeAsync(site);

        var breakdown = await BreakdownAsync(site);

        first.Judged.Should().Be(1);
        again.Judged.Should().Be(0);
        breakdown.Sum(group => group.Sessions).Should().Be(1);
    }

    /// <summary>
    /// Two instances of the engine reading the same stretch at once can each judge a visit before
    /// either verdict is stored, and a visit made longer is judged again. Every verdict is kept under
    /// the visit's one name, the visit is counted once, and the conclusion reached later stands.
    /// </summary>
    [Fact]
    public async Task Judging_The_Same_Visit_Twice_Leaves_One_Verdict_And_The_Later_Stands()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());

        await WriteAsync(Page(site.Id, "returning", Yesterday, "/", Chrome));

        var visit = (await ReadSessionsAsync(site)).Single();
        var verdict = TrafficClassifier.Current().Classify(visit.Evidence);
        var store = stack.Services.GetRequiredService<IClassificationStore>();

        await store.SaveAsync(
            site.Id,
            [new SessionJudgement(visit.Evidence, verdict)],
            Now.AddMinutes(-2),
            Cancellation.Token);
        await store.SaveAsync(
            site.Id,
            [new SessionJudgement(visit.Evidence, verdict with { Category = TrafficCategory.SecurityScanner })],
            Now.AddMinutes(-1),
            Cancellation.Token);

        var breakdown = await BreakdownAsync(site);
        var visits = await VisitsAsync(site);

        breakdown.Sum(group => group.Sessions).Should().Be(1);
        visits.Should().ContainSingle()
            .Which.Verdict.Category.Should().Be(TrafficCategory.SecurityScanner);
    }

    /// <summary>
    /// A verdict under other rules does not stand for one under these. That is what lets a change
    /// of rules judge a site's whole history again: the reading leaves out only what the rules being
    /// run have judged.
    /// </summary>
    [Fact]
    public async Task A_Visit_Judged_Only_Under_Other_Rules_Is_Judged_Under_These()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());

        await WriteAsync(Page(site.Id, "returning", Yesterday, "/", Chrome));

        var visit = (await ReadSessionsAsync(site)).Single();
        var earlier = new RulesetVersion(RulesetVersion.Current.Major - 1, 0);
        var verdict = TrafficClassifier.Current().Classify(visit.Evidence) with { RulesetVersion = earlier };

        await stack.Services.GetRequiredService<IClassificationStore>()
            .SaveAsync(site.Id, [new SessionJudgement(visit.Evidence, verdict)], Now, Cancellation.Token);

        var outcome = await JudgeAsync(site);
        var visits = await VisitsAsync(site);

        outcome.Judged.Should().Be(1);
        visits.Should().ContainSingle()
            .Which.Verdict.RulesetVersion.Should().Be(RulesetVersion.Current);
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

        var whole = await ReadSessionsAsync(site, at.AddMinutes(-1));
        var remainder = await ReadSessionsAsync(site, at.AddMinutes(3));

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
    /// A page left on somebody's screen reports every minute while it is in front of them, so
    /// their visit is still going hours later. It waits to be judged; the visits that began while it
    /// was open do not — not the one an hour in, and not the one that began more than a whole
    /// stretch after it, past anything the engine reads in one pass.
    /// </summary>
    [Fact]
    public async Task A_Page_Left_Open_All_Day_Holds_Back_Only_Its_Own_Visit()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var stretch = stack.Services.GetRequiredService<IOptions<ClassificationOptions>>().Value.LongestPass;
        var later = stretch + TimeSpan.FromHours(1);
        var opened = Now - later - TimeSpan.FromHours(1);

        await WriteAsync(LeftOpen(site.Id, "lingering", opened, Now.AddMinutes(-5)));
        await WriteAsync(
            Page(site.Id, "early", opened.AddHours(1), "/", Chrome),
            Read(site.Id, "early", opened.AddHours(1).AddMinutes(4), Chrome),
            Page(site.Id, "late", opened + later, "/posts/later", Chrome),
            Read(site.Id, "late", opened + later + TimeSpan.FromMinutes(4), Chrome, "/posts/later"));

        var outcome = await JudgeAsync(site);

        var visits = await VisitsAsync(site);

        visits.Select(visit => visit.StartedAt).Order().Should().SatisfyRespectively(
            early => early.Should().BeCloseTo(opened.AddHours(1), TimeSpan.FromMilliseconds(1)),
            late => late.Should().BeCloseTo(opened + later, TimeSpan.FromMilliseconds(1)));
        outcome.ResumeFrom.Should().BeCloseTo(opened, TimeSpan.FromMilliseconds(1));
    }

    /// <summary>
    /// Every run starts back at the visit still open, so as to find it again, and leaves out what
    /// it judged last time. A visit is judged once, however many runs pass before the page left
    /// open is closed.
    /// </summary>
    [Fact]
    public async Task A_Visit_Judged_Behind_One_Still_Open_Is_Not_Judged_Again()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var opened = Now.AddHours(-3);

        await WriteAsync(LeftOpen(site.Id, "lingering", opened, Now.AddMinutes(-5)));
        await WriteAsync(
            Page(site.Id, "behind", opened.AddHours(1), "/", Chrome),
            Read(site.Id, "behind", opened.AddHours(1).AddMinutes(4), Chrome));

        var first = await JudgeAsync(site);
        var second = await JudgeAsync(site);

        first.Judged.Should().Be(1);
        second.Judged.Should().Be(0);
    }

    /// <summary>
    /// A page announcing it is being left can arrive long after the reading it ends, and it belongs
    /// to the visit that page was opened in. Where that visit has been judged already, it is a
    /// longer visit than the one judged, and it is judged again on all of itself.
    /// </summary>
    [Fact]
    public async Task A_Visit_Made_Longer_After_It_Was_Judged_Is_Judged_Again_On_All_Of_Itself()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var opened = Now.AddHours(-3);
        var arrived = opened.AddHours(1);

        await WriteAsync(LeftOpen(site.Id, "lingering", opened, Now.AddMinutes(-5)));
        await WriteAsync(
            Page(site.Id, "returning", arrived, "/", Chrome),
            Read(site.Id, "returning", arrived.AddMinutes(4), Chrome));

        await JudgeAsync(site);
        await WriteAsync(Read(site.Id, "returning", arrived.AddHours(1), Chrome));
        var again = await JudgeAsync(site);

        var visits = await VisitsAsync(site);

        again.Judged.Should().Be(1);
        visits.Should().ContainSingle()
            .Which.EndedAt.Should().BeCloseTo(arrived.AddHours(1), TimeSpan.FromMilliseconds(1));
    }

    /// <summary>
    /// The same case while the report is still recent, which is how it reaches a live site: the
    /// collector stamps a report when it arrives, so a visit a report has just made longer is in
    /// progress again. It is not judged again yet, and its verdict stands as it was until it is.
    /// </summary>
    [Fact]
    public async Task A_Visit_A_Report_Has_Just_Made_Longer_Keeps_Its_Verdict_Until_It_Is_Over()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var opened = Now.AddHours(-3);
        var arrived = opened.AddHours(1);

        await WriteAsync(LeftOpen(site.Id, "lingering", opened, Now.AddMinutes(-5)));
        await WriteAsync(
            Page(site.Id, "returning", arrived, "/", Chrome),
            Read(site.Id, "returning", arrived.AddMinutes(4), Chrome));

        await JudgeAsync(site);
        await WriteAsync(Read(site.Id, "returning", Now.AddMinutes(-10), Chrome));
        var again = await JudgeAsync(site);

        var visits = await VisitsAsync(site);

        again.Judged.Should().Be(0);
        visits.Should().ContainSingle()
            .Which.EndedAt.Should().BeCloseTo(arrived.AddMinutes(4), TimeSpan.FromMilliseconds(1));
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

        var found = await ReadSessionsAsync(site);

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

        var found = await ReadSessionsAsync(site);

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
        starts.Should().AllSatisfy(start =>
        {
            start.ClassifiedThrough.Should().BeCloseTo(from, TimeSpan.FromMicroseconds(1));
            start.ReadThrough.Should().BeCloseTo(from, TimeSpan.FromMicroseconds(1));
        });
    }

    private async Task<ClassificationBookmark> ResumePointAsync(Guid siteId, DateTimeOffset from)
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
    /// The address is what settles the identity of a crawler whose operator publishes no list, and
    /// it has to survive the whole reconstruction to be of any use. It is carried beside the
    /// evidence rather than within it: the engine must never be able to reason about where
    /// somebody lives, and a test that only read the stored column would not notice if it could.
    /// </summary>
    [Fact]
    public async Task The_Address_A_Visit_Arrived_From_Is_Carried_Back_Beside_The_Evidence()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            From(Page(site.Id, "indexer", at, "/", HeadlessChrome), "77.88.5.1"),
            From(Page(site.Id, "indexer", at.AddMinutes(2), "/posts/hello", HeadlessChrome), "77.88.5.1"));

        var visits = await ReconstructAsync(site);

        visits.Should().ContainSingle();
        visits[0].Address.Should().Be("77.88.5.1");
        visits[0].Evidence.ConfirmedOperator.Should().BeNull();
    }

    /// <summary>
    /// One report carrying an address is enough. A visit spanning several has one operator behind
    /// it whichever of its addresses the question is asked about.
    /// </summary>
    [Fact]
    public async Task One_Report_Carrying_An_Address_Is_Enough_To_Ask_About_The_Visit()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Page(site.Id, "indexer", at, "/", HeadlessChrome),
            From(Page(site.Id, "indexer", at.AddMinutes(2), "/posts/hello", HeadlessChrome), "77.88.5.1"));

        var visits = await ReconstructAsync(site);

        visits.Should().ContainSingle();
        visits[0].Address.Should().Be("77.88.5.1");
    }

    /// <summary>
    /// What every visit older than three days looks like once the address has been erased, and
    /// what a surface that never observed one looks like from the beginning.
    /// </summary>
    [Fact]
    public async Task A_Visit_With_No_Address_Recorded_Comes_Back_Without_One()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());

        await WriteAsync(Page(site.Id, "indexer", Yesterday, "/", HeadlessChrome));

        var visits = await ReconstructAsync(site);

        visits.Should().ContainSingle();
        visits[0].Address.Should().BeNull();
    }

    /// <summary>
    /// Somebody a search sent to the site, reading on a desktop rented in Microsoft's cloud: a
    /// person, end to end, with the network kept as the reason pointing the other way.
    /// </summary>
    [Fact]
    public async Task Somebody_A_Search_Sent_To_A_Cloud_Desktop_Who_Then_Read_Is_A_Person()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            OnMicrosoftsNetwork(SentFrom(Page(site.Id, "cloud", at, "/", Chrome), Bing)),
            OnMicrosoftsNetwork(Page(site.Id, "cloud", at.AddMinutes(2), "/posts/hello", Chrome)),
            OnMicrosoftsNetwork(Read(site.Id, "cloud", at.AddMinutes(4), Chrome)));

        await JudgeAsync(site);

        var visit = (await VisitsAsync(site)).Should().ContainSingle().Subject;

        visit.Verdict.Category.Should().Be(TrafficCategory.LikelyHuman);
        visit.Verdict.Strength.Should().Be(EvidenceStrength.Moderate);
        visit.Verdict.Contradicting.Should().Contain(signal => signal.Code == SignalCodes.HostingNetwork);
        visit.Verdict.Supporting.Single(signal => signal.Code == SignalCodes.ArrivedFromAnotherSite)
            .Parameters["kind"].Should().Be("search");
    }

    /// <summary>
    /// The shape measured on Microsoft's network: straight to one page, a while on it, the pointer
    /// pressed and no key. Still not a person.
    /// </summary>
    [Fact]
    public async Task A_Visit_That_Came_Straight_To_A_Cloud_Network_And_Only_Pressed_The_Pointer_Is_Not_A_Person()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            OnMicrosoftsNetwork(Page(site.Id, "hands-off", at, "/", Chrome)),
            OnMicrosoftsNetwork(Read(site.Id, "hands-off", at.AddSeconds(26), Chrome) with
            {
                EngagedMs = 26_000,
                ScrollDepthPercent = 11,
            }));

        await JudgeAsync(site);

        (await VisitsAsync(site)).Should().ContainSingle()
            .Which.Verdict.Category.Should().Be(TrafficCategory.SuspiciousAutomation);
    }

    /// <summary>
    /// What kind of site sent a visit reaches the engine through the reconstruction, from the
    /// visit's first report naming somebody else — and only the kind, never the address.
    /// </summary>
    [Fact]
    public async Task Which_Kind_Of_Site_Sent_A_Visit_Survives_The_Round_Trip()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            SentFrom(Page(site.Id, "searcher", at, "/", Chrome), Bing),
            SentFrom(Page(site.Id, "linked", at.AddMinutes(5), "/", Chrome), "https://news.example.org/story/42"),
            Page(site.Id, "straight", at.AddMinutes(10), "/", Chrome));

        var sent = (await ReadSessionsAsync(site)).ToDictionary(
            visit => visit.Evidence.SessionKey.Split(':')[0],
            visit => visit.Evidence.SentBy,
            StringComparer.Ordinal);

        sent["searcher"].Should().Be(SourceChannel.Search);
        sent["linked"].Should().Be(SourceChannel.Link);
        sent["straight"].Should().Be(SourceChannel.Direct);
    }

    /// <summary>
    /// A reader moving from one of the site's pages to the next, or onto one of its own
    /// subdomains, names the site itself as where they came from. That is no arrival.
    /// </summary>
    [Fact]
    public async Task The_Sites_Own_Pages_Never_Send_It_A_Visit()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            SentFrom(Page(site.Id, "inside", at, "/posts/hello", Chrome), $"https://{site.Domain}/"),
            SentFrom(Page(site.Id, "docs", at.AddMinutes(5), "/", Chrome), $"https://docs.{site.Domain}/guide"));

        var found = await ReadSessionsAsync(site);

        found.Should().HaveCount(2).And.OnlyContain(visit => visit.Evidence.SentBy == SourceChannel.Direct);
    }

    /// <summary>
    /// The visit fleets of browsers made on a live installation: one page opened straight, the
    /// tracker's first account of a page still open at fifteen seconds, and nothing after it — the
    /// browser was switched off rather than the page closed. It reaches the engine as a page left
    /// open, and a browser keeping a page open is not called a person.
    /// </summary>
    [Fact]
    public async Task A_Page_Left_Open_And_Never_Said_To_Be_Left_Is_Not_Taken_For_Reading()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = YesterdayAtNoon;

        await WriteAsync(
            Page(site.Id, "left-open", at, "/posts/hello", Chrome),
            Progress(site.Id, "left-open", at.AddSeconds(15), "/posts/hello", 15_005));

        var found = await ReadSessionsAsync(site);

        found.Should().ContainSingle();
        found[0].Evidence.EngagedMs.Should().Be(15_005);
        found[0].Evidence.LeftOpenMs.Should().Be(15_005);

        await JudgeAsync(site);

        var visits = await VisitsAsync(site);

        visits.Should().ContainSingle();
        visits[0].Verdict.Category.Should().Be(TrafficCategory.Unknown);
        visits[0].Verdict.Supporting.Should().Contain(signal => signal.Code == SignalCodes.LeftOpen);
        visits[0].Verdict.Supporting.Should().NotContain(signal => signal.Code == SignalCodes.ReadTime);
    }

    /// <summary>
    /// A page read for twenty seconds, put behind another tab, and brought back for half a minute
    /// more before the browser stopped answering: the twenty were said as the page was left, and
    /// the thirty after them were not.
    /// </summary>
    [Fact]
    public async Task Time_Up_To_The_Last_Time_A_Page_Said_It_Was_Being_Left_Is_Read_And_What_Followed_Is_Left_Open()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = YesterdayAtNoon;

        await WriteAsync(
            Page(site.Id, "returner", at, "/posts/hello", Chrome),
            Progress(site.Id, "returner", at.AddSeconds(15), "/posts/hello", 15_000),
            Left(site.Id, "returner", at.AddSeconds(20), "/posts/hello", 20_000),
            Progress(site.Id, "returner", at.AddSeconds(80), "/posts/hello", 50_000));

        var found = await ReadSessionsAsync(site);

        found.Should().ContainSingle();
        found[0].Evidence.EngagedMs.Should().Be(50_000);
        found[0].Evidence.LeftOpenMs.Should().Be(30_000);
    }

    /// <summary>
    /// A page every one of whose readings was said as it was left has nothing left open, and a
    /// page only something on the site's own server saw has no reading to leave open at all.
    /// </summary>
    [Fact]
    public async Task Nothing_Is_Left_Open_Where_Every_Page_Was_Let_Go_Of_And_Nothing_Where_Nothing_Was_Measured()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Page(site.Id, "reader", at, "/posts/hello", Chrome),
            Progress(site.Id, "reader", at.AddSeconds(15), "/posts/hello", 15_000),
            Left(site.Id, "reader", at.AddSeconds(40), "/posts/hello", 40_000),
            Probe(site.Id, "unwatched", at, "/posts/hello") with { StatusCode = 200 });

        var found = await ReadSessionsAsync(site);

        found.Should().HaveCount(2);
        found.Single(visit => visit.Evidence.SessionKey.StartsWith("reader", StringComparison.Ordinal))
            .Evidence.LeftOpenMs.Should().Be(0);
        found.Single(visit => visit.Evidence.SessionKey.StartsWith("unwatched", StringComparison.Ordinal))
            .Evidence.LeftOpenMs.Should().BeNull();
    }

    /// <summary>
    /// What a page opened in a sandbox and hurried through looks like on a live installation: the
    /// tracker's fifteen-second wait over within two seconds, by the page's own time of day and by
    /// the time it says it was in front of somebody alike. Whatever else the page did, its timers
    /// ran ahead of its clocks.
    /// </summary>
    [Fact]
    public async Task A_Page_Whose_Wait_Was_Cut_Short_Is_Something_Automated()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Page(site.Id, "sandbox", at, "/posts/hello", Chrome) with { ClientTimestamp = at },
            Progress(site.Id, "sandbox", at.AddSeconds(2), "/posts/hello", 1_800) with
            {
                ClientTimestamp = at.AddMilliseconds(1_850),
                HadPointerInteraction = true,
                ScrollDepthPercent = 60,
            },
            Progress(site.Id, "sandbox", at.AddSeconds(4), "/posts/hello", 3_600) with
            {
                ClientTimestamp = at.AddMilliseconds(3_650),
            },
            Left(site.Id, "sandbox", at.AddSeconds(25), "/posts/hello", 25_000) with
            {
                ClientTimestamp = at.AddSeconds(25),
                HadPointerInteraction = true,
                ScrollDepthPercent = 60,
            });

        var found = await ReadSessionsAsync(site);

        found.Should().ContainSingle();
        found[0].Evidence.SoonestProgressMs.Should().Be(1_850);

        await JudgeAsync(site);

        var visits = await VisitsAsync(site);

        visits.Should().ContainSingle();
        visits[0].Verdict.Category.Should().Be(TrafficCategory.SuspiciousAutomation);
        visits[0].Verdict.Supporting.Should().Contain(signal => signal.Code == SignalCodes.WaitCutShort);
    }

    /// <summary>
    /// A browser that reports the time a page has been in front of somebody at a fraction of the
    /// real rate, while its timers keep real time: its first report arrives on time by the page's
    /// own clock, so the longer of the two readings is the full wait, and it is not taken for a
    /// wait cut short.
    /// </summary>
    [Fact]
    public async Task A_Browser_Whose_Reading_Clock_Runs_Slow_Is_Not_Taken_For_A_Wait_Cut_Short()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Page(site.Id, "slow-clock", at, "/posts/hello", Chrome) with { ClientTimestamp = at },
            Progress(site.Id, "slow-clock", at.AddSeconds(15), "/posts/hello", 8_300) with
            {
                ClientTimestamp = at.AddSeconds(15),
            });

        var found = await ReadSessionsAsync(site);

        found.Should().ContainSingle();
        found[0].Evidence.SoonestProgressMs.Should().Be(15_000);
    }

    /// <summary>
    /// The longer of the two clocks is read, whichever it is: a report the page's time of day puts
    /// five seconds after the page opened, but which says the page was in front of somebody for
    /// the whole fifteen, took its wait.
    /// </summary>
    [Fact]
    public async Task The_Longer_Of_A_Reports_Two_Clocks_Is_The_One_Read()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Page(site.Id, "clock-set-back", at, "/posts/hello", Chrome) with { ClientTimestamp = at },
            Progress(site.Id, "clock-set-back", at.AddSeconds(15), "/posts/hello", 15_000) with
            {
                ClientTimestamp = at.AddSeconds(5),
            });

        var found = await ReadSessionsAsync(site);

        found.Should().ContainSingle();
        found[0].Evidence.SoonestProgressMs.Should().Be(15_000);
    }

    /// <summary>
    /// Two tabs of one page opened five seconds apart, on a browser whose reading clock runs at
    /// half the real rate: the first tab's report reaches the collector after the second tab's
    /// page view and is filed with it. Read against the second tab's opening, both of its clocks
    /// would look short; it is not read against it, because only the first page at an address in
    /// a visit is read for how soon its wait was over.
    /// </summary>
    [Fact]
    public async Task A_Report_Is_Never_Read_Against_A_Later_Opening_Of_The_Same_Page()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Page(site.Id, "two-tabs", at, "/posts/hello", Chrome) with { ClientTimestamp = at },
            Page(site.Id, "two-tabs", at.AddSeconds(5), "/posts/hello", Chrome) with { ClientTimestamp = at.AddSeconds(5) },
            Progress(site.Id, "two-tabs", at.AddSeconds(15), "/posts/hello", 7_500) with
            {
                ClientTimestamp = at.AddSeconds(15),
            });

        var found = await ReadSessionsAsync(site);

        found.Should().ContainSingle();
        found[0].Evidence.SoonestProgressMs.Should().BeNull();
    }

    /// <summary>
    /// A report filed with a page but sent, by its own time of day, before that page opened came
    /// from somewhere else — a tab of the same page from earlier — and is never read against this
    /// page's opening.
    /// </summary>
    [Fact]
    public async Task A_Report_Sent_Before_The_Page_It_Is_Filed_With_Opened_Is_Not_Read_Against_It()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Page(site.Id, "earlier-tab", at, "/posts/hello", Chrome) with { ClientTimestamp = at },
            Progress(site.Id, "earlier-tab", at.AddSeconds(1), "/posts/hello", 1_800) with
            {
                ClientTimestamp = at.AddSeconds(-2),
            });

        var found = await ReadSessionsAsync(site);

        found.Should().ContainSingle();
        found[0].Evidence.SoonestProgressMs.Should().BeNull();
    }

    /// <summary>
    /// A reader jumps to another section of a page, which reports the page being left and arrived
    /// at again under the same address, and the report of it being left reaches the collector a
    /// moment after the new arrival. The first stretch of the page is not taken for one left open:
    /// its leaving was filed with the next.
    /// </summary>
    [Fact]
    public async Task A_Page_Arrived_At_Again_At_The_Same_Address_Is_Not_Taken_For_One_Left_Open()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Page(site.Id, "sections", at, "/docs/guide", Chrome),
            Progress(site.Id, "sections", at.AddSeconds(15), "/docs/guide", 15_000),
            Page(site.Id, "sections", at.AddSeconds(20), "/docs/guide", Chrome),
            Left(site.Id, "sections", at.AddSeconds(20).AddMilliseconds(80), "/docs/guide", 20_000),
            Left(site.Id, "sections", at.AddSeconds(30), "/docs/guide", 10_000));

        var found = await ReadSessionsAsync(site);

        found.Should().ContainSingle();
        found[0].Evidence.LeftOpenMs.Should().Be(0);
    }

    /// <summary>
    /// A visitor's key is renewed at midnight, so what a page still open then reports afterwards
    /// is filed under a key that never arrived at it. A page that reported within a progress
    /// report's interval of midnight is not taken for one left open; one that fell silent well
    /// before midnight is.
    /// </summary>
    [Fact]
    public async Task A_Page_Still_Reporting_At_Midnight_Is_Not_Taken_For_One_Left_Open()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var midnight = new DateTimeOffset(Now.UtcDateTime.Date.AddDays(-1), TimeSpan.Zero);
        var late = midnight.AddSeconds(-60);
        var early = midnight.AddMinutes(-10);

        await WriteAsync(
            Page(site.Id, "midnight", late, "/posts/hello", Chrome),
            Progress(site.Id, "midnight", late.AddSeconds(15), "/posts/hello", 15_000),
            Progress(site.Id, "midnight", late.AddSeconds(45), "/posts/hello", 45_000),
            Page(site.Id, "evening", early, "/posts/hello", Chrome),
            Progress(site.Id, "evening", early.AddSeconds(15), "/posts/hello", 15_000));

        var found = await ReadSessionsAsync(site, midnight.AddDays(-1));

        found.Should().HaveCount(2);
        found.Single(visit => visit.Evidence.SessionKey.StartsWith("midnight", StringComparison.Ordinal))
            .Evidence.LeftOpenMs.Should().Be(0);
        found.Single(visit => visit.Evidence.SessionKey.StartsWith("evening", StringComparison.Ordinal))
            .Evidence.LeftOpenMs.Should().Be(15_000);
    }

    /// <summary>
    /// A page left before its first report of how it was going says nothing about how soon that
    /// report came, and a report missing either of its readings is never read as nought.
    /// </summary>
    [Fact]
    public async Task A_Wait_Is_Not_Read_Where_Any_Of_Its_Readings_Is_Missing()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Page(site.Id, "glance", at, "/posts/hello", Chrome) with { ClientTimestamp = at },
            Left(site.Id, "glance", at.AddSeconds(5), "/posts/hello", 5_000) with { ClientTimestamp = at.AddSeconds(5) },
            Page(site.Id, "no-time-of-day", at.AddMinutes(1), "/posts/hello", Chrome) with
            {
                ClientTimestamp = at.AddMinutes(1),
            },
            Progress(site.Id, "no-time-of-day", at.AddMinutes(1).AddSeconds(2), "/posts/hello", 1_800),
            Page(site.Id, "no-reading", at.AddMinutes(2), "/posts/hello", Chrome) with
            {
                ClientTimestamp = at.AddMinutes(2),
            },
            Progress(site.Id, "no-reading", at.AddMinutes(2).AddSeconds(2), "/posts/hello", 1_800) with
            {
                ClientTimestamp = at.AddMinutes(2).AddSeconds(2),
                EngagedMs = null,
            });

        var found = await ReadSessionsAsync(site);

        found.Should().HaveCount(3).And.OnlyContain(visit => visit.Evidence.SoonestProgressMs == null);
    }

    /// <summary>
    /// A page both the site's own server and the tracker reported is one page, and its wait is
    /// read against the time of day the tracker's page view gave, whichever report opens it.
    /// </summary>
    [Fact]
    public async Task A_Page_Both_The_Server_And_The_Tracker_Saw_Is_Read_Against_The_Trackers_Opening()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Probe(site.Id, "both-halves", at, "/posts/hello") with { StatusCode = 200, UserAgent = Chrome, ClientTimestamp = at },
            Page(site.Id, "both-halves", at.AddMilliseconds(300), "/posts/hello", Chrome) with
            {
                ClientTimestamp = at.AddMilliseconds(300),
            },
            Progress(site.Id, "both-halves", at.AddSeconds(2), "/posts/hello", 1_800) with
            {
                ClientTimestamp = at.AddMilliseconds(2_300),
            });

        var found = await ReadSessionsAsync(site);

        found.Should().ContainSingle();
        found[0].Evidence.SoonestProgressMs.Should().Be(2_000);
    }

    /// <summary>
    /// Across a visit's pages: one let go of, one left open, one never reported on and one whose
    /// wait was cut short. What was left open is the open pages' time, and how soon a wait was over
    /// is the cut-short page's.
    /// </summary>
    [Fact]
    public async Task Pages_Left_Open_And_Waits_Cut_Short_Are_Read_Across_A_Whole_Visit()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = YesterdayAtNoon;

        await WriteAsync(
            Page(site.Id, "four-pages", at, "/a", Chrome),
            Left(site.Id, "four-pages", at.AddSeconds(30), "/a", 30_000),
            Page(site.Id, "four-pages", at.AddSeconds(31), "/b", Chrome),
            Progress(site.Id, "four-pages", at.AddSeconds(46), "/b", 15_000),
            Page(site.Id, "four-pages", at.AddSeconds(60), "/c", Chrome),
            Page(site.Id, "four-pages", at.AddSeconds(70), "/d", Chrome) with { ClientTimestamp = at.AddSeconds(70) },
            Progress(site.Id, "four-pages", at.AddSeconds(72), "/d", 1_800) with
            {
                ClientTimestamp = at.AddSeconds(70).AddMilliseconds(1_850),
            });

        var found = await ReadSessionsAsync(site);

        found.Should().ContainSingle();
        found[0].Evidence.EngagedMs.Should().Be(46_800);
        found[0].Evidence.LeftOpenMs.Should().Be(16_800);
        found[0].Evidence.SoonestProgressMs.Should().Be(1_850);
    }

    /// <summary>
    /// One report claiming a page was open for as long as a reading can say does not swallow what
    /// the visit's other pages said as they were left: what was read is the seventy seconds the
    /// first page said, whatever the second claimed.
    /// </summary>
    [Fact]
    public async Task One_Impossible_Report_Does_Not_Swallow_What_Other_Pages_Said_As_They_Were_Left()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = YesterdayAtNoon;

        await WriteAsync(
            Page(site.Id, "impossible", at, "/a", Chrome),
            Left(site.Id, "impossible", at.AddSeconds(70), "/a", 70_000),
            Page(site.Id, "impossible", at.AddSeconds(71), "/b", Chrome),
            Progress(site.Id, "impossible", at.AddSeconds(86), "/b", int.MaxValue));

        var found = await ReadSessionsAsync(site);

        found.Should().ContainSingle();
        found[0].Evidence.EngagedMs.Should().Be(int.MaxValue);
        (found[0].Evidence.EngagedMs - found[0].Evidence.LeftOpenMs).Should().Be(70_000);
    }

    /// <summary>
    /// A reporter on the site's own server keeps a different clock from the tracker's, so a page
    /// whose opening only the server saw has no time of day the tracker's report can be read
    /// against — and nothing missing is read as nought.
    /// </summary>
    [Fact]
    public async Task A_Report_Is_Never_Read_Against_A_Time_Of_Day_The_Tracker_Did_Not_Give()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            Probe(site.Id, "server-seen", at, "/posts/hello") with { StatusCode = 200, ClientTimestamp = at },
            Progress(site.Id, "server-seen", at.AddSeconds(2), "/posts/hello", 1_800) with
            {
                ClientTimestamp = at.AddSeconds(2),
            });

        var found = await ReadSessionsAsync(site);

        found.Should().ContainSingle();
        found[0].Evidence.SoonestProgressMs.Should().BeNull();
    }

    /// <summary>A search engine's results page, as a browser names it.</summary>
    private const string Bing = "https://www.bing.com/search?q=traffic+authenticity";

    /// <summary>The same activity, with the page it was reached from named.</summary>
    private static RawEvent SentFrom(RawEvent observed, string referrer) =>
        observed with { Referrer = referrer, ReferrerDomain = new Uri(referrer).Host };

    /// <summary>The same activity, over Microsoft's network.</summary>
    private static RawEvent OnMicrosoftsNetwork(RawEvent observed) =>
        observed with { AutonomousSystem = 8075, NetworkOwner = "MICROSOFT-CORP-MSN-AS-BLOCK" };

    /// <summary>Rebuilds the visits the engine would be handed, without judging them.</summary>
    private async Task<IReadOnlyList<ObservedSession>> ReconstructAsync(Site site) =>
        await stack.Services.GetRequiredService<ISessionSource>()
            .ReadAsync(
                new SessionWindow
                {
                    SiteId = site.Id,
                    SiteDomain = site.Domain,
                    From = Now.AddDays(-2),
                    To = Now.AddMinutes(-31),
                    SettledBefore = Now.AddMinutes(-30),
                    UnjudgedUnder = RulesetVersion.Current,
                    IdleTimeout = TimeSpan.FromMinutes(30),
                    MaxRequestsPerSession = 1000,
                },
                Cancellation.Token)
            .ConfigureAwait(false);

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
            .CatchUpAsync(new SiteRegistration(site.Id, site.Domain, site.CreatedAt.AddDays(-2)), Cancellation.Token)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Removes the bookmark so the same activity is read again from the start, which is what an
    /// interrupted run or a second instance of the engine would do.
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

    private Task<IReadOnlyList<ObservedSession>> ReadSessionsAsync(Site site) =>
        ReadSessionsAsync(site, Now.AddDays(-2));

    private Task<IReadOnlyList<ObservedSession>> ReadSessionsAsync(Site site, DateTimeOffset from) =>
        ReadSessionsAsync(site, from, Now.AddMinutes(-30));

    private async Task<IReadOnlyList<ObservedSession>> ReadSessionsAsync(
        Site site,
        DateTimeOffset from,
        DateTimeOffset to) =>
        await stack.Services.GetRequiredService<ISessionSource>()
            .ReadAsync(
                new SessionWindow
                {
                    SiteId = site.Id,
                    SiteDomain = site.Domain,
                    From = from,
                    To = to,
                    SettledBefore = Now.AddMinutes(-30),
                    UnjudgedUnder = RulesetVersion.Current,
                    IdleTimeout = TimeSpan.FromMinutes(30),
                    MaxRequestsPerSession = 1000,
                },
                Cancellation.Token)
            .ConfigureAwait(false);

    private async Task<IReadOnlyList<JudgedSession>> VisitsAsync(Site site) =>
        (await stack.Services.GetRequiredService<ITelemetryQueries>()
            .GetJudgedSessionsAsync(
                Scope(site),
                new JudgedSessionsQuery(Window(), TimeSpan.FromMinutes(30), site.Domain, 50),
                Cancellation.Token)
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

    /// <summary>
    /// A page held in front of somebody from one moment to another, reporting on itself while it
    /// is, as a tracker does — often enough that the visit is never quiet for an idle timeout, so
    /// it is still going at the last report.
    /// </summary>
    /// <param name="siteId">The site.</param>
    /// <param name="visitor">Whose page it is.</param>
    /// <param name="from">When the page was opened.</param>
    /// <param name="until">When it last reported.</param>
    /// <returns>The arrival and every report after it.</returns>
    private static RawEvent[] LeftOpen(Guid siteId, string visitor, DateTimeOffset from, DateTimeOffset until)
    {
        var reports = new List<RawEvent> { Page(siteId, visitor, from, "/docs", Chrome) };

        for (var at = from.AddMinutes(20); at < until; at = at.AddMinutes(20))
        {
            reports.Add(StillReading(siteId, visitor, from, at));
        }

        reports.Add(StillReading(siteId, visitor, from, until));

        return [.. reports];
    }

    /// <summary>
    /// The tracker's running account of a page still open: how long it has been in front of
    /// somebody so far, the first screen in view, nothing pressed.
    /// </summary>
    private static RawEvent Progress(Guid siteId, string visitor, DateTimeOffset at, string path, int engagedMs) =>
        Page(siteId, visitor, at, path, Chrome) with
        {
            Kind = EventKind.Engagement,
            EngagedMs = engagedMs,
            ScrollDepthPercent = 18,
            HadPointerInteraction = false,
            HadKeyboardInteraction = false,
            DeclaredWebDriver = false,
        };

    /// <summary>The tracker's account of a page as it is left, carrying the whole of its time so far.</summary>
    private static RawEvent Left(Guid siteId, string visitor, DateTimeOffset at, string path, int engagedMs) =>
        Progress(siteId, visitor, at, path, engagedMs) with { Kind = EventKind.Exit };

    private static RawEvent StillReading(Guid siteId, string visitor, DateTimeOffset opened, DateTimeOffset at) =>
        Page(siteId, visitor, at, "/docs", Chrome) with
        {
            Kind = EventKind.Engagement,
            EngagedMs = (int)(at - opened).TotalMilliseconds,
            ScrollDepthPercent = 40,
            HadPointerInteraction = true,
            HadKeyboardInteraction = false,
            DeclaredWebDriver = false,
        };

    /// <summary>The same activity, arriving from an address the named company vouches for.</summary>
    private static RawEvent Confirmed(RawEvent observed, string operatorName) =>
        observed with { ConfirmedOperator = operatorName };

    /// <summary>The same activity, as observed from a particular address.</summary>
    private static RawEvent From(RawEvent observed, string ipAddress) =>
        observed with { IpAddress = ipAddress };

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
