namespace Dewiride.Analytics.Application.Accounts;

/// <summary>
/// Getting back into an account whose password has been forgotten.
/// </summary>
/// <remarks>
/// <para>
/// Two steps, and they are deliberately asymmetric. Asking for a link tells the caller nothing:
/// whether or not the address belongs to an account, the answer is the same, because an answer
/// that differed would turn the form into a way of listing who has an account on somebody's
/// installation. Following the link can fail honestly, because whoever is holding it was sent it.
/// </para>
/// <para>
/// The link carries a token the account store issues and verifies. It stops working when it is
/// used, because completing a reset changes the account's security stamp and the stamp is sealed
/// inside the token — so a link read out of a mailbox after the fact is worth nothing.
/// </para>
/// </remarks>
public interface IPasswordReset
{
    /// <summary>
    /// Sends a way back in, if the address belongs to an account.
    /// </summary>
    /// <remarks>
    /// Completes the same way whether it sent anything or not. Nothing about the outcome may
    /// reach the caller.
    /// </remarks>
    /// <param name="emailAddress">The address somebody typed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the request has been dealt with.</returns>
    Task BeginAsync(string emailAddress, CancellationToken cancellationToken);

    /// <summary>
    /// Sets a new password for somebody holding a link that is still good.
    /// </summary>
    /// <param name="request">The link's token, the address it was sent to, and the new password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether the password was changed, and why not when it was not.</returns>
    Task<PasswordResetOutcome> CompleteAsync(
        PasswordResetRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// What somebody following a reset link sends back.
/// </summary>
/// <param name="EmailAddress">The address the link was sent to.</param>
/// <param name="Token">The token from the link, exactly as it arrived.</param>
/// <param name="Password">The password to set.</param>
public sealed record PasswordResetRequest(string EmailAddress, string Token, string Password);

/// <summary>
/// How an attempt to complete a reset ended.
/// </summary>
public enum PasswordResetStatus
{
    /// <summary>The password was changed.</summary>
    Reset = 1,

    /// <summary>
    /// The link cannot be used: it has expired, it has already been used, or it was never valid.
    /// </summary>
    /// <remarks>
    /// One answer for all three, and for an address that belongs to no account. Telling them
    /// apart would say which addresses have accounts, and saying a link had "already been used"
    /// would confirm somebody had asked for one.
    /// </remarks>
    LinkNotUsable = 2,

    /// <summary>The link was good, but the password offered is not one this product accepts.</summary>
    PasswordRejected = 3,
}

/// <summary>
/// The result of completing a reset.
/// </summary>
/// <param name="Status">How it ended.</param>
/// <param name="Problems">
/// Why the password was refused, when it was. Empty for every other outcome: a link that cannot
/// be used is described by its status alone, because anything more would be a detail about
/// somebody else's account.
/// </param>
public sealed record PasswordResetOutcome(
    PasswordResetStatus Status,
    IReadOnlyList<AccountProblem> Problems)
{
    /// <summary>The outcome for a password that was changed.</summary>
    public static PasswordResetOutcome Reset { get; } = new(PasswordResetStatus.Reset, []);

    /// <summary>The outcome for a link that cannot be used, whatever the reason.</summary>
    public static PasswordResetOutcome LinkNotUsable { get; } =
        new(PasswordResetStatus.LinkNotUsable, []);

    /// <summary>Builds the outcome for a password this product will not accept.</summary>
    /// <param name="problems">Why it was refused.</param>
    /// <returns>The outcome.</returns>
    public static PasswordResetOutcome PasswordRejected(IReadOnlyList<AccountProblem> problems) =>
        new(PasswordResetStatus.PasswordRejected, problems);
}
