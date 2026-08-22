using Dewiride.Analytics.Application.Ingest;
using Dewiride.Analytics.Application.Sites;

namespace Dewiride.Analytics.Infrastructure.Entitlements;

/// <summary>
/// An installation whose owner decides what it measures.
/// </summary>
/// <remarks>
/// <para>
/// The open-source edition's answer to both allowances, and it is a real answer rather than a
/// stand-in: somebody running this on their own server is already paying for the machine it runs
/// on, and there is nobody for the product to ration them on behalf of. Every site they point at
/// it is measured, and they may add as many as they like.
/// </para>
/// <para>
/// It is one class answering two ports because it is one decision. Two classes, each saying yes,
/// would suggest there was a case where they differed.
/// </para>
/// </remarks>
public sealed class UnmeteredInstallation : IMeasurementAllowance, ISiteAllowance
{
    /// <inheritdoc />
    public ValueTask<bool> AllowsAsync(SiteSnapshot site, CancellationToken cancellationToken) =>
        ValueTask.FromResult(true);

    /// <inheritdoc />
    public Task<bool> AllowsAnotherAsync(Guid organizationId, CancellationToken cancellationToken) =>
        Task.FromResult(true);
}
