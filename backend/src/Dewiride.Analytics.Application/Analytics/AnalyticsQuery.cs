using Dewiride.Analytics.Application.Telemetry;

namespace Dewiride.Analytics.Application.Analytics;

/// <summary>
/// The complete vocabulary of questions that may be asked of the telemetry store.
/// </summary>
/// <remarks>
/// <para>
/// The constructor is <c>private protected</c>, so the cases live here and are added here. The
/// SQL compiler in the ClickHouse infrastructure project pattern-matches over them and builds
/// statements from a static identifier allow-list with every value bound as a parameter, which
/// means there is no code path anywhere that turns caller-supplied text into SQL. A question the
/// compiler has not been taught produces no statement at all: it throws.
/// </para>
/// <para>
/// That property carries more weight here than in an ordinary application, because this
/// product's own dataset is attacker-controlled text — user agents, referrers, and the URL
/// paths crawlers ask for. It is also what makes the natural-language layer safe to build
/// later: the model chooses <em>which</em> of these questions to ask and never how to
/// execute one.
/// </para>
/// </remarks>
public abstract record AnalyticsQuery
{
    private protected AnalyticsQuery(TimeRange range)
    {
        Range = range;
    }

    /// <summary>The window the question is asked over.</summary>
    public TimeRange Range { get; }
}

/// <summary>
/// Headline totals for a site over a window.
/// </summary>
/// <remarks>
/// Asked of everybody the window holds or of the people alone, by the same arithmetic over
/// fewer reports — so a total about people is a share of the total about everybody rather than a
/// second, differently-derived number.
/// </remarks>
/// <param name="Range">The window to summarise.</param>
public sealed record OverviewQuery(TimeRange Range) : AnalyticsQuery(Range)
{
    /// <summary>Who the totals are about.</summary>
    public Population Population { get; init; } = Population.Everybody;
}

/// <summary>
/// A single metric bucketed over time.
/// </summary>
/// <remarks>
/// Asked of people alone it is the same buckets over fewer reports, so the buckets add up to the
/// headline asked of the same people. The last stretch of a running period holds visits nothing
/// has judged yet, which the caller says beside the answer rather than in it.
/// </remarks>
/// <param name="Range">The window to cover.</param>
/// <param name="Granularity">Bucket size.</param>
/// <param name="Metric">Which metric to bucket.</param>
public sealed record TimeSeriesQuery(TimeRange Range, TimeGranularity Granularity, TimeSeriesMetric Metric)
    : AnalyticsQuery(Range)
{
    /// <summary>Who the series counts.</summary>
    public Population Population { get; init; } = Population.Everybody;
}

/// <summary>Bucket size for a time series.</summary>
public enum TimeGranularity
{
    /// <summary>One bucket per hour.</summary>
    Hour = 1,

    /// <summary>One bucket per day, in the site's configured time zone.</summary>
    Day = 2,
}

/// <summary>Which metric a time series reports.</summary>
public enum TimeSeriesMetric
{
    /// <summary>Number of page views.</summary>
    PageViews = 1,

    /// <summary>Number of distinct visitor keys observed.</summary>
    Visitors = 2,
}

/// <summary>
/// Who a question about a window is asked about.
/// </summary>
/// <remarks>
/// <para>
/// A population narrows the reports a question counts and never the arithmetic: the same
/// statement is asked, over the reports of fewer visits. So every figure asked of people is a
/// share of the figure asked of everybody, and a screen kept to people adds up.
/// </para>
/// <para>
/// "People" is a verdict. It is the visits the engine concluded were people, under the newest
/// ruleset to judge each one, and nothing else: a visit nothing has judged yet is not among them,
/// however like a person it looks, and neither is one the engine could not tell.
/// </para>
/// <para>
/// Only the questions answered from activity carry one. The questions answered from verdicts
/// already say what each visit was; the account of one visit is about that visit whoever made it;
/// and what is happening now is counted rather than judged.
/// </para>
/// </remarks>
public enum Population
{
    /// <summary>Every visit the window holds, judged or not.</summary>
    Everybody = 1,

    /// <summary>The visits the engine concluded were people.</summary>
    People = 2,
}

/// <summary>
/// One slice of the pages a site's traffic went to, busiest first.
/// </summary>
/// <remarks>
/// <para>
/// Counted as pages delivered rather than as reports received, on the same terms as
/// <see cref="OverviewQuery"/>, so a share taken against the headline total is a share of the
/// same arithmetic rather than of a second, differently-derived number. Asked of people alone, it
/// is the same arithmetic over the reports the verdicts on people cover, so a share is still a
/// share of the headline it sits under.
/// </para>
/// <para>
/// Every address the window holds is reachable by asking for successive slices. The ordering is
/// total — busiest first, and the address itself breaks a tie — so a page of results does not
/// shuffle beneath somebody moving through them.
/// </para>
/// </remarks>
public sealed record SitePagesQuery : AnalyticsQuery
{
    /// <summary>
    /// Most pages any one question may ask for.
    /// </summary>
    /// <remarks>
    /// A documentation site has thousands of addresses and every one of them is a group in the
    /// store. This bounds one answer, not the work behind it, which is why the window is bounded
    /// separately and why the whole list is reached a slice at a time rather than at once.
    /// </remarks>
    public const int MostPages = 100;

