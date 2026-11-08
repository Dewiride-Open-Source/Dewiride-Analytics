using System.Collections.Immutable;
using ClickHouse.Driver;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Consolidation;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Posts;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// Lists a website's posts as a reading of its sitemap would have, keeps months of their views as an
/// engine would have, and reads back what was kept.
/// </summary>
internal static class KeptPosts
{
    /// <summary>
    /// A post the website's sitemap lists, in the part recognised as holding its posts.
    /// </summary>
    /// <param name="siteId">The website.</param>
    /// <param name="path">Its path.</param>
    /// <param name="listedAt">When a sitemap first listed it.</param>
    /// <param name="publishedAt">When its page says it was published.</param>
    /// <param name="landed">The path its page answered at, where it was redirected.</param>
    /// <param name="title">What its page says it is called.</param>
    /// <returns>The row, not yet saved.</returns>
    public static SitePost Post(
        Guid siteId,
        string path,
        DateTimeOffset listedAt,
        DateTimeOffset? publishedAt = null,
        string? landed = null,
        string? title = null) =>
        new()
        {
            SiteId = siteId,
            Path = path,
            Address = $"https://example.com{path}",
            LandedPath = landed,
            ListedInPostsPart = true,
            FirstListedAt = listedAt,
            Title = title,
            PublishedAt = publishedAt,
        };

    /// <summary>
    /// A page the website's sitemap lists that is not a post: listed outside its posts, and saying nothing
    /// of itself.
    /// </summary>
    /// <param name="siteId">The website.</param>
    /// <param name="path">Its path.</param>
    /// <param name="listedAt">When a sitemap first listed it.</param>
    /// <returns>The row, not yet saved.</returns>
    public static SitePost NotAPost(Guid siteId, string path, DateTimeOffset listedAt) =>
        new()
        {
            SiteId = siteId,
            Path = path,
            Address = $"https://example.com{path}",
            ListedInPostsPart = false,
            FirstListedAt = listedAt,
        };

    /// <summary>A post a complete reading of the website's sitemap found gone.</summary>
    /// <param name="siteId">The website.</param>
    /// <param name="path">Its path.</param>
    /// <param name="listedAt">When a sitemap first listed it, and when it was found gone.</param>
    /// <returns>The row, not yet saved.</returns>
    public static SitePost Gone(Guid siteId, string path, DateTimeOffset listedAt) =>
        new()
        {
            SiteId = siteId,
            Path = path,
            Address = $"https://example.com{path}",
            ListedInPostsPart = true,
            FirstListedAt = listedAt,
            UnlistedAt = listedAt,
        };

    /// <summary>
    /// A month of posts as an engine kept it, judged whole under the rules running now.
    /// </summary>
    /// <param name="site">The website.</param>
    /// <param name="calendar">Its calendar.</param>
    /// <param name="month">The first of the month.</param>
    /// <param name="fingerprint">Which list of posts it was counted for.</param>
    /// <param name="countedAt">When it was counted.</param>
    /// <param name="views">Each path read, with everybody's pages and the people's.</param>
    /// <returns>The month.</returns>
    public static ConsolidatedPostMonth Counted(
        Site site,
        ZoneCalendar calendar,
        DateOnly month,
        string fingerprint,
        DateTimeOffset countedAt,
        params PostMonthViews[] views)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(calendar);

        var window = new TimeRange(calendar.FirstInstantOn(month), calendar.FirstInstantOn(month.AddMonths(1)));

        return new ConsolidatedPostMonth(
            new MonthToCount(site.Id, site.TimeZoneId, month, window),
            [.. views],
            fingerprint,
            RulesetVersion.Current,
            window.To,
            window.To,
            window.To,
            countedAt);
    }

    /// <summary>Keeps a month of posts directly, as an engine would have kept it.</summary>
    /// <param name="stack">The running stack.</param>
    /// <param name="month">The month.</param>
    /// <returns>A task that completes once it is kept.</returns>
    public static Task KeepAsync(AnalyticsStackFixture stack, ConsolidatedPostMonth month) =>
        stack.Services.GetRequiredService<IMonthConsolidation>().KeepPostsAsync([month], Cancellation.Token);

    /// <summary>
    /// Reads every month of posts kept for a website, each as its newest count.
    /// </summary>
    /// <param name="stack">The running stack.</param>
    /// <param name="siteId">The website.</param>
    /// <returns>The rows, oldest month first, keyed by column name.</returns>
    public static Task<List<Dictionary<string, object?>>> RowsAsync(AnalyticsStackFixture stack, Guid siteId) =>
        TelemetryStore.RowsAsync(
            stack.Services.GetRequiredService<IClickHouseClient>(),
            """
            SELECT *
            FROM consolidated_post_months
            WHERE site_id = {site_id:UUID}
            ORDER BY month, counted_at DESC
            LIMIT 1 BY month
            """,
            TelemetryStore.Bind("site_id", siteId));

    /// <summary>Counts every row of posts written for a website, before the store has replaced any.</summary>
    /// <param name="stack">The running stack.</param>
    /// <param name="siteId">The website.</param>
    /// <returns>How many rows there are.</returns>
    public static Task<ulong> WrittenAsync(AnalyticsStackFixture stack, Guid siteId) =>
        TelemetryStore.ScalarAsync<ulong>(
            stack.Services.GetRequiredService<IClickHouseClient>(),
            "SELECT count() FROM consolidated_post_months WHERE site_id = {site_id:UUID}",
            TelemetryStore.Bind("site_id", siteId));

    /// <summary>The pages a kept row holds, by path, for everybody or for the people.</summary>
    /// <param name="row">The row.</param>
    /// <param name="column">Which counts to read: <c>page_views</c> or <c>people_page_views</c>.</param>
    /// <returns>The counts, by path.</returns>
    public static ImmutableDictionary<string, long> ViewsIn(Dictionary<string, object?> row, string column)
    {
        ArgumentNullException.ThrowIfNull(row);

        var paths = (string[])row["paths"]!;
        var counts = ((System.Collections.IEnumerable)row[column]!).Cast<object>()
            .Select(count => Convert.ToInt64(count, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

        return paths.Select((path, index) => (path, counts[index]))
            .ToImmutableDictionary(pair => pair.path, pair => pair.Item2, StringComparer.Ordinal);
    }
}
