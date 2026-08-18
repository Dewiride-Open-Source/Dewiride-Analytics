namespace Dewiride.Analytics.Domain.Sites;

/// <summary>
/// The account that owns sites and to which people belong.
/// </summary>
/// <remarks>
/// The Community edition always has exactly one organisation, created on first run. It
/// exists there rather than being conditionally compiled away so that both editions share
/// one schema: an open-core product whose self-hosted and hosted schemas diverge cannot
/// keep them in step for long, and the divergence is what makes upgrades and migrations
/// between editions impossible later.
/// </remarks>
public sealed class Organization
{
    /// <summary>Identity of the organisation.</summary>
    public Guid Id { get; private set; }

    /// <summary>Human-readable name, shown in the account area.</summary>
    public string Name { get; private set; }

    /// <summary>When the organisation was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    private readonly List<Site> _sites = [];

    /// <summary>Sites owned by this organisation.</summary>
    public IReadOnlyCollection<Site> Sites => _sites.AsReadOnly();

    private Organization()
    {
        Name = string.Empty;
    }

    /// <summary>Creates an organisation.</summary>
    /// <param name="id">Identity to assign.</param>
    /// <param name="name">Human-readable name.</param>
    /// <param name="createdAt">Creation time, from the injected clock.</param>
    /// <exception cref="ArgumentException">The name is empty or whitespace.</exception>
    public Organization(Guid id, string name, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        Name = name.Trim();
        CreatedAt = createdAt;
    }

    /// <summary>Renames the organisation.</summary>
    /// <param name="name">The new name.</param>
    /// <exception cref="ArgumentException">The name is empty or whitespace.</exception>
    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }
}
