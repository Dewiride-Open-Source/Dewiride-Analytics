namespace Dewiride.Analytics.Application.Accounts;

/// <summary>
/// Something that has to happen alongside an account being closed, brought back, or deleted.
/// </summary>
/// <remarks>
/// <para>
/// The free product closes an account completely on its own; what an edition adds here is
/// whatever has no meaning on a server somebody runs themselves — an arrangement with a payment
/// company, say. Any number may be registered, including none, and none is the ordinary outcome.
/// </para>
/// <para>
/// <see cref="ClosingAsync"/> and <see cref="RestoringAsync"/> run inside the transaction that
/// closes or restores the account, before it commits, so an observer that throws refuses the act
/// and leaves the account as it was. <see cref="PurgingAsync"/> runs after the account's telemetry
/// and rows have been deleted and before that deletion commits, so that the one thing an observer
/// does which cannot be undone is the last thing attempted. An observer's own writes commit on
/// their own: it must be idempotent, because a purge that fails after it ran is run again, and it
/// must not assume that a failed purge undid what it did.
/// </para>
/// </remarks>
public interface IAccountClosureObserver
{
    /// <summary>The account is being closed.</summary>
    /// <param name="organizationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ClosingAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>The account is being brought back.</summary>
    /// <param name="organizationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RestoringAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>The account is being deleted for good.</summary>
    /// <param name="organizationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PurgingAsync(Guid organizationId, CancellationToken cancellationToken);
}
