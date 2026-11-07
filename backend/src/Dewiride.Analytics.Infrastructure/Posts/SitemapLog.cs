using Dewiride.Analytics.Application.Posts;
using Microsoft.Extensions.Logging;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// What reading sitemaps writes in the log.
/// </summary>
/// <remarks>
/// A website's identifier and the product's own words, and nothing else. Everything a website sends was
/// written by a stranger, and the message of an exception from reading it can quote it, so what a failure
/// on one part or page leaves here is the name of its type. What a website could not or would not do is
/// not a fault of this product's at all: it goes where its owner can read it, in the status of the reading.
/// </remarks>
internal static partial class SitemapLog
{
    [LoggerMessage(
        EventId = 1301,
        Level = LogLevel.Debug,
        Message = "A pass over website {SiteId} took {Elapsed}.")]
    public static partial void PassFinished(ILogger logger, Guid siteId, TimeSpan elapsed);

    [LoggerMessage(
        EventId = 1302,
        Level = LogLevel.Debug,
        Message = "Reading the sitemap of website {SiteId} ended as {State} ({Problem}).")]
    public static partial void ReadingEnded(ILogger logger, Guid siteId, SitemapState state, SitemapProblem? problem);

    [LoggerMessage(
        EventId = 1303,
        Level = LogLevel.Information,
        Message = "The claim on website {SiteId} was lost during a pass, which stopped there and changed "
            + "nothing further.")]
    public static partial void ClaimLost(ILogger logger, Guid siteId);

    [LoggerMessage(
        EventId = 1304,
        Level = LogLevel.Error,
        Message = "Reading sitemaps could not run. The next run tries again.")]
    public static partial void RunFailed(ILogger logger, Exception failure);

    [LoggerMessage(
        EventId = 1305,
        Level = LogLevel.Warning,
        Message = "The claim on website {SiteId} could not be let go of. It runs out by itself.")]
    public static partial void ReleaseFailed(ILogger logger, Exception failure, Guid siteId);

    [LoggerMessage(
        EventId = 1306,
        Level = LogLevel.Error,
        Message = "Part of reading website {SiteId} failed with a {Failure}. That part is tried again later, "
            + "and the reading carries on past it.")]
    public static partial void ItemFailed(ILogger logger, Guid siteId, string failure);

    [LoggerMessage(
        EventId = 1307,
        Level = LogLevel.Warning,
        Message = "Reading sitemaps is switched off. Whatever was read before stays where it is, but nothing "
            + "new will be read.")]
    public static partial void Disabled(ILogger logger);

    [LoggerMessage(
        EventId = 1308,
        Level = LogLevel.Information,
        Message = "This engine has been given no websites to read at once, so it leaves that to whichever "
            + "others share its control plane.")]
    public static partial void LeftToOthers(ILogger logger);
}
