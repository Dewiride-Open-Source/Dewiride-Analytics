using System.Collections.Concurrent;
using ClickHouse.Driver;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Integration.Tests.Classification;

/// <summary>
/// Proves that a crawler settled by the name its address answers to stays settled.
/// </summary>
/// <remarks>
/// <para>
/// The address an answer like that is worked out from is cleared three days after it was
/// collected, and history is judged again whenever the rules improve. The tests here settle visits
/// by name, or keep answers for them directly, and then judge or rebuild those visits through both
/// real stores, because the answer has to survive a round trip through each of them and through the
/// statement that rebuilds the visit. The first of them also does to the stores what time does to
/// them, and judges again under later rules.
/// </para>
/// <para>
/// The name server is stood in for. Nothing in this suite may reach the internet, and what is being
/// proven is what happens to an answer once it has been given, not how it is asked for.
/// </para>
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class NameConfirmationTests(AnalyticsStackFixture stack)
{
    /// <summary>A crawler whose company publishes no list of addresses, only a domain.</summary>
    private const string YandexBot = "Mozilla/5.0 (compatible; YandexBot/3.0; +http://yandex.com/bots)";

    private const string YandexAddress = "77.88.5.1";

    private const string Googlebot = "Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)";

    /// <summary>Rules later than the ones compiled into the build, as the next release's would be.</summary>
    private static readonly RulesetVersion Later = new(RulesetVersion.Current.Major + 1, 0);

    /// <summary>The present moment, taken from the host's own clock rather than the machine's.</summary>
    private DateTimeOffset Now => stack.Services.GetRequiredService<TimeProvider>().GetUtcNow();

    /// <summary>Long enough ago that every visit written here is over.</summary>
    private DateTimeOffset Yesterday => Now.AddDays(-1);

    /// <summary>
    /// The whole point. Once the address is gone there is nothing left to ask about, and without the
    /// answer kept the visit would go back to being whatever it said it was.
    /// </summary>
    [Fact]
    public async Task A_Crawler_Settled_By_Name_Stays_Confirmed_When_Judged_Again_After_Its_Address_Is_Gone()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());

        await WriteAsync(
            From(Crawl(site.Id, Yesterday, "/"), YandexAddress),
            From(Crawl(site.Id, Yesterday.AddMinutes(2), "/posts/hello"), YandexAddress));

        await JudgeAsync(site, RulesetVersion.Current, new StandInNames().Settling(YandexAddress, "Yandex"));
        await EraseAddressesAsync(site.Id);

        var later = new StandInNames();
        await JudgeAsync(site, Later, later);

        later.Asked.Should().BeEmpty();

        var visit = (await VisitsAsync(site)).Should().ContainSingle().Subject;

        visit.Verdict.RulesetVersion.Should().Be(Later);
        visit.Verdict.Category.Should().Be(TrafficCategory.KnownSearchCrawler);
        visit.Verdict.Strength.Should().Be(EvidenceStrength.Verified);
        visit.Verdict.Supporting.Single(signal => signal.Code == SignalCodes.ConfirmedCrawler)
            .Parameters["operator"].Should().Be("Yandex");
    }

    /// <summary>
    /// While the address is still kept, an answer already reached is not a reason to send it to a
    /// name server again. The address is personal data, and the question has been answered.
    /// </summary>
    [Fact]
    public async Task A_Kept_Answer_Stops_The_Address_Being_Sent_Again_While_It_Is_Still_Kept()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());

        await WriteAsync(From(Crawl(site.Id, Yesterday, "/"), YandexAddress));

        await JudgeAsync(site, RulesetVersion.Current, new StandInNames().Settling(YandexAddress, "Yandex"));

        var later = new StandInNames();
        await JudgeAsync(site, Later, later);

        later.Asked.Should().BeEmpty();
        (await VisitsAsync(site)).Should().ContainSingle()
            .Which.Verdict.Strength.Should().Be(EvidenceStrength.Verified);
    }

    /// <summary>
    /// A visit settled from a list the company publishes has the answer on its own activity, which
    /// outlives the address already. Nothing is kept beside it, and nobody is asked.
    /// </summary>
    [Fact]
    public async Task A_Visit_Settled_By_A_Published_List_Keeps_Nothing_Beside_It()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());

        await WriteAsync(
            From(Crawl(site.Id, Yesterday, "/", Googlebot), "66.249.64.9") with { ConfirmedOperator = "Google" });

        var names = new StandInNames().Settling("66.249.64.9", "Google");
        await JudgeAsync(site, RulesetVersion.Current, names);

        names.Asked.Should().BeEmpty();
        (await KeptAsync(site.Id)).Should().BeEmpty();
        (await VisitsAsync(site)).Should().ContainSingle()
            .Which.Verdict.Strength.Should().Be(EvidenceStrength.Verified);
    }

    /// <summary>
    /// Two sites can hold visits with the same name — the same visitor key, beginning at the same
    /// instant — and an answer reached on one says nothing about the other.
    /// </summary>
    [Fact]
    public async Task One_Sites_Answer_Is_Never_Read_For_Another()
    {
        var mine = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var theirs = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            From(Crawl(mine.Id, at, "/"), YandexAddress),
            Crawl(theirs.Id, at, "/"));

        await JudgeAsync(mine, RulesetVersion.Current, new StandInNames().Settling(YandexAddress, "Yandex"));
        await JudgeAsync(theirs, RulesetVersion.Current, new StandInNames());

        (await VisitsAsync(mine)).Should().ContainSingle()
            .Which.Verdict.Strength.Should().Be(EvidenceStrength.Verified);

        var unsettled = (await VisitsAsync(theirs)).Should().ContainSingle().Subject;

        unsettled.Verdict.Strength.Should().NotBe(EvidenceStrength.Verified);
        unsettled.Verdict.Supporting.Should().NotContain(signal => signal.Code == SignalCodes.ConfirmedCrawler);
    }

    /// <summary>
    /// Where a visit has been given two answers, the later one is the one it is judged on, whether
    /// or not the store has merged the two rows yet.
    /// </summary>
    [Fact]
    public async Task The_Latest_Answer_Kept_For_A_Visit_Is_The_One_Read()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(Crawl(site.Id, at, "/"));

        var key = $"crawler:{at.ToUnixTimeMilliseconds()}";
        var store = stack.Services.GetRequiredService<INameConfirmationStore>();

        await store.RecordAsync(site.Id, [new NameConfirmation(key, at, "Microsoft")], at.AddHours(1), Cancellation.Token);
        await store.RecordAsync(site.Id, [new NameConfirmation(key, at, "Yandex")], at.AddHours(2), Cancellation.Token);

        var visits = await ReconstructAsync(site);

        visits.Should().ContainSingle().Which.Evidence.ConfirmedOperator.Should().Be("Yandex");
    }

    /// <summary>
    /// The activity itself speaks first. Where a report was stamped as it arrived, against a list
    /// the company publishes about all of its machines, that is the answer the visit is judged on.
    /// </summary>
    [Fact]
    public async Task What_The_Activity_Says_Is_Read_Before_What_Was_Kept()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(Crawl(site.Id, at, "/") with { ConfirmedOperator = "Google" });

        await stack.Services.GetRequiredService<INameConfirmationStore>().RecordAsync(
            site.Id,
            [new NameConfirmation($"crawler:{at.ToUnixTimeMilliseconds()}", at, "Yandex")],
            at.AddHours(1),
            Cancellation.Token);

        var visits = await ReconstructAsync(site);

        visits.Should().ContainSingle().Which.Evidence.ConfirmedOperator.Should().Be("Google");
    }

    private static string Domain() => $"{Guid.NewGuid():n}.example.com";

    private Task WriteAsync(params RawEvent[] events) =>
        stack.Services.GetRequiredService<IEventSink>().WriteBatchAsync(events, Cancellation.Token);

    /// <summary>
    /// Clears the addresses on a site's activity, as the store itself does three days after each
    /// report arrived, and waits until the change is visible.
    /// </summary>
    /// <param name="siteId">The site.</param>
    /// <returns>A task that completes once no address is left.</returns>
    private async Task EraseAddressesAsync(Guid siteId)
    {
        await Client.ExecuteNonQueryAsync(
                """
                ALTER TABLE events
                UPDATE ip_address = ''
                WHERE site_id = {site_id:UUID}
                SETTINGS mutations_sync = 2
                """,
                TelemetryStore.Bind("site_id", siteId),
                cancellationToken: Cancellation.Token)
            .ConfigureAwait(false);

        (await TelemetryStore.ScalarAsync<ulong>(
                Client,
                "SELECT countIf(ip_address != '') FROM events WHERE site_id = {site_id:UUID}",
                TelemetryStore.Bind("site_id", siteId)))
            .Should().Be(0, "the addresses are gone before the visit is judged again");
    }

    /// <summary>Every answer kept for a site, by visit.</summary>
    private async Task<IReadOnlyList<string>> KeptAsync(Guid siteId)
    {
        var rows = await TelemetryStore.RowsAsync(
            Client,
            "SELECT session_key FROM name_confirmations WHERE site_id = {site_id:UUID}",
            TelemetryStore.Bind("site_id", siteId));

        return [.. rows.Select(row => (string)row["session_key"]!)];
    }

    /// <summary>
    /// Judges a site under the given rules with the given name server, through both real stores.
    /// </summary>
    /// <remarks>
    /// Built here rather than taken from the host, because the host's name check is switched off
    /// for the whole suite and its rules are the build's own; what these tests need is a name server
    /// that answers as told and a set of rules that comes later.
    /// </remarks>
    private async Task JudgeAsync(Site site, RulesetVersion ruleset, ICrawlerNameLookup names)
    {
        await using var scope = stack.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var classifier = new SessionClassifier(
            services.GetRequiredService<ISessionSource>(),
            services.GetRequiredService<IClassificationStore>(),
            services.GetRequiredService<INameConfirmationStore>(),
            services.GetRequiredService<IClassificationProgressStore>(),
            new TrafficClassifier(TrafficClassifier.Standard, ruleset),
            names,
            services.GetRequiredService<TimeProvider>(),
            services.GetRequiredService<IOptions<ClassificationOptions>>(),
            NullLogger<SessionClassifier>.Instance);

        await classifier.CatchUpAsync(new SiteRegistration(site.Id, site.Domain, site.CreatedAt.AddDays(-2)), Cancellation.Token)
            .ConfigureAwait(false);
    }

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

    private async Task<IReadOnlyList<JudgedSession>> VisitsAsync(Site site) =>
        (await stack.Services.GetRequiredService<ITelemetryQueries>()
            .GetJudgedSessionsAsync(
                new TenantScope(site.Id, site.OrganizationId, SiteRole.Viewer, site.TimeZoneId),
                new JudgedSessionsQuery(
                    new TimeRange(Now.AddDays(-2), Now.AddMinutes(1)),
                    TimeSpan.FromMinutes(30),
                    site.Domain,
                    50),
                Cancellation.Token)
            .ConfigureAwait(false)).Visits;

    private IClickHouseClient Client => stack.Services.GetRequiredService<IClickHouseClient>();

    /// <summary>One page fetched by a crawler, as a reporter on the site's own server sees it.</summary>
    private static RawEvent Crawl(Guid siteId, DateTimeOffset at, string path, string userAgent = YandexBot) =>
        new()
        {
            EventId = Guid.CreateVersion7(at),
            SiteId = siteId,
            Kind = EventKind.PageView,
            Surface = IngestSurface.CloudflareWorker,
            ServerTimestamp = at,
            VisitorKey = "crawler",
            Host = "example.com",
            Path = path,
            UserAgent = userAgent,
            StatusCode = 200,
        };

    /// <summary>The same activity, as observed from a particular address.</summary>
    private static RawEvent From(RawEvent observed, string ipAddress) => observed with { IpAddress = ipAddress };

    /// <summary>
    /// A name server that answers exactly as it is told to, and remembers what it was asked.
    /// </summary>
    private sealed class StandInNames : ICrawlerNameLookup
    {
        private readonly Dictionary<string, string> _settles = new(StringComparer.Ordinal);
        private readonly ConcurrentQueue<string> _asked = new();

        /// <summary>Every address it was asked about, in order.</summary>
        public IReadOnlyCollection<string> Asked => _asked;

        /// <summary>Makes it settle one address as one company's.</summary>
        public StandInNames Settling(string address, string operatorName)
        {
            _settles[address] = operatorName;

            return this;
        }

        /// <inheritdoc />
        public Task<IReadOnlyDictionary<string, string>> OperatorsOfAsync(
            IReadOnlyCollection<string> ipAddresses,
            CancellationToken cancellationToken)
        {
            foreach (var address in ipAddresses)
            {
                _asked.Enqueue(address);
            }

            IReadOnlyDictionary<string, string> settled = ipAddresses
                .Where(_settles.ContainsKey)
                .ToDictionary(address => address, address => _settles[address], StringComparer.Ordinal);

            return Task.FromResult(settled);
        }
    }
}
