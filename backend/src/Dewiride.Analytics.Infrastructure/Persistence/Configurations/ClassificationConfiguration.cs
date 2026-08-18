using Dewiride.Analytics.Domain.Classification;
using Dewiride.Analytics.Domain.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dewiride.Analytics.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="ClassificationProgress"/>.</summary>
public sealed class ClassificationProgressConfiguration : IEntityTypeConfiguration<ClassificationProgress>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ClassificationProgress> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("classification_progress");

        // Keyed by the ruleset as well as the site, because improving the rules starts a second
        // bookmark rather than resetting the first. Both sets of verdicts are kept, so both need
        // somewhere to record how far they got.
        builder.HasKey(progress => new
        {
            progress.SiteId,
            progress.RulesetMajor,
            progress.RulesetMinor,
        });

        builder.Property(progress => progress.ClassifiedThrough).IsRequired();
        builder.Property(progress => progress.UpdatedAt).IsRequired();

        builder.HasOne<Site>()
            .WithMany()
            .HasForeignKey(progress => progress.SiteId)
            .HasConstraintName("fk_classification_progress_sites_site_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
