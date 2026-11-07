using Dewiride.Analytics.Application.Ingest;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// One pass over one website's sitemap: claim it, get as far as the time allows, and let go.
/// </summary>
/// <remarks>
/// <para>
/// A pass ends well inside its claim. It stops starting new work once its budget is spent, and each thing
/// it has already started has a deadline of its own, so what it runs over by is known in advance, and a
/// setting for how long a claim lasts that would not cover that is refused at start. A website that drips
/// its answer out costs the one request, and the next website gets its turn when it would have.
/// </para>
/// <para>
/// Letting go is the last thing a pass does however it ended, on a token of its own and with only a
/// moment to do it in, because the engine stopping is one of the ways a pass ends. If it cannot be done at
/// all, the claim runs out by itself.
/// </para>
/// </remarks>
/// <param name="scopes">Creates the scope each pass's database work is resolved from.</param>
/// <param name="options">How often and how long.</param>
/// <param name="clock">The clock the pass's times are written by.</param>
/// <param name="logger">Log.</param>
public sealed class SitemapPass(
    IServiceScopeFactory scopes,
    IOptions<SitemapOptions> options,
    TimeProvider clock,
    ILogger<SitemapPass> logger)
{
    /// <summary>How long letting go of a claim may take.</summary>
    private static readonly TimeSpan LetGoWithin = TimeSpan.FromSeconds(5);

    private readonly SitemapOptions _settings = options.Value;

    /// <summary>
    /// Runs one pass over whichever website's time has come first, if any has.
    /// </summary>
    /// <param name="cancellationToken">Stops the pass, which then ends as a cancellation.</param>
    /// <returns>Whether there was one, and whoever asked might as well look again at once.</returns>
    public async Task<bool> RunNextAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();

        var claims = scope.ServiceProvider.GetRequiredService<SitemapClaims>();

        await claims.PrepareAsync(cancellationToken).ConfigureAwait(false);

        var lease = await claims.ClaimNextAsync(cancellationToken).ConfigureAwait(false);

        if (lease is null)
        {
            return false;
        }

        await ReadAsync(scope.ServiceProvider, claims, lease, cancellationToken).ConfigureAwait(false);

        return true;
    }

    private async Task ReadAsync(
        IServiceProvider services,
        SitemapClaims claims,
        SitemapLease lease,
        CancellationToken cancellationToken)
    {
        var began = clock.GetTimestamp();
        DateTimeOffset? nextPassAt = null;
        SitemapPassContext? pass = null;

        try
        {
            pass = await BeginAsync(services, lease, cancellationToken).ConfigureAwait(false);

            if (pass is not null)
            {
                await services.GetRequiredService<SitemapCycle>().AdvanceAsync(pass, cancellationToken).ConfigureAwait(false);
                await services.GetRequiredService<PageReader>().ReadDueAsync(pass, cancellationToken).ConfigureAwait(false);

                nextPassAt = await NextPassAsync(services, pass, cancellationToken).ConfigureAwait(false);
            }

            var elapsed = clock.GetElapsedTime(began);

            SitemapLog.PassFinished(logger, lease.SiteId, elapsed);
        }
        catch (SitemapClaimLostException)
        {
            SitemapLog.ClaimLost(logger, lease.SiteId);
        }
        finally
        {
            var next = nextPassAt ?? WhenUnfinished(pass, cancellationToken);

            await LetGoAsync(services, claims, lease, pass, next, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads what a pass has to go on, unless the claim turns out to be for something nobody wants any more.
    /// </summary>
    /// <remarks>
    /// A website is read only while its account is being measured. When measurement stops, for whatever
    /// reason the edition has, nothing more is asked of the website on that account's behalf, and reading
    /// carries on by itself once measurement does.
    /// </remarks>
    private async Task<SitemapPassContext?> BeginAsync(
        IServiceProvider services,
        SitemapLease lease,
        CancellationToken cancellationToken)
    {
        var database = services.GetRequiredService<ControlPlaneDbContext>();

        var site = await database.OpenSites
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == lease.SiteId, cancellationToken)
            .ConfigureAwait(false);

        var row = await database.SitemapReads
            .AsNoTracking()
            .SingleOrDefaultAsync(read => read.SiteId == lease.SiteId, cancellationToken)
            .ConfigureAwait(false);

        if (site is null
            || row is null
            || site.SitemapAddress != lease.Address
            || row.ClaimToken != lease.Token
            || !Uri.TryCreate(lease.Address, UriKind.Absolute, out var sitemap))
        {
            return null;
        }

        var measured = await services.GetRequiredService<IMeasurementAllowance>()
            .AllowsAsync(SnapshotOf(site), cancellationToken)
            .ConfigureAwait(false);

        return measured
            ? new SitemapPassContext(lease, sitemap, row, site, services.GetRequiredService<WebsiteFetcher>(), _settings, clock)
            : null;
    }

    /// <summary>
    /// When a website's time next comes after a pass that did not get as far as working it out.
    /// </summary>
    /// <remarks>
    /// An engine stopping in good order is nobody's doing, and the website is due again as soon as an
    /// engine is back, unless it asked to be left alone for longer. Anything else is waited out as a
    /// failure is.
    /// </remarks>
    private DateTimeOffset WhenUnfinished(SitemapPassContext? pass, CancellationToken stopping)
    {
        var now = clock.GetUtcNow();

        return stopping.IsCancellationRequested
            ? now + (pass?.LeftAloneFor ?? TimeSpan.Zero)
            : now + _settings.RetryAfterFailure;
    }

    private static SiteSnapshot SnapshotOf(Site site) => new()
    {
        Id = site.Id,
        OrganizationId = site.OrganizationId,
        Domain = site.Domain,
        RetainQueryStrings = site.RetainQueryStrings,
        CaptureClicks = site.CaptureClicks,
        AllowedOrigins = [.. site.AllowedOrigins],
    };

    /// <summary>
    /// Works out when the website's time next comes: as soon as is decent if there is more to do now, and
    /// otherwise when the first thing falls due, for as long as the website has not asked for longer.
    /// </summary>
    private async Task<DateTimeOffset> NextPassAsync(
        IServiceProvider services,
        SitemapPassContext pass,
        CancellationToken cancellationToken)
    {
        var soonestPage = await services.GetRequiredService<ControlPlaneDbContext>().SitePosts
            .AsNoTracking()
            .Where(post => post.SiteId == pass.Lease.SiteId && post.UnlistedAt == null && post.PageDueAt != null)
            .MinAsync(post => post.PageDueAt, cancellationToken)
            .ConfigureAwait(false);

        var now = clock.GetUtcNow();
        var soonest = now + _settings.Interval;

        if (pass.LeftAloneFor is { } wait && now + wait > soonest)
        {
            soonest = now + wait;
        }

        if (pass.ReadingStartedAt is not null)
        {
            return soonest;
        }

        var due = soonestPage < pass.NextReadingAt ? soonestPage.Value : pass.NextReadingAt;

        return due > soonest ? due : soonest;
    }

    /// <summary>
    /// Lets go of a claim, and hands back whatever attempt the engine stopping cut short.
    /// </summary>
    /// <remarks>
    /// An attempt is written down before anything is asked so that a crash uses it up, and it has to. An
    /// engine stopping in good order is not that. Nobody got an answer, nothing went wrong at the website,
    /// and a sitemap should neither read as failed nor sit idle until the next hour because somebody
    /// deployed.
    /// </remarks>
    private async Task LetGoAsync(
        IServiceProvider services,
        SitemapClaims claims,
        SitemapLease lease,
        SitemapPassContext? pass,
        DateTimeOffset nextPassAt,
        CancellationToken stopping)
    {
        using var limit = new CancellationTokenSource(LetGoWithin, clock);

        try
        {
            if (stopping.IsCancellationRequested && pass is not null)
            {
                await GiveBackAsync(services.GetRequiredService<PostWriter>(), pass, limit.Token).ConfigureAwait(false);
            }

            await claims.ReleaseAsync(lease, nextPassAt, limit.Token).ConfigureAwait(false);
        }
        catch (Exception failure) when (!stopping.IsCancellationRequested)
        {
            SitemapLog.ReleaseFailed(logger, failure, lease.SiteId);
        }
        catch (Exception) when (stopping.IsCancellationRequested)
        {
            // The store went down with the machine, or the claim was lost and there was nothing to hand
            // back. Either way the claim runs out by itself, and nothing is left to say about it.
        }
    }

    private async Task GiveBackAsync(PostWriter writer, SitemapPassContext pass, CancellationToken cancellationToken)
    {
        if (pass.AddressInHand)
        {
            await writer.GiveBackAddressAsync(pass.Lease, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        }

        if (pass.PartInHand is { } part)
        {
            await writer.GiveBackPartAsync(pass.Lease, part, cancellationToken).ConfigureAwait(false);
        }
    }
}
