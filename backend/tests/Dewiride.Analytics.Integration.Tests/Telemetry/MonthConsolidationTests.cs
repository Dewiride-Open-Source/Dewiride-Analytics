using System.Collections;
using System.Globalization;
using System.Net;
using ClickHouse.Driver;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Api.Startup;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Consolidation;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;
using Dewiride.Analytics.Infrastructure.Sites;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Integration.Tests.Telemetry;

/// <summary>
/// Proves that a kept month is the dashboard's own figure for that month, however it is cut.
/// </summary>
/// <remarks>
/// <para>
/// A month is kept so that it can be read long after its reports are gone, at which point nothing
/// can check it any more. So it is checked here, while they are not: each kept figure against the
/// same month read the way the dashboard reads it, through the dashboard's own addresses — the
/// overview of the month, its daily and hourly series, and who came.
/// </para>
/// <para>
/// Written in Kolkata, the zone most of the product's websites are read in, where a month begins at
/// 18:30 the evening before on the clock reports are stamped by — and, where it is a month's edges
/// that are tested, in Kathmandu and London too, and across a change from one zone to another.
/// </para>
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class MonthConsolidationTests(AnalyticsStackFixture stack)
{
    private const string Kolkata = "Asia/Kolkata";
    private const string London = "Europe/London";

    /// <summary>
    /// How long before the month after the freeze line can no longer be counted no test that needs
    /// it countable begins, comfortably more than any one test takes.
    /// </summary>
    private static readonly TimeSpan FreezeMargin = TimeSpan.FromMinutes(2);

    /// <summary>How long a test waits for a moment it has arranged before calling it lost.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_Kept_Month_Is_The_Overview_Of_That_Month(bool peopleOnly)
    {
        var (site, calendar, month) = await LastMonthReadAndScrapedAsync();

        await KeptRecord.CountAsync(stack, site);

        var kept = (await KeptRecord.RowsAsync(stack, site.Id)).Should().ContainSingle().Subject;
        var prefix = peopleOnly ? "people_" : string.Empty;

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var overview = await KeptRecord.GetAsync<OverviewResponse>(
            browser,
            $"/api/sites/{site.Id}/overview?{Span(calendar, month)}{(peopleOnly ? "&only=people" : string.Empty)}");

        overview.PageViews.Should().BePositive();
        Long(kept[$"{prefix}page_views"]).Should().Be(overview.PageViews);
        Long(kept[$"{prefix}visitors"]).Should().Be(overview.Visitors);
    }

    /// <summary>
    /// The busiest day is the tallest bar of the month's daily series, and the weekdays and hours
    /// are that series and the hourly one folded — so the figures a reader is shown about when
    /// people read are the bars they could have counted themselves while the month was held.
    /// </summary>
    [Fact]
    public async Task A_Kept_Months_Busiest_Day_Weekdays_And_Hours_Are_Its_Daily_And_Hourly_Series_Folded()
    {
        var (site, calendar, month) = await LastMonthReadAndScrapedAsync();

        await KeptRecord.CountAsync(stack, site);

        var kept = (await KeptRecord.RowsAsync(stack, site.Id)).Should().ContainSingle().Subject;

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var daily = await KeptRecord.GetAsync<SeriesResponse>(
            browser,
            $"/api/sites/{site.Id}/series?metric=pageviews&granularity=day&{Span(calendar, month)}");
        var hourly = await KeptRecord.GetAsync<SeriesResponse>(
            browser,
            $"/api/sites/{site.Id}/series?metric=pageviews&granularity=hour&{Span(calendar, month)}");

        var busiest = daily.Points.MaxBy(point => point.Value);

        Day(kept["best_day"]).Should().Be(calendar.DateOf(busiest.BucketStart)).And.Be(month.AddDays(2));
        Long(kept["best_day_page_views"]).Should().Be(busiest.Value).And.Be(7);

        Longs(kept["page_views_by_weekday"]).Should().Equal(
            Enumerable.Range(0, 7).Select(weekday => daily.Points
                .Where(point => ((int)calendar.DateOf(point.BucketStart).DayOfWeek + 6) % 7 == weekday)
                .Sum(point => point.Value)));
        Longs(kept["page_views_by_hour"]).Should().Equal(
            Enumerable.Range(0, 24).Select(hour => hourly.Points
                .Where(point => LocalHour(calendar, point.BucketStart) == hour)
                .Sum(point => point.Value)));

        Longs(kept["page_views_by_hour"])[10].Should().Be(2);
        Longs(kept["page_views_by_hour"])[15].Should().Be(5);
        Longs(kept["page_views_by_hour"])[22].Should().Be(1);
    }

    /// <summary>
    /// Asked about people, the busiest day is counted from the people alone: the scraper's five pages
    /// on the third leave the reader's two.
    /// </summary>
    [Fact]
    public async Task A_Kept_Months_Busiest_Day_For_People_Is_Counted_From_The_People_Alone()
    {
        var (site, _, month) = await LastMonthReadAndScrapedAsync();

        await KeptRecord.CountAsync(stack, site);

        var kept = (await KeptRecord.RowsAsync(stack, site.Id)).Should().ContainSingle().Subject;

        Day(kept["people_best_day"]).Should().Be(month.AddDays(2));
        Long(kept["people_best_day_page_views"]).Should().Be(2);
        Longs(kept["people_page_views_by_hour"]).Sum().Should().Be(3);
    }

    /// <summary>
    /// Of two days as busy, the earlier is the busiest, for everybody and for the people alone —
    /// here with the later day's reports written first. A busiest day kept for a month outlives the
    /// reports that could correct it, so it cannot depend on the order the store hands days back in.
    /// </summary>
    [Fact]
    public async Task A_Kept_Months_Busiest_Day_Is_The_Earliest_Of_Two_As_Busy()
    {
        var calendar = new ZoneCalendar(Kolkata);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var now = await KeptRecord.NowInsideTheMonthAsync(stack, calendar);
        var month = ZoneCalendar.FirstOf(calendar.DateOf(now)).AddMonths(-1);
        var reader = $"reader-{site.Id:n}";
        var later = calendar.MidnightOn(month.AddDays(8)).AddHours(10);
        var earlier = calendar.MidnightOn(month.AddDays(3)).AddHours(10);

        await KeptRecord.WriteAsync(stack, ThreePages(site.Id, later, reader));
        await KeptRecord.WriteAsync(stack, ThreePages(site.Id, earlier, reader));
        await KeptRecord.JudgedAsync(stack, site.Id, reader, later, later.AddMinutes(2), TrafficCategory.LikelyHuman);
        await KeptRecord.JudgedAsync(stack, site.Id, reader, earlier, earlier.AddMinutes(2), TrafficCategory.LikelyHuman);

        await KeptRecord.CountAsync(stack, site);

        var kept = (await KeptRecord.RowsAsync(stack, site.Id)).Should().ContainSingle().Subject;

        Day(kept["best_day"]).Should().Be(month.AddDays(3));
        Long(kept["best_day_page_views"]).Should().Be(3);
        Day(kept["people_best_day"]).Should().Be(month.AddDays(3));
        Long(kept["people_best_day_page_views"]).Should().Be(3);
    }

    [Fact]
    public async Task A_Kept_Months_Verdicts_Are_Who_Came_That_Month()
    {
        var (site, calendar, month) = await LastMonthReadAndScrapedAsync();

        await KeptRecord.CountAsync(stack, site);

        var kept = (await KeptRecord.RowsAsync(stack, site.Id)).Should().ContainSingle().Subject;

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var traffic = await KeptRecord.GetAsync<TrafficResponse>(
            browser,
            $"/api/sites/{site.Id}/traffic?{Span(calendar, month)}");

        var categories = ((IEnumerable)kept["verdict_categories"]!).Cast<string>().ToArray();
        var visits = Longs(kept["verdict_visits"]);
        var pageViews = Longs(kept["verdict_page_views"]);

        categories.Should().BeEquivalentTo("LikelyHuman", "ContentScraper");

        var shown = traffic.Groups
            .GroupBy(group => group.Category)
            .ToDictionary(group => group.Key, group => (group.Sum(row => row.Sessions), group.Sum(row => row.PageViews)));

        shown["likely-human"].Should().Be((visits[Array.IndexOf(categories, "LikelyHuman")], pageViews[Array.IndexOf(categories, "LikelyHuman")]));
        shown["content-scraper"].Should().Be((visits[Array.IndexOf(categories, "ContentScraper")], pageViews[Array.IndexOf(categories, "ContentScraper")]));
    }

    /// <summary>
    /// A month is kept on the website's calendar, which is where the month's bar on the dashboard
    /// begins: in Kolkata at 18:30 the evening before on the clock reports are stamped by, in
    /// Kathmandu at 18:15, and in London at midnight or an hour before it — where a month across a
    /// change of the clocks begins on one offset and ends on the other, so its end is not its
    /// beginning and a whole number of days.
    /// </summary>
    /// <param name="timeZoneId">The website's zone.</param>
    /// <param name="acrossAClockChange">Whether the month kept is one the clocks changed in.</param>
    [Theory]
    [InlineData(Kolkata, false)]
    [InlineData("Asia/Kathmandu", false)]
    [InlineData(London, true)]
    public async Task A_Kept_Month_Begins_Where_The_Pictures_Month_Begins(string timeZoneId, bool acrossAClockChange)
    {
        var (site, calendar, month) = await MonthReadAndScrapedAsync(timeZoneId, acrossAClockChange);

        await KeptRecord.CountAsync(stack, site);

        var kept = Of(await KeptRecord.RowsAsync(stack, site.Id), month);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var monthly = await KeptRecord.GetAsync<SeriesResponse>(
            browser,
            $"/api/sites/{site.Id}/series?metric=pageviews&granularity=month&{Span(calendar, month)}");

        Instant(kept["begins_at"]).Should().Be(monthly.Points.Should().ContainSingle().Subject.BucketStart);
        Instant(kept["begins_at"]).Should().Be(calendar.MidnightOn(month));
        Instant(kept["ends_at"]).Should().Be(calendar.MidnightOn(month.AddMonths(1)));
        ((string?)kept["time_zone"]).Should().Be(timeZoneId);
    }

    /// <summary>
    /// Every finished month still held is kept, oldest first, a run's allowance at a time — the rest
    /// on the runs after — a month nobody visited as a month of noughts rather than a gap, and the
    /// first day kept with the month that holds it, which is what an average per day is later
    /// divided by.
    /// </summary>
    [Fact]
    public async Task Every_Finished_Month_Still_Held_Is_Kept_Oldest_First_A_Runs_Allowance_At_A_Time()
    {
        var allowance = stack.Services.GetRequiredService<IOptions<ConsolidationOptions>>().Value.MonthsPerRun;
        var calendar = new ZoneCalendar(Kolkata);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var running = ZoneCalendar.FirstOf(calendar.DateOf(await KeptRecord.NowInsideTheMonthAsync(stack, calendar)));
        var first = running.AddMonths(-(allowance + 1));
        IEnumerable<DateOnly> finished = [.. Enumerable.Range(0, allowance + 1).Select(first.AddMonths)];
        IEnumerable<long> pageViews = [1, .. Enumerable.Repeat(0L, allowance - 1), 1];
        IEnumerable<DateOnly?> firstDays = [first.AddDays(4), .. Enumerable.Repeat<DateOnly?>(null, allowance)];
        var visitor = $"reader-{site.Id:n}";

        await KeptRecord.WriteAsync(
            stack,
            [
                KeptRecord.Page(site.Id, calendar.MidnightOn(first.AddDays(4)).AddHours(9), "/", visitor),
                KeptRecord.Page(site.Id, calendar.MidnightOn(running.AddMonths(-1).AddDays(1)).AddHours(9), "/", visitor),
            ]);

        await KeptRecord.CountAsync(stack, site);

        (await KeptRecord.RowsAsync(stack, site.Id)).Select(row => Day(row["month"]))
            .Should().Equal(finished.Take(allowance), "a run keeps its allowance, oldest first");

        await KeptRecord.CountAsync(stack, site);

        var kept = await KeptRecord.RowsAsync(stack, site.Id);

        kept.Select(row => Day(row["month"])).Should().Equal(finished);
        kept.Select(row => Long(row["page_views"])).Should().Equal(pageViews);
        kept.Select(row => row["first_day"] is null ? (DateOnly?)null : Day(row["first_day"]))
            .Should().Equal(firstDays);
        Long(kept[0]["days_measured"]).Should().Be(DateTime.DaysInMonth(first.Year, first.Month) - 4);
        Long(kept[1]["days_measured"]).Should().Be(DateTime.DaysInMonth(first.AddMonths(1).Year, first.AddMonths(1).Month));
    }

    [Fact]
    public async Task The_Month_Running_Now_Is_Never_Kept()
    {
        var calendar = new ZoneCalendar(Kolkata);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var now = await KeptRecord.NowInsideTheMonthAsync(stack, calendar);

        await KeptRecord.WriteAsync(stack, [KeptRecord.Page(site.Id, Later(calendar.MidnightOn(ZoneCalendar.FirstOf(calendar.DateOf(now))), now.AddMinutes(-5)), "/", "reader")]);

        await KeptRecord.CountAsync(stack, site);

        (await KeptRecord.RowsAsync(stack, site.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Nothing_Is_Kept_For_A_Website_With_No_Reports()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);

        await KeptRecord.CountAsync(stack, site);

        (await KeptRecord.WrittenAsync(stack, site.Id)).Should().Be(0);
    }

    /// <summary>
    /// A month kept whole, judged whole, by the arithmetic and in the zone in force now is left
    /// alone: counting it again would read a month of reports to write the same row.
    /// </summary>
    [Fact]
    public async Task A_Month_Already_Kept_Is_Not_Counted_Again_Until_Something_About_It_Changes()
    {
        var (site, _, _) = await LastMonthReadAndScrapedAsync();

        await KeptRecord.CountAsync(stack, site);
        await KeptRecord.CountAsync(stack, site);

        (await KeptRecord.WrittenAsync(stack, site.Id)).Should().Be(1);
    }

    /// <summary>
    /// A month kept while judging had stalled inside it is counted once, not on every run for as long
    /// as judging stays there. Judging records how far it reached to the microsecond and a kept month
    /// holds it to the millisecond, so a stall part-way through a millisecond would otherwise always
    /// look a fraction further on than the month says it had got.
    /// </summary>
    [Fact]
    public async Task A_Month_Kept_Where_Judging_Stalled_Is_Not_Counted_Again_While_It_Stays_There()
    {
        var (site, calendar, month) = await LastMonthReadAndScrapedAsync();
        var stalled = calendar.MidnightOn(month.AddDays(19))
            .AddHours(23).AddMinutes(40).AddSeconds(12).AddMilliseconds(345).AddTicks(6780);

        await using (var scope = stack.Services.CreateAsyncScope())
        {
            var progress = scope.ServiceProvider.GetRequiredService<IClassificationProgressStore>();

            await progress.ResumeFromAsync(site.Id, RulesetVersion.Current, calendar.MidnightOn(month), Cancellation.Token);
            (await progress.AdvanceAsync(
                    site.Id,
                    RulesetVersion.Current,
                    new ClassificationBookmark(stalled, stalled),
                    Cancellation.Token))
                .Should().BeTrue();
        }

        await KeptRecord.CountAsync(stack, site);
        await KeptRecord.CountAsync(stack, site);

        (await KeptRecord.WrittenAsync(stack, site.Id)).Should().Be(1);
        Instant((await KeptRecord.RowsAsync(stack, site.Id)).Should().ContainSingle().Subject["judged_to"])
            .Should().Be(stalled.AddTicks(-(stalled.UtcTicks % TimeSpan.TicksPerMillisecond)));
    }

    /// <summary>
    /// A change of zone counts again, on the new calendar, every month whose reports are still held,
    /// and leaves a month that can no longer be counted as it was kept, on the old one — read back
    /// on the new calendar, and still counted. Moved east, from London to Kolkata, the evening before
    /// the first month still held begins in Kolkata and has not ended in London: it is inside both
    /// the month frozen on London's calendar and the one counted again on Kolkata's, so All time
    /// counts it twice.
    /// </summary>
    [Fact]
    public async Task A_Change_Of_Zone_Counts_Again_The_Months_Still_Held_And_Keeps_The_Frozen_Ones()
    {
        var london = new ZoneCalendar(London);
        var kolkata = new ZoneCalendar(Kolkata);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: London);
        var (frozen, held) = await FreezeLineAsync(kolkata);
        var between = kolkata.MidnightOn(held).AddHours(2);

        await KeptRecord.KeepAsync(stack, KeptRecord.Counted(site, london, frozen, 40));
        await KeptRecord.WriteAsync(stack, [KeptRecord.Page(site.Id, between, "/", $"reader-{site.Id:n}")]);

        await KeptRecord.CountAsync(stack, site);

        var before = Of(await KeptRecord.RowsAsync(stack, site.Id), held);

        ((string?)before["time_zone"]).Should().Be(London);
        Long(before["page_views"]).Should().Be(0, "on London's calendar the page is on the last evening of the month before");

        await MoveAsync(site, Kolkata);
        await KeptRecord.CountAsync(stack, site);

        var rows = await KeptRecord.RowsAsync(stack, site.Id);
        var still = Of(rows, frozen);
        var again = Of(rows, held);

        ((string?)still["time_zone"]).Should().Be(London);
        Instant(still["begins_at"]).Should().Be(london.MidnightOn(frozen));
        Instant(still["ends_at"]).Should().Be(london.MidnightOn(held)).And.BeAfter(between);
        Long(still["page_views"]).Should().Be(40);

        ((string?)again["time_zone"]).Should().Be(Kolkata);
        Instant(again["begins_at"]).Should().Be(kolkata.MidnightOn(held)).And.BeBefore(between);
        Instant(again["ends_at"]).Should().Be(kolkata.MidnightOn(held.AddMonths(1)));
        Long(again["page_views"]).Should().Be(1);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var allTime = await KeptRecord.GetAsync<AllTimeResponse>(browser, $"/api/sites/{site.Id}/all-time");
        var series = await KeptRecord.GetAsync<AllTimeSeriesResponse>(
            browser,
            $"/api/sites/{site.Id}/all-time/series?metric=pageviews&granularity=month");

        allTime.PageViews.Should().Be(41);
        allTime.Missing.Should().BeEmpty();
        series.Points.Should().AllSatisfy(point =>
            point.BucketStart.Should().Be(kolkata.MidnightOn(ZoneCalendar.FirstOf(kolkata.DateOf(point.BucketStart)))));
        series.Points.Should().ContainSingle(point => point.BucketStart == kolkata.MidnightOn(frozen))
            .Which.Value.Should().Be(40);
        series.Points.Should().ContainSingle(point => point.BucketStart == kolkata.MidnightOn(held))
            .Which.Value.Should().Be(1);
    }

    /// <summary>
    /// A website removed while its months are being counted keeps none of them, wherever the count
    /// has reached when the removal begins — including the one moment a month written would outlive
    /// both: after the removal has deleted everything the website had, and before it has deleted the
    /// website. Nothing removes a kept month but the removal of its website, so a month written then
    /// would be kept for ever.
    /// </summary>
    [Fact]
    public async Task A_Website_Removed_While_Its_Months_Are_Being_Kept_Keeps_None_Of_Them()
    {
        var (site, _, _) = await LastMonthReadAndScrapedAsync();
        var staying = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var (created, owner) = await ControlPlaneSeed
            .AddAccountAsync(stack, SignedIn.Address(), Passwords.Acceptable);

        created.Succeeded.Should().BeTrue();
        await ControlPlaneSeed.GrantAsync(stack, site.Id, owner.Id, SiteRole.Owner);
        await ControlPlaneSeed.GrantAsync(stack, staying.Id, owner.Id, SiteRole.Owner);

        var removal = new HeldRemoval(site.Id);

        using var host = stack.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddScoped<SiteRoster>();
            services.AddScoped<ISiteRoster>(provider =>
                new WaitingRoster(provider.GetRequiredService<SiteRoster>(), removal));
            services.AddSingleton<ITelemetryPurge>(provider =>
                new HeldPurge(new ClickHouseTelemetryPurge(provider.GetRequiredService<IClickHouseClient>()), removal));
        }));
        var services = host.Services;

        var counting = Task.Run(() => KeptRecord.CountAsync(services, site), Cancellation.Token);
        await removal.Counted.Task.WaitAsync(Patience, Cancellation.Token);

        var removing = Task.Run(() => RemoveAsync(services, owner.Id, site.Id), Cancellation.Token);
        await removal.Purged.Task.WaitAsync(Patience, Cancellation.Token);

        // Long enough for a write that does not wait for the removal to have begun.
        await Task.WhenAny(removal.Writing.Task, Task.Delay(TimeSpan.FromSeconds(2), Cancellation.Token));
        removal.Released.SetResult();

        (await removing).Should().Be(SiteRemovalOutcome.Removed);
        await counting;

        removal.Writing.Task.IsCompleted.Should().BeFalse("the website was gone by the time its months could be written");
        (await KeptRecord.WrittenAsync(stack, site.Id)).Should().Be(0);
    }

    /// <summary>
    /// The engine runs the work that keeps months. Every host this suite starts switches it off, so a
    /// test counts only when it means to — which leaves this the one test that would notice the engine
    /// no longer ran it, and every month after that lost once its reports went.
    /// </summary>
    [Fact]
    public void The_Engine_Runs_The_Work_That_Keeps_Months()
    {
        stack.Services.GetServices<IHostedService>().OfType<ConsolidationWorker>().Should().ContainSingle();
    }

    /// <summary>
    /// Last month in Kolkata: a reader on the third and the eleventh, and a scraper on the third.
    /// </summary>
    private Task<(Site Site, ZoneCalendar Calendar, DateOnly Month)> LastMonthReadAndScrapedAsync() =>
        MonthReadAndScrapedAsync(Kolkata, acrossAClockChange: false);

    /// <summary>
    /// A finished month where a website is: a reader on the third and the eleventh, and a scraper on
    /// the third.
    /// </summary>
    /// <remarks>
    /// The reader reads two pages at 10:00 on the third and one at 22:30 on the eleventh, a week and
    /// a day later so the two land on different weekdays; the scraper takes five pages at 15:00 on
    /// the third. Both are judged, so the month has people and machinery in it.
    /// </remarks>
    /// <param name="timeZoneId">The website's zone.</param>
    /// <param name="acrossAClockChange">
    /// Whether the month is the latest the clocks changed in there, rather than last month.
    /// </param>
    private async Task<(Site Site, ZoneCalendar Calendar, DateOnly Month)> MonthReadAndScrapedAsync(
        string timeZoneId,
        bool acrossAClockChange)
    {
        var calendar = new ZoneCalendar(timeZoneId);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: timeZoneId);
        var now = await KeptRecord.NowInsideTheMonthAsync(stack, calendar);
        var month = acrossAClockChange
            ? calendar.LatestMonthBeginningOnANewOffset(now).AddMonths(-1)
            : ZoneCalendar.FirstOf(calendar.DateOf(now)).AddMonths(-1);

        var reader = $"reader-{site.Id:n}";
        var scraper = $"scraper-{site.Id:n}";
        var morning = calendar.MidnightOn(month.AddDays(2)).AddHours(10);
        var afternoon = calendar.MidnightOn(month.AddDays(2)).AddHours(15);
        var night = calendar.MidnightOn(month.AddDays(10)).AddHours(22).AddMinutes(30);

        await KeptRecord.WriteAsync(
            stack,
            [
                KeptRecord.Page(site.Id, morning, "/", reader),
                KeptRecord.Page(site.Id, morning.AddMinutes(2), "/pricing", reader),
                .. Enumerable.Range(0, 5).Select(page =>
                    KeptRecord.Page(site.Id, afternoon.AddSeconds(page), $"/archive/{page}", scraper)),
                KeptRecord.Page(site.Id, night, "/", reader),
            ]);

        await KeptRecord.JudgedAsync(stack, site.Id, reader, morning, morning.AddMinutes(2), TrafficCategory.LikelyHuman);
        await KeptRecord.JudgedAsync(stack, site.Id, scraper, afternoon, afternoon.AddSeconds(4), TrafficCategory.ContentScraper);
        await KeptRecord.JudgedAsync(stack, site.Id, reader, night, night, TrafficCategory.LikelyHuman);

        return (site, calendar, month);
    }

    /// <summary>
    /// The last month on a calendar that can no longer be counted, and the month after it, which can
    /// be for longer than a test takes.
    /// </summary>
    /// <param name="calendar">The calendar.</param>
    /// <returns>The two months.</returns>
    private async Task<(DateOnly Frozen, DateOnly Held)> FreezeLineAsync(ZoneCalendar calendar)
    {
        var now = KeptRecord.Now(stack);
        var held = ZoneCalendar.FirstOf(calendar.DateOf(now));

        while (now < Retention.CountableUntil(calendar.MidnightOn(held.AddMonths(-1))))
        {
            held = held.AddMonths(-1);
        }

        var closes = Retention.CountableUntil(calendar.MidnightOn(held));

        if (closes - now < FreezeMargin)
        {
            await Task.Delay(closes - now + TimeSpan.FromSeconds(1), Cancellation.Token);
            held = held.AddMonths(1);
        }

        return (held.AddMonths(-1), held);
    }

    /// <summary>Moves a website to another zone, as an editor would.</summary>
    /// <param name="site">The website, which is moved too.</param>
    /// <param name="timeZoneId">The zone.</param>
    private async Task MoveAsync(Site site, string timeZoneId)
    {
        using var editor = await SignedIn.AsAsync(stack, SiteRole.Editor, site.Id);
        var response = await editor.PutAsync(
            $"/api/sites/{site.Id}/settings",
            new UpdateSiteSettingsRequest { TimeZoneId = timeZoneId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        site.SetTimeZone(timeZoneId);
    }

    /// <summary>Asks a host's directory to remove a website, as its owner.</summary>
    private static async Task<SiteRemovalOutcome> RemoveAsync(IServiceProvider services, Guid ownerId, Guid siteId)
    {
        await using var scope = services.CreateAsyncScope();

        var removal = await scope.ServiceProvider
            .GetRequiredService<ISiteDirectory>()
            .RemoveAsync(ownerId, siteId, Cancellation.Token);

        return removal.Outcome;
    }

    /// <summary>Three pages a minute apart, the first at the moment given.</summary>
    private static RawEvent[] ThreePages(Guid siteId, DateTimeOffset from, string visitorKey) =>
        [.. Enumerable.Range(0, 3).Select(page => KeptRecord.Page(siteId, from.AddMinutes(page), $"/guide/{page}", visitorKey))];

    /// <summary>The newest row kept for one month.</summary>
    private static Dictionary<string, object?> Of(IEnumerable<Dictionary<string, object?>> rows, DateOnly month) =>
        rows.Should().ContainSingle(row => Day(row["month"]) == month).Subject;

    /// <summary>The query string naming a whole month as a period.</summary>
    private static string Span(ZoneCalendar calendar, DateOnly month) =>
        $"from={KeptRecord.Moment(calendar.MidnightOn(month))}&to={KeptRecord.Moment(calendar.MidnightOn(month.AddMonths(1)))}";

    private static int LocalHour(ZoneCalendar calendar, DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(calendar.TimeZoneId)).Hour;

    private static DateTimeOffset Later(DateTimeOffset one, DateTimeOffset other) => one > other ? one : other;

    private static DateOnly Day(object? value) => value switch
    {
        DateOnly day => day,
        DateTime moment => DateOnly.FromDateTime(moment),
        _ => throw new InvalidCastException($"Not a day: {value}"),
    };

    private static DateTimeOffset Instant(object? value) => value switch
    {
        DateTimeOffset instant => instant,
        DateTime moment => new DateTimeOffset(DateTime.SpecifyKind(moment, DateTimeKind.Utc)),
        _ => throw new InvalidCastException($"Not an instant: {value}"),
    };

    private static long[] Longs(object? values) =>
        [.. ((IEnumerable)values!).Cast<object>().Select(Long)];

    private static long Long(object? value) => Convert.ToInt64(value, CultureInfo.InvariantCulture);

    private static string Domain() => $"months-{Guid.NewGuid():n}.example";

    /// <summary>
    /// The moments a removal and a count of one website's months reach, and the one that lets the
    /// removal go on.
    /// </summary>
    /// <param name="siteId">The website.</param>
    private sealed class HeldRemoval(Guid siteId)
    {
        /// <summary>The website.</summary>
        public Guid SiteId { get; } = siteId;

        /// <summary>The count has its months, and is about to ask to write them.</summary>
        public TaskCompletionSource Counted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The removal has deleted everything the website had, and not yet the website.</summary>
        public TaskCompletionSource Purged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The count has begun writing its months.</summary>
        public TaskCompletionSource Writing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The removal may go on to delete the website.</summary>
        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// The roster, holding a count's write for one website until its removal has deleted everything
    /// the website had.
    /// </summary>
    /// <param name="roster">The roster.</param>
    /// <param name="removal">The moments to wait for and announce.</param>
    private sealed class WaitingRoster(SiteRoster roster, HeldRemoval removal) : ISiteRoster
    {
        /// <inheritdoc />
        public Task<IReadOnlyList<SiteRegistration>> ListAsync(CancellationToken cancellationToken) =>
            roster.ListAsync(cancellationToken);

        /// <inheritdoc />
        public async Task<bool> WhileItExistsAsync(
            Guid siteId,
            Func<CancellationToken, Task> work,
            CancellationToken cancellationToken)
        {
            if (siteId != removal.SiteId)
            {
                return await roster.WhileItExistsAsync(siteId, work, cancellationToken);
            }

            removal.Counted.TrySetResult();
            await removal.Purged.Task.WaitAsync(cancellationToken);

            return await roster.WhileItExistsAsync(
                siteId,
                token =>
                {
                    removal.Writing.TrySetResult();

                    return work(token);
                },
                cancellationToken);
        }
    }

    /// <summary>
    /// The purge, holding one website's removal once everything the website had is deleted, until
    /// the test lets it go on to delete the website.
    /// </summary>
    /// <param name="purge">The purge.</param>
    /// <param name="removal">The moments to announce and wait for.</param>
    private sealed class HeldPurge(ITelemetryPurge purge, HeldRemoval removal) : ITelemetryPurge
    {
        /// <inheritdoc />
        public async Task PurgeSiteAsync(Guid siteId, CancellationToken cancellationToken)
        {
            await purge.PurgeSiteAsync(siteId, cancellationToken);

            if (siteId == removal.SiteId)
            {
                removal.Purged.TrySetResult();
                await removal.Released.Task.WaitAsync(cancellationToken);
            }
        }
    }
}
