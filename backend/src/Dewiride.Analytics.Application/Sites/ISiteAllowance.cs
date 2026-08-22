namespace Dewiride.Analytics.Application.Sites;

/// <summary>
/// Whether an organisation may take on another site.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <see cref="Ingest.IMeasurementAllowance"/> for the one act that adds to what
/// an installation is measuring. A self-hosted installation has no such limit and its edition
/// answers yes; where somebody else is running the service, how many sites an account may measure
/// is part of what they are paying for.
/// </para>
/// <para>
/// Asked before a site is written and never afterwards. A limit that removed sites somebody had
/// already added would delete measurements they had asked for and paid for, which is not
/// something an allowance may do.
/// </para>
/// </remarks>
public interface ISiteAllowance
{
    /// <summary>
    /// Decides whether an organisation may add one more site.
    /// </summary>
    /// <param name="organizationId">The organisation the site would join.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> where another site may be added.</returns>
    Task<bool> AllowsAnotherAsync(Guid organizationId, CancellationToken cancellationToken);
}
