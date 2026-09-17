using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Classification;

namespace Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;

/// <summary>
/// Keeps the reports of the visits the engine concluded were people, for a question asked about
/// people alone.
/// </summary>
/// <remarks>
/// <para>
/// "People" is a verdict. Nothing about a single report says whether a person sent it; what the
/// engine concluded about the whole visit does, and that conclusion is stored against the visit's
/// identity — whose it was and when it began — beside the first and last instants of the activity
/// it was reached from. A report is therefore a person's report exactly when it falls inside a
/// visit the newest ruleset to judge it concluded was a person, and that is the whole of the test
/// below. A report about the visit that arrived after the verdict was reached is not covered, on
/// the same terms as the opened visit reads no further than its verdict.
/// </para>
/// <para>
/// Each visit is first reduced to the newest verdict about it, by the reduction the breakdown of
/// who came uses, so the visits kept here are the visits that panel counts as people. A visit
/// judged again under a newer ruleset is kept or dropped by the newer verdict; one judged by
/// nothing yet is not among the people, however like a person it looks. One visitor's judged
/// visits never overlap in time, so a report matches at most one of them.
/// </para>
/// <para>
/// Verdicts are read from a day before the window, which is as long as a visit can be — see
/// <see cref="VisitorKeys.LongestVisit"/> — so a visit that began the evening before and ran into
/// the window keeps the reports it made inside it, exactly as the counts of everybody keep them.
/// </para>
/// <para>
/// Applied after identity has been settled and never before it. A verdict names a visit by the
/// key the engine derived once both halves of the measurement were folded together, so a report
/// the site's own server sent under its own key would match nothing until it has been given the
/// browser's. Expects the selection it follows to carry <c>visitor_key</c> and <c>server_ts</c>,
/// and a <c>longest_visit_seconds</c> value bound by the caller.
/// </para>
/// <para>
/// The two instants are given names of their own inside the reduction, because an alias is
/// substituted wherever its name appears in the same selection and the column the window is cut
/// on is one of them.
/// </para>
/// </remarks>
internal static class JudgedPeople
{
    /// <summary>The selection a statement asked about people reads from.</summary>
    public const string Attributed = "attributed";

    /// <summary>Where the next expression of a statement's list begins, after the one before it.</summary>
    private const string Onto = ",\n    ";

    /// <summary>What the store calls the conclusion that a visit was a person.</summary>
    private static readonly string Person = StoredNames.CategoryNames[TrafficCategory.LikelyHuman];

    /// <summary>
    /// How a statement is written for a population: what follows the selection it would have
    /// read from, and what it reads from instead.
    /// </summary>
    /// <param name="Following">
    /// The expressions that follow that selection, beginning with the separator that joins them
    /// on, placed directly after the closing bracket of the selection they follow. Nothing for
    /// everybody, so a statement about everybody reads exactly as it would with no population at
    /// all.
    /// </param>
    /// <param name="Source">The selection the statement reads from.</param>
    public readonly record struct Kept(string Following, string Source);

    /// <summary>
    /// Decides how a statement is written for whoever it is asked about.
    /// </summary>
    /// <param name="among">
    /// The selection the statement would read from for everybody. A fixed identifier written by
    /// a compiler in this assembly and never by a caller.
    /// </param>
    /// <param name="population">Who the statement is asked about.</param>
    /// <returns>What to write after that selection, and what to read from.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The population is not one the vocabulary defines.</exception>
    public static Kept Among(string among, Population population) => population switch
    {
        Population.Everybody => new(string.Empty, among),
        Population.People => new($"{Onto}{Of(among)}", Attributed),
        _ => throw new ArgumentOutOfRangeException(nameof(population), population, "Ask about everybody or about people."),
    };

    /// <summary>
    /// Writes the two expressions that keep, from a selection, the reports the verdicts on people
    /// cover.
    /// </summary>
    /// <param name="among">The selection to keep from.</param>
    /// <returns>The two expressions, ending in <see cref="Attributed"/>.</returns>
    private static string Of(string among) => $$"""
        people AS
            (
                SELECT
                    splitByChar(':', session_key)[1] AS visitor_key,
                    began,
                    ended
                FROM
                (
                    SELECT
                        session_key,
                        argMax(category, (ruleset_major, ruleset_minor, classified_at)) AS category,
                        argMax(started_at, (ruleset_major, ruleset_minor, classified_at)) AS began,
                        argMax(ended_at, (ruleset_major, ruleset_minor, classified_at)) AS ended
                    FROM session_classifications
                    WHERE site_id = {site_id:UUID}
                      AND started_at >= fromUnixTimestamp64Milli({from_ms:Int64} - {longest_visit_seconds:Int64} * 1000, 'UTC')
                      AND started_at < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')
                    GROUP BY session_key
                )
                WHERE category = '{{Person}}'
            ),
            {{Attributed}} AS
            (
                SELECT {{among}}.*
                FROM {{among}}
                INNER JOIN people ON {{among}}.visitor_key = people.visitor_key
                WHERE {{among}}.server_ts >= people.began
                  AND {{among}}.server_ts <= people.ended
            )
        """;
}
