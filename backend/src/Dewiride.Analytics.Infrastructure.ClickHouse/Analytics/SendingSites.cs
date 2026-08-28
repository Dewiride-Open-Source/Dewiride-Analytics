using Dewiride.Analytics.Application.Telemetry;

namespace Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;

/// <summary>
/// Reduces the address a visitor arrived from to the site that sent them, and names it.
/// </summary>
/// <remarks>
/// <para>
/// A search engine answers on hundreds of addresses — <c>google.com</c>, <c>www.google.com</c> and
/// <c>google.co.in</c> are one place — so a list keyed on the hostname reports the busiest source
/// of a site's traffic at a fraction of its size on each of a dozen rows. A leading <c>www.</c> is
/// cut and the rest is reduced to the label in front of the public suffix, which is then looked up
/// in a catalogue that gives it a name and a kind.
/// </para>
/// <para>
/// That reduction is what makes the lookup safe. A referrer is written by whoever visited the
/// site, so matching a catalogue entry against any label in the address would let somebody who
/// registers <c>google.attacker.test</c> file their traffic under Google's name on a stranger's
/// dashboard. Taking the label in front of the suffix gives <c>attacker</c>, and the entry does
/// not match.
/// </para>
/// <para>
/// Written once here because several statements need it: the one that ranks where a window's
/// visitors came from, the one that opens a single visit, and the one that rebuilds what each
/// visit was so a reader can narrow to the sites that sent them. A site is therefore named
/// identically wherever it is shown, and a correction to the catalogue reaches all of them.
/// </para>
/// <para>
/// Every value it depends on is bound by the caller — the site's own address, the approximate
/// public-suffix list, and the three parallel catalogue arrays. Nothing here is concatenated from
/// anything a caller supplied.
/// </para>
/// </remarks>
internal static class SendingSites
{
    /// <summary>Where a carried-through column sits in the statement this writes.</summary>
    private const string ColumnIndent = "\n            ";

    /// <summary>Where a further condition on the activity read sits.</summary>
    private const string ConditionIndent = "\n          AND ";

    /// <summary>
    /// The activity a period itself holds.
    /// </summary>
    /// <remarks>
    /// What a statement that counts reports asks for: everything received inside the period and
    /// nothing else. A report either falls inside it or it does not, and no report outside it
    /// changes what one inside it says.
    /// </remarks>
    public static string ThePeriod { get; } = string.Join(
        ConditionIndent,
        "server_ts >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')",
        "server_ts < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')");

    /// <summary>
    /// The activity a period holds, and enough on either side of it to see whole the visits that
    /// cross its edges.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What a statement that counts visits asks for instead. A day either side, which is as long as
    /// a visit can be — see <see cref="VisitorKeys.LongestVisit"/> — so a visit touching the period
    /// at all is read whole.
    /// </para>
    /// <para>
    /// Reaching back matters most where a visit's identity is derived from when it began: read from
    /// the period's own start, a visit already under way has its arrival out of range, every report
    /// about it takes no part, and the visit vanishes from anything narrowed this way while still
    /// appearing in the list it was narrowed from. Reaching forward is what makes "this visit is
    /// over" an observation rather than an artefact of where the reading stopped.
    /// </para>
    /// <para>
    /// The idle timeout is no reach for this, close to hand as it is. A report about a page belongs
    /// to the visit that page was arrived at in however long the silence before it, so a visit is a
    /// chain that can have hours between its links and a timeout spans none of it.
    /// </para>
    /// </remarks>
    public static string ThePeriodAndTheVisitsAcrossIt { get; } = string.Join(
        ConditionIndent,
        "server_ts >= fromUnixTimestamp64Milli({from_ms:Int64} - {longest_visit_seconds:Int64} * 1000, 'UTC')",
        "server_ts < fromUnixTimestamp64Milli({to_ms:Int64} + {longest_visit_seconds:Int64} * 1000, 'UTC')");

