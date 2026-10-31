using System.Collections.Frozen;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Classification;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Dewiride.Analytics.Api.Analytics;

/// <summary>
/// The words the questions about a website's numbers are asked in, and how a question that cannot
/// be answered as asked is refused.
/// </summary>
/// <remarks>
/// Shared by every endpoint that answers such a question, so a word means one thing on all of them
/// and a refusal reads the same wherever it is met. Each table is written out rather than derived
/// from an enumeration, so renaming a member in C# cannot change an address the dashboard already
/// asks for, and anything not in a table is refused before the compiler is reached — a second answer
/// to the question the compiler already settles by taking every identifier from a fixed table of its
/// own.
/// </remarks>
internal static class QueryVocabulary
{
    /// <summary>What is said when a question is asked about a population this product cannot separate.</summary>
    public const string UnknownPopulation = "Ask about everybody, or only about people.";

    /// <summary>What is said when a series is asked for in buckets of a size this product does not cut.</summary>
    public const string UnknownGranularity = "Ask for buckets of an hour, a day, a week or a month.";

    /// <summary>What is said when a website's whole record is asked for in buckets it is not cut into.</summary>
    public const string UnknownKeptGranularity = "Ask for buckets of a month or a year.";

    /// <summary>What is said when a series is asked for a measure this product does not count.</summary>
    public const string UnknownMetric = "Ask for either page views or visitors.";

    /// <summary>Which measure a series counts, by the word used on the wire.</summary>
    public static readonly FrozenDictionary<string, TimeSeriesMetric> Metrics =
        new Dictionary<string, TimeSeriesMetric>(StringComparer.OrdinalIgnoreCase)
        {
            ["pageviews"] = TimeSeriesMetric.PageViews,
            ["visitors"] = TimeSeriesMetric.Visitors,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The spelling each measure is reported back under.
    /// </summary>
    /// <remarks>
    /// The answer names what it counted rather than repeating the caller's own spelling, so a
    /// request for <c>PageViews</c> and one for <c>pageviews</c> produce identical documents.
    /// </remarks>
    public static readonly FrozenDictionary<TimeSeriesMetric, string> MetricNames =
        Metrics.ToFrozenDictionary(entry => entry.Value, entry => entry.Key);

    /// <summary>
    /// Who an answer may be about, by the word used on the wire.
    /// </summary>
    /// <remarks>
    /// One word: everybody is what a question means when nobody is named, and is never spelled.
    /// </remarks>
    public static readonly FrozenDictionary<string, Population> Populations =
        new Dictionary<string, Population>(StringComparer.OrdinalIgnoreCase)
        {
            ["people"] = Population.People,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// How wide each bucket of a series may be, by the word used on the wire.
    /// </summary>
    /// <remarks>
    /// A week runs Monday to Sunday and a month is the calendar month, both where the website is.
    /// </remarks>
    public static readonly FrozenDictionary<string, TimeGranularity> Granularities =
        new Dictionary<string, TimeGranularity>(StringComparer.OrdinalIgnoreCase)
        {
            ["hour"] = TimeGranularity.Hour,
            ["day"] = TimeGranularity.Day,
            ["week"] = TimeGranularity.Week,
            ["month"] = TimeGranularity.Month,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>The spelling each bucket width is reported back under.</summary>
    public static readonly FrozenDictionary<TimeGranularity, string> GranularityNames =
        Granularities.ToFrozenDictionary(entry => entry.Value, entry => entry.Key);

    /// <summary>
    /// How wide each bucket of a website's whole record may be, by the word used on the wire.
    /// </summary>
    /// <remarks>
    /// A month or a year, each on the website's calendar: the record reaches back beyond the reports a
    /// finer bucket would be counted from.
    /// </remarks>
    public static readonly FrozenDictionary<string, KeptGranularity> KeptGranularities =
        new Dictionary<string, KeptGranularity>(StringComparer.OrdinalIgnoreCase)
        {
            ["month"] = KeptGranularity.Month,
            ["year"] = KeptGranularity.Year,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>The spelling each bucket width of a whole record is reported back under.</summary>
    public static readonly FrozenDictionary<KeptGranularity, string> KeptGranularityNames =
        KeptGranularities.ToFrozenDictionary(entry => entry.Value, entry => entry.Key);

    /// <summary>
    /// Reads who a question is asked about.
    /// </summary>
    /// <remarks>
    /// Nothing named is everybody, which is what every question means until somebody is named.
    /// Called before the site is resolved on the same terms as every other check, so a word naming
    /// no population is refused identically whether or not the site exists.
    /// </remarks>
    /// <param name="asked">What the caller supplied, or nothing.</param>
    /// <param name="population">Who it names, where it names anyone this product can separate.</param>
    /// <returns><see langword="true"/> when the request can be answered as asked.</returns>
    public static bool TryReadPopulation(string? asked, out Population population)
    {
        if (string.IsNullOrEmpty(asked))
        {
            population = Population.Everybody;

            return true;
        }

        return Populations.TryGetValue(asked, out population);
    }

    /// <summary>
    /// Refuses a question that cannot be answered as asked.
    /// </summary>
    /// <param name="detail">Why, in a sentence the caller can act on.</param>
    /// <returns>The refusal.</returns>
    public static ProblemHttpResult Unusable(string? detail) =>
        TypedResults.Problem(
            title: "That request could not be answered as asked.",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest);

    /// <summary>
    /// Where an answer counted from verdicts stops being complete.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The earlier of two instants, as <see cref="JudgingReach.Settled"/> explains. A site judging
    /// has not started on is complete nowhere in the window being read, which is said by answering
    /// with the window's own start.
    /// </para>
    /// <para>
    /// Read before the answer it qualifies, never after. The engine stores a pass's verdicts before
    /// it moves its bookmark past them, so a bookmark read first can only be behind what an answer
    /// read afterwards holds — which washes a bucket that had in fact settled — whereas read second
    /// it can be ahead of it, and call a stretch complete whose verdicts the answer never saw.
    /// </para>
    /// </remarks>
    /// <param name="scope">The site being read.</param>
    /// <param name="range">The window being read, whose start is the answer for a site judging has not started on.</param>
    /// <param name="progress">Where judging has reached.</param>
    /// <param name="settings">The engine's own idle timeout.</param>
    /// <param name="clock">The present moment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The instant.</returns>
    public static async Task<DateTimeOffset> JudgedUpToAsync(
        TenantScope scope,
        TimeRange range,
        IClassificationProgressStore progress,
        ClassificationOptions settings,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clock);

        var caughtUp = await progress
                .CaughtUpToAsync(scope.SiteId, RulesetVersion.Current, cancellationToken)
                .ConfigureAwait(false)
            ?? range.From;

        return JudgingReach.Settled(caughtUp, clock.GetUtcNow(), settings.IdleTimeout);
    }
}
