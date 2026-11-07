using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Posts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dewiride.Analytics.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="SitemapRead"/>.</summary>
public sealed class SitemapReadConfiguration : IEntityTypeConfiguration<SitemapRead>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SitemapRead> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sitemap_reads");
        builder.HasKey(read => read.SiteId);

        builder.Property(read => read.Address)
            .HasMaxLength(Site.MaxSitemapAddressLength)
            .IsRequired();

        builder.Property(read => read.Host)
            .HasMaxLength(Site.MaxDomainLength)
            .IsRequired();

        builder.Property(read => read.NextPassAt).IsRequired();
        builder.Property(read => read.NextCycleAt).IsRequired();
        builder.Property(read => read.UpdatedAt).IsRequired();

        builder.Property(read => read.State)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(read => read.Problem)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(read => read.RootKind)
            .HasConversion<string>()
            .HasMaxLength(8);

        builder.Property(read => read.RootEtag).HasMaxLength(SitemapRead.MaxEtagLength);

        builder.Property(read => read.RootProblem)
            .HasConversion<string>()
            .HasMaxLength(32);

        // Asked on every poll for the rows whose time has come, across every website on the
        // installation.
        builder.HasIndex(read => read.NextPassAt).HasDatabaseName("ix_sitemap_reads_next_pass_at");

        // A claim asks whether anybody holds an unexpired claim on the same host, and the limit on
        // how often a website is asked for anything rests on that answer.
        builder.HasIndex(read => new { read.Host, read.ClaimedUntil })
            .HasDatabaseName("ix_sitemap_reads_host_claimed_until");

        builder.HasOne<Site>()
            .WithOne()
            .HasForeignKey<SitemapRead>(read => read.SiteId)
            .HasConstraintName("fk_sitemap_reads_sites_site_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps <see cref="SitemapPart"/>.</summary>
public sealed class SitemapPartConfiguration : IEntityTypeConfiguration<SitemapPart>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SitemapPart> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sitemap_parts");
        builder.HasKey(part => part.Id);

        // The reader makes the identifier, so a part can be named before it has been stored.
        builder.Property(part => part.Id).ValueGeneratedNever();

        builder.Property(part => part.Address)
            .HasMaxLength(Site.MaxSitemapAddressLength)
            .IsRequired();

        builder.Property(part => part.IsPosts).IsRequired();
        builder.Property(part => part.Etag).HasMaxLength(SitemapRead.MaxEtagLength);

        builder.Property(part => part.Outcome)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(part => part.Entries)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(part => part.OffSiteEntries)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(part => part.QueryEntries)
            .HasDefaultValue(0)
            .IsRequired();

        builder.HasIndex(part => new { part.SiteId, part.Address })
            .IsUnique()
            .HasDatabaseName("ux_sitemap_parts_site_address");

        builder.HasOne<Site>()
            .WithMany()
            .HasForeignKey(part => part.SiteId)
            .HasConstraintName("fk_sitemap_parts_sites_site_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps <see cref="SitePost"/>.</summary>
public sealed class SitePostConfiguration : IEntityTypeConfiguration<SitePost>
{
    /// <summary>
    /// What makes a listed page a post.
    /// </summary>
    /// <remarks>
    /// A page that says it is something other than an article is not one, and neither is a page that
    /// has gone. Otherwise it is a post where the part listing it is the posts part, or where the page
    /// says it is an article. Written so that a page that has said nothing yet comes out as false: a
    /// plain comparison with a missing value is neither true nor false, and the column is required.
    /// </remarks>
    private const string IsPostSql =
        $"page_type IS DISTINCT FROM '{nameof(PostPageType.Other)}' "
        + $"AND page_outcome IS DISTINCT FROM '{nameof(PostPageOutcome.Gone)}' "
        + $"AND (listed_in_posts_part OR page_type IS NOT DISTINCT FROM '{nameof(PostPageType.Article)}')";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SitePost> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("site_posts");
        builder.HasKey(post => new { post.SiteId, post.Path });

        builder.Property(post => post.Path)
            .HasMaxLength(Site.MaxSitemapAddressLength)
            .IsRequired();

        builder.Property(post => post.Address)
            .HasMaxLength(Site.MaxSitemapAddressLength)
            .IsRequired();

        builder.Property(post => post.LandedPath).HasMaxLength(Site.MaxSitemapAddressLength);

        builder.Property(post => post.ListedInPostsPart).IsRequired();
        builder.Property(post => post.FirstListedAt).IsRequired();

        builder.Property(post => post.Title).HasMaxLength(SitePost.MaxTitleLength);

        builder.Property(post => post.TitleSource)
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(post => post.PublishedSource)
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(post => post.PageType)
            .HasConversion<string>()
            .HasMaxLength(8);

        builder.Property(post => post.IsPost)
            .HasComputedColumnSql(IsPostSql, stored: true)
            .IsRequired();

        builder.Property(post => post.PageAttempts)
            .HasDefaultValue((short)0)
            .IsRequired();

        builder.Property(post => post.PageOutcome)
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(post => post.PageEtag).HasMaxLength(SitemapRead.MaxEtagLength);

        // The pages of one website still to be read, in the order they fall due. Most posts have
        // been read and are never due again, so the index holds only the ones that matter.
        builder.HasIndex(post => new { post.SiteId, post.PageDueAt })
            .HasFilter("page_due_at IS NOT NULL AND unlisted_at IS NULL")
            .HasDatabaseName("ix_site_posts_page_due");

        // Deleting a part clears the reference on every post it listed, and PostgreSQL does not
        // index a foreign key on its own.
        builder.HasIndex(post => post.PartId).HasDatabaseName("ix_site_posts_part_id");

        builder.HasOne<Site>()
            .WithMany()
            .HasForeignKey(post => post.SiteId)
            .HasConstraintName("fk_site_posts_sites_site_id")
            .OnDelete(DeleteBehavior.Cascade);

        // A post outlives the part it was listed in. It is keyed by its path, so the next complete
        // reading finds it listed again or finds it gone.
        builder.HasOne<SitemapPart>()
            .WithMany()
            .HasForeignKey(post => post.PartId)
            .HasConstraintName("fk_site_posts_sitemap_parts_part_id")
            .OnDelete(DeleteBehavior.SetNull);
    }
}
