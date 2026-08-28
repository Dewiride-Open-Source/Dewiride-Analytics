using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Dashboard;

/// <summary>
/// Proves the two screens' questions are answered, and answered only to somebody entitled to ask.
/// </summary>
/// <remarks>
/// What comes back here is what the dashboard renders, so the spellings matter as much as the
/// numbers: every category, band and reason is looked up in the message catalogue by the name in
/// this answer, and a name that changed would leave a screen showing nothing at all.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class TrafficReadTests(AnalyticsStackFixture stack)
{
    private const string Password = Passwords.Acceptable;

    private const string Scanner = "python-requests/2.32.3";

    [Fact]
    public async Task A_Member_Sees_What_Generated_Their_Traffic()
    {
        var site = await JudgedSiteAsync();
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/traffic");

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var traffic = await response.Content.ReadFromJsonAsync<TrafficResponse>(Cancellation.Token);

            traffic.Should().NotBeNull();
            traffic.Sessions.Should().Be(1);
            traffic.PageViews.Should().Be(3);
            traffic.Groups.Should().ContainSingle();
            traffic.Groups[0].Category.Should().Be("security-scanner");
            traffic.Groups[0].Strength.Should().Be("strong");
        }
    }

    [Fact]
    public async Task A_Member_Sees_Why_Each_Visit_Was_Judged_The_Way_It_Was()
    {
        var site = await JudgedSiteAsync();
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/visits");

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var visits = await response.Content.ReadFromJsonAsync<VisitsResponse>(Cancellation.Token);

            visits.Should().NotBeNull();
            visits.Visits.Should().ContainSingle();

            var visit = visits.Visits[0];

            visit.Category.Should().Be("security-scanner");
            visit.PageCount.Should().Be(3);
            // Which ruleset is in force is stated once, in the test that exists to make moving it a
            // deliberate act. What matters here is that a visit says which one judged it.
            visit.Ruleset.Should().Be(RulesetVersion.Current.ToString());
            visit.Surfaces.Should().Equal("cloudflare-worker");
            visit.Supporting.Should().Contain(reason => reason.Code == "probing.sensitive_paths");
            visit.Supporting.Should().OnlyContain(reason => reason.Direction == "toward-automation");
            visit.Supporting.Single(reason => reason.Code == "probing.sensitive_paths")
                .Values["attemptCount"].Should().Be("3");
        }
    }

    /// <summary>
    /// The list is a slice, so the count beside it has to describe the period rather than the slice.
    /// A list reporting its own length would tell somebody with a hundred visits that they had two,
    /// and would stop without admitting there was anything behind it.
    /// </summary>
    [Fact]
    public async Task Every_Visit_A_Period_Holds_Can_Be_Reached_A_Slice_At_A_Time()
    {
        var site = await JudgedSiteAsync(visitors: 5);
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var first = await ReadVisitsAsync(browser, site.Id, "limit=2&offset=0");
            var second = await ReadVisitsAsync(browser, site.Id, "limit=2&offset=2");
            var last = await ReadVisitsAsync(browser, site.Id, "limit=2&offset=4");

            first.TotalVisits.Should().Be(5);
            second.TotalVisits.Should().Be(5);
            last.TotalVisits.Should().Be(5);

            first.Visits.Should().HaveCount(2);
            second.Visits.Should().HaveCount(2);
            last.Visits.Should().ContainSingle();

            // The ordering is total, so walking the slices reaches every visit exactly once. A tie
            // broken arbitrarily would show one of them twice and leave another unreachable.
            IEnumerable<string> walked =
            [
                .. first.Visits.Select(visit => visit.Id),
                .. second.Visits.Select(visit => visit.Id),
                .. last.Visits.Select(visit => visit.Id),
            ];

            walked.Should().OnlyHaveUniqueItems().And.HaveCount(5);
            first.Visits.Should().BeInDescendingOrder(visit => visit.StartedAt);
        }
    }

    /// <summary>
    /// Newest first, so the list opens on what just happened rather than on the oldest thing the
    /// period still holds.
    /// </summary>
    [Fact]
    public async Task The_Visit_List_Opens_On_The_Most_Recent_Visit()
    {
        var site = await JudgedSiteAsync(visitors: 3);
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var everything = await ReadVisitsAsync(browser, site.Id, "limit=10&offset=0");
            var opening = await ReadVisitsAsync(browser, site.Id, "limit=1&offset=0");

            opening.Visits.Should().ContainSingle();
            opening.Visits[0].StartedAt.Should().Be(everything.Visits.Max(visit => visit.StartedAt));
        }
    }

    /// <summary>
    /// Asked past the end, the answer is a slice with nothing in it rather than a refusal: reaching
    /// the end of a list is an ordinary thing to have done, not a mistake.
    /// </summary>
    [Fact]
    public async Task Asking_Past_The_End_Of_The_Visit_List_Answers_With_An_Empty_Slice()
    {
        var site = await JudgedSiteAsync(visitors: 2);
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var past = await ReadVisitsAsync(browser, site.Id, "limit=10&offset=500");

            past.Visits.Should().BeEmpty();
        }
    }

    /// <summary>
    /// The same answer as a site that was never created, so neither endpoint can be used to test
    /// which identifiers on an install are real.
    /// </summary>
    [Theory]
    [InlineData("traffic")]
    [InlineData("visits")]
    [InlineData("traffic/series?granularity=day")]
    [InlineData("visits/facets")]
    public async Task A_Site_Somebody_Has_No_Role_On_Is_Answered_As_Though_It_Did_Not_Exist(string screen)
    {
        var mine = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var theirs = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(mine.Id, SiteRole.Owner);

        using (browser)
        {
            var otherSite = await browser.GetAsync($"/api/sites/{theirs.Id}/{screen}");
            var noSite = await browser.GetAsync($"/api/sites/{Guid.NewGuid()}/{screen}");

            otherSite.StatusCode.Should().Be(HttpStatusCode.NotFound);
            noSite.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [Theory]
    [InlineData("traffic")]
    [InlineData("visits")]
    [InlineData("traffic/series?granularity=day")]
    [InlineData("visits/facets")]
    public async Task Nobody_Signed_In_Is_Refused(string screen)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        using var browser = await Browser.OpenAsync(stack);

        var response = await browser.GetAsync($"/api/sites/{site.Id}/{screen}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("visits?limit=0")]
    [InlineData("visits?limit=501")]
    [InlineData("visits?offset=-1")]
    [InlineData("visits?from=2024-01-02T00:00:00Z&to=2024-01-01T00:00:00Z")]
    [InlineData("visits?category=whatever")]
    [InlineData("visits?category=security-scanner&category=whatever")]
    [InlineData("visits?strength=certain")]
    [InlineData("visits?minPages=-1")]
    [InlineData("visits?device=hovercraft")]
    [InlineData("visits?sourceKind=telepathy")]
    [InlineData("visits/facets?from=2024-01-02T00:00:00Z&to=2024-01-01T00:00:00Z")]
    [InlineData("traffic?from=2020-01-01T00:00:00Z&to=2024-01-01T00:00:00Z")]
    [InlineData("traffic/series")]
    [InlineData("traffic/series?granularity=fortnight")]
    [InlineData("traffic/series?granularity=hour&from=2024-01-01T00:00:00Z&to=2024-03-01T00:00:00Z")]
    public async Task A_Question_That_Cannot_Be_Answered_As_Asked_Is_Refused(string query)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/{query}");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task A_Member_Sees_What_Generated_Their_Traffic_Bucket_By_Bucket()
    {
        var site = await JudgedSiteAsync(visitors: 3);
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var series = await ReadTrafficSeriesAsync(browser, site.Id, "granularity=day");

            series.Granularity.Should().Be("day");
            series.Buckets.Should().NotBeEmpty();
            series.Groups.Should().ContainSingle();
            series.Groups[0].Category.Should().Be("security-scanner");
            series.Groups[0].Sessions.Sum().Should().Be(3);
            series.Groups[0].PageViews.Sum().Should().Be(9);
        }
    }

    /// <summary>
    /// Every category's counts are as long as the bucket list and in its order, which is what lets
    /// a reader index one against the other rather than join them. A ragged answer would line every
    /// count up against the wrong day without anything failing.
    /// </summary>
    [Theory]
    [InlineData("day")]
    [InlineData("hour")]
    public async Task Every_Category_Reports_One_Count_Per_Bucket(string granularity)
    {
        var site = await JudgedSiteAsync(visitors: 2);
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var series = await ReadTrafficSeriesAsync(browser, site.Id, $"granularity={granularity}");

            series.Groups.Should().NotBeEmpty();
            series.Groups.Should().OnlyContain(group =>
                group.Sessions.Count == series.Buckets.Count
                && group.PageViews.Count == series.Buckets.Count);
        }
    }

    /// <summary>
    /// The two answers about one window are the same arithmetic cut two ways. A series whose
    /// buckets did not add up to the breakdown beside it would put one figure on a screen above
    /// another that contradicts it, with nothing to say which of them was right.
    /// </summary>
    [Theory]
    [InlineData("day")]
    [InlineData("hour")]
    public async Task The_Series_Adds_Up_To_The_Breakdown_Of_The_Same_Window(string granularity)
    {
        var site = await JudgedSiteAsync(visitors: 3);
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);
        var clock = stack.Services.GetRequiredService<TimeProvider>();
        var window = $"from={Written(clock.GetUtcNow().AddDays(-3))}&to={Written(clock.GetUtcNow())}";

        using (browser)
        {
            var breakdown = await browser.GetAsync($"/api/sites/{site.Id}/traffic?{window}");
            breakdown.StatusCode.Should().Be(HttpStatusCode.OK);

            var totals = await breakdown.Content.ReadFromJsonAsync<TrafficResponse>(Cancellation.Token);
            var series = await ReadTrafficSeriesAsync(
                browser,
                site.Id,
                $"granularity={granularity}&{window}");

            totals.Should().NotBeNull();
            totals.Sessions.Should().Be(3);
            series.Groups.Sum(group => group.Sessions.Sum()).Should().Be(totals.Sessions);
            series.Groups.Sum(group => group.PageViews.Sum()).Should().Be(totals.PageViews);
        }
    }

    /// <summary>
    /// A visit belongs to the bucket it began in, so a window opening after it began does not hold
    /// it at all — the same rule every other visit-shaped answer counts by.
    /// </summary>
    /// <remarks>
    /// It is also what catches a window reporting a category it never held. Asked for a stretch
    /// with nothing in it the store still answers with a filled run, under whichever name its own
    /// enumeration begins with, and a screen would draw a band and a legend entry for traffic that
    /// was never there.
    /// </remarks>
    [Fact]
    public async Task A_Visit_That_Began_Before_The_Window_Is_Not_In_It()
    {
        var site = await JudgedSiteAsync();
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);
        var clock = stack.Services.GetRequiredService<TimeProvider>();
        var recently = $"from={Written(clock.GetUtcNow().AddHours(-2))}&to={Written(clock.GetUtcNow())}";

        using (browser)
        {
            var series = await ReadTrafficSeriesAsync(browser, site.Id, $"granularity=hour&{recently}");

            series.Groups.Should().BeEmpty();
            series.Buckets.Should().NotBeEmpty();
        }
    }

    /// <summary>
    /// Judging happens once a visit has ended, so the newest part of any window is still filling in.
    /// The answer says where that begins rather than leaving a reader to read a line falling away at
    /// its end as traffic that stopped.
    /// </summary>
    [Fact]
    public async Task The_Series_Says_How_Far_Judging_Has_Got()
    {
        var site = await JudgedSiteAsync();
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var series = await ReadTrafficSeriesAsync(browser, site.Id, "granularity=day");

            series.CompleteTo.Should().BeAfter(series.From).And.BeBefore(series.To);
        }
    }

    /// <summary>
    /// Days are cut where the website is. A site reporting in Kolkata begins its days at half past
    /// six the evening before in UTC, and a reader anywhere sees the day the traffic was counted in
    /// rather than the one their own clock happened to be on.
    /// </summary>
    [Fact]
    public async Task Days_Are_Cut_Where_The_Website_Is()
    {
        var site = await JudgedSiteAsync(timeZoneId: "Asia/Kolkata");
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var series = await ReadTrafficSeriesAsync(browser, site.Id, "granularity=day");

            series.Buckets.Should().NotBeEmpty();
            series.Buckets.Should().OnlyContain(bucket =>
                bucket.UtcDateTime.TimeOfDay == new TimeSpan(18, 30, 0));
        }
    }

    /// <summary>
    /// Checked before the site is, so a question outside the vocabulary is refused the same way
    /// whether or not the site exists. The other order would turn a narrowing nobody can answer
    /// into a way of finding out which identifiers on an install are real.
    /// </summary>
    [Fact]
    public async Task A_Question_Outside_The_Vocabulary_Is_Refused_Before_The_Site_Is_Looked_Up()
    {
        var mine = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(mine.Id, SiteRole.Owner);

        using (browser)
        {
            var nowhere = await browser.GetAsync($"/api/sites/{Guid.NewGuid()}/visits?device=hovercraft");

            nowhere.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    /// <summary>
    /// Narrowing the list is what makes it usable on a site whose traffic is mostly machinery: the
    /// question people actually have is "show me the ones that were people", and it has to be
    /// answerable without reading every visit to find them.
    /// </summary>
    /// <param name="narrowing">What the reader narrowed to.</param>
    /// <param name="expected">How many of the site's two visits should come back.</param>
    [Theory]
    [InlineData("category=security-scanner", 2)]
    [InlineData("category=likely-human", 0)]
    [InlineData("category=likely-human&category=security-scanner", 2)]
    [InlineData("minPages=3", 2)]
    [InlineData("minPages=4", 0)]
    [InlineData("strength=weak", 2)]
    [InlineData("strength=verified", 0)]
    [InlineData("category=security-scanner&strength=strong&minPages=2", 2)]
    public async Task A_Member_Can_Narrow_The_List_To_The_Visits_They_Came_For(string narrowing, int expected)
    {
        var site = await JudgedSiteAsync(visitors: 2);
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var visits = await ReadVisitsAsync(browser, site.Id, narrowing);

            visits.Visits.Should().HaveCount(expected);

            // The figure a list counts itself against has to be the narrowed list, or somebody
            // shown three of two hundred would be told there were a hundred and ninety-seven more.
            visits.TotalVisits.Should().Be(expected);
        }
    }

    /// <summary>
    /// A narrowed list still counts the whole of what it was narrowed to rather than the screenful
    /// returned, which is what lets it say how far through somebody has read.
    /// </summary>
    [Fact]
    public async Task A_Narrowed_List_Counts_Every_Visit_It_Was_Narrowed_To()
    {
        var site = await JudgedSiteAsync(visitors: 3);
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var visits = await ReadVisitsAsync(browser, site.Id, "category=security-scanner&limit=1");

            visits.Visits.Should().ContainSingle();
            visits.TotalVisits.Should().Be(3);
        }
    }

    /// <summary>
    /// A site nothing has visited yet answers with an empty breakdown rather than a refusal, so
    /// the screen can say "nothing yet" instead of "something went wrong".
    /// </summary>
    [Fact]
    public async Task A_Site_With_No_Judged_Traffic_Answers_With_Nothing_Rather_Than_A_Failure()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var traffic = await browser.GetAsync($"/api/sites/{site.Id}/traffic");
            var visits = await browser.GetAsync($"/api/sites/{site.Id}/visits");

            var groups = await traffic.Content.ReadFromJsonAsync<TrafficResponse>(Cancellation.Token);
            var listed = await visits.Content.ReadFromJsonAsync<VisitsResponse>(Cancellation.Token);

            groups.Should().NotBeNull();
            groups.Groups.Should().BeEmpty();
            groups.Sessions.Should().Be(0);
            listed.Should().NotBeNull();
            listed.Visits.Should().BeEmpty();
        }
    }

    /// <summary>
    /// A bound on the question rather than on the answer. Each named value is one more comparison
    /// against every row the store rebuilt, and nobody picks two dozen towns off a list on purpose.
    /// </summary>
    [Fact]
    public async Task Narrowing_To_More_Values_Than_Anyone_Means_Is_Refused()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var asking = string.Join('&', Enumerable.Range(0, 26).Select(each => $"town=town-{each}"));

            var response = await browser.GetAsync($"/api/sites/{site.Id}/visits?{asking}");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    /// <summary>
    /// The nine things a reader recognises a visit by are offered as what the period actually held,
    /// spelled the way the rest of the product spells them: a kind of device reads as the word the
    /// dashboard looks up in its catalogue rather than as whatever the engine calls it internally.
    /// </summary>
    [Fact]
    public async Task A_Member_Is_Offered_What_Their_Period_Actually_Held()
    {
        var site = await DescribedTraffic.ADescribedVisitAsync(stack);
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var offered = await ReadFacetsAsync(browser, site.Id);

            offered.Devices.Should().ContainSingle().Which.Value.Should().Be("phone");
            offered.SourceKinds.Should().ContainSingle().Which.Value.Should().Be("search");
            offered.Browsers.Should().ContainSingle().Which.Value.Should().Be(DescribedTraffic.Browser);
            offered.Systems.Should().ContainSingle().Which.Value.Should().Be(DescribedTraffic.SystemName);
            offered.Countries.Should().ContainSingle().Which.Value.Should().Be(DescribedTraffic.Country);
            offered.Towns.Should().ContainSingle().Which.Value.Should().Be(DescribedTraffic.Town);
            offered.Networks.Should().ContainSingle().Which.Value.Should().Be(DescribedTraffic.Network);
            offered.Sources.Should().ContainSingle().Which.Value.Should().Be(DescribedTraffic.Source);
            offered.EntryPages.Should().ContainSingle().Which.Value.Should().Be(DescribedTraffic.EntryPage);
            offered.EntryPages[0].Visits.Should().Be(1);
        }
    }

    /// <summary>
    /// The property the whole arrangement rests on, asked the way the dashboard will ask it. Every
    /// value offered, written back as an address, finds the visit it was taken from — alone and all
    /// nine at once. It is also what proves the two closed vocabularies survive the round trip: a
    /// device offered as <c>phone</c> but narrowed by as <c>Phone</c> would refuse the question, and
    /// one compared against the engine's own spelling would answer with an empty list and no error.
    /// </summary>
    [Fact]
    public async Task Every_Value_Offered_Narrows_The_List_To_The_Visit_It_Came_From()
    {
        var site = await DescribedTraffic.ADescribedVisitAsync(stack);
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var offered = await ReadFacetsAsync(browser, site.Id);

            string[] narrowings =
            [
                Asking("device", offered.Devices),
                Asking("sourceKind", offered.SourceKinds),
                Asking("browser", offered.Browsers),
                Asking("system", offered.Systems),
                Asking("country", offered.Countries),
                Asking("town", offered.Towns),
                Asking("network", offered.Networks),
                Asking("source", offered.Sources),
                Asking("entryPage", offered.EntryPages),
            ];

            narrowings.Should().OnlyContain(narrowing => narrowing.Length > 0);

            foreach (var narrowing in narrowings)
            {
                var narrowed = await ReadVisitsAsync(browser, site.Id, narrowing);

                narrowed.TotalVisits.Should().Be(1, "{0} was offered", narrowing);
            }

            var together = await ReadVisitsAsync(browser, site.Id, string.Join('&', narrowings));

            together.TotalVisits.Should().Be(1);
        }
    }

    /// <summary>
    /// Each detail on its own excludes the visits that did not hold it — which is the half the
    /// round trip above cannot see, since a narrowing that never reached the store at all would
    /// leave the list unnarrowed and hand the visit straight back.
    /// </summary>
    /// <param name="narrowing">A detail the described visit did not hold.</param>
    [Theory]
    [InlineData("device=desktop")]
    [InlineData("sourceKind=social")]
    [InlineData("browser=Chrome")]
    [InlineData("system=Windows")]
    [InlineData("country=FR")]
    [InlineData("town=Lyon")]
    [InlineData("network=Vodafone")]
    [InlineData("source=Bing")]
    [InlineData("entryPage=/nowhere")]
    public async Task A_Detail_The_Visit_Did_Not_Have_Finds_Nothing(string narrowing)
    {
        var site = await DescribedTraffic.ADescribedVisitAsync(stack);
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var everything = await ReadVisitsAsync(browser, site.Id, "limit=10");
            var visits = await ReadVisitsAsync(browser, site.Id, narrowing);

            everything.TotalVisits.Should().Be(1);
            visits.Visits.Should().BeEmpty();
            visits.TotalVisits.Should().Be(0);
        }
    }

    /// <summary>
    /// Asking to see the visits nothing was established about is a different question from asking
    /// to see all of them, so an empty value has to survive the address it was written in rather
    /// than being read as though it had been left out.
    /// </summary>
    [Fact]
    public async Task Asking_For_What_Nothing_Was_Established_About_Is_Not_Asking_For_Everything()
    {
        var site = await DescribedTraffic.ADescribedVisitAsync(stack);
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var everything = await ReadVisitsAsync(browser, site.Id, "limit=10");
            var unplaced = await ReadVisitsAsync(browser, site.Id, "country=");

            everything.TotalVisits.Should().Be(1);
            unplaced.TotalVisits.Should().Be(0);
        }
    }

    /// <summary>
    /// A site whose traffic has been judged.
    /// </summary>
    /// <param name="visitors">
    /// How many separate visitors to seed. Sessions are cut per visitor, so this is how many judged
    /// visits the site ends up with — which is what a list read a slice at a time needs more than
    /// one of.
    /// </param>
    /// <param name="timeZoneId">The zone the site reports in, which its days are cut in.</param>
    /// <returns>The site.</returns>
    private async Task<Site> JudgedSiteAsync(int visitors = 1, string timeZoneId = "Etc/UTC")
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: timeZoneId);
        var clock = stack.Services.GetRequiredService<TimeProvider>();
        var at = clock.GetUtcNow().AddDays(-1);

        // Spaced an hour apart so the visits are in a definite order rather than one the store is
        // free to choose, which is what makes walking through them meaningful to assert.
        var probes = Enumerable.Range(0, visitors)
            .SelectMany(visitor =>
            {
                var began = at.AddHours(visitor);

                return new[]
                {
                    Probe(site.Id, began, "/.env", visitor),
                    Probe(site.Id, began.AddSeconds(1), "/.git/config", visitor),
                    Probe(site.Id, began.AddSeconds(2), "/wp-login.php", visitor),
                };
            });

        await stack.Services.GetRequiredService<IEventSink>()
            .WriteBatchAsync([.. probes], Cancellation.Token);

        await using var scope = stack.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SessionClassifier>()
            .CatchUpAsync(site.Id, site.CreatedAt.AddDays(-2), Cancellation.Token);

        return site;
    }

    private static RawEvent Probe(Guid siteId, DateTimeOffset at, string path, int visitor = 0) => new()
    {
        EventId = Guid.CreateVersion7(at),
        SiteId = siteId,
        Kind = EventKind.PageView,
        Surface = IngestSurface.CloudflareWorker,
        ServerTimestamp = at,
        VisitorKey = $"intruder-{visitor}-{siteId:n}",
        Host = "example.com",
        Path = path,
        UserAgent = Scanner,
        StatusCode = 404,
    };

    private static async Task<VisitsResponse> ReadVisitsAsync(Browser browser, Guid siteId, string slice)
    {
        var response = await browser.GetAsync($"/api/sites/{siteId}/visits?{slice}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var visits = await response.Content.ReadFromJsonAsync<VisitsResponse>(Cancellation.Token);

        visits.Should().NotBeNull();

        return visits;
    }

    private static async Task<TrafficSeriesResponse> ReadTrafficSeriesAsync(
        Browser browser,
        Guid siteId,
        string asked)
    {
        var response = await browser.GetAsync($"/api/sites/{siteId}/traffic/series?{asked}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var series = await response.Content.ReadFromJsonAsync<TrafficSeriesResponse>(Cancellation.Token);

        series.Should().NotBeNull();

        return series;
    }

    /// <summary>An instant written the way a window in an address carries it.</summary>
    /// <param name="at">The moment.</param>
    /// <returns>The moment, ready to be put in a query string.</returns>
    private static string Written(DateTimeOffset at) =>
        Uri.EscapeDataString(at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

    private static async Task<VisitFacetsResponse> ReadFacetsAsync(Browser browser, Guid siteId)
    {
        var response = await browser.GetAsync($"/api/sites/{siteId}/visits/facets");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var offered = await response.Content.ReadFromJsonAsync<VisitFacetsResponse>(Cancellation.Token);

        offered.Should().NotBeNull();

        return offered;
    }

    /// <summary>
    /// Writes one detail's offered values as the address a reader narrowing by them would arrive at.
    /// </summary>
    /// <param name="key">What the detail is called in an address.</param>
    /// <param name="offered">The values the answer offered.</param>
    /// <returns>The query string that narrows to all of them.</returns>
    private static string Asking(string key, IEnumerable<VisitDetailRow> offered) =>
        string.Join('&', offered.Select(row => $"{key}={Uri.EscapeDataString(row.Value)}"));

    private async Task<Browser> SignedInAsync(Guid siteId, SiteRole role)
    {
        var address = $"reader-{Guid.NewGuid():n}@example.com";
        var (_, user) = await ControlPlaneSeed.AddAccountAsync(stack, address, Password);
        await ControlPlaneSeed.GrantAsync(stack, siteId, user.Id, role);

        var browser = await Browser.OpenAsync(stack);
        var response = await browser.PostAsync(
            "/api/session",
            new SignInRequest { EmailAddress = address, Password = Password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await browser.DescribeAsync();

        return browser;
    }

    private static string Domain() => $"traffic-{Guid.NewGuid():n}.example";
}
