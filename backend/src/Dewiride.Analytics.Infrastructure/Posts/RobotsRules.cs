using Dewiride.Analytics.Domain.Sites;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// What a website's robots.txt says the reader may ask for.
/// </summary>
/// <remarks>
/// <para>
/// RFC 9309, for one product token. The groups naming the token are read together as one, however many
/// there are and wherever they are; where none does, the groups naming everybody are read the same
/// way; and where neither exists nothing is forbidden. Of the rules that match an address, the longest
/// decides, and an allow decides a tie. The token is compared without regard to case, and the same goes
/// for the names of the lines.
/// </para>
/// <para>
/// The file was written by whoever runs the website, so what it can cost is held down. A rule longer
/// than any address read here could come to once escaped can match nothing, and is left out. A file
/// with more rules for the reader than are kept permits nothing, because there is no telling which of
/// the ones left out would have decided.
/// </para>
/// </remarks>
internal sealed class RobotsRules
{
    /// <summary>Where a website keeps the file, by the protocol's own rule.</summary>
    public const string FilePath = "/robots.txt";

    /// <summary>The most rules kept for one reader.</summary>
    private const int MostRules = 10_000;

    /// <summary>The longest an address read here can come to with every character escaped.</summary>
    private const int LongestRule = 3 * Site.MaxSitemapAddressLength;

    /// <summary>Most specific first, so that the first to match is the one that decides.</summary>
    private readonly RobotsRule[] _rules;

    private RobotsRules(RobotsRule[] rules) => _rules = rules;

    /// <summary>A website with nothing to say about the reader.</summary>
    public static RobotsRules PermitEverything { get; } = new([]);

    /// <summary>A website whose rules can not be followed, so that nothing can be read.</summary>
    public static RobotsRules PermitNothing { get; } = new([new RobotsRule(RobotsPattern.Of("/"), false)]);

    /// <summary>
    /// Reads a robots.txt.
    /// </summary>
    /// <param name="text">The file, as far as it was read.</param>
    /// <param name="productToken">The name the reader looks for.</param>
    /// <param name="whole">
    /// Whether that was all of it. Where it was not, the last line is left out unless it ended, because
    /// the start of a rule is not the rule. An allow cut short would permit more than was written.
    /// </param>
    /// <returns>The rules.</returns>
    public static RobotsRules Read(string text, string productToken, bool whole)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(productToken);

        var complete = whole ? text : text[..(text.LastIndexOfAny(['\r', '\n']) + 1)];
        var file = new RobotsFile(productToken);

        foreach (var line in complete.AsSpan().TrimStart('\uFEFF').EnumerateLines())
        {
            file.Take(line);
        }

        return file.Rules();
    }

    /// <summary>
    /// Whether the website permits an address to be read.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>Whether it may be.</returns>
    public bool Permits(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);

        var path = address.PathAndQuery;

        if (path == FilePath)
        {
            return true;
        }

        var canonical = RobotsPattern.Canonical(path);
        var deciding = Array.FindIndex(_rules, rule => rule.Pattern.Matches(canonical));

        return deciding < 0 || _rules[deciding].Allows;
    }

    /// <summary>
    /// One line that says something about addresses.
    /// </summary>
    /// <param name="Pattern">The addresses it speaks for.</param>
    /// <param name="Allows">What it says about them.</param>
    private readonly record struct RobotsRule(RobotsPattern Pattern, bool Allows);

    /// <summary>
    /// A robots.txt being read, a line at a time.
    /// </summary>
    /// <param name="productToken">The name to look for.</param>
    private sealed class RobotsFile(string productToken)
    {
        private const string Blank = " \t";

        private readonly GroupRules _named = new();
        private readonly GroupRules _everybody = new();
        private bool _withinAGroup;
        private bool _groupHasRules;
        private bool _groupNamesToken;
        private bool _groupNamesEverybody;

        public void Take(ReadOnlySpan<char> line)
        {
            var comment = line.IndexOf('#');
            var said = comment < 0 ? line : line[..comment];
            var colon = said.IndexOf(':');

            if (colon < 0)
            {
                return;
            }

            var key = said[..colon].Trim(Blank);
            var value = said[(colon + 1)..].Trim(Blank);

            if (key.Equals("user-agent", StringComparison.OrdinalIgnoreCase))
            {
                TakeAgent(value);
            }
            else if (key.Equals("allow", StringComparison.OrdinalIgnoreCase))
            {
                TakeRule(value, allows: true);
            }
            else if (key.Equals("disallow", StringComparison.OrdinalIgnoreCase))
            {
                TakeRule(value, allows: false);
            }
        }

        public RobotsRules Rules() => (_named.Seen ? _named : _everybody).Result();

        /// <summary>
        /// Several agent lines in a row name one group, and an agent line after its rules begins the
        /// next. Lines about anything else, such as a sitemap, do neither.
        /// </summary>
        private void TakeAgent(ReadOnlySpan<char> value)
        {
            if (_groupHasRules)
            {
                _groupNamesToken = false;
                _groupNamesEverybody = false;
                _groupHasRules = false;
            }

            _withinAGroup = true;
            _groupNamesEverybody |= value is "*";
            _groupNamesToken |= value[..TokenLength(value)].Equals(productToken, StringComparison.OrdinalIgnoreCase);

            _named.Seen |= _groupNamesToken;
            _everybody.Seen |= _groupNamesEverybody;
        }

        /// <summary>
        /// How much of a line's value is a product token: letters, hyphens and underscores, so that
        /// a version number after the name does not hide the name.
        /// </summary>
        private static int TokenLength(ReadOnlySpan<char> value)
        {
            var length = 0;

            while (length < value.Length && (char.IsAsciiLetter(value[length]) || value[length] is '-' or '_'))
            {
                length++;
            }

            return length;
        }

        /// <summary>
        /// A rule outside any group is nobody's. An empty one is still a rule, so it ends the run of
        /// agent lines, but it matches nothing.
        /// </summary>
        private void TakeRule(ReadOnlySpan<char> value, bool allows)
        {
            if (!_withinAGroup)
            {
                return;
            }

            _groupHasRules = true;

            if (value.IsEmpty || value.Length > LongestRule || !(_groupNamesToken || _groupNamesEverybody))
            {
                return;
            }

            var rule = new RobotsRule(RobotsPattern.Of(value), allows);

            if (_groupNamesToken)
            {
                _named.Add(rule);
            }

            if (_groupNamesEverybody)
            {
                _everybody.Add(rule);
            }
        }
    }

    /// <summary>
    /// The rules of every group naming one reader, read together.
    /// </summary>
    private sealed class GroupRules
    {
        private readonly List<RobotsRule> _rules = [];
        private bool _tooMany;

        /// <summary>Whether a group named the reader at all, with rules or without.</summary>
        public bool Seen { get; set; }

        public void Add(RobotsRule rule)
        {
            if (_rules.Count < MostRules)
            {
                _rules.Add(rule);
            }
            else
            {
                _tooMany = true;
            }
        }

        public RobotsRules Result()
        {
            if (_tooMany)
            {
                return PermitNothing;
            }

            _rules.Sort(static (one, other) => other.Pattern.Octets != one.Pattern.Octets
                ? other.Pattern.Octets.CompareTo(one.Pattern.Octets)
                : other.Allows.CompareTo(one.Allows));

            return new RobotsRules([.. _rules]);
        }
    }
}
