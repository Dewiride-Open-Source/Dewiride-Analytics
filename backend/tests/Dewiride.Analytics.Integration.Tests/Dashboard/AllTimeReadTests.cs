using System.Net;
using System.Text.Json;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Integration.Tests.Fixtures;

namespace Dewiride.Analytics.Integration.Tests.Dashboard;

/// <summary>
/// Proves what a reader is shown for a website's whole record: every month from the first one kept
/// or still held to the one running now.
/// </summary>
/// <remarks>
/// All time is read from two places — the months kept after their reports, and the reports of every
/// month nothing was kept of — and these hold it to the one figure a reader can check it against
/// while the reports are still there: the same stretch read as an ordinary period.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class AllTimeReadTests(AnalyticsStackFixture stack)
{
    private const string Kolkata = "Asia/Kolkata";

    [Fact]
    public async Task All_Time_Adds_Up_The_Kept_Months_And_The_Month_Running_Now()
    {
        var (site, calendar, lastMonth) = await TwoMonthsReadAsync();

        await KeptRecord.CountAsync(stack, site);
        (await KeptRecord.RowsAsync(stack, site.Id)).Should().ContainSingle();

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var allTime = await KeptRecord.GetAsync<AllTimeResponse>(browser, $"/api/sites/{site.Id}/all-time");
        var since = await OverviewAsync(browser, site.Id, calendar.MidnightOn(lastMonth), allTime.To);

        allTime.PageViews.Should().Be(since.PageViews).And.Be(3);

        // One reader each month, on one key the test reuses: a month counts each visitor once, and
        // All time adds the months up, where an overview over both months would count the key once.
        allTime.Visitors.Should().Be(2);
        allTime.Missing.Should().BeEmpty();
        allTime.From.Should().Be(calendar.MidnightOn(lastMonth));
        allTime.FirstDay.Should().Be(lastMonth.AddDays(4).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Counting the months runs in the background and may be behind, or switched off: a month nothing
    /// has been kept of yet is counted from its reports, so the record is never behind the periods
    /// read from reports alone.
    /// </summary>
    [Fact]
    public async Task A_Month_Nothing_Has_Been_Kept_Of_Yet_Is_Answered_From_Its_Reports()
    {
        var (site, calendar, lastMonth) = await TwoMonthsReadAsync();

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var allTime = await KeptRecord.GetAsync<AllTimeResponse>(browser, $"/api/sites/{site.Id}/all-time");
        var series = await KeptRecord.GetAsync<AllTimeSeriesResponse>(
            browser,
            $"/api/sites/{site.Id}/all-time/series?metric=pageviews&granularity=month");

        (await KeptRecord.RowsAsync(stack, site.Id)).Should().BeEmpty();
        allTime.PageViews.Should().Be(3);
        series.Points.Select(point => point.BucketStart)
            .Should().Equal(calendar.MidnightOn(lastMonth), calendar.MidnightOn(lastMonth.AddMonths(1)));
        series.Points.Select(point => point.Value).Should().Equal(2, 1);
    }

    /// <summary>
    /// The record of a website measured only this year is this year: all of it and the period named
    /// after the year are one figure, whether or not anything has been kept yet.
    /// </summary>
    [Fact]
    public async Task All_Time_Is_Never_Less_Than_This_Year_For_A_Website_Measured_Only_This_Year()
    {
        var calendar = new ZoneCalendar(Kolkata);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var now = await KeptRecord.NowInsideTheMonthAsync(stack, calendar);
        var today = calendar.DateOf(now);
        var running = calendar.MidnightOn(ZoneCalendar.FirstOf(today));
        var at = running > now.AddMinutes(-5) ? running : now.AddMinutes(-5);

        await KeptRecord.WriteAsync(
            stack,
            [KeptRecord.Page(site.Id, at, "/", "reader"), KeptRecord.Page(site.Id, at.AddSeconds(1), "/about", "reader")]);
        await KeptRecord.CountAsync(stack, site);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var allTime = await KeptRecord.GetAsync<AllTimeResponse>(browser, $"/api/sites/{site.Id}/all-time");
        var thisYear = await OverviewAsync(browser, site.Id, calendar.MidnightOn(new DateOnly(today.Year, 1, 1)), allTime.To);

        allTime.PageViews.Should().Be(thisYear.PageViews).And.Be(2);
        allTime.Visitors.Should().Be(thisYear.Visitors);
    }

    [Fact]
    public async Task All_Time_By_Year_Adds_Up_Its_Months()
    {
        var (site, calendar, lastMonth) = await TwoMonthsReadAsync();

        await KeptRecord.CountAsync(stack, site);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var years = await KeptRecord.GetAsync<AllTimeSeriesResponse>(
            browser,
            $"/api/sites/{site.Id}/all-time/series?metric=pageviews&granularity=year");

        var expected = new[] { lastMonth, lastMonth.AddMonths(1) }
            .Select(month => month.Year)
            .Distinct()
            .Select(year => calendar.MidnightOn(new DateOnly(year, 1, 1)));

        years.Granularity.Should().Be("year");
        years.Points.Select(point => point.BucketStart).Should().Equal(expected);
        years.Points.Sum(point => point.Value).Should().Be(3);
    }

    [Fact]
    public async Task All_Time_Of_People_Counts_The_People_Alone()
    {
        var (site, calendar, lastMonth) = await TwoMonthsReadAsync();
        var scraper = $"scraper-{site.Id:n}";
        var afternoon = calendar.MidnightOn(lastMonth.AddDays(6)).AddHours(15);

        await KeptRecord.WriteAsync(
            stack,
            [.. Enumerable.Range(0, 4).Select(page => KeptRecord.Page(site.Id, afternoon.AddSeconds(page), $"/archive/{page}", scraper))]);
        await KeptRecord.JudgedAsync(stack, site.Id, scraper, afternoon, afternoon.AddSeconds(3), TrafficCategory.ContentScraper);
        await KeptRecord.CountAsync(stack, site);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var everybody = await KeptRecord.GetAsync<AllTimeResponse>(browser, $"/api/sites/{site.Id}/all-time");
        var people = await KeptRecord.GetAsync<AllTimeResponse>(browser, $"/api/sites/{site.Id}/all-time?only=people");

        everybody.PageViews.Should().Be(7);
        everybody.Visitors.Should().Be(3);
        people.PageViews.Should().Be(3);
        people.Visitors.Should().Be(2);

        (await ValuesAsync(browser, site.Id, "pageviews", string.Empty)).Should().Equal(6, 1);
        (await ValuesAsync(browser, site.Id, "pageviews", "&only=people")).Should().Equal(2, 1);
        (await ValuesAsync(browser, site.Id, "visitors", string.Empty)).Should().Equal(2, 1);
        (await ValuesAsync(browser, site.Id, "visitors", "&only=people")).Should().Equal(1, 1);

        // Judging never started on this website — its verdicts were written straight into the store —
        // so last month was kept as judged only to its first instant, and its people are complete to
        // there. An answer about everybody is complete to the moment it was read.
        people.CompleteTo.Should().Be(calendar.MidnightOn(lastMonth));
        everybody.CompleteTo.Should().Be(everybody.To);
    }

    [Fact]
    public async Task Who_Came_All_Time_Lines_Every_Conclusion_Up_On_One_List_Of_Months()
    {
        var (site, calendar, lastMonth) = await TwoMonthsReadAsync();

        await KeptRecord.CountAsync(stack, site);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var traffic = await KeptRecord.GetAsync<AllTimeTrafficSeriesResponse>(
            browser,
            $"/api/sites/{site.Id}/all-time/traffic/series?granularity=month");

        traffic.Buckets.Should().Equal(calendar.MidnightOn(lastMonth), calendar.MidnightOn(lastMonth.AddMonths(1)));
        traffic.Groups.Should().ContainSingle().Which.Category.Should().Be("likely-human");
        traffic.Groups[0].Sessions.Should().Equal(1, 1);
        traffic.Groups[0].PageViews.Should().Equal(1, 1);
    }

    /// <summary>
    /// A month that was never kept, and whose reports may already have begun to go, has no figure: it
    /// is said to be missing, never counted as nothing — and the months either side of it still add up.
    /// </summary>
    [Fact]
    public async Task A_Month_That_Was_Never_Kept_And_Can_No_Longer_Be_Counted_Is_Missing()
    {
        var calendar = new ZoneCalendar(Kolkata);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var running = ZoneCalendar.FirstOf(calendar.DateOf(await KeptRecord.NowInsideTheMonthAsync(stack, calendar)));
        var longAgo = running.AddMonths(-15);

        await KeptRecord.KeepAsync(stack, KeptRecord.Counted(site, calendar, longAgo, 40));

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var allTime = await KeptRecord.GetAsync<AllTimeResponse>(browser, $"/api/sites/{site.Id}/all-time");
        var series = await KeptRecord.GetAsync<AllTimeSeriesResponse>(
            browser,
            $"/api/sites/{site.Id}/all-time/series?metric=pageviews&granularity=month");

        allTime.PageViews.Should().Be(40);
        allTime.Visitors.Should().Be(20);
        allTime.Missing.Should().Contain(longAgo.AddMonths(1).ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture));
        allTime.Missing.Should().NotContain(longAgo.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture));

        series.Points.Should().HaveCount(16);
        series.Points[0].Value.Should().Be(40);
        series.Points[1].Value.Should().BeNull();
        series.Points[^1].Value.Should().Be(0);
        series.Points.Count(point => point.Value is null).Should().Be(allTime.Missing.Count);
        series.Missing.Should().Equal(allTime.Missing);

        var visitors = await KeptRecord.GetAsync<AllTimeSeriesResponse>(
            browser,
            $"/api/sites/{site.Id}/all-time/series?metric=visitors&granularity=month");

        visitors.Points[0].Value.Should().Be(20);
        visitors.Points[1].Value.Should().BeNull();
        visitors.Missing.Should().Equal(allTime.Missing);

        var traffic = await KeptRecord.GetAsync<AllTimeTrafficSeriesResponse>(
            browser,
            $"/api/sites/{site.Id}/all-time/traffic/series?granularity=month");

        traffic.Missing.Should().Equal(allTime.Missing);
        traffic.Groups.Should().ContainSingle().Which.Sessions[1].Should().BeNull();
        traffic.Groups[0].Sessions[0].Should().Be(40);
    }

    [Fact]
    public async Task All_Time_Before_Anything_Was_Measured_Says_So()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var allTime = await KeptRecord.GetAsync<AllTimeResponse>(browser, $"/api/sites/{site.Id}/all-time");
        var series = await KeptRecord.GetAsync<AllTimeSeriesResponse>(
            browser,
            $"/api/sites/{site.Id}/all-time/series?metric=visitors&granularity=month");

        allTime.From.Should().BeNull();
        allTime.FirstDay.Should().BeNull();
        allTime.PageViews.Should().Be(0);
        allTime.Missing.Should().BeEmpty();
        series.Points.Should().BeEmpty();
        series.From.Should().Be(series.To);
        series.Missing.Should().BeEmpty();
    }

    /// <summary>
    /// A month kept before its visits were all judged, and too old to be counted again, has figures
    /// for everybody but none for the people in it. The people's picture names it apart from the
    /// months never counted; everybody's draws it and names only those.
    /// </summary>
    [Fact]
    public async Task The_Peoples_Picture_Names_A_Month_Kept_Before_Its_Visits_Were_All_Judged()
    {
        var calendar = new ZoneCalendar(Kolkata);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var running = ZoneCalendar.FirstOf(calendar.DateOf(await KeptRecord.NowInsideTheMonthAsync(stack, calendar)));
        var longAgo = running.AddMonths(-15);
        var counted = KeptRecord.Counted(site, calendar, longAgo, 40);

        await KeptRecord.KeepAsync(stack, counted with { JudgedTo = counted.Month.Window.From });

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var everybody = await KeptRecord.GetAsync<AllTimeSeriesResponse>(
            browser,
            $"/api/sites/{site.Id}/all-time/series?metric=pageviews&granularity=month");
        var people = await KeptRecord.GetAsync<AllTimeSeriesResponse>(
            browser,
            $"/api/sites/{site.Id}/all-time/series?metric=pageviews&granularity=month&only=people");
        var month = longAgo.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);

        everybody.Points[0].Value.Should().Be(40);
        everybody.Missing.Should().NotContain(month).And.NotBeEmpty();
        everybody.Unjudged.Should().BeEmpty();

        people.Points[0].Value.Should().BeNull();
        people.Missing.Should().Equal(everybody.Missing);
        people.Unjudged.Should().Equal(month);

        var traffic = await KeptRecord.GetAsync<AllTimeTrafficSeriesResponse>(
            browser,
            $"/api/sites/{site.Id}/all-time/traffic/series?granularity=month");
        var peoplesTotals = await KeptRecord.GetAsync<AllTimeResponse>(browser, $"/api/sites/{site.Id}/all-time?only=people");

        traffic.Missing.Should().Equal(everybody.Missing);
        traffic.Unjudged.Should().Equal(month);
        peoplesTotals.Missing.Should().Equal(everybody.Missing);
        peoplesTotals.Unjudged.Should().Equal(month);
    }

    /// <summary>
    /// The first day still held in full is the first midnight where the website is a whole visit
    /// after the oldest report kept — the same day the dashboard stops letting a period reach back to.
    /// </summary>
    [Fact]
    public async Task All_Time_Says_How_Far_Its_Detail_Reaches()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(Kolkata);
        var before = Retention.DetailFrom(KeptRecord.Now(stack), zone);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var allTime = await KeptRecord.GetAsync<AllTimeResponse>(browser, $"/api/sites/{site.Id}/all-time");

        allTime.DetailFrom.Should().BeOneOf(before, Retention.DetailFrom(KeptRecord.Now(stack), zone));
        new ZoneCalendar(Kolkata).IsMidnight(allTime.DetailFrom).Should().BeTrue();
    }

    [Theory]
    [InlineData("/all-time/series?metric=pageviews&granularity=day")]
    [InlineData("/all-time/series?metric=pageviews&granularity=week")]
    [InlineData("/all-time/traffic/series?granularity=hour")]
    public async Task All_Time_Refuses_Buckets_Smaller_Than_A_Month(string question)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var response = await browser.GetAsync($"/api/sites/{site.Id}{question}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var refusal = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation.Token));
        refusal.RootElement.GetProperty("detail").GetString()
            .Should().Be("Ask for buckets of a month or a year.");
    }

    [Theory]
    [InlineData("/all-time")]
    [InlineData("/all-time/series?metric=pageviews&granularity=month")]
    [InlineData("/all-time/traffic/series?granularity=month")]
    public async Task All_Time_Of_A_Website_Somebody_Cannot_See_Is_Not_Found(string question)
    {
        var mine = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var theirs = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, mine.Id);
        var response = await browser.GetAsync($"/api/sites/{theirs.Id}{question}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Two pages last month, on the fifth, and one this month, all read by one person and judged a
    /// person.
    /// </summary>
    private async Task<(Site Site, ZoneCalendar Calendar, DateOnly LastMonth)> TwoMonthsReadAsync()
    {
        var calendar = new ZoneCalendar(Kolkata);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var now = await KeptRecord.NowInsideTheMonthAsync(stack, calendar);
        var running = ZoneCalendar.FirstOf(calendar.DateOf(now));
        var lastMonth = running.AddMonths(-1);
        var reader = $"reader-{site.Id:n}";
        var then = calendar.MidnightOn(lastMonth.AddDays(4)).AddHours(11);
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

        return (site, calendar, lastMonth);
    }

    /// <summary>One measure of all time, month by month, as the dashboard asks for it.</summary>
    private static async Task<long?[]> ValuesAsync(Browser browser, Guid siteId, string metric, string only)
    {
        var series = await KeptRecord.GetAsync<AllTimeSeriesResponse>(
            browser,
            $"/api/sites/{siteId}/all-time/series?metric={metric}&granularity=month{only}");

        return [.. series.Points.Select(point => point.Value)];
    }

    private static Task<OverviewResponse> OverviewAsync(Browser browser, Guid siteId, DateTimeOffset from, DateTimeOffset to) =>
        KeptRecord.GetAsync<OverviewResponse>(
            browser,
            $"/api/sites/{siteId}/overview?from={KeptRecord.Moment(from)}&to={KeptRecord.Moment(to)}");

    private static string Domain() => $"all-time-{Guid.NewGuid():n}.example";
}