    /// <summary>
    /// The activity one visit's verdict was reached from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What a statement that shows a visit beside its verdict asks for. A verdict records the last
    /// instant of the activity it was reached from, and reading up to that instant is what makes
    /// the account of a visit and the reasons given for it two views of one thing rather than two
    /// answers.
    /// </para>
    /// <para>
    /// It matters because they can otherwise differ. The engine judges a visit once it has been
    /// quiet long enough to be over, and a page announcing that it is being left can reach the
    /// collector an hour after that — belonging to the visit all the same, and arriving too late
    /// for the verdict. Read without this bound, a reader is shown a trail of pages adding up to an
    /// hour of reading beside a sentence saying the visit was read for four minutes, and neither
    /// number is wrong.
    /// </para>
    /// <para>
    /// A visit nothing has judged yet is read to the end of the period, because there is no verdict
    /// for it to disagree with and the account is the only thing there is to show.
    /// </para>
    /// </remarks>
    public static string ThePeriodUpToTheVerdict { get; } = string.Join(
        ConditionIndent,
        "server_ts >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')",
        "server_ts < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')",
        TheVerdictReached);

    /// <summary>
    /// The last instant of the activity this visit's verdict was reached from, or the end of the
    /// period where nothing has judged it.
    /// </summary>
    /// <remarks>
    /// The visit is named the way the engine names one — the visitor and the instant it began,
    /// which are the two values the calling statement already binds to find it at all — so no
    /// third value travels and nothing a caller wrote reaches the comparison.
    /// </remarks>
    private const string TheVerdictReached = """
        server_ts <= (
                      SELECT if(
                          count() = 0,
                          fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC'),
                          argMax(ended_at, (ruleset_major, ruleset_minor, classified_at)))
                      FROM session_classifications
                      WHERE site_id = {site_id:UUID}
                        AND session_key = concat(
                            {visitor_key:String}, ':', toString({from_ms:Int64})))
        """;

    /// <summary>
    /// Writes the reduction over a window of raw activity, ending in a <c>windowed</c> selection.
    /// </summary>
    /// <param name="window">
    /// Which activity takes part, as a condition over <c>events</c> beside the site. One of the
    /// two above, both written in this file: which one is a statement's choice, never a caller's.
    /// </param>
    /// <param name="carried">
    /// The columns of <c>events</c> the calling statement needs carried through. Each is a fixed
    /// identifier written by a compiler in this assembly and never by a caller.
    /// </param>
    /// <returns>
    /// Three expressions ending in <c>windowed</c>, which carries <paramref name="carried"/> plus
    /// <c>source_address</c>, <c>sending_host</c>, <c>source_site</c> and <c>source_channel</c>.
    /// </returns>
    public static string Of(string window, params string[] carried) =>
        Reduction(window, string.Join($",{ColumnIndent}", carried));

    private static string Reduction(string window, string columns) => $$"""
        arrived AS
            (
                SELECT
                    {{columns}},
                    referrer AS source_address,
                    if(
                        referrer_domain != ''
                        AND referrer_domain != {site_domain:String}
                        AND NOT endsWith(referrer_domain, concat('.', {site_domain:String})),
                        if(
                            startsWith(referrer_domain, 'www.'),
                            substring(referrer_domain, 5),
                            referrer_domain),
                        '') AS sending_host
                FROM events
                WHERE site_id = {site_id:UUID}
                  AND {{window}}
            ),
            named AS
            (
                SELECT
                    *,
                    splitByChar('.', sending_host) AS labels,
                    multiIf(
                        length(labels) < 2, sending_host,
                        length(labels) > 2
                            AND has({second_levels:Array(String)}, arrayElement(labels, -2)),
                            arrayElement(labels, -3),
                        arrayElement(labels, -2)) AS sending_name,
                    if(
                        has({source_keys:Array(String)}, sending_host),
                        sending_host,
                        sending_name) AS catalogue_key
                FROM arrived
            ),
            windowed AS
            (
                SELECT
                    {{columns}},
                    source_address,
                    sending_host,
                    transform(
                        catalogue_key,
                        {source_keys:Array(String)},
                        {source_names:Array(String)},
                        sending_host) AS source_site,
                    if(
                        sending_host = '',
                        '',
                        transform(
                            catalogue_key,
                            {source_keys:Array(String)},
                            {source_channels:Array(String)},
                            'link')) AS source_channel
                FROM named
            )
        """;
}
