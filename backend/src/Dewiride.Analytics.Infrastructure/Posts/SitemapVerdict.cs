using Dewiride.Analytics.Application.Posts;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// What a reading of a sitemap came to, once there is nothing left to ask for.
/// </summary>
/// <remarks>
/// <para>
/// A reading is complete when every part it was asked to read answered and all of what it listed was
/// taken in, and only a complete reading may say a page has stopped being listed. Anything less has not
/// seen enough to say so.
/// </para>
/// <para>
/// A part that lists an address off the website is complete all the same. What it lists on the website
/// has all been read, and a sitemap with one stray entry would otherwise never be able to say a post was
/// gone. It is still worth saying, because the website's owner can do something about it. A sitemap that
/// lists nothing but such addresses is different: it has said nothing about the website at all, and that
/// is not the same as saying its posts have gone.
/// </para>
/// <para>
/// The first thing that went wrong is the one reported, in the order the parts were found, and a list of
/// parts that was itself cut short or led elsewhere comes after anything that went wrong reading what it
/// named. Addresses told apart only by a query are only a problem when nothing else was found: a website
/// with some of each has its readable posts counted and nothing to fix for them.
/// </para>
/// </remarks>
/// <param name="State">How it stands.</param>
/// <param name="Problem">What got in the way, where anything did.</param>
/// <param name="Complete">Whether it saw enough to say what has stopped being listed.</param>
internal sealed record SitemapVerdict(SitemapState State, SitemapProblem? Problem, bool Complete)
{
    /// <summary>
    /// Works out what a reading came to.
    /// </summary>
    /// <param name="parts">How each part stands, in the order they were found.</param>
    /// <param name="listProblem">What was wrong with the list of parts, where anything was.</param>
    /// <returns>What it came to.</returns>
    public static SitemapVerdict Of(IReadOnlyList<PartStanding> parts, SitemapProblem? listProblem)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var failed = parts.Where(part => !part.WasRead).ToList();
        var problem = failed.Count > 0 ? SitemapProblems.Of(failed[0].Outcome) : listProblem ?? ProblemOfWhatWasListed(parts);

        if (problem is null)
        {
            return new SitemapVerdict(SitemapState.Read, null, Complete: true);
        }

        var complete = failed.Count == 0 && listProblem != SitemapProblem.TooMany && !LedOnlyOffTheWebsite(parts);

        return new SitemapVerdict(StateOf(parts, failed), problem, complete);
    }

    private static bool LedOnlyOffTheWebsite(IReadOnlyList<PartStanding> parts) =>
        parts.Any(part => part.OffSiteEntries > 0) && parts.All(part => part.Entries == 0);

    private static SitemapProblem? ProblemOfWhatWasListed(IReadOnlyList<PartStanding> parts)
    {
        if (parts.Any(part => part.OffSiteEntries > 0))
        {
            return SitemapProblem.LeftTheWebsite;
        }

        return parts.Any(part => part.QueryEntries > 0) && parts.All(part => part.Entries == 0)
            ? SitemapProblem.AddressesWithQueries
            : null;
    }

    /// <summary>
    /// A reading where nothing at all got through has failed. A part cut short by the limit on how much
    /// is kept is not that, since all it could hold was read.
    /// </summary>
    private static SitemapState StateOf(IReadOnlyList<PartStanding> parts, List<PartStanding> failed)
    {
        var nothingGotThrough = failed.Count > 0
            && failed.Count == parts.Count
            && failed.TrueForAll(part => part.Outcome != SitemapPartOutcome.TooMany);

        return nothingGotThrough || LedOnlyOffTheWebsite(parts) ? SitemapState.Failed : SitemapState.PartlyRead;
    }
}

/// <summary>
/// How one part of a sitemap stands at the end of a reading.
/// </summary>
/// <param name="Outcome">What came of asking for it.</param>
/// <param name="Entries">How many pages it listed.</param>
/// <param name="OffSiteEntries">How many of its entries led off the website.</param>
/// <param name="QueryEntries">How many of its entries were told apart only by a query string.</param>
internal readonly record struct PartStanding(
    SitemapPartOutcome Outcome,
    int Entries,
    int OffSiteEntries,
    int QueryEntries)
{
    /// <summary>Whether everything it lists is known.</summary>
    public bool WasRead => Outcome is SitemapPartOutcome.Read or SitemapPartOutcome.Unchanged;
}
