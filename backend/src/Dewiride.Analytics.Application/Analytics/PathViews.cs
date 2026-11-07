namespace Dewiride.Analytics.Application.Analytics;

/// <summary>
/// Pages delivered at one path.
/// </summary>
/// <param name="Path">
/// The path, exactly as the collector stored it. A post's path comes from the website's sitemap and a
/// page's from whoever asked for it, so it is data everywhere it travels and never anything else.
/// </param>
/// <param name="PageViews">Pages delivered at it, counted as the dashboard's list of pages counts them.</param>
public readonly record struct PathViews(string Path, long PageViews);
