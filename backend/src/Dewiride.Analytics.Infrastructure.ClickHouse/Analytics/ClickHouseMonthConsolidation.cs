using System.Collections.Immutable;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using ClickHouse.Driver.Utility;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Consolidation;
using Dewiride.Analytics.Classification;

namespace Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;

/// <summary>
/// Counts a website's finished months and keeps them in the telemetry store.
/// </summary>
/// <remarks>
/// <para>
/// Runs over a client of its own, allowed far longer than a screen's question: a month of a busy
/// website is a month of reports read on one thread.
/// </para>
/// <para>
/// Rows go in through the binary row format, as events and verdicts do, so nothing a month carries
/// is ever parsed as SQL. The store replaces a month on its newest count, so writing one twice keeps
/// one.
/// </para>
/// <para>
/// A month's posts are kept the same way, on a row of their own. They are counted by one statement that
/// answers for every path a page was delivered at in the month, of which only the posts' rows are kept
/// (<see cref="PostViewsStatement.ReadAsync"/>), so no post's path is ever sent to the store.
/// </para>
/// <para>
/// A month's busiest pages are kept on a row of their own too. The statement that counts them ranks every
/// path in the store and answers with the ones the month keeps and what the rest added up to, so whatever
/// was asked for in a month, what comes back is bounded.
/// </para>
/// </remarks>
/// <param name="client">Telemetry store client for counting.</param>
/// <param name="origin">When this installation began keeping months.</param>
internal sealed class ClickHouseMonthConsolidation(IClickHouseClient client, KeepingOrigin origin) : IMonthConsolidation
{
    private const string TableName = "consolidated_months";

    private const string PostTableName = "consolidated_post_months";

    private const string PageTableName = "consolidated_page_months";

    /// <summary>Insert columns, in the order <see cref="ToRow(ConsolidatedMonth)"/> produces values for them.</summary>
    private static readonly string[] Columns =
    [
        "site_id",
        "month",
        "time_zone",
        "begins_at",
        "ends_at",
        "first_day",
        "days_measured",
        "page_views",
        "visitors",
        "best_day",
        "best_day_page_views",
        "best_hour_day",
        "best_hour",
        "best_hour_page_views",
        "page_views_by_weekday",
        "page_views_by_hour",
        "people_page_views",
        "people_visitors",
        "people_best_day",
        "people_best_day_page_views",
        "people_best_hour_day",
        "people_best_hour",
        "people_best_hour_page_views",
        "people_page_views_by_weekday",
        "people_page_views_by_hour",
        "verdict_categories",
        "verdict_visits",
        "verdict_page_views",
        "ruleset_major",
        "ruleset_minor",
        "ruleset_judged_to",
        "counting_rules",
        "counted_to",
        "judged_to",
        "counted_at",
    ];

    /// <summary>Insert columns of a month of posts, in the order its <see cref="ToRow(ConsolidatedPostMonth)"/> produces values for them.</summary>
    private static readonly string[] PostColumns =
    [
        "site_id",
        "month",
        "time_zone",
        "begins_at",
        "ends_at",
        "paths",
        "page_views",
        "people_page_views",
        "posts_fingerprint",
        "ruleset_major",
        "ruleset_minor",
        "ruleset_judged_to",
        "counting_rules",
        "counted_to",
        "judged_to",
        "counted_at",
    ];

    /// <summary>Insert columns of a month of pages, in the order its <see cref="ToRow(ConsolidatedPageMonth)"/> produces values for them.</summary>
    private static readonly string[] PageColumns =
    [
        "site_id",
        "month",
        "time_zone",
        "begins_at",
        "ends_at",
        "paths",
        "page_views",
        "people_page_views",
        "left_out_paths",
        "left_out_page_views",
        "left_out_people_page_views",
        "ruleset_major",
        "ruleset_minor",
        "ruleset_judged_to",
        "counting_rules",
        "counted_to",
        "judged_to",
        "counted_at",
    ];

