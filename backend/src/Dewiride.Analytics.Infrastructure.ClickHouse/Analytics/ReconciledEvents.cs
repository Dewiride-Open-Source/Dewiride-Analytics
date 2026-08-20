namespace Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;

/// <summary>
/// Reconciles the two halves of the measurement, so that one visitor asking for one page is
/// counted as one visitor asking for one page.
/// </summary>
/// <remarks>
/// <para>
/// The intended arrangement on a measured site is both halves at once: a tracker in the browser,
/// which sees whether anybody was actually there, and a reporter on the site's own server, which
/// sees the visitors that never run a script at all. Every page a person reads is therefore
/// reported twice, and taken at face value that tells every customer running the product properly
/// that their traffic is twice what it is.
/// </para>
/// <para>
/// Two things have to be true before it can be counted correctly, and they are separate problems.
/// </para>
/// <para>
/// <b>The two halves must agree on who the visitor was.</b> Each half derives its own key from the
/// address the request came from, and those addresses are not reliably the same one: the page and
/// the collector are different hosts, so a visitor whose network offers both kinds of address can
/// reach one over each. Where a reporter stamped an identifier onto the page it served and the
/// browser echoed it back, that identifier settles it, and the browser's key is the one kept —
/// it was measured from the visitor's own connection rather than asserted on their behalf.
/// </para>
/// <para>
/// <b>One page delivered is one page view, however many surfaces watched it happen.</b> Applied by
/// counting each half separately and keeping the larger, which is exact rather than approximate:
/// the two halves genuinely see different things, and the one that saw more pages saw them. A
/// crawler that runs no script is counted entirely from the server's half; a page reached without
/// a fresh request — moving back, or a site that redraws itself rather than reloading — entirely
/// from the browser's; and a page asked for twice is two, because both halves saw it twice. It
/// holds whether or not a site can stamp an identifier, which matters because several of the
/// places a reporter runs cannot.
/// </para>
/// <para>
/// Both statements this product sends against raw activity are built from the fragments below, so
/// which surfaces count as the visitor's own browser is stated once.
/// </para>
/// </remarks>
internal static class ReconciledEvents
{
    /// <summary>Tests whether a report came from the visitor's own browser.</summary>
    public static string FromVisitorBrowser { get; } = $"surface IN ({StoredNames.BrowserSurfaceList})";

    /// <summary>Tests whether a report came from somewhere between the visitor and the site.</summary>
    public static string FromRequestPath { get; } = $"surface NOT IN ({StoredNames.BrowserSurfaceList})";

    /// <summary>
    /// Settles who each report was about, given a preceding <c>windowed</c> selection carrying at
    /// least <c>surface</c>, <c>visitor_key</c> and <c>correlation_id</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What one echoed page establishes is not merely that one report; it is that these two keys
    /// are the same person. So the pair is learned once and then applied to everything that half
    /// reported, which is what keeps a redirect, a missing page, or anything else the browser
    /// never rendered inside the visit it belongs to instead of stranding it as a visitor of its
    /// own.
    /// </para>
    /// <para>
    /// A visitor no echoed page ever linked keeps the key their own half derived. That is the only
    /// answer available for a crawler that ran no script, and the right one: there was no browser
    /// there to disagree.
    /// </para>
    /// </remarks>
    public static string Reconciliation { get; } = $$"""
        echoed AS
            (
                SELECT
                    minIf(visitor_key, {{FromVisitorBrowser}}) AS browser_key,
                    minIf(visitor_key, {{FromRequestPath}}) AS reported_key
                FROM windowed
                WHERE correlation_id != ''
                  AND visitor_key != ''
                GROUP BY correlation_id
                HAVING browser_key != ''
                   AND reported_key != ''
            ),
            linked AS
            (
                SELECT
                    reported_key,
                    min(browser_key) AS browser_key
                FROM echoed
                GROUP BY reported_key
            ),
            identified AS
            (
                SELECT
                    windowed.* EXCEPT (visitor_key),
                    if(linked.browser_key != '' AND {{FromRequestPath}}, linked.browser_key, windowed.visitor_key) AS visitor_key
                FROM windowed
                LEFT JOIN linked ON windowed.visitor_key = linked.reported_key
            )
        """;

    /// <summary>
    /// Pages delivered, for a grouping already narrowed to one visitor and one page.
    /// </summary>
    /// <param name="indent">
    /// Spaces the fragment's own lines are laid out against, matching the depth the calling
    /// statement nests it at. The approved statements beside these compilers are read by people
    /// deciding whether a change to them was intended, and a fragment that keeps one statement's
    /// indentation wherever it is dropped makes the rest of them ragged.
    /// </param>
    /// <returns>The two expressions, ready to place at that depth.</returns>
    /// <remarks>
    /// <para>
    /// The browser's half is credited with a delivery it never announced but plainly saw. A tracker
    /// only reports how a page is being read from the page itself, so a progress or departure
    /// report naming an address is evidence that the address was delivered — and it is the report
    /// announcing the arrival, sent first and often while the page is already closing, that is the
    /// one most easily lost on the way. One is added rather than one per report, so a page read for
    /// half an hour and reported on thirty times is still the single delivery it was.
    /// </para>
    /// <para>
    /// Activity carrying no visitor key is counted as it arrives. Nothing about it says which
    /// visitor asked for the page, so there is no second sighting to recognise, and folding those
    /// together would discard views rather than duplicates.
    /// </para>
    /// </remarks>
    public static string DeliveredPageViews(int indent)
    {
        var pad = new string(' ', indent);

        return $"""
            greatest(
            {pad}    countIf(kind = 'PageView' AND {FromVisitorBrowser}),
            {pad}    toUInt64(countIf(kind != 'PageView' AND {FromVisitorBrowser}) > 0),
            {pad}    countIf(kind = 'PageView' AND {FromRequestPath})) AS delivered,
            {pad}if(visitor_key = '', countIf(kind = 'PageView'), delivered) AS page_views
            """;
    }
}
