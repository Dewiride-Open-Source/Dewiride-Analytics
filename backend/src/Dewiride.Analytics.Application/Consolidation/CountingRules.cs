namespace Dewiride.Analytics.Application.Consolidation;

/// <summary>
/// Which version of the arithmetic a kept month was counted with.
/// </summary>
/// <remarks>
/// <para>
/// Raised whenever the statements that count a month change what they count — a correction to how
/// a page delivered is recognised, a change to who is a visitor — and whenever a month comes to keep
/// a figure that rows counted by an older version lack, even where the statements already count
/// it: version 3 keeps each month's best hour (ADR 0079). A month kept under an older version is
/// counted again while its reports remain, and keeps the old arithmetic once they are gone, saying
/// which it was.
/// </para>
/// <para>
/// A test holds this to the statements themselves, so a change to any fragment they share with the
/// dashboard's own statements fails until the version is raised with it.
/// </para>
/// </remarks>
public static class CountingRules
{
    /// <summary>The version this build counts with.</summary>
    public const int Current = 3;

    /// <summary>The version this build counts a month's posts with.</summary>
    /// <remarks>
    /// A number of its own, held by a test of its own to the statements that count a month's posts, so
    /// a change to how posts are counted recounts the months of posts still held and none of the
    /// months' totals, and the other way round.
    /// </remarks>
    public const int Posts = 2;

    /// <summary>The version this build counts a month's busiest pages with.</summary>
    /// <remarks>
    /// A number of its own on the terms <see cref="Posts"/> is, held by a test of its own to the
    /// statement that counts a month's pages — the bound <see cref="KeptPages"/> sets included — so a
    /// change to how pages are counted recounts the months of pages still held, and neither the months'
    /// totals nor their posts.
    /// </remarks>
    public const int Pages = 1;
}
