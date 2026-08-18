namespace Dewiride.Analytics.Classification;

/// <summary>
/// How much weight stands behind a classification.
/// </summary>
/// <remarks>
/// <para>
/// This is a discrete band with a stated meaning, deliberately <b>not</b> a percentage.
/// A calibrated probability requires labelled data with a known base rate; until that
/// exists, the detector weights are informed priors and any number derived from them would
/// look like a measurement while being an opinion. Publishing "94% confident" on that basis
/// is precisely the false precision this product exists to argue against, so the type system
/// makes it unavailable rather than merely discouraged.
/// </para>
/// <para>
/// A further reason bands are correct today: an additive score treats a missing signal as
/// contributing zero, which is only sound if the weights were fitted with that signal
/// missing. Sessions arrive with very different evidence available depending on capture
/// surface, so a single scale across all of them would not mean the same thing twice.
/// </para>
/// <para>
/// When real labelled traffic exists, calibrated probabilities can be introduced per signal
/// profile. That is a deliberate later step, recorded in
/// <c>docs/adr/0009-evidence-bands-not-probabilities.md</c>.
/// </para>
/// </remarks>
public enum EvidenceStrength
{
    /// <summary>Nothing to weigh. Pairs with <see cref="TrafficCategory.InsufficientEvidence"/>.</summary>
    None = 0,

    /// <summary>
    /// Something points this way, but a single ordinary circumstance would explain it away.
    /// Shown to users as needing corroboration.
    /// </summary>
    Weak = 1,

    /// <summary>Several independent signals agree and no strong signal contradicts them.</summary>
    Moderate = 2,

    /// <summary>
    /// Multiple independent signals agree, including at least one that is difficult to
    /// produce accidentally.
    /// </summary>
    Strong = 3,

    /// <summary>
    /// Identity was established by a means that is not open to inference — a request from an
    /// address range the operator publishes, confirmed by forward-verified reverse DNS.
    /// Reserved for verified identity; behavioural evidence alone never reaches this band.
    /// </summary>
    Verified = 4,
}
