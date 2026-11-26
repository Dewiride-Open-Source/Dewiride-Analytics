using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Application.Consolidation;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Integration.Tests.Fixtures;

namespace Dewiride.Analytics.Integration.Tests.Dashboard;

/// <summary>
/// Proves what a reader is shown on the list of every page: each page a website was viewed at, with its
/// views across the website's whole record, the most viewed first, searched for by its address and a page
/// at a time — and that a question that cannot be answered as asked is refused before the website is
/// looked for.
/// </summary>
/// <remarks>
/// The views come from reports and from months of pages kept as an engine keeps them. A search is written
/// by whoever asks, so it is proved to stand only for itself, to change nothing, and never to be repeated
/// in a refusal.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class AllPagesReadTests(AnalyticsStackFixture stack)
{
    private const string Kolkata = "Asia/Kolkata";

    private const string Guide = "/guides/getting-started/";
    private const string Old = "/old/";
    private const string Archive = "/archive/2024/";
    private const string Bad = "/bad%FF/";
    private const string Cafe = "/caf%C3%A9/";
    private const string Odd = "/odd%C3(/";
    private const string Pricing = "/pricing/";

    private const string ImpossiblePage = "Ask for between 1 and 100 pages at a time.";
    private const string BeforeTheStart = "Start the list at the beginning or further along it, never before it.";
    private const string UnknownPopulation = "Ask about everybody, or only about people.";
    private const string SearchTooLong = "Search for at most 100 characters.";

    /// <summary>A search one character longer than a search may be, of text written nowhere else.</summary>
    private static readonly string TooLong = "refused-" + new string('x', 93);

    /// <summary>
    /// Searches written as statements, as a wildcard meant to widen one, as placeholders, and as bytes that
    /// are not text or are a character no text value can hold — each as it travels in an address.
    /// </summary>
    public static TheoryData<string> ReadAsSql =>
    [
        Uri.EscapeDataString("'; DROP TABLE events; --"),
        Uri.EscapeDataString("' OR '1'='1"),
        Uri.EscapeDataString("%' OR path LIKE '%"),
        Uri.EscapeDataString(@"\' ESCAPE '' OR TRUE --"),
        Uri.EscapeDataString("') UNION SELECT path FROM events --"),
        Uri.EscapeDataString("$1 @p0 {words:Array(String)}"),
        "%FF",
        "%ED%A0%80",
        "no%00such%00post",
    ];

    /// <summary>
    /// Questions that cannot be answered as asked, each with what its refusal says and something it asked
    /// that the refusal must not repeat: alone, and together in the order they are checked in.
    /// </summary>
    public static TheoryData<string, string, string> CannotBeAnsweredAsAsked => new()
    {
        { "limit=0", ImpossiblePage, "limit=0" },
        { "limit=101", ImpossiblePage, "limit=101" },
        { "limit=-1", ImpossiblePage, "limit=-1" },
        { "offset=-1", BeforeTheStart, "offset=-1" },
        { "only=robots", UnknownPopulation, "robots" },
        { $"search={TooLong}", SearchTooLong, TooLong },
        { $"limit=0&search={TooLong}&only=robots", ImpossiblePage, TooLong },
        { $"search={TooLong}&only=robots", SearchTooLong, TooLong },
    };

    /// <summary>
    /// Every page is listed with what was kept of it in the months whose reports have gone and what the
    /// reports say since, added up — the most viewed first, the path breaking a tie — and the answer says
    /// which month the views are counted from and that a month kept only its busiest pages.
    /// </summary>
    [Fact]
    public async Task Every_Page_Is_Listed_With_Its_Views_Across_The_Whole_Record()
    {
        var read = await ReadAcrossTheRecordAsync();

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, read.Site.Id);
        var pages = await AskAsync(browser, read.Site.Id);

        Listed(pages).Should().Equal(
            (Guide, 43L), (Old, 9L), (Archive, 1L), (Bad, 1L), (Cafe, 1L), (Odd, 1L), (Pricing, 1L));
        pages.Matching.Should().Be(7);
        pages.MostPageViews.Should().Be(43);
        pages.ViewsFrom.Should().Be(Month(read.LongAgo));
        pages.LeftOut.Should().BeTrue();
    }

    /// <summary>
    /// In the month running now, with nothing kept, a page has exactly the views the list of pages for the
    /// month gives it, for everybody and for people alike.
    /// </summary>
    /// <param name="only">Who the answer is about: nothing for everybody, or <c>people</c>.</param>
    [Theory]
    [InlineData("")]
    [InlineData("people")]
    public async Task In_The_Running_Month_A_Page_Has_The_Views_The_Pages_Card_Gives_It(string only)
    {
        var (site, running) = await ReadThisMonthAsync();
        var calendar = new ZoneCalendar(Kolkata);
        var people = only.Length == 0 ? string.Empty : $"&only={only}";

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var listed = await AskAsync(browser, site.Id, $"?limit=100{people}");
        var card = await KeptRecord.GetAsync<PagesResponse>(
            browser,
            $"/api/sites/{site.Id}/pages?from={KeptRecord.Moment(calendar.MidnightOn(running))}&limit=100{people}");

        card.Pages.Should().NotBeEmpty();
        Listed(listed).Should().Equal(card.Pages.Select(page => (page.Path, page.PageViews)));
        listed.Matching.Should().Be(card.TotalPaths);
        listed.MostPageViews.Should().Be(card.MostPageViews);
        listed.ViewsFrom.Should().Be(Month(running));
        listed.LeftOut.Should().BeFalse();
    }

    /// <summary>
    /// Kept to people, a page's views are the reader's alone, from the reports and from what was kept for
    /// people, and a page no person was delivered is not listed at all.
    /// </summary>
    [Fact]
    public async Task Pages_Of_People_Count_The_Reader_Alone()
    {
        var read = await ReadAcrossTheRecordAsync();

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, read.Site.Id);
        var people = await AskAsync(browser, read.Site.Id, "?only=people");

        Listed(people).Should().Equal((Guide, 26L), (Cafe, 1L), (Pricing, 1L));
        people.Matching.Should().Be(3);
        people.MostPageViews.Should().Be(26);
        people.LeftOut.Should().BeTrue();
    }

    /// <summary>
    /// A search finds the pages whose address holds every word of it, without regard to case, read as words
    /// however it was spaced; a page holding only some of them is not found, and what is found is counted.
    /// </summary>
    [Fact]
    public async Task A_Search_Finds_Pages_Holding_Every_Word_Whatever_The_Case()
    {
        var read = await ReadAcrossTheRecordAsync();

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, read.Site.Id);
        var both = await AskAsync(browser, read.Site.Id, Search("STARTED guides"));
        var spaced = await AskAsync(browser, read.Site.Id, Search("  archive \t 2024 "));
        var some = await AskAsync(browser, read.Site.Id, Search("guides nowhere"));
        var several = await AskAsync(browser, read.Site.Id, Search("I"));
        var blank = await AskAsync(browser, read.Site.Id, Search("   "));

        Listed(both).Should().Equal((Guide, 43L));
        both.Matching.Should().Be(1);
        both.MostPageViews.Should().Be(43);
        Listed(spaced).Should().Equal((Archive, 1L));
        some.Matching.Should().Be(0);
        Paths(several).Should().Equal(Guide, Archive, Pricing);
        several.Matching.Should().Be(3);
        blank.Matching.Should().Be(7);
    }

    /// <summary>
    /// A page whose address escapes the letters it was written in is found by those letters as they are read,
    /// in either case, and by the escapes as the address holds them; an address whose escapes are not text is
    /// found by what it holds, and what its escapes leave is read as the character that stands for what cannot
    /// be read rather than failing.
    /// </summary>
    [Fact]
    public async Task A_Search_Finds_A_Page_By_The_Letters_Its_Address_Escapes()
    {
        var read = await ReadAcrossTheRecordAsync();

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, read.Site.Id);
        var letters = await AskAsync(browser, read.Site.Id, Search("CAFÉ"));
        var escapes = await AskAsync(browser, read.Site.Id, Search("%c3%a9"));
        var notText = await AskAsync(browser, read.Site.Id, Search("%FF"));
        var halfALetter = await AskAsync(browser, read.Site.Id, Search("%C3("));
        var unreadable = await AskAsync(browser, read.Site.Id, Search("�"));

        Paths(letters).Should().Equal(Cafe);
        Paths(escapes).Should().Equal(Cafe);
        Paths(notText).Should().Equal(Bad);
        Paths(halfALetter).Should().Equal(Odd);
        Paths(unreadable).Should().Equal(Bad, Odd);
    }

    /// <summary>
    /// An address pasted whole finds its page by the path it names, whatever followed the path and whatever
    /// case its scheme and name were written in — alone, or among other words.
    /// </summary>
    [Fact]
    public async Task An_Address_Pasted_Whole_Finds_Its_Page()
    {
        var read = await ReadAcrossTheRecordAsync();

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, read.Site.Id);
        var guide = await AskAsync(
            browser,
            read.Site.Id,
            Search("https://example.com/guides/getting-started/?utm_source=newsletter#top"));
        var cafe = await AskAsync(browser, read.Site.Id, Search("HTTPS://EXAMPLE.COM/café/"));
        var among = await AskAsync(browser, read.Site.Id, Search("pricing https://example.com/pricing/"));

        Paths(guide).Should().Equal(Guide);
        Paths(cafe).Should().Equal(Cafe);
        Paths(among).Should().Equal(Pricing);
    }

    /// <summary>
    /// A search written as a statement is compared as the text it is, finds no page, and leaves every page
    /// where it was; bytes that are not text and a character no text value can hold are answered the same way
    /// rather than failing.
    /// </summary>
    /// <param name="search">The search, as it travels in an address.</param>
    [Theory]
    [MemberData(nameof(ReadAsSql))]
    public async Task A_Search_That_Reads_As_Sql_Finds_Nothing_And_Changes_Nothing(string search)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var calendar = new ZoneCalendar(Kolkata);
        var now = await KeptRecord.NowInsideTheMonthAsync(stack, calendar);
        var lately = Lately(calendar, ZoneCalendar.FirstOf(calendar.DateOf(now)), now);

        await KeptRecord.WriteAsync(
            stack,
            [
                KeptRecord.Page(site.Id, lately, "/a/", "reader-a"),
                KeptRecord.Page(site.Id, lately.AddSeconds(1), "/b/", "reader-b"),
            ]);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var searched = await AskAsync(browser, site.Id, $"?search={search}");
        var afterwards = await AskAsync(browser, site.Id);

        searched.Matching.Should().Be(0);
        searched.ViewsFrom.Should().BeNull();
        searched.MostPageViews.Should().Be(0);
        searched.Shown.Should().BeEmpty();
        afterwards.Matching.Should().Be(2);
        Paths(afterwards).Should().Equal("/a/", "/b/");
    }

    /// <summary>
    /// A page holds as many pages as were asked for, from as far along the list as was asked; a page past the
    /// end — however far past it, beyond the furthest the list can reach included — has nothing on it and
    /// still says how many pages there are and how often the most viewed was viewed, and twenty-five is what a
    /// page holds unless more are asked for.
    /// </summary>
    [Fact]
    public async Task Pages_Are_Read_A_Page_At_A_Time_And_A_Page_Past_The_End_Still_Says_How_Many_There_Are()
    {
        var read = await ReadAcrossTheRecordAsync();

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, read.Site.Id);
        var first = await AskAsync(browser, read.Site.Id, "?limit=2");
        var second = await AskAsync(browser, read.Site.Id, "?limit=2&offset=2");
        var last = await AskAsync(browser, read.Site.Id, "?limit=100&offset=6");
        var beyond = await AskAsync(browser, read.Site.Id, "?limit=2&offset=7");
        var furthest = await AskAsync(
            browser,
            read.Site.Id,
            $"?limit=100&offset={int.MaxValue.ToString(CultureInfo.InvariantCulture)}");
        var unasked = await AskAsync(browser, read.Site.Id);

        Paths(first).Should().Equal(Guide, Old);
        Paths(second).Should().Equal(Archive, Bad);
        second.MostPageViews.Should().Be(43);
        Paths(last).Should().Equal(Pricing);

        foreach (var past in new[] { beyond, furthest })
        {
            past.Shown.Should().BeEmpty();
            past.Matching.Should().Be(7);
            past.MostPageViews.Should().Be(43);
            past.ViewsFrom.Should().Be(Month(read.LongAgo));
        }

        unasked.Shown.Should().HaveCount(7);
    }

    /// <summary>
    /// The answer says that a month kept only its busiest pages only where one the views are read from left
    /// out a page delivered to whoever it is about: a month that left out only pages no person was delivered
    /// says so for everybody and not for people, and a month that left nothing out says nothing.
    /// </summary>
    [Fact]
    public async Task It_Says_So_Where_A_Month_Kept_Only_Its_Busiest_Pages()
    {
        var calendar = new ZoneCalendar(Kolkata);
        var leftOut = await KeptLongAgoAsync(calendar, new PagesLeftOut(2, 3, 0));
        var whole = await KeptLongAgoAsync(calendar, new PagesLeftOut(0, 0, 0));

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, leftOut.Id);
        var everybody = await AskAsync(browser, leftOut.Id);
        var people = await AskAsync(browser, leftOut.Id, "?only=people");

        using var wholeBrowser = await SignedIn.AsAsync(stack, SiteRole.Viewer, whole.Id);
        var nothingLeftOut = await AskAsync(wholeBrowser, whole.Id);

        everybody.LeftOut.Should().BeTrue();
        people.LeftOut.Should().BeFalse();
        nothingLeftOut.LeftOut.Should().BeFalse();
        Listed(nothingLeftOut).Should().Equal((Guide, 4L));
    }

    /// <summary>A website nothing is held of has no page to list, and says nothing was left out.</summary>
    [Fact]
    public async Task A_Website_Nothing_Is_Held_Of_Is_Answered_With_No_Pages()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);

        foreach (var question in new[] { string.Empty, Search("guides"), "?only=people&limit=100&offset=50" })
        {
            var pages = await AskAsync(browser, site.Id, question);

            pages.Matching.Should().Be(0);
            pages.ViewsFrom.Should().BeNull();
            pages.LeftOut.Should().BeFalse();
            pages.MostPageViews.Should().Be(0);
            pages.Shown.Should().BeEmpty();
        }
    }

    /// <summary>
    /// A website whose only kept month kept none of its pages — each was opened by one visitor alone, so the
    /// month kept them only as what they added up to — and that nobody has viewed since has no page to list
    /// and none most viewed, names no month for views no page has, and says that a month left pages out, so
    /// nothing listed is never taken for nothing viewed: for everybody, and for people where one of those
    /// views was a person's.
    /// </summary>
    [Fact]
    public async Task A_Website_Whose_Only_Kept_Month_Kept_No_Page_Lists_None_And_Says_Pages_Were_Left_Out()
    {
        var calendar = new ZoneCalendar(Kolkata);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var now = await KeptRecord.NowInsideTheMonthAsync(stack, calendar);
        var longAgo = ZoneCalendar.FirstOf(calendar.DateOf(now)).AddMonths(-15);
        var leftOut = new PagesLeftOut(3, 3, 1);

        await KeptRecord.KeepAsync(stack, KeptRecord.Counted(site, calendar, longAgo, leftOut.PageViews));
        await KeptPageMonths.KeepAsync(
            stack,
            KeptPageMonths.Counted(site, calendar, longAgo, calendar.MidnightOn(longAgo.AddMonths(1)).AddDays(1), leftOut));

        var kept = (await KeptPageMonths.RowsAsync(stack, site.Id)).Should().ContainSingle().Subject;

        ((string[])kept["paths"]!).Should().BeEmpty();
        KeptPageMonths.LeftOutIn(kept).Should().Be(leftOut);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);

        foreach (var question in new[] { string.Empty, "?only=people" })
        {
            var pages = await AskAsync(browser, site.Id, question);

            pages.Matching.Should().Be(0);
            pages.MostPageViews.Should().Be(0);
            pages.Shown.Should().BeEmpty();
            pages.LeftOut.Should().BeTrue();
            pages.ViewsFrom.Should().BeNull();
        }
    }

    /// <summary>
    /// A question that cannot be answered as asked is refused before the website is looked for — the same
    /// status and the same problem for a website the caller can read and for one that does not exist — with
    /// the first thing wrong with it, in the order the checks are made, and without repeating what was asked.
    /// </summary>
    /// <param name="question">What follows the address.</param>
    /// <param name="detail">What the refusal says.</param>
    /// <param name="asked">Something the question asked that the refusal must not repeat.</param>
    [Theory]
    [MemberData(nameof(CannotBeAnsweredAsAsked))]
    public async Task A_List_That_Cannot_Be_Answered_As_Asked_Is_Refused_Whether_Or_Not_The_Website_Exists(
        string question,
        string detail,
        string asked)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var real = await RefusalAsync(browser, site.Id, $"?{question}", asked);
        var invented = await RefusalAsync(browser, Guid.NewGuid(), $"?{question}", asked);

        real.Status.Should().Be(HttpStatusCode.BadRequest);
        real.Should().Be(invented);
        real.RepeatsWhatWasAsked.Should().BeFalse();
        JsonNode.Parse(real.Problem)!["detail"]!.GetValue<string>().Should().Be(detail);
    }

    /// <summary>
    /// A website somebody has no role on answers exactly as one that does not exist — the same status and the
    /// same problem — so the pages of another account's website can neither be read nor found to be there,
    /// and the answer never repeats what was searched for.
    /// </summary>
    [Fact]
    public async Task Pages_Of_A_Website_Somebody_Cannot_See_Are_Not_Found()
    {
        var mine = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var theirs = await ReadAcrossTheRecordAsync();
        var marker = $"marker-{Guid.NewGuid():n}";

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, mine.Id);
        var existing = await RefusalAsync(browser, theirs.Site.Id, Search(marker), marker);
        var invented = await RefusalAsync(browser, Guid.NewGuid(), Search(marker), marker);
        var unsearched = await RefusalAsync(browser, theirs.Site.Id, string.Empty, marker);

        existing.Status.Should().Be(HttpStatusCode.NotFound);
        existing.Should().Be(invented);
        existing.Should().Be(unsearched);
        existing.RepeatsWhatWasAsked.Should().BeFalse();
    }

    [Fact]
    public async Task The_List_Is_Not_Read_By_Anybody_Signed_Out()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);

        using var browser = await Browser.OpenAsync(stack);
        var response = await browser.GetAsync($"/api/sites/{site.Id}/insights/pages");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The names on the wire are what the dashboard's own check of this answer is written against, and this
    /// is the only place that sees them as they are sent.
    /// </summary>
    [Fact]
    public async Task The_Answer_Is_Written_Under_The_Names_The_Dashboard_Reads()
    {
        var read = await ReadAcrossTheRecordAsync();

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, read.Site.Id);
        var response = await browser.GetAsync($"/api/sites/{read.Site.Id}/insights/pages");

        using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation.Token));
        var root = answer.RootElement;

        Names(root).Should().Equal("matching", "viewsFrom", "leftOut", "mostPageViews", "shown");
        Names(root.GetProperty("shown")[0]).Should().Equal("path", "pageViews");
        root.GetProperty("leftOut").ValueKind.Should().Be(JsonValueKind.True);
    }

    /// <summary>
    /// A website in Kolkata whose busiest pages were kept for a month fifteen months ago — the guide viewed
    /// forty times, twenty-five of them by people, and a page nobody has opened since nine times, with five
    /// views of three other pages left out, one of them a person's — and that this month a reader judged a
    /// person read the guide, a page at an escaped address and the pricing page once each, and a scraper took
    /// the guide twice and an archive page and two pages whose escapes are not text once each.
    /// </summary>
    private async Task<RecordRead> ReadAcrossTheRecordAsync()
    {
        var calendar = new ZoneCalendar(Kolkata);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var now = await KeptRecord.NowInsideTheMonthAsync(stack, calendar);
        var running = ZoneCalendar.FirstOf(calendar.DateOf(now));
        var longAgo = running.AddMonths(-15);
        var lately = Lately(calendar, running, now);
        var reader = $"reader-{site.Id:n}";
        var scraper = $"scraper-{site.Id:n}";

        await KeptRecord.KeepAsync(stack, KeptRecord.Counted(site, calendar, longAgo, 54));
        await KeptPageMonths.KeepAsync(
            stack,
            KeptPageMonths.Counted(
                site,
                calendar,
                longAgo,
                calendar.MidnightOn(longAgo.AddMonths(1)).AddDays(1),
                new PagesLeftOut(3, 5, 1),
                new PathMonthViews(Guide, 40, 25),
                new PathMonthViews(Old, 9, 0)));
        await KeptRecord.WriteAsync(
            stack,
            [
                KeptRecord.Page(site.Id, lately, Guide, reader),
                KeptRecord.Page(site.Id, lately.AddSeconds(1), Cafe, reader),
                KeptRecord.Page(site.Id, lately.AddSeconds(2), Pricing, reader),
                KeptRecord.Page(site.Id, lately.AddSeconds(10), Guide, scraper),
                KeptRecord.Page(site.Id, lately.AddSeconds(11), Guide, scraper),
                KeptRecord.Page(site.Id, lately.AddSeconds(12), Archive, scraper),
                KeptRecord.Page(site.Id, lately.AddSeconds(13), Bad, scraper),
                KeptRecord.Page(site.Id, lately.AddSeconds(14), Odd, scraper),
            ]);
        await KeptRecord.JudgedAsync(stack, site.Id, reader, lately, lately.AddSeconds(2), TrafficCategory.LikelyHuman);
        await KeptRecord.JudgedAsync(
            stack,
            site.Id,
            scraper,
            lately.AddSeconds(10),
            lately.AddSeconds(14),
            TrafficCategory.ContentScraper);

        return new RecordRead(site, longAgo);
    }

    /// <summary>
    /// A website in Kolkata nothing was kept of, read this month: a reader judged a person read two pages and
    /// one of them twice, and a scraper took one of them and another page.
    /// </summary>
    private async Task<(Site Site, DateOnly Running)> ReadThisMonthAsync()
    {
        var calendar = new ZoneCalendar(Kolkata);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var now = await KeptRecord.NowInsideTheMonthAsync(stack, calendar);
        var running = ZoneCalendar.FirstOf(calendar.DateOf(now));
        var lately = Lately(calendar, running, now);
        var reader = $"reader-{site.Id:n}";
        var scraper = $"scraper-{site.Id:n}";

        await KeptRecord.WriteAsync(
            stack,
            [
                KeptRecord.Page(site.Id, lately, Guide, reader),
                KeptRecord.Page(site.Id, lately.AddSeconds(1), Guide, reader),
                KeptRecord.Page(site.Id, lately.AddSeconds(2), Pricing, reader),
                KeptRecord.Page(site.Id, lately.AddSeconds(10), Pricing, scraper),
                KeptRecord.Page(site.Id, lately.AddSeconds(11), Archive, scraper),
            ]);
        await KeptRecord.JudgedAsync(stack, site.Id, reader, lately, lately.AddSeconds(2), TrafficCategory.LikelyHuman);
        await KeptRecord.JudgedAsync(
            stack,
            site.Id,
            scraper,
            lately.AddSeconds(10),
            lately.AddSeconds(11),
            TrafficCategory.ContentScraper);

        return (site, running);
    }

    /// <summary>
    /// A website in Kolkata whose busiest pages were kept fifteen months ago — the guide, viewed four times —
    /// with what that month left out, and nothing read since.
    /// </summary>
    private async Task<Site> KeptLongAgoAsync(ZoneCalendar calendar, PagesLeftOut leftOut)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var now = await KeptRecord.NowInsideTheMonthAsync(stack, calendar);
        var longAgo = ZoneCalendar.FirstOf(calendar.DateOf(now)).AddMonths(-15);

        await KeptRecord.KeepAsync(stack, KeptRecord.Counted(site, calendar, longAgo, 4 + leftOut.PageViews));
        await KeptPageMonths.KeepAsync(
            stack,
            KeptPageMonths.Counted(
                site,
                calendar,
                longAgo,
                calendar.MidnightOn(longAgo.AddMonths(1)).AddDays(1),
                leftOut,
                new PathMonthViews(Guide, 4, 2)));

        return site;
    }

    /// <summary>A moment this month, no more than forty minutes ago.</summary>
    private static DateTimeOffset Lately(ZoneCalendar calendar, DateOnly running, DateTimeOffset now) =>
        calendar.MidnightOn(running) > now.AddMinutes(-40) ? calendar.MidnightOn(running) : now.AddMinutes(-40);

    /// <summary>Asks for a page of every page of a website and insists it was answered.</summary>
    private static Task<AllPagesResponse> AskAsync(Browser browser, Guid siteId, string question = "") =>
        KeptRecord.GetAsync<AllPagesResponse>(browser, $"/api/sites/{siteId}/insights/pages{question}");

    /// <summary>Everything the refusal of a page of pages says, and whether it repeats what was asked.</summary>
    private static async Task<Refusal.Said> RefusalAsync(Browser browser, Guid siteId, string question, string asked) =>
        await Refusal.WholeAsync(await browser.GetAsync($"/api/sites/{siteId}/insights/pages{question}"), asked);

    /// <summary>The question searching for a text, escaped as the dashboard escapes it.</summary>
    private static string Search(string text) => $"?search={Uri.EscapeDataString(text)}";

    private static (string Path, long PageViews)[] Listed(AllPagesResponse pages) =>
        [.. pages.Shown.Select(page => (page.Path, page.PageViews))];

    private static string[] Paths(AllPagesResponse pages) => [.. pages.Shown.Select(page => page.Path)];

    private static string[] Names(JsonElement element) => [.. element.EnumerateObject().Select(property => property.Name)];

    private static string Month(DateOnly month) => month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static string Domain() => $"all-pages-{Guid.NewGuid():n}.example";

    /// <summary>What was written of a website's record.</summary>
    /// <param name="Site">The website.</param>
    /// <param name="LongAgo">The month fifteen months ago whose pages were kept.</param>
    private sealed record RecordRead(Site Site, DateOnly LongAgo);
}