    /// <summary>Asks for a slice of the pages in a window.</summary>
    /// <param name="range">The window to count over.</param>
    /// <param name="limit">How many pages to return, at most <see cref="MostPages"/>.</param>
    /// <param name="offset">How many of the busiest pages to pass over first.</param>
    /// <exception cref="ArgumentOutOfRangeException">The limit is outside its bounds, or the offset is negative.</exception>
    public SitePagesQuery(TimeRange range, int limit, int offset = 0)
        : base(range)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MostPages);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        Limit = limit;
        Offset = offset;
    }

    /// <summary>How many pages to return.</summary>
    public int Limit { get; }

    /// <summary>How many of the busiest pages to pass over first.</summary>
    public int Offset { get; }

    /// <summary>Who the pages were delivered to.</summary>
    public Population Population { get; init; } = Population.Everybody;
}

/// <summary>
/// One slice of what a site's visitors operated, most pressed first.
/// </summary>
/// <remarks>
/// <para>
/// Answered from presses alone, with none of the reconciliation the page counts need. A press can
/// only be seen by something running in the visitor's own browser, so there is no second sighting
/// of it to recognise and nothing to fold together.
/// </para>
/// <para>
/// The ordering is total — most pressed first, then the name, then the kind — so successive slices
/// neither repeat a row nor skip one.
/// </para>
/// </remarks>
public sealed record SiteActionsQuery : AnalyticsQuery
{
    /// <summary>Most controls any one question may ask for.</summary>
    public const int MostControls = 100;

    /// <summary>Asks for a slice of what was operated in a window.</summary>
    /// <param name="range">The window to count over.</param>
    /// <param name="grouping">What the presses are gathered by.</param>
    /// <param name="limit">How many rows to return, at most <see cref="MostControls"/>.</param>
    /// <param name="offset">How many of the most pressed to pass over first.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The grouping is not one this product defines, the limit is outside its bounds, or the
    /// offset is negative.
    /// </exception>
    public SiteActionsQuery(TimeRange range, ActionGrouping grouping, int limit, int offset = 0)
        : base(range)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MostControls);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        if (!Enum.IsDefined(grouping))
        {
            throw new ArgumentOutOfRangeException(nameof(grouping));
        }

        Grouping = grouping;
        Limit = limit;
        Offset = offset;
    }

    /// <summary>What the presses are gathered by.</summary>
    public ActionGrouping Grouping { get; }

    /// <summary>How many rows to return.</summary>
    public int Limit { get; }

    /// <summary>How many of the most pressed to pass over first.</summary>
    public int Offset { get; }

    /// <summary>Who the presses are counted for.</summary>
    /// <remarks>
    /// A press is reported by the visitor's own browser under the browser's own key, which is the
    /// key a verdict names — so presses are kept by the verdict on the visit they were made in
    /// without any of the reconciliation the page counts need.
    /// </remarks>
    public Population Population { get; init; } = Population.Everybody;
}

/// <summary>
/// What a window's presses are gathered by.
/// </summary>
public enum ActionGrouping
{
    /// <summary>By the control itself: what it said, and what sort of thing it was.</summary>
    Control = 1,

    /// <summary>
    /// By the host a press led to, counting only the presses that led off the site. Where a press
    /// led on the site is answered by the pages themselves.
    /// </summary>
    Destination = 2,
}

/// <summary>
/// One slice of the places a site's traffic came from, busiest first.
/// </summary>
/// <remarks>
/// <para>
/// Counted per visitor rather than per page, because a place is a fact about people rather than
/// about pages: one reader in Pune who works through forty pages is one reader in Pune, and
/// ranking places by pages read would put whoever browses most at the top of a list that claims
/// to be about where an audience is. Counted per visitor within whoever the question is asked
/// about: a person is settled on one place, one source and one device on the same terms as
/// everybody is.
/// </para>
/// <para>
/// Read a slice at a time on the same terms as <see cref="SitePagesQuery"/>, with the same total
/// ordering, so successive slices neither repeat a place nor skip one.
/// </para>
/// </remarks>
public sealed record SiteLocationsQuery : AnalyticsQuery
{
    /// <summary>Most places any one question may ask for.</summary>
    public const int MostPlaces = 100;

    /// <summary>Asks for a slice of the places in a window.</summary>
    /// <param name="range">The window to count over.</param>
    /// <param name="grouping">Whether to group by country or by town.</param>
    /// <param name="limit">How many places to return, at most <see cref="MostPlaces"/>.</param>
    /// <param name="offset">How many of the busiest places to pass over first.</param>
    /// <exception cref="ArgumentOutOfRangeException">The limit is outside its bounds, or the offset is negative.</exception>
    public SiteLocationsQuery(TimeRange range, LocationGrouping grouping, int limit, int offset = 0)
        : base(range)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MostPlaces);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        Grouping = grouping;
        Limit = limit;
        Offset = offset;
    }

    /// <summary>What each row stands for.</summary>
    public LocationGrouping Grouping { get; }

    /// <summary>How many places to return.</summary>
    public int Limit { get; }

    /// <summary>How many of the busiest places to pass over first.</summary>
    public int Offset { get; }

    /// <summary>Whose places are counted.</summary>
    public Population Population { get; init; } = Population.Everybody;
}

/// <summary>What one row of a place list stands for.</summary>
public enum LocationGrouping
{
    /// <summary>One row per country.</summary>
    Country = 1,

    /// <summary>
    /// One row per town.
    /// </summary>
    /// <remarks>
    /// Towns are an estimate: address ranges are allocated to networks rather than to streets, so
    /// the answer is frequently the nearest sizeable town rather than where the visitor is.
    /// </remarks>
    Town = 2,

