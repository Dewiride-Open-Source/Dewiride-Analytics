using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Dewiride.Analytics.Infrastructure.Persistence;

/// <summary>
/// The control plane: organisations, sites, memberships, accounts and authorisation state.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately does not hold telemetry. Events, sessions, classifications and rollups live in
/// ClickHouse and are reached through the <c>AnalyticsQuery</c> vocabulary; this context is for
/// the comparatively small, highly mutable, transactional data that a relational database and an
/// object-relational mapper are actually good at.
/// </para>
/// <para>
/// Both editions share this schema. The Community edition simply has one organisation row. An
/// open-core product whose self-hosted and hosted schemas drift apart cannot offer an upgrade
/// path between them, and the drift is invisible until the day it matters.
/// </para>
/// </remarks>
/// <param name="options">Context options.</param>
public sealed class ControlPlaneDbContext(DbContextOptions<ControlPlaneDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options), IDataProtectionKeyContext
{
    /// <summary>Organisations.</summary>
    public DbSet<Organization> Organizations => Set<Organization>();

    /// <summary>Sites whose traffic is observed.</summary>
    public DbSet<Site> Sites => Set<Site>();

    /// <summary>Grants of a role on a site.</summary>
    public DbSet<SiteMembership> SiteMemberships => Set<SiteMembership>();

    /// <summary>
    /// Keys the framework uses to sign and encrypt sign-in cookies.
    /// </summary>
    /// <remarks>
    /// Kept in the database rather than on disk. A container filesystem does not survive a
    /// restart, so keys held there would sign every signed-in person out on each deployment, and
    /// a second instance of the API would reject cookies the first one issued.
    /// </remarks>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.OnModelCreating(builder);

        // Applied here rather than through the options builder so that the order is explicit:
        // OpenIddict contributes its entities first and the configurations applied below can
        // then rename their tables. Through the options builder the customizer runs after
        // OnModelCreating and would silently undo those names.
        builder.UseOpenIddict<Guid>();

        builder.ApplyConfigurationsFromAssembly(typeof(ControlPlaneDbContext).Assembly);
    }
}
