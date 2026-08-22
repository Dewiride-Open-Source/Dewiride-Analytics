namespace Dewiride.Analytics.Domain.Sites;

/// <summary>
/// What a person may do across everything an organisation owns.
/// </summary>
/// <remarks>
/// Deliberately not the same enum as <see cref="SiteRole"/>. A role on one website and a standing
/// across a whole account answer different questions, and giving them one spelling would make
/// every future divergence — an organisation-level role with no meaning on a single site — a
/// migration rather than an addition.
/// </remarks>
public enum OrganizationRole
{
    /// <summary>Belongs to the organisation and may read what it owns.</summary>
    Member = 1,

    /// <summary>May additionally correct classifications and manage the organisation's sites.</summary>
    Admin = 2,

    /// <summary>May additionally manage people and remove sites.</summary>
    Owner = 3,
}

/// <summary>
/// Grants a person a standing in an organisation.
/// </summary>
/// <remarks>
/// <para>
/// Sites are the unit telemetry is keyed by, but people are added to an account rather than to a
/// website: somebody invited to an organisation expects to see the sites it owns, including the
/// ones added after they joined, without anybody remembering to grant them each one.
/// </para>
/// <para>
/// It lives in the shared schema and both editions write it. A self-hosted install has one
/// organisation and the account created on first run owns it; the hosted service has many. The
/// two editions differ in how a scope is resolved from these grants, never in which grants exist.
/// </para>
/// </remarks>
public sealed class OrganizationMembership
{
    /// <summary>Identity of the membership.</summary>
    public Guid Id { get; private set; }

    /// <summary>Organisation the standing applies to.</summary>
    public Guid OrganizationId { get; private set; }

    /// <summary>Person the standing is granted to.</summary>
    public Guid UserId { get; private set; }

    /// <summary>The granted standing.</summary>
    public OrganizationRole Role { get; private set; }

    /// <summary>When the membership was granted.</summary>
    public DateTimeOffset GrantedAt { get; private set; }

    private OrganizationMembership()
    {
    }

    /// <summary>Grants a standing in an organisation.</summary>
    /// <param name="id">Identity to assign.</param>
    /// <param name="organizationId">Organisation the standing applies to.</param>
    /// <param name="userId">Person the standing is granted to.</param>
    /// <param name="role">The standing to grant.</param>
    /// <param name="grantedAt">Grant time, from the injected clock.</param>
    public OrganizationMembership(
        Guid id,
        Guid organizationId,
        Guid userId,
        OrganizationRole role,
        DateTimeOffset grantedAt)
    {
        Id = id;
        OrganizationId = organizationId;
        UserId = userId;
        Role = role;
        GrantedAt = grantedAt;
    }

    /// <summary>Changes the granted standing.</summary>
    /// <param name="role">The new standing.</param>
    public void ChangeRole(OrganizationRole role) => Role = role;
}

/// <summary>
/// Translates a standing in an organisation into what it permits on one of its sites.
/// </summary>
public static class OrganizationRoles
{
    /// <summary>
    /// What a standing in an organisation permits on any site the organisation owns.
    /// </summary>
    /// <remarks>
    /// Where somebody holds both a standing and a grant on the same site, the wider of the two
    /// applies. An owner of the account who was never named on one of its websites still owns
    /// that website, and a grant made directly on a site is not weakened by joining as a reader.
    /// </remarks>
    /// <param name="role">The standing held in the organisation.</param>
    /// <returns>The equivalent role on a site.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The standing is not one this product defines.</exception>
    public static SiteRole OnItsSites(this OrganizationRole role) => role switch
    {
        OrganizationRole.Member => SiteRole.Viewer,
        OrganizationRole.Admin => SiteRole.Editor,
        OrganizationRole.Owner => SiteRole.Owner,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    /// <summary>
    /// The role somebody holds on a site, taken from whichever of their two claims to it is wider.
    /// </summary>
    /// <remarks>
    /// Two things can give somebody a role on a site: a grant made on that site, and a standing
    /// held in the organisation that owns it. Taking the wider means an owner of the account who
    /// was never named on one of its websites still owns that website, and somebody granted
    /// editing on a single site does not lose it by joining the account as a reader.
    /// </remarks>
    /// <param name="granted">A role granted on the site itself, where there is one.</param>
    /// <param name="standing">A standing held in the owning organisation, where there is one.</param>
    /// <returns>The role, or <see langword="null"/> when neither claim exists.</returns>
    public static SiteRole? Widest(SiteRole? granted, OrganizationRole? standing)
    {
        var implied = standing?.OnItsSites();

        if (granted is null)
        {
            return implied;
        }

        return implied is null || granted.Value >= implied.Value ? granted : implied;
    }
}
