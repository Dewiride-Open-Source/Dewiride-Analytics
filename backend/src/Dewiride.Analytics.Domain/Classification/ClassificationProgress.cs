namespace Dewiride.Analytics.Domain.Classification;

/// <summary>
/// How far the engine has judged one site's traffic under one set of rules.
/// </summary>
/// <remarks>
/// <para>
/// A bookmark, not a queue. Sessions are reconstructed from the events themselves, so the only
/// thing that has to be remembered between runs is where to resume — and remembering two instants
/// rather than a list of outstanding work means an interrupted run resumes correctly whatever it
/// had already done, and a run that judges the same session twice costs a little time and changes
/// nothing.
/// </para>
/// <para>
/// Two instants, because a visit still in progress and the reading of everything after it are two
/// different things to wait for. Judging cannot step past a visit that has not finished, or that
/// visit would never be judged; reading has no such reason to stop, and a bookmark that kept only
/// the first would make one visitor who leaves a page open all afternoon the reason nothing after
/// them is judged until they close it.
/// </para>
/// <para>
/// Each instant only ever moves forward. Two instances of the engine may work the same site without
/// coordinating, and one of them may be running a moment behind; a bookmark that could move
/// backwards would send it over the same stretch for ever.
/// </para>
/// <para>
/// Kept per ruleset. Improving the rules does not rewrite what the old ones concluded; it starts
/// a second bookmark, and the stored verdicts sit beside each other so a number can still be
/// attributed to the rules that produced it.
/// </para>
/// </remarks>
public sealed class ClassificationProgress
{
    /// <summary>The site being judged.</summary>
    public Guid SiteId { get; private set; }

    /// <summary>Major component of the ruleset this bookmark belongs to.</summary>
    public int RulesetMajor { get; private set; }

    /// <summary>Minor component of the ruleset this bookmark belongs to.</summary>
    public int RulesetMinor { get; private set; }

    /// <summary>
    /// Every session that began before this instant has been judged.
    /// </summary>
    /// <remarks>
    /// It stops at the earliest visit that had not finished when it was last read, and sessions
    /// that began after it have been judged too wherever they had finished — see
    /// <see cref="ReadThrough"/>.
    /// </remarks>
    public DateTimeOffset ClassifiedThrough { get; private set; }

    /// <summary>
    /// Every session that began before this instant has been read.
    /// </summary>
    /// <remarks>
    /// The ones that had finished when they were read were judged then, and any that had not began
    /// no earlier than <see cref="ClassifiedThrough"/>, which is where the next reading starts from
    /// so that they are found again. Everything judged has been read, so wherever this is earlier
    /// than <see cref="ClassifiedThrough"/> the reading is taken to have got that far.
    /// </remarks>
    public DateTimeOffset ReadThrough { get; private set; }

    /// <summary>When the bookmark last moved.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    private ClassificationProgress()
    {
    }

    /// <summary>Starts a bookmark.</summary>
    /// <param name="siteId">The site being judged.</param>
    /// <param name="rulesetMajor">Major component of the ruleset.</param>
    /// <param name="rulesetMinor">Minor component of the ruleset.</param>
    /// <param name="startingAt">Where judging begins. Normally the moment the site was added.</param>
    public ClassificationProgress(Guid siteId, int rulesetMajor, int rulesetMinor, DateTimeOffset startingAt)
    {
        SiteId = siteId;
        RulesetMajor = rulesetMajor;
        RulesetMinor = rulesetMinor;
        ClassifiedThrough = startingAt;
        ReadThrough = startingAt;
        UpdatedAt = startingAt;
    }
}