    /// <summary>
    /// One row per network the visitors came over.
    /// </summary>
    /// <remarks>
    /// The one grouping here that answers a question about authenticity rather than about an
    /// audience. A country tells a publisher who is reading them; a network tells them whether
    /// those readers are people at all, because a hundred readers arriving from one company's
    /// datacentre are not a hundred readers. Countries are the answer that hides this: a rented
    /// server in Singapore reports Singapore, truthfully, and reads as a Singaporean audience.
    /// </remarks>
    Network = 3,
}

/// <summary>
/// Where a site's visitors came from before they arrived.
/// </summary>
/// <remarks>
/// <para>
/// Counted per visitor rather than per page, on the same terms as <see cref="SiteLocationsQuery"/>
/// and for the same reason: where somebody came from is a fact about their arrival, and counting
/// it once per page would rank sources by how much the people they sent went on to read. Counted
/// within whoever the question is asked about, so a person is settled on one source on the same
/// terms as everybody is.
/// </para>
/// <para>
/// One visitor is settled on one source. Only the first page of a visit carries an address from
/// somewhere else — every page after it was reached from the site itself — so the site's own
/// address is excluded and whatever remains is the source for everything that visitor did. A
/// visitor who left, followed a link back from somewhere else and returned within the same day
/// has two sources and is credited to one of them; that is a limit of counting people rather than
/// arrivals, and is the same trade every other per-visitor figure on the dashboard makes.
/// </para>
/// <para>
/// A visitor whose arrival named nowhere is a row rather than an omission. Typing an address in,
/// opening a bookmark, following a link from an application, and arriving from a site that
/// withholds the address all look identical here, and on most sites they are together the largest
/// row on the list — so a list that quietly dropped them would make every share on it wrong.
/// </para>
/// </remarks>
public sealed record SiteSourcesQuery : AnalyticsQuery
{
    /// <summary>Most sources any one question may ask for.</summary>
    public const int MostSources = 100;

    /// <summary>Asks for a slice of the sources in a window.</summary>
    /// <param name="range">The window to count over.</param>
    /// <param name="grouping">Whether to group by the sending site or by the sending page.</param>
    /// <param name="siteDomain">
    /// The measured site's own address. Traffic from it and from anything below it is a reader
    /// moving between pages rather than a source, and takes no part.
    /// </param>
    /// <param name="limit">How many sources to return, at most <see cref="MostSources"/>.</param>
    /// <param name="offset">How many of the busiest sources to pass over first.</param>
    /// <exception cref="ArgumentException">The site's own address is missing.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The limit is outside its bounds, or the offset is negative.</exception>
    public SiteSourcesQuery(
        TimeRange range,
        SourceGrouping grouping,
        string siteDomain,
        int limit,
        int offset = 0)
        : base(range)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteDomain);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MostSources);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        Grouping = grouping;
        SiteDomain = siteDomain;
        Limit = limit;
        Offset = offset;
    }

    /// <summary>What each row stands for.</summary>
    public SourceGrouping Grouping { get; }

    /// <summary>The measured site's own address, whose traffic is not a source.</summary>
    public string SiteDomain { get; }

    /// <summary>How many sources to return.</summary>
    public int Limit { get; }

    /// <summary>How many of the busiest sources to pass over first.</summary>
    public int Offset { get; }

    /// <summary>Whose arrivals are counted.</summary>
    public Population Population { get; init; } = Population.Everybody;
}

/// <summary>What one row of a source list stands for.</summary>
public enum SourceGrouping
{
    /// <summary>One row per sending site.</summary>
    Site = 1,

    /// <summary>
    /// One row per sending page.
    /// </summary>
    /// <remarks>
    /// The address of the page a link was on, without whatever followed a question mark in it.
    /// What is wanted is which article sent the readers; the rest of that address is somebody
    /// else's site carrying somebody else's state, and it is not needed to answer the question.
    /// </remarks>
    Page = 2,

    /// <summary>
    /// One row per kind of source.
    /// </summary>
    /// <remarks>
    /// A closed set of five, from <see cref="SourceChannel"/>. It answers the question a list of
    /// hostnames cannot — how much of an audience search brings — which otherwise needs the reader
    /// to already know which of the names on the list are search engines and to add them up.
    /// </remarks>
    Kind = 3,
}

/// <summary>
/// How many of a site's audience were on each kind of device.
/// </summary>
/// <remarks>
/// <para>
/// Unpaged, because the answer is a closed set of five and there is nothing to page through. That
/// is the whole difference between this question and <see cref="SiteSoftwareQuery"/>: a browser is
/// one of an open and growing list, and a device is one of a handful the engine can name.
/// </para>
/// <para>
/// Counted per visitor, on the same terms as <see cref="SiteLocationsQuery"/> and for the same
/// reason, and within whoever the question is asked about: a person is settled on one device on
/// the same terms as everybody is.
/// </para>
/// </remarks>
/// <param name="Range">The window to count over.</param>
public sealed record SiteDeviceKindsQuery(TimeRange Range) : AnalyticsQuery(Range)
{
    /// <summary>Whose devices are counted.</summary>
    public Population Population { get; init; } = Population.Everybody;
}

