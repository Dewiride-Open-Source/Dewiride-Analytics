using Dewiride.Analytics.Application.Posts;
using Dewiride.Analytics.Infrastructure.Persistence;
using Dewiride.Analytics.Infrastructure.Posts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// Writes what a reading of a website's sitemap leaves behind, without one having happened.
/// </summary>
/// <remarks>
/// For the tests about what becomes of those rows, which are about settings, status and removal
/// rather than about fetching anything.
/// </remarks>
internal static class SitemapSeed
{
    /// <summary>
    /// Writes one reading of a website's sitemap: how far it got, one part, and one post listed in
    /// that part.
    /// </summary>
    /// <param name="stack">The running stack.</param>
    /// <param name="siteId">The website.</param>
    /// <param name="address">The sitemap the reading was of.</param>
    /// <returns>A task that completes once the rows are saved.</returns>
    public static async Task AddReadingAsync(AnalyticsStackFixture stack, Guid siteId, string address)
    {
        var at = Now(stack);
        var part = Guid.CreateVersion7(at);

        await WriteAsync(
            stack,
            Read(siteId, address, at),
            new SitemapPart
            {
                Id = part,
                SiteId = siteId,
                Address = address,
            },
            new SitePost
            {
                SiteId = siteId,
                Path = "/posts/hello",
                Address = new Uri(new Uri(address), "/posts/hello").AbsoluteUri,
                PartId = part,
                ListedInPostsPart = true,
                FirstListedAt = at,
            }).ConfigureAwait(false);
    }

    /// <summary>A row saying how far reading an address got.</summary>
    /// <param name="siteId">The website.</param>
    /// <param name="address">The address.</param>
    /// <param name="at">When it was written.</param>
    /// <param name="state">How the reading stands.</param>
    /// <param name="problem">What got in its way.</param>
    /// <returns>The row, not yet saved.</returns>
    public static SitemapRead Read(
        Guid siteId,
        string address,
        DateTimeOffset at,
        SitemapState state = SitemapState.Read,
        SitemapProblem? problem = null) =>
        new()
        {
            SiteId = siteId,
            Address = address,
            Host = new Uri(address).IdnHost,
            NextPassAt = at,
            NextCycleAt = at,
            State = state,
            Problem = problem,
            ReadAt = at,
            SucceededAt = at,
            UpdatedAt = at,
        };

    /// <summary>
    /// The present moment by the host's own clock, for rows that have to be stamped with one.
    /// </summary>
    /// <remarks>
    /// Cut to the microsecond, which is as finely as the database keeps a time, so that a moment read
    /// back compares equal to the one that was written.
    /// </remarks>
    /// <param name="stack">The running stack.</param>
    /// <returns>The moment.</returns>
    public static DateTimeOffset Now(AnalyticsStackFixture stack)
    {
        var now = stack.Services.GetRequiredService<TimeProvider>().GetUtcNow();

        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
    }

    /// <summary>Saves rows of the reading tables.</summary>
    /// <param name="stack">The running stack.</param>
    /// <param name="rows">The rows.</param>
    /// <returns>A task that completes once they are saved.</returns>
    public static async Task WriteAsync(AnalyticsStackFixture stack, params object[] rows)
    {
        await using var work = stack.Services.CreateAsyncScope();
        var database = work.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        database.AddRange(rows);
        await database.SaveChangesAsync(Cancellation.Token).ConfigureAwait(false);
    }

    /// <summary>How many rows a reading of a website's sitemap has left behind.</summary>
    /// <param name="stack">The running stack.</param>
    /// <param name="siteId">The website.</param>
    /// <returns>The counts, table by table.</returns>
    public static async Task<Left> LeftAsync(AnalyticsStackFixture stack, Guid siteId)
    {
        await using var work = stack.Services.CreateAsyncScope();
        var database = work.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        return new Left(
            await database.SitemapReads
                .CountAsync(read => read.SiteId == siteId, Cancellation.Token)
                .ConfigureAwait(false),
            await database.SitemapParts
                .CountAsync(part => part.SiteId == siteId, Cancellation.Token)
                .ConfigureAwait(false),
            await database.SitePosts
                .CountAsync(post => post.SiteId == siteId, Cancellation.Token)
                .ConfigureAwait(false));
    }

    /// <summary>What a reading of a website's sitemap has left behind, counted.</summary>
    /// <param name="Reads">Rows saying how far the reading got.</param>
    /// <param name="Parts">Files of the sitemap.</param>
    /// <param name="Posts">Pages it listed.</param>
    public readonly record struct Left(int Reads, int Parts, int Posts);
}
