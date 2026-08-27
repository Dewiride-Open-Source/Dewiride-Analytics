using System.Collections.Frozen;
using System.Collections.Immutable;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using ClickHouse.Driver.Utility;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;

/// <summary>
/// Reads the telemetry store through the compiled analytics vocabulary.
/// </summary>
/// <param name="client">Telemetry store client.</param>
internal sealed class ClickHouseTelemetryQueries(IClickHouseClient client) : ITelemetryQueries
{
    /// <inheritdoc />
    public async Task<OverviewResult> GetOverviewAsync(
        TenantScope scope,
        OverviewQuery query,
        CancellationToken cancellationToken)
    {
        await using var reader = await ExecuteAsync(
                AnalyticsSqlCompiler.Compile(scope, query),
                cancellationToken)
            .ConfigureAwait(false);

        // An aggregate with no grouping always produces exactly one row, including over an empty
        // window, where it produces zeroes.
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new OverviewResult(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2))
            : default;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(
        TenantScope scope,
        TimeSeriesQuery query,
        CancellationToken cancellationToken)
    {
        var points = new List<TimeSeriesPoint>();

        await using var reader = await ExecuteAsync(
                AnalyticsSqlCompiler.Compile(scope, query),
                cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            points.Add(new TimeSeriesPoint(reader.GetDateTimeOffset(0), reader.GetInt64(1)));
        }

        return points;
    }

    /// <inheritdoc />
    public async Task<SitePages> GetSitePagesAsync(
        TenantScope scope,
        SitePagesQuery query,
        CancellationToken cancellationToken)
    {
        var pages = ImmutableArray.CreateBuilder<SitePageRow>();
        var totalPageViews = 0L;
        var totalPaths = 0L;
        var mostPageViews = 0L;

        await using var reader = await ExecuteAsync(
                AnalyticsSqlCompiler.Compile(scope, query),
                cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            pages.Add(new SitePageRow(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2)));

            // The three window figures are the same on every row, and there is nowhere else to
            // read them from. A slice with nothing in it — an empty window, or one asked for past
            // the end of the list — returns no rows, and nought is the honest answer for all
            // three rather than a figure invented to fill them.
            totalPageViews = reader.GetInt64(3);
            totalPaths = reader.GetInt64(4);
            mostPageViews = reader.GetInt64(5);
        }

