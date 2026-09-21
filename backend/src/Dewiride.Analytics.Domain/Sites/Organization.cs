namespace Dewiride.Analytics.Domain.Sites;

/// <summary>
/// The account that owns sites and to which people belong.
/// </summary>
/// <remarks>
/// <para>
/// The Community edition always has exactly one organisation, created on first run. It
/// exists there rather than being conditionally compiled away so that both editions share
/// one schema: an open-core product whose self-hosted and hosted schemas diverge cannot
/// keep them in step for long, and the divergence is what makes upgrades and migrations
/// between editions impossible later.
/// </para>
/// <para>
/// An organisation can be closed. A closed organisation is kept whole — its sites, its people,
/// its telemetry — for <see cref="ClosureRetention"/>, so that bringing it back is one field
/// cleared rather than anything rebuilt; nothing about it is deleted before that time has run.
/// While it is closed nothing is measured for it and nobody in it can open its screens.
/// </para>
/// </remarks>
public sealed class Organization
{
    /// <summary>
    /// How long a closed organisation is kept before everything about it is deleted.
    /// </summary>
    /// <remarks>
    /// The figure is spelled out to people in two places that must change with it: the sentence
    /// on the dashboard's card that closes an account, held to this by <c>ClosureCopyTests</c>,
    /// and the hosted service's public terms and privacy policy, held to it by the commercial
    /// edition's <c>PublishedPricesTests</c>. Changing it here without changing them would make a
    /// promise the product does not keep.
    /// </remarks>
    public static readonly TimeSpan ClosureRetention = TimeSpan.FromDays(30);

    /// <summary>
    /// How long before deletion the owners are reminded that it is coming.
    /// </summary>
    public static readonly TimeSpan DeletionReminderLead = TimeSpan.FromDays(7);

    /// <summary>Identity of the organisation.</summary>
    public Guid Id { get; private set; }

    /// <summary>Human-readable name, shown in the account area.</summary>
    public string Name { get; private set; }

    /// <summary>When the organisation was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When it was closed, or <see langword="null"/> while it is open.</summary>
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>Who closed it, or <see langword="null"/> while it is open.</summary>
    public Guid? ClosedByUserId { get; private set; }

    /// <summary>
    /// When the owners were reminded that deletion is coming, or <see langword="null"/> while
    /// they have not been.
    /// </summary>
    public DateTimeOffset? DeletionReminderSentAt { get; private set; }

    /// <summary>Whether it is closed.</summary>
    public bool IsClosed => ClosedAt is not null;

    /// <summary>
    /// When everything about it is deleted, or <see langword="null"/> while it is open.
    /// </summary>
    public DateTimeOffset? DeletionDue => ClosedAt + ClosureRetention;

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

    /// <summary>
    /// Closes the organisation.
    /// </summary>
    /// <remarks>
    /// A second press, or a retried request, changes nothing: the deletion date and the message
    /// that announces the closure both hang off the instant recorded here, and moving it would
    /// move them. A new closure starts with no reminder sent, whatever an earlier one left behind.
    /// </remarks>
    /// <param name="byUserId">Who is closing it.</param>
    /// <param name="at">When, from the injected clock.</param>
    public void Close(Guid byUserId, DateTimeOffset at)
    {
        if (ClosedAt is not null)
        {
            return;
        }

        ClosedAt = at;
        ClosedByUserId = byUserId;
        DeletionReminderSentAt = null;
    }

    /// <summary>
    /// Brings a closed organisation back, exactly as it was.
    /// </summary>
    public void Reopen()
    {
        ClosedAt = null;
        ClosedByUserId = null;
        DeletionReminderSentAt = null;
    }
}