/// <summary>
/// One slice of the browsers or the operating systems a site's audience used, commonest first.
/// </summary>
/// <remarks>
/// Read a slice at a time on the same terms as <see cref="SitePagesQuery"/>, with the same total
/// ordering, so successive slices neither repeat a name nor skip one. Open-ended in a way the
/// device kinds are not: browsers are released, renamed and forked, and the engine's catalogue
/// grows with them. Counted per visitor within whoever the question is asked about, on the same
/// terms as the device kinds.
/// </remarks>
public sealed record SiteSoftwareQuery : AnalyticsQuery
{
    /// <summary>Most names any one question may ask for.</summary>
    public const int MostNames = 100;

    /// <summary>Asks for a slice of the software in a window.</summary>
    /// <param name="range">The window to count over.</param>
    /// <param name="grouping">Whether to group by browser or by operating system.</param>
    /// <param name="limit">How many names to return, at most <see cref="MostNames"/>.</param>
    /// <param name="offset">How many of the commonest names to pass over first.</param>
    /// <exception cref="ArgumentOutOfRangeException">The limit is outside its bounds, or the offset is negative.</exception>
    public SiteSoftwareQuery(TimeRange range, SoftwareGrouping grouping, int limit, int offset = 0)
        : base(range)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MostNames);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        Grouping = grouping;
        Limit = limit;
        Offset = offset;
    }

    /// <summary>What each row stands for.</summary>
    public SoftwareGrouping Grouping { get; }

    /// <summary>How many names to return.</summary>
    public int Limit { get; }

    /// <summary>How many of the commonest names to pass over first.</summary>
    public int Offset { get; }

    /// <summary>Whose software is counted.</summary>
    public Population Population { get; init; } = Population.Everybody;
}

/// <summary>What one row of a software list stands for.</summary>
public enum SoftwareGrouping
{
    /// <summary>One row per browser family, without its version.</summary>
    Browser = 1,

    /// <summary>One row per operating system, without its version.</summary>
    OperatingSystem = 2,
}

/// <summary>
/// How a window's pages were actually read, across the whole site.
/// </summary>
/// <remarks>
/// <para>
/// Unpaged, because it is one answer about one window rather than a list.
/// </para>
/// <para>
/// Counted per reading — one reader on one page — rather than per report, because a page reports
/// its progress several times over as it is read and every one of those reports carries a running
/// total rather than an instalment. The largest of them is what that reading was worth.
/// </para>
/// <para>
/// Only the browser half of the measurement can observe any of this: a reporter on a site's own
/// server sees a page leave the building and nothing after that. So the answer carries how many
/// readings could be measured alongside how many there were, and the two are never conflated. A
/// site measured only from its server has nothing to say here, which is a different statement from
/// a site whose readers did nothing.
/// </para>
/// <para>
/// A reading of people is a reading the verdict on the visit covers; a page a person left open and
/// reported on after the engine had judged the visit is measured for everybody and not for people.
/// </para>
/// </remarks>
/// <param name="Range">The window to count over.</param>
public sealed record SiteEngagementQuery(TimeRange Range) : AnalyticsQuery(Range)
{
    /// <summary>Whose readings are counted.</summary>
    public Population Population { get; init; } = Population.Everybody;
}

/// <summary>
/// One slice of a site's pages ranked by how they were read rather than by how often.
/// </summary>
/// <remarks>
/// Read a slice at a time on the same terms as <see cref="SitePagesQuery"/>, with the same total
/// ordering. Only pages at least one reading could be measured on appear: a page seen solely by a
/// reporter on the site's own server has nothing to say about how it was read, and a row of noughts
/// beside it would say something quite different from nothing.
/// </remarks>
public sealed record SitePageEngagementQuery : AnalyticsQuery
{
    /// <summary>Most pages any one question may ask for.</summary>
    public const int MostPages = 100;

    /// <summary>Asks for a slice of the pages in a window.</summary>
    /// <param name="range">The window to count over.</param>
    /// <param name="ranking">What the list is ordered by.</param>
    /// <param name="limit">How many pages to return, at most <see cref="MostPages"/>.</param>
    /// <param name="offset">How many of the leading pages to pass over first.</param>
    /// <exception cref="ArgumentOutOfRangeException">The limit is outside its bounds, or the offset is negative.</exception>
    public SitePageEngagementQuery(TimeRange range, EngagementRanking ranking, int limit, int offset = 0)
        : base(range)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MostPages);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        Ranking = ranking;
        Limit = limit;
        Offset = offset;
    }

    /// <summary>What the list is ordered by.</summary>
    public EngagementRanking Ranking { get; }

    /// <summary>How many pages to return.</summary>
    public int Limit { get; }

    /// <summary>How many of the leading pages to pass over first.</summary>
    public int Offset { get; }

    /// <summary>Whose readings rank the pages.</summary>
    public Population Population { get; init; } = Population.Everybody;
}

/// <summary>What a page-engagement list is ordered by.</summary>
public enum EngagementRanking
{
    /// <summary>Longest typical attention first.</summary>
    Attention = 1,

    /// <summary>Furthest typical scroll depth first.</summary>
    Depth = 2,
}

/// <summary>
/// What a visit is, for the questions that have to rebuild them.
/// </summary>
/// <remarks>
/// <para>
/// Visits are not stored. They are the standard grouping — one visitor's activity up to the first
/// silence longer than the idle timeout — worked out on the way out, so what counts as one is part
/// of the question rather than a property of a table.
/// </para>
/// <para>
/// A visit still under way is left out of every answer these produce. Its pages are still
/// arriving, so counting it would report a reader who is two pages into a long article as somebody
/// who read one page and left — and on a quiet site that alone would decide the number.
/// </para>
/// </remarks>
/// <param name="IdleTimeout">How long a visitor may be quiet before their next activity is a new visit.</param>
/// <param name="SettledBefore">
/// A visit whose last activity falls at or after this instant is still under way. The caller sets
/// it an idle timeout behind the present moment, which is the point at which falling silent has
/// been observed rather than assumed.
/// </param>
public readonly record struct VisitBoundaries(TimeSpan IdleTimeout, DateTimeOffset SettledBefore);

