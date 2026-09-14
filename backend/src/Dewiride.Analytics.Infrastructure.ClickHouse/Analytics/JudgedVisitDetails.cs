namespace Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;

/// <summary>
/// Rebuilds what each visit in a period was — where it came from, where it was, and what it was
/// read with — and names each visit the way the verdict beside it is named.
/// </summary>
/// <remarks>
/// <para>
/// None of this is stored. A verdict is kept because it cannot be worked out again; these nine
/// facts are the opposite, and every one of them is already derived from activity for the account
/// a reader is shown when they open a visit. Keeping a second copy beside the verdict would create
/// something able to disagree with the events, and would freeze the two catalogues at the moment
/// the visit was judged — so correcting an entry would leave the same visit filed one way on a card
/// and another way in a list, depending only on when it happened to arrive.
/// </para>
/// <para>
/// Every expression here is deliberately one already used elsewhere, because a value picked off a
/// card has to be the value that narrows a list. Where a visit came from and what it was read with
/// are the account a single visit gives of itself; the network is named the way the list of
/// networks names one; and the page a visit began on is the one the arrivals list counts. The
/// account every row of the list carries is this same rebuild, so a row and the panel opened from
/// it cannot disagree.
/// </para>
/// <para>
/// A visit is named by the identity the detection engine derives, which is the visitor's key and
/// the instant the visit began. Reproducing it exactly is what makes the join to a verdict work at
/// all, and it needs four things together: activity read a full day on either side of the stretch
/// being described, which is the period where a list is narrowed by these facts and the span of
/// the page where a list is showing them; the reconciliation of the two halves of the measurement,
/// which rewrites the visitor key and so writes half the identity; the same grouping into visits
/// everything else uses; and the earliest report of the whole visit rather than the earliest that
/// announced a page. Get any of them wrong and nothing errors: the identities simply fail to match
/// and the list comes back empty.
/// </para>
/// <para>
/// A visit belongs to the period it began in, on the same terms as the verdicts it is joined to, so
/// each belongs to exactly one period however long it ran for.
/// </para>
/// <para>
/// Nothing established reads as empty rather than as a row missing. An install behind a proxy that
/// passes on no address resolves no country at all, and a visitor who arrived by typing the address
/// came from nowhere in particular — both are answers, and both are askable.
/// </para>
/// </remarks>
internal static class JudgedVisitDetails
{
    /// <summary>The kind of device a visit was made on.</summary>
    public const string Device = "device";

    /// <summary>The kind of place a visit was sent by.</summary>
    public const string SourceKind = "from_kind";

    /// <summary>The browser a visit was made with.</summary>
    public const string Browser = "browser";

    /// <summary>The operating system underneath it.</summary>
    public const string System = "system_name";

    /// <summary>The country a visit arrived from.</summary>
    public const string Country = "country";

    /// <summary>The town within it.</summary>
    public const string Town = "town";

    /// <summary>Who runs the network a visit arrived over.</summary>
    public const string Network = "network";

    /// <summary>The site that sent the visit.</summary>
    public const string Source = "from_site";

    /// <summary>The page the visit began on.</summary>
    public const string EntryPage = "entry_path";

    /// <summary>Every detail, in the order an answer reports them.</summary>
    private static readonly string[] Every =
        [Device, SourceKind, Browser, System, Country, Town, Network, Source, EntryPage];

    /// <summary>
    /// Writes the nine details as one array, ready to be unfolded a row per detail.
    /// </summary>
    /// <remarks>
    /// Assembled from the names above rather than typed out beside them, so the name a statement
    /// reports a value under is the name the reader sorts it by — a pair that could drift would
    /// quietly drop a whole detail out of an answer and leave a filter with nothing to offer.
    /// </remarks>
    /// <param name="indent">
    /// The column the calling statement places the expression at, which the details themselves are
    /// laid out one step inside.
    /// </param>
    /// <returns>The expression, ready to place at that column.</returns>
    public static string EveryDetail(int indent)
    {
        var pad = new string(' ', indent);
        var details = string.Join($",\n{pad}    ", Every.Select(detail => $"('{detail}', {detail})"));

        return $"arrayJoin([\n{pad}    {details}])";
    }

