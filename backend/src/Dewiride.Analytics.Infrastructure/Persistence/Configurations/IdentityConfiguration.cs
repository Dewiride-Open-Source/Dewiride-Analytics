using Dewiride.Analytics.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dewiride.Analytics.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="ApplicationUser"/>.</summary>
/// <remarks>
/// ASP.NET Core Identity names its tables <c>AspNetUsers</c> and so on, which the snake-case
/// convention turns into <c>asp_net_users</c>. Self-hosters own this database and will open it
/// with a SQL client, so the tables are named for what they hold instead. The framework does not
/// care what they are called.
/// </remarks>
public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("users");

        builder.Property(user => user.DisplayName).HasMaxLength(200);
        builder.Property(user => user.CreatedAt).IsRequired();

        // Identity names these "UserNameIndex" and "EmailIndex", which the snake-case convention
        // leaves alone because they are set explicitly. Renaming keeps every index in this
        // database readable by the same rule; uniqueness and the indexed columns are untouched.
        builder.HasIndex(user => user.NormalizedUserName).HasDatabaseName("ux_users_normalized_user_name");
        builder.HasIndex(user => user.NormalizedEmail).HasDatabaseName("ix_users_normalized_email");
    }
}

/// <summary>Maps <see cref="ApplicationRole"/>.</summary>
public sealed class ApplicationRoleConfiguration : IEntityTypeConfiguration<ApplicationRole>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ApplicationRole> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("roles");
        builder.HasIndex(role => role.NormalizedName).HasDatabaseName("ux_roles_normalized_name");
    }
}

/// <summary>Maps the claims held directly by a user.</summary>
public sealed class UserClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<Guid>>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("user_claims");
    }
}

/// <summary>Maps the roles a user holds.</summary>
public sealed class UserRoleConfiguration : IEntityTypeConfiguration<IdentityUserRole<Guid>>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<IdentityUserRole<Guid>> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("user_roles");
    }
}

/// <summary>Maps external sign-in providers linked to a user.</summary>
public sealed class UserLoginConfiguration : IEntityTypeConfiguration<IdentityUserLogin<Guid>>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("user_logins");
    }
}

/// <summary>Maps the claims attached to a role.</summary>
public sealed class RoleClaimConfiguration : IEntityTypeConfiguration<IdentityRoleClaim<Guid>>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<IdentityRoleClaim<Guid>> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("role_claims");
    }
}

/// <summary>Maps the tokens Identity issues for password reset and two-step verification.</summary>
public sealed class UserTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("user_tokens");
    }
}
