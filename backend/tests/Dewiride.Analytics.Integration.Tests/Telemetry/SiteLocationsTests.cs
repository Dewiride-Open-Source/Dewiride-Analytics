using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Telemetry;

/// <summary>
/// Proves that where an audience was is counted once per person, whatever saw them.
/// </summary>
/// <remarks>
/// The list has two properties that only a real store can demonstrate. A visitor watched by both
/// halves of the measurement resolves to one place rather than to a place and to nowhere, because
/// the two halves see different addresses and one of them may see none. And the figures beside the
/// rows describe the whole window, so a share stays true on the fourth screenful of a long list.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class SiteLocationsTests(AnalyticsStackFixture stack)
{
    private static readonly DateTimeOffset Midnight = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Countries_Are_Listed_By_How_Many_People_Were_In_Them()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            InCountry(siteId, Midnight.AddHours(1), "visitor-a", "IN", "Pune"),
            InCountry(siteId, Midnight.AddHours(1), "visitor-b", "IN", "Mumbai"),
            InCountry(siteId, Midnight.AddHours(2), "visitor-c", "IN", "Pune"),
            InCountry(siteId, Midnight.AddHours(3), "visitor-d", "GB", "London"));

        var places = await PageOfPlaces(siteId);

        places.Places.Select(place => place.Place).Should().Equal("IN", "GB");
        places.Places.Select(place => place.Visitors).Should().Equal(3, 1);
    }

    /// <summary>
    /// A place is a fact about people. Ranked by pages instead, one busy reader in a small country
    /// would outrank a hundred readers in a large one on a list that claims to say where an
    /// audience is.
    /// </summary>
    [Fact]
    public async Task A_Country_With_More_People_Outranks_One_With_More_Pages()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            InCountry(siteId, Midnight.AddHours(1), "reader-1", "IN", "Pune"),
            InCountry(siteId, Midnight.AddHours(1), "reader-2", "IN", "Pune"),
            InCountry(siteId, Midnight.AddHours(1), "browser", "GB", "London", path: "/a"),
            InCountry(siteId, Midnight.AddHours(2), "browser", "GB", "London", path: "/b"),
            InCountry(siteId, Midnight.AddHours(3), "browser", "GB", "London", path: "/c"),
            InCountry(siteId, Midnight.AddHours(4), "browser", "GB", "London", path: "/d"));

        var places = await PageOfPlaces(siteId);

        places.Places[0].Place.Should().Be("IN");
        places.Places[0].Visitors.Should().Be(2);
        places.Places[1].Place.Should().Be("GB");
        places.Places[1].PageViews.Should().Be(4);
    }

    [Fact]
    public async Task Towns_Are_Listed_When_The_List_Is_Grouped_By_Town()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            InCountry(siteId, Midnight.AddHours(1), "visitor-a", "IN", "Pune"),
            InCountry(siteId, Midnight.AddHours(1), "visitor-b", "IN", "Pune"),
            InCountry(siteId, Midnight.AddHours(2), "visitor-c", "IN", "Mumbai"));

        var places = await PageOfPlaces(siteId, LocationGrouping.Town);

        places.Places.Select(place => place.Place).Should().Equal("Pune", "Mumbai");
        places.Places.Select(place => place.Visitors).Should().Equal(2, 1);
    }

    /// <summary>
    /// The row carries its country as well as its name, so a town is never confused with the
    /// identically-named town somewhere else — and the two are separate rows rather than one.
    /// </summary>
    [Fact]
    public async Task Two_Towns_Of_The_Same_Name_In_Different_Countries_Are_Two_Places()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            InCountry(siteId, Midnight.AddHours(1), "visitor-a", "GB", "Cambridge"),
            InCountry(siteId, Midnight.AddHours(2), "visitor-b", "US", "Cambridge"));

        var places = await PageOfPlaces(siteId, LocationGrouping.Town);

        places.Places.Should().HaveCount(2);
        places.Places.Select(place => place.CountryCode).Should().BeEquivalentTo("GB", "US");
        places.Places.Should().OnlyContain(place => place.Place == "Cambridge");
    }

    /// <summary>
    /// The property that only a real store proves. Each half of the measurement resolves the
    /// address it saw, and a report forwarded by a site's own server frequently carries no useful
    /// address at all — so a reader watched by both would otherwise be counted once in their own
    /// country and once nowhere.
    /// </summary>
    [Fact]
    public async Task A_Visitor_One_Half_Could_Not_Place_Is_Still_Placed_Once()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            InCountry(siteId, Midnight.AddHours(1), "visitor-a", "IN", "Pune", surface: IngestSurface.BrowserTracker),
            InCountry(siteId, Midnight.AddHours(1), "visitor-a", "", "", surface: IngestSurface.NextJsMiddleware));

        var places = await PageOfPlaces(siteId);

        places.Places.Should().ContainSingle();
        places.Places[0].Place.Should().Be("IN");
        places.Places[0].Visitors.Should().Be(1);
        places.Places[0].PageViews.Should().Be(1);
    }

    /// <summary>
    /// An installation behind a proxy that does not pass the visitor's address through resolves
    /// nothing at all. It has to be able to see that, so the unresolved group is a row on the list
    /// rather than an omission from it.
    /// </summary>
    [Fact]
    public async Task A_Place_That_Did_Not_Resolve_Is_A_Row_Rather_Than_A_Silence()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            InCountry(siteId, Midnight.AddHours(1), "visitor-a", "IN", "Pune"),
            InCountry(siteId, Midnight.AddHours(2), "visitor-b", "", ""),
            InCountry(siteId, Midnight.AddHours(3), "visitor-c", "", ""));

        var places = await PageOfPlaces(siteId);

        places.Places[0].Place.Should().BeEmpty();
        places.Places[0].Visitors.Should().Be(2);
        places.TotalPlaces.Should().Be(2);
    }

    /// <summary>
    /// The figures beside the rows describe every place the window held, so a share taken on the
    /// fourth screenful of a long list is a share of the site rather than of the screen.
    /// </summary>
    [Fact]
    public async Task The_Figures_Cover_Places_The_Slice_Was_Cut_Off_Before_Reaching()
    {
        var siteId = Guid.NewGuid();
        var events = Enumerable.Range(0, 12)
            .Select(rank => InCountry(
                siteId,
                Midnight.AddMinutes(rank),
                $"visitor-{rank:00}",
                Alphabetical(rank),
                $"town-{rank:00}"))
            .ToArray();

        await WriteAsync(events);

        var places = await PageOfPlaces(siteId, limit: 4);

        places.Places.Should().HaveCount(4);
        places.TotalPlaces.Should().Be(12);
        places.TotalVisitors.Should().Be(12);
        places.MostVisitors.Should().Be(1);
    }

    /// <summary>
    /// The whole list has to be reachable, and reachable exactly once. Without a total ordering
    /// two places with equal audiences could swap between one slice and the next, showing one
    /// twice and never showing the other.
    /// </summary>
    [Fact]
    public async Task Every_Place_Is_Reached_Exactly_Once_By_Walking_The_Slices()
    {
        var siteId = Guid.NewGuid();
        var events = Enumerable.Range(0, 21)
            .Select(rank => InCountry(
                siteId,
                Midnight.AddMinutes(rank),
                $"visitor-{rank:00}",
                Alphabetical(rank),
                $"town-{rank:00}"))
            .ToArray();

        await WriteAsync(events);

        var walked = new List<string>();

        for (var offset = 0; offset < 21; offset += 5)
        {
            var slice = await PageOfPlaces(siteId, limit: 5, offset: offset);
            walked.AddRange(slice.Places.Select(place => place.Place));
        }

        walked.Should().HaveCount(21);
        walked.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task A_Slice_Past_The_End_Of_The_List_Is_Simply_Empty()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(InCountry(siteId, Midnight.AddHours(1), "visitor-a", "IN", "Pune"));

        var places = await PageOfPlaces(siteId, offset: 50);

        places.Places.Should().BeEmpty();
        places.TotalVisitors.Should().Be(0);
    }

    /// <summary>
    /// Activity nobody could attribute to a visitor says nothing about who was where, so it takes
    /// no part in a list of who was where.
    /// </summary>
    [Fact]
    public async Task Activity_With_No_Visitor_Behind_It_Places_Nobody()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            InCountry(siteId, Midnight.AddHours(1), "visitor-a", "IN", "Pune"),
            InCountry(siteId, Midnight.AddHours(2), null, "GB", "London"));

        var places = await PageOfPlaces(siteId);

        places.Places.Select(place => place.Place).Should().Equal("IN");
        places.TotalVisitors.Should().Be(1);
    }

    [Fact]
    public async Task Places_Outside_The_Window_Are_Left_Out()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            InCountry(siteId, Midnight.AddHours(1), "visitor-a", "IN", "Pune"),
            InCountry(siteId, Midnight.AddDays(-1), "visitor-b", "GB", "London"),
            InCountry(siteId, Midnight.AddDays(2), "visitor-c", "US", "Austin"));

        var places = await PageOfPlaces(siteId);

        places.Places.Select(place => place.Place).Should().Equal("IN");
    }

    [Fact]
    public async Task Another_Sites_Places_Are_Never_Listed()
    {
        var siteId = Guid.NewGuid();
        var neighbour = Guid.NewGuid();

        await WriteAsync(
            InCountry(siteId, Midnight.AddHours(1), "visitor-a", "IN", "Pune"),
            InCountry(neighbour, Midnight.AddHours(1), "visitor-b", "GB", "London"));

        var places = await PageOfPlaces(siteId);

        places.Places.Select(place => place.Place).Should().Equal("IN");
    }

    [Fact]
    public async Task A_Window_With_No_Traffic_Answers_With_Nothing_And_A_Nought()
    {
        var places = await PageOfPlaces(Guid.NewGuid());

        places.Places.Should().BeEmpty();
        places.TotalVisitors.Should().Be(0);
        places.TotalPlaces.Should().Be(0);
        places.MostVisitors.Should().Be(0);
    }

    /// <summary>
    /// A town name arrives from a file somebody else publishes and is grouped on, never built into
    /// the statement. It is treated like any other value for the same reason a requested path is.
    /// </summary>
    [Fact]
    public async Task A_Town_Named_To_Break_The_Statement_Is_Counted_Like_Any_Other()
    {
        var siteId = Guid.NewGuid();
        const string hostile = "'); DROP TABLE events; --";

        await WriteAsync(InCountry(siteId, Midnight.AddHours(1), "visitor-a", "IN", hostile));

        var places = await PageOfPlaces(siteId, LocationGrouping.Town);

        places.Places.Select(place => place.Place).Should().Equal(hostile);
    }

    /// <summary>Turns a rank into a distinct two-letter code, so ranks never collide.</summary>
    private static string Alphabetical(int rank) =>
        string.Concat((char)('A' + (rank / 26)), (char)('A' + (rank % 26)));

    private Task<SiteLocations> PageOfPlaces(
        Guid siteId,
        LocationGrouping grouping = LocationGrouping.Country,
        int limit = 10,
        int offset = 0) =>
        stack.Services.GetRequiredService<ITelemetryQueries>().GetSiteLocationsAsync(
            Scope(siteId),
            new SiteLocationsQuery(new TimeRange(Midnight, Midnight.AddDays(1)), grouping, limit, offset),
            Cancellation.Token);

    private Task WriteAsync(params RawEvent[] events) =>
        stack.Services.GetRequiredService<IEventSink>().WriteBatchAsync(events, Cancellation.Token);

    private static TenantScope Scope(Guid siteId) =>
        new(siteId, Guid.NewGuid(), SiteRole.Viewer, "Etc/UTC");

    private static RawEvent InCountry(
        Guid siteId,
        DateTimeOffset at,
        string? visitorKey,
        string countryCode,
        string city,
        string path = "/",
        IngestSurface surface = IngestSurface.BrowserTracker) =>
        new()
        {
            EventId = Guid.CreateVersion7(at),
            SiteId = siteId,
            Kind = EventKind.PageView,
            Surface = surface,
            ServerTimestamp = at,
            VisitorKey = visitorKey,
            Host = "example.com",
            Path = path,
            CountryCode = countryCode,
            City = city,
        };
}
