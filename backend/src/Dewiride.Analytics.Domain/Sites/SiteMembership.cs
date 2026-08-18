namespace Dewiride.Analytics.Domain.Sites;

/// <summary>
/// What a person may do with a site.
/// </summary>
public enum SiteRole
{
    /// <summary>May read the dashboard and export data.</summary>
    Viewer = 1,

    /// <summary>May additionally correct classifications and manage site settings.</summary>
    Editor = 2,

    /// <summary>May additionally manage members and delete the site.</summary>
    Owner = 3,
}

/// <summary>
/// Grants a person a role on a site.
/// </summary>
/// <remarks>
/// The Community edition creates exactly one membership, as <see cref="SiteRole.Owner"/>,
/// for the account created on first run. Teams and invitations are a commercial-edition
/// concern, but the grant itself lives in the shared schema so that authorisation has one
/// implementation rather than two.
/// </remarks>
public sealed class SiteMembership
{
    /// <summary>Identity of the membership.</summary>
    public Guid Id { get; private set; }

    /// <summary>Site the role applies to.</summary>
    public Guid SiteId { get; private set; }

    /// <summary>Person the role is granted to.</summary>
    public Guid UserId { get; private set; }

    /// <summary>The granted role.</summary>
    public SiteRole Role { get; private set; }

    /// <summary>When the membership was granted.</summary>
    public DateTimeOffset GrantedAt { get; private set; }

    private SiteMembership()
    {
    }

    /// <summary>Grants a role on a site.</summary>
    /// <param name="id">Identity to assign.</param>
    /// <param name="siteId">Site the role applies to.</param>
    /// <param name="userId">Person the role is granted to.</param>
    /// <param name="role">The role to grant.</param>
    /// <param name="grantedAt">Grant time, from the injected clock.</param>
    public SiteMembership(Guid id, Guid siteId, Guid userId, SiteRole role, DateTimeOffset grantedAt)
    {
        Id = id;
        SiteId = siteId;
        UserId = userId;
        Role = role;
        GrantedAt = grantedAt;
    }

    /// <summary>Changes the granted role.</summary>
    /// <param name="role">The new role.</param>
    public void ChangeRole(SiteRole role) => Role = role;
}
