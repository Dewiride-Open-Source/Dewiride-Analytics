namespace Dewiride.Analytics.Application.Accounts;

/// <summary>
/// Changing the two things somebody may change about their own account.
/// </summary>
/// <remarks>
/// <para>
/// The name they are shown under, and their password. The address is not among them: it is what
/// every link this product sends is addressed to and what the account is looked up by, so moving
/// it is a confirmed change of address rather than an edit, and nothing yet asks for one.
/// </para>
/// <para>
/// Changing a password requires the current one even though the caller is already signed in.
/// A cookie left open on a shared machine is the case this guards against, and it is the one case
/// where being signed in is not evidence of being the account holder.
/// </para>
/// </remarks>
public interface IAccountProfile
{
    /// <summary>Changes the name somebody is shown under.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="displayName">The name to show.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What came of it.</returns>
    Task<ProfileOutcome> RenameAsync(Guid userId, string displayName, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces somebody's password.
    /// </summary>
    /// <remarks>
    /// Every sign-in already issued elsewhere stops working within minutes, because setting a
    /// password rotates the stamp every cookie is checked against and that check is repeated on a
    /// short interval. Somebody changing a password because they fear it is known needs exactly
    /// that, and it is why this is not merely a stored value.
    /// </remarks>
    /// <param name="userId">The account.</param>
    /// <param name="currentPassword">The password they have now.</param>
    /// <param name="replacement">The password to set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What came of it.</returns>
    Task<ProfileOutcome> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string replacement,
        CancellationToken cancellationToken);
}

/// <summary>What came of changing something about an account.</summary>
public enum ProfileStatus
{
    /// <summary>It was changed.</summary>
    Changed = 1,

    /// <summary>There is no such account.</summary>
    NoSuchAccount = 2,

    /// <summary>The name is not one an account can be shown under.</summary>
    NameRejected = 3,

    /// <summary>
    /// The current password does not match, so nothing was changed.
    /// </summary>
    /// <remarks>
    /// Kept apart from a refused new password because the two need different things done about
    /// them, and because this one is not about the password being offered at all.
    /// </remarks>
    CurrentPasswordWrong = 4,

    /// <summary>The new password is not one this installation accepts.</summary>
    PasswordRejected = 5,
}

/// <summary>
/// The result of changing something about an account.
/// </summary>
/// <param name="Status">What came of it.</param>
/// <param name="Problems">Why a password was refused, where it was.</param>
public readonly record struct ProfileOutcome(ProfileStatus Status, IReadOnlyList<AccountProblem> Problems)
{
    /// <summary>Reports a change that was made.</summary>
    /// <returns>The result.</returns>
    public static ProfileOutcome Changed() => new(ProfileStatus.Changed, []);

    /// <summary>Reports a refusal that carries no detail.</summary>
    /// <param name="status">Why it was refused.</param>
    /// <returns>The result.</returns>
    public static ProfileOutcome Refused(ProfileStatus status) => new(status, []);
}
