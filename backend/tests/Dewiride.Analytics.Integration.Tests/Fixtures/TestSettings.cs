using Dewiride.Analytics.Application.Consolidation;
using Dewiride.Analytics.Application.Dashboard;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Infrastructure.Crawlers;
using Dewiride.Analytics.Infrastructure.Posts;

namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// Configuration keys the suite overrides on the hosts it builds.
/// </summary>
/// <remarks>
/// Written as keys rather than literals so that renaming a setting breaks the build here instead
/// of quietly leaving a test running against the shipped default.
/// </remarks>
internal static class TestSettings
{
    /// <summary>Attempts to sign in allowed from one address in five minutes.</summary>
    public static readonly string SignInAllowance =
        $"{DashboardOptions.SectionName}:{nameof(DashboardOptions.SignInAttemptsPerFiveMinutes)}";

    /// <summary>
    /// An allowance no test will reach, for the hosts whose subject is something else.
    /// </summary>
    public const string NoPracticalLimit = "100000";

    /// <summary>The whole address this installation says people open it on.</summary>
    public static readonly string PublicAddress =
        $"{DashboardOptions.SectionName}:{nameof(DashboardOptions.PublicAddress)}";

    /// <summary>Whether the engine judges traffic on a timer in the background.</summary>
    /// <remarks>
    /// Switched off across the suite. A run that judged traffic on its own schedule would write
    /// verdicts in the middle of the tests that are checking what judging produces, and the same
    /// code is driven directly instead — which is what those tests are about.
    /// </remarks>
    public static readonly string BackgroundJudging =
        $"{ClassificationOptions.SectionName}:{nameof(ClassificationOptions.Enabled)}";

    /// <summary>Whether the engine counts and keeps each website's finished months on a timer.</summary>
    /// <remarks>
    /// Switched off in every host, on the same terms as judging: a run on its own schedule would
    /// write kept months in the middle of the tests checking what counting produces, and on a host
    /// that is never asked about kept months it would only be reading the store for nothing.
    /// </remarks>
    public static readonly string BackgroundConsolidation =
        $"{ConsolidationOptions.SectionName}:{nameof(ConsolidationOptions.Enabled)}";

    /// <summary>Whether the engine asks a name server whose crawler an address belongs to.</summary>
    /// <remarks>
    /// Switched off across the suite. The addresses these tests write are documentation ranges
    /// nobody answers for, so every question would be a real query leaving the machine and timing
    /// out — which would make the suite slow, dependent on the network, and no more truthful. The
    /// rule the answers are put through is proved separately, against a name server that says
    /// exactly what the test tells it to.
    /// </remarks>
    public static readonly string NameChecks =
        $"{CrawlerNameOptions.SectionName}:{nameof(CrawlerNameOptions.Enabled)}";

    /// <summary>Whether the published crawler address lists are fetched.</summary>
    /// <remarks>
    /// Switched off for the same reason: a suite that reached out to a dozen other companies' web
    /// servers on every run would be measuring their availability.
    /// </remarks>
    public static readonly string CrawlerRangeDownloads =
        $"{CrawlerRangeOptions.SectionName}:{nameof(CrawlerRangeOptions.AutoDownload)}";

    /// <summary>Whether this installation reads the sitemaps its websites name.</summary>
    public static readonly string SitemapReading =
        $"{SitemapOptions.SectionName}:{nameof(SitemapOptions.Enabled)}";

    /// <summary>How many websites an engine reads at once in the background.</summary>
    /// <remarks>
    /// Nought across the suite, on the same terms as judging: the reader is driven directly by the tests
    /// that are about reading, and one running on its own schedule would go looking for websites named by
    /// tests about something else. It is this setting rather than the switch above because that one is
    /// about the installation, and the status the dashboard is told has to go on reading as it would on
    /// an installation that reads.
    /// </remarks>
    public static readonly string BackgroundSitemapReaders =
        $"{SitemapOptions.SectionName}:{nameof(SitemapOptions.SitesAtOnce)}";

    /// <summary>What <see cref="BackgroundSitemapReaders"/> is set to, which is none.</summary>
    public const string NoBackgroundSitemapReaders = "0";
}
