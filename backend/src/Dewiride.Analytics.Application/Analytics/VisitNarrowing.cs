using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Application.Analytics;

/// <summary>
/// What a caller narrowed a list of visits to.
/// </summary>
/// <remarks>
/// <para>
/// Two kinds of question live here, and the difference between them decides what answering one
/// costs. The first three are asked of the stored verdict — what generated the visit, how much
/// weight stands behind saying so, and how much of the site it went to — and cost nothing beyond
/// the rows they leave out. The nine after them are asked of the activity the verdict was formed
/// from, which has to be rebuilt before any of them can be compared against anything.
/// <see cref="ReadsActivity"/> is how that is decided once rather than dimension by dimension.
/// </para>
/// <para>
/// Every value is checked here rather than wherever it arrived. A member of a closed set is
/// refused unless the engine reaches it, and a free value is bounded by how many may be named
/// rather than by how long each one is: these are compared literally against what the store holds,
/// and a page's address or a network's name is as long as it is. None of it ever becomes text in a
/// statement.
/// </para>
/// <para>
/// The empty string is a value rather than an absence. It is exactly what the store holds where
/// nothing was established about a visitor's browser, whereabouts or network, so asking for it
/// asks to see the visits nothing is known about — which is a fair question, and a different one
/// from asking for all of them.
/// </para>
/// </remarks>
public sealed record VisitNarrowing
{
    /// <summary>
    /// Most values one dimension may be narrowed to at once.
    /// </summary>
    /// <remarks>
    /// A bound on the question rather than on the answer. Each named value is one more comparison
    /// against every row the store has rebuilt, and nobody picks two dozen countries off a list on
    /// purpose.
    /// </remarks>
    public const int MostValues = 25;

    private readonly ImmutableArray<TrafficCategory> categories = [];
    private readonly EvidenceStrength? leastStrength;
    private readonly int leastPages;
    private readonly ImmutableArray<DeviceClass> devices = [];
    private readonly ImmutableArray<SourceChannel> sourceKinds = [];
    private readonly ImmutableArray<string> browsers = [];
    private readonly ImmutableArray<string> operatingSystems = [];
    private readonly ImmutableArray<string> countries = [];
    private readonly ImmutableArray<string> towns = [];
    private readonly ImmutableArray<string> networks = [];
    private readonly ImmutableArray<string> sources = [];
    private readonly ImmutableArray<string> entryPages = [];

    /// <summary>Nothing asked for, which is every visit the window holds.</summary>
    public static VisitNarrowing Nothing { get; } = new();

    /// <summary>
    /// Which conclusions to return, or empty for all of them.
    /// </summary>
    /// <remarks>
    /// A set rather than one category, because the categories a reader thinks of as one thing —
    /// every kind of crawler, say — are several here and stay several. Collapsing them into groups
    /// on the way in would put a coarser vocabulary in front of the one the verdicts are stored in.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">A member is not a category the engine reaches.</exception>
    public ImmutableArray<TrafficCategory> Categories
    {
        get => categories;
        init => categories = Reached(value, "Narrow to categories the engine can conclude.");
    }

    /// <summary>
    /// The least weight a verdict must carry to be returned, or nothing for any weight at all.
    /// </summary>
    /// <remarks>
    /// A floor rather than an exact band. "Show me the ones there is real evidence for" is the
    /// question people actually have, and a band on its own answers a narrower one.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The band is not one the engine reaches.</exception>
    public EvidenceStrength? LeastStrength
    {
        get => leastStrength;

        init
        {
            if (value is not null && !Enum.IsDefined(value.Value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(LeastStrength),
                    "Narrow to a strength the engine reports.");
            }

            leastStrength = value;
        }
    }

