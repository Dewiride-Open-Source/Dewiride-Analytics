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
/// Two instances of the engine may hold a bookmark for the same site at the same time. They are
/// not coordinated, and they do not need to be: the bookmark only moves forward, and the verdicts
/// either of them writes replace each other rather than accumulating. The worst that happens is
/// that a stretch of traffic is judged twice and stored once.
/// </remarks>
/// <param name="database">Control-plane database.</param>
/// <param name="timeProvider">Source of the stamp recorded against a move.</param>
public sealed class ClassificationProgressStore(ControlPlaneDbContext database, TimeProvider timeProvider)
    : IClassificationProgressStore
{
    /// <inheritdoc />
    public async Task<DateTimeOffset> ResumeFromAsync(
        Guid siteId,
        RulesetVersion ruleset,
        DateTimeOffset ifUnrecorded,
        CancellationToken cancellationToken)
    {
        var existing = await FindAsync(siteId, ruleset, cancellationToken).ConfigureAwait(false);

        if (existing is not null)
        {
            return existing.ClassifiedThrough;
        }

        database.ClassificationProgress.Add(
            new ClassificationProgress(siteId, ruleset.Major, ruleset.Minor, ifUnrecorded));

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return ifUnrecorded;
        }
        catch (DbUpdateException)
        {
            // Another instance started the same bookmark between the read above and this write.
            // Its value is the one to work from — which is the same value this one would have
            // written, since both derive it from when the site was added.
            database.ChangeTracker.Clear();

            var started = await FindAsync(siteId, ruleset, cancellationToken).ConfigureAwait(false);

            return started?.ClassifiedThrough ?? ifUnrecorded;
        }
    }

    /// <inheritdoc />
    public async Task<bool> AdvanceAsync(
        Guid siteId,
        RulesetVersion ruleset,
        DateTimeOffset classifiedThrough,
        CancellationToken cancellationToken)
    {
        var progress = await FindAsync(siteId, ruleset, cancellationToken).ConfigureAwait(false);

        if (progress is null || !progress.AdvanceTo(classifiedThrough, timeProvider.GetUtcNow()))
        {
            return false;
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    private Task<ClassificationProgress?> FindAsync(
        Guid siteId,
        RulesetVersion ruleset,
        CancellationToken cancellationToken) =>
        database.ClassificationProgress.FirstOrDefaultAsync(
            progress => progress.SiteId == siteId
                && progress.RulesetMajor == ruleset.Major
                && progress.RulesetMinor == ruleset.Minor,
            cancellationToken);
}
