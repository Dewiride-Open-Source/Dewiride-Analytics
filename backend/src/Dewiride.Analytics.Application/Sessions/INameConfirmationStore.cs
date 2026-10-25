namespace Dewiride.Analytics.Application.Sessions;

/// <summary>
/// Keeps whose crawler a visit was, where the engine settled that from the name its address
/// answers to.
/// </summary>
/// <remarks>
/// <para>
/// The address an answer like that is worked out from is cleared three days after it was
/// collected, and history is judged again whenever the rules improve. Without the answer kept, a
/// visit judged a second time would find nothing left to ask about and go back to being the claim
/// it had already been shown to be more than. The visit reconstruction reads what is kept here
/// wherever the activity itself does not say who vouched for it.
/// </para>
/// <para>
/// Keyed by the visit, never by the visitor: on a rented network one visitor key can stand for a
/// whole network's day, and an answer spread across all of it would vouch for activity whose own
/// address was never checked. Nothing here is personal data — the company is kept, and the address
/// it was established from is not.
/// </para>
/// </remarks>
public interface INameConfirmationStore
{
    /// <summary>
    /// Records answers.
    /// </summary>
    /// <remarks>
    /// Writing the same answer twice leaves one, and a later answer for a visit replaces an earlier
    /// one, so a pass that is interrupted and run again, or run by two instances of the engine at
    /// once, keeps one answer per visit.
    /// </remarks>
    /// <param name="siteId">The site the visits belong to.</param>
    /// <param name="confirmations">Which visit, and whose crawler it turned out to be.</param>
    /// <param name="settledAt">When the pass that settled them began.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the answers are stored.</returns>
    Task RecordAsync(
        Guid siteId,
        IReadOnlyCollection<NameConfirmation> confirmations,
        DateTimeOffset settledAt,
        CancellationToken cancellationToken);
}

/// <summary>
/// Whose crawler one visit turned out to be.
/// </summary>
/// <param name="SessionKey">The visit's derived identity.</param>
/// <param name="StartedAt">When the visit began, which is how the store finds it again.</param>
/// <param name="Operator">The company the visit's address answers for.</param>
public readonly record struct NameConfirmation(string SessionKey, DateTimeOffset StartedAt, string Operator);
