using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Classification;

namespace Dewiride.Analytics.Application.Tests.Sessions;

/// <summary>
/// Proves when a visit is judged, and when it is left alone.
/// </summary>
/// <remarks>
/// Every one of these is about the same promise: a verdict is reached once, about a visit that is
/// over, and it can be reached again from the same activity without changing anything.
/// </remarks>
public sealed class SessionClassifierTests
{
    /// <summary>
    /// A verdict on a visit still in progress would be replaced within the hour and would have
    /// been wrong in the meantime — and it is the reader half-way through their second page who
    /// would be called a crawler.
    /// </summary>
    [Fact]
    public async Task A_Visit_Still_In_Progress_Is_Not_Judged()
    {
        var harness = new JudgingHarness();
        harness.AnswerOnce(
            JudgingHarness.Visit(JudgingHarness.AddedAt.AddHours(1)),
            JudgingHarness.Visit(JudgingHarness.AddedAt.AddHours(2), isClosed: false));

        var outcome = await harness.RunAsync();

        outcome.Judged.Should().Be(1);
        harness.Stored.Should().ContainSingle()
            .Which.Session.StartedAt.Should().Be(JudgingHarness.AddedAt.AddHours(1));
    }

    /// <summary>
    /// The bookmark stops at the earliest visit that had not finished, so the next run picks that
    /// visit up rather than stepping over it.
    /// </summary>
    [Fact]
    public async Task The_Bookmark_Stops_At_The_Earliest_Unfinished_Visit()
    {
        var harness = new JudgingHarness();
        harness.Answer(JudgingHarness.Visit(JudgingHarness.AddedAt.AddHours(2), isClosed: false));

        await harness.RunAsync();

        harness.Bookmark.Should().Be(JudgingHarness.AddedAt.AddHours(2));
    }

    /// <summary>
    /// Finished visits after the unfinished one are judged all the same, and simply judged again
    /// next time. One visitor reading all afternoon must not hold up everything behind them.
    /// </summary>
    [Fact]
    public async Task Finished_Visits_After_An_Unfinished_One_Are_Still_Judged()
    {
        var harness = new JudgingHarness();
        harness.AnswerOnce(
            JudgingHarness.Visit(JudgingHarness.AddedAt.AddHours(2), isClosed: false),
            JudgingHarness.Visit(JudgingHarness.AddedAt.AddHours(3)));

        var outcome = await harness.RunAsync();

        outcome.Judged.Should().Be(1);
        harness.Windows[1].From.Should().Be(JudgingHarness.AddedAt.AddHours(2));
    }

    /// <summary>
    /// Nothing within an idle timeout of the present can be known to have finished, because a
    /// visitor who is merely pausing has not left.
    /// </summary>
    [Fact]
    public async Task Nothing_Is_Read_Past_The_Point_Where_A_Visit_Could_Still_Be_Running()
    {
        var harness = new JudgingHarness();

        await harness.RunAsync();

        harness.Windows.Should().NotBeEmpty();
        harness.Windows[^1].To.Should().Be(JudgingHarness.Now - harness.Settings.IdleTimeout);
    }

    [Fact]
    public async Task A_Site_Already_Caught_Up_Is_Not_Read_At_All()
    {
        var harness = new JudgingHarness();
        harness.ResumeFrom(JudgingHarness.Now);

        var outcome = await harness.RunAsync();

        harness.Windows.Should().BeEmpty();
        outcome.Judged.Should().Be(0);
    }

    /// <summary>
    /// A backlog is worked through in bounded stretches rather than in one enormous reconstruction,
    /// so an installation that has been switched off for a month recovers without asking the store
    /// to group a month of activity at once.
    /// </summary>
    [Fact]
    public async Task A_Backlog_Is_Worked_Through_In_Stretches()
    {
        var harness = new JudgingHarness
        {
            Settings = new ClassificationOptions { LongestPass = TimeSpan.FromHours(6) },
        };

        await harness.RunAsync();

        harness.Windows.Should().HaveCount(4);
        harness.Windows[0].From.Should().Be(JudgingHarness.AddedAt);
        harness.Windows[0].To.Should().Be(JudgingHarness.AddedAt.AddHours(6));
        harness.Windows[1].From.Should().Be(JudgingHarness.AddedAt.AddHours(6));
    }

    [Fact]
    public async Task A_Run_Yields_After_The_Number_Of_Stretches_It_Was_Given()
    {
        var harness = new JudgingHarness
        {
            Settings = new ClassificationOptions { LongestPass = TimeSpan.FromHours(6), PassesPerRun = 2 },
        };

        var outcome = await harness.RunAsync();

        harness.Windows.Should().HaveCount(2);
        outcome.ResumeFrom.Should().Be(JudgingHarness.AddedAt.AddHours(12));
    }

    [Fact]
    public async Task Judging_Resumes_From_Where_It_Stopped()
    {
        var harness = new JudgingHarness();
        harness.ResumeFrom(JudgingHarness.AddedAt.AddHours(20));

        await harness.RunAsync();

        harness.Windows[0].From.Should().Be(JudgingHarness.AddedAt.AddHours(20));
    }

    /// <summary>
    /// The verdict carries the rules that produced it, so a number on a screen can still be
    /// attributed a month later and a rebuild can be told apart from a regression.
    /// </summary>
    [Fact]
    public async Task Every_Verdict_Carries_The_Rules_That_Produced_It()
    {
        var harness = new JudgingHarness();
        harness.AnswerOnce(JudgingHarness.Visit(JudgingHarness.AddedAt.AddHours(1)));

        await harness.RunAsync();

        harness.Stored.Should().ContainSingle()
            .Which.Verdict.RulesetVersion.Should().Be(RulesetVersion.Current);
    }

    /// <summary>
    /// The tuning the engine was given travels with the question, because what counts as one visit
    /// is decided in the statement that groups the activity rather than afterwards.
    /// </summary>
    [Fact]
    public async Task The_Window_Carries_What_Counts_As_One_Visit()
    {
        var harness = new JudgingHarness
        {
            Settings = new ClassificationOptions
            {
                IdleTimeout = TimeSpan.FromMinutes(45),
                MaxRequestsPerSession = 250,
            },
        };

        await harness.RunAsync();

        harness.Windows[0].IdleTimeout.Should().Be(TimeSpan.FromMinutes(45));
        harness.Windows[0].MaxRequestsPerSession.Should().Be(250);
    }

    [Fact]
    public async Task Nothing_Is_Stored_When_There_Was_Nothing_To_Judge()
    {
        var harness = new JudgingHarness();

        var outcome = await harness.RunAsync();

        harness.Stored.Should().BeEmpty();
        outcome.Judged.Should().Be(0);
    }
}
