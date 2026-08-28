namespace Dewiride.Analytics.Infrastructure.Crawlers;

/// <summary>
/// How this installation asks a name server whose crawler an address belongs to.
/// </summary>
/// <remarks>
/// <para>
/// Which domains stand for which company is not configured. That is part of the catalogue the
/// engine reasons with, because a domain listed there decides whose name goes beside a customer's
/// traffic, and being able to add one in an environment file would make it possible to have this
/// product vouch for anybody. What is configurable is whether this installation asks at all, how
/// long it waits, and how much it remembers.
/// </para>
/// <para>
/// Nothing here is on the ingest path. The questions are asked when a finished visit is judged, on
/// a background worker, so the timeout is generous by the standards of the rest of this product:
/// the cost of waiting is a verdict arriving a moment later, and the cost of not waiting is a
/// crawler going unrecognised for good.
/// </para>
/// </remarks>
public sealed class CrawlerNameOptions
{
    /// <summary>Configuration section these options are bound from.</summary>
    public const string SectionName = "Dewiride:CrawlerNames";

    /// <summary>
    /// Whether this installation may ask a name server about a visitor's address.
    /// </summary>
    /// <remarks>
    /// Turn it off for an install with no way out to the internet, or for one whose operator would
    /// rather no visitor's address left the building for any reason. Everything else behaves
    /// identically: crawlers whose operators publish a list of addresses are still recognised from
    /// it, and the ones that publish only a domain are reported as having said what they said,
    /// which is the whole truth about them under this setting.
    /// </remarks>
    public bool Enabled { get; init; } = true;

    /// <summary>How long one address may take to settle before it is given up on.</summary>
    /// <remarks>
    /// Covers both halves of the question — the name the address answers to, and the addresses
    /// that name points back at — because a company whose name server is slow for the first is
    /// slow for the second, and two allowances would only make the pause twice as long.
    /// </remarks>
    public TimeSpan LookupTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How many addresses are asked about at the same time.
    /// </summary>
    /// <remarks>
    /// Enough that a pass with a few hundred questions finishes in seconds, and few enough that a
    /// site being judged from its beginning does not arrive at somebody's name server as a burst.
    /// </remarks>
    public int LookupsAtOnce { get; init; } = 8;

    /// <summary>How long an answer is reused before the address is asked about again.</summary>
    /// <remarks>
    /// Applies to both answers, and the negative one is the reason there is a limit at all: an
    /// address that belonged to nobody in particular this morning is the ordinary case, and asking
    /// again for every visit from it would turn one determined visitor into a stream of questions.
    /// A day is short enough that a machine a company has just commissioned is recognised the
    /// following day at the latest.
    /// </remarks>
    public TimeSpan RememberFor { get; init; } = TimeSpan.FromHours(24);

    /// <summary>
    /// How many answers are held at once.
    /// </summary>
    /// <remarks>
    /// Not a tuning knob but a bound on what a visitor can make this process hold. Addresses
    /// arrive from whoever is crawling the site, so without a limit a sweep from a large fleet
    /// would decide how much memory the engine uses. Fifty thousand is far past any real crawler
    /// fleet and costs a few megabytes.
    /// </remarks>
    public int RememberedAnswers { get; init; } = 50_000;
}
