using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dewiride.Analytics.Infrastructure.Persistence;

/// <summary>
/// The random salt used to derive visitor keys on one calendar day.
/// </summary>
/// <remarks>
/// <para>
/// Salts are generated randomly, kept for two days, and then deleted. Two days rather than one
/// because a visit that begins before midnight and continues after it must not fragment into two
/// visitors, so the previous day's salt has to remain available for a short overlap.
/// </para>
/// <para>
/// Deletion is the mechanism, not a tidiness measure. Once a salt is gone, the keys derived from
/// it cannot be recomputed by anyone — including the operator, including someone who later
/// obtains the database and every server secret. That is what makes the claim "this cannot follow
/// a visitor between days" a property of the system rather than a promise about our intentions.
/// A salt derived from a long-lived server secret would look identical in the data and would not
/// have this property.
/// </para>
/// </remarks>
public sealed class VisitorKeySalt
{
    /// <summary>The UTC day this salt applies to.</summary>
    public DateOnly Day { get; private set; }

    /// <summary>32 random bytes from a cryptographic generator.</summary>
    public byte[] Salt { get; private set; }

    private VisitorKeySalt()
    {
        Salt = [];
    }

    /// <summary>Creates a salt for a day.</summary>
    /// <param name="day">The UTC day.</param>
    /// <param name="salt">The random bytes.</param>
    public VisitorKeySalt(DateOnly day, byte[] salt)
    {
        ArgumentNullException.ThrowIfNull(salt);

        Day = day;
        Salt = salt;
    }
}

/// <summary>Maps <see cref="VisitorKeySalt"/>.</summary>
public sealed class VisitorKeySaltConfiguration : IEntityTypeConfiguration<VisitorKeySalt>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<VisitorKeySalt> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("visitor_key_salts");
        builder.HasKey(salt => salt.Day);

        builder.Property(salt => salt.Day)
            .HasColumnName("day")
            .IsRequired();

        builder.Property(salt => salt.Salt)
            .HasColumnName("salt")
            .HasMaxLength(32)
            .IsRequired();
    }
}
