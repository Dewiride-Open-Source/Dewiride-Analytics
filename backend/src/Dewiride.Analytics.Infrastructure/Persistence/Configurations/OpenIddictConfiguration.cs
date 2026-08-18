using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenIddict.EntityFrameworkCore.Models;

namespace Dewiride.Analytics.Infrastructure.Persistence.Configurations;

/// <summary>Names the table holding registered OAuth client applications.</summary>
/// <remarks>
/// OpenIddict's own names run through the snake-case convention as <c>open_iddict_*</c>, which
/// reads as a mistake to anyone opening the database. Only the names change; the columns, keys
/// and relationships stay exactly as OpenIddict defines them, because they are its schema and
/// its migrations will expect them.
/// </remarks>
public sealed class OpenIddictApplicationConfiguration
    : IEntityTypeConfiguration<OpenIddictEntityFrameworkCoreApplication<Guid>>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OpenIddictEntityFrameworkCoreApplication<Guid>> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("openiddict_applications");
    }
}

/// <summary>Names the table holding the consent a user has granted an application.</summary>
public sealed class OpenIddictAuthorizationConfiguration
    : IEntityTypeConfiguration<OpenIddictEntityFrameworkCoreAuthorization<Guid>>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OpenIddictEntityFrameworkCoreAuthorization<Guid>> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("openiddict_authorizations");

        // Left to convention this name is assembled from both renamed table names and runs past
        // PostgreSQL's 63-character limit, where it is silently truncated mid-word.
        builder.HasOne(authorization => authorization.Application)
            .WithMany(application => application.Authorizations)
            .HasConstraintName("fk_openiddict_authorizations_applications_application_id");
    }
}

/// <summary>Names the table holding the scopes an application may request.</summary>
public sealed class OpenIddictScopeConfiguration
    : IEntityTypeConfiguration<OpenIddictEntityFrameworkCoreScope<Guid>>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OpenIddictEntityFrameworkCoreScope<Guid>> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("openiddict_scopes");
    }
}

/// <summary>Names the table holding issued tokens.</summary>
public sealed class OpenIddictTokenConfiguration
    : IEntityTypeConfiguration<OpenIddictEntityFrameworkCoreToken<Guid>>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OpenIddictEntityFrameworkCoreToken<Guid>> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("openiddict_tokens");

        builder.HasOne(token => token.Application)
            .WithMany(application => application.Tokens)
            .HasConstraintName("fk_openiddict_tokens_applications_application_id");

        builder.HasOne(token => token.Authorization)
            .WithMany(authorization => authorization.Tokens)
            .HasConstraintName("fk_openiddict_tokens_authorizations_authorization_id");
    }
}
