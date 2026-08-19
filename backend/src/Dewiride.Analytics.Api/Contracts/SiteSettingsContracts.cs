namespace Dewiride.Analytics.Api.Contracts;

/// <summary>
/// A website's own settings.
/// </summary>
/// <param name="DisplayName">Name it is shown under.</param>
/// <param name="TimeZoneId">IANA time zone its days are counted in.</param>
/// <param name="CaptureClicks">
/// Whether the controls visitors operate are recorded. What is kept of a press is the site's own
/// name for its own control and where that control pointed, never anything a visitor entered.
/// </param>
public sealed record SiteSettingsResponse(string DisplayName, string TimeZoneId, bool CaptureClicks);

/// <summary>
/// A change to a website's settings.
/// </summary>
/// <remarks>
/// Every setting is optional and absent means unchanged, so a caller that knows about one setting
/// cannot reset another it has never heard of by leaving it out.
/// </remarks>
public sealed record UpdateSiteSettingsRequest
{
    /// <summary>The name to show the website under.</summary>
    public string? DisplayName { get; init; }

    /// <summary>IANA time zone its days should be counted in from now on.</summary>
    public string? TimeZoneId { get; init; }

    /// <summary>Whether the controls visitors operate should be recorded from now on.</summary>
    public bool? CaptureClicks { get; init; }
}
