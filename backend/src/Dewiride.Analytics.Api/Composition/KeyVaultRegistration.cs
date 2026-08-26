using Azure.Identity;
using Dewiride.Analytics.Api.Configuration;

namespace Dewiride.Analytics.Api.Composition;

/// <summary>
/// Puts a vault behind everything else this installation reads its settings from.
/// </summary>
/// <remarks>
/// Added last, and therefore read first: a value the vault holds wins over the same value in the
/// environment. That is what lets a deployment keep its keys somewhere with an audit trail and a
/// door on it, while the machine running the product holds only the one credential that opens it.
/// </remarks>
internal static class KeyVaultRegistration
{
    /// <summary>
    /// Reads the vault named in configuration, if one is named.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// A vault was mentioned and not enough was given to open it. Starting anyway would mean
    /// running with every setting the vault holds silently absent, which on this product is a
    /// service that looks healthy and sends no mail and takes no money.
    /// </exception>
    public static IHostApplicationBuilder AddKeyVault(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var settings = builder.Configuration.GetSection(KeyVaultOptions.SectionName).Get<KeyVaultOptions>()
            ?? new KeyVaultOptions();

        if (!settings.Named)
        {
            return builder;
        }

        if (!settings.AddressUnderstood)
        {
            throw new InvalidOperationException(
                $"{Key(nameof(KeyVaultOptions.Address))} must be the whole address of a vault, "
                + "such as https://example.vault.azure.net/. Every secret this installation has "
                + "travels over that connection, so an unencrypted address is refused as well as "
                + "an incomplete one.");
        }

        if (settings.Missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"{KeyVaultOptions.SectionName} is half filled in. "
                + string.Join(", ", settings.Missing.Select(Key))
                + " must be set as well, because an installation that names a vault it cannot open "
                + "would otherwise start with everything the vault holds absent and no sign of it.");
        }

        builder.Configuration.AddAzureKeyVault(
            settings.LocatedAt!,
            new ClientSecretCredential(settings.TenantId, settings.ClientId, settings.ClientSecret));

        return builder;
    }

    private static string Key(string name) => $"{KeyVaultOptions.SectionName}:{name}";
}
