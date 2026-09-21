using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dewiride.Analytics.Infrastructure.Persistence;

/// <summary>
/// The record that this installation has been claimed.
/// </summary>
/// <remarks>
/// <para>
/// One row, written the first time an account comes to exist on the installation, and never
/// removed. It exists because accounts can go: a closed organisation is purged after its retention
/// has run, and when it was the last organisation on the installation every account goes with it.
/// Without this row the installation would then look exactly as it did before anybody claimed it,
/// and the welcome screen — the one moment an anonymous caller may create an owner — would open
/// again for whoever found it. An installation that has been emptied is finished, not new; the
/// documented way to start over is to wipe it.
/// </para>
/// <para>
/// The key is fixed at one and the database refuses any other value, so the table can never hold
/// a second row by accident. The instant kept is the earliest account's creation, which is when
/// the claim in fact happened.
/// </para>
/// </remarks>
public sealed class InstallationClaim
{
    /// <summary>The only key a row may have.</summary>
    public const int TheOnlyRow = 1;

    /// <summary>Always <see cref="TheOnlyRow"/>.</summary>
    public int Id { get; private set; }

    /// <summary>When the installation was claimed.</summary>
    public DateTimeOffset ClaimedAt { get; private set; }

    private InstallationClaim()
    {
    }

    /// <summary>Records the claim.</summary>
    /// <param name="claimedAt">When the installation was claimed, from the injected clock or the earliest account.</param>
    public InstallationClaim(DateTimeOffset claimedAt)
    {
        Id = TheOnlyRow;
        ClaimedAt = claimedAt;
    }
}

/// <summary>Maps <see cref="InstallationClaim"/>.</summary>
public sealed class InstallationClaimConfiguration : IEntityTypeConfiguration<InstallationClaim>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<InstallationClaim> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "installation_claims",
            table => table.HasCheckConstraint(
                "ck_installation_claims_one_row",
                $"id = {InstallationClaim.TheOnlyRow}"));

        builder.HasKey(claim => claim.Id);

        // The key is a constant the code supplies, never a number the database hands out; an
        // identity column on a table that may only ever hold row one would be a sequence nobody
        // uses.
        builder.Property(claim => claim.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(claim => claim.ClaimedAt)
            .HasColumnName("claimed_at")
            .IsRequired();
    }
}