    /// <inheritdoc />
    public async Task<MonthSurvey> SurveyAsync(
        Guid siteId,
        string timeZoneId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var months = ImmutableArray.CreateBuilder<SurveyedMonth>();
        DateOnly? firstReportDay = null;

        await using var reader = await ExecuteAsync(
                ConsolidationSqlCompiler.CompileSurvey(siteId, timeZoneId, now),
                cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            firstReportDay = KeptRows.DayOrNothing(reader, KeptRows.FirstReportDay);
            months.Add(new SurveyedMonth(
                KeptRows.Day(reader, KeptRows.Month),
                new TimeRange(reader.GetDateTimeOffset(KeptRows.Begins), reader.GetDateTimeOffset(KeptRows.Ends)),
                KeptRows.WasKept(reader) ? Described(reader) : null));
        }

        return new MonthSurvey(firstReportDay, months.DrainToImmutable())
        {
            KeepingSince = await origin.SinceAsync(cancellationToken).ConfigureAwait(false),
        };
    }

    /// <inheritdoc />
    public async Task<MonthFigures> CountAsync(
        MonthToCount month,
        Population population,
        CancellationToken cancellationToken)
    {
        long pageViews = 0;
        long visitors = 0;
        var busiestDay = new Peak<DateOnly>();
        var busiestHour = new Peak<DateTime>();
        var byWeekday = new long[MonthFigures.Weekdays];
        var byHour = new long[MonthFigures.Hours];

        await using var reader = await ExecuteAsync(
                ConsolidationSqlCompiler.CompileCount(month, population),
                cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var day = KeptRows.Day(reader, 1);
            var slot = reader.GetByte(2);
            var delivered = reader.GetInt64(3);

            switch (reader.GetString(0))
            {
                case "month":
                    pageViews = delivered;
                    visitors = reader.GetInt64(4);
                    break;

                case "day":
                    byWeekday[slot - 1] += delivered;
                    busiestDay.Offer(day, delivered);
                    break;

                default:
                    // One row per hour on the clock, so the hour the clocks go back through is offered
                    // twice, each time with its own pages.
                    byHour[slot] += delivered;
                    busiestHour.Offer(day.ToDateTime(new TimeOnly(slot, 0), DateTimeKind.Unspecified), delivered);
                    break;
            }
        }

        return new MonthFigures(
            pageViews,
            visitors,
            busiestDay.When,
            busiestDay.PageViews,
            busiestHour.When,
            busiestHour.PageViews,
            [.. byWeekday],
            [.. byHour]);
    }

    /// <inheritdoc />
    public async Task<ImmutableArray<VerdictTally>> CountVerdictsAsync(
        MonthToCount month,
        CancellationToken cancellationToken)
    {
        var tallies = ImmutableArray.CreateBuilder<VerdictTally>();

        await using var reader = await ExecuteAsync(
                ConsolidationSqlCompiler.CompileVerdicts(month),
                cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            tallies.Add(new VerdictTally(
                StoredNames.Categories[reader.GetString(0)],
                reader.GetInt64(1),
                reader.GetInt64(2)));
        }

        return tallies.DrainToImmutable();
    }