/// <summary>
/// How a window's finished visits were shaped.
/// </summary>
/// <remarks>
/// Unpaged: three figures about one window rather than a list. Answered from activity rather than
/// from stored verdicts, so it keeps step with the headline totals instead of trailing them the
/// way <see cref="TrafficBreakdownQuery"/> does — what a visit was is a slower question than how
/// many there were. Asked of people alone, the activity is narrowed to the reports the verdicts
/// on people cover before it is grouped, so each rebuilt visit is the visit the engine judged; a
/// visit dropped only widens the silence either side of it.
/// </remarks>
/// <param name="Range">The window to count over, by when each visit began.</param>
/// <param name="Boundaries">What counts as one visit, and which ones have finished.</param>
public sealed record SiteVisitShapeQuery(TimeRange Range, VisitBoundaries Boundaries) : AnalyticsQuery(Range)
{
    /// <summary>Whose visits are counted.</summary>
    public Population Population { get; init; } = Population.Everybody;
}

/// <summary>
/// One slice of the pages a window's visits began or ended on, commonest first.
/// </summary>
/// <remarks>
/// Counted per visit rather than per page view: an arrival is a thing that happens once however
/// many times the page is read afterwards. Read a slice at a time on the same terms as
/// <see cref="SitePagesQuery"/>, with the same total ordering. Asked of people alone, the activity
/// is narrowed to the reports the verdicts on people cover before it is grouped, on the same terms
/// as <see cref="SiteVisitShapeQuery"/>.
/// </remarks>
public sealed record SiteVisitFlowQuery : AnalyticsQuery
{
    /// <summary>Most pages any one question may ask for.</summary>
    public const int MostPages = 100;

    /// <summary>Asks for a slice of the pages in a window.</summary>
    /// <param name="range">The window to count over, by when each visit began.</param>
    /// <param name="boundaries">What counts as one visit, and which ones have finished.</param>
    /// <param name="position">Whether to count where visits began or where they ended.</param>
    /// <param name="limit">How many pages to return, at most <see cref="MostPages"/>.</param>
    /// <param name="offset">How many of the commonest pages to pass over first.</param>
    /// <exception cref="ArgumentOutOfRangeException">The limit is outside its bounds, or the offset is negative.</exception>
    public SiteVisitFlowQuery(
        TimeRange range,
        VisitBoundaries boundaries,
        VisitPosition position,
        int limit,
        int offset = 0)
        : base(range)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MostPages);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        Boundaries = boundaries;
        Position = position;
        Limit = limit;
        Offset = offset;
    }

    /// <summary>What counts as one visit, and which ones have finished.</summary>
    public VisitBoundaries Boundaries { get; }

    /// <summary>Which end of a visit each row stands for.</summary>
    public VisitPosition Position { get; }

    /// <summary>How many pages to return.</summary>
    public int Limit { get; }

    /// <summary>How many of the commonest pages to pass over first.</summary>
    public int Offset { get; }

    /// <summary>Whose visits are counted.</summary>
    public Population Population { get; init; } = Population.Everybody;
}

/// <summary>Which end of a visit a page list stands for.</summary>
public enum VisitPosition
{
    /// <summary>The page each visit began on.</summary>
    Entry = 1,

    /// <summary>The last page each visit reached.</summary>
    Exit = 2,
}

/// <summary>
/// The pages one visit went through, in the order it went through them.
/// </summary>
/// <remarks>
/// <para>
/// Answered from activity on demand rather than stored beside the verdict. A verdict is the one
/// thing that cannot be worked out again, and a journey is the opposite: it is exactly what the
/// events say, so keeping a second copy of it would only create something that could disagree with
/// them. It also means the whole journey is available rather than the opening the classifier was
/// handed, which is capped because one sweep can ask for tens of thousands of pages.
/// </para>
/// <para>
/// Activity is read forward from where the visit began, for as long as one visit may be followed.
/// A visit that outlasts that has already gone far past the number of steps any one answer
/// carries.
/// </para>
/// </remarks>
public sealed record SiteVisitJourneyQuery : AnalyticsQuery
{
    /// <summary>Most steps any one question may ask for.</summary>
    /// <remarks>
    /// A sweep's journey is thousands of pages long and nobody reads it; what the reader wants
    /// from one is its shape, which the opening gives them. The visit's own page count is exact
    /// and is reported beside it, so a journey that was cut short says so.
    /// </remarks>
    public const int MostSteps = 200;

    /// <summary>How far forward a visit is followed from where it began.</summary>
    /// <remarks>
    /// <see cref="VisitorKeys.LongestVisit"/>, because that is how long a visit can be rather than
    /// how far anybody chose to look.
    /// </remarks>
    public static readonly TimeSpan LongestVisit = VisitorKeys.LongestVisit;

