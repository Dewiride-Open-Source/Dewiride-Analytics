using System.Collections.Frozen;
using System.Globalization;

namespace Dewiride.Analytics.Application.Ingest;

/// <summary>
/// The websites whose visitors an installation does not keep.
/// </summary>
/// <remarks>
/// <para>
/// An operator's decision, not a verdict. The engine judges a visit by what it did; a visit that
/// names a sender on this list is never judged at all. It is turned away as it arrives, with
/// everything its visitor sends until the visit is over, and nothing of it is stored, counted or
/// shown. That suits a sender whose traffic is worth nothing to measure — a campaign whose programs
/// name one website in every visit they make — and it costs any genuine visit that names the same
/// sender, which is turned away alike.
/// </para>
/// <para>
/// A sender stands for itself and every address below it, so <c>example.org</c> covers
/// <c>www.example.org</c>. Names are compared in the ASCII form a browser sends, so a name written
/// in another script and the same name as it travels are one sender.
/// </para>
/// </remarks>
public sealed class IgnoredSenders
{
    /// <summary>Longest host name accepted, which is the longest a domain name can be.</summary>
    private const int MaxNameLength = 253;

    private readonly FrozenSet<string> _names;
    private readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> _lookup;
    private readonly IIgnoredVisitors _visitors;

    /// <summary>
    /// Builds the list an installation was configured with.
    /// </summary>
    /// <param name="senders">Host names, each standing for itself and every address below it.</param>
    /// <param name="visitors">Remembers the visitors whose visit is being turned away.</param>
    /// <exception cref="ArgumentException">An entry is not a host name.</exception>
    public IgnoredSenders(IEnumerable<string> senders, IIgnoredVisitors visitors)
    {
        ArgumentNullException.ThrowIfNull(senders);
        ArgumentNullException.ThrowIfNull(visitors);

        var names = new List<string>();

        foreach (var sender in senders)
        {
            names.Add(Normalized(sender)
                ?? throw new ArgumentException($"'{sender}' is not a host name.", nameof(senders)));
        }

        _names = names.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        _lookup = _names.GetAlternateLookup<ReadOnlySpan<char>>();
        _visitors = visitors;
    }

    /// <summary>
    /// Whether a value can stand on the list: a host name of at least two labels, with no scheme,
    /// port, path or wildcard.
    /// </summary>
    /// <remarks>
    /// One label is refused because it would be a whole top-level domain, and turning away every
    /// visit sent from anywhere under <c>org</c> is a typing mistake rather than a decision.
    /// </remarks>
    /// <param name="value">The value as configured.</param>
    /// <returns>Whether it is one.</returns>
    public static bool IsHostName(string? value) => Normalized(value) is not null;

    /// <summary>
    /// Whether a report belongs to a visit this installation does not keep.
    /// </summary>
    /// <remarks>
    /// An arrival from a sender on the list is turned away and its visitor remembered; every later
    /// report from a visitor still remembered is turned away too. With nothing on the list, nothing
    /// is turned away and nothing is remembered.
    /// </remarks>
    /// <param name="siteId">The website the report is for.</param>
    /// <param name="sender">
    /// The host of the website a report says its page was reached from, in the form a URI gives it;
    /// nothing where it names none, and nothing where the page was reached from the website itself.
    /// </param>
    /// <param name="visitorKey">The visitor, or nothing where no key could be derived.</param>
    /// <returns>Whether the report is to be turned away.</returns>
    public bool TurnsAway(Guid siteId, string? sender, string? visitorKey)
    {
        if (_names.Count == 0)
        {
            return false;
        }

        if (sender is not null && Covers(sender))
        {
            if (visitorKey is not null)
            {
                _visitors.Remember(siteId, visitorKey);
            }

            return true;
        }

        return visitorKey is not null && _visitors.Recalls(siteId, visitorKey);
    }

    /// <summary>Whether a host is a name on the list or an address below one.</summary>
    private bool Covers(string host)
    {
        var name = host.AsSpan().TrimEnd('.');

        while (!_lookup.Contains(name))
        {
            var dot = name.IndexOf('.');

            if (dot < 0)
            {
                return false;
            }

            name = name[(dot + 1)..];
        }

        return true;
    }

    /// <summary>
    /// The form a configured name is compared in, or nothing where the value is not a host name.
    /// </summary>
    private static string? Normalized(string? value)
    {
        var trimmed = value?.Trim().TrimEnd('.');

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxNameLength)
        {
            return null;
        }

        string ascii;

        try
        {
            // The rules for what a host name may hold, so that a scheme, a port, a path, a space or
            // a wildcard is refused here rather than read as part of a name nobody could send.
            ascii = new IdnMapping { UseStd3AsciiRules = true }.GetAscii(trimmed).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return null;
        }

        return ascii.Contains('.') && Uri.CheckHostName(ascii) == UriHostNameType.Dns ? ascii : null;
    }
}
