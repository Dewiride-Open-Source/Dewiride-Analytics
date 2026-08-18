using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Dewiride.Analytics.Infrastructure.Sites;

/// <summary>
/// Creates, lists and withdraws a site's server keys.
/// </summary>
/// <param name="database">Control-plane database.</param>
/// <param name="timeProvider">Clock the created and withdrawn times are taken from.</param>
public sealed class IngestKeyDirectory(ControlPlaneDbContext database, TimeProvider timeProvider)
    : IIngestKeyDirectory
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<IngestKeyDescription>> ListAsync(
        Guid siteId,
        CancellationToken cancellationToken) =>
        await Live(siteId)
            .AsNoTracking()
            .OrderByDescending(key => key.CreatedAt)
            .ThenBy(key => key.Id)
            .Select(key => new IngestKeyDescription(
                key.Id,
                key.Name,
                key.Preview,
                key.CreatedAt,
                key.LastUsedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IssuedIngestKey> IssueAsync(
        Guid siteId,
        string name,
        CancellationToken cancellationToken)
    {
        var createdAt = timeProvider.GetUtcNow();
        var (secret, hash, preview) = IngestKeySecret.Create();

        var key = new SiteIngestKey(
            Guid.CreateVersion7(createdAt),
            siteId,
            name,
            hash,
            preview,
            createdAt);

        await database.AddAsync(key, cancellationToken).ConfigureAwait(false);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new IssuedIngestKey(
            new IngestKeyDescription(key.Id, key.Name, key.Preview, key.CreatedAt, key.LastUsedAt),
            secret);
    }

    /// <inheritdoc />
    public async Task<bool> RevokeAsync(Guid siteId, Guid keyId, CancellationToken cancellationToken)
    {
        var key = await Live(siteId)
            .FirstOrDefaultAsync(candidate => candidate.Id == keyId, cancellationToken)
            .ConfigureAwait(false);

        if (key is null)
        {
            return false;
        }

        key.Revoke(timeProvider.GetUtcNow());
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// The keys on a site that have not been withdrawn.
    /// </summary>
    /// <remarks>
    /// Every query here is scoped by site before it is scoped by anything else, so a key
    /// identifier belonging to another site cannot be reached even by naming it exactly.
    /// </remarks>
    private IQueryable<SiteIngestKey> Live(Guid siteId) =>
        database.SiteIngestKeys.Where(key => key.SiteId == siteId && key.RevokedAt == null);
}