    /// <summary>Asks for the pages one visit went through.</summary>
    /// <param name="visit">Which visit.</param>
    /// <param name="idleTimeout">How long a visitor may be quiet before their next activity is a new visit.</param>
    /// <param name="siteDomain">The measured site's own address, so it is never one of its own sources.</param>
    /// <param name="limit">How many steps to return, at most <see cref="MostSteps"/>.</param>
    /// <exception cref="ArgumentException">The site's address is missing.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The limit is outside its bounds.</exception>
    public SiteVisitJourneyQuery(VisitKey visit, TimeSpan idleTimeout, string siteDomain, int limit)
        : base(new TimeRange(visit.StartedAt, visit.StartedAt + LongestVisit))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteDomain);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MostSteps);

        Visit = visit;
        IdleTimeout = idleTimeout;
        SiteDomain = siteDomain;
        Limit = limit;
    }

    /// <summary>Which visit.</summary>
    public VisitKey Visit { get; }

    /// <summary>How long a visitor may be quiet before their next activity is a new visit.</summary>
    public TimeSpan IdleTimeout { get; }

    /// <summary>
    /// The measured site's own address.
    /// </summary>
    /// <remarks>
    /// Read from the site catalogue rather than from the request, on the same terms as
    /// <see cref="SiteSourcesQuery.SiteDomain"/>: it decides which referrer counts as somewhere
    /// else, and a caller who could name it could decide what a visit is said to have come from.
    /// </remarks>
    public string SiteDomain { get; }

    /// <summary>How many steps to return.</summary>
    public int Limit { get; }
}

/// <summary>
/// Visits grouped by what the engine concluded generated them.
/// </summary>
/// <remarks>
/// Answered from stored verdicts rather than from raw activity, because what generated a visit is
/// a property of the whole visit and is not knowable one request at a time. Only visits that have
/// been judged appear, so a window that reaches into the last half-hour reports less than the
/// headline totals do — which the interface states rather than papers over.
/// </remarks>
/// <param name="Range">The window to group over, by when each visit began.</param>
public sealed record TrafficBreakdownQuery(TimeRange Range) : AnalyticsQuery(Range);

/// <summary>
/// Judged visits counted by what generated them, bucket by bucket across a window.
/// </summary>
/// <remarks>
/// <para>
/// The same population as <see cref="TrafficBreakdownQuery"/>, cut along a second axis, so the two
/// agree by construction: summing every bucket of every category gives the breakdown's own totals
/// for the same window.
/// </para>
/// <para>
/// A visit is counted whole, in the bucket it began in, however long it went on for. Spreading one
/// across the buckets it touched would need the activity behind it rebuilt, and would report a
/// visit that ran from a quarter to nine until half past as two visits — which is not a thing that
/// happened.
/// </para>
/// </remarks>
/// <param name="Range">The window to count over, by when each visit began.</param>
/// <param name="Granularity">Bucket size.</param>
public sealed record TrafficSeriesQuery(TimeRange Range, TimeGranularity Granularity)
    : AnalyticsQuery(Range);

/// <summary>
/// Individual visits with the evidence behind each verdict, newest first.
/// </summary>
/// <remarks>
/// <para>
/// What the caller narrowed to is one value rather than a property per dimension, because a
/// narrowing is one question — "show me these visits" — and splitting it across this question's own
/// surface would leave every caller and every statement free to handle half of it.
/// </para>
/// <para>
/// What counts as one visit, and which address is the site's own, are needed by every shape of
/// the question, because every row carries what the visit was. Which statement answers this
/// question is the compiler's decision rather than the caller's, and a question that could arrive
/// half filled in would leave every caller guessing which half.
/// </para>
/// </remarks>
public sealed record JudgedSessionsQuery : AnalyticsQuery
{
    /// <summary>
    /// Most visits any one question may ask for.
    /// </summary>
    /// <remarks>
    /// Each visit carries its whole evidence list, so this bounds the answer's size rather than
    /// merely its length. Nobody reads five hundred visits at once either.
    /// </remarks>
    public const int MostSessions = 500;

    /// <summary>Asks for judged visits in a window, newest first, one slice at a time.</summary>
    /// <param name="range">The window to look in, by when each visit began.</param>
    /// <param name="idleTimeout">How long a visitor may be quiet before their next activity is a new visit.</param>
    /// <param name="siteDomain">The measured site's own address, so it is never one of its own sources.</param>
    /// <param name="limit">How many visits to return, at most <see cref="MostSessions"/>.</param>
    /// <param name="offset">How many of the most recent visits to pass over first.</param>
    /// <exception cref="ArgumentException">The site's address is missing.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The limit is not between one and the maximum, or the offset is negative.
    /// </exception>
    public JudgedSessionsQuery(
        TimeRange range,
        TimeSpan idleTimeout,
        string siteDomain,
        int limit,
        int offset = 0)
        : base(range)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteDomain);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MostSessions);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        IdleTimeout = idleTimeout;
        SiteDomain = siteDomain;
        Limit = limit;
        Offset = offset;
    }

    /// <summary>How long a visitor may be quiet before their next activity is a new visit.</summary>
    /// <remarks>
    /// Read where the narrowing asks about the activity behind a verdict, because that activity has
    /// to be rebuilt into visits before any of it can be compared against anything.
    /// </remarks>
    public TimeSpan IdleTimeout { get; }

    /// <summary>
    /// The measured site's own address.
    /// </summary>
    /// <remarks>
    /// Read from the site catalogue rather than from the request, on the same terms as
    /// <see cref="SiteVisitJourneyQuery.SiteDomain"/>: it decides which referrer counts as somewhere
    /// else, and a caller who could name it could decide what a visit is said to have come from.
    /// </remarks>
    public string SiteDomain { get; }

    /// <summary>How many visits to return.</summary>
    public int Limit { get; }

    /// <summary>How many of the most recent visits to pass over first.</summary>
    public int Offset { get; }

    /// <summary>What to leave out, or nothing asked for, which is every visit the window holds.</summary>
    public VisitNarrowing Narrowing { get; init; } = VisitNarrowing.Nothing;
}

