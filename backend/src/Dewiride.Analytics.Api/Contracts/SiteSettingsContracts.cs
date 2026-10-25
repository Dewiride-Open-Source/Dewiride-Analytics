namespace Dewiride.Analytics.Api.Contracts;

/// <summary>
/// A website's own settings.
/// </summary>
/// <param name="DisplayName">Name it is shown under.</param>
/// <param name="TimeZoneId">IANA time zone its days are counted in.</param>
/// <param name="CaptureClicks">
/// Whether the controls visitors operate are recorded. What is kept of a press is the words on the
/// control and where it pointed — on a page that shows visitors their own messages, names or
/// accounts those words are theirs — and nothing is ever read out of a text area.
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
