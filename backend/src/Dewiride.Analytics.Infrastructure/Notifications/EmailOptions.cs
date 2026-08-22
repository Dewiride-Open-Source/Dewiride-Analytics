namespace Dewiride.Analytics.Infrastructure.Notifications;

/// <summary>
/// How this installation hands messages to a mail server.
/// </summary>
/// <remarks>
/// Off unless switched on. A self-hosted copy usually has no mail server to point at, and a
/// default that pretended otherwise would fail on the first password reset — at the moment
/// somebody is already locked out.
/// </remarks>
public sealed class EmailOptions
{
    /// <summary>Configuration section these options are bound from.</summary>
    public const string SectionName = "Dewiride:Email";

    /// <summary>Whether messages are handed to a mail server at all.</summary>
    public bool Enabled { get; init; }

    /// <summary>Hostname of the mail server.</summary>
    public string Host { get; init; } = string.Empty;

    /// <summary>Port the mail server accepts submissions on.</summary>
    public int Port { get; init; } = 587;

    /// <summary>How the connection to the mail server is encrypted.</summary>
    public EmailSecurity Security { get; init; } = EmailSecurity.StartTls;

    /// <summary>Account to submit as. Left empty for a relay that takes no credential.</summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>Password for that account.</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>Address messages are sent from.</summary>
    public string FromAddress { get; init; } = string.Empty;

    /// <summary>Name shown beside that address in somebody's inbox.</summary>
    public string FromName { get; init; } = "Dewiride Analytics";

    /// <summary>
    /// How long one submission may take before it is abandoned.
    /// </summary>
    /// <remarks>
    /// Bounded because a mail server that has stopped answering must not hold a request open. The
    /// only caller treats a failure and a success identically, so an abandoned send costs the
    /// message and nothing else.
    /// </remarks>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// How the connection to a mail server is encrypted.
/// </summary>
/// <remarks>
/// Stated rather than guessed from the port. A library left to decide will fall back to sending
/// in the clear when a server does not offer encryption, which is the one outcome nobody would
/// choose on purpose and nobody would notice.
/// </remarks>
public enum EmailSecurity
{
    /// <summary>
    /// Connect, then require the server to raise the connection to TLS. The usual choice, and
    /// what port 587 expects. A server that does not offer it is refused rather than obliged.
    /// </summary>
    StartTls = 1,

    /// <summary>Encrypted from the first byte. What port 465 expects.</summary>
    SslOnConnect = 2,

    /// <summary>
    /// No encryption. Only ever right for a relay on this same machine, where the credential and
    /// the message never leave it.
    /// </summary>
    None = 3,
}
