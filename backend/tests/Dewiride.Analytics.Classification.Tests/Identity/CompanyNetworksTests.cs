using Dewiride.Analytics.Classification.Identity;

namespace Dewiride.Analytics.Classification.Tests.Identity;

/// <summary>
/// The catalogue of networks companies keep for their own computers.
/// </summary>
public sealed class CompanyNetworksTests
{
    [Fact]
    public void Every_Entry_Names_The_Company_Running_It()
    {
        CompanyNetworks.All.Should().OnlyContain(network => !string.IsNullOrWhiteSpace(network.Operator));
    }

    [Fact]
    public void Every_Entry_Carries_A_Routing_Number()
    {
        CompanyNetworks.All.Should().OnlyContain(network => network.AutonomousSystem > 0);
    }

    /// <summary>
    /// One number cannot belong to two companies, and a duplicate would mean whichever was written
    /// second silently never applied.
    /// </summary>
    [Fact]
    public void No_Routing_Number_Appears_Twice()
    {
        CompanyNetworks.All.Select(network => network.AutonomousSystem).Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// Nought is what an address nobody could place reads as. Matching it would call every visit on
    /// an installation behind a proxy a company's machine.
    /// </summary>
    [Fact]
    public void Nothing_Resolved_Matches_Nothing()
    {
        CompanyNetworks.TryFind(0, out _).Should().BeFalse();
    }

    [Fact]
    public void Metas_Own_Network_Is_Named()
    {
        CompanyNetworks.TryFind(32934, out var company).Should().BeTrue();
        company.Should().Be("Meta");
    }

    /// <summary>
    /// A network is either rented out or kept by a company for itself, and a visit over it is weighed
    /// one way or the other, never both.
    /// </summary>
    [Fact]
    public void No_Network_Is_Both_Rented_And_A_Companys_Own()
    {
        CompanyNetworks.All.Where(network => HostingNetworks.TryFind(network.AutonomousSystem, out _))
            .Should().BeEmpty();
    }

    /// <summary>
    /// The company is spelt as the crawler catalogue spells it, which is what lets a visitor naming
    /// one of its own crawlers be told apart from one that does not.
    /// </summary>
    [Fact]
    public void Every_Company_Is_Spelt_As_The_Crawler_Catalogue_Spells_It()
    {
        var crawlerOperators = CrawlerCatalogue.Known.Select(crawler => crawler.Operator).ToHashSet(StringComparer.Ordinal);

        CompanyNetworks.All.Should().OnlyContain(network => crawlerOperators.Contains(network.Operator));
    }
}
