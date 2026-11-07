using Dewiride.Analytics.Application.Posts;
using Dewiride.Analytics.Infrastructure.Posts;

namespace Dewiride.Analytics.Integration.Tests.Posts;

/// <summary>
/// Covers what a reading of a sitemap comes to once there is nothing left to ask for, and which words
/// the ways of failing go by.
/// </summary>
public sealed class SitemapVerdictTests
{
    private static readonly PartStanding Read = new(SitemapPartOutcome.Read, 3, 0, 0);
    private static readonly PartStanding Unchanged = new(SitemapPartOutcome.Unchanged, 3, 0, 0);
    private static readonly PartStanding Failed = new(SitemapPartOutcome.ServerError, 0, 0, 0);

    [Fact]
    public void Every_Part_Read_Is_A_Complete_Reading_With_Nothing_To_Report()
    {
        SitemapVerdict.Of([Read, Unchanged], null)
            .Should().Be(new SitemapVerdict(SitemapState.Read, null, Complete: true));
    }

    /// <summary>Nothing was listed, and all of nothing has been read.</summary>
    [Fact]
    public void A_Sitemap_With_No_Parts_Has_Been_Read_In_Full()
    {
        SitemapVerdict.Of([], null).Should().Be(new SitemapVerdict(SitemapState.Read, null, Complete: true));
    }

    [Fact]
    public void A_Part_That_Failed_Makes_The_Reading_Partial_And_Unable_To_Unlist()
    {
        SitemapVerdict.Of([Read, Failed], null)
            .Should().Be(new SitemapVerdict(SitemapState.PartlyRead, SitemapProblem.ServerError, Complete: false));
    }

    [Fact]
    public void Nothing_Getting_Through_Is_A_Failure()
    {
        SitemapVerdict.Of([Failed, Failed], null)
            .Should().Be(new SitemapVerdict(SitemapState.Failed, SitemapProblem.ServerError, Complete: false));
    }

    [Fact]
    public void The_First_Thing_That_Went_Wrong_Is_The_One_Reported()
    {
        PartStanding[] parts =
        [
            new(SitemapPartOutcome.NotFound, 0, 0, 0),
            new(SitemapPartOutcome.ServerError, 0, 0, 0),
        ];

        SitemapVerdict.Of(parts, SitemapProblem.LeftTheWebsite).Problem.Should().Be(SitemapProblem.NotFound);
    }

    /// <summary>All of what it could hold was read, so it did get through, and yet what it left out is unknown.</summary>
    [Fact]
    public void A_Part_Cut_Short_Is_Partial_And_Unable_To_Unlist_But_Did_Get_Through()
    {
        SitemapVerdict.Of([new PartStanding(SitemapPartOutcome.TooMany, 5, 0, 0)], null)
            .Should().Be(new SitemapVerdict(SitemapState.PartlyRead, SitemapProblem.TooMany, Complete: false));
    }

    /// <summary>The parts left out might have listed anything, so nothing can be said to have gone.</summary>
    [Fact]
    public void A_List_Of_Parts_Cut_Short_Cannot_Unlist()
    {
        SitemapVerdict.Of([Read], SitemapProblem.TooMany)
            .Should().Be(new SitemapVerdict(SitemapState.PartlyRead, SitemapProblem.TooMany, Complete: false));
    }

    /// <summary>What it lists on the website has all been read, and one stray entry must not stop it saying a post has gone.</summary>
    [Fact]
    public void Entries_Off_The_Website_Are_Reported_And_Do_Not_Stop_It_Being_Complete()
    {
        SitemapVerdict.Of([new PartStanding(SitemapPartOutcome.Read, 3, 1, 0)], null)
            .Should().Be(new SitemapVerdict(SitemapState.PartlyRead, SitemapProblem.LeftTheWebsite, Complete: true));
    }

    /// <summary>A sitemap with nothing on the website has said nothing about it, which is not saying its posts have gone.</summary>
    [Fact]
    public void Nothing_But_Entries_Off_The_Website_Is_A_Failure_And_Cannot_Unlist()
    {
        SitemapVerdict.Of([new PartStanding(SitemapPartOutcome.Read, 0, 3, 0)], null)
            .Should().Be(new SitemapVerdict(SitemapState.Failed, SitemapProblem.LeftTheWebsite, Complete: false));
    }

    [Fact]
    public void Parts_Named_Off_The_Website_Are_Reported_And_Do_Not_Stop_It_Being_Complete()
    {
        SitemapVerdict.Of([Read], SitemapProblem.LeftTheWebsite)
            .Should().Be(new SitemapVerdict(SitemapState.PartlyRead, SitemapProblem.LeftTheWebsite, Complete: true));
    }

    [Fact]
    public void Addresses_With_Queries_Are_Only_A_Problem_When_Nothing_Else_Was_Found()
    {
        SitemapVerdict.Of([new PartStanding(SitemapPartOutcome.Read, 0, 0, 2)], null).Problem
            .Should().Be(SitemapProblem.AddressesWithQueries);

        SitemapVerdict.Of([new PartStanding(SitemapPartOutcome.Read, 1, 0, 2)], null).Problem
            .Should().BeNull();
    }