/// <summary>
/// What each detail of a period's judged visits actually held, counted per visit.
/// </summary>
/// <remarks>
/// <para>
/// Asked so that a reader is offered the values their own traffic holds rather than a list of every
/// country in the world. Six of the nine are open sets with no list to offer at all — nobody can
/// guess whether a search engine is recorded as <c>Google</c>, <c>google</c> or <c>google.com</c> —
/// so without this the filters those details need could not be built honestly.
/// </para>
/// <para>
/// Counted per visit, because a visit is what the list it narrows is made of. Counting reports
/// instead would offer a value promising four hundred visits and hand back ninety.
/// </para>
/// <para>
/// It describes the whole period rather than what is left after the rest of the narrowing, which is
/// the ground the conclusions have always been counted on. Conditioning it on everything else asked
/// for would give the answer as many shapes as the question and double the cost of every change a
/// reader makes to it.
/// </para>
/// </remarks>
public sealed record SiteVisitFacetsQuery : AnalyticsQuery
{
    /// <summary>
    /// Most values any one detail offers.
    /// </summary>
    /// <remarks>
    /// The commonest of them, because a busy site's pages run into thousands and a list nobody can
    /// reach the bottom of is not a choice. Deliberately more than
    /// <see cref="VisitNarrowing.MostValues"/>, which bounds what may be picked rather than what
    /// may be looked through.
    /// </remarks>
    public const int MostValues = 100;

    /// <summary>Asks what each detail of a window's judged visits held.</summary>
    /// <param name="range">The window to look in, by when each visit began.</param>
    /// <param name="idleTimeout">How long a visitor may be quiet before their next activity is a new visit.</param>
    /// <param name="siteDomain">The measured site's own address, so it is never one of its own sources.</param>
    /// <exception cref="ArgumentException">The site's address is missing.</exception>
    public SiteVisitFacetsQuery(TimeRange range, TimeSpan idleTimeout, string siteDomain)
        : base(range)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteDomain);

        IdleTimeout = idleTimeout;
        SiteDomain = siteDomain;
    }

    /// <summary>How long a visitor may be quiet before their next activity is a new visit.</summary>
    public TimeSpan IdleTimeout { get; }

    /// <summary>The measured site's own address, read from the site catalogue and never from a request.</summary>
    public string SiteDomain { get; }
}

/// <summary>
/// Everyone seen on a site in the last stretch of minutes, with what each of them did in it.
/// </summary>
/// <remarks>
/// <para>
/// The unit here is a visitor over a fixed trailing window, not a visit, and that is the whole
/// shape of it. A visit is over once it has been quiet for an idle timeout, and a report about a
/// page belongs to the visit that page was arrived at in however long the silence before it — so a
/// tab dismissed the next morning reopens a visit hours after its reader left, and a reader with an
/// old tab open has two visits running at once. Either would be counted as somebody who is here.
/// Asking instead which visitors reported in the last few minutes has none of that: it is one row
/// per visitor, it is exactly the question a reader is asking, and it needs no visit boundaries to
/// answer.
/// </para>
/// <para>
/// Nothing is read from outside the window, which is what separates this from every other
/// reconstruction in the vocabulary. Those rebuild a visit and so must reach back to where it
/// began; this one describes a stretch of minutes and a report outside it is not part of that
/// description. It is also what makes the question cheap enough to ask over and over.
/// </para>
/// <para>
/// A page here is a page the visitor was on during the window, counted once however many reports
/// described it. Somebody who returns to a page they were already on is not counted a second time,
/// because there are no visit boundaries in this answer to make "again" mean anything.
/// </para>
/// </remarks>
public sealed record SiteLiveVisitorsQuery : AnalyticsQuery
{
    /// <summary>
    /// Most visitors any one reading carries back.
    /// </summary>
    /// <remarks>
    /// A sweep can put hundreds of visitors on a site inside a few minutes, and each one carries
    /// the evidence a conclusion is reached from. This bounds one answer; how many there were is
    /// counted separately and exactly, so a reading that could not list everybody still says how
    /// many there are.
    /// </remarks>
    public const int MostVisitors = 100;

    /// <summary>
    /// Most pages carried back for any one visitor.
    /// </summary>
    /// <remarks>
    /// The evidence a conclusion is reached from includes what was asked for, so the pages travel
    /// with the visitor rather than being fetched again. A sweep can ask for thousands inside the
    /// window, and carrying all of them would let one visitor decide how much memory answering
    /// takes. The count of pages stays exact.
    /// </remarks>
    public const int MostRequests = 500;

