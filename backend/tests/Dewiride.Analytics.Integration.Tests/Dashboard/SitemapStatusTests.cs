using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Application.Posts;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Posts;
using Dewiride.Analytics.Integration.Tests.Fixtures;

namespace Dewiride.Analytics.Integration.Tests.Dashboard;

/// <summary>
/// Proves what the settings panel is told about how reading a website's sitemap is going, and who
/// it tells.
/// </summary>
/// <remarks>
/// Nothing here has read anything. The rows a reading leaves behind are written directly, because
/// what is being proved is the sentence those rows turn into: which state and which problem reach
/// the dashboard, and which of a website's pages count as its posts.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class SitemapStatusTests(AnalyticsStackFixture stack)
{
    [Fact]
    public async Task A_Website_Without_A_Sitemap_Says_None()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedIn.AsAsync(stack, SiteRole.Owner, site.Id);

        using (browser)
        {
            var status = await StatusOfAsync(browser, site.Id);

            status.Should().Be(new SitemapStatusResponse(null, "none", null, null, null, 0, 0, 0, 0));
        }
    }

    /// <summary>
    /// Nothing has read it yet, and there is no row saying so: the row is the reader's to make. The
    /// panel still has to say something true about the address somebody has just named.
    /// </summary>
    [Fact]
    public async Task A_New_Sitemap_Is_Waiting()
    {
        var (site, address) = await WebsiteWithASitemapAsync();
        var browser = await SignedIn.AsAsync(stack, SiteRole.Owner, site.Id);

        using (browser)
        {
            var status = await StatusOfAsync(browser, site.Id);

            status.Should().Be(new SitemapStatusResponse(address, "waiting", null, null, null, 0, 0, 0, 0));
        }
    }

    /// <summary>
    /// How reading a website's sitemap is going is part of how it is set up, and anybody who can see
    /// its numbers can check that. Changing the sitemap is what takes a role.
    /// </summary>
    [Theory]
    [InlineData(SiteRole.Viewer)]
    [InlineData(SiteRole.Editor)]
    [InlineData(SiteRole.Owner)]
    public async Task Any_Role_May_Read_How_Reading_Is_Going(SiteRole role)
    {
        var (site, address) = await WebsiteWithASitemapAsync();
        var browser = await SignedIn.AsAsync(stack, role, site.Id);

        using (browser)
        {
            (await StatusOfAsync(browser, site.Id)).Address.Should().Be(address);
        }
    }

    /// <summary>
    /// A website that does not exist and a website the caller has no role on answer identically, so
    /// this cannot be used to find out which identifiers on an install are real, or which of them
    /// name a sitemap.
    /// </summary>
    [Fact]
    public async Task Another_Accounts_Website_Is_Not_Found()
    {
        var mine = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var (theirs, _) = await WebsiteWithASitemapAsync();
        var browser = await SignedIn.AsAsync(stack, SiteRole.Owner, mine.Id);

        using (browser)
        {
            var existing = await browser.GetAsync(Sitemap(theirs.Id));
            var invented = await browser.GetAsync(Sitemap(Guid.NewGuid()));

            existing.StatusCode.Should().Be(HttpStatusCode.NotFound);
            invented.StatusCode.Should().Be(existing.StatusCode);
        }
    }

    [Fact]
    public async Task Nobody_Signed_In_Cannot_Read_How_Reading_Is_Going()
    {
        var (site, _) = await WebsiteWithASitemapAsync();
        using var browser = await Browser.OpenAsync(stack);

        var response = await browser.GetAsync(Sitemap(site.Id));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// An operator who switched reading off has not deleted anything. What was read before is still
    /// reported, and the state says why it is not moving.
    /// </summary>
    [Fact]
    public async Task Reading_Switched_Off_Says_So()
    {
        var (site, address) = await WebsiteWithASitemapAsync();
        var at = SitemapSeed.Now(stack);
        var emailAddress = SignedIn.Address();
        var (created, account) = await ControlPlaneSeed.AddAccountAsync(stack, emailAddress, Passwords.Acceptable);

        created.Succeeded.Should().BeTrue();
        await ControlPlaneSeed.GrantAsync(stack, site.Id, account.Id, SiteRole.Viewer);

        await SitemapSeed.WriteAsync(
            stack,
            SitemapSeed.Read(site.Id, address, at),
            Post(site.Id, "/posts/first", at, publishedAt: at));

        using var switchedOff = stack.WithWebHostBuilder(builder =>
            builder.UseSetting(TestSettings.SitemapReading, bool.FalseString));
        using var browser = await SignedIn.AsAccountAsync(switchedOff, emailAddress);

        var status = await StatusOfAsync(browser, site.Id);

        status.State.Should().Be("off");
        status.Address.Should().Be(address);
        status.ReadAt.Should().Be(at);
        status.Posts.Should().Be(1);
        status.Dated.Should().Be(1);
    }

    /// <summary>
    /// With reading switched off the panel says so whether or not a sitemap is named, rather than
    /// asking somebody to add one so their posts can be counted, which nothing would then read.
    /// </summary>
    [Fact]
    public async Task A_Website_Without_A_Sitemap_Says_Off_With_Reading_Switched_Off()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var emailAddress = SignedIn.Address();
        var (created, account) = await ControlPlaneSeed.AddAccountAsync(stack, emailAddress, Passwords.Acceptable);

        created.Succeeded.Should().BeTrue();
        await ControlPlaneSeed.GrantAsync(stack, site.Id, account.Id, SiteRole.Viewer);

        using var switchedOff = stack.WithWebHostBuilder(builder =>
            builder.UseSetting(TestSettings.SitemapReading, bool.FalseString));
        using var browser = await SignedIn.AsAccountAsync(switchedOff, emailAddress);

        (await StatusOfAsync(browser, site.Id)).State.Should().Be("off");
    }

    /// <summary>
    /// A switch that is mistyped in an environment file stops the installation with a sentence naming
    /// it, rather than leaving somebody who thought they had switched reading off with an engine that
    /// goes on fetching from other people's websites.
    /// </summary>
    [Fact]
    public void An_Installation_Does_Not_Start_On_A_Switch_That_Is_Neither_On_Nor_Off()
    {
        HostRefusal.Of(() => stack.WithWebHostBuilder(builder =>
                builder.UseSetting(TestSettings.SitemapReading, "perhaps")))
            .Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain(TestSettings.SitemapReading);
    }

    /// <summary>
    /// Each state a reading can end in reaches the dashboard under the word it writes its sentence
    /// from, with the problem that goes with it. A word the engine sent and the dashboard could not
    /// read would be a status line with nothing on it.
    /// </summary>
    [Theory]
    [InlineData(SitemapState.Waiting, null, "waiting", null)]
    [InlineData(SitemapState.Read, null, "read", null)]
    [InlineData(SitemapState.PartlyRead, SitemapProblem.TooMany, "partly-read", "too-many")]
    [InlineData(SitemapState.PartlyRead, SitemapProblem.LeftTheWebsite, "partly-read", "left-the-website")]
    [InlineData(SitemapState.Failed, SitemapProblem.NotFound, "failed", "not-found")]
    [InlineData(SitemapState.Failed, SitemapProblem.RobotsDisallowed, "failed", "robots-disallowed")]
    [InlineData(SitemapState.Failed, SitemapProblem.RobotsUnreadable, "failed", "robots-unreadable")]
    [InlineData(SitemapState.Failed, SitemapProblem.AddressesWithQueries, "failed", "addresses-with-queries")]
    public async Task What_A_Reading_Came_To_Is_Reported(
        SitemapState state,
        SitemapProblem? problem,
        string stateWord,
        string? problemWord)
    {
        var (site, address) = await WebsiteWithASitemapAsync();
        var at = SitemapSeed.Now(stack);
        var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);

        await SitemapSeed.WriteAsync(stack, SitemapSeed.Read(site.Id, address, at, state, problem));

        using (browser)
        {
            var status = await StatusOfAsync(browser, site.Id);

            status.State.Should().Be(stateWord);
            status.Problem.Should().Be(problemWord);
            status.ReadAt.Should().Be(at);
            status.SucceededAt.Should().Be(at);
        }
    }

    /// <summary>
    /// A post is what the page says it is and whether the sitemap still lists it. These are the numbers
    /// the panel prints, and each is counted over the rows only this website holds.
    /// </summary>
    [Fact]
    public async Task The_Posts_Counted_Are_The_Ones_This_Website_Lists_Now()
    {
        var (site, address) = await WebsiteWithASitemapAsync();
        var (neighbour, _) = await WebsiteWithASitemapAsync();
        var at = SitemapSeed.Now(stack);
        var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);

        await SitemapSeed.WriteAsync(
            stack,
            SitemapSeed.Read(site.Id, address, at),

            // A post, with a date, read.
            Post(site.Id, "/posts/dated", at, publishedAt: at),

            // A post, not read yet.
            Post(site.Id, "/posts/waiting", at, dueAt: at),

            // A post with no date, read.
            Post(site.Id, "/posts/undated", at),

            // A page the sitemap's posts part lists that says it is not an article.
            Post(site.Id, "/about", at, type: PostPageType.Other),

            // A page that has gone, though the sitemap has not caught up with it.
            Post(site.Id, "/posts/gone", at, publishedAt: at, outcome: PostPageOutcome.Gone),

            // A post the sitemap no longer lists.
            new SitePost
            {
                SiteId = site.Id,
                Path = "/posts/unlisted",
                Address = $"https://{site.Domain}/posts/unlisted",
                ListedInPostsPart = true,
                FirstListedAt = at,
                PublishedAt = at,
                UnlistedAt = at,
            },

            // A page from outside the posts part, not read yet, so nothing says it is a post.
            new SitePost
            {
                SiteId = site.Id,
                Path = "/from-elsewhere",
                Address = $"https://{site.Domain}/from-elsewhere",
                FirstListedAt = at,
                PageDueAt = at,
            },

            // A page the website turned the reader away from, so nothing says it is a post.
            new SitePost
            {
                SiteId = site.Id,
                Path = "/refused",
                Address = $"https://{site.Domain}/refused",
                FirstListedAt = at,
                PageOutcome = PostPageOutcome.TurnedAway,
            },

            // One it turned the reader away from that the sitemap no longer lists.
            new SitePost
            {
                SiteId = site.Id,
                Path = "/refused-and-unlisted",
                Address = $"https://{site.Domain}/refused-and-unlisted",
                FirstListedAt = at,
                UnlistedAt = at,
                PageOutcome = PostPageOutcome.TurnedAway,
            },

            // Somebody else's.
            Post(neighbour.Id, "/posts/theirs", at, publishedAt: at),
            Post(neighbour.Id, "/posts/refused", at, outcome: PostPageOutcome.TurnedAway));

        using (browser)
        {
            var status = await StatusOfAsync(browser, site.Id);

            status.Posts.Should().Be(3);
            status.Dated.Should().Be(1);
            status.Reading.Should().Be(2);
            status.TurnedAway.Should().Be(1);
        }
    }

    /// <summary>
    /// A row left over from another address says nothing about this one, though the posts it listed
    /// are still counted: those are kept until a complete reading of the new sitemap says otherwise.
    /// </summary>
    [Fact]
    public async Task A_Reading_Of_Another_Address_Says_Nothing_About_This_One()
    {
        var (site, address) = await WebsiteWithASitemapAsync();
        var at = SitemapSeed.Now(stack);
        var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);

        await SitemapSeed.WriteAsync(
            stack,
            SitemapSeed.Read(
                site.Id,
                $"https://{site.Domain}/old-sitemap.xml",
                at,
                SitemapState.Failed,
                SitemapProblem.NotFound),
            Post(site.Id, "/posts/kept", at));

        using (browser)
        {
            var status = await StatusOfAsync(browser, site.Id);

            status.Should().Be(new SitemapStatusResponse(address, "waiting", null, null, null, 1, 0, 0, 0));
        }
    }

    /// <summary>
    /// The names on the wire are what the dashboard's own check of this answer is written against,
    /// and this is the only place that sees them as they are sent rather than after a client has
    /// been forgiving about them.
    /// </summary>
    [Fact]
    public async Task The_Answer_Is_Written_Under_The_Names_The_Dashboard_Reads()
    {
        var (site, address) = await WebsiteWithASitemapAsync();
        var at = SitemapSeed.Now(stack);
        var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);

        await SitemapSeed.WriteAsync(
            stack,
            SitemapSeed.Read(site.Id, address, at, SitemapState.PartlyRead, SitemapProblem.TooSlow));

        using (browser)
        {
            var response = await browser.GetAsync(Sitemap(site.Id));

            using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation.Token));

            answer.RootElement.EnumerateObject().Select(property => property.Name)
                .Should().Equal("address", "state", "problem", "readAt", "succeededAt", "posts", "dated", "reading", "turnedAway");
            answer.RootElement.GetProperty("state").GetString().Should().Be("partly-read");
            answer.RootElement.GetProperty("problem").GetString().Should().Be("too-slow");
        }
    }

    private static string Sitemap(Guid siteId) => $"/api/sites/{siteId}/sitemap";

    private async Task<(Site Site, string Address)> WebsiteWithASitemapAsync()
    {
        var domain = Domain();
        var address = $"https://{domain}/sitemap.xml";
        var site = await ControlPlaneSeed.AddSiteAsync(
            stack,
            domain: domain,
            configure: created => created.SetSitemapAddress(address));

        return (site, address);
    }

    /// <summary>A page the sitemap's posts part lists.</summary>
    private static SitePost Post(
        Guid siteId,
        string path,
        DateTimeOffset at,
        DateTimeOffset? publishedAt = null,
        DateTimeOffset? dueAt = null,
        PostPageType? type = null,
        PostPageOutcome? outcome = null) =>
        new()
        {
            SiteId = siteId,
            Path = path,
            Address = $"https://example.com{path}",
            ListedInPostsPart = true,
            FirstListedAt = at,
            PublishedAt = publishedAt,
            PageDueAt = dueAt,
            PageType = type,
            PageOutcome = outcome,
        };

    private static async Task<SitemapStatusResponse> StatusOfAsync(Browser browser, Guid siteId)
    {
        var response = await browser.GetAsync(Sitemap(siteId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var status = await response.Content.ReadFromJsonAsync<SitemapStatusResponse>(Cancellation.Token);

        status.Should().NotBeNull();

        return status;
    }

    private static string Domain() => $"sitemap-{Guid.NewGuid():n}.example";
}