    /// <summary>Goes through the way a part stands, however it is written.</summary>
    /// <param name="outcome">What came of asking for it.</param>
    /// <param name="wasRead">Whether everything it lists is known.</param>
    [Theory]
    [InlineData(SitemapPartOutcome.Read, true)]
    [InlineData(SitemapPartOutcome.Unchanged, true)]
    [InlineData(SitemapPartOutcome.TooSlow, false)]
    [InlineData(SitemapPartOutcome.TooMany, false)]
    [InlineData(SitemapPartOutcome.RobotsUnreadable, false)]
    public void A_Part_Was_Read_Only_If_All_Of_It_Is_Known(SitemapPartOutcome outcome, bool wasRead)
    {
        new PartStanding(outcome, 0, 0, 0).WasRead.Should().Be(wasRead);
    }

    /// <summary>
    /// What stops a part being read goes by the same name as what the reading then reports. The one that
    /// has no part is about what a whole sitemap lists, and a word with no partner would be an exception
    /// at the point somebody's status is being written.
    /// </summary>
    [Fact]
    public void Everything_That_Can_Stop_A_Part_Goes_By_One_Name()
    {
        foreach (var outcome in Enum.GetValues<SitemapPartOutcome>().Except([SitemapPartOutcome.Read, SitemapPartOutcome.Unchanged]))
        {
            var problem = SitemapProblems.Of(outcome);

            problem.ToString().Should().Be(outcome.ToString());
            SitemapProblems.OutcomeOf(problem).Should().Be(outcome);
        }

        Enum.GetValues<SitemapProblem>().Except([SitemapProblem.AddressesWithQueries])
            .Should().AllSatisfy(problem => SitemapProblems.OutcomeOf(problem).ToString().Should().Be(problem.ToString()));
    }

    /// <summary>
    /// Everything a website can come back with while being asked for a sitemap has a word the dashboard
    /// has a sentence for. An answer that is not a page, or a success, cannot reach here at all, and if one
    /// did it is a website answering with something nobody asked it for.
    /// </summary>
    /// <param name="outcome">What came of asking.</param>
    /// <param name="problem">What the reading reports.</param>
    [Theory]
    [InlineData(FetchOutcome.NotFound, SitemapProblem.NotFound)]
    [InlineData(FetchOutcome.TurnedAway, SitemapProblem.TurnedAway)]
    [InlineData(FetchOutcome.AskedToSlowDown, SitemapProblem.TurnedAway)]
    [InlineData(FetchOutcome.ServerError, SitemapProblem.ServerError)]
    [InlineData(FetchOutcome.Unreachable, SitemapProblem.Unreachable)]
    [InlineData(FetchOutcome.Insecure, SitemapProblem.Insecure)]
    [InlineData(FetchOutcome.NotASitemap, SitemapProblem.NotASitemap)]
    [InlineData(FetchOutcome.TooLarge, SitemapProblem.TooLarge)]
    [InlineData(FetchOutcome.LeftTheWebsite, SitemapProblem.LeftTheWebsite)]
    [InlineData(FetchOutcome.TooManyRedirects, SitemapProblem.TooManyRedirects)]
    [InlineData(FetchOutcome.PrivateAddress, SitemapProblem.PrivateAddress)]
    [InlineData(FetchOutcome.RobotsDisallowed, SitemapProblem.RobotsDisallowed)]
    [InlineData(FetchOutcome.RobotsUnreadable, SitemapProblem.RobotsUnreadable)]
    [InlineData(FetchOutcome.NotAPage, SitemapProblem.ServerError)]
    [InlineData(FetchOutcome.Succeeded, SitemapProblem.ServerError)]
    [InlineData(FetchOutcome.NotModified, SitemapProblem.ServerError)]
    public void Every_Way_Of_Failing_Has_A_Word(FetchOutcome outcome, SitemapProblem problem)
    {
        SitemapProblems.Of(outcome).Should().Be(problem);
    }

    /// <summary>
    /// Trouble at the website is what the rest of it can wait out. A file that is not where it was said to
    /// be, or is not a sitemap, says nothing about how the next address would go.
    /// </summary>
    /// <param name="problem">What got in the way.</param>
    /// <param name="trouble">Whether it is the website itself.</param>
    [Theory]
    [InlineData(SitemapProblem.Unreachable, true)]
    [InlineData(SitemapProblem.TurnedAway, true)]
    [InlineData(SitemapProblem.ServerError, true)]
    [InlineData(SitemapProblem.Insecure, true)]
    [InlineData(SitemapProblem.TooSlow, true)]
    [InlineData(SitemapProblem.RobotsUnreadable, true)]
    [InlineData(SitemapProblem.NotFound, false)]
    [InlineData(SitemapProblem.NotASitemap, false)]
    [InlineData(SitemapProblem.LeftTheWebsite, false)]
    [InlineData(SitemapProblem.RobotsDisallowed, false)]
    public void Only_Trouble_At_The_Website_Keeps_The_Reader_Away(SitemapProblem problem, bool trouble)
    {
        SitemapProblems.IsTroubleAtTheWebsite(problem).Should().Be(trouble);
    }
}
