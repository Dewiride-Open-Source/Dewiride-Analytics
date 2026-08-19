using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dewiride.Analytics.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Organization"/>.</summary>
public sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("organizations");
        builder.HasKey(organization => organization.Id);

        builder.Property(organization => organization.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(organization => organization.CreatedAt).IsRequired();

        builder.HasMany(organization => organization.Sites)
            .WithOne()
            .HasForeignKey(site => site.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(organization => organization.Sites)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary>Maps <see cref="Site"/>.</summary>
public sealed class SiteConfiguration : IEntityTypeConfiguration<Site>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Site> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sites");
        builder.HasKey(site => site.Id);

        builder.Property(site => site.OrganizationId).IsRequired();

        builder.Property(site => site.Domain)
            .HasMaxLength(253)
            .IsRequired();

        builder.Property(site => site.DisplayName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(site => site.TimeZoneId)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(site => site.CreatedAt).IsRequired();
        builder.Property(site => site.RetainQueryStrings).IsRequired();
        builder.Property(site => site.CaptureClicks).IsRequired();

        // Mapped from the backing field, because the public member exposes the list read-only and
        // the aggregate replaces its contents through a method rather than a setter. The
        // read-only view itself is not part of the model.
        builder.Ignore(site => site.AllowedOrigins);

        builder.PrimitiveCollection<List<string>>("_allowedOrigins")
            .HasColumnName("allowed_origins")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired();

        // Domains are not unique across the estate: two organisations may legitimately track the
        // same public site, and in the Community edition a domain may be re-added after deletion.
        // The index exists to make lookup by domain fast, not to enforce a constraint the product
        // does not actually have.
        builder.HasIndex(site => site.Domain).HasDatabaseName("ix_sites_domain");
        builder.HasIndex(site => site.OrganizationId).HasDatabaseName("ix_sites_organization_id");
    }
}

/// <summary>Maps <see cref="SiteIngestKey"/>.</summary>
public sealed class SiteIngestKeyConfiguration : IEntityTypeConfiguration<SiteIngestKey>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SiteIngestKey> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("site_ingest_keys");
        builder.HasKey(key => key.Id);

        builder.Property(key => key.SiteId).IsRequired();

        builder.Property(key => key.Name)
            .HasMaxLength(SiteIngestKey.MaxNameLength)
            .IsRequired();

        // Fixed width: a SHA-256 digest in lower-case hexadecimal is always sixty-four
        // characters, and saying so lets the database refuse anything that is not one.
        builder.Property(key => key.TokenHash)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(key => key.Preview)
            .HasMaxLength(8)
            .IsRequired();

        builder.Property(key => key.CreatedAt).IsRequired();

        builder.Ignore(key => key.IsRevoked);

        // Unique across the estate rather than within a site, because the hash is what a
        // presented secret is looked up by and that lookup names no site. A collision would be
        // a secret that authorises somebody else's traffic.
        builder.HasIndex(key => key.TokenHash)
            .IsUnique()
            .HasDatabaseName("ux_site_ingest_keys_token_hash");

        builder.HasIndex(key => key.SiteId).HasDatabaseName("ix_site_ingest_keys_site_id");

        builder.HasOne<Site>()
            .WithMany()
            .HasForeignKey(key => key.SiteId)
            .HasConstraintName("fk_site_ingest_keys_sites_site_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps <see cref="SiteMembership"/>.</summary>
public sealed class SiteMembershipConfiguration : IEntityTypeConfiguration<SiteMembership>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SiteMembership> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("site_memberships");
        builder.HasKey(membership => membership.Id);

        builder.Property(membership => membership.SiteId).IsRequired();
        builder.Property(membership => membership.UserId).IsRequired();
        builder.Property(membership => membership.GrantedAt).IsRequired();

        builder.Property(membership => membership.Role)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.HasIndex(membership => new { membership.SiteId, membership.UserId })
            .IsUnique()
            .HasDatabaseName("ux_site_memberships_site_user");

        builder.HasIndex(membership => membership.UserId)
            .HasDatabaseName("ix_site_memberships_user_id");

        // The grant carries plain identifiers rather than navigations, because the domain model
        // must not know that accounts are stored by ASP.NET Core Identity. The constraints still
        // belong in the database: a grant that outlives the site or the person it refers to is
        // an authorisation decision nobody can evaluate.
        builder.HasOne<Site>()
            .WithMany()
            .HasForeignKey(membership => membership.SiteId)
            .HasConstraintName("fk_site_memberships_sites_site_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(membership => membership.UserId)
            .HasConstraintName("fk_site_memberships_users_user_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
