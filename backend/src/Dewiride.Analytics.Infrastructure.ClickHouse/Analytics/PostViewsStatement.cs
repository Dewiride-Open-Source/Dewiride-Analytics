using System.Collections.Immutable;
using ClickHouse.Driver.ADO.Readers;
using Dewiride.Analytics.Application.Analytics;

namespace Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;

/// <summary>
/// Writes the count of the pages delivered at every path of a website over a window — the arithmetic
/// of the dashboard's list of pages — and reads a website's posts out of its answer.
/// </summary>
/// <remarks>
/// <para>
/// Written once for every compiler that counts a website's posts or its pages: the one that counts a
/// month of them for keeping, and the one that counts them from the reports for whoever is reading. So a
/// post's or a page's views kept for a month and the same month's views counted from its reports are one
/// figure, and each is that path's row on the list of pages for the same stretch.
/// </para>
/// <para>
/// The statement is the list of pages whole: every path a page was delivered at, with no slice taken and
/// nothing measured across the rows. The two halves of the measurement are reconciled across the whole
/// window as the list reconciles them, so a server's report of a page is still told whose it was by the
/// browser that echoed another page of the same visit. Its two parts are written apart as well —
/// <see cref="Reported"/>, which settles who each report was about, and <see cref="ByPath"/>, which
/// counts the pages delivered at each path from that — for the statements that read the count beside
/// something else.
/// </para>
/// <para>
/// A post's paths come from a website's sitemap, which whoever runs the website writes. None of them is
/// sent to the store, in the statement or bound to it: the posts are kept from the answer as it is read
/// (<see cref="ReadAsync"/>). So one statement counts however many posts a website lists, however their
/// paths are spelled, and costs what the list of pages for the same window costs.
/// </para>
/// </remarks>
internal static class PostViewsStatement
{
    /// <summary>
    /// Writes the statement for whoever it is asked about.
    /// </summary>
    /// <param name="population">Who the pages were delivered to.</param>
    /// <returns>
    /// The statement. Each row is a path a page was delivered at, and how many, in no particular order.
    /// It binds <c>site_id</c>, <c>from_ms</c>, <c>to_ms</c> and, asked about people,
    /// <c>longest_visit_seconds</c>.
    /// </returns>
    public static string Text(Population population) =>
        $"""
        {Reported(population)}
        {ByPath(JudgedPeople.Among(ReconciledEvents.Identified, population).Source, 0)}
        HAVING page_views > 0
        """;

    /// <summary>
    /// Writes what every count of a window's pages reads from: the window's reports, with who each was
    /// about settled and, about people, the verdicts on people applied.
    /// </summary>
    /// <remarks>
    /// The common table expressions alone, ending in <see cref="ReconciledEvents.Identified"/> for
    /// everybody and in <see cref="JudgedPeople.Attributed"/> for people. Written about people, it holds
    /// both, so a statement can count everybody from the one and the people from the other.
    /// </remarks>
    /// <param name="population">Who the reports are kept for.</param>
    /// <returns>
    /// The expressions, from the <c>WITH</c> that opens them, with nothing after the last. They bind what
    /// <see cref="Text"/> binds.
    /// </returns>
    public static string Reported(Population population)
    {
        var kept = JudgedPeople.Among(ReconciledEvents.Identified, population);

        return $$"""
            WITH
                windowed AS
                (
                    SELECT kind, surface, path, visitor_key, correlation_id, server_ts
                    FROM events
                    WHERE site_id = {site_id:UUID}
                      AND server_ts >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')
                      AND server_ts < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')
                ),
                {{ReconciledEvents.Reconciliation}}{{kept.Following}}
            """;
    }

    /// <summary>
    /// Writes the count of the pages delivered at each path, from one of the selections
    /// <see cref="Reported"/> writes, before any path is left out.
    /// </summary>
    /// <param name="source">
    /// The selection to count from. A fixed identifier written by a compiler in this assembly and never by
    /// a caller.
    /// </param>
    /// <param name="indent">
    /// Spaces the selection's lines after its first are laid out against, matching the depth the calling
    /// statement nests it at.
    /// </param>
    /// <returns>The selection: each path a report named, and the pages delivered there, which may be none.</returns>
    public static string ByPath(string source, int indent) => Grouped(source, indent, string.Empty);

    /// <summary>
    /// Writes the count of <see cref="ByPath"/>, with how many different visitor keys a page was delivered
    /// to at each path beside it.
    /// </summary>
    /// <remarks>
    /// A visitor is a key a page was delivered to at the path: activity carrying no key observed nothing
    /// about who was there, and is no visitor however many pages it holds, and a key that only reported
    /// reading a page it was already on was delivered nothing there. Counted from the grouping by path and
    /// key the pages are counted from, where each row is one key at one path.
    /// </remarks>
    /// <param name="source">The selection to count from, on the terms of <see cref="ByPath"/>.</param>
    /// <param name="indent">Spaces the selection's lines after its first are laid out against.</param>
    /// <returns>The selection: each path a report named, the pages delivered there, and to how many keys.</returns>
    public static string ByPathWithVisitors(string source, int indent) =>
        Grouped(
            source,
            indent,
            $",\n{new string(' ', indent)}    toInt64(countIf(visitor_key != '' AND delivered > 0)) AS visitors");

    /// <summary>
    /// Writes the two groupings the pages delivered at each path are counted by: each key at each path,
    /// and then each path.
    /// </summary>
    private static string Grouped(string source, int indent, string also)
    {
        var pad = new string(' ', indent);

        return $"""
            SELECT
            {pad}    path,
            {pad}    toInt64(sum(page_views)) AS page_views{also}
            {pad}FROM
            {pad}(
            {pad}    SELECT
            {pad}        path,
            {pad}        visitor_key,
            {pad}        {ReconciledEvents.DeliveredPageViews(indent + 8)}
            {pad}    FROM {source}
            {pad}    GROUP BY path, visitor_key
            {pad})
            {pad}GROUP BY path
            """;
    }

    /// <summary>
    /// The values the statement binds, in the order it declares them.
    /// </summary>
    /// <param name="siteId">The website.</param>
    /// <param name="range">The window to count over.</param>
    /// <param name="population">Who the pages were delivered to.</param>
    /// <returns>The values.</returns>
    public static QueryParameter[] Bound(Guid siteId, TimeRange range, Population population) =>
    [
        .. StatementBindings.Window(siteId, range),
        .. StatementBindings.Population(population),
    ];

    /// <summary>
    /// Reads a website's posts out of the statement's answer.
    /// </summary>
    /// <remarks>
    /// Each row is looked up among the posts' paths as it arrives and only a post's is kept: on a busy
    /// website the answer holds far more pages than posts, and the others are never gathered. What is
    /// kept is put in ordinal order here rather than by the store, which orders text by its bytes.
    /// </remarks>
    /// <param name="reader">The reader, before its first row.</param>
    /// <param name="paths">The paths the website's posts are read at, as the collector stores them.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Each of the paths a page was delivered at, in ordinal order; the others are left out.</returns>
    public static async Task<ImmutableArray<PathViews>> ReadAsync(
        ClickHouseDataReader reader,
        IEnumerable<string> paths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(paths);

        var posts = new HashSet<string>(paths, StringComparer.Ordinal);
        var kept = ImmutableArray.CreateBuilder<PathViews>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var path = reader.GetString(0);

            if (posts.Contains(path))
            {
                kept.Add(new PathViews(path, reader.GetInt64(1)));
            }
        }

        kept.Sort(static (one, other) => StringComparer.Ordinal.Compare(one.Path, other.Path));

        return kept.DrainToImmutable();
    }
}