    /// <inheritdoc />
    public Task KeepAsync(IReadOnlyCollection<ConsolidatedMonth> months, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(months);

        return months.Count == 0
            ? Task.CompletedTask
            : client.InsertBinaryAsync(TableName, Columns, months.Select(ToRow), Waited(), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ImmutableArray<KeptPostMonth>> SurveyPostsAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var months = ImmutableArray.CreateBuilder<KeptPostMonth>();

        await using var reader = await ExecuteAsync(ConsolidationSqlCompiler.CompilePostSurvey(siteId), cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            months.Add(DescribedPosts(reader));
        }

        return months.DrainToImmutable();
    }

    /// <inheritdoc />
    public async Task<ImmutableArray<PathViews>> CountPostsAsync(
        MonthToCount month,
        Population population,
        ImmutableArray<string> paths,
        CancellationToken cancellationToken)
    {
        await using var reader = await ExecuteAsync(
                ConsolidationSqlCompiler.CompilePostCount(month, population),
                cancellationToken)
            .ConfigureAwait(false);

        return await PostViewsStatement.ReadAsync(reader, paths, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task KeepPostsAsync(IReadOnlyCollection<ConsolidatedPostMonth> months, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(months);

        return months.Count == 0
            ? Task.CompletedTask
            : client.InsertBinaryAsync(PostTableName, PostColumns, months.Select(ToRow), Waited(), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ImmutableArray<KeptPageMonth>> SurveyPagesAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var months = ImmutableArray.CreateBuilder<KeptPageMonth>();

        await using var reader = await ExecuteAsync(ConsolidationSqlCompiler.CompilePageSurvey(siteId), cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            months.Add(new KeptPageMonth(KeptRows.Day(reader, 0), DescribedKept(reader)));
        }

        return months.DrainToImmutable();
    }

    /// <inheritdoc />
    /// <remarks>
    /// What is kept is put in ordinal order here rather than by the store, which orders text by its bytes.
    /// </remarks>
    public async Task<CountedPages> CountPagesAsync(MonthToCount month, CancellationToken cancellationToken)
    {
        await using var reader = await ExecuteAsync(ConsolidationSqlCompiler.CompilePageCount(month), cancellationToken)
            .ConfigureAwait(false);

        // An aggregate with no grouping always produces exactly one row, including over a month that
        // holds no page at all, where it keeps none and leaves none out.
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return new CountedPages([], default);
        }

        var paths = await reader.GetFieldValueAsync<string[]>(0, cancellationToken).ConfigureAwait(false);
        var everybody = await reader.GetFieldValueAsync<long[]>(1, cancellationToken).ConfigureAwait(false);
        var people = await reader.GetFieldValueAsync<long[]>(2, cancellationToken).ConfigureAwait(false);

        return new CountedPages(
            [
                .. paths
                    .Select((path, at) => new PathMonthViews(path, everybody[at], people[at]))
                    .OrderBy(view => view.Path, StringComparer.Ordinal),
            ],
            new PagesLeftOut(reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5)));
    }

    /// <inheritdoc />
    public Task KeepPagesAsync(IReadOnlyCollection<ConsolidatedPageMonth> months, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(months);

        return months.Count == 0
            ? Task.CompletedTask
            : client.InsertBinaryAsync(PageTableName, PageColumns, months.Select(ToRow), Waited(), cancellationToken);
    }

    /// <summary>
    /// How a kept month is written: waited for, so a run that returns has kept what it counted.
    /// </summary>
    /// <remarks>
    /// The store gathers small writes from many callers before it commits them, and a write it was still
    /// holding when the process stopped would be a month nobody knows was never kept.
    /// </remarks>
    private static InsertOptions Waited() =>
        new()
        {
            CustomSettings = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["async_insert"] = 1,
                ["wait_for_async_insert"] = 1,
            },
        };

    private static object[] ToRow(ConsolidatedMonth month)
    {
        var counted = month.Month;

        return
        [
            counted.SiteId,
            Midnight(counted.Month),
            counted.TimeZoneId,
            counted.Window.From.UtcDateTime,
            counted.Window.To.UtcDateTime,
            month.FirstDay is { } first ? Midnight(first) : DBNull.Value,
            (byte)month.DaysMeasured,
            (ulong)month.Everybody.PageViews,
            (ulong)month.Everybody.Visitors,
            month.Everybody.BestDay is { } best ? Midnight(best) : DBNull.Value,
            (ulong)month.Everybody.BestDayPageViews,
            .. BestHourOf(month.Everybody),
            Unsigned(month.Everybody.ByWeekday),
            Unsigned(month.Everybody.ByHour),
            (ulong)month.People.PageViews,
            (ulong)month.People.Visitors,
            month.People.BestDay is { } peopleBest ? Midnight(peopleBest) : DBNull.Value,
            (ulong)month.People.BestDayPageViews,
            .. BestHourOf(month.People),
            Unsigned(month.People.ByWeekday),
            Unsigned(month.People.ByHour),
            month.Verdicts.Select(tally => StoredNames.CategoryNames[tally.Category]).ToArray(),
            month.Verdicts.Select(tally => (ulong)tally.Visits).ToArray(),
            month.Verdicts.Select(tally => (ulong)tally.PageViews).ToArray(),
            (ushort)month.Ruleset.Major,
            (ushort)month.Ruleset.Minor,
            month.RulesetJudgedTo.UtcDateTime,
            (ushort)month.CountingRules,
            month.CountedTo.UtcDateTime,
            month.JudgedTo.UtcDateTime,
            month.CountedAt.UtcDateTime,
        ];
    }

    private static object[] ToRow(ConsolidatedPostMonth month) =>
    [
        .. Where(month.Month),
        .. Viewed(month.Views),
        month.Fingerprint,
        .. HowCounted(month.Ruleset, month.RulesetJudgedTo, month.CountingRules, month.CountedTo, month.JudgedTo, month.CountedAt),
    ];

    private static object[] ToRow(ConsolidatedPageMonth month) =>
    [
        .. Where(month.Month),
        .. Viewed(month.Views),
        (ulong)month.LeftOut.Paths,
        (ulong)month.LeftOut.PageViews,
        (ulong)month.LeftOut.PeoplePageViews,
        .. HowCounted(month.Ruleset, month.RulesetJudgedTo, month.CountingRules, month.CountedTo, month.JudgedTo, month.CountedAt),
    ];

    /// <summary>Which website and month a row of paths is, and where that month was read.</summary>
    private static object[] Where(MonthToCount counted) =>
    [
        counted.SiteId,
        Midnight(counted.Month),
        counted.TimeZoneId,
        counted.Window.From.UtcDateTime,
        counted.Window.To.UtcDateTime,
    ];

    /// <summary>The paths of a row of paths, each with everybody's pages and the people's, as three arrays.</summary>
    private static object[] Viewed(ImmutableArray<PathMonthViews> views) =>
    [
        views.Select(view => view.Path).ToArray(),
        views.Select(view => (ulong)view.PageViews).ToArray(),
        views.Select(view => (ulong)view.PeoplePageViews).ToArray(),
    ];

    /// <summary>How a row of paths was counted, as it ends.</summary>
    private static object[] HowCounted(
        RulesetVersion ruleset,
        DateTimeOffset rulesetJudgedTo,
        int countingRules,
        DateTimeOffset countedTo,
        DateTimeOffset judgedTo,
        DateTimeOffset countedAt) =>
    [
        (ushort)ruleset.Major,
        (ushort)ruleset.Minor,
        rulesetJudgedTo.UtcDateTime,
        (ushort)countingRules,
        countedTo.UtcDateTime,
        judgedTo.UtcDateTime,
        countedAt.UtcDateTime,
    ];

    /// <summary>How a kept month was counted, from the columns the calendar reads of it.</summary>
    private static KeptMonth Described(ClickHouseDataReader reader)
    {
        var at = KeptRows.FirstKept;

        return new KeptMonth(
            reader.GetString(at),
            KeptRows.DayOrNothing(reader, at + 1),
            new RulesetVersion(reader.GetFieldValue<ushort>(at + 2), reader.GetFieldValue<ushort>(at + 3)),
            reader.GetDateTimeOffset(at + 4),
            reader.GetFieldValue<ushort>(at + 5),
            reader.GetDateTimeOffset(at + 6),
            reader.GetDateTimeOffset(at + 7),
            reader.GetDateTimeOffset(at + 8));
    }

    /// <summary>How a kept month of posts was counted, from the columns its survey reads.</summary>
    private static KeptPostMonth DescribedPosts(ClickHouseDataReader reader) =>
        new(KeptRows.Day(reader, 0), DescribedKept(reader), reader.GetString(9));

    /// <summary>
    /// How a kept month of posts or of pages was counted, from the columns every survey of them reads
    /// after the month.
    /// </summary>
    private static KeptMonth DescribedKept(ClickHouseDataReader reader) =>
        new(
            reader.GetString(1),
            null,
            new RulesetVersion(reader.GetFieldValue<ushort>(2), reader.GetFieldValue<ushort>(3)),
            reader.GetDateTimeOffset(4),
            reader.GetFieldValue<ushort>(5),
            reader.GetDateTimeOffset(6),
            reader.GetDateTimeOffset(7),
            reader.GetDateTimeOffset(8));

    /// <summary>
    /// A month's best hour as it is kept: the day it fell on and which hour of that day, on the website's
    /// clock, and the pages delivered in it; no day, hour nought and no pages where it has none.
    /// </summary>
    private static object[] BestHourOf(MonthFigures figures) =>
    [
        figures.BestHour is { } hour ? Midnight(DateOnly.FromDateTime(hour)) : DBNull.Value,
        (byte)(figures.BestHour?.Hour ?? 0),
        (ulong)figures.BestHourPageViews,
    ];

    private static DateTime Midnight(DateOnly day) => day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    private static ulong[] Unsigned(ImmutableArray<long> values) => [.. values.Select(value => (ulong)value)];

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
