namespace Dewiride.Analytics.Application.Telemetry;

/// <summary>
/// How far apart in time the two halves of the measurement may say the same thing and still be
/// read as one account of it.
/// </summary>
/// <remarks>
/// <para>
/// A reporter on a site's own server can name each page it delivers, and the tracker in the
/// browser that receives the page sends the name back. Where one browser does, the server's report
/// and the browser's are the same page — and, more usefully, the two keys they were filed under
/// are the same person. Where several do, the page was served to all of them from a copy kept by a
/// cache in between, and the name says nothing about which of them the server saw.
/// </para>
/// <para>
/// Both figures are fixed rather than settings, because every statement that reads the activity
/// has to decide the same way. They are written into those statements as literals, which leaves
/// each statement's bound values as they are.
/// </para>
/// </remarks>
public static class EchoedDeliveries
{
    /// <summary>
    /// How long either side of a server's report a browser's echo of the same name counts as its
    /// answer.
    /// </summary>
    /// <remarks>
    /// A browser that runs the tracker sends the name back within seconds of receiving the page,
    /// and either report can reach the collector first, so the window opens on both sides. Two
    /// minutes covers a slow phone on a poor connection. A copy a cache hands out later than that
    /// is not counted at all, which is what keeps the reader who first asked for a page its own
    /// answer however many readers the cache serves after them.
    /// </remarks>
    public static readonly TimeSpan AnswerWindow = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How far either side of one of a reporter's reports the answers that decide whose it was are
    /// read from.
    /// </summary>
    /// <remarks>
    /// Long enough to keep a redirect, a page that was not there, or a page whose tracker never ran
    /// with the reading around it; short enough that what a key did in the morning says nothing
    /// about whose its evening was.
    /// </remarks>
    public static readonly TimeSpan Reach = TimeSpan.FromMinutes(28);

    /// <summary>
    /// How long after a report everything that can decide whose it was has arrived.
    /// </summary>
    /// <remarks>
    /// A visit is judged only once it has been quiet for the idle timeout, so the idle timeout may
    /// never be shorter than this. If it were, a verdict could be reached on a visit whose reports a
    /// later answer then gave to somebody else, leaving the verdict about a visit nobody can find.
    /// </remarks>
    public static TimeSpan Settled => Reach + AnswerWindow;
}