    /// <summary>
    /// The reconstruction over the whole period, ending in a <c>described</c> selection of one row
    /// per visit.
    /// </summary>
    /// <remarks>
    /// Reads a day either side of the period, because a list narrowed by these facts has to describe
    /// every visit the period holds before it can keep or drop one. Laid out to follow a
    /// <c>WITH</c> keyword, on the same terms as the fragments it is built from. It carries
    /// <c>session_key</c> and the nine columns named above.
    /// </remarks>
    public static string Fragment { get; } = Rebuilt(SendingSites.ThePeriodAndTheVisitsAcrossIt);

    /// <summary>
    /// The same reconstruction over the visits on one page of a list, ending in <c>described</c>.
    /// </summary>
    /// <remarks>
    /// Reads a day either side of the page's own span rather than the period's, because the list
    /// has already chosen which visits it is showing and only those need describing. Expects the
    /// <c>span</c> selection <see cref="SendingSites.ThePageAndTheVisitsAcrossIt"/> names, written
    /// by the calling statement beside the page.
    /// </remarks>
    public static string OfThePage { get; } = Rebuilt(SendingSites.ThePageAndTheVisitsAcrossIt);

    /// <summary>
    /// Writes the reconstruction over one window of activity.
    /// </summary>
    /// <remarks>
    /// One body with the window as its only hole, so the two readings above cannot describe a visit
    /// two ways. The visits are still held to the period they began in whichever window was read:
    /// every visit on a page began inside the period, so the bound is right for both.
    /// </remarks>
    /// <param name="activity">Which activity takes part, as <see cref="SendingSites"/> writes one.</param>
    /// <returns>The expressions, ending in <c>described</c>.</returns>
    private static string Rebuilt(string activity) => $$"""
        {{SendingSites.Of(
            activity,
            "event_id",
            "surface",
            "visitor_key",
            "correlation_id",
            "server_ts",
            "kind",
            "path",
            "country_code",
            "city",
            "autonomous_system",
            "network_owner",
            "device_class",
            "browser_family",
            "operating_system")}},
            {{ReconciledEvents.Reconciliation}},
            {{VisitGrouping.Of(VisitGrouping.EveryVisitor)}},
            gathered AS
            (
                SELECT
                    concat(visitor_key, ':', toString(toUnixTimestamp64Milli(min(server_ts)))) AS session_key,
                    min(server_ts) AS started_at,
                    argMinIf(source_site, (server_ts, event_id), sending_host != '') AS from_site,
                    argMinIf(source_channel, (server_ts, event_id), sending_host != '') AS from_kind,
                    argMinIf(country_code, (server_ts, event_id), country_code != '') AS country,
                    argMinIf(city, (server_ts, event_id), city != '') AS town,
                    argMinIf(network_owner, (server_ts, event_id), network_owner != '') AS network_owner,
                    max(autonomous_system) AS autonomous_system,
                    argMinIf(
                        toString(device_class),
                        (server_ts, event_id),
                        device_class != 'Unknown') AS device,
                    argMinIf(browser_family, (server_ts, event_id), browser_family != '') AS browser,
                    argMinIf(
                        operating_system,
                        (server_ts, event_id),
                        operating_system != '') AS system_name,
                    argMinIf(path, (server_ts, event_id), opens_page) AS entry_path
                FROM opened
                GROUP BY visitor_key, visit_ordinal
                HAVING started_at >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')
                   AND started_at < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')
            ),
            described AS
            (
                SELECT
                    session_key,
                    from_site,
                    from_kind,
                    country,
                    town,
                    {{NamedNetworks.From(12)}} AS network,
                    device,
                    browser,
                    system_name,
                    entry_path
                FROM gathered
            )
        """;
}