        return new SitePages(totalPageViews, totalPaths, mostPageViews, pages.DrainToImmutable());
    }

    /// <inheritdoc />
    public async Task<SiteActions> GetSiteActionsAsync(
        TenantScope scope,
        SiteActionsQuery query,
        CancellationToken cancellationToken)
    {
        var controls = ImmutableArray.CreateBuilder<SiteActionRow>();
        var totalPresses = 0L;
        var totalControls = 0L;
        var mostPresses = 0L;

        await using var reader = await ExecuteAsync(
                AnalyticsSqlCompiler.Compile(scope, query),
                cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // The statement reads the kind out as text, so anything this table does not hold is a
            // control described in terms this product does not recognise.
            controls.Add(new SiteActionRow(
                reader.GetString(0),
                StoredNames.ControlKinds.TryGetValue(reader.GetString(1), out var control)
                    ? control
                    : ControlKind.Unknown,
                reader.GetInt64(2),
                reader.GetInt64(3)));

            // The same on every row, and nowhere else to read them from. A slice with nothing in
            // it returns no rows at all, and nought is the honest answer for all three.
            totalPresses = reader.GetInt64(4);
            totalControls = reader.GetInt64(5);
            mostPresses = reader.GetInt64(6);
        }

        return new SiteActions(totalPresses, totalControls, mostPresses, controls.DrainToImmutable());
    }

    /// <inheritdoc />
    public async Task<SiteLocations> GetSiteLocationsAsync(
        TenantScope scope,
        SiteLocationsQuery query,
        CancellationToken cancellationToken)
    {
        var places = ImmutableArray.CreateBuilder<SiteLocationRow>();
        var totalVisitors = 0L;
        var totalPlaces = 0L;
        var mostVisitors = 0L;

        await using var reader = await ExecuteAsync(
                AnalyticsSqlCompiler.Compile(scope, query),
                cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            places.Add(new SiteLocationRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetInt64(3)));

            // The same on every row, and nowhere else to read them from. A slice with nothing in
            // it returns no rows at all, and nought is the honest answer for all three.
            totalVisitors = reader.GetInt64(4);
            totalPlaces = reader.GetInt64(5);
            mostVisitors = reader.GetInt64(6);
        }

        return new SiteLocations(totalVisitors, totalPlaces, mostVisitors, places.DrainToImmutable());
    }

    /// <inheritdoc />
    public async Task<SiteSources> GetSiteSourcesAsync(
        TenantScope scope,
        SiteSourcesQuery query,
        CancellationToken cancellationToken)
    {
        var sources = ImmutableArray.CreateBuilder<SiteSourceRow>();
        var totalVisitors = 0L;
        var totalSources = 0L;
        var mostVisitors = 0L;

        await using var reader = await ExecuteAsync(
                AnalyticsSqlCompiler.Compile(scope, query),
                cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            sources.Add(new SiteSourceRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetInt64(3)));

            // The same on every row, and nowhere else to read them from. A slice with nothing in
            // it returns no rows at all, and nought is the honest answer for all three.
            totalVisitors = reader.GetInt64(4);
            totalSources = reader.GetInt64(5);
            mostVisitors = reader.GetInt64(6);
        }

        return new SiteSources(totalVisitors, totalSources, mostVisitors, sources.DrainToImmutable());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SiteDeviceKindRow>> GetSiteDeviceKindsAsync(
        TenantScope scope,
        SiteDeviceKindsQuery query,
        CancellationToken cancellationToken)
    {
        var devices = new List<SiteDeviceKindRow>();

        await using var reader = await ExecuteAsync(
                AnalyticsSqlCompiler.Compile(scope, query),
                cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // The statement reads the kind out as text and leaves it empty where nothing was
            // established, so anything this table does not hold is the unresolved group.
            devices.Add(new SiteDeviceKindRow(
                StoredNames.DeviceClasses.TryGetValue(reader.GetString(0), out var device)
                    ? device
                    : DeviceClass.Unknown,
                reader.GetInt64(1),
                reader.GetInt64(2)));
        }

        return devices;
    }

    /// <inheritdoc />
    public async Task<SiteSoftware> GetSiteSoftwareAsync(
        TenantScope scope,
        SiteSoftwareQuery query,
        CancellationToken cancellationToken)
    {
        var names = ImmutableArray.CreateBuilder<SiteSoftwareRow>();
        var totalVisitors = 0L;
        var totalNames = 0L;
        var mostVisitors = 0L;

        await using var reader = await ExecuteAsync(
                AnalyticsSqlCompiler.Compile(scope, query),
                cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            names.Add(new SiteSoftwareRow(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2)));

            // The same on every row, and nowhere else to read them from. A slice with nothing in
            // it returns no rows at all, and nought is the honest answer for all three.
            totalVisitors = reader.GetInt64(3);
            totalNames = reader.GetInt64(4);
            mostVisitors = reader.GetInt64(5);
        }

        return new SiteSoftware(totalVisitors, totalNames, mostVisitors, names.DrainToImmutable());
    }

    /// <inheritdoc />
    public async Task<SiteEngagement> GetSiteEngagementAsync(
        TenantScope scope,
        SiteEngagementQuery query,
        CancellationToken cancellationToken)
    {
        await using var reader = await ExecuteAsync(
                AnalyticsSqlCompiler.Compile(scope, query),
                cancellationToken)
            .ConfigureAwait(false);

        // An aggregate with no grouping always produces exactly one row, including over an empty
        // window, where it produces zeroes.
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new SiteEngagement(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt32(2),
                reader.GetInt64(3),
                new ScrollReach(
                    reader.GetInt64(4),
                    reader.GetInt64(5),
                    reader.GetInt64(6),
                    reader.GetInt64(7)))
            : new SiteEngagement(0, 0, 0, 0, default);
    }

    /// <inheritdoc />
    public async Task<SitePageEngagement> GetSitePageEngagementAsync(
        TenantScope scope,
        SitePageEngagementQuery query,
        CancellationToken cancellationToken)
    {
        var pages = ImmutableArray.CreateBuilder<SitePageEngagementRow>();
        var totalPages = 0L;
        var longestMedianEngagedMs = 0;

        await using var reader = await ExecuteAsync(
                AnalyticsSqlCompiler.Compile(scope, query),
                cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            pages.Add(new SitePageEngagementRow(
                reader.GetString(0),
                reader.GetInt64(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt64(4)));

            // The same on every row, and nowhere else to read them from. A slice with nothing in
            // it returns no rows at all, and nought is the honest answer for both.
            totalPages = reader.GetInt64(5);
            longestMedianEngagedMs = reader.GetInt32(6);
        }

        return new SitePageEngagement(totalPages, longestMedianEngagedMs, pages.DrainToImmutable());
    }

    /// <inheritdoc />
    public async Task<SiteVisitShape> GetSiteVisitShapeAsync(
        TenantScope scope,
        SiteVisitShapeQuery query,
        CancellationToken cancellationToken)
    {
        await using var reader = await ExecuteAsync(
                AnalyticsSqlCompiler.Compile(scope, query),
                cancellationToken)
            .ConfigureAwait(false);

        // An aggregate with no grouping always produces exactly one row, including over a window
        // holding no visits at all, where it produces zeroes.
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new SiteVisitShape(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2))
            : default;
    }

    /// <inheritdoc />
    public async Task<SiteVisitFlow> GetSiteVisitFlowAsync(
        TenantScope scope,
        SiteVisitFlowQuery query,
        CancellationToken cancellationToken)
    {
        var pages = ImmutableArray.CreateBuilder<SiteVisitFlowRow>();
        var totalVisits = 0L;
        var totalPaths = 0L;
        var mostVisits = 0L;

        await using var reader = await ExecuteAsync(
                AnalyticsSqlCompiler.Compile(scope, query),
                cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            pages.Add(new SiteVisitFlowRow(reader.GetString(0), reader.GetInt64(1)));

            // The same on every row, and nowhere else to read them from. A slice with nothing in
            // it returns no rows at all, and nought is the honest answer for all three.
            totalVisits = reader.GetInt64(2);
            totalPaths = reader.GetInt64(3);
            mostVisits = reader.GetInt64(4);
        }

        return new SiteVisitFlow(totalVisits, totalPaths, mostVisits, pages.DrainToImmutable());
    }

    /// <inheritdoc />
    public async Task<VisitJourney> GetSiteVisitJourneyAsync(
        TenantScope scope,
        SiteVisitJourneyQuery query,
        CancellationToken cancellationToken)
    {
        var steps = ImmutableArray.CreateBuilder<VisitStep>();
        var context = VisitContext.Nothing;

        await using var reader = await ExecuteAsync(
                AnalyticsSqlCompiler.Compile(scope, query),
                cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // The statement lays arrivals and presses end to end in one ordered list and marks
            // which each row is. A press carries no reading and an arrival carries no control, so
            // every row is read for the half of the shape that belongs to it.
            steps.Add(new VisitStep(
                reader.GetDateTimeOffset(0),
                reader.GetString(2),
                Observed(reader.GetInt16(3)),
                Observed(reader.GetInt32(4)),
                Observed(reader.GetInt16(5)),
                reader.GetByte(1) == 1 ? Operated(reader) : null));

            // Settled over the whole visit and repeated on every row, so it is read once and the
            // rows after the first say the same thing. A visit with no steps says nothing, which
            // is what an identity naming no visit here answers with.
            context = Established(reader);
        }

        return new VisitJourney(context, steps.DrainToImmutable());
    }

    /// <summary>
    /// Reads what could be established about the visitor behind a visit.
    /// </summary>
    /// <remarks>
    /// The kind of source and the kind of device both arrive as text, so anything the vocabulary
    /// does not hold reads as unrecognised rather than as a failure — on the same terms as the
    /// control a press was on.
    /// </remarks>
    /// <param name="reader">The open row.</param>
    /// <returns>The account.</returns>
    private static VisitContext Established(ClickHouseDataReader reader) =>
        new(
            reader.GetString(10),
            AsSourceKind(reader.GetString(11)),
            reader.GetString(12),
            reader.GetString(13),
            reader.GetString(14),
            AsDevice(reader.GetString(15)),
            reader.GetString(16),
            reader.GetString(17));

    /// <summary>
    /// Turns the statement's "not observed" back into nothing.
    /// </summary>
    /// <remarks>
    /// The statement carries what it could not measure as minus one rather than as nothing, because
    /// the store's counting functions refuse a condition that might be nothing. Every figure it
    /// applies to is one no surface can report as negative, so the two states stay distinct all the
    /// way from the column to the screen.
    /// </remarks>
    private static int? Observed(int value) => value < 0 ? null : value;

    /// <summary>
    /// Reads the control a press was on.
    /// </summary>
    /// <remarks>
    /// The kind and the sort of place it pointed at both arrive as text, so anything the stored
    /// vocabulary does not hold reads as unrecognised rather than as a failure. A row written by a
    /// later release of this product must not stop an earlier one from showing the visit.
    /// </remarks>
    /// <param name="reader">The open row.</param>
    /// <returns>What was operated.</returns>
    private static VisitPress Operated(ClickHouseDataReader reader) =>
        new(
            reader.GetString(6),
            StoredNames.ControlKinds.TryGetValue(reader.GetString(7), out var control)
                ? control
                : ControlKind.Unknown,
            NothingIfEmpty(reader.GetString(8)),
            StoredNames.TargetKinds.TryGetValue(reader.GetString(9), out var target)
                ? target
                : TargetKind.None);

    /// <summary>
    /// Renders a column the store holds as empty rather than as absent.
    /// </summary>
    /// <param name="value">The text.</param>
    /// <returns>The text, or nothing where there was none.</returns>
    private static string? NothingIfEmpty(string value) => value.Length == 0 ? null : value;

    /// <inheritdoc />
    public async Task<IReadOnlyList<TrafficBreakdownRow>> GetTrafficBreakdownAsync(
        TenantScope scope,
        TrafficBreakdownQuery query,
        CancellationToken cancellationToken)
    {
        var groups = new List<TrafficBreakdownRow>();

        await using var reader = await ExecuteAsync(
                AnalyticsSqlCompiler.Compile(scope, query),
                cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            groups.Add(new TrafficBreakdownRow(
                StoredNames.Categories[reader.GetString(0)],
                StoredNames.Strengths[reader.GetString(1)],
                reader.GetInt64(2),
                reader.GetInt64(3)));
        }

        return groups;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The statement returns one row per category and bucket, ordered by both and filled so that
    /// every category covers the whole window. That is what lets the rows be read as runs: a change
    /// of category closes the run before it, and the bucket list is taken from the first run alone
    /// because every later one repeats it exactly.
    /// </remarks>
    public async Task<TrafficSeries> GetTrafficSeriesAsync(
        TenantScope scope,
        TrafficSeriesQuery query,
        CancellationToken cancellationToken)
    {
        var buckets = ImmutableArray.CreateBuilder<DateTimeOffset>();
        var groups = ImmutableArray.CreateBuilder<TrafficSeriesGroup>();
        var sessions = ImmutableArray.CreateBuilder<long>();
        var pageViews = ImmutableArray.CreateBuilder<long>();
        TrafficCategory? gathering = null;
        var firstRun = true;

        await using var reader = await ExecuteAsync(
                AnalyticsSqlCompiler.Compile(scope, query),
                cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var category = StoredNames.Categories[reader.GetString(0)];

            if (gathering is { } previous && previous != category)
            {
                Keep(groups, previous, sessions, pageViews);
                firstRun = false;
            }

            if (firstRun)
            {
                buckets.Add(reader.GetDateTimeOffset(1));
            }

            gathering = category;
            sessions.Add(reader.GetInt64(2));
            pageViews.Add(reader.GetInt64(3));
        }

        if (gathering is { } last)
        {
            Keep(groups, last, sessions, pageViews);
        }

        return new TrafficSeries(buckets.DrainToImmutable(), groups.DrainToImmutable());

        // A window holding nothing at all still comes back filled: with no category to group by,
        // the store invents one run of zeroes under the first name in its own enumeration. A
        // category that genuinely occurred has a count somewhere in it, so a run that is zero the
        // whole way along is that invention and never a real one.
        static void Keep(
            ImmutableArray<TrafficSeriesGroup>.Builder groups,
            TrafficCategory category,
            ImmutableArray<long>.Builder sessions,
            ImmutableArray<long>.Builder pageViews)
        {
            var counted = sessions.DrainToImmutable();
            var pages = pageViews.DrainToImmutable();

            if (counted.Any(count => count > 0))
            {
                groups.Add(new TrafficSeriesGroup(category, counted, pages));
            }
        }
    }

    /// <inheritdoc />
    public async Task<JudgedSessions> GetJudgedSessionsAsync(
        TenantScope scope,
        JudgedSessionsQuery query,
        CancellationToken cancellationToken)
    {
        var visits = ImmutableArray.CreateBuilder<JudgedSession>();
        var totalVisits = 0L;

        await using var reader = await ExecuteAsync(
                AnalyticsSqlCompiler.Compile(scope, query),
                cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            visits.Add(ToJudgedSession(reader));

            // The same on every row, and there is nowhere else to read it from. A slice with
            // nothing in it — an empty window, or one asked for past the end of the list —
            // returns no rows at all, and nought is the honest answer rather than a figure
            // invented to fill it.
            totalVisits = reader.GetInt64(TotalVisitsColumn);
        }

        return new JudgedSessions(totalVisits, visits.DrainToImmutable());
    }

    /// <summary>
    /// Where the whole-window count sits on a judged-visit row.
    /// </summary>
    /// <remarks>
    /// Last, after the fifteen columns a visit is built from, so adding it left every index the
    /// visit itself is read from where it was.
    /// </remarks>
    private const int TotalVisitsColumn = 15;

    private static JudgedSession ToJudgedSession(ClickHouseDataReader reader)
    {
        var evidence = ToEvidence(reader);

        return new JudgedSession
        {
            SessionKey = reader.GetString(0),
            StartedAt = reader.GetDateTimeOffset(1),
            EndedAt = reader.GetDateTimeOffset(2),
            PageCount = (int)reader.GetFieldValue<uint>(3),
            Surfaces =
            [
                .. reader.GetFieldValue<string[]>(4).Select(name =>
                    StoredNames.Surfaces.TryGetValue(name, out var surface) ? surface : IngestSurface.Unknown),
            ],
            Verdict = new ClassificationVerdict
            {
                Category = StoredNames.Categories[reader.GetString(5)],
                Strength = StoredNames.Strengths[reader.GetString(6)],
                IsProvisional = reader.GetBoolean(7),
                RulesetVersion = new RulesetVersion(
                    reader.GetFieldValue<ushort>(8),
                    reader.GetFieldValue<ushort>(9)),
                Supporting = [.. evidence.Where(entry => entry.Supporting).Select(entry => entry.Signal)],
                Contradicting = [.. evidence.Where(entry => !entry.Supporting).Select(entry => entry.Signal)],
            },
        };
    }

    /// <summary>
    /// Rebuilds the evidence list from the parallel arrays it is stored as.
    /// </summary>
    /// <remarks>
    /// Read back as signals rather than as sentences, because the sentence is produced from the
    /// message catalogue in the reader's language. A stored English string could not be shown to
    /// somebody reading in another one, and could not be corrected without rewriting history.
    /// </remarks>
    private static ImmutableArray<(Signal Signal, bool Supporting)> ToEvidence(ClickHouseDataReader reader)
    {
        var codes = reader.GetFieldValue<string[]>(10);
        var directions = reader.GetFieldValue<string[]>(11);
        var weights = reader.GetFieldValue<byte[]>(12);
        var supporting = reader.GetFieldValue<bool[]>(13);
        var parameters = reader.GetFieldValue<Dictionary<string, string>[]>(14);

        var evidence = ImmutableArray.CreateBuilder<(Signal, bool)>(codes.Length);

        for (var index = 0; index < codes.Length; index++)
        {
            evidence.Add((
                new Signal
                {
                    Code = codes[index],
                    Direction = StoredNames.Directions[directions[index]],
                    Weight = weights[index],
                    Parameters = parameters[index].ToFrozenDictionary(StringComparer.Ordinal),
                },
                supporting[index]));
        }

        return evidence.DrainToImmutable();
    }

    /// <inheritdoc />
    public async Task<VisitFacets> GetSiteVisitFacetsAsync(
        TenantScope scope,
        SiteVisitFacetsQuery query,
        CancellationToken cancellationToken)
    {
        var rows = new List<DetailRow>();

        await using var reader = await ExecuteAsync(
                AnalyticsSqlCompiler.Compile(scope, query),
                cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new DetailRow(reader.GetString(0), reader.GetString(1), reader.GetInt64(2)));
        }

        return Sorted(rows);
    }

    /// <summary>
    /// Sorts the answer's rows back into a list per detail.
    /// </summary>
    /// <remarks>
    /// The statement reports all nine details down one column so that they can be counted in a
    /// single pass over the rebuild, and this is the other half of that arrangement. Every list
    /// keeps the order the statement put the rows in, which is commonest first. A row naming a
    /// detail this build does not know belongs to no list and is left out, on the same terms as
    /// every other value read back from the store: a row written by a later release must not stop
    /// an earlier one showing what it does understand.
    /// </remarks>
    /// <param name="rows">Every row the answer held.</param>
    /// <returns>What each detail offered.</returns>
    private static VisitFacets Sorted(List<DetailRow> rows) =>
        new()
        {
            Devices =
            [
                .. Of(rows, JudgedVisitDetails.Device)
                    .Select(row => new VisitDetailCount<DeviceClass>(AsDevice(row.Value), row.Visits)),
            ],
            SourceKinds =
            [
                .. Of(rows, JudgedVisitDetails.SourceKind)
                    .Select(row => new VisitDetailCount<SourceChannel>(AsSourceKind(row.Value), row.Visits)),
            ],
            Browsers = AsText(rows, JudgedVisitDetails.Browser),
            OperatingSystems = AsText(rows, JudgedVisitDetails.System),
            Countries = AsText(rows, JudgedVisitDetails.Country),
            Towns = AsText(rows, JudgedVisitDetails.Town),
            Networks = AsText(rows, JudgedVisitDetails.Network),
            Sources = AsText(rows, JudgedVisitDetails.Source),
            EntryPages = AsText(rows, JudgedVisitDetails.EntryPage),
        };

    /// <summary>Every row belonging to one detail, in the order the statement returned them.</summary>
    /// <param name="rows">Every row the answer held.</param>
    /// <param name="detail">Which detail.</param>
    /// <returns>Its rows.</returns>
    private static IEnumerable<DetailRow> Of(List<DetailRow> rows, string detail) =>
        rows.Where(row => string.Equals(row.Detail, detail, StringComparison.Ordinal));

    /// <summary>
    /// One detail's values, kept as the store spells them.
    /// </summary>
    /// <remarks>
    /// Seven of the nine are open sets — a browser's name, a town, a page's address — so there is
    /// no vocabulary to translate them into and the empty text stays a value, meaning the visits
    /// nothing was established about.
    /// </remarks>
    /// <param name="rows">Every row the answer held.</param>
    /// <param name="detail">Which detail.</param>
    /// <returns>Its values, commonest first.</returns>
    private static ImmutableArray<VisitDetailCount<string>> AsText(List<DetailRow> rows, string detail) =>
        [.. Of(rows, detail).Select(row => new VisitDetailCount<string>(row.Value, row.Visits))];

    /// <summary>
    /// Reads a kind of device back.
    /// </summary>
    /// <remarks>
    /// Anything the vocabulary does not hold reads as unrecognised rather than as a failure, and
    /// what nothing established is stored as an empty text — so both arrive here as the same
    /// answer, which is the honest one.
    /// </remarks>
    /// <param name="stored">What the store holds.</param>
    /// <returns>The kind of device.</returns>
    private static DeviceClass AsDevice(string stored) =>
        StoredNames.DeviceClasses.TryGetValue(stored, out var device) ? device : DeviceClass.Unknown;

    /// <summary>
    /// Reads a kind of source back.
    /// </summary>
    /// <remarks>
    /// On the same terms as a kind of device. A visit nothing sent is stored as an empty text and
    /// reads as direct, which is not "nobody sent them" but "nothing said who did".
    /// </remarks>
    /// <param name="stored">What the store holds.</param>
    /// <returns>The kind of source.</returns>
    private static SourceChannel AsSourceKind(string stored) =>
        TrafficSources.Kinds.TryGetValue(stored, out var kind) ? kind : SourceChannel.Direct;

    /// <summary>
    /// One row of a facets answer, before it is sorted into the detail it belongs to.
    /// </summary>
    /// <param name="Detail">Which detail the value belongs to.</param>
    /// <param name="Value">The value, as the store holds it.</param>
    /// <param name="Visits">How many of the window's judged visits held it.</param>
    private readonly record struct DetailRow(string Detail, string Value, long Visits);

    private async Task<ClickHouseDataReader> ExecuteAsync(
        CompiledStatement statement,
        CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();

        foreach (var parameter in statement.Parameters)
        {
            parameters.AddParameter(parameter.Name, parameter.Value);
        }

        return await client.ExecuteReaderAsync(statement.Sql, parameters, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}
