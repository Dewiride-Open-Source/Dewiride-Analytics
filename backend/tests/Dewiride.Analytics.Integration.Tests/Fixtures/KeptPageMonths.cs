using ClickHouse.Driver;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Consolidation;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Sites;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// Keeps months of a website's busiest pages as an engine would have, and reads back what was kept.
/// </summary>
internal static class KeptPageMonths
{
    /// <summary>
    /// A month of pages as an engine kept it, judged whole under the rules running now.
    /// </summary>
    /// <param name="site">The website.</param>
    /// <param name="calendar">Its calendar.</param>
    /// <param name="month">The first of the month.</param>
    /// <param name="countedAt">When it was counted.</param>
    /// <param name="leftOut">What the paths it did not keep added up to.</param>
    /// <param name="views">Each path kept, with everybody's pages and the people's.</param>
    /// <returns>The month.</returns>
    public static ConsolidatedPageMonth Counted(
        Site site,
        ZoneCalendar calendar,
        DateOnly month,
        DateTimeOffset countedAt,
        PagesLeftOut leftOut,
        params PathMonthViews[] views)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(calendar);

        var window = new TimeRange(calendar.FirstInstantOn(month), calendar.FirstInstantOn(month.AddMonths(1)));

        return new ConsolidatedPageMonth(
            new MonthToCount(site.Id, site.TimeZoneId, month, window),
            [.. views],
            leftOut,
            RulesetVersion.Current,
            window.To,
            window.To,
            window.To,
            countedAt);
    }

    /// <summary>Keeps a month of pages directly, as an engine would have kept it.</summary>
    /// <param name="stack">The running stack.</param>
    /// <param name="month">The month.</param>
    /// <returns>A task that completes once it is kept.</returns>
    public static Task KeepAsync(AnalyticsStackFixture stack, ConsolidatedPageMonth month) =>
        stack.Services.GetRequiredService<IMonthConsolidation>().KeepPagesAsync([month], Cancellation.Token);

    /// <summary>
    /// Reads every month of pages kept for a website, each as its newest count.
    /// </summary>
    /// <param name="stack">The running stack.</param>
    /// <param name="siteId">The website.</param>
    /// <returns>The rows, oldest month first, keyed by column name.</returns>
    public static Task<List<Dictionary<string, object?>>> RowsAsync(AnalyticsStackFixture stack, Guid siteId) =>
        TelemetryStore.RowsAsync(
            stack.Services.GetRequiredService<IClickHouseClient>(),
            """
            SELECT *
            FROM consolidated_page_months
            WHERE site_id = {site_id:UUID}
            ORDER BY month, counted_at DESC
            LIMIT 1 BY month
            """,
            TelemetryStore.Bind("site_id", siteId));

    /// <summary>Counts every row of pages written for a website, before the store has replaced any.</summary>
    /// <param name="stack">The running stack.</param>
    /// <param name="siteId">The website.</param>
    /// <returns>How many rows there are.</returns>
    public static Task<ulong> WrittenAsync(AnalyticsStackFixture stack, Guid siteId) =>
        TelemetryStore.ScalarAsync<ulong>(
            stack.Services.GetRequiredService<IClickHouseClient>(),
            "SELECT count() FROM consolidated_page_months WHERE site_id = {site_id:UUID}",
            TelemetryStore.Bind("site_id", siteId));

    /// <summary>What the paths a kept row did not keep added up to.</summary>
    /// <param name="row">The row.</param>
    /// <returns>How many paths there were, and the pages delivered at them to everybody and to people.</returns>
    public static PagesLeftOut LeftOutIn(Dictionary<string, object?> row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new PagesLeftOut(
            Convert.ToInt64(row["left_out_paths"], System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToInt64(row["left_out_page_views"], System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToInt64(row["left_out_people_page_views"], System.Globalization.CultureInfo.InvariantCulture));
    }
}
