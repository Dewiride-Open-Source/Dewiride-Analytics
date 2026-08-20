using System.Net;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Classification.Identity;

namespace Dewiride.Analytics.Application.Tests.Telemetry;

/// <summary>
/// Covers what a visitor is recognised by, which decides how many visitors a site is reported to
/// have had.
/// </summary>
public sealed class VisitorConnectionTests
{
    /// <summary>A network that rents servers, and one of the pools this was written for.</summary>
    private const uint RentedNetwork = 45102;

    /// <summary>A network that carries households.</summary>
    private const uint HomeNetwork = 55836;

    [Fact]
    public void An_Address_On_An_Ordinary_Network_Is_Used_As_It_Arrived()
    {
        VisitorConnection.Identifying("198.51.100.24", HomeNetwork).Should().Be("198.51.100.24");
    }

    /// <summary>
    /// Nought is what an address nothing resolved reads as. It is not a network this catalogue
    /// holds, and treating it as one would gather everybody whose address could not be placed into
    /// a single impossibly busy visitor.
    /// </summary>
    [Fact]
    public void An_Address_Nothing_Resolved_Is_Used_As_It_Arrived()
    {
        VisitorConnection.Identifying("198.51.100.24", 0).Should().Be("198.51.100.24");
    }

    [Fact]
    public void Nothing_Observed_Reduces_To_Nothing()
    {
        VisitorConnection.Identifying(null, 0).Should().BeNull();
    }

    /// <summary>
    /// The whole point. A pool of rented addresses is sold as a way of not being recognised, and
    /// read literally it turns one program into as many visitors as it held addresses.
    /// </summary>
    [Fact]
    public void Every_Address_On_One_Rented_Network_Is_The_Same_Visitor()
    {
        var pool = new[] { "47.238.1.1", "47.238.9.240", "8.219.64.13", "149.129.200.7" };

        var reduced = pool.Select(address => VisitorConnection.Identifying(address, RentedNetwork));

        reduced.Distinct(StringComparer.Ordinal).Should().ContainSingle();
    }

    [Fact]
    public void Two_Rented_Networks_Are_Two_Visitors()
    {
        var alibaba = VisitorConnection.Identifying("47.238.1.1", RentedNetwork);
        var amazon = VisitorConnection.Identifying("47.238.1.1", 16509);

        alibaba.Should().NotBe(amazon);
    }

    /// <summary>
    /// A network stands in for an address, so the two share one field. A network that could be
    /// spelt the same way as an address would quietly fold whoever arrived from that address into
    /// the network's traffic.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryRentedNetwork))]
    public void A_Network_Can_Never_Be_Spelt_The_Way_An_Address_Is(uint autonomousSystem)
    {
        var reduced = VisitorConnection.Identifying("198.51.100.24", autonomousSystem);

        reduced.Should().NotBeNull();
        IPAddress.TryParse(reduced, out _).Should().BeFalse();
    }

    public static TheoryData<uint> EveryRentedNetwork()
    {
        var networks = new TheoryData<uint>();

        foreach (var network in HostingNetworks.Numbers)
        {
            networks.Add(network);
        }

        return networks;
    }
}
