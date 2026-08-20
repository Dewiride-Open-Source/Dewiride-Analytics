using Dewiride.Analytics.Classification.Detectors;

namespace Dewiride.Analytics.Classification.Tests.Detectors;

/// <summary>
/// What the engine makes of where a visit came from.
/// </summary>
public sealed class NetworkDetectorTests
{
    private readonly NetworkDetector detector = new();

    [Fact]
    public void A_Visit_From_A_Rented_Server_Is_Reported_As_One()
    {
        var found = detector.Examine(Visits.AReaderFromARentedServer());

        found.Should().ContainSingle()
            .Which.Code.Should().Be(SignalCodes.HostingNetwork);
    }

    [Fact]
    public void It_Names_The_Company_Whose_Datacentre_It_Was()
    {
        var found = detector.Examine(Visits.AReaderFromARentedServer());

        found.Single().Parameters["operator"].Should().Be("Alibaba Cloud");
    }

    /// <summary>
    /// The company is taken from the catalogue rather than echoed from the routing registry, so
    /// the screen shows one spelling of a name whatever the registry happens to hold this month.
    /// </summary>
    [Fact]
    public void It_Does_Not_Echo_What_The_Routing_Registry_Called_The_Network()
    {
        var found = detector.Examine(Visits.AReaderFromARentedServer());

        found.Single().Parameters["operator"].Should().NotContain("ALIBABA-CN-NET");
    }

    [Fact]
    public void It_Points_Toward_Automation_And_Never_Toward_A_Person()
    {
        var found = detector.Examine(Visits.AReaderFromARentedServer());

        found.Single().Direction.Should().Be(SignalDirection.TowardAutomation);
    }

    /// <summary>
    /// Above the heaviest thing a person can be observed doing, because it has to outweigh a real
    /// browser reading a real page — which is exactly what a scraper running one produces.
    /// </summary>
    [Fact]
    public void It_Outweighs_Every_Sign_Of_Somebody_Reading()
    {
        var reading = new EngagementDetector().Examine(Visits.AReader());
        var network = detector.Examine(Visits.AReaderFromARentedServer());

        network.Single().Weight.Should().BeGreaterThan(reading.Max(signal => signal.Weight));
    }

    /// <summary>
    /// There is no complete list of every network that rents servers. A network missing from the
    /// catalogue produces nothing, and never a signal that the visit was therefore a person.
    /// </summary>
    [Fact]
    public void A_Network_The_Catalogue_Does_Not_Hold_Is_Not_A_Finding()
    {
        // Reliance Jio, which carries people rather than servers.
        var found = detector.Examine(Visits.AReaderFromARentedServer(55836));

        found.Should().BeEmpty();
    }

    [Fact]
    public void A_Visit_Nothing_Could_Place_Is_Not_A_Finding()
    {
        var found = detector.Examine(Visits.AReader());

        found.Should().BeEmpty();
    }

    [Fact]
    public void An_Unresolved_Network_Is_Not_A_Finding()
    {
        var found = detector.Examine(Visits.AReaderFromARentedServer(0));

        found.Should().BeEmpty();
    }
}