    /// <summary>Asks who has been on a site in the last stretch of minutes.</summary>
    /// <param name="range">The stretch of minutes, ending at the present moment.</param>
    /// <param name="siteDomain">The measured site's own address, so it is never one of its own sources.</param>
    /// <param name="limit">How many visitors to carry back, at most <see cref="MostVisitors"/>.</param>
    /// <exception cref="ArgumentException">The site's address is missing.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The limit is outside its bounds.</exception>
    public SiteLiveVisitorsQuery(TimeRange range, string siteDomain, int limit)
        : base(range)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteDomain);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MostVisitors);

        SiteDomain = siteDomain;
        Limit = limit;
    }

    /// <summary>
    /// The measured site's own address.
    /// </summary>
    /// <remarks>
    /// Read from the site catalogue rather than from the request, on the same terms as
    /// <see cref="SiteVisitJourneyQuery.SiteDomain"/>: it decides which referrer counts as somewhere
    /// else, and a caller who could name it could decide what a visit is said to have come from.
    /// </remarks>
    public string SiteDomain { get; }

    /// <summary>How many visitors to carry back.</summary>
    public int Limit { get; }
}

/// <summary>
/// How much of a site was read in each minute of the last stretch of them.
/// </summary>
/// <remarks>
/// Counted over the whole window rather than over the visitors an answer had room for, so the
/// picture is of the site and not of a hundred of its readers. Every minute the window covers comes
/// back, including the ones nothing happened in, because a gap in a drawing is a fact about the
/// site and filling it in afterwards would be the reader's own arithmetic.
/// </remarks>
/// <param name="Range">The stretch of minutes, ending at the present moment.</param>
public sealed record SiteLiveActivityQuery(TimeRange Range) : AnalyticsQuery(Range);

/// <summary>
/// The pages a site's visitors are on at this moment, the one holding most visitors first.
/// </summary>
/// <remarks>
/// Each visitor is placed on one page — the one their most recent report named, which is the page
/// the row listing them prints — and the pages are counted by how many visitors stand on each. So
/// the visitors across every page add up to the visitors seen, because it is the same population
/// placed rather than a second count taken on other terms, and a page one visitor reloaded twenty
/// times holds one visitor. How often pages were delivered is
/// <see cref="SiteLiveActivityQuery"/>'s question.
/// </remarks>
public sealed record SiteLivePagesQuery : AnalyticsQuery
{
    /// <summary>Most pages any one reading carries back.</summary>
    /// <remarks>
    /// A short list read at a glance rather than a site map. Somebody who wants the whole picture
    /// of a period has a screen for it. A list cut short falls short of the visitors seen by exactly
    /// the visitors on the pages it left out, which a screen can say without a further figure.
    /// </remarks>
    public const int MostPages = 25;

    /// <summary>Asks which pages visitors are on.</summary>
    /// <param name="range">The stretch of minutes, ending at the present moment.</param>
    /// <param name="limit">How many pages to carry back, at most <see cref="MostPages"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The limit is outside its bounds.</exception>
    public SiteLivePagesQuery(TimeRange range, int limit)
        : base(range)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MostPages);

        Limit = limit;
    }

    /// <summary>How many pages to carry back.</summary>
    public int Limit { get; }
}

/// <summary>
/// The pages one visitor has been on in the last stretch of minutes, in the order they reached
/// them.
/// </summary>
/// <remarks>
/// <para>
/// A separate question from <see cref="SiteVisitJourneyQuery"/> rather than a narrowing of it, and
/// the three reasons are the same three that made the visitor list its own question. The journey is
/// read from where a visit began to as long as one may last, and clamped to the instant a verdict
/// was reached — which for a visit still under way is no upper bound at all until a verdict appears
/// and snaps it back. It reads forward with no reach-back, so a departure arriving from a stale tab
/// is folded into whichever visit last arrived at that page and can print an hour of reading against
/// a page opened three minutes ago. And its page count comes from a different question over a
/// different window, so a live panel built on it would say "three of four pages" for a reason that
/// is nothing but a difference in timing.
/// </para>
/// <para>
/// This asks about the stretch of minutes the visitor's row was drawn from — the caller carries
/// that reading's instant back, and the reach from it is the same — so what it shows and what the
/// list said are one reading of one window even when a beat has passed between them. A page is a
/// page the visitor was on during it, counted once, which is exactly what the list counted — so
/// the steps and the count agree by construction rather than by hope.
/// </para>
/// <para>
/// Nothing is established about the visitor here. The list already carries where they were, what
/// they were reading on and who sent them, settled over the same minutes; asking a second time
/// would be a second reading taken moments later, free to disagree with the row it was opened from.
/// </para>
/// </remarks>
public sealed record SiteLiveTrailQuery : AnalyticsQuery
{
    /// <summary>Most steps any one reading carries back.</summary>
    /// <remarks>
    /// A sweep asks for thousands of pages inside half an hour and nobody reads a list that long;
    /// what somebody wants from one is its shape. The visitor's own page count is exact and is
    /// reported beside it, so a trail that was cut short says so.
    /// </remarks>
    public const int MostSteps = 200;

    /// <summary>Asks what one visitor has been doing.</summary>
    /// <param name="range">The stretch of minutes the row was drawn from.</param>
    /// <param name="visitorKey">
    /// The visitor, as the reading of who is here named them — after both halves of the measurement
    /// were folded onto one key, which is the only spelling that finds anybody.
    /// </param>
    /// <param name="limit">How many steps to carry back, at most <see cref="MostSteps"/>.</param>
    /// <exception cref="ArgumentException">The visitor key is missing.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The limit is outside its bounds.</exception>
    public SiteLiveTrailQuery(TimeRange range, string visitorKey, int limit)
        : base(range)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(visitorKey);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MostSteps);

        VisitorKey = visitorKey;
        Limit = limit;
    }

    /// <summary>Whose activity to read.</summary>
    public string VisitorKey { get; }

    /// <summary>How many steps to carry back.</summary>
    public int Limit { get; }
}
