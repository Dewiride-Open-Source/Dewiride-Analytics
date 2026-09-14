using System.Collections.Frozen;
using System.Collections.Immutable;
using Dewiride.Analytics.Classification.Identity;
using Dewiride.Analytics.Classification.Sessions;

namespace Dewiride.Analytics.Classification.Detectors;

/// <summary>
/// Reports who the visitor was, and what it said it was.
/// </summary>
/// <remarks>
/// <para>
/// Two questions that have to be answered together, because the answer to the first changes what
/// may be said about the second. A user agent is one line of text the visitor writes itself, so a
/// recognised name on its own is always paired with <see cref="SignalCodes.UnverifiedClaim"/>, and
/// that pairing is what the interface renders as "says it is" rather than "is". Where the request
/// arrived from an address its operator vouches for the pairing is gone and
/// <see cref="SignalCodes.ConfirmedCrawler"/> stands in its place, because the claim has stopped
/// being a claim.
/// </para>
/// <para>
/// The weight on an unconfirmed name is high all the same, and deliberately. Almost everything that
/// names itself GPTBot really is a crawler of some sort — the open question is whose, not whether —
/// so the claim is strong evidence of automation while being no evidence at all of identity.
/// </para>
/// <para>
/// A visitor that only describes itself as a crawler, in words this product recognises as the way
/// crawlers introduce themselves but under no name it has an entry for, is reported as having done
/// so and nothing else — never by the name it used, which is text the visitor wrote.
/// </para>
/// </remarks>
public sealed class DeclaredIdentityDetector : IDetector
{
    /// <summary>
    /// Weight of an identity settled by where the request came from.
    /// </summary>
    /// <remarks>
    /// Decisive, and the only observation this engine makes that earns it. Everything else here is
    /// something the visitor chose to say or something it chose to do, and both can be produced
    /// deliberately; a request from an address a company vouches for as its own crawlers' cannot
    /// be, short of being that company.
    /// </remarks>
    private const int ConfirmedWeight = 100;

    /// <summary>
    /// Weight of a name contradicted by where the request came from.
    /// </summary>
    /// <remarks>
    /// Just short of decisive on the same evidence, because the contradiction rests on both
    /// companies' files being current at the moment the visit was collected, and one of the two
    /// could have been a day behind a range it had just started using.
    /// </remarks>
    private const int ImpostorWeight = 90;

    /// <summary>Weight of a recognised crawler name.</summary>
    private const int CrawlerWeight = 70;

    /// <summary>Weight of a recognised fetching tool.</summary>
    private const int ToolWeight = 60;

    /// <summary>
    /// Weight of describing itself as a crawler without a name this product knows.
    /// </summary>
    /// <remarks>
    /// The same as a named tool. Both say what kind of thing was fetching and neither says whose,
    /// and a program that calls itself a crawler is almost always one — what is open is only which.
    /// </remarks>
    private const int GenericCrawlerWeight = 60;

    /// <summary>
    /// Weight of sending nothing at all.
    /// </summary>
    /// <remarks>
    /// Lower than a named tool. Every ordinary browser sends a user agent, so its absence is
    /// telling — but a stripped header is also what a privacy-minded person's browser extension
    /// produces, and that person is not automation.
    /// </remarks>
    private const int SilenceWeight = 30;

    /// <summary>What a purpose nobody has spelt out is reported as.</summary>
    private const string Unstated = "unstated";

    /// <inheritdoc />
    public ImmutableArray<Signal> Examine(SessionEvidence session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var claimed = CrawlerCatalogue.Match(session.UserAgent);

        if (!string.IsNullOrEmpty(session.ConfirmedOperator))
        {
            return Established(session.ConfirmedOperator, claimed);
        }

        if (string.IsNullOrWhiteSpace(session.UserAgent))
        {
            return [Observed.Signal(SignalCodes.NoUserAgent, SignalDirection.TowardAutomation, SilenceWeight)];
        }

        if (claimed is not null)
        {
            return
            [
                Declaration(claimed),

                // Weightless on purpose. It changes nothing about how automated the session looks;
                // it changes only what may be said about whose automation it is.
                Observed.Signal(SignalCodes.UnverifiedClaim, SignalDirection.Neutral, 0),
            ];
        }

        var tool = ToolCatalogue.Match(session.UserAgent);

        if (tool is not null)
        {
            return [Observed.Signal(SignalCodes.DeclaredTool, SignalDirection.TowardAutomation, ToolWeight, ("kind", tool))];
        }

        // Described itself as a crawler, in a name this product has no entry for. Nothing it said
        // travels with the report: the name is text the visitor wrote, and all that is known is
        // that it called itself a crawler.
        return CrawlerWords.AppearIn(session.UserAgent)
            ? [Observed.Signal(SignalCodes.DeclaredGenericCrawler, SignalDirection.TowardAutomation, GenericCrawlerWeight)]
            : [];
    }

