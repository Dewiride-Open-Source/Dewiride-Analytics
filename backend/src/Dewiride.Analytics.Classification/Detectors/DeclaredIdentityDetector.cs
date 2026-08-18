using System.Collections.Immutable;
using Dewiride.Analytics.Classification.Identity;
using Dewiride.Analytics.Classification.Sessions;

namespace Dewiride.Analytics.Classification.Detectors;

/// <summary>
/// Reports what the visitor said it was.
/// </summary>
/// <remarks>
/// <para>
/// A user agent is one line of text the visitor writes itself, so nothing this detector reports
/// is an identity. It always pairs a recognised name with
/// <see cref="SignalCodes.UnverifiedClaim"/>, and that pairing is what the interface renders as
/// "says it is" rather than "is". Only checking the address against the operator's published
/// ranges removes it.
/// </para>
/// <para>
/// The weight is high all the same, and deliberately. Almost everything that names itself GPTBot
/// really is a crawler of some sort — the open question is whose, not whether — so the claim is
/// strong evidence of automation while being no evidence at all of identity.
/// </para>
/// </remarks>
public sealed class DeclaredIdentityDetector : IDetector
{
    /// <summary>Weight of a recognised crawler name.</summary>
    private const int CrawlerWeight = 70;

    /// <summary>Weight of a recognised fetching tool.</summary>
    private const int ToolWeight = 60;

    /// <summary>
    /// Weight of sending nothing at all.
    /// </summary>
    /// <remarks>
    /// Lower than a named tool. Every ordinary browser sends a user agent, so its absence is
    /// telling — but a stripped header is also what a privacy-minded person's browser extension
    /// produces, and that person is not automation.
    /// </remarks>
    private const int SilenceWeight = 30;

    /// <inheritdoc />
    public ImmutableArray<Signal> Examine(SessionEvidence session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (string.IsNullOrWhiteSpace(session.UserAgent))
        {
            return [Observed.Signal(SignalCodes.NoUserAgent, SignalDirection.TowardAutomation, SilenceWeight)];
        }

        var crawler = CrawlerCatalogue.Match(session.UserAgent);

        if (crawler is not null)
        {
            return
            [
                Observed.Signal(
                    SignalCodes.DeclaredCrawler,
                    SignalDirection.TowardAutomation,
                    CrawlerWeight,
                    ("operator", crawler.Operator),
                    ("token", crawler.Token),
                    ("purpose", Name(crawler.Purpose))),

                // Weightless on purpose. It changes nothing about how automated the session looks;
                // it changes only what may be said about whose automation it is.
                Observed.Signal(SignalCodes.UnverifiedClaim, SignalDirection.Neutral, 0),
            ];
        }

        var tool = ToolCatalogue.Match(session.UserAgent);

        return tool is null
            ? []
            : [Observed.Signal(SignalCodes.DeclaredTool, SignalDirection.TowardAutomation, ToolWeight, ("kind", tool))];
    }

    /// <summary>
    /// The stored spelling of a purpose.
    /// </summary>
    /// <remarks>
    /// Written out rather than taken from the member name, so renaming one in C# cannot change
    /// the meaning of a verdict that was stored months ago.
    /// </remarks>
    private static string Name(CrawlerPurpose purpose) => purpose switch
    {
        CrawlerPurpose.AiTraining => "ai-training",
        CrawlerPurpose.AiAssistant => "ai-assistant",
        CrawlerPurpose.AiSearch => "ai-search",
        CrawlerPurpose.SearchIndex => "search-index",
        CrawlerPurpose.Advertising => "advertising",
        CrawlerPurpose.SiteTooling => "site-tooling",
        _ => "unstated",
    };
}
