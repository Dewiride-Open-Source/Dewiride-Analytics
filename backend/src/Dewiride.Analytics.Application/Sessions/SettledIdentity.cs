using System.Collections.Frozen;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Classification.Scoring;

namespace Dewiride.Analytics.Application.Sessions;

/// <summary>
/// Decides what may be said about a visitor who is still on the site.
/// </summary>
/// <remarks>
/// <para>
/// The engine reaches a conclusion from evidence that accumulates, so a conclusion drawn part-way
/// through is not a weaker form of the eventual one — it is frequently a different one, and on the
/// traffic this product exists to measure it is wrong in the direction that matters most. On a site
/// reported by both its own server and the browser, the server's report arrives first: for that
/// moment the visitor has executed nothing, which the engine reads as evidence of machinery, and a
/// person quietly reading an article is called something automated until their browser speaks.
/// Nothing about that is a defect to be tuned away. It is what "not enough has happened yet" looks
/// like when it is made to produce an answer.
/// </para>
/// <para>
/// So a live reading says nothing about most visitors, and says nothing deliberately. What it does
/// say is what rests on evidence that cannot later be taken back: a crawler whose operator vouches
/// for the address it arrived from, a crawler that named itself or merely called itself one, a
/// browser declaring that it is being driven, and a visitor asking for the places only an intruder
/// looks for. Every one of those is a thing that happened rather than a balance that was struck.
/// </para>
/// <para>
/// The rule is a set of observations rather than a set of categories, on purpose. What a category
/// means is decided by the ordered rules of <see cref="EvidenceScorecard"/>, and naming categories
/// here would be a second copy of that order with nobody keeping the two in step. Naming the
/// observations instead shows a conclusion exactly when it was reached by a rule that cannot be
/// reversed, whatever category that rule produced.
/// </para>
/// </remarks>
public static class SettledIdentity
{
    /// <summary>
    /// The observations that settle what something is, rather than weigh what it might be.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each is read by a first-match rule in <see cref="EvidenceScorecard"/> ahead of anything
    /// weighed, and each rests on something that only ever accumulates:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <see cref="SignalCodes.ConfirmedCrawler"/> — settled by the collector against the addresses a
    /// company publishes for its own machines. The one route to
    /// <see cref="EvidenceStrength.Verified"/>, and the one thing this product can say about a
    /// visitor who is still reading and stand behind entirely.
    /// </item>
    /// <item>
    /// <see cref="SignalCodes.FalseCrawlerClaim"/> — a name worn while arriving from another
    /// company's crawler addresses. It needs both halves, and both halves accumulate.
    /// </item>
    /// <item>
    /// <see cref="SignalCodes.DeclaredCrawler"/> — a visitor naming itself. The category it produces
    /// keeps "suspected" in its name where nothing bore the claim out, which is the distinction the
    /// interface is obliged to keep and a live reading must not blur.
    /// </item>
    /// <item>
    /// <see cref="SignalCodes.DeclaredGenericCrawler"/> — a visitor describing itself as a crawler
    /// in a name this product has no entry for. The description travels with every request it
    /// makes, and a later request cannot withdraw it.
    /// </item>
    /// <item>
    /// <see cref="SignalCodes.DeclaredWebDriver"/> — the browser volunteered that it is being
    /// driven. A declaration already made is not withdrawn by a later request.
    /// </item>
    /// <item>
    /// <see cref="SignalCodes.SensitivePaths"/> — a request for a credential store that was never
    /// published. A path asked for cannot be un-asked.
    /// </item>
    /// </list>
    /// <para>
    /// Two observations that look as though they belong here do not, and the reasons are the whole
    /// point of the rule. <see cref="SignalCodes.MissingPaths"/> is weighed as the share of a visit
    /// spent failing, so a sweep that goes on to fetch real pages falls back under the threshold
    /// that made it a scanner. And the systematic-retrieval rule requires that nobody has been
    /// observed reading, so a content scraper stops being one the moment somebody reads for two
    /// seconds. Either would put a red conclusion on a screen and then take it off again.
    /// </para>
    /// </remarks>
    private static readonly FrozenSet<string> Settling = new[]
    {
        SignalCodes.ConfirmedCrawler,
        SignalCodes.FalseCrawlerClaim,
        SignalCodes.DeclaredCrawler,
        SignalCodes.DeclaredGenericCrawler,
        SignalCodes.DeclaredWebDriver,
        SignalCodes.SensitivePaths,
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Returns the verdict where it may be shown about a visitor still under way, and nothing where
    /// it may not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing is the answer for most visitors and is not a failure of the reading. A screen shows
    /// such a visitor as somebody nobody has said anything about yet, which is true, and the settled
    /// verdict arrives on the screen that reports finished visits.
    /// </para>
    /// <para>
    /// What this guarantees is the property a reader would notice being broken: something named here
    /// is still machinery once its visit has finished, and is never afterwards called a person. The
    /// particular name may sharpen — a visitor asking for an administration panel is a scanner until
    /// the address it arrived from turns out to belong to a company that publishes it, at which
    /// point it is that company. That is the answer improving, and it is the only direction the
    /// ordered rules allow.
    /// </para>
    /// </remarks>
    /// <param name="verdict">What the engine made of the evidence gathered so far.</param>
    /// <returns>The verdict, or <see langword="null"/> where nothing may yet be said.</returns>
    /// <exception cref="ArgumentNullException">The verdict is missing.</exception>
    public static ClassificationVerdict? Of(ClassificationVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        return verdict.Supporting.Any(signal => Settling.Contains(signal.Code)) ? verdict : null;
    }
}
