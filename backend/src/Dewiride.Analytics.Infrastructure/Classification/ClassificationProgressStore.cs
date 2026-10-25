using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Classification;
using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Dewiride.Analytics.Infrastructure.Classification;

/// <summary>
/// Keeps each site's judging bookmark in the control-plane database.
/// </summary>
/// <remarks>
/// <para>
/// Two instances of the engine may hold a bookmark for the same site at the same time. They are
/// not coordinated, and they do not need to be: the bookmark only moves forward, and the verdicts
/// either of them writes replace each other rather than accumulating. The worst that happens is
/// that a stretch of traffic is judged twice and stored once.
/// </para>
/// <para>
/// Forward only is the database's to enforce rather than either instance's, because only the
/// database sees both of them. A bookmark is moved by one statement that takes, for each of its two
/// instants, the later of what it holds and what it is offered, so an instance a moment behind
/// cannot take back what another has recorded — however long it has been holding the values it
/// read when its run began.
/// </para>
/// </remarks>
/// <param name="database">Control-plane database.</param>
/// <param name="timeProvider">Source of the stamp recorded against a move.</param>
public sealed class ClassificationProgressStore(ControlPlaneDbContext database, TimeProvider timeProvider)
    : IClassificationProgressStore
{
    /// <inheritdoc />
    public async Task<ClassificationBookmark> ResumeFromAsync(
        Guid siteId,
        RulesetVersion ruleset,
        DateTimeOffset ifUnrecorded,
        CancellationToken cancellationToken)
    {
        var existing = await FindAsync(siteId, ruleset, cancellationToken).ConfigureAwait(false);

        if (existing is not null)
        {
            return existing.Value;
        }

        database.ClassificationProgress.Add(
            new ClassificationProgress(siteId, ruleset.Major, ruleset.Minor, ifUnrecorded));

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new ClassificationBookmark(ifUnrecorded, ifUnrecorded);
        }
        catch (DbUpdateException)
        {
            // Another instance started the same bookmark between the read above and this write.
            // Its value is the one to work from — which is the same value this one would have
            // written, since both derive it from when the site was added.
            database.ChangeTracker.Clear();

            var started = await FindAsync(siteId, ruleset, cancellationToken).ConfigureAwait(false);

            return started ?? new ClassificationBookmark(ifUnrecorded, ifUnrecorded);
        }
    }

    /// <inheritdoc />
    public async Task<bool> AdvanceAsync(
        Guid siteId,
        RulesetVersion ruleset,
        ClassificationBookmark reached,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(reached.ClassifiedThrough, reached.ReadThrough);

        var judged = reached.ClassifiedThrough;
        var read = reached.ReadThrough;
        var at = timeProvider.GetUtcNow();

        var moved = await database.ClassificationProgress
            .Where(progress => progress.SiteId == siteId
                && progress.RulesetMajor == ruleset.Major
                && progress.RulesetMinor == ruleset.Minor
                && (progress.ClassifiedThrough < judged || progress.ReadThrough < read))
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(
                        progress => progress.ClassifiedThrough,
                        progress => progress.ClassifiedThrough < judged ? judged : progress.ClassifiedThrough)
                    .SetProperty(
                        progress => progress.ReadThrough,
                        progress => progress.ReadThrough < read ? read : progress.ReadThrough)
                    .SetProperty(progress => progress.UpdatedAt, at),
                cancellationToken)
            .ConfigureAwait(false);

        return moved > 0;
    }

    /// <inheritdoc />
    public Task<DateTimeOffset?> CaughtUpToAsync(
        Guid siteId,
        RulesetVersion running,
        CancellationToken cancellationToken) =>
        database.ClassificationProgress
            .AsNoTracking()
            .Where(progress => progress.SiteId == siteId)
            .MaxAsync(
                progress => (DateTimeOffset?)(progress.RulesetMajor == running.Major
                    && progress.RulesetMinor == running.Minor
                    && progress.ReadThrough > progress.ClassifiedThrough
                        ? progress.ReadThrough
                        : progress.ClassifiedThrough),
                cancellationToken);

    /// <summary>
    /// Reads one bookmark as the classifier works from it.
    /// </summary>
    /// <remarks>
    /// Everything judged has been read, so the reading is taken to be at least as far as the
    /// judging whatever the row holds. A release that keeps only the judging instant — one this
    /// installation has been rolled back to — moves it alone, and starts a bookmark with the
    /// column's default, which lies before anything that can be judged; either way the reading is
    /// left behind the judging.
    /// </remarks>
    private async Task<ClassificationBookmark?> FindAsync(
        Guid siteId,
        RulesetVersion ruleset,
        CancellationToken cancellationToken)
    {
        var found = await database.ClassificationProgress
            .AsNoTracking()
            .Where(progress => progress.SiteId == siteId
                && progress.RulesetMajor == ruleset.Major
                && progress.RulesetMinor == ruleset.Minor)
            .Select(progress => new { progress.ClassifiedThrough, progress.ReadThrough })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (found is null)
        {
            return null;
        }

        var read = found.ReadThrough > found.ClassifiedThrough ? found.ReadThrough : found.ClassifiedThrough;

        return new ClassificationBookmark(found.ClassifiedThrough, read);
    }
}
