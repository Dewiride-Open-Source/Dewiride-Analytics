namespace Dewiride.Analytics.Api.Configuration;

/// <summary>
/// A vault to read this installation's settings from, ahead of its own environment.
/// </summary>
/// <remarks>
/// <para>
/// Optional, and absent on most installations. Where it is named, the vault is added as the last
/// place configuration is read from, so a value kept there wins over the same value in the
/// environment. That ordering is the whole point: a key can then be changed where it is stored
/// rather than on the machine the product runs on, and nothing on that machine holds it.
/// </para>
/// <para>
/// A secret's name in a vault may hold letters, digits and dashes and nothing else, so the two
/// dashes in <c>Dewiride--Stripe--SecretKey</c> stand where a colon stands everywhere else a
/// setting is written. The substitution is made as the vault is read.
/// </para>
/// <para>
/// The vault is read once, while the engine is starting, and nothing polls it afterwards. A value
/// changed there reaches a running engine when that engine is next started. That is deliberate
/// rather than a limitation: several of these settings decide whether money is taken from
/// somebody, and a value that changes underneath a request already in flight is worse than one
/// that changes at a moment somebody chose.
/// </para>
/// </remarks>
public sealed class KeyVaultOptions
{
    /// <summary>Configuration section these options are bound from.</summary>
    public const string SectionName = "Dewiride:KeyVault";

    /// <summary>
    /// The vault's own address.
    /// </summary>
    /// <remarks>
    /// Read as text rather than as an address, because most installations have none and every way
    /// of not having one — absent, blank, a variable that expanded to nothing — has to mean the
    /// same thing. What was written becomes an address in <see cref="LocatedAt"/>.
    /// </remarks>
    public string? Address { get; init; }

    /// <summary>The directory the sign-in below belongs to.</summary>
    public string? TenantId { get; init; }

    /// <summary>Which sign-in this engine opens the vault as.</summary>
    public string? ClientId { get; init; }

    /// <summary>
    /// That sign-in's password.
    /// </summary>
    /// <remarks>
    /// The one secret that cannot itself live in the vault, and the reason this arrangement is a
    /// reduction in what a machine holds rather than an elimination of it. It authorises reading
    /// and nothing else: the standing it is given is over this vault's secrets alone.
    /// </remarks>
    public string? ClientSecret { get; init; }

    /// <summary>
    /// The configured address, when what was configured is somewhere a vault could answer.
    /// </summary>
    /// <remarks>
    /// A vault's name on its own is not, and neither is an unencrypted address: this connection
    /// carries every secret the installation has, and the password that opens it travels in the
    /// request. Nothing is guessed on the setting's behalf — anything that is not a whole secure
    /// address is answered here as nothing at all, and is a refusal to start.
    /// </remarks>
    public Uri? LocatedAt =>
        Uri.TryCreate(Address, UriKind.Absolute, out var address)
        && address.Scheme == Uri.UriSchemeHttps
            ? address
            : null;

    /// <summary>Whether a vault was mentioned at all.</summary>
    public bool Named =>
        !string.IsNullOrWhiteSpace(Address)
        || !string.IsNullOrWhiteSpace(TenantId)
        || !string.IsNullOrWhiteSpace(ClientId)
        || !string.IsNullOrWhiteSpace(ClientSecret);

    /// <summary>Whether what was written as the address is one.</summary>
    public bool AddressUnderstood => string.IsNullOrWhiteSpace(Address) || LocatedAt is not null;

    /// <summary>
    /// The settings a vault needs that this installation has not been given.
    /// </summary>
    /// <remarks>
    /// Named rather than counted, so that an installation refused at start-up is told which keys
    /// to fill in. Half of this section is the failure worth catching: an engine that quietly
    /// carried on with the environment alone would run with its mail and payment settings absent
    /// and look perfectly healthy doing it.
    /// </remarks>
    public IReadOnlyList<string> Missing => [.. Absent()];

    /// <summary>Whether enough was given to open the vault.</summary>
    public bool Configured => AddressUnderstood && Missing.Count == 0;

    private IEnumerable<string> Absent()
    {
        if (string.IsNullOrWhiteSpace(Address))
        {
            yield return nameof(Address);
        }

        if (string.IsNullOrWhiteSpace(TenantId))
        {
            yield return nameof(TenantId);
        }

        if (string.IsNullOrWhiteSpace(ClientId))
        {
            yield return nameof(ClientId);
        }

        if (string.IsNullOrWhiteSpace(ClientSecret))
        {
            yield return nameof(ClientSecret);
        }
    }
}
