namespace Dewiride.Analytics.Application.Consolidation;

/// <summary>
/// Which version of the arithmetic a kept month was counted with.
/// </summary>
/// <remarks>
/// <para>
/// Raised whenever the statements that count a month change what they count — a correction to how
/// a page delivered is recognised, a change to who is a visitor. A month kept under an older
/// version is counted again while its reports remain, and keeps the old arithmetic once they are
/// gone, saying which it was.
/// </para>
/// <para>
/// A test holds this to the statements themselves, so a change to any fragment they share with the
/// dashboard's own statements fails until the version is raised with it.
/// </para>
/// </remarks>
public static class CountingRules
{
    /// <summary>The version this build counts with.</summary>
    public const int Current = 1;

    /// <summary>The version this build counts a month's posts with.</summary>
    /// <remarks>
    /// A number of its own, held by a test of its own to the statements that count a month's posts, so
    /// a change to how posts are counted recounts the months of posts still held and none of the
    /// months' totals, and the other way round.
    /// </remarks>
    public const int Posts = 1;
}
