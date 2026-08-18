namespace Dewiride.Analytics.Domain.Classification;

/// <summary>
/// How far the engine has judged one site's traffic under one set of rules.
/// </summary>
/// <remarks>
/// <para>
/// A bookmark, not a queue. Sessions are reconstructed from the events themselves, so the only
/// thing that has to be remembered between runs is where to resume — and remembering an instant
/// rather than a list of outstanding work means an interrupted run resumes correctly whatever it
/// had already done, and a run that judges the same session twice costs a little time and changes
/// nothing.
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
    /// Sessions that began after it may also have been judged — a run judges everything it can
    /// see and then moves the bookmark only as far as the earliest visit that had not finished,
    /// so that one long visit does not hold up everything behind it.
    /// </remarks>
    public DateTimeOffset ClassifiedThrough { get; private set; }

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
        UpdatedAt = startingAt;
    }

    /// <summary>
    /// Moves the bookmark forward.
    /// </summary>
    /// <remarks>
    /// Forward only. Two instances of the engine may work the same site without coordinating —
    /// they reach the same conclusions from the same events and store them idempotently — but one
    /// of them may be running a moment behind, and a bookmark that could move backwards would
    /// make it judge the same stretch for ever.
    /// </remarks>
    /// <param name="instant">The instant to move to.</param>
    /// <param name="at">When the move happened, from the injected clock.</param>
    /// <returns><see langword="true"/> when the bookmark moved.</returns>
    public bool AdvanceTo(DateTimeOffset instant, DateTimeOffset at)
    {
        if (instant <= ClassifiedThrough)
        {
            return false;
        }

        ClassifiedThrough = instant;
        UpdatedAt = at;

        return true;
    }
}
