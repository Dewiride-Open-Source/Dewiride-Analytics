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
/// Written once here because two statements need it: the one that ranks where a window's visitors
/// came from, and the one that opens a single visit. A site is therefore named identically
/// wherever it is shown, and a correction to the catalogue reaches both at once.
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

    /// <summary>
    /// Writes the reduction over a window of raw activity, ending in a <c>windowed</c> selection.
    /// </summary>
    /// <param name="carried">
    /// The columns of <c>events</c> the calling statement needs carried through. Each is a fixed
    /// identifier written by a compiler in this assembly and never by a caller.
    /// </param>
    /// <returns>
    /// Three expressions ending in <c>windowed</c>, which carries <paramref name="carried"/> plus
    /// <c>source_address</c>, <c>sending_host</c>, <c>source_site</c> and <c>source_channel</c>.
    /// </returns>
    public static string Of(params string[] carried) => Reduction(string.Join($",{ColumnIndent}", carried));

    private static string Reduction(string columns) => $$"""
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
                  AND server_ts >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')
                  AND server_ts < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')
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