    /// <summary>
    /// What to report when the address settled who the visitor is.
    /// </summary>
    /// <param name="confirmed">The company the address belongs to.</param>
    /// <param name="claimed">What the visitor called itself, where that was a name known here.</param>
    /// <remarks>
    /// A visitor that named nothing recognisable is not thereby suspect: several companies fetch
    /// pages behind a stock browser string, and one of them is the largest search engine's
    /// rendering crawler. What its name would have supplied is the purpose, so where the company
    /// runs crawlers for only one thing that stands in for it, and where it runs them for several
    /// the purpose is left unstated rather than guessed at.
    /// </remarks>
    private static ImmutableArray<Signal> Established(string confirmed, CrawlerIdentity? claimed)
    {
        if (claimed is null)
        {
            return [Confirmation(confirmed, CrawlerCatalogue.PurposeOf(confirmed))];
        }

        if (!string.Equals(claimed.Operator, confirmed, StringComparison.Ordinal))
        {
            return
            [
                Observed.Signal(
                    SignalCodes.FalseCrawlerClaim,
                    SignalDirection.TowardAutomation,
                    ImpostorWeight,
                    ("claimed", claimed.Operator),
                    ("operator", confirmed)),
            ];
        }

        return [Declaration(claimed), Confirmation(confirmed, claimed.Purpose)];
    }

    private static Signal Declaration(CrawlerIdentity crawler) => Observed.Signal(
        SignalCodes.DeclaredCrawler,
        SignalDirection.TowardAutomation,
        CrawlerWeight,
        ("operator", crawler.Operator),
        ("token", crawler.Token),
        ("purpose", Name(crawler.Purpose)));

    /// <summary>
    /// The observation that the address belongs to a company's own crawlers.
    /// </summary>
    /// <remarks>
    /// Carries the purpose although the sentence shown to a reader does not use it. What the
    /// address establishes is the company and nothing finer, so the sentence says only that; the
    /// purpose is here because it is what decides which kind of crawler the verdict names, and the
    /// scorecard reads the evidence rather than the session.
    /// </remarks>
    private static Signal Confirmation(string operatorName, CrawlerPurpose? purpose) => Observed.Signal(
        SignalCodes.ConfirmedCrawler,
        SignalDirection.TowardAutomation,
        ConfirmedWeight,
        ("operator", operatorName),
        ("purpose", Name(purpose)));

    /// <summary>
    /// The stored spelling of every purpose.
    /// </summary>
    /// <remarks>
    /// Written out rather than taken from the member name, so renaming one in C# cannot change
    /// the meaning of a verdict that was stored months ago.
    /// </remarks>
    private static readonly FrozenDictionary<CrawlerPurpose, string> Purposes =
        new Dictionary<CrawlerPurpose, string>
        {
            [CrawlerPurpose.AiTraining] = "ai-training",
            [CrawlerPurpose.AiAssistant] = "ai-assistant",
            [CrawlerPurpose.AiSearch] = "ai-search",
            [CrawlerPurpose.SearchIndex] = "search-index",
            [CrawlerPurpose.Advertising] = "advertising",
            [CrawlerPurpose.SiteTooling] = "site-tooling",
            [CrawlerPurpose.SocialPreview] = "social-preview",
            [CrawlerPurpose.Monitoring] = "monitoring",
            [CrawlerPurpose.Archival] = "archival",
            [CrawlerPurpose.SeoAudit] = "seo-audit",
        }.ToFrozenDictionary();

    /// <summary>
    /// The stored spelling of a purpose.
    /// </summary>
    /// <remarks>
    /// A purpose nobody established, and one with no spelling of its own, are both reported as
    /// unstated rather than as a member name, so a reader is told a crawler visited without being
    /// told something invented about what it was for.
    /// </remarks>
    private static string Name(CrawlerPurpose? purpose) =>
        purpose is { } known && Purposes.TryGetValue(known, out var name) ? name : Unstated;
}
