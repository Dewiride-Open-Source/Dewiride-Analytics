using Dewiride.Analytics.Api.Analytics;
using Dewiride.Analytics.Application.Posts;

namespace Dewiride.Analytics.Api.Tests.Contracts;

/// <summary>
/// Covers what the closed sets a reading of a sitemap can end in are called on the wire.
/// </summary>
/// <remarks>
/// Each word is the key of a sentence the dashboard writes, so these are a published contract rather
/// than a detail of the engine. A member with no word fails the request that reports it, and a
/// member renamed in C# must not be able to change what the dashboard is told.
/// </remarks>
public sealed class ReportedNamesTests
{
    /// <summary>
    /// A lookup that misses throws, and it is on the path that answers a status request, so a state
    /// added without a word would turn the settings panel's status line into a failed request.
    /// </summary>
    [Fact]
    public void Every_Sitemap_State_And_Problem_Has_A_Wire_Name()
    {
        ReportedNames.SitemapStates.Keys.Should().BeEquivalentTo(Enum.GetValues<SitemapState>());
        ReportedNames.SitemapProblems.Keys.Should().BeEquivalentTo(Enum.GetValues<SitemapProblem>());
    }

    [Theory]
    [InlineData(SitemapState.None, "none")]
    [InlineData(SitemapState.Off, "off")]
    [InlineData(SitemapState.Waiting, "waiting")]
    [InlineData(SitemapState.Read, "read")]
    [InlineData(SitemapState.PartlyRead, "partly-read")]
    [InlineData(SitemapState.Failed, "failed")]
    public void A_Sitemap_State_Goes_By_The_Word_The_Dashboard_Reads(SitemapState state, string word)
    {
        ReportedNames.SitemapStates[state].Should().Be(word);
    }

    [Theory]
    [InlineData(SitemapProblem.Unreachable, "unreachable")]
    [InlineData(SitemapProblem.NotFound, "not-found")]
    [InlineData(SitemapProblem.TurnedAway, "turned-away")]
    [InlineData(SitemapProblem.ServerError, "server-error")]
    [InlineData(SitemapProblem.Insecure, "insecure")]
    [InlineData(SitemapProblem.NotASitemap, "not-a-sitemap")]
    [InlineData(SitemapProblem.TooLarge, "too-large")]
    [InlineData(SitemapProblem.LeftTheWebsite, "left-the-website")]
    [InlineData(SitemapProblem.TooManyRedirects, "too-many-redirects")]
    [InlineData(SitemapProblem.PrivateAddress, "private-address")]
    [InlineData(SitemapProblem.TooSlow, "too-slow")]
    [InlineData(SitemapProblem.TooMany, "too-many")]
    [InlineData(SitemapProblem.AddressesWithQueries, "addresses-with-queries")]
    [InlineData(SitemapProblem.RobotsDisallowed, "robots-disallowed")]
    [InlineData(SitemapProblem.RobotsUnreadable, "robots-unreadable")]
    public void A_Sitemap_Problem_Goes_By_The_Word_The_Dashboard_Reads(SitemapProblem problem, string word)
    {
        ReportedNames.SitemapProblems[problem].Should().Be(word);
    }
}
