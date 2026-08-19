namespace Dewiride.Analytics.Api.Contracts;

/// <summary>
/// What a website collects, as far as its owner decides it.
/// </summary>
/// <param name="CaptureClicks">
/// Whether the controls visitors operate are recorded. What is kept of a press is the site's own
/// name for its own control and where that control pointed, never anything a visitor entered.
/// </param>
public sealed record SiteSettingsResponse(bool CaptureClicks);

/// <summary>
/// A change to what a website collects.
/// </summary>
/// <remarks>
/// Every setting is optional and absent means unchanged, so a caller that knows about one setting
/// cannot switch off another it has never heard of by leaving it out.
/// </remarks>
public sealed record UpdateSiteSettingsRequest
{
    /// <summary>Whether the controls visitors operate should be recorded from now on.</summary>
    public bool? CaptureClicks { get; init; }
}