    /// <summary>The fewest pages a visit must have gone to, or nought for every visit.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The figure is negative.</exception>
    public int LeastPages
    {
        get => leastPages;

        init
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(LeastPages),
                    "Ask for visits that went to no pages or more.");
            }

            leastPages = value;
        }
    }

    /// <summary>Which kinds of device to return, or empty for all of them.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A member is not a kind this product recognises.</exception>
    public ImmutableArray<DeviceClass> Devices
    {
        get => devices;
        init => devices = Reached(value, "Narrow to kinds of device this product recognises.");
    }

    /// <summary>Which kinds of place traffic was sent from, or empty for all of them.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A member is not a kind this product recognises.</exception>
    public ImmutableArray<SourceChannel> SourceKinds
    {
        get => sourceKinds;
        init => sourceKinds = Reached(value, "Narrow to kinds of source this product recognises.");
    }

    /// <summary>Which browsers to return, or empty for all of them.</summary>
    public ImmutableArray<string> Browsers
    {
        get => browsers;
        init => browsers = Named(value);
    }

    /// <summary>Which operating systems to return, or empty for all of them.</summary>
    public ImmutableArray<string> OperatingSystems
    {
        get => operatingSystems;
        init => operatingSystems = Named(value);
    }

    /// <summary>
    /// Which countries to return, as the two-letter codes the store holds, or empty for all of them.
    /// </summary>
    /// <remarks>
    /// Deliberately not a closed set. The store holds whatever the address catalogue resolved, and
    /// a table of every country code kept alive for the life of the product would be a list to
    /// maintain for no safety this does not already have: the value is bound, compared, and never
    /// written into a statement.
    /// </remarks>
    public ImmutableArray<string> Countries
    {
        get => countries;
        init => countries = Named(value);
    }

    /// <summary>Which towns and cities to return, or empty for all of them.</summary>
    public ImmutableArray<string> Towns
    {
        get => towns;
        init => towns = Named(value);
    }

    /// <summary>Which networks to return, named by whoever owns them, or empty for all of them.</summary>
    public ImmutableArray<string> Networks
    {
        get => networks;
        init => networks = Named(value);
    }

    /// <summary>Which sending sites to return, named as this product names them, or empty for all of them.</summary>
    public ImmutableArray<string> Sources
    {
        get => sources;
        init => sources = Named(value);
    }

    /// <summary>Which arrival pages to return, as addresses on the measured site, or empty for all of them.</summary>
    public ImmutableArray<string> EntryPages
    {
        get => entryPages;
        init => entryPages = Named(value);
    }

    /// <summary>
    /// Whether anything asked for has to be answered from the activity behind the verdicts.
    /// </summary>
    /// <remarks>
    /// The three narrowings on the verdict itself are a comparison against a row that already
    /// exists. The nine after them are not: what a visitor was on, where they came from and where
    /// they arrived are properties of their events, so a window's visits have to be rebuilt before
    /// any of it can be compared. Asked once here, so the whole period is rebuilt only when the
    /// question genuinely needs it; the page a list shows is rebuilt regardless, because every row
    /// carries what its visit was.
    /// </remarks>
    public bool ReadsActivity =>
        !devices.IsEmpty
        || !sourceKinds.IsEmpty
        || !browsers.IsEmpty
        || !operatingSystems.IsEmpty
        || !countries.IsEmpty
        || !towns.IsEmpty
        || !networks.IsEmpty
        || !sources.IsEmpty
        || !entryPages.IsEmpty;

    /// <summary>
    /// Accepts members of a closed set, or refuses one the engine never produces.
    /// </summary>
    /// <remarks>
    /// The set bounds itself, so there is nothing here to cap: a caller cannot name more distinct
    /// values than the engine has to give.
    /// </remarks>
    /// <typeparam name="T">The set being narrowed to.</typeparam>
    /// <param name="value">What was asked for.</param>
    /// <param name="refusal">What to say when it is not something the engine reaches.</param>
    /// <param name="dimension">Which narrowing was being set, supplied by the compiler.</param>
    /// <returns>What was asked for, or an empty set where nothing was.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A member is not one the engine reaches.</exception>
    private static ImmutableArray<T> Reached<T>(
        ImmutableArray<T> value,
        string refusal,
        [CallerMemberName] string dimension = "")
        where T : struct, Enum
    {
        if (value.IsDefaultOrEmpty)
        {
            return [];
        }

        if (value.Any(member => !Enum.IsDefined(member)))
        {
            throw new ArgumentOutOfRangeException(dimension, refusal);
        }

        return value;
    }

    /// <summary>
    /// Accepts values to compare against what the store holds, or refuses more than anyone means.
    /// </summary>
    /// <remarks>
    /// Bounded by how many were named and never by how long each one is. A page's address, a
    /// network's name and a browser's name are as long as they are, and refusing a long one would
    /// refuse a real question about a real visit.
    /// </remarks>
    /// <param name="value">What was asked for.</param>
    /// <param name="dimension">Which narrowing was being set, supplied by the compiler.</param>
    /// <returns>What was asked for, or an empty set where nothing was.</returns>
    /// <exception cref="ArgumentOutOfRangeException">More values were named than one question may carry.</exception>
    private static ImmutableArray<string> Named(
        ImmutableArray<string> value,
        [CallerMemberName] string dimension = "")
    {
        if (value.IsDefaultOrEmpty)
        {
            return [];
        }

        if (value.Length > MostValues)
        {
            throw new ArgumentOutOfRangeException(
                dimension,
                $"Narrow to at most {MostValues} values at once.");
        }

        return value;
    }
}
