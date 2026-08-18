namespace Dewiride.Analytics.Api.Contracts;

/// <summary>
/// What is asked for when a key is created.
/// </summary>
/// <param name="Name">
/// What to call it. Required, because a list of keys nobody can tell apart is a list nobody dares
/// remove anything from.
/// </param>
public sealed record CreateServerKeyRequest(string? Name);

/// <summary>
/// One key, described without its secret.
/// </summary>
/// <param name="Id">Identifier of the key, used to withdraw it.</param>
/// <param name="Name">What it was called when it was created.</param>
/// <param name="Preview">Last few characters of the secret, enough to recognise a stored copy.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="LastUsedAt">
/// When something last reported with it, or <see langword="null"/> if nothing ever has. Accurate
/// to about a minute.
/// </param>
public sealed record ServerKeySummary(
    Guid Id,
    string Name,
    string Preview,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt);

/// <summary>
/// A key at the one moment its secret exists outside the caller's own storage.
/// </summary>
/// <param name="Key">The key as it will appear from now on.</param>
/// <param name="Secret">
/// The secret in full. Nothing keeps a copy: only a hash of it is stored, so this response is the
/// only opportunity to record it.
/// </param>
public sealed record IssuedServerKey(ServerKeySummary Key, string Secret);
