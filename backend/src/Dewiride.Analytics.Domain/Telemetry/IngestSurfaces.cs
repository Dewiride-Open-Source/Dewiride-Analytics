namespace Dewiride.Analytics.Domain.Telemetry;

/// <summary>
/// Facts about capture surfaces that the rest of the product reasons from.
/// </summary>
public static class IngestSurfaces
{
    /// <summary>
    /// Whether a surface reports from inside the visitor's own browser.
    /// </summary>
    /// <param name="surface">The surface that produced an event.</param>
    /// <returns><see langword="true"/> when the report came from the visitor's browser.</returns>
    /// <remarks>
    /// <para>
    /// The distinction decides two things that would otherwise be wrong on every site running the
    /// intended arrangement of a browser tracker and a reporter on its own server. Both halves see
    /// the same page delivered, so one delivery arrives as two reports and would be counted twice;
    /// and only the browser's half was measured from the visitor's own connection, so where the
    /// two disagree about who was there, the browser is the one to believe.
    /// </para>
    /// <para>
    /// A browser surface is the visitor's own software talking. Everything else observed the
    /// request from somewhere in the path between them and the site, on the visitor's behalf.
    /// </para>
    /// </remarks>
    public static bool RunsInVisitorBrowser(IngestSurface surface) =>
        surface is IngestSurface.BrowserTracker or IngestSurface.NoScriptPixel;
}
