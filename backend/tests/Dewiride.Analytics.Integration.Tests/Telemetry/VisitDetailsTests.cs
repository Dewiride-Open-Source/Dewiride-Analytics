using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Classification.Sessions;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Telemetry;

/// <summary>
/// Proves that what a visit was can be asked about, and that asking gives back the visit.
/// </summary>
/// <remarks>
/// <para>
/// None of these nine facts is stored beside a verdict. They are rebuilt from activity when
/// somebody asks, and the rebuild has to arrive at the same name for a visit as the engine that
/// judged it did — from a different statement, over a different window, reached by a different
/// caller. Nothing enforces that: the two meet in a text rather than in a foreign key, so a
/// difference of one millisecond matches nothing at all and hands back an empty list with no
/// error anywhere.
/// </para>
/// <para>
/// So the property worth proving is a round trip. Every value the product offers a reader has to
/// be a value that, asked for, returns at least the visit it was taken from — which fails the
/// moment the two reconstructions disagree, a value is stored one way and compared another, or
/// the empty text stops meaning "nothing was established".
/// </para>
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class VisitDetailsTests(AnalyticsStackFixture stack)
{
    private DateTimeOffset Now => stack.Services.GetRequiredService<TimeProvider>().GetUtcNow();

    /// <summary>
    /// Far enough in the past that every visit written here has been silent for longer than the
    /// idle timeout, so a run judges them rather than waiting for them to finish.
    /// </summary>
    private DateTimeOffset Yesterday => Now.AddDays(-1);

    [Fact]
    public async Task Every_Detail_Of_A_Judged_Visit_Can_Be_Read_Back()
    {
        var site = await DescribedTraffic.ADescribedVisitAsync(stack);

        var offered = await DetailsOfAsync(site);

        offered.Devices.Should().ContainSingle().Which.Value.Should().Be(DescribedTraffic.Device);
        offered.SourceKinds.Should().ContainSingle().Which.Value.Should().Be(DescribedTraffic.SentBy);
        offered.Browsers.Should().ContainSingle().Which.Value.Should().Be(DescribedTraffic.Browser);
        offered.OperatingSystems.Should().ContainSingle().Which.Value.Should().Be(DescribedTraffic.SystemName);
        offered.Countries.Should().ContainSingle().Which.Value.Should().Be(DescribedTraffic.Country);
        offered.Towns.Should().ContainSingle().Which.Value.Should().Be(DescribedTraffic.Town);
        offered.Networks.Should().ContainSingle().Which.Value.Should().Be(DescribedTraffic.Network);
        offered.Sources.Should().ContainSingle().Which.Value.Should().Be(DescribedTraffic.Source);
        offered.EntryPages.Should().ContainSingle().Which.Value.Should().Be(DescribedTraffic.EntryPage);
    }

    /// <summary>
    /// A detail describes a visit rather than a report, so a visit that asked for three pages and
    /// reported its reading of them counts once wherever it is offered. Counted per report, a
    /// filter would promise a number nothing beneath it could show.
    /// </summary>
    [Fact]
    public async Task A_Detail_Counts_Each_Visit_Once()
    {
        var site = await DescribedTraffic.ADescribedVisitAsync(stack);

        var offered = await DetailsOfAsync(site);

        offered.Countries.Should().ContainSingle().Which.Visits.Should().Be(1);
        offered.EntryPages.Should().ContainSingle().Which.Visits.Should().Be(1);
    }

    /// <summary>
    /// The property this whole arrangement rests on. Every value the product offers is a value the
    /// list can be narrowed to, and narrowing to it finds the visit it was taken from — which is
    /// only true while the rebuild reached for the answer and the rebuild reached for the question
    /// name a visit identically.
    /// </summary>
    [Fact]
    public async Task Every_Value_Offered_Narrows_The_List_To_At_Least_One_Visit()
    {
        var site = await DescribedTraffic.ADescribedVisitAsync(stack);
        var offered = await DetailsOfAsync(site);

        var narrowings = new VisitNarrowing[]
        {
            new() { Devices = [.. offered.Devices.Select(count => count.Value)] },
            new() { SourceKinds = [.. offered.SourceKinds.Select(count => count.Value)] },
            new() { Browsers = [.. offered.Browsers.Select(count => count.Value)] },
            new() { OperatingSystems = [.. offered.OperatingSystems.Select(count => count.Value)] },
            new() { Countries = [.. offered.Countries.Select(count => count.Value)] },
            new() { Towns = [.. offered.Towns.Select(count => count.Value)] },
            new() { Networks = [.. offered.Networks.Select(count => count.Value)] },
            new() { Sources = [.. offered.Sources.Select(count => count.Value)] },
            new() { EntryPages = [.. offered.EntryPages.Select(count => count.Value)] },
        };

        foreach (var narrowing in narrowings)
        {
            var visits = await VisitsOfAsync(site, narrowing);

            visits.TotalVisits.Should().Be(1);
            visits.Visits.Should().ContainSingle();
        }
    }

    /// <summary>
    /// Every detail at once still finds the visit, which is what a reader who narrows a list twice
    /// experiences. Nine conditions that each hold separately can still fail together if one of
    /// them is compared against a different column from the one it was offered from.
    /// </summary>
    [Fact]
    public async Task Every_Detail_At_Once_Still_Finds_The_Visit()
    {
        var site = await DescribedTraffic.ADescribedVisitAsync(stack);

        var visits = await VisitsOfAsync(
            site,
            new VisitNarrowing
            {
                Devices = [DescribedTraffic.Device],
                SourceKinds = [DescribedTraffic.SentBy],
                Browsers = [DescribedTraffic.Browser],
                OperatingSystems = [DescribedTraffic.SystemName],
                Countries = [DescribedTraffic.Country],
                Towns = [DescribedTraffic.Town],
                Networks = [DescribedTraffic.Network],
                Sources = [DescribedTraffic.Source],
                EntryPages = [DescribedTraffic.EntryPage],
            });

        visits.Visits.Should().ContainSingle();
    }

    [Fact]
    public async Task A_Detail_The_Visit_Did_Not_Have_Finds_Nothing()
    {
        var site = await DescribedTraffic.ADescribedVisitAsync(stack);

        var visits = await VisitsOfAsync(site, new VisitNarrowing { Devices = [DeviceClass.Desktop] });

        visits.TotalVisits.Should().Be(0);
        visits.Visits.Should().BeEmpty();
    }

    [Fact]
    public async Task Narrowing_By_Nothing_Returns_Every_Judged_Visit()
    {
        var site = await DescribedTraffic.ADescribedVisitAsync(stack);

        var visits = await VisitsOfAsync(site, VisitNarrowing.Nothing);

        visits.Visits.Should().ContainSingle();
    }

    /// <summary>
    /// Every listed visit carries what it was, rebuilt from the activity behind its verdict and
    /// joined on the same derived identity the narrowing joins on. The ordinary list reads only the
    /// page it is showing, so this is the shape that proves the page-bounded rebuild derives the
    /// identity the engine stored: a rebuild whose window cut off the visit's first report, or
    /// whose reconciliation wrote the key differently, would match nothing and hand back eight
    /// empties with no error anywhere.
    /// </summary>
    [Fact]
    public async Task A_Listed_Visit_Says_What_It_Was()
    {
        var site = await DescribedTraffic.ADescribedVisitAsync(stack);

        var visits = await VisitsOfAsync(site, VisitNarrowing.Nothing);

        visits.Visits.Should().ContainSingle().Which.Context.Should().Be(Described());
    }

    /// <summary>
    /// A narrowed list carries the same eight facts in the same words as an unnarrowed one, taken
    /// from the rebuild it was narrowed by.
    /// </summary>
    [Fact]
    public async Task A_Narrowed_Visit_Says_What_It_Was()
    {
        var site = await DescribedTraffic.ADescribedVisitAsync(stack);

        var visits = await VisitsOfAsync(site, new VisitNarrowing { Devices = [DescribedTraffic.Device] });

        visits.Visits.Should().ContainSingle().Which.Context.Should().Be(Described());
    }

    /// <summary>
    /// A page is a slice, and the account each row carries is that row's own — whichever slice it
    /// falls on, and whether or not the list was narrowed. The rebuild behind a page reads every
    /// visit around it and the join is what hands each verdict its own account; two visits handed
    /// each other's would pass every one-visit test above.
    /// </summary>
    [Fact]
    public async Task Each_Visit_On_A_Page_Of_Several_Carries_Its_Own_Account()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var began = Yesterday;

        await DescribedTraffic.WriteAsync(
            stack,
            DescribedTraffic.Placed(DescribedTraffic.Arrived(site.Id, began, "/pricing")) with
            {
                Referrer = "https://www.bing.com/search?q=analytics",
                ReferrerDomain = "www.bing.com",
            },
            DescribedTraffic.Placed(DescribedTraffic.Arrived(site.Id, began.AddHours(1), "/docs")) with
            {
                VisitorKey = "0a1b2c3d4e5f60718293a4b5c6d7e8f9",
                CountryCode = "FR",
                City = "Lyon",
            });

        await DescribedTraffic.JudgeAsync(stack, site);

        var fromBing = Described() with { SendingSite = "Bing" };
        var straightToLyon = Described() with
        {
            SendingSite = string.Empty,
            Channel = SourceChannel.Direct,
            CountryCode = "FR",
            Town = "Lyon",
        };

        var newest = await VisitsOfAsync(site, VisitNarrowing.Nothing, limit: 1, offset: 0);
        var older = await VisitsOfAsync(site, VisitNarrowing.Nothing, limit: 1, offset: 1);
        var both = await VisitsOfAsync(site, VisitNarrowing.Nothing);
        var narrowed = await VisitsOfAsync(site, new VisitNarrowing { Towns = ["Lyon"] });

        newest.Visits.Should().ContainSingle().Which.Context.Should().Be(straightToLyon);
        older.Visits.Should().ContainSingle().Which.Context.Should().Be(fromBing);
        both.Visits.Select(visit => visit.Context).Should().Equal(straightToLyon, fromBing);
        narrowed.Visits.Should().ContainSingle().Which.Context.Should().Be(straightToLyon);
    }

    /// <summary>
    /// A verdict outlives the activity it was reached from, and the visit is still listed once
    /// that activity is gone — saying nothing about itself, in the vocabulary's own words for
    /// nothing established. This is also what proves the store hands back empty texts rather than
    /// nothing at all for a visit the rebuild could not describe.
    /// </summary>
    [Fact]
    public async Task A_Listed_Visit_Whose_Activity_Is_Gone_Says_Nothing_About_Itself()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var began = Yesterday;

        await stack.Services.GetRequiredService<IClassificationStore>().SaveAsync(
            site.Id,
            [
                new SessionJudgement(
                    new SessionEvidence
                    {
                        SessionKey = new VisitKey("2f8a1c0b4d6e7f905a1b2c3d4e5f6071", began).ToString(),
                        StartedAt = began,
                        EndedAt = began.AddMinutes(1),
                        Requests = [new ObservedRequest(began, "/", null)],
                        Surfaces = [IngestSurface.BrowserTracker],
                    },
                    ClassificationVerdict.Insufficient(RulesetVersion.Current)),
            ],
            began.AddMinutes(1),
            Cancellation.Token);

        var visits = await VisitsOfAsync(site, VisitNarrowing.Nothing);

        visits.TotalVisits.Should().Be(1);
        visits.Visits.Should().ContainSingle().Which.Context.Should().Be(VisitContext.Nothing);
    }

    /// <summary>
    /// A visitor who typed the address came from nowhere in particular, and an install behind a
    /// proxy that passes on no address places nobody. Both are answers rather than gaps, and both
    /// have to be askable — so what nothing established is offered and narrowed to as an empty
    /// value, and reads back as the vocabulary's own word for "nothing said".
    /// </summary>
    [Fact]
    public async Task The_Visits_Nothing_Was_Established_About_Can_Be_Asked_For()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        RawEvent OnFirefox(DateTimeOffset when, string path) =>
            DescribedTraffic.Arrived(site.Id, when, path) with { UserAgent = DescribedTraffic.MobileFirefox };

        await DescribedTraffic.WriteAsync(stack, OnFirefox(at, "/"), OnFirefox(at.AddMinutes(2), "/docs"));

        await DescribedTraffic.JudgeAsync(stack, site);

        var offered = await DetailsOfAsync(site);

        offered.SourceKinds.Should().ContainSingle().Which.Value.Should().Be(SourceChannel.Direct);
        offered.Countries.Should().ContainSingle().Which.Value.Should().BeEmpty();
        offered.Sources.Should().ContainSingle().Which.Value.Should().BeEmpty();

        var visits = await VisitsOfAsync(
            site,
            new VisitNarrowing { Countries = [string.Empty], SourceKinds = [SourceChannel.Direct] });

        visits.Visits.Should().ContainSingle();
    }

    /// <summary>
    /// A visit belongs to the period it began in, and the rebuild agrees with the verdict about
    /// when that was. A period opening part-way through a visit holds no part of it: read from its
    /// own start the visit's second page would look like a whole visit that began there, and a
    /// reader would be shown one visit twice under two names across two periods.
    /// </summary>
    [Fact]
    public async Task A_Visit_Belongs_To_The_Period_It_Began_In()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var began = Yesterday;

        await DescribedTraffic.WriteAsync(
            stack,
            DescribedTraffic.Arrived(site.Id, began, "/pricing"),
            DescribedTraffic.Arrived(site.Id, began.AddMinutes(10), "/docs"));

        await DescribedTraffic.JudgeAsync(stack, site);

        var acrossIt = new TimeRange(began.AddMinutes(5), Now.AddMinutes(1));

        var visits = await VisitsOfAsync(site, new VisitNarrowing { EntryPages = ["/pricing"] }, acrossIt);

        visits.Visits.Should().BeEmpty();

        var offered = await DetailsOfAsync(site, acrossIt);

        offered.EntryPages.Should().BeEmpty();
    }

    /// <summary>
    /// Only visits the engine has judged are described, so a filter never offers a value the list
    /// beneath it cannot show.
    /// </summary>
    [Fact]
    public async Task Activity_Nobody_Has_Judged_Offers_Nothing()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());

        await DescribedTraffic.WriteAsync(stack, DescribedTraffic.Arrived(site.Id, Yesterday, "/pricing"));

        var offered = await DetailsOfAsync(site);

        offered.EntryPages.Should().BeEmpty();
        offered.Devices.Should().BeEmpty();
    }

    private Task<VisitFacets> DetailsOfAsync(Site site) => DetailsOfAsync(site, Window());

    private Task<VisitFacets> DetailsOfAsync(Site site, TimeRange period) =>
        stack.Services.GetRequiredService<ITelemetryQueries>()
            .GetSiteVisitFacetsAsync(
                Scope(site),
                new SiteVisitFacetsQuery(period, IdleTimeout, site.Domain),
                Cancellation.Token);

    private Task<JudgedSessions> VisitsOfAsync(
        Site site,
        VisitNarrowing narrowing,
        int limit = 50,
        int offset = 0) =>
        VisitsOfAsync(site, narrowing, Window(), limit, offset);

    private Task<JudgedSessions> VisitsOfAsync(
        Site site,
        VisitNarrowing narrowing,
        TimeRange period,
        int limit = 50,
        int offset = 0) =>
        stack.Services.GetRequiredService<ITelemetryQueries>()
            .GetJudgedSessionsAsync(
                Scope(site),
                new JudgedSessionsQuery(period, IdleTimeout, site.Domain, limit, offset) { Narrowing = narrowing },
                Cancellation.Token);

    private TimeRange Window() => new(Now.AddDays(-2), Now.AddMinutes(1));

    /// <summary>The account the described visit gives of itself, as the opened visit gives it.</summary>
    private static VisitContext Described() =>
        new(
            DescribedTraffic.Source,
            DescribedTraffic.SentBy,
            DescribedTraffic.Country,
            DescribedTraffic.Town,
            DescribedTraffic.Network,
            DescribedTraffic.Device,
            DescribedTraffic.Browser,
            DescribedTraffic.SystemName);

    private static TimeSpan IdleTimeout => TimeSpan.FromMinutes(30);

    private static string Domain() => $"{Guid.NewGuid():n}.example.com";

    private static TenantScope Scope(Site site) =>
        new(site.Id, site.OrganizationId, SiteRole.Viewer, site.TimeZoneId);
}
