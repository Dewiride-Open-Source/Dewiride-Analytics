using System.Collections.Frozen;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// Reads the pages a website's sitemap lists: each once, and again only when the sitemap says it has changed.
/// </summary>
/// <remarks>
/// <para>
/// An attempt is written down before anything is asked of the website, with when the next falls due, so
/// neither a body that cannot be read nor an engine dying can bring the same page round again any sooner.
/// A fault here is a line in the log and the page comes round when that write said. There are only so
/// many attempts, and each waits longer than the one before.
/// </para>
/// <para>
/// A website in trouble is not one page's fault, and every page it could be asked for would use up one of
/// its few attempts on the same answer. So a few of those in a row end the reading of pages for this pass
/// and leave the website alone for a while, and an outage costs the attempts of a few pages rather than
/// of every page it lists.
/// </para>
/// <para>
/// The newest come first, because those are what a website's latest posts are read from, and it can take
/// most of a day to get through a large website one page a second.
/// </para>
/// </remarks>
/// <param name="database">Control-plane database.</param>
/// <param name="writer">Writes what comes of it, under the claim.</param>
/// <param name="fetcher">Asks the website.</param>
/// <param name="options">How many, how often and how long.</param>
/// <param name="clock">The clock the page's times are written by.</param>
/// <param name="logger">Log.</param>
internal sealed class PageReader(
    ControlPlaneDbContext database,
    PostWriter writer,
    WebsiteFetcher fetcher,
    IOptions<SitemapOptions> options,
    TimeProvider clock,
    ILogger<PageReader> logger)
{
    /// <summary>How many times a page is asked for before it waits for the sitemap to say it has changed.</summary>
    private const int MostAttempts = 3;

    /// <summary>What to record for each thing that can come of asking for a page, short of reading it.</summary>
    private static readonly FrozenDictionary<FetchOutcome, PostPageOutcome> Outcomes =
        new Dictionary<FetchOutcome, PostPageOutcome>
        {
            [FetchOutcome.NotModified] = PostPageOutcome.Unchanged,
            [FetchOutcome.NotFound] = PostPageOutcome.Gone,
            [FetchOutcome.TurnedAway] = PostPageOutcome.TurnedAway,
            [FetchOutcome.AskedToSlowDown] = PostPageOutcome.TurnedAway,
            [FetchOutcome.ServerError] = PostPageOutcome.ServerError,
            [FetchOutcome.Unreachable] = PostPageOutcome.Unreachable,
            [FetchOutcome.Insecure] = PostPageOutcome.Insecure,
            [FetchOutcome.NotAPage] = PostPageOutcome.NotAPage,
            [FetchOutcome.TooLarge] = PostPageOutcome.TooLarge,
            [FetchOutcome.LeftTheWebsite] = PostPageOutcome.LeftTheWebsite,
            [FetchOutcome.TooManyRedirects] = PostPageOutcome.TooManyRedirects,
            [FetchOutcome.PrivateAddress] = PostPageOutcome.PrivateAddress,
        }.ToFrozenDictionary();

    private readonly SitemapOptions _settings = options.Value;

    /// <summary>
    /// Reads as many of a website's due pages as this pass has room for.
    /// </summary>
    /// <param name="pass">The pass.</param>
    /// <param name="cancellationToken">Stops the reading, which then ends as a cancellation.</param>
    /// <returns>A task that completes once it has read as many as it can.</returns>
    public async Task ReadDueAsync(SitemapPassContext pass, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pass);

        if (!pass.MayAskForMore)
        {
            return;
        }

        var now = clock.GetUtcNow();

        var due = await database.SitePosts
            .AsNoTracking()
            .Where(post => post.SiteId == pass.Lease.SiteId && post.UnlistedAt == null && post.PageDueAt <= now)
            .OrderByDescending(post => post.ListedInPostsPart)
            .ThenByDescending(post => post.ListedModifiedAt != null)
            .ThenByDescending(post => post.ListedModifiedAt)
            .ThenByDescending(post => post.FirstListedAt)
            .ThenBy(post => post.Path)
            .Take(_settings.PagesPerPass)
            .Select(post => new DuePage(post.Path, post.Address, post.PageAttempts, post.PageEtag, post.PageLastModified))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (due.Count > 0)
        {
            await ReadAllAsync(pass, due, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReadAllAsync(SitemapPassContext pass, List<DuePage> due, CancellationToken cancellationToken)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(pass.TimeZoneId);
        var troubleInARow = 0;

        foreach (var page in due)
        {
            if (!pass.MayAskForMore)
            {
                return;
            }

            var trouble = await ReadOneAsync(pass, page, zone, cancellationToken).ConfigureAwait(false);

            troubleInARow = trouble ? troubleInARow + 1 : 0;

            if (troubleInARow == SitemapPassContext.MostTroubleInARow)
            {
                pass.LeaveAloneAfterFailure();
            }
        }
    }

    /// <summary>
    /// Reads one page, and whatever happens goes on to the next.
    /// </summary>
    /// <remarks>
    /// A fault here counts towards the website being left alone, like trouble at the website does. It
    /// says nothing about the website, but a fault that goes on happening costs a request and a line in
    /// the log for every page there is, and it can wait for a while as well as anything else can.
    /// </remarks>
    /// <returns>Whether to count it towards leaving the website alone.</returns>
    private async Task<bool> ReadOneAsync(
        SitemapPassContext pass,
        DuePage page,
        TimeZoneInfo zone,
        CancellationToken cancellationToken)
    {
        try
        {
            return await AskIfAllowedAsync(pass, page, zone, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not SitemapClaimLostException && !cancellationToken.IsCancellationRequested)
        {
            SitemapLog.ItemFailed(logger, pass.Lease.SiteId, failure.GetType().Name);

            return true;
        }
    }

    private async Task<bool> AskIfAllowedAsync(
        SitemapPassContext pass,
        DuePage page,
        TimeZoneInfo zone,
        CancellationToken cancellationToken)
    {
        var address = new Uri(page.Address);

        if (!pass.Bounds.Admits(address))
        {
            await SettleAsync(pass, page, PostPageOutcome.LeftTheWebsite, null, cancellationToken).ConfigureAwait(false);

            return false;
        }

        var verdict = await pass.Robots.AskAsync(address, cancellationToken).ConfigureAwait(false);

        pass.Hear(verdict);

        if (verdict.Trouble is not null)
        {
            // Nothing is known about what may be read, so nothing is read, and that is nobody's attempt.
            pass.LeaveAloneAfterFailure();

            return true;
        }

        if (!verdict.Permitted)
        {
            await SettleAsync(pass, page, PostPageOutcome.TurnedAway, null, cancellationToken).ConfigureAwait(false);

            return false;
        }

        await BeginAsync(pass, page, cancellationToken).ConfigureAwait(false);

        return await AskAsync(pass, page, address, zone, cancellationToken).ConfigureAwait(false);
    }

    private Task BeginAsync(SitemapPassContext pass, DuePage page, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var attempt = (short)(page.Attempts + 1);
        DateTimeOffset? dueAgainAt = attempt < MostAttempts ? now + (_settings.Cadence * attempt) : null;

        return writer.BeginPageAsync(pass.Lease, page.Path, attempt, dueAgainAt, now, cancellationToken);
    }

    private async Task<bool> AskAsync(
        SitemapPassContext pass,
        DuePage page,
        Uri address,
        TimeZoneInfo zone,
        CancellationToken cancellationToken)
    {
        using var fetched = await fetcher
            .FetchAsync(pass.Bounds, address, FetchKind.Page, page.Validators, pass.Robots.AskAsync, cancellationToken)
            .ConfigureAwait(false);

        pass.Hear(fetched);

        if (fetched.Outcome == FetchOutcome.Succeeded)
        {
            return await ReadBodyAsync(pass, page, fetched, zone, cancellationToken).ConfigureAwait(false);
        }

        if (fetched.StoppedByRobots)
        {
            return await StoppedOnTheWayAsync(pass, page, fetched, cancellationToken).ConfigureAwait(false);
        }

        var outcome = Outcomes.GetValueOrDefault(fetched.Outcome, PostPageOutcome.ServerError);

        if (MayBeDifferentNextTime(outcome))
        {
            await PutOffAsync(pass, page, outcome, cancellationToken).ConfigureAwait(false);

            return IsTroubleAtTheWebsite(outcome);
        }

        // A website saying what it sent is not a web page has said what it is, so it is not a post. A body
        // that could not be unpacked, further down, has said no such thing.
        var pageType = outcome == PostPageOutcome.NotAPage ? PostPageType.Other : (PostPageType?)null;

        await SettleAsync(pass, page, outcome, pageType, cancellationToken).ConfigureAwait(false);

        return false;
    }

    /// <summary>
    /// Comes to what robots.txt said about where a page redirects, the same as if it had been said of the
    /// address itself. A page it refuses is turned away with no attempt spent. Where it could not say, nothing
    /// is known about what may be read, and that is nobody's attempt either, though by then one has been written.
    /// </summary>
    /// <returns>Whether the website looks to be in trouble.</returns>
    private async Task<bool> StoppedOnTheWayAsync(
        SitemapPassContext pass,
        DuePage page,
        Fetched fetched,
        CancellationToken cancellationToken)
    {
        if (fetched.Outcome == FetchOutcome.RobotsDisallowed)
        {
            await SettleAsync(pass, page, PostPageOutcome.TurnedAway, null, cancellationToken).ConfigureAwait(false);

            return false;
        }

        pass.LeaveAloneAfterFailure();

        var dueAt = clock.GetUtcNow() + (pass.LeftAloneFor ?? _settings.RetryAfterFailure);

        await writer.GiveBackPageAsync(pass.Lease, page.Path, page.Attempts, dueAt, cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Reads a page that answered. A failure while it does is what the page came to this time, and it
    /// comes round again, since a body cut off or spoiled in transit says nothing about the page.
    /// </summary>
    /// <returns>Whether the website looks to be in trouble.</returns>
    private async Task<bool> ReadBodyAsync(
        SitemapPassContext pass,
        DuePage page,
        Fetched fetched,
        TimeZoneInfo zone,
        CancellationToken cancellationToken)
    {
        PageFacts facts;

        try
        {
            var read = await fetched.ReadPrefixAsync().ConfigureAwait(false);

            facts = PageMetadata.Read(read.Bytes, fetched.ContentType, zone, clock.GetUtcNow(), fetched.Deadline);
        }
        catch (Exception failure) when (fetched.OutcomeOf(failure) is { } outcome)
        {
            var recorded = Outcomes.GetValueOrDefault(outcome, PostPageOutcome.NotAPage);

            await PutOffAsync(pass, page, recorded, cancellationToken).ConfigureAwait(false);

            return IsTroubleAtTheWebsite(recorded);
        }

        var landed = PagePath.Of(fetched.Address);

        await writer
            .SavePageAsync(
                pass.Lease,
                page.Path,
                new PageSaying(facts, fetched.Validators, landed == page.Path ? null : landed),
                clock.GetUtcNow(),
                cancellationToken)
            .ConfigureAwait(false);

        return false;
    }

    private Task SettleAsync(
        SitemapPassContext pass,
        DuePage page,
        PostPageOutcome outcome,
        PostPageType? pageType,
        CancellationToken cancellationToken) =>
        writer.SettlePageAsync(pass.Lease, page.Path, outcome, pageType, clock.GetUtcNow(), cancellationToken);

    private Task PutOffAsync(
        SitemapPassContext pass,
        DuePage page,
        PostPageOutcome outcome,
        CancellationToken cancellationToken) =>
        writer.PutPageOffAsync(
            pass.Lease,
            page.Path,
            outcome,
            clock.GetUtcNow() + pass.LeftAloneFor,
            cancellationToken);

    /// <summary>
    /// Whether asking again could come to something different without the page itself having changed.
    /// A page that has gone, or redirects off the website, stays that way until the sitemap says otherwise.
    /// </summary>
    private static bool MayBeDifferentNextTime(PostPageOutcome outcome) =>
        outcome == PostPageOutcome.TooLarge || IsTroubleAtTheWebsite(outcome);

    /// <summary>
    /// Whether an outcome says something about the whole website, where the rest say something about one
    /// page.
    /// </summary>
    private static bool IsTroubleAtTheWebsite(PostPageOutcome outcome) =>
        outcome is PostPageOutcome.TurnedAway
            or PostPageOutcome.ServerError
            or PostPageOutcome.Unreachable
            or PostPageOutcome.Insecure;

    /// <summary>
    /// One page due to be read.
    /// </summary>
    /// <param name="Path">Its path.</param>
    /// <param name="Address">Its address.</param>
    /// <param name="Attempts">How many times it has been asked for since it was last read.</param>
    /// <param name="ETag">The entity tag it last answered with.</param>
    /// <param name="LastModified">When it last said it changed.</param>
    private sealed record DuePage(string Path, string Address, short Attempts, string? ETag, DateTimeOffset? LastModified)
    {
        /// <summary>What to ask with, to be told if nothing has changed.</summary>
        public WebsiteValidators Validators => new(ETag, LastModified);
    }
}
