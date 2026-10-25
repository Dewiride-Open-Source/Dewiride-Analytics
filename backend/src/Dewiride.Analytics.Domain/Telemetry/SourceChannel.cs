namespace Dewiride.Analytics.Domain.Telemetry;

/// <summary>
/// What kind of thing sent a visitor.
/// </summary>
/// <remarks>
/// <para>
/// A closed set, and every member is an answer rather than a gap. A screen works the kind out
/// whenever it asks — compiled into a statement, compared against a column, read back — so
/// correcting the catalogue behind it re-answers every period a site has ever recorded instead of
/// only the ones recorded after the correction.
/// </para>
/// <para>
/// A verdict is the one place a kind is kept. The engine weighs a visit another site sent as a
/// faint sign of somebody reading, and a verdict carries the kind it was judged with among its
/// reasons until history is next judged again. A change to how a sender is recognised therefore
/// moves verdicts as well as screens, and ships with new rules rather than on its own.
/// </para>
/// </remarks>
public enum SourceChannel
{
    /// <summary>
    /// The visitor's browser named nowhere at all.
    /// </summary>
    /// <remarks>
    /// Typing the address, opening a bookmark, following a link from an application, and arriving
    /// from a site that withholds the address are indistinguishable, and this covers all of them.
    /// It is not "nobody sent them": it is "nothing said who did".
    /// </remarks>
    Direct = 1,

    /// <summary>A search engine.</summary>
    Search = 2,

    /// <summary>
    /// A conversational assistant.
    /// </summary>
    /// <remarks>
    /// Kept apart from search rather than folded into it. Somebody who arrives having been told
    /// about a page did not read a list of results and choose it, and a product whose subject is
    /// telling machinery and people apart should not be the one to blur that.
    /// </remarks>
    Assistant = 3,

    /// <summary>A social network.</summary>
    Social = 4,

    /// <summary>An ordinary link on some other website.</summary>
    Link = 5,
}
