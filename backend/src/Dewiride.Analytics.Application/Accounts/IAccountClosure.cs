namespace Dewiride.Analytics.Application.Accounts;

/// <summary>
/// Closing an account, bringing it back, and describing a closed one to somebody in it.
/// </summary>
/// <remarks>
/// <para>
/// Closing is the organisation's act, not a person's: measurement stops for every website it
/// owns, nobody in it can open its screens, and a clock starts. For <see cref="Domain.Sites.Organization.ClosureRetention"/>
/// everything is kept exactly as it was so that an owner who signs in can bring it back with one
/// press; only when that time has run is anything deleted, by the sweep that runs on its own.
/// </para>
/// <para>
/// Ownership is established here, from where the caller stands, and never trusted from whoever
/// called; whether the organisation that standing names is still there, and still open or closed,
/// is decided under the same lock the sweep takes. The organisation somebody closes is the one
/// they are in — an open one whenever they have one — and the organisation somebody restores is
/// the closed one they belong to, so the account described to them on the closed screen is the
/// account their button acts on.
/// </para>
/// </remarks>
public interface IAccountClosure
{
    /// <summary>
    /// Closes the organisation somebody belongs to.
    /// </summary>
    /// <param name="userId">The person asking.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see cref="ClosureOutcome.Closed"/>, or why not: no organisation, not its owner, or already closed.
    /// </returns>
    Task<ClosureOutcome> CloseAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Brings back the closed organisation somebody belongs to, exactly as it was.
    /// </summary>
    /// <param name="userId">The person asking.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see cref="ClosureOutcome.Restored"/>, or why not: no organisation, not its owner, or not closed.
    /// </returns>
    Task<ClosureOutcome> RestoreAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Describes the closed account somebody is in, if they are in one.
    /// </summary>
    /// <remarks>
    /// Present whenever the person belongs to a closed organisation, whether or not they also
    /// belong to an open one; <see cref="ClosedAccount.HasOpenAccount"/> says which, and is what
    /// decides whether the closure is the only thing they can see or a line beside what they can.
    /// </remarks>
    /// <param name="userId">The person asking.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The closed account, or <see langword="null"/> where they belong to none.</returns>
    Task<ClosedAccount?> DescribeForAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>What came of closing or restoring an account.</summary>
public enum ClosureOutcome
{
    /// <summary>It is closed.</summary>
    Closed = 1,

    /// <summary>It is open again.</summary>
    Restored = 2,

    /// <summary>The person belongs to no organisation.</summary>
    NoSuchAccount = 3,

    /// <summary>The person belongs to it but does not own it.</summary>
    NotOwner = 4,

    /// <summary>It was closed already, so nothing changed.</summary>
    AlreadyClosed = 5,

    /// <summary>It is open, so there is nothing to bring back.</summary>
    NotClosed = 6,
}

/// <summary>
/// A closed account as it is explained to somebody in it.
/// </summary>
/// <param name="Name">What it is called.</param>
/// <param name="ClosedAt">When it was closed.</param>
/// <param name="DeletionDue">When everything about it is deleted unless it is brought back first.</param>
/// <param name="ClosedBy">The name of whoever closed it, or <see langword="null"/> where that is no longer known.</param>
/// <param name="ClosedByYou">Whether the person asking is the one who closed it.</param>
/// <param name="CanRestore">Whether the person asking may bring it back.</param>
/// <param name="HasOpenAccount">
/// Whether the person asking has somewhere open to be — a standing in an open organisation or a
/// grant on one of its sites — so that the closure is a line beside their dashboard rather than
/// the only thing they can see.
/// </param>
public readonly record struct ClosedAccount(
    string Name,
    DateTimeOffset ClosedAt,
    DateTimeOffset DeletionDue,
    string? ClosedBy,
    bool ClosedByYou,
    bool CanRestore,
    bool HasOpenAccount);
