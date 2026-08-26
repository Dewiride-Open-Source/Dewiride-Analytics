using Dewiride.Analytics.Api.Composition;
using Dewiride.Analytics.Api.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Dewiride.Analytics.Api.Tests.Configuration;

/// <summary>
/// What the engine does about a vault before it has read a single setting from one.
/// </summary>
/// <remarks>
/// The working case is deliberately absent: opening a vault needs a vault, a directory and a
/// credential, and a test carrying one would be a live secret in a public repository. What is
/// worth pinning here is the other half — that an installation naming no vault is left entirely
/// alone, and that one naming a vault it cannot open says which setting is missing and stops. An
/// engine that started anyway would run with its mail and payment settings absent and report
/// itself healthy while doing it.
/// </remarks>
public sealed class KeyVaultTests
{
    private const string Vault = "https://kv-example.vault.azure.net/";
    private const string Tenant = "00000000-0000-0000-0000-000000000001";
    private const string SignIn = "00000000-0000-0000-0000-000000000002";
    private const string Password = "not-a-real-password";

    /// <summary>
    /// An installation that mentions no vault goes on reading its own environment.
    /// </summary>
    /// <remarks>
    /// The ordinary case, and the one anybody running this themselves is in. Nothing is resolved,
    /// nothing is contacted and no credential library is asked for an opinion.
    /// </remarks>
    [Fact]
    public void An_installation_that_names_no_vault_is_left_alone()
    {
        var builder = Reading();
        var before = builder.Configuration.Sources.Count;

        builder.AddKeyVault();

        builder.Configuration.Sources.Should().HaveCount(before);
    }

    /// <summary>
    /// Every way of not naming a vault means the same thing.
    /// </summary>
    /// <remarks>
    /// A setting that expanded to nothing looks different from an absent one and has to behave
    /// identically, because a deployment that leaves these keys out of its environment file
    /// produces exactly the blank case rather than the absent one.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_setting_is_the_same_as_an_absent_one(string blank)
    {
        var builder = Reading(
            (Key(nameof(KeyVaultOptions.Address)), blank),
            (Key(nameof(KeyVaultOptions.TenantId)), blank),
            (Key(nameof(KeyVaultOptions.ClientId)), blank),
            (Key(nameof(KeyVaultOptions.ClientSecret)), blank));
        var before = builder.Configuration.Sources.Count;

        builder.AddKeyVault();

        builder.Configuration.Sources.Should().HaveCount(before);
    }

    /// <summary>
    /// A vault named with no way to open it stops the engine, naming what is missing.
    /// </summary>
    [Fact]
    public void A_vault_named_with_no_sign_in_stops_the_engine_starting()
    {
        var builder = Reading((Key(nameof(KeyVaultOptions.Address)), Vault));

        var refusal = Assert.Throws<InvalidOperationException>(() => { builder.AddKeyVault(); });

        refusal.Message.Should().Contain(Key(nameof(KeyVaultOptions.TenantId)));
        refusal.Message.Should().Contain(Key(nameof(KeyVaultOptions.ClientId)));
        refusal.Message.Should().Contain(Key(nameof(KeyVaultOptions.ClientSecret)));
    }

    /// <summary>
    /// A sign-in with nothing to open stops the engine too.
    /// </summary>
    /// <remarks>
    /// The same mistake from the other side, and the likelier one: a deployment that carried its
    /// credential over from somewhere and left the address behind.
    /// </remarks>
    [Fact]
    public void A_sign_in_with_no_vault_to_open_stops_the_engine_starting()
    {
        var builder = Reading(
            (Key(nameof(KeyVaultOptions.TenantId)), Tenant),
            (Key(nameof(KeyVaultOptions.ClientId)), SignIn),
            (Key(nameof(KeyVaultOptions.ClientSecret)), Password));

        var refusal = Assert.Throws<InvalidOperationException>(() => { builder.AddKeyVault(); });

        refusal.Message.Should().Contain(Key(nameof(KeyVaultOptions.Address)));
    }

    /// <summary>
    /// Anything that is not a whole, encrypted address is refused.
    /// </summary>
    /// <remarks>
    /// A vault's own name reads correctly in a settings file and then resolves to nothing. An
    /// unencrypted address is worse than wrong: the password that opens the vault travels in the
    /// request, and every secret this installation has travels back.
    /// </remarks>
    [Theory]
    [InlineData("kv-example")]
    [InlineData("kv-example.vault.azure.net")]
    [InlineData("http://kv-example.vault.azure.net/")]
    [InlineData("//kv-example.vault.azure.net/")]
    public void An_address_that_is_not_a_secure_address_stops_the_engine_starting(string written)
    {
        var builder = Reading(
            (Key(nameof(KeyVaultOptions.Address)), written),
            (Key(nameof(KeyVaultOptions.TenantId)), Tenant),
            (Key(nameof(KeyVaultOptions.ClientId)), SignIn),
            (Key(nameof(KeyVaultOptions.ClientSecret)), Password));

        var refusal = Assert.Throws<InvalidOperationException>(() => { builder.AddKeyVault(); });

        refusal.Message.Should().Contain(Key(nameof(KeyVaultOptions.Address)));
    }

    /// <summary>
    /// What is missing is named rather than counted.
    /// </summary>
    /// <remarks>
    /// Somebody reading a start-up failure is looking for the key to fill in. A count of two sends
    /// them back to the documentation to work out which two.
    /// </remarks>
    [Fact]
    public void What_is_missing_is_named()
    {
        var settings = new KeyVaultOptions { Address = Vault, ClientId = SignIn };

        settings.Missing.Should().BeEquivalentTo(
            nameof(KeyVaultOptions.TenantId),
            nameof(KeyVaultOptions.ClientSecret));
        settings.Named.Should().BeTrue();
        settings.Configured.Should().BeFalse();
    }

    /// <summary>
    /// A section filled in describes a vault this installation means to open.
    /// </summary>
    /// <remarks>
    /// The one thing about the working case provable without a vault. What happens on the
    /// connection itself is proven against a real one at the point it is deployed.
    /// </remarks>
    [Fact]
    public void A_section_filled_in_describes_a_vault_to_open()
    {
        var settings = new KeyVaultOptions
        {
            Address = Vault,
            TenantId = Tenant,
            ClientId = SignIn,
            ClientSecret = Password,
        };

        settings.Configured.Should().BeTrue();
        settings.Missing.Should().BeEmpty();
        settings.LocatedAt.Should().Be(new Uri(Vault));
    }

    private static string Key(string name) => $"{KeyVaultOptions.SectionName}:{name}";

    private static HostApplicationBuilder Reading(params (string Key, string? Value)[] settings)
    {
        var builder = Host.CreateEmptyApplicationBuilder(null);

        builder.Configuration.AddInMemoryCollection(
            settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)));

        return builder;
    }
}
