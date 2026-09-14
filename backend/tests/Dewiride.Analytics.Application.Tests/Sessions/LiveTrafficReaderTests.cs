using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Classification.Sessions;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Dewiride.Analytics.Application.Tests.Sessions;

/// <summary>
/// Covers the reading that answers who is on a site at this moment.
/// </summary>
/// <remarks>
/// Three things are being proven, and they are the three a live screen could get wrong without
/// anybody noticing: which stretch of minutes it asks about, that a trail is read over the very
/// stretch its row was drawn from, and how little it is willing to say about the people in it.
/// </remarks>
public sealed class LiveTrafficReaderTests
{
    private static readonly Guid SiteId = Guid.Parse("0197c0de-0000-7000-8000-000000000001");
    private static readonly Guid OrganizationId = Guid.Parse("0197c0de-0000-7000-8000-0000000000ff");

    /// <summary>A visitor of the shape the engine derives, which is what a row carries.</summary>
    private const string Somebody = "2f8a1c0b4d6e7f905a1b2c3d4e5f6071";

    /// <summary>A moment part-way through a minute, which is where every real reading is taken.</summary>
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 37, TimeSpan.Zero);

    private readonly ITelemetryQueries _telemetry = Substitute.For<ITelemetryQueries>();
    private readonly FakeTimeProvider _clock = new(Now);

    [Fact]
    public async Task Asks_About_As_Long_As_A_Visitor_May_Be_Quiet_Before_Their_Visit_Is_Over()
    {
        Answering();

        await Read();

        var asked = _telemetry.ReceivedCalls()
            .Select(call => call.GetArguments()[1])
            .OfType<SiteLiveVisitorsQuery>()
            .Single();

        // The whole minute the timeout landed in, so the near end is never part of one.
        asked.Range.From.Should().Be(new DateTimeOffset(2026, 3, 1, 11, 30, 0, TimeSpan.Zero));
        asked.Range.To.Should().Be(Now);
        (asked.Range.To - asked.Range.From).Should().BeGreaterThanOrEqualTo(new ClassificationOptions().IdleTimeout);
    }

    [Fact]
    public async Task Asks_Every_Question_About_The_Same_Stretch_Of_Minutes()
    {
        Answering();

        await Read();

        var windows = _telemetry.ReceivedCalls()
            .Select(call => call.GetArguments()[1])
            .OfType<AnalyticsQuery>()
            .Select(query => query.Range)
            .ToArray();

        windows.Should().HaveCount(3);
        windows.Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task Names_A_Crawler_Whose_Owner_Vouches_For_It()
    {
        Answering(Visitor("vouched", Watching.ACrawlerWhoseOwnerVouchesForIt()));

        var reading = await Read();

        reading.Visitors.Should().ContainSingle();
        var named = reading.Visitors[0].Named;

        named.Should().NotBeNull();
        named.Strength.Should().Be(EvidenceStrength.Verified);
    }

    [Fact]
    public async Task Says_Nothing_About_Somebody_Who_Is_Still_Reading()
    {
        Answering(Visitor("reader", Watching.AReader()));

        var reading = await Read();

        reading.Visitors.Should().ContainSingle();
        reading.Visitors[0].Named.Should().BeNull();
    }

    /// <summary>
    /// The moment before a reader's browser has said anything is the one this whole reading has to
    /// get right, because the engine's honest answer for it is machinery and shows in red.
    /// </summary>
    [Fact]
    public async Task Says_Nothing_About_A_Reader_Whose_Browser_Has_Not_Spoken_Yet()
    {
        Answering(Visitor("arriving", Watching.AReaderWhoseBrowserHasNotSpokenYet()));

        var reading = await Read();

        reading.Visitors[0].Named.Should().BeNull();
    }

    /// <summary>
    /// How busy a site is and how much of that one answer had room for are separate figures. A
    /// reading that reported the length of its own list would say a sweep of three hundred was a
    /// hundred.
    /// </summary>
    [Fact]
    public async Task Says_How_Busy_The_Site_Is_Rather_Than_How_Long_Its_Own_List_Was()
    {
        Answering(312, Visitor("one", Watching.AReader()), Visitor("two", Watching.AScanner()));

        var reading = await Read();

        reading.VisitorsSeen.Should().Be(312);
        reading.Visitors.Should().HaveCount(2);
    }

    [Fact]
    public async Task Stamps_The_Reading_With_The_Moment_It_Was_Taken()
    {
        Answering();

        var reading = await Read();

        reading.At.Should().Be(Now);
    }

    [Fact]
    public async Task Answers_A_Site_Nobody_Is_On_Without_Inventing_Anybody()
    {
        Answering();

        var reading = await Read();

        reading.VisitorsSeen.Should().Be(0);
        reading.Visitors.Should().BeEmpty();
        reading.Minutes.Should().BeEmpty();
        reading.Pages.Should().BeEmpty();
    }

    /// <summary>
    /// A trail is opened from a row, and the pages it shows have to be the pages that row counted.
    /// That holds only while both cover the same minutes — so a trail is read under the reading's
    /// own instant, and the clock having moved on since, even across a minute boundary, changes
    /// nothing about which minutes it covers.
    /// </summary>
    [Fact]
    public async Task Asks_For_A_Trail_Over_The_Stretch_The_List_Was_Drawn_From()
    {
        Answering();

        var reading = await Read();

        // Past the minute the reading was taken in, so a trail read about a fresh present moment
        // would begin a minute later than the row did.
        _clock.Advance(TimeSpan.FromSeconds(40));

        await ReadTrail(reading.At);

        var windows = _telemetry.ReceivedCalls()
            .Select(call => call.GetArguments()[1])
            .OfType<AnalyticsQuery>()
            .Select(query => query.Range)
            .ToArray();

        windows.Should().HaveCount(4);
        windows.Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task Stamps_A_Trail_With_The_Reading_It_Sits_Under()
    {
        Answering();
        _clock.Advance(TimeSpan.FromSeconds(40));

        var trail = await ReadTrail(Now);

        trail.At.Should().Be(Now);
    }

    /// <summary>
    /// A reading is stamped by the engine's own clock, so an instant ahead of it is another instance
    /// of the engine a few seconds off, or a request somebody wrote by hand. The first is allowed
    /// and the second is not.
    /// </summary>
    [Fact]
    public async Task Refuses_A_Trail_Under_A_Reading_From_The_Future()
    {
        Answering();

        var act = () => ReadTrail(Now + LiveTrafficReader.Skew + TimeSpan.FromSeconds(1));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Allows_A_Reading_A_Little_Ahead_Of_This_Clock()
    {
        Answering();

        var trail = await ReadTrail(Now.AddSeconds(30));

        trail.At.Should().Be(Now.AddSeconds(30));
    }

    /// <summary>
    /// A screen somebody has held still may be an hour old and its rows must still open. A day back
    /// is as far as any visit runs, and is what the list of finished visits already answers about.
    /// The reader allows a minute past that: the endpoint has already asked the same question
    /// against its own reading of the clock, and an instant it admitted must not be refused here
    /// because the clock moved on in between.
    /// </summary>
    [Fact]
    public async Task Refuses_A_Trail_Under_A_Reading_Older_Than_A_Day()
    {
        Answering();

        var act = () => ReadTrail(Now - VisitorKeys.LongestVisit - LiveTrafficReader.Skew - TimeSpan.FromSeconds(1));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Allows_A_Reading_A_Day_Old()
    {
        Answering();

        var trail = await ReadTrail(Now - VisitorKeys.LongestVisit);

        trail.At.Should().Be(Now - VisitorKeys.LongestVisit);
    }

    [Fact]
    public async Task Allows_A_Reading_Admitted_A_Moment_Before_It_Turned_A_Day_Old()
    {
        Answering();
        var admitted = Now - VisitorKeys.LongestVisit;

        _clock.Advance(TimeSpan.FromSeconds(5));

        var trail = await ReadTrail(admitted);

        trail.At.Should().Be(admitted);
    }

    [Fact]
    public void Says_Whether_An_Instant_Is_One_A_Reading_Could_Have_Been_Taken_At()
    {
        var reader = Reader();

        reader.IsAboutNow(Now).Should().BeTrue();
        reader.IsAboutNow(Now + LiveTrafficReader.Skew).Should().BeTrue();
        reader.IsAboutNow(Now + LiveTrafficReader.Skew + TimeSpan.FromTicks(1)).Should().BeFalse();
        reader.IsAboutNow(Now - VisitorKeys.LongestVisit).Should().BeTrue();
        reader.IsAboutNow(Now - VisitorKeys.LongestVisit - TimeSpan.FromTicks(1)).Should().BeFalse();
    }

    [Fact]
    public async Task Asks_For_A_Trail_By_The_Visitor_It_Was_Given()
    {
        Answering();

        await ReadTrail(Now);

        var asked = _telemetry.ReceivedCalls()
            .Select(call => call.GetArguments()[1])
            .OfType<SiteLiveTrailQuery>()
            .Single();

        asked.VisitorKey.Should().Be(Somebody);
        asked.Limit.Should().Be(SiteLiveTrailQuery.MostSteps);
    }

    /// <summary>
    /// A visitor leaving is what happens to every visitor. Somebody who has fallen out of the
    /// stretch since their row was drawn is an empty trail rather than a failure, so a panel open on
    /// them can say they have gone.
    /// </summary>
    [Fact]
    public async Task Answers_A_Visitor_Who_Has_Gone_With_An_Empty_Trail()
    {
        Answering();

        var trail = await ReadTrail(Now);

        trail.Steps.Should().BeEmpty();
        trail.At.Should().Be(Now);
    }

    [Fact]
    public async Task Carries_A_Trail_Back_In_The_Order_It_Was_Read()
    {
        Answering();
        Trailing(
            new VisitStep(Now.AddMinutes(-4), "/", 200, 9_000, 80, null),
            new VisitStep(Now.AddMinutes(-1), "/pricing", 200, null, null, null));

        var trail = await ReadTrail(Now);

        trail.Steps.Select(step => step.Path).Should().Equal("/", "/pricing");
    }

    [Fact]
    public async Task Refuses_To_Read_A_Trail_For_Nobody()
    {
        Answering();

        var act = () => Reader().ReadTrailAsync(Scope(), "  ", Now, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private Task<LiveTraffic> Read() =>
        Reader().ReadAsync(Scope(), "example.com", TestContext.Current.CancellationToken);

    private Task<LiveTrail> ReadTrail(DateTimeOffset at) =>
        Reader().ReadTrailAsync(Scope(), Somebody, at, TestContext.Current.CancellationToken);

    private LiveTrafficReader Reader() =>
        new(
            _telemetry,
            TrafficClassifier.Current(),
            _clock,
            Options.Create(new ClassificationOptions()));

    private void Trailing(params VisitStep[] steps) =>
        _telemetry
            .GetSiteLiveTrailAsync(
                Arg.Any<TenantScope>(),
                Arg.Any<SiteLiveTrailQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(steps);

    private void Answering(params LiveVisitor[] visitors) => Answering(visitors.Length, visitors);

    private void Answering(int seen, params LiveVisitor[] visitors)
    {
        _telemetry
            .GetSiteLiveVisitorsAsync(
                Arg.Any<TenantScope>(),
                Arg.Any<SiteLiveVisitorsQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(new LiveVisitors(seen, [.. visitors]));

        _telemetry
            .GetSiteLiveActivityAsync(
                Arg.Any<TenantScope>(),
                Arg.Any<SiteLiveActivityQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<LiveMinute>());

        _telemetry
            .GetSiteLivePagesAsync(
                Arg.Any<TenantScope>(),
                Arg.Any<SiteLivePagesQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<LivePage>());

        Trailing();
    }

    private static LiveVisitor Visitor(string key, SessionEvidence evidence) => new(
        key,
        evidence,
        new VisitContext("", SourceChannel.Direct, "GB", "London", "Example", DeviceClass.Desktop, "Chrome", "Windows"),
        evidence.Requests.IsEmpty ? "/" : evidence.Requests[^1].Path,
        evidence.ConfirmedOperator ?? string.Empty,
        evidence.AutonomousSystem);

    private static TenantScope Scope() =>
        new(SiteId, OrganizationId, SiteRole.Viewer, "Europe/London");
}
