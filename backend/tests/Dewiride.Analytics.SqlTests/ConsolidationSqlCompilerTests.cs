using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Consolidation;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;
using Dewiride.Analytics.Testing;

namespace Dewiride.Analytics.SqlTests;

/// <summary>
/// Approves the statements that count a website's finished months for keeping.
/// </summary>
/// <remarks>
/// A kept month is the only record of its month once its reports go, so a change to how it is
/// counted is a change to every month counted afterwards and to none of the months already frozen.
/// The approved statements make the change visible; the version of the arithmetic, held to their
/// text below, makes sure the months still held are counted again by it.
/// </remarks>
public sealed partial class ConsolidationSqlCompilerTests
{
    private const string Zone = "Asia/Kolkata";

    private static readonly Guid SiteId = Guid.Parse("0197c0de-0000-7000-8000-000000000001");
    private static readonly Guid OrganizationId = Guid.Parse("0197c0de-0000-7000-8000-0000000000ff");

    /// <summary>May 2026 where the website is: from midnight on the first in Kolkata to midnight on the first of June.</summary>
    private static readonly MonthToCount May = new(
        SiteId,
        Zone,
        new DateOnly(2026, 5, 1),
        new TimeRange(
            new DateTimeOffset(2026, 4, 30, 18, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 31, 18, 30, 0, TimeSpan.Zero)));

    /// <summary>The moment the calendar is read at: the middle of June.</summary>
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// What the arithmetic of each version reads as, by the SHA-256 of the statements that count.
    /// </summary>
    /// <remarks>
    /// When the test below fails, how a month is counted has changed — most likely through a
    /// fragment these statements share with the dashboard's own. Raise
    /// <see cref="CountingRules.Current"/> and add the new version's entry here, so the months still
    /// held are counted again by the new arithmetic rather than freezing with the old. The earlier
    /// entries stay, as the record of what each version counted with.
    /// </remarks>
    private static readonly Dictionary<int, string> ArithmeticOf = new()
    {
        [1] = "2c66959a2fac04ae5e9a15dca73075c14b3c4406d3fa2cd793c00cf2ad74c9e6",
    };

