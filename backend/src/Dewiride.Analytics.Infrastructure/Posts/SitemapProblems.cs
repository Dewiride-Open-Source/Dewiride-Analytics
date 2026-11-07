using System.Collections.Frozen;
using Dewiride.Analytics.Application.Posts;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// What the ways a website can fail are called, by each thing that has to say.
/// </summary>
internal static class SitemapProblems
{
    private static readonly FrozenDictionary<FetchOutcome, SitemapProblem> OfFetches =
        new Dictionary<FetchOutcome, SitemapProblem>
        {
            [FetchOutcome.NotFound] = SitemapProblem.NotFound,
            [FetchOutcome.TurnedAway] = SitemapProblem.TurnedAway,
            [FetchOutcome.AskedToSlowDown] = SitemapProblem.TurnedAway,
            [FetchOutcome.ServerError] = SitemapProblem.ServerError,
            [FetchOutcome.Unreachable] = SitemapProblem.Unreachable,
            [FetchOutcome.Insecure] = SitemapProblem.Insecure,
            [FetchOutcome.NotASitemap] = SitemapProblem.NotASitemap,
            [FetchOutcome.TooLarge] = SitemapProblem.TooLarge,
            [FetchOutcome.LeftTheWebsite] = SitemapProblem.LeftTheWebsite,
            [FetchOutcome.TooManyRedirects] = SitemapProblem.TooManyRedirects,
            [FetchOutcome.PrivateAddress] = SitemapProblem.PrivateAddress,
            [FetchOutcome.RobotsDisallowed] = SitemapProblem.RobotsDisallowed,
            [FetchOutcome.RobotsUnreadable] = SitemapProblem.RobotsUnreadable,
        }.ToFrozenDictionary();

    /// <summary>
    /// What a website not giving an answer that can be read comes to.
    /// </summary>
    /// <remarks>
    /// A success and "nothing has changed" are not problems, and an answer that was not a page can only
    /// come of asking for one. Should either arrive here, the website has answered with something
    /// nobody asked it for.
    /// </remarks>
    /// <param name="outcome">What came of asking.</param>
    /// <returns>What to call it.</returns>
    public static SitemapProblem Of(FetchOutcome outcome) =>
        OfFetches.GetValueOrDefault(outcome, SitemapProblem.ServerError);

    /// <summary>
    /// What a part that was not read comes to, for saying how the whole reading went.
    /// </summary>
    /// <param name="outcome">What came of asking for it, which is not that it was read.</param>
    /// <returns>What to call it, which goes by the same name.</returns>
    public static SitemapProblem Of(SitemapPartOutcome outcome) => Enum.Parse<SitemapProblem>(outcome.ToString());

    /// <summary>
    /// What to record about a part a problem stopped being read.
    /// </summary>
    /// <param name="problem">What stopped it, of those that can stop one part.</param>
    /// <returns>What to record, which goes by the same name.</returns>
    public static SitemapPartOutcome OutcomeOf(SitemapProblem problem) =>
        Enum.Parse<SitemapPartOutcome>(problem.ToString());

    /// <summary>
    /// Whether a problem is trouble at the website itself, in which case the rest of it can wait.
    /// </summary>
    /// <remarks>
    /// Every page it could be asked for would use up one of its few attempts on the same answer. Anything
    /// else here is about one address, and says nothing about how the next one would go.
    /// </remarks>
    /// <param name="problem">What got in the way.</param>
    /// <returns>Whether it is.</returns>
    public static bool IsTroubleAtTheWebsite(SitemapProblem problem) =>
        problem is SitemapProblem.Unreachable
            or SitemapProblem.TurnedAway
            or SitemapProblem.ServerError
            or SitemapProblem.Insecure
            or SitemapProblem.TooSlow
            or SitemapProblem.RobotsUnreadable;
}
