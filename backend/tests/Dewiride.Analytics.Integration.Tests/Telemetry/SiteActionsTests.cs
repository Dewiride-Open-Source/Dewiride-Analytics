using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Telemetry;

/// <summary>
/// Proves what a list of operated controls counts, and what it refuses to count.
/// </summary>
/// <remarks>
/// A press is the one measurement in this product with exactly one witness. Nothing between the
/// visitor and the site can see one, so there is no second sighting to fold together and no
/// reconciliation to get wrong — which makes the interesting properties here about what is left
/// out rather than about what is added up.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class SiteActionsTests(AnalyticsStackFixture stack)
{
    private static readonly DateTimeOffset Midnight = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Controls_Are_Listed_Most_Pressed_First()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Pressed(siteId, Midnight.AddHours(1), "visitor-a", ControlKind.Button, "Subscribe"),
            Pressed(siteId, Midnight.AddHours(2), "visitor-b", ControlKind.Button, "Subscribe"),
            Pressed(siteId, Midnight.AddHours(3), "visitor-c", ControlKind.Link, "Pricing"));

        var controls = await Presses(siteId);

        controls.Controls.Select(row => row.Name).Should().Equal("Subscribe", "Pricing");
        controls.Controls.Select(row => row.Presses).Should().Equal(2, 1);
    }

    [Fact]
    public async Task A_Controls_Kind_Is_Reported_Beside_Its_Name()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Pressed(siteId, Midnight.AddHours(1), "visitor-a", ControlKind.Field, "Search"));

        var controls = await Presses(siteId);

        controls.Controls.Single().Control.Should().Be(ControlKind.Field);
    }

    /// <summary>
    /// Two controls that read the same but are different things stay two rows. A link and a button
    /// both saying "Read more" do different jobs, and merging them would report one figure for two
    /// questions.
    /// </summary>
    [Fact]
    public async Task Two_Controls_Sharing_A_Name_Stay_Apart_When_They_Are_Different_Things()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Pressed(siteId, Midnight.AddHours(1), "visitor-a", ControlKind.Link, "Read more"),
            Pressed(siteId, Midnight.AddHours(2), "visitor-b", ControlKind.Button, "Read more"));

        var controls = await Presses(siteId);

        controls.Controls.Should().HaveCount(2);
        controls.Controls.Select(row => row.Control)
            .Should()
            .BeEquivalentTo([ControlKind.Link, ControlKind.Button]);
    }

    /// <summary>
    /// The same control pressed on every page of a site is one row. Which page it was pressed on
    /// is a different question with its own answer.
    /// </summary>
    [Fact]
    public async Task One_Control_Pressed_Across_Many_Pages_Is_One_Row()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Pressed(siteId, Midnight.AddHours(1), "visitor-a", ControlKind.Link, "Home", page: "/"),
            Pressed(siteId, Midnight.AddHours(2), "visitor-b", ControlKind.Link, "Home", page: "/pricing"));

        var controls = await Presses(siteId);

        controls.Controls.Should().ContainSingle();
        controls.Controls[0].Presses.Should().Be(2);
        controls.Controls[0].Visitors.Should().Be(2);
    }

    /// <summary>
    /// A site that gave a control no name still had somebody press it. Leaving those out would
    /// report a quieter page than the one people used, and the row is exactly the prompt a site
    /// needs to go and name the thing.
    /// </summary>
    [Fact]
    public async Task A_Control_With_No_Name_Is_A_Row_Rather_Than_An_Omission()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Pressed(siteId, Midnight.AddHours(1), "visitor-a", ControlKind.Unknown, name: null));

        var controls = await Presses(siteId);

        controls.Controls.Should().ContainSingle();
        controls.Controls[0].Name.Should().BeEmpty();
        controls.Controls[0].Control.Should().Be(ControlKind.Unknown);
    }

    /// <summary>
    /// Reading a page and pressing something on it are different reports, and the list of presses
    /// counts only the second.
    /// </summary>
    [Fact]
    public async Task Reading_A_Page_Is_Not_Pressing_Anything_On_It()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Read(siteId, Midnight.AddHours(1), "visitor-a", "/posts/hello"),
            Pressed(siteId, Midnight.AddHours(2), "visitor-a", ControlKind.Button, "Subscribe"));

        var controls = await Presses(siteId);

        controls.Controls.Should().ContainSingle();
        controls.TotalPresses.Should().Be(1);
    }

    /// <summary>
    /// Where a press led on the site is answered by the pages themselves, so a destination list
    /// that carried them would rank the site against itself and bury the places it sends people.
    /// </summary>
    [Fact]
    public async Task Only_Presses_That_Led_Off_The_Site_Are_Listed_As_Destinations()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            LedTo(siteId, Midnight.AddHours(1), "visitor-a", "github.com", TargetKind.External),
            LedTo(siteId, Midnight.AddHours(2), "visitor-b", "github.com", TargetKind.External),
            LedTo(siteId, Midnight.AddHours(3), "visitor-c", "/pricing", TargetKind.Internal),
            Pressed(siteId, Midnight.AddHours(4), "visitor-d", ControlKind.Button, "Subscribe"));

        var destinations = await Presses(siteId, ActionGrouping.Destination);

        destinations.Controls.Select(row => row.Name).Should().Equal("github.com");
        destinations.Controls[0].Presses.Should().Be(2);
    }

    /// <summary>
    /// An address to write to records only that it was used. The address itself names a person and
    /// is never kept, so there is nothing for a destination list to show.
    /// </summary>
    [Fact]
    public async Task An_Address_To_Write_To_Is_Not_A_Destination()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            LedTo(siteId, Midnight.AddHours(1), "visitor-a", target: null, TargetKind.Contact));

        var destinations = await Presses(siteId, ActionGrouping.Destination);

        destinations.Controls.Should().BeEmpty();
    }

    [Fact]
    public async Task Visitors_Are_Counted_Once_However_Often_They_Press()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Pressed(siteId, Midnight.AddHours(1), "visitor-a", ControlKind.Button, "Subscribe"),
            Pressed(siteId, Midnight.AddHours(2), "visitor-a", ControlKind.Button, "Subscribe"),
            Pressed(siteId, Midnight.AddHours(3), "visitor-a", ControlKind.Button, "Subscribe"));

        var controls = await Presses(siteId);

        controls.Controls[0].Presses.Should().Be(3);
        controls.Controls[0].Visitors.Should().Be(1);
    }

    /// <summary>
    /// The figures beside a slice describe the whole window, so a share and a bar mean the same
    /// thing wherever in the list a row appears.
    /// </summary>
    [Fact]
    public async Task Every_Slice_Carries_The_Figures_The_Whole_Window_Gives_It_Meaning_Against()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Pressed(siteId, Midnight.AddHours(1), "visitor-a", ControlKind.Button, "Busy"),
            Pressed(siteId, Midnight.AddHours(2), "visitor-b", ControlKind.Button, "Busy"),
            Pressed(siteId, Midnight.AddHours(3), "visitor-c", ControlKind.Button, "Busy"),
            Pressed(siteId, Midnight.AddHours(4), "visitor-d", ControlKind.Button, "Quiet"));

        var second = await Presses(siteId, limit: 1, offset: 1);

        second.Controls.Select(row => row.Name).Should().Equal("Quiet");
        second.TotalPresses.Should().Be(4);
        second.TotalControls.Should().Be(2);
        second.MostPresses.Should().Be(3);
    }

    [Fact]
    public async Task Successive_Slices_Neither_Repeat_A_Row_Nor_Skip_One()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Pressed(siteId, Midnight.AddHours(1), "visitor-a", ControlKind.Button, "Alike A"),
            Pressed(siteId, Midnight.AddHours(2), "visitor-b", ControlKind.Button, "Alike B"),
            Pressed(siteId, Midnight.AddHours(3), "visitor-c", ControlKind.Button, "Alike C"),
            Pressed(siteId, Midnight.AddHours(4), "visitor-d", ControlKind.Button, "Alike D"));

        var first = await Presses(siteId, limit: 2);
        var second = await Presses(siteId, limit: 2, offset: 2);

        first.Controls.Select(row => row.Name).Should().Equal("Alike A", "Alike B");
        second.Controls.Select(row => row.Name).Should().Equal("Alike C", "Alike D");
    }

    [Fact]
    public async Task A_Slice_Past_The_End_Of_The_List_Is_Simply_Empty()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Pressed(siteId, Midnight.AddHours(1), "visitor-a", ControlKind.Button, "Only"));

        var controls = await Presses(siteId, limit: 10, offset: 50);

        controls.Controls.Should().BeEmpty();
    }

    [Fact]
    public async Task Presses_Outside_The_Window_Are_Left_Out()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Pressed(siteId, Midnight.AddHours(1), "visitor-a", ControlKind.Button, "Inside"),
            Pressed(siteId, Midnight.AddDays(2), "visitor-a", ControlKind.Button, "Outside"));

        var controls = await Presses(siteId);

        controls.Controls.Select(row => row.Name).Should().Equal("Inside");
    }

    [Fact]
    public async Task Another_Sites_Presses_Are_Never_Counted()
    {
        var siteId = Guid.NewGuid();
        var neighbour = Guid.NewGuid();

        await WriteAsync(
            Pressed(siteId, Midnight.AddHours(1), "visitor-a", ControlKind.Button, "Mine"),
            Pressed(neighbour, Midnight.AddHours(1), "visitor-b", ControlKind.Button, "Theirs"));

        var controls = await Presses(siteId);

        controls.Controls.Select(row => row.Name).Should().Equal("Mine");
    }

    [Fact]
    public async Task A_Window_With_No_Presses_Answers_With_Nothing_And_A_Nought()
    {
        var controls = await Presses(Guid.NewGuid());

        controls.Controls.Should().BeEmpty();
        controls.TotalPresses.Should().Be(0);
        controls.TotalControls.Should().Be(0);
    }

    /// <summary>
    /// A control's name is written by whoever wrote the page, and a page may carry writing
    /// somebody else put there. It is grouped on and read back, never built into the statement.
    /// </summary>
    [Fact]
    public async Task A_Name_Written_To_Break_The_Statement_Is_Counted_Like_Any_Other()
    {
        var siteId = Guid.NewGuid();
        const string hostile = "'; DROP TABLE events; --";

        await WriteAsync(
            Pressed(siteId, Midnight.AddHours(1), "visitor-a", ControlKind.Button, hostile));

        var controls = await Presses(siteId);

        controls.Controls.Select(row => row.Name).Should().Equal(hostile);
    }

    private Task<SiteActions> Presses(
        Guid siteId,
        ActionGrouping grouping = ActionGrouping.Control,
        int limit = 10,
        int offset = 0) =>
        stack.Services.GetRequiredService<ITelemetryQueries>().GetSiteActionsAsync(
            Scope(siteId),
            new SiteActionsQuery(new TimeRange(Midnight, Midnight.AddDays(1)), grouping, limit, offset),
            Cancellation.Token);

    private Task WriteAsync(params RawEvent[] events) =>
        stack.Services.GetRequiredService<IEventSink>().WriteBatchAsync(events, Cancellation.Token);

    private static TenantScope Scope(Guid siteId) =>
        new(siteId, Guid.NewGuid(), SiteRole.Viewer, "Etc/UTC");

    private static RawEvent Pressed(
        Guid siteId,
        DateTimeOffset at,
        string visitorKey,
        ControlKind control,
        string? name,
        string page = "/posts/hello") =>
        new()
        {
            EventId = Guid.CreateVersion7(at),
            SiteId = siteId,
            Kind = EventKind.Action,
            Surface = IngestSurface.BrowserTracker,
            ServerTimestamp = at,
            VisitorKey = visitorKey,
            Host = "example.com",
            Path = page,
            ActionControl = control,
            ActionLabel = name,
        };

    private static RawEvent LedTo(
        Guid siteId,
        DateTimeOffset at,
        string visitorKey,
        string? target,
        TargetKind targetKind) =>
        Pressed(siteId, at, visitorKey, ControlKind.Link, "Somewhere") with
        {
            ActionTarget = target,
            ActionTargetKind = targetKind,
        };

    private static RawEvent Read(Guid siteId, DateTimeOffset at, string visitorKey, string page) =>
        Pressed(siteId, at, visitorKey, ControlKind.Unknown, null, page) with
        {
            Kind = EventKind.PageView,
        };
}