    [Fact]
    public void Month_Calendar()
    {
        var statement = ConsolidationSqlCompiler.CompileSurvey(SiteId, Zone, Now);

        Snapshot.Matches(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public void Month_Count()
    {
        var statement = ConsolidationSqlCompiler.CompileCount(May, Population.Everybody);

        Snapshot.Matches(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public void Month_Count_Of_People()
    {
        var statement = ConsolidationSqlCompiler.CompileCount(May, Population.People);

        Snapshot.Matches(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public void Month_Verdicts()
    {
        var statement = ConsolidationSqlCompiler.CompileVerdicts(May);

        Snapshot.Matches(CompiledStatementReport.Render(statement));
    }

    /// <summary>
    /// A kept month is the overview of that month, read by the same selection: the same reports,
    /// the same settling of who each was about, the same people kept by the same verdicts, and the
    /// same pages delivered and daily visitors counted from them. Two derivations of one figure
    /// would agree until they did not, and the kept one would then be wrong for ever.
    /// </summary>
    /// <param name="population">Who the month is counted for.</param>
    [Theory]
    [InlineData(Population.Everybody)]
    [InlineData(Population.People)]
    public void A_Month_Is_Counted_By_The_Dashboards_Arithmetic(Population population)
    {
        var counted = ConsolidationSqlCompiler.CompileCount(May, population);
        var overview = AnalyticsSqlCompiler.Compile(Scope(), new OverviewQuery(May.Window) { Population = population });

        var visitors = VisitorsColumn().Match(overview.Sql).Groups["expression"].Value;

        Selection(counted.Sql).Should().Be(Selection(overview.Sql));
        Flattened(counted.Sql).Should().Contain(Delivered(overview.Sql));
        visitors.Should().NotBeNullOrEmpty();
        counted.Sql.Should().Contain($"{visitors} AS visitors");
    }

    /// <summary>
    /// A kept month's busiest day and weekdays are its daily series, and its hours its hourly
    /// series: each cut exactly where the dashboard's series cuts the same day or hour.
    /// </summary>
    [Fact]
    public void A_Months_Days_And_Hours_Are_Cut_Where_The_Dashboards_Series_Cut_Them()
    {
        var counted = ConsolidationSqlCompiler.CompileCount(May, Population.Everybody);

        counted.Sql.Should().Contain($"{SeriesCut(TimeGranularity.Day)} AS calendar_day");
        counted.Sql.Should().Contain($"{SeriesCut(TimeGranularity.Hour)} AS calendar_hour");
        counted.Sql.Should().Contain("GROUP BY calendar_day, visitor_key, path");
        counted.Sql.Should().Contain("GROUP BY calendar_hour, visitor_key, path");
    }

    /// <summary>
    /// Counting runs beside the screens people are reading, which matter more than how soon a month
    /// is kept: every counting statement asks the store for one thread and for ten minutes at most.
    /// </summary>
    [Fact]
    public void Counting_A_Month_Takes_One_Thread_And_Ten_Minutes_At_Most()
    {
        foreach (var statement in EveryCountingStatement())
        {
            statement.Sql.TrimEnd().Should().EndWith("SETTINGS max_threads = 1, max_execution_time = 600");
        }
    }

    /// <summary>
    /// A statement and its values are written apart and have to meet exactly: a placeholder with
    /// nothing bound is refused by the store, and a value bound to nothing is a narrowing that
    /// silently does nothing.
    /// </summary>
    [Fact]
    public void Every_Placeholder_In_A_Counting_Statement_Has_A_Bound_Value()
    {
        foreach (var statement in EveryCountingStatement().Append(ConsolidationSqlCompiler.CompileSurvey(SiteId, Zone, Now)))
        {
            var named = Placeholder().Matches(statement.Sql)
                .Select(match => match.Groups[1].Value)
                .Distinct(StringComparer.Ordinal);

            named.Should().BeEquivalentTo(statement.Parameters.Select(parameter => parameter.Name));
        }
    }

    /// <summary>
    /// The website and the month come from the control plane and the store, never from a request,
    /// and none of them is ever text in a statement.
    /// </summary>
    [Fact]
    public void No_Value_Appears_In_A_Counting_Statement()
    {
        foreach (var statement in EveryCountingStatement().Append(ConsolidationSqlCompiler.CompileSurvey(SiteId, Zone, Now)))
        {
            statement.Sql.Should().NotContain(SiteId.ToString());
            statement.Sql.Should().NotContain(Zone);
        }
    }

    [Fact]
    public void Counting_Refuses_A_Missing_Month()
    {
        var count = () => ConsolidationSqlCompiler.CompileCount(null!, Population.Everybody);
        var verdicts = () => ConsolidationSqlCompiler.CompileVerdicts(null!);
        var survey = () => ConsolidationSqlCompiler.CompileSurvey(SiteId, " ", Now);

        count.Should().Throw<ArgumentNullException>();
        verdicts.Should().Throw<ArgumentNullException>();
        survey.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// The statements that count share their fragments with the dashboard's own, so a change made
    /// for the dashboard changes how a month is counted too. Holding the version of the arithmetic to
    /// their text is what turns that into a failing build until the version is raised — and raising
    /// it is what has the months still held counted again, rather than freezing with arithmetic the
    /// rest of the product no longer uses.
    /// </summary>
    [Fact]
    public void The_Version_Of_The_Arithmetic_Moves_With_The_Statements_That_Count()
    {
        ArithmeticOf.Should().ContainKey(CountingRules.Current);
        Fingerprint().Should().Be(
            ArithmeticOf[CountingRules.Current],
            "the statements that count a month changed, so raise CountingRules.Current and record the new fingerprint");
    }

    /// <summary>
    /// The fingerprint of the arithmetic: a hash of the text of every statement that counts a month,
    /// with line endings as the repository keeps them.
    /// </summary>
    private static string Fingerprint()
    {
        var text = string.Join("\n--\n", EveryCountingStatement().Select(statement => statement.Sql))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static CompiledStatement[] EveryCountingStatement() =>
    [
        ConsolidationSqlCompiler.CompileCount(May, Population.Everybody),
        ConsolidationSqlCompiler.CompileCount(May, Population.People),
        ConsolidationSqlCompiler.CompileVerdicts(May),
    ];

    /// <summary>Everything a statement reads before its own selection: the reports, settled and kept.</summary>
    private static string Selection(string sql) => sql[..sql.IndexOf("\nSELECT", StringComparison.Ordinal)];

    /// <summary>The pages-delivered expression of the overview, laid out on one line.</summary>
    private static string Delivered(string sql)
    {
        var flattened = Flattened(sql);
        var start = flattened.IndexOf("greatest(", StringComparison.Ordinal);
        var end = flattened.IndexOf("AS page_views", start, StringComparison.Ordinal) + "AS page_views".Length;

        return flattened[start..end];
    }

    private static string Flattened(string sql) => Whitespace().Replace(sql, " ");

    /// <summary>What the dashboard's series of reports names its buckets of one size by.</summary>
    private static string SeriesCut(TimeGranularity granularity)
    {
        var series = AnalyticsSqlCompiler.Compile(
            Scope(),
            new TimeSeriesQuery(May.Window, granularity, TimeSeriesMetric.PageViews));

        return CalendarBucket().Match(series.Sql).Groups["cut"].Value;
    }

    private static TenantScope Scope() => new(SiteId, OrganizationId, SiteRole.Viewer, Zone, DateTimeOffset.UnixEpoch);

    /// <summary>Finds every placeholder a statement names.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"\{(\w+):")]
    private static partial Regex Placeholder();

    /// <summary>Finds what a series names its counted buckets by on the website's calendar.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"^\s*(?<cut>\S.*) AS calendar_bucket,?$", RegexOptions.Multiline)]
    private static partial Regex CalendarBucket();

    /// <summary>Finds what the overview counts its visitors by.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"^\s*(?<expression>\S.*) AS visitors,?$", RegexOptions.Multiline)]
    private static partial Regex VisitorsColumn();

    /// <summary>Finds a run of whitespace.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
