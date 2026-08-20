using Dewiride.Analytics.Classification.Identity;

namespace Dewiride.Analytics.Classification.Tests.Identity;

/// <summary>
/// The catalogue of networks whose business is renting computers.
/// </summary>
public sealed class HostingNetworksTests
{
    [Fact]
    public void Every_Entry_Names_The_Company_Running_It()
    {
        HostingNetworks.All.Should().OnlyContain(network => !string.IsNullOrWhiteSpace(network.Operator));
    }

    [Fact]
    public void Every_Entry_Carries_A_Routing_Number()
    {
        HostingNetworks.All.Should().OnlyContain(network => network.AutonomousSystem > 0);
    }

    /// <summary>
    /// One number cannot belong to two companies, and a duplicate would mean whichever was written
    /// second silently never applied.
    /// </summary>
    [Fact]
    public void No_Routing_Number_Appears_Twice()
    {
        HostingNetworks.All.Select(network => network.AutonomousSystem)
            .Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// Nought is what an address nobody could place reads as. Matching it would call every visit on
    /// an installation behind a proxy a rented server.
    /// </summary>
    [Fact]
    public void Nothing_Resolved_Matches_Nothing()
    {
        HostingNetworks.TryFind(0, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(45102u, "Alibaba Cloud")]
    [InlineData(16509u, "Amazon Web Services")]
    [InlineData(24940u, "Hetzner")]
    [InlineData(14061u, "DigitalOcean")]
    [InlineData(396982u, "Google Cloud")]
    public void A_Catalogued_Network_Is_Named(uint autonomousSystem, string expected)
    {
        HostingNetworks.TryFind(autonomousSystem, out var operatorName).Should().BeTrue();
        operatorName.Should().Be(expected);
    }

    /// <summary>
    /// Kept out on purpose, and the test says so rather than the absence being an accident nobody
    /// notices. Real people browse from all three, and a catalogue holding them would call a whole
    /// customer's audience automation on the day they put a delivery network in front of their site.
    /// </summary>
    /// <param name="autonomousSystem">A network that carries people rather than servers.</param>
    [Theory]
    [InlineData(13335u)] // Cloudflare, which a site's own readers arrive through.
    [InlineData(20940u)] // Akamai, the same.
    [InlineData(53813u)] // Zscaler, behind which everybody is an employee at a desk.
    [InlineData(16247u)] // M247, which mostly carries consumer privacy services.
    [InlineData(55836u)] // Reliance Jio, a household network.
    public void Networks_That_Carry_People_Are_Deliberately_Absent(uint autonomousSystem)
    {
        HostingNetworks.TryFind(autonomousSystem, out _).Should().BeFalse();
    }
}
