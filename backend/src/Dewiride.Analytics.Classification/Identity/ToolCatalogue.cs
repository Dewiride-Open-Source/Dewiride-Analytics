using System.Collections.Immutable;

namespace Dewiride.Analytics.Classification.Identity;

/// <summary>
/// Programs that fetch pages and say so, without claiming to be anybody in particular.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="CrawlerCatalogue"/> and held to a different standard on purpose.
/// These are the default user agents of ordinary software libraries and command-line tools, so
/// recognising one attributes the traffic to a <em>kind of program</em> rather than to a company.
/// Getting an entry wrong here mislabels a tool; getting one wrong there would put a real
/// company's name on somebody else's traffic.
/// </para>
/// <para>
/// None of these is evidence of ill intent. A great deal of entirely legitimate traffic is a
/// script somebody wrote, and the engine treats this only as what it is: something that is not a
/// person reading a page.
/// </para>
/// </remarks>
public static class ToolCatalogue
{
    /// <summary>
    /// Tokens that identify a fetching tool, and the everyday name for what it is.
    /// </summary>
    /// <remarks>
    /// The name is a catalogue parameter rather than a sentence, so the interface can render it
    /// in the reader's own language and the golden fixtures compare a token rather than English.
    /// </remarks>
    public static readonly ImmutableArray<(string Token, string Kind)> Known =
    [
        ("HeadlessChrome", "headless-browser"),
        ("Headless", "headless-browser"),
        ("PhantomJS", "headless-browser"),
        ("python-requests", "script"),
        ("python-httpx", "script"),
        ("aiohttp", "script"),
        ("Scrapy", "scraping-framework"),
        ("Go-http-client", "script"),
        ("node-fetch", "script"),
        ("axios", "script"),
        ("okhttp", "script"),
        ("libwww-perl", "script"),
        ("Java/", "script"),
        ("curl/", "command-line"),
        ("Wget/", "command-line"),
        ("HTTPie", "command-line"),
    ];

    /// <summary>
    /// Finds the kind of tool a user agent names itself as.
    /// </summary>
    /// <param name="userAgent">The string the visitor sent. Attacker-controlled.</param>
    /// <returns>The kind, or <see langword="null"/> when nothing recognisable was named.</returns>
    public static string? Match(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        return Known
            .Where(candidate => userAgent.Contains(candidate.Token, StringComparison.OrdinalIgnoreCase))
            .Select(candidate => candidate.Kind)
            .FirstOrDefault();
    }
}
