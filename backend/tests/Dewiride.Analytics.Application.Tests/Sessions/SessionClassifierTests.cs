using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Telemetry;

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
    /// Judging waits at the earliest visit that had not finished, so the next run picks that visit
    /// up rather than stepping over it. Reading does not wait for it.
    /// </summary>
    [Fact]
    public async Task Judging_Waits_At_The_Earliest_Unfinished_Visit_While_The_Reading_Moves_On()
    {
        var harness = new JudgingHarness();
        harness.Holding(JudgingHarness.Visit(JudgingHarness.AddedAt.AddHours(2), isClosed: false));

        await harness.RunAsync();

        harness.Bookmark.Should().Be(JudgingHarness.AddedAt.AddHours(2));
        harness.ReadThrough.Should().Be(JudgingHarness.Now - harness.Settings.IdleTimeout);
    }

    /// <summary>
    /// Finished visits after the unfinished one are judged all the same, and the next reading,
    /// which starts back at the unfinished one so as to find it again, leaves them out.
    /// </summary>
    [Fact]
    public async Task Finished_Visits_After_An_Unfinished_One_Are_Judged_Once()
    {
        var harness = new JudgingHarness();
        harness.Holding(
            JudgingHarness.Visit(JudgingHarness.AddedAt.AddHours(2), isClosed: false),
            JudgingHarness.Visit(JudgingHarness.AddedAt.AddHours(3)));

        var outcome = await harness.RunAsync();

        outcome.Judged.Should().Be(1);
        harness.Stored.Should().ContainSingle()
            .Which.Session.StartedAt.Should().Be(JudgingHarness.AddedAt.AddHours(3));
        harness.Windows[1].From.Should().Be(JudgingHarness.AddedAt.AddHours(2));
    }

    /// <summary>
    /// A page left open on somebody's screen reports every minute while it is in front of them, so
    /// their visit is not over until they close it. It waits; nothing that began after it does —
    /// however much longer than one stretch it stays open.
    /// </summary>
    [Fact]
    public async Task A_Visit_In_Progress_For_Longer_Than_A_Stretch_Holds_Back_Only_Itself()
    {
        var harness = new JudgingHarness
        {
            Settings = new ClassificationOptions { LongestPass = TimeSpan.FromHours(6) },
        };
        var open = JudgingHarness.AddedAt.AddHours(1);
        harness.Holding(
            JudgingHarness.Visit(open, isClosed: false),
            JudgingHarness.Visit(open.AddHours(8)),
            JudgingHarness.Visit(open.AddHours(14)),
            JudgingHarness.Visit(open.AddHours(21)));

        var outcome = await harness.RunAsync();

        outcome.Judged.Should().Be(3);
        harness.Stored.Select(judgement => judgement.Session.StartedAt)
            .Should().Equal(open.AddHours(8), open.AddHours(14), open.AddHours(21));
        harness.Bookmark.Should().Be(open);
        harness.ReadThrough.Should().Be(JudgingHarness.Now - harness.Settings.IdleTimeout);
    }

    /// <summary>
    /// Once a run has read up to the present, the next one has half a minute of new ground and the
    /// visit still open somewhere behind it. One reading covers both: it starts where judging waits
    /// and stops at the new horizon, however far back the open visit began.
    /// </summary>
    [Fact]
    public async Task A_Run_Behind_A_Visit_Still_Open_Reads_Once_From_It_To_The_Present()
    {
        var harness = new JudgingHarness();
        var horizon = JudgingHarness.Now - harness.Settings.IdleTimeout;
        var open = horizon.AddHours(-18);
        harness.ResumeFrom(open, horizon - harness.Settings.Interval);
        harness.Holding(JudgingHarness.Visit(open, isClosed: false));

        await harness.RunAsync();

        harness.Windows.Should().ContainSingle();
        harness.Windows[0].From.Should().Be(open);
        harness.Windows[0].To.Should().Be(horizon);
        harness.Bookmark.Should().Be(open);
        harness.ReadThrough.Should().Be(horizon);
    }

    /// <summary>
    /// How far a reading reaches is counted from where the last one stopped, not from where judging
    /// waits: between the two there is nothing but visits already judged, which are left out, and
    /// the visits still waiting.
    /// </summary>
    [Fact]
    public async Task How_Far_A_Reading_Reaches_Is_Counted_From_Where_The_Last_One_Stopped()
    {
        var harness = new JudgingHarness
        {
            Settings = new ClassificationOptions { LongestPass = TimeSpan.FromHours(6) },
        };
        harness.ResumeFrom(JudgingHarness.AddedAt.AddHours(1), JudgingHarness.AddedAt.AddHours(10));

        await harness.RunAsync();

        harness.Windows[0].From.Should().Be(JudgingHarness.AddedAt.AddHours(1));
        harness.Windows[0].To.Should().Be(JudgingHarness.AddedAt.AddHours(16));
    }

    /// <summary>
    /// Every reading is told the rules it is judging under, which is what lets the store leave out
    /// what those rules have judged already — and only what they have.
    /// </summary>
    [Fact]
    public async Task Every_Reading_Names_The_Rules_It_Judges_Under()
    {
        var harness = new JudgingHarness();

        await harness.RunAsync();

        harness.Windows.Should().NotBeEmpty()
            .And.OnlyContain(window => window.UnjudgedUnder == RulesetVersion.Current);
    }

    /// <summary>
    /// The next run starts back at the open visit and leaves out what it judged last time, so a
    /// visit is judged once however many runs pass before the page left open is closed — and the
    /// next run reads once, from the open visit to its own horizon.
    /// </summary>
    [Fact]
    public async Task A_Visit_Judged_Behind_One_Still_Open_Is_Not_Judged_Again_On_The_Next_Run()
    {
        var harness = new JudgingHarness();
        harness.Holding(
            JudgingHarness.Visit(JudgingHarness.AddedAt.AddHours(2), isClosed: false),
            JudgingHarness.Visit(JudgingHarness.AddedAt.AddHours(3)));

        await harness.RunAsync();
        var readBefore = harness.Windows.Count;
        harness.Wait(harness.Settings.Interval);
        var next = await harness.RunAsync();

        next.Judged.Should().Be(0);
        harness.Stored.Should().ContainSingle();
        harness.Windows.Should().HaveCount(readBefore + 1);
        harness.Windows[^1].From.Should().Be(JudgingHarness.AddedAt.AddHours(2));
        harness.Windows[^1].To.Should().Be(
            JudgingHarness.Now + harness.Settings.Interval - harness.Settings.IdleTimeout);
    }

    /// <summary>
    /// When the page left open is finally closed, its visit is judged once, on all of itself, and
    /// judging moves on past it to wherever the reading has got.
    /// </summary>
    [Fact]
    public async Task A_Visit_That_Held_Judging_Back_Is_Judged_Once_It_Closes_And_Judging_Moves_On()
    {
        var harness = new JudgingHarness();
        var open = JudgingHarness.Visit(JudgingHarness.AddedAt.AddHours(2), isClosed: false);
        var behind = JudgingHarness.Visit(JudgingHarness.AddedAt.AddHours(3));
        var closed = open with
        {
            IsClosed = true,
            Evidence = open.Evidence with { EndedAt = open.Evidence.EndedAt.AddHours(5) },
        };
        harness.Holding(open, behind);

        await harness.RunAsync();
        harness.Holding(closed, behind);
        harness.Wait(harness.Settings.Interval);
        var next = await harness.RunAsync();

        next.Judged.Should().Be(1);
        harness.Stored[^1].Session.SessionKey.Should().Be(open.Evidence.SessionKey);
        harness.Stored[^1].Session.EndedAt.Should().Be(closed.Evidence.EndedAt);
        harness.Bookmark.Should().Be(
            JudgingHarness.Now + harness.Settings.Interval - harness.Settings.IdleTimeout);
    }

    /// <summary>
    /// A visit judged already that a later report puts back in progress is waited on like any
    /// other visit still in progress: judging stops where it began until it is over again.
    /// </summary>
    [Fact]
    public async Task Judging_Waits_At_A_Judged_Visit_A_Later_Report_Has_Put_Back_In_Progress()
    {
        var harness = new JudgingHarness();
        var open = JudgingHarness.Visit(JudgingHarness.AddedAt.AddHours(1), isClosed: false);
        var judged = JudgingHarness.Visit(JudgingHarness.AddedAt.AddHours(2));
        var closed = open with { IsClosed = true };
        var growing = judged with
        {
            IsClosed = false,
            Evidence = judged.Evidence with { EndedAt = judged.Evidence.EndedAt.AddHours(3) },
        };
        harness.Holding(open, judged);

        await harness.RunAsync();
        harness.Holding(closed, growing);
        harness.Wait(harness.Settings.Interval);
        await harness.RunAsync();

        harness.Bookmark.Should().Be(JudgingHarness.AddedAt.AddHours(2));
        harness.Stored.Select(judgement => judgement.Session.SessionKey)
            .Should().Equal(judged.Evidence.SessionKey, open.Evidence.SessionKey);
    }

    /// <summary>
    /// A report that arrives after a visit was judged and belongs to it — a page announcing it is
    /// being left, hours after it was opened — makes it a longer visit than the one judged. Once it
    /// is over it is judged again, on all of itself.
    /// </summary>
    [Fact]
    public async Task A_Visit_Made_Longer_After_It_Was_Judged_Is_Judged_Again_On_All_Of_Itself()
    {
        var harness = new JudgingHarness();
        var open = JudgingHarness.Visit(JudgingHarness.AddedAt.AddHours(1), isClosed: false);
        var judged = JudgingHarness.Visit(JudgingHarness.AddedAt.AddHours(2));
        var longer = judged with
        {
            Evidence = judged.Evidence with { EndedAt = judged.Evidence.EndedAt.AddHours(1) },
        };
        harness.Holding(open, judged);

        await harness.RunAsync();
        harness.Holding(open, longer);
        harness.Wait(harness.Settings.Interval);
        var again = await harness.RunAsync();

        again.Judged.Should().Be(1);
        harness.Stored.Select(judgement => judgement.Session.EndedAt)
            .Should().Equal(judged.Evidence.EndedAt, longer.Evidence.EndedAt);
    }

    /// <summary>
    /// Another instance of the engine that has already been at least as far is the one to carry on,
    /// and this one stops rather than reading what the other has read.
    /// </summary>
    [Fact]
    public async Task A_Run_Stops_Where_Another_Engine_Has_Already_Been()
    {
        var harness = new JudgingHarness { AnotherEngineIsAhead = true };

        await harness.RunAsync();

        harness.Windows.Should().ContainSingle();
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
    public async Task A_Site_Judged_As_Far_As_The_Horizon_Is_Not_Read_At_All()
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

    /// <summary>
    /// A run reads up to the horizon it began with: an idle timeout before the moment it started,
    /// the latest a visit can have begun and be known to be over. The present moves on while a read
    /// is running, and a site already up to date is read once a run rather than again and again in
    /// slivers as wide as the last read took — which, on every site, every half minute, is most of
    /// the work a store would otherwise be asked to do.
    /// </summary>
    [Fact]
    public async Task A_Site_That_Is_Up_To_Date_Is_Read_Once_However_Long_The_Read_Takes()
    {
        var harness = new JudgingHarness { EachReadTakes = TimeSpan.FromMilliseconds(140) };
        var horizon = JudgingHarness.Now - harness.Settings.IdleTimeout;
        harness.ResumeFrom(horizon - harness.Settings.Interval);

        await harness.RunAsync();

        harness.Windows.Should().ContainSingle()
            .Which.To.Should().Be(horizon);
        harness.Bookmark.Should().Be(horizon);
    }

    [Fact]
    public async Task A_Backlog_Run_Ends_At_The_Horizon_It_Began_With_However_Long_Each_Stretch_Takes()
    {
        var harness = new JudgingHarness
        {
            Settings = new ClassificationOptions { LongestPass = TimeSpan.FromHours(6) },
            EachReadTakes = TimeSpan.FromMinutes(1),
        };

        await harness.RunAsync();

        harness.Windows.Should().HaveCount(4);
        harness.Windows[^1].To.Should().Be(JudgingHarness.Now - harness.Settings.IdleTimeout);
    }

    /// <summary>
    /// Once a pass has read as far as the horizon, a visit it found still in progress stays in
    /// progress for the rest of the run, so reading again from that visit would find nothing new.
    /// Judging waits on the visit, and the next run, with a later horizon, reads from there.
    /// </summary>
    [Fact]
    public async Task A_Visit_Still_In_Progress_At_The_Horizon_Is_Left_To_The_Next_Run()
    {
        var harness = new JudgingHarness();
        var horizon = JudgingHarness.Now - harness.Settings.IdleTimeout;
        var resumeFrom = horizon - harness.Settings.Interval;
        harness.ResumeFrom(resumeFrom);
        harness.Holding(JudgingHarness.Visit(resumeFrom.AddSeconds(10), isClosed: false));

        await harness.RunAsync();

        harness.Windows.Should().ContainSingle();
        harness.Bookmark.Should().Be(resumeFrom.AddSeconds(10));
        harness.ReadThrough.Should().Be(horizon);
    }

    /// <summary>
    /// Two instances of the engine reading the same stretch at once can each judge one visit, and
    /// the stored verdict with the later stamp is the one that stands — so each pass's stamp is when
    /// that pass began rather than when its run did.
    /// </summary>
    [Fact]
    public async Task Each_Pass_Stamps_Its_Verdicts_With_The_Moment_It_Began()
    {
        var harness = new JudgingHarness
        {
            Settings = new ClassificationOptions { LongestPass = TimeSpan.FromHours(6) },
            EachReadTakes = TimeSpan.FromMinutes(1),
        };
        harness.Holding(
            JudgingHarness.Visit(JudgingHarness.AddedAt.AddHours(1), isClosed: false),
            JudgingHarness.Visit(JudgingHarness.AddedAt.AddHours(2)),
            JudgingHarness.Visit(JudgingHarness.AddedAt.AddHours(8)));

        await harness.RunAsync();

        harness.Stored.Select(judgement => judgement.Session.StartedAt)
            .Should().Equal(JudgingHarness.AddedAt.AddHours(2), JudgingHarness.AddedAt.AddHours(8));
        harness.StampedAt.Should().Equal(JudgingHarness.Now, JudgingHarness.Now.AddMinutes(1));
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

    /// <summary>
    /// The site's own address travels with every reading, because it is what tells a reader moving
    /// from one of its pages to the next from a visitor another site sent.
    /// </summary>
    [Fact]
    public async Task Every_Reading_Carries_The_Sites_Own_Address()
    {
        var harness = new JudgingHarness();
        harness.ResumeFrom(JudgingHarness.Now.AddDays(-1));

        await harness.RunAsync();

        harness.Windows.Should().NotBeEmpty()
            .And.OnlyContain(window => window.SiteDomain == JudgingHarness.SiteDomain);
    }

    /// <summary>
    /// Somebody another site sent, who then pressed the pointer or read the page, is somebody
    /// reading even on a rented network — and an address that may be a person's is not sent to a
    /// name server.
    /// </summary>
    [Fact]
    public async Task A_Reader_Another_Site_Sent_Over_A_Rented_Network_Is_Never_Asked_About()
    {
        var harness = new JudgingHarness();
        var reader = JudgingHarness.Reader(JudgingHarness.AddedAt.AddHours(1), "20.42.10.7");

        harness.Settling("20.42.10.7", "Microsoft");
        harness.AnswerOnce(reader with
        {
            Evidence = reader.Evidence with { AutonomousSystem = 8075, SentBy = SourceChannel.Search },
        });

        await harness.RunAsync();

        harness.Asked.Should().BeEmpty();
        harness.Stored.Should().ContainSingle().Which.Verdict.Category.Should().Be(TrafficCategory.LikelyHuman);
    }

    /// <summary>
    /// The same reading, come straight in with no key pressed, is machinery on a rented network,
    /// and so its address is asked about.
    /// </summary>
    [Fact]
    public async Task A_Visit_That_Came_Straight_Over_A_Rented_Network_Is_Still_Asked_About()
    {
        var harness = new JudgingHarness();
        var reader = JudgingHarness.Reader(JudgingHarness.AddedAt.AddHours(1), "20.42.10.7");

        harness.AnswerOnce(reader with { Evidence = reader.Evidence with { AutonomousSystem = 8075 } });

        await harness.RunAsync();

        harness.Asked.Should().ContainSingle().Which.Should().Equal("20.42.10.7");
        harness.Stored.Should().ContainSingle()
            .Which.Verdict.Category.Should().Be(TrafficCategory.SuspiciousAutomation);
    }

    [Fact]
    public async Task Nothing_Is_Stored_When_There_Was_Nothing_To_Judge()
    {
        var harness = new JudgingHarness();

        var outcome = await harness.RunAsync();

        harness.Stored.Should().BeEmpty();
        outcome.Judged.Should().Be(0);
    }

    /// <summary>
    /// The case no catalogue of names could ever reach: a crawler that says nothing about itself,
    /// whose operator publishes no list of addresses. All that is left is what its address is
    /// called, and asking is the only way to find out.
    /// </summary>
    [Fact]
    public async Task A_Visit_That_Looks_Like_Machinery_Has_Its_Address_Asked_About()
    {
        var harness = new JudgingHarness();
        harness.Settling("77.88.5.1", "Microsoft");
        harness.AnswerOnce(JudgingHarness.Visit(
            JudgingHarness.AddedAt.AddHours(1),
            address: "77.88.5.1",
            userAgent: JudgingHarness.Anonymous));

        await harness.RunAsync();

        harness.Asked.Should().ContainSingle().Which.Should().Equal("77.88.5.1");
        harness.Stored.Should().ContainSingle();
        harness.Stored[0].Session.ConfirmedOperator.Should().Be("Microsoft");
        harness.Stored[0].Verdict.Category.Should().Be(TrafficCategory.KnownSearchCrawler);
        harness.Stored[0].Verdict.Strength.Should().Be(EvidenceStrength.Verified);
    }

    /// <summary>
    /// The line this check is not allowed to cross. A visit that reads like somebody reading is
    /// somebody reading, and their address is not ours to send to a name server out of curiosity.
    /// </summary>
    [Fact]
    public async Task A_Visit_That_Looks_Like_Somebody_Reading_Is_Never_Asked_About()
    {
        var harness = new JudgingHarness();
        harness.AnswerOnce(JudgingHarness.Reader(JudgingHarness.AddedAt.AddHours(1), "203.0.113.7"));

        await harness.RunAsync();

        harness.Asked.Should().BeEmpty();
        harness.Stored.Should().ContainSingle();
        harness.Stored[0].Verdict.Category.Should().Be(TrafficCategory.LikelyHuman);
    }

    /// <summary>
    /// A visit nothing could be concluded about is not thereby machinery. Where anything observed
    /// points toward a person, the address is treated as a person's — which is the only safe way to
    /// treat an address the product is not sure about.
    /// </summary>
    [Fact]
    public async Task A_Visit_Nothing_Could_Be_Concluded_About_Whose_Browser_Spoke_Is_Not_Asked_About()
    {
        var harness = new JudgingHarness();
        harness.AnswerOnce(JudgingHarness.Glance(JudgingHarness.AddedAt.AddHours(1), "203.0.113.9"));

        await harness.RunAsync();

        harness.Asked.Should().BeEmpty();
        harness.Stored.Should().ContainSingle();
        harness.Stored[0].Verdict.Category.Should().Be(TrafficCategory.Unknown);
    }

    /// <summary>
    /// Already settled when the activity was collected, against a list the company publishes.
    /// There is nothing left to establish and no reason to ask anybody.
    /// </summary>
    [Fact]
    public async Task A_Visit_Already_Settled_At_Collection_Is_Not_Asked_About()
    {
        var harness = new JudgingHarness();
        harness.AnswerOnce(JudgingHarness.Visit(
            JudgingHarness.AddedAt.AddHours(1),
            address: "66.249.64.9",
            userAgent: JudgingHarness.Anonymous,
            confirmedOperator: "Google"));

        await harness.RunAsync();

        harness.Asked.Should().BeEmpty();
        harness.Stored[0].Session.ConfirmedOperator.Should().Be("Google");
    }

    /// <summary>
    /// Anything older than the retention window has no address left on it, which is the ordinary
    /// state of a visit being judged again under a later ruleset.
    /// </summary>
    [Fact]
    public async Task A_Visit_With_No_Address_Left_Is_Not_Asked_About()
    {
        var harness = new JudgingHarness();
        harness.AnswerOnce(JudgingHarness.Visit(JudgingHarness.AddedAt.AddHours(1)));

        await harness.RunAsync();

        harness.Asked.Should().BeEmpty();
        harness.Stored[0].Session.ConfirmedOperator.Should().BeNull();
    }

    /// <summary>
    /// An address that answers to a company's own name belongs to that company whatever any one
    /// visit from it looked like, and two visits from one address cannot have been two companies.
    /// </summary>
    [Fact]
    public async Task One_Settled_Address_Settles_Every_Visit_From_It()
    {
        var harness = new JudgingHarness();
        harness.Settling("77.88.5.1", "Microsoft");
        harness.AnswerOnce(
            JudgingHarness.Visit(
                JudgingHarness.AddedAt.AddHours(1),
                address: "77.88.5.1",
                userAgent: JudgingHarness.Anonymous),
            JudgingHarness.Reader(JudgingHarness.AddedAt.AddHours(2), "77.88.5.1"));

        await harness.RunAsync();

        harness.Asked.Should().ContainSingle().Which.Should().Equal("77.88.5.1");
        harness.Stored.Should().HaveCount(2);
        harness.Stored.Should().OnlyContain(judgement => judgement.Session.ConfirmedOperator == "Microsoft");
    }

    /// <summary>
    /// Wearing one company's name while arriving from another's machines. Nothing legitimate
    /// produces that, and the address establishing it by name rather than by list makes no
    /// difference to what it means.
    /// </summary>
    [Fact]
    public async Task A_Visitor_Wearing_The_Wrong_Companys_Name_Is_Called_Out()
    {
        var harness = new JudgingHarness();
        harness.Settling("77.88.5.1", "Microsoft");
        harness.AnswerOnce(JudgingHarness.Visit(JudgingHarness.AddedAt.AddHours(1), address: "77.88.5.1"));

        await harness.RunAsync();

        harness.Stored[0].Verdict.Category.Should().Be(TrafficCategory.SuspiciousAutomation);
        harness.Stored[0].Verdict.Supporting.Should()
            .Contain(signal => signal.Code == SignalCodes.FalseCrawlerClaim);
    }

    /// <summary>
    /// A site whose whole history is being judged at once is the case this bounds. A visit past
    /// the cap is judged on everything else known about it, which is what every visit was judged
    /// on before this check existed.
    /// </summary>
    [Fact]
    public async Task No_More_Addresses_Are_Asked_About_Than_A_Pass_Allows()
    {
        var harness = new JudgingHarness { Settings = new ClassificationOptions { MostNameChecksPerPass = 2 } };
        harness.AnswerOnce(
            JudgingHarness.Visit(JudgingHarness.AddedAt.AddHours(1), address: "198.51.100.1"),
            JudgingHarness.Visit(JudgingHarness.AddedAt.AddHours(2), address: "198.51.100.2"),
            JudgingHarness.Visit(JudgingHarness.AddedAt.AddHours(3), address: "198.51.100.3"));

        await harness.RunAsync();

        harness.Asked.Should().ContainSingle().Which.Should().HaveCount(2);
        harness.Stored.Should().HaveCount(3);
    }

    /// <summary>
    /// The address an answer like this is worked out from is cleared after three days. Kept beside
    /// the visit, the answer is still there when the visit is judged again under better rules,
    /// rather than the visit going back to being a claim.
    /// </summary>
    [Fact]
    public async Task An_Identity_Settled_By_Name_Is_Kept_For_Its_Visit()
    {
        var harness = new JudgingHarness();
        var visit = JudgingHarness.Visit(
            JudgingHarness.AddedAt.AddHours(1),
            address: "77.88.5.1",
            userAgent: JudgingHarness.Anonymous);

        harness.Settling("77.88.5.1", "Yandex");
        harness.AnswerOnce(visit);

        await harness.RunAsync();

        harness.Kept.Should().ContainSingle().Which.Should().Be(
            new NameConfirmation(visit.Evidence.SessionKey, visit.Evidence.StartedAt, "Yandex"));
    }

    /// <summary>
    /// A settled address settles every visit from it, and every one of them has to keep the answer:
    /// the visit that prompted the question is not the only one that would lose it.
    /// </summary>
    [Fact]
    public async Task Every_Visit_A_Settled_Address_Settles_Keeps_The_Answer()
    {
        var harness = new JudgingHarness();
        harness.Settling("77.88.5.1", "Yandex");
        harness.AnswerOnce(
            JudgingHarness.Visit(
                JudgingHarness.AddedAt.AddHours(1),
                address: "77.88.5.1",
                userAgent: JudgingHarness.Anonymous),
            JudgingHarness.Reader(JudgingHarness.AddedAt.AddHours(2), "77.88.5.1"));

        await harness.RunAsync();

        harness.Kept.Should().HaveCount(2).And.OnlyContain(answer => answer.Operator == "Yandex");
        harness.Kept.Select(answer => answer.SessionKey).Should()
            .Equal(harness.Stored.Select(judgement => judgement.Session.SessionKey));
    }

    /// <summary>
    /// What a visitor wearing another company's name is caught by is the company its address
    /// belongs to, and that is what is kept — never the name it gave, which is a claim.
    /// </summary>
    [Fact]
    public async Task A_Visitor_Wearing_The_Wrong_Companys_Name_Keeps_The_Company_Its_Address_Belongs_To()
    {
        var harness = new JudgingHarness();
        harness.Settling("77.88.5.1", "Microsoft");
        harness.AnswerOnce(JudgingHarness.Visit(JudgingHarness.AddedAt.AddHours(1), address: "77.88.5.1"));

        await harness.RunAsync();

        harness.Kept.Should().ContainSingle().Which.Operator.Should().Be("Microsoft");
    }

    /// <summary>
    /// An address that answers to nobody's name settles nothing, and nothing is kept for it: an
    /// answer kept for a visit is always a company.
    /// </summary>
    [Fact]
    public async Task Nothing_Is_Kept_Where_Nothing_Was_Settled()
    {
        var harness = new JudgingHarness();
        harness.AnswerOnce(JudgingHarness.Visit(
            JudgingHarness.AddedAt.AddHours(1),
            address: "198.51.100.1",
            userAgent: JudgingHarness.Anonymous));

        await harness.RunAsync();

        harness.Asked.Should().ContainSingle();
        harness.Kept.Should().BeEmpty();
        harness.Writes.Should().Equal(JudgingHarness.VerdictsWritten);
    }

    /// <summary>
    /// A visit the activity already speaks for keeps the answer it arrived with, even in a pass
    /// that settles its address afresh because another visit from it said nothing, and nothing more
    /// is kept for it. The answer on the activity was reached as each report arrived, and it is the
    /// one the visit is rebuilt with every time.
    /// </summary>
    [Fact]
    public async Task A_Visit_Already_Settled_Keeps_Its_Answer_And_Nothing_Beside_It()
    {
        var harness = new JudgingHarness();
        var stamped = JudgingHarness.Visit(
            JudgingHarness.AddedAt.AddHours(1),
            address: "66.249.64.9",
            userAgent: JudgingHarness.Anonymous,
            confirmedOperator: "Google");
        var unstamped = JudgingHarness.Visit(
            JudgingHarness.AddedAt.AddHours(2),
            address: "66.249.64.9",
            userAgent: JudgingHarness.Anonymous);

        harness.Settling("66.249.64.9", "Microsoft");
        harness.AnswerOnce(stamped, unstamped);

        await harness.RunAsync();

        harness.Asked.Should().ContainSingle().Which.Should().Equal("66.249.64.9");
        harness.Kept.Select(answer => answer.SessionKey).Should().Equal(unstamped.Evidence.SessionKey);
        harness.Stored.Single(judgement => judgement.Session.SessionKey == stamped.Evidence.SessionKey)
            .Session.ConfirmedOperator.Should().Be("Google");
        harness.Stored.Single(judgement => judgement.Session.SessionKey == unstamped.Evidence.SessionKey)
            .Session.ConfirmedOperator.Should().Be("Microsoft");
    }

    /// <summary>
    /// The answer goes first, stamped with the same moment as the verdicts, so a verdict is never
    /// stored that a later reading of its visit could not reach again.
    /// </summary>
    [Fact]
    public async Task The_Answer_Is_Kept_Before_The_Verdict_That_Rests_On_It()
    {
        var harness = new JudgingHarness();
        harness.Settling("77.88.5.1", "Yandex");
        harness.AnswerOnce(JudgingHarness.Visit(
            JudgingHarness.AddedAt.AddHours(1),
            address: "77.88.5.1",
            userAgent: JudgingHarness.Anonymous));

        await harness.RunAsync();

        harness.Writes.Should().Equal(JudgingHarness.AnswersWritten, JudgingHarness.VerdictsWritten);
        harness.KeptAt.Should().Equal(harness.StampedAt);
    }

    /// <summary>
    /// Where the answers cannot be kept, the verdicts resting on them are not stored either, and
    /// the bookmark stays where it was: the next run reads the same visits, asks again, and reaches
    /// the same conclusions with somewhere to keep them.
    /// </summary>
    [Fact]
    public async Task A_Pass_Whose_Answers_Cannot_Be_Kept_Stores_No_Verdict_And_Moves_Nothing()
    {
        var harness = new JudgingHarness { KeepingFails = true };
        harness.Settling("77.88.5.1", "Yandex");
        harness.AnswerOnce(JudgingHarness.Visit(
            JudgingHarness.AddedAt.AddHours(1),
            address: "77.88.5.1",
            userAgent: JudgingHarness.Anonymous));

        var run = () => harness.RunAsync();

        await run.Should().ThrowAsync<InvalidOperationException>();

        harness.Stored.Should().BeEmpty();
        harness.Bookmark.Should().Be(JudgingHarness.AddedAt);
        harness.ReadThrough.Should().Be(JudgingHarness.AddedAt);
    }
}
