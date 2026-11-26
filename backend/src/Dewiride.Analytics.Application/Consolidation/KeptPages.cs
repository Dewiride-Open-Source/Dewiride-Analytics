namespace Dewiride.Analytics.Application.Consolidation;

/// <summary>
/// What a month keeps of a website's pages: its busiest paths, and how many others there were.
/// </summary>
/// <remarks>
/// <para>
/// A path is whatever was asked for, so how many a month holds is up to whoever asks, not the website.
/// A month therefore keeps the paths most viewed by everybody and the paths most viewed by the visits
/// judged to be people — automation can crowd out the first list, and the second still holds the pages
/// people read — and keeps every other path only as totals. Of two paths viewed as often, the one
/// whose bytes come first is kept.
/// </para>
/// <para>
/// Only a path at least two different visitors were delivered a page at in the month can be kept, so a
/// page only one visitor opened — often their own search, order or account page — is never kept. A
/// visitor here is a visitor key, which is rebuilt every day, so one person who opened a page on two
/// days is two. A path longer than any a month keeps is left out however busy it was: it can only have
/// reached that length through the escaping of the letters it was written in.
/// </para>
/// <para>
/// The figures are written into the statement that counts a month's pages as they are, so the test that
/// holds <see cref="CountingRules.Pages"/> to that statement fails when one of them changes.
/// </para>
/// </remarks>
public static class KeptPages
{
    /// <summary>How many of the paths most viewed by each population a month keeps.</summary>
    public const int MostPerPopulation = 5_000;

    /// <summary>The longest path, in characters, a month keeps.</summary>
    public const int LongestPath = 2_048;

    /// <summary>
    /// How many different visitor keys have to have been delivered a page at a path in a month for the
    /// month to keep it.
    /// </summary>
    public const int FewestVisitors = 2;
}
