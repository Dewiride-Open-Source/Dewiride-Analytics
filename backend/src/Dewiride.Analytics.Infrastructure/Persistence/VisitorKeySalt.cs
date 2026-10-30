using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dewiride.Analytics.Infrastructure.Persistence;

/// <summary>
/// The random salt used to derive visitor keys on one calendar day.
/// </summary>
/// <remarks>
/// <para>
/// Salts are generated randomly a day before the day they apply to, kept through the day after it,
/// and then deleted. Made a day early because a day's first reports arrive the instant it begins,
/// and each one needs its day's salt waiting rather than made by whatever runs next. Kept a day
/// after so that a report stamped just before midnight and keyed just after still finds the salt
/// of the day it was stamped on.
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
