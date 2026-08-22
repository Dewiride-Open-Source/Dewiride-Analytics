namespace Dewiride.Analytics.Domain.Sites;

/// <summary>Where an invitation stands.</summary>
public enum InvitationState
{
    /// <summary>Sent, and still usable.</summary>
    Pending = 1,

    /// <summary>Taken up. The person it was sent to belongs to the organisation.</summary>
    Accepted = 2,

    /// <summary>Withdrawn before it was taken up.</summary>
    Revoked = 3,

    /// <summary>Left long enough that it no longer works.</summary>
    Expired = 4,
}

/// <summary>
/// An offer of a standing in an organisation, sent to an address.
/// </summary>
/// <remarks>
/// <para>
/// It is the only way a second person joins an installation, and it is deliberately an offer
/// rather than an act: no account is created until whoever holds the address takes it up. Creating
/// one on their behalf would mean anybody who could name an address could claim it, and on a
/// service running many organisations it would also answer, from whether the attempt succeeded,
/// whether that address already had an account.
/// </para>
/// <para>
/// Only a hash of the secret is held, for the reason a key's is: a stolen copy of the control
/// plane must not hand over the ability to join somebody else's account. The row survives being
/// taken up or withdrawn, because who was invited and by whom is part of the account's history and
/// is the first thing anybody looks for after an unexpected arrival.
/// </para>
/// </remarks>
public sealed class OrganizationInvitation
{
    /// <summary>Longest address an invitation may be sent to.</summary>
    public const int MaxEmailAddressLength = 256;

    /// <summary>
    /// How long an invitation works for.
    /// </summary>
    /// <remarks>
    /// Long enough to survive a week away and short enough that a forgotten one in a mailbox stops
    /// being a way in. Sending another to the same address renews it rather than adding a second.
    /// </remarks>
    public static readonly TimeSpan Life = TimeSpan.FromDays(7);

    /// <summary>Identity of the invitation. Safe to show; it is not the secret.</summary>
    public Guid Id { get; private set; }

    /// <summary>Organisation the standing would be held in.</summary>
    public Guid OrganizationId { get; private set; }

    /// <summary>Address it was sent to, as whoever sent it typed it.</summary>
    public string EmailAddress { get; private set; }

    /// <summary>
    /// The same address in the form it is matched under.
    /// </summary>
    /// <remarks>
    /// Kept beside the typed form rather than replacing it, exactly as the account store keeps
    /// both. Two addresses differing only in case are one mailbox, so matching has to ignore case
    /// — and a list that showed somebody their colleague shouting at them would be the price of
    /// storing only the matched form.
    /// </remarks>
    public string NormalizedEmailAddress { get; private set; }

    /// <summary>The standing being offered.</summary>
    public OrganizationRole Role { get; private set; }

    /// <summary>Who sent it.</summary>
    public Guid InvitedByUserId { get; private set; }

    /// <summary>Hash of the secret, which is the only form of it that is stored.</summary>
    public string TokenHash { get; private set; }

    /// <summary>When it was last sent.</summary>
    public DateTimeOffset InvitedAt { get; private set; }

    /// <summary>When it stops working.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When it was taken up, or <see langword="null"/> while it has not been.</summary>
    public DateTimeOffset? AcceptedAt { get; private set; }

    /// <summary>When it was withdrawn, or <see langword="null"/> while it has not been.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    private OrganizationInvitation()
    {
        EmailAddress = string.Empty;
        NormalizedEmailAddress = string.Empty;
        TokenHash = string.Empty;
    }

    /// <summary>Offers a standing to an address.</summary>
    /// <param name="id">Identity to assign.</param>
    /// <param name="organizationId">Organisation the standing would be held in.</param>
    /// <param name="emailAddress">Address to send it to.</param>
    /// <param name="role">The standing to offer.</param>
    /// <param name="invitedByUserId">Who is sending it.</param>
    /// <param name="tokenHash">Hash of the generated secret.</param>
    /// <param name="invitedAt">Send time, from the injected clock.</param>
    /// <exception cref="ArgumentException">The address or the hash is empty or whitespace.</exception>
    public OrganizationInvitation(
        Guid id,
        Guid organizationId,
        string emailAddress,
        OrganizationRole role,
        Guid invitedByUserId,
        string tokenHash,
        DateTimeOffset invitedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(emailAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        Id = id;
        OrganizationId = organizationId;
        EmailAddress = emailAddress.Trim();
        NormalizedEmailAddress = Normalise(emailAddress);
        Role = role;
        InvitedByUserId = invitedByUserId;
        TokenHash = tokenHash;
        InvitedAt = invitedAt;
        ExpiresAt = invitedAt + Life;
    }

    /// <summary>
    /// Writes an address in the form invitations are stored and matched under.
    /// </summary>
    /// <remarks>
    /// Upper case, matching how the account store normalises the addresses it holds. An invitation
    /// and an account that differ only in case are the same mailbox, and a comparison that missed
    /// that would let one address hold two standings.
    /// </remarks>
    /// <param name="emailAddress">The address as it was typed.</param>
    /// <returns>The address as it is matched.</returns>
    /// <exception cref="ArgumentNullException">No address was given.</exception>
    public static string Normalise(string emailAddress)
    {
        ArgumentNullException.ThrowIfNull(emailAddress);

        return emailAddress.Trim().ToUpperInvariant();
    }

    /// <summary>Where the invitation stands at a given moment.</summary>
    /// <param name="now">The moment, from the injected clock.</param>
    /// <returns>Its state.</returns>
    public InvitationState StateAt(DateTimeOffset now)
    {
        if (AcceptedAt is not null)
        {
            return InvitationState.Accepted;
        }

        if (RevokedAt is not null)
        {
            return InvitationState.Revoked;
        }

        return now >= ExpiresAt ? InvitationState.Expired : InvitationState.Pending;
    }

    /// <summary>
    /// Sends it again, with a fresh secret and possibly a different standing.
    /// </summary>
    /// <remarks>
    /// Renewing rather than adding a second means the list somebody reads never shows one address
    /// twice, and that the older secret stops working the moment a newer one is sent. Somebody who
    /// left and is asked back arrives here too, which is why being taken up before is cleared along
    /// with being withdrawn.
    /// </remarks>
    /// <param name="role">The standing to offer this time.</param>
    /// <param name="tokenHash">Hash of the new secret.</param>
    /// <param name="invitedAt">Send time, from the injected clock.</param>
    /// <exception cref="ArgumentException">The hash is empty or whitespace.</exception>
    public void Renew(OrganizationRole role, string tokenHash, DateTimeOffset invitedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        Role = role;
        TokenHash = tokenHash;
        InvitedAt = invitedAt;
        ExpiresAt = invitedAt + Life;
        AcceptedAt = null;
        RevokedAt = null;
    }

    /// <summary>Records that it was taken up.</summary>
    /// <param name="at">When it was taken up, from the injected clock.</param>
    public void Accept(DateTimeOffset at) => AcceptedAt ??= at;

    /// <summary>Withdraws it.</summary>
    /// <param name="at">When it was withdrawn, from the injected clock.</param>
    public void Revoke(DateTimeOffset at) => RevokedAt ??= at;
}
