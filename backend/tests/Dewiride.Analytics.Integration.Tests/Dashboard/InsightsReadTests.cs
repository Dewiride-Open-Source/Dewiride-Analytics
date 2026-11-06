using System.Globalization;
using System.Net;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Integration.Tests.Fixtures;

namespace Dewiride.Analytics.Integration.Tests.Dashboard;

/// <summary>
/// Proves what a reader is shown on the Insights screen: a website's whole record, read from the
/// months kept after their reports and the reports of every month nothing was kept of.
/// </summary>
/// <remarks>
/// A kept month and a month answered from its reports are counted apart, by different statements,
/// and these hold them to one answer wherever a month's reports all arrive under one key. ADR 0066
/// states the one case that can move by a few: two halves of the measurement linked only across the
/// month's edge, while the month is read beside the one after it.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class InsightsReadTests(AnalyticsStackFixture stack)
{
    private const string Kolkata = "Asia/Kolkata";
    private const string Address = "/insights";

    /// <summary>
    /// For a month kept and the month running now: the first day, the page views, the best day, and
    /// the weekday and hour on the website's clock.
    /// </summary>
    [Fact]
    public async Task Insights_Read_The_Kept_Months()
    {
        var read = await TwoMonthsReadAsync();

        await KeptRecord.CountAsync(stack, read.Site);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, read.Site.Id);
        var insights = await AskAsync(browser, read.Site.Id);

        insights.FirstDay.Should().Be(Day(read.FirstDay));
        insights.PageViews.Should().Be(3);
        insights.BestDay.Should().Be(new InsightsBestDay(Day(read.FirstDay), 2));
        insights.Weekdays.Should().Equal(read.Weekdays);
        insights.BusiestWeekdays.Should().Equal(Busiest(read.Weekdays));
        insights.Hours.Should().Equal(read.Hours);
        insights.BusiestHours.Should().Equal(Busiest(read.Hours));
        insights.Missing.Should().BeEmpty();
        insights.Unjudged.Should().BeEmpty();
    }

    /// <summary>
    /// A month counted by the statements that keep it and the same month answered from its reports are
    /// one answer, in a zone away from UTC, so these figures do not move when a month is kept.
    /// </summary>
    [Fact]
    public async Task Insights_Say_The_Same_Whether_A_Month_Was_Kept_Or_Not()
    {
        var read = await TwoMonthsReadAsync();

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, read.Site.Id);
        var answered = await AskAsync(browser, read.Site.Id);

        (await KeptRecord.RowsAsync(stack, read.Site.Id)).Should().BeEmpty();

        await KeptRecord.CountAsync(stack, read.Site);

        (await KeptRecord.RowsAsync(stack, read.Site.Id)).Should().ContainSingle();

        var kept = await AskAsync(browser, read.Site.Id);

        kept.FirstDay.Should().Be(answered.FirstDay);
        kept.PageViews.Should().Be(answered.PageViews);
        kept.Visitors.Should().Be(answered.Visitors);
        kept.BestDay.Should().Be(answered.BestDay);
        kept.Weekdays.Should().Equal(answered.Weekdays);
        kept.Hours.Should().Equal(answered.Hours);
        kept.Months.Should().Equal(answered.Months);
    }

    /// <summary>Two screens never disagree about one website: its totals here are All time's, to the unit.</summary>
    /// <param name="only">Who is being asked about.</param>
    [Theory]
    [InlineData("")]
    [InlineData("?only=people")]
    public async Task Insights_Agree_With_All_Time_To_The_Unit(string only)
    {
        var read = await TwoMonthsReadAsync();

        await KeptRecord.CountAsync(stack, read.Site);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, read.Site.Id);
        var allTime = await KeptRecord.GetAsync<AllTimeResponse>(browser, $"/api/sites/{read.Site.Id}/all-time{only}");
        var insights = await KeptRecord.GetAsync<InsightsResponse>(browser, $"/api/sites/{read.Site.Id}{Address}{only}");

        insights.PageViews.Should().Be(allTime.PageViews);
        insights.Visitors.Should().Be(allTime.Visitors);
        insights.FirstDay.Should().Be(allTime.FirstDay);
        insights.Missing.Should().Equal(allTime.Missing);
        insights.Unjudged.Should().Equal(allTime.Unjudged);
    }

    /// <summary>
    /// An answer about everybody is complete to the moment it was read. One about people is complete
    /// as far as judging had reached: judging never started on this website, so its first month was
    /// kept as judged only to its first instant.
    /// </summary>
    [Fact]
    public async Task Insights_Say_How_Far_They_Are_Complete()
    {
        var read = await TwoMonthsReadAsync();

        await KeptRecord.CountAsync(stack, read.Site);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, read.Site.Id);
        var everybody = await AskAsync(browser, read.Site.Id);
        var people = await AskAsync(browser, read.Site.Id, "?only=people");

        everybody.CompleteTo.Should().Be(everybody.To);
        people.CompleteTo.Should().Be(read.Calendar.MidnightOn(read.LastMonth));
    }

    /// <summary>
    /// Kept to people, the screen counts the reader and leaves the scraper out of everything, whether
    /// the month the scraper came in was kept or is still answered from its reports.
    /// </summary>
    /// <param name="kept">Whether the finished month has been kept.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Insights_Of_People_Count_The_Reader_Alone(bool kept)
    {
        var read = await TwoMonthsReadAsync();
        var scraper = $"scraper-{read.Site.Id:n}";
        var theSeventh = read.Calendar.MidnightOn(read.LastMonth.AddDays(6));
        var afternoon = theSeventh.AddHours(15);

        await KeptRecord.WriteAsync(
            stack,
            [.. Enumerable.Range(0, 4).Select(page => KeptRecord.Page(read.Site.Id, afternoon.AddSeconds(page), $"/archive/{page}", scraper))]);
        await KeptRecord.JudgedAsync(stack, read.Site.Id, scraper, afternoon, afternoon.AddSeconds(3), TrafficCategory.ContentScraper);

        if (kept)
        {
            await KeptRecord.CountAsync(stack, read.Site);
        }

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, read.Site.Id);
        var everybody = await AskAsync(browser, read.Site.Id);
        var people = await AskAsync(browser, read.Site.Id, "?only=people");

        everybody.PageViews.Should().Be(7);
        everybody.BestDay.Should().Be(new InsightsBestDay(Day(read.LastMonth.AddDays(6)), 4));
        everybody.Hours[15].Should().Be(read.Hours[15] + 4);

        people.PageViews.Should().Be(3);
        people.BestDay.Should().Be(new InsightsBestDay(Day(read.FirstDay), 2));
        people.Weekdays.Should().Equal(read.Weekdays);
        people.Hours.Should().Equal(read.Hours);
        people.Months.Select(month => month.PageViews).Should().Equal(2, 1);
    }

    /// <summary>
    /// Every month carries the days its figure covers, which is what an average per day divides by:
    /// from the first day in the first month, and up to and including today in the month running now.
    /// </summary>
    [Fact]
    public async Task The_Grid_Carries_Days_Measured_For_Every_Month()
    {
        var read = await TwoMonthsReadAsync();

        await KeptRecord.CountAsync(stack, read.Site);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, read.Site.Id);
        var insights = await AskAsync(browser, read.Site.Id);
        var today = read.Calendar.DateOf(insights.To);

        insights.Months.Should().Equal(
            new InsightsMonth(Month(read.LastMonth), 2, DateTime.DaysInMonth(read.LastMonth.Year, read.LastMonth.Month) - 4),
            new InsightsMonth(Month(ZoneCalendar.FirstOf(today)), 1, today.Day));
    }

    /// <summary>
    /// A month that was never kept and can no longer be counted has no figure and covers no days, and
    /// the answer names it; the month kept before it, and the months still held after it, stand.
    /// </summary>
    [Fact]
    public async Task A_Month_That_Was_Never_Counted_Has_No_Figure_And_Is_Named()
    {
        var calendar = new ZoneCalendar(Kolkata);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var running = ZoneCalendar.FirstOf(calendar.DateOf(await KeptRecord.NowInsideTheMonthAsync(stack, calendar)));
        var longAgo = running.AddMonths(-15);

        await KeptRecord.KeepAsync(stack, KeptRecord.Counted(site, calendar, longAgo, 40));

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var insights = await AskAsync(browser, site.Id);

        insights.Months.Should().HaveCount(16);
        insights.Months[0].Should().Be(new InsightsMonth(Month(longAgo), 40, DateTime.DaysInMonth(longAgo.Year, longAgo.Month)));
        insights.Months[1].Should().Be(new InsightsMonth(Month(longAgo.AddMonths(1)), null, 0));
        insights.Months[^1].PageViews.Should().Be(0);
        insights.Missing.Should().Contain(Month(longAgo.AddMonths(1)));
        insights.Months.Count(month => month.PageViews is null).Should().Be(insights.Missing.Count);
        insights.BestDay.Should().Be(new InsightsBestDay(Day(longAgo), 40));
        insights.PageViews.Should().Be(40);
    }

    /// <summary>
    /// A month kept before its visits were all judged, and too old to be counted again, has figures for
    /// everybody but none for the people in it: asked about people it is named apart from the months
    /// never counted, and adds nothing.
    /// </summary>
    [Fact]
    public async Task A_Month_Kept_Before_Its_Visits_Were_All_Judged_Is_Named_Among_People()
    {
        var calendar = new ZoneCalendar(Kolkata);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var running = ZoneCalendar.FirstOf(calendar.DateOf(await KeptRecord.NowInsideTheMonthAsync(stack, calendar)));
        var longAgo = running.AddMonths(-15);
        var counted = KeptRecord.Counted(site, calendar, longAgo, 40);

        await KeptRecord.KeepAsync(stack, counted with { JudgedTo = counted.Month.Window.From });

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var everybody = await AskAsync(browser, site.Id);
        var people = await AskAsync(browser, site.Id, "?only=people");

        everybody.Months[0].PageViews.Should().Be(40);
        everybody.Unjudged.Should().BeEmpty();

        people.Months[0].Should().Be(new InsightsMonth(Month(longAgo), null, 0));
        people.Unjudged.Should().Equal(Month(longAgo));
        people.Missing.Should().Equal(everybody.Missing);
        people.BestDay.Should().BeNull();
    }

    /// <summary>Before anything was measured there is nothing to name, and it says so rather than naming Monday.</summary>
    [Fact]
    public async Task Insights_Before_Anything_Was_Measured_Say_So()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var insights = await AskAsync(browser, site.Id);

        insights.FirstDay.Should().BeNull();
        insights.PageViews.Should().Be(0);
        insights.BestDay.Should().BeNull();
        insights.Weekdays.Should().HaveCount(7).And.OnlyContain(pages => pages == 0);
        insights.Hours.Should().HaveCount(24).And.OnlyContain(pages => pages == 0);
        insights.BusiestWeekdays.Should().BeEmpty();
        insights.BusiestHours.Should().BeEmpty();
        insights.Months.Should().BeEmpty();
    }

    [Fact]
    public async Task Insights_Refuse_Anybody_But_Everybody_Or_People()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var response = await browser.GetAsync($"/api/sites/{site.Id}{Address}?only=robots");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Insights_Of_A_Website_Somebody_Cannot_See_Are_Not_Found()
    {
        var mine = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var theirs = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, mine.Id);
        var response = await browser.GetAsync($"/api/sites/{theirs.Id}{Address}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Two pages last month, on the fifth at eleven in the morning in India, and one this month, all
    /// read by one person and judged a person.
    /// </summary>
    private async Task<TwoMonths> TwoMonthsReadAsync()
    {
        var calendar = new ZoneCalendar(Kolkata);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var now = await KeptRecord.NowInsideTheMonthAsync(stack, calendar);
        var running = ZoneCalendar.FirstOf(calendar.DateOf(now));
        var lastMonth = running.AddMonths(-1);
        var firstDay = lastMonth.AddDays(4);
        var reader = $"reader-{site.Id:n}";
        var then = calendar.MidnightOn(firstDay).AddHours(11);
        var lately = calendar.MidnightOn(running) > now.AddMinutes(-40) ? calendar.MidnightOn(running) : now.AddMinutes(-40);

        await KeptRecord.WriteAsync(
            stack,
            [
                KeptRecord.Page(site.Id, then, "/", reader),
                KeptRecord.Page(site.Id, then.AddMinutes(1), "/pricing", reader),
                KeptRecord.Page(site.Id, lately, "/", reader),
            ]);

        await KeptRecord.JudgedAsync(stack, site.Id, reader, then, then.AddMinutes(1), TrafficCategory.LikelyHuman);
        await KeptRecord.JudgedAsync(stack, site.Id, reader, lately, lately, TrafficCategory.LikelyHuman);

        var weekdays = new long[7];
        var hours = new long[24];
        var zone = TimeZoneInfo.FindSystemTimeZoneById(Kolkata);

        weekdays[MondayFirst(firstDay)] += 2;
        hours[11] += 2;
        weekdays[MondayFirst(calendar.DateOf(lately))] += 1;
        hours[TimeZoneInfo.ConvertTime(lately, zone).Hour] += 1;

        return new TwoMonths(site, calendar, lastMonth, firstDay, weekdays, hours);
    }

    private static Task<InsightsResponse> AskAsync(Browser browser, Guid siteId, string only = "") =>
        KeptRecord.GetAsync<InsightsResponse>(browser, $"/api/sites/{siteId}{Address}{only}");

    private static int MondayFirst(DateOnly day) => ((int)day.DayOfWeek + 6) % 7;

    private static int[] Busiest(long[] figures) =>
        [.. Enumerable.Range(0, figures.Length).Where(index => figures[index] == figures.Max())];

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Month(DateOnly month) => month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static string Domain() => $"insights-{Guid.NewGuid():n}.example";

    /// <summary>What was read, and what a reader should be told of it.</summary>
    /// <param name="Site">The website.</param>
    /// <param name="Calendar">Its calendar.</param>
    /// <param name="LastMonth">The first of the month before the one running now.</param>
    /// <param name="FirstDay">The day of the website's first page.</param>
    /// <param name="Weekdays">Pages read on each day of the week, Monday first.</param>
    /// <param name="Hours">Pages read in each hour of the day.</param>
    private sealed record TwoMonths(
        Site Site,
        ZoneCalendar Calendar,
        DateOnly LastMonth,
        DateOnly FirstDay,
        long[] Weekdays,
        long[] Hours);
}
