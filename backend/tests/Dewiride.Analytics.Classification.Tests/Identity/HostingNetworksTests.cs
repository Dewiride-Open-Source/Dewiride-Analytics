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
    [InlineData(55960u, "Sinnet")]
    [InlineData(203020u, "HostRoyale")]
    [InlineData(137718u, "Volcano Engine")]
    [InlineData(134756u, "China Telecom")]
    [InlineData(134543u, "China Unicom")]
    [InlineData(202053u, "UpCloud")]
    [InlineData(25697u, "UpCloud")]
    [InlineData(46475u, "Limestone Networks")]
    public void A_Catalogued_Network_Is_Named(uint autonomousSystem, string expected)
    {
        HostingNetworks.TryFind(autonomousSystem, out var operatorName).Should().BeTrue();
        operatorName.Should().Be(expected);
    }

    /// <summary>
    /// Kept out on purpose, and the test says so rather than the absence being an accident nobody
    /// notices. Real people browse from all of them, and a catalogue holding them would call a whole
    /// customer's audience automation on the day they put a delivery network in front of their site,
    /// or a whole country's readers automation because its carrier also rents racks.
    /// </summary>
    /// <param name="autonomousSystem">A network that carries people rather than servers.</param>
    [Theory]
    [InlineData(13335u)] // Cloudflare, which a site's own readers arrive through.
    [InlineData(20940u)] // Akamai, the same.
    [InlineData(60068u)] // CDN77, the same, which carries consumer privacy services' exits besides.
    [InlineData(53813u)] // Zscaler, behind which everybody is an employee at a desk.
    [InlineData(16247u)] // M247-UK by its registry handle, now Convergence Group's, a carrier for people.
    [InlineData(9009u)] // M247 Europe, whose exits mostly carry consumer privacy services.
    [InlineData(212238u)] // Datacamp's server network, the same.
    [InlineData(55836u)] // Reliance Jio, a household network.
    [InlineData(4134u)] // ChinaNet, the backbone most of China browses from; its data centres are numbered apart.
    [InlineData(4837u)] // China169, China Unicom's, the same.
    [InlineData(9808u)] // China Mobile, the same.
    public void Networks_That_Carry_People_Are_Deliberately_Absent(uint autonomousSystem)
    {
        HostingNetworks.TryFind(autonomousSystem, out _).Should().BeFalse();
    }

    /// <summary>
    /// A network a company keeps for its own computers is not a rented one: nobody rents a desktop
    /// there, so a visit over it is never decided by what it did the way a rented network's is.
    /// </summary>
    [Fact]
    public void A_Network_A_Company_Keeps_For_Its_Own_Computers_Is_Not_A_Rented_One()
    {
        HostingNetworks.TryFind(32934u, out _).Should().BeFalse();
    }
}
