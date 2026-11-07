using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Dewiride.Analytics.Domain.Sites;

/// <summary>
/// A website whose traffic is being observed.
/// </summary>
/// <remarks>
/// All telemetry is keyed by site, never by organisation. Sites get transferred between
/// organisations, and re-keying a billion telemetry rows to follow an ownership change is
/// not a migration anyone survives.
/// </remarks>
public sealed class Site
{
    /// <summary>
    /// Longest address a site may be measured under.
    /// </summary>
    /// <remarks>
    /// The ceiling DNS puts on a fully qualified name, and the same width the column is declared
    /// at, so an address the aggregate accepts is one the database can store. Refusing it here
    /// rather than letting the write fail is what turns an over-long address into an answer
    /// somebody can act on instead of a save that reports nothing until it reaches PostgreSQL.
    /// </remarks>
    public const int MaxDomainLength = 253;

    /// <summary>
    /// Longest name a site may be shown under.
    /// </summary>
    /// <remarks>
    /// Taken from the address rather than chosen independently. A site is shown under its address
    /// until somebody renames it, so a name allowed less room than an address would refuse a site
    /// whose address is perfectly legal.
    /// </remarks>
    public const int MaxDisplayNameLength = MaxDomainLength;

    /// <summary>
    /// Longest address a sitemap may be read from.
    /// </summary>
    /// <remarks>
    /// The same ceiling the collector puts on a page's address, and the one the sitemaps.org protocol
    /// puts on a location, so anything a sitemap may list is an address this length can hold. It is
    /// also the width the column is declared at.
    /// </remarks>
    public const int MaxSitemapAddressLength = 2048;

    /// <summary>Asks for the form of a name that a name server knows it by.</summary>
    private static readonly IdnMapping InternationalNames = new();

    /// <summary>
    /// Identity of the site. This value is public: it is embedded in the tracker snippet the
    /// customer pastes into their pages, and it is what the collector matches an incoming
    /// event against. It identifies a site; it grants nothing.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>Organisation that currently owns the site.</summary>
    public Guid OrganizationId { get; private set; }

    /// <summary>Primary hostname, lower-cased and without scheme or trailing dot.</summary>
    public string Domain { get; private set; }

    /// <summary>Name shown in the dashboard. Defaults to the domain.</summary>
    public string DisplayName { get; private set; }

    /// <summary>
    /// IANA time zone the site's owner thinks in, used to bucket "yesterday" and daily
    /// reports. Stored as an identifier rather than an offset so that daylight-saving
    /// transitions are handled correctly.
    /// </summary>
    public string TimeZoneId { get; private set; }

    /// <summary>When the site was added.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Whether query strings are retained on collected events. Off by default: query strings
    /// on content sites routinely carry tracking parameters and occasionally carry things
    /// that should never have been in a URL.
    /// </summary>
    public bool RetainQueryStrings { get; private set; }

    /// <summary>
    /// Whether the controls visitors operate are recorded. Off until somebody switches it on.
    /// </summary>
    /// <remarks>
    /// What is kept of a press is the words on the control, and only somebody who knows the site
    /// knows what those are. On a page that shows nothing but the site's own writing they are its
    /// own names for its own controls; on a page that shows its visitors their own things — their
    /// messages, their names, their accounts — the words on its buttons and links are those
    /// things, and nothing collected here can tell the two apart.
    /// </remarks>
    public bool CaptureClicks { get; private set; }

    /// <summary>
    /// Where the site lists its posts, or nothing where nobody has named a sitemap.
    /// </summary>
    /// <remarks>
    /// Only the address somebody chose, in one spelling: an absolute web address on one of the site's
    /// own hosts, with the host in lower-case ASCII and neither a default port nor a fragment. How
    /// reading it went is the engine's to write, often and from more than one process, so it is kept
    /// in tables of its own rather than on the row every settings save and every report's lookup
    /// reads.
    /// </remarks>
    public string? SitemapAddress { get; private set; }

    private readonly List<string> _allowedOrigins = [];

    /// <summary>
    /// Origins permitted to submit events for this site. Empty means the site's own domain
    /// and its subdomains only. The collector is a public unauthenticated endpoint, so this
    /// is the first line of defence against someone pointing a firehose at another
    /// customer's site identifier.
    /// </summary>
    public IReadOnlyCollection<string> AllowedOrigins => _allowedOrigins.AsReadOnly();

    private Site()
    {
        Domain = string.Empty;
        DisplayName = string.Empty;
        TimeZoneId = "Etc/UTC";
    }

    /// <summary>Registers a site.</summary>
    /// <param name="id">Identity to assign; also the public tracker identifier.</param>
    /// <param name="organizationId">Owning organisation.</param>
    /// <param name="domain">Primary hostname.</param>
    /// <param name="timeZoneId">IANA time zone identifier.</param>
    /// <param name="createdAt">Creation time, from the injected clock.</param>
    /// <exception cref="ArgumentException">
    /// The domain is empty, whitespace, or longer than <see cref="MaxDomainLength"/> once
    /// normalised, or the time zone is not a known IANA identifier.
    /// </exception>
    public Site(Guid id, Guid organizationId, string domain, string timeZoneId, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        RequireKnownTimeZone(timeZoneId);

        // Measured after normalising, so the length checked is the length stored rather than the
        // length typed.
        var normalized = NormalizeDomain(domain);

        if (normalized.Length > MaxDomainLength)
        {
            throw new ArgumentException(
                $"A site's address may be at most {MaxDomainLength} characters.",
                nameof(domain));
        }

        Id = id;
        OrganizationId = organizationId;
        Domain = normalized;
        TimeZoneId = timeZoneId;
        CreatedAt = createdAt;

        // Through the same method a rename goes through, so a site's name is subject to one set of
        // rules whether the site or a person chose it.
        SetDisplayName(Domain);
    }

    /// <summary>Sets the name shown in the dashboard.</summary>
    /// <param name="displayName">The new display name.</param>
    /// <exception cref="ArgumentException">
    /// The name is empty or whitespace, or longer than <see cref="MaxDisplayNameLength"/> once
    /// the surrounding space has been taken off it.
    /// </exception>
    [MemberNotNull(nameof(DisplayName))]
    public void SetDisplayName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var trimmed = displayName.Trim();

        // Measured after trimming, so a name that only overruns because somebody pasted it with
        // space around it is accepted rather than refused for length it does not have.
        if (trimmed.Length > MaxDisplayNameLength)
        {
            throw new ArgumentException(
                $"A site's name may be at most {MaxDisplayNameLength} characters.",
                nameof(displayName));
        }

        DisplayName = trimmed;
    }

    /// <summary>Sets the time zone the site's days are counted in.</summary>
    /// <remarks>
    /// Moving the zone moves every day boundary the site's numbers are cut on, so yesterday's
    /// total is counted again over different hours. Nothing already collected changes: the
    /// identifier travels with each read and the buckets are cut at the moment of asking.
    /// </remarks>
    /// <param name="timeZoneId">IANA time zone identifier.</param>
    /// <exception cref="ArgumentException">
    /// The identifier is empty, whitespace, or not one this installation knows.
    /// </exception>
    public void SetTimeZone(string timeZoneId)
    {
        RequireKnownTimeZone(timeZoneId);
        TimeZoneId = timeZoneId;
    }

    /// <summary>Sets whether query strings are retained on collected events.</summary>
    /// <param name="retain">Whether to retain them.</param>
    public void SetQueryStringRetention(bool retain) => RetainQueryStrings = retain;

    /// <summary>Sets whether the controls visitors operate are recorded.</summary>
    /// <param name="capture">Whether to record them.</param>
    public void SetClickCapture(bool capture) => CaptureClicks = capture;

    /// <summary>
    /// Whether an address is one the engine may read on this site's behalf.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one rule for anything fetched for a website, and it answers rather than throws, whatever
    /// it is given. The addresses put to it come from whoever runs the website: the one typed into
    /// its settings, and every one that a sitemap or a redirect then names.
    /// </para>
    /// <para>
    /// A name with no dot in it covers nothing, as a host or as what the site is registered under. A
    /// site registered as <c>com</c> would otherwise be published at every address below it, and
    /// nothing checks what a site is registered as. A host longer than a name server can be asked
    /// about is refused here too, because <see cref="Uri"/> accepts one and it fits nowhere it would
    /// be kept.
    /// </para>
    /// <para>
    /// Both sides are compared in ASCII. The address already is, and a site registered under its
    /// international spelling would otherwise never match the name a name server knows it by.
    /// </para>
    /// <para>
    /// It does not ask whose file named the address. What a site is registered as can be wide enough
    /// to take in other people's websites, so anything a sitemap leads to has to pass
    /// <see cref="PublishedHosts.SameWebsite"/> as well.
    /// </para>
    /// </remarks>
    /// <param name="address">The address.</param>
    /// <returns>
    /// Whether it is an absolute web address carrying no credentials, on the default port, whose host
    /// is a name on one of the site's own hosts.
    /// </returns>
    public bool Publishes(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return IsPlainWebAddress(address)
            && TryReadHost(address, out var host)
            && PublishedHosts.Covers(CoveringName(Domain), [.. _allowedOrigins.Select(CoveringName)], host);
    }

    /// <summary>Sets where the site lists its posts.</summary>
    /// <remarks>
    /// The address is kept in one spelling, so that the same sitemap typed twice is the same sitemap.
    /// Its length is measured as it was typed, so that a long enough paste is refused without being
    /// parsed, and again as it would be stored, because escaping can make it longer. A query string
    /// is kept, since a sitemap can be served from one.
    /// </remarks>
    /// <param name="address">The address, or nothing or blank to stop reading one.</param>
    /// <exception cref="ArgumentException">
    /// The address is not one <see cref="Publishes"/> accepts, or is longer than
    /// <see cref="MaxSitemapAddressLength"/>.
    /// </exception>
    public void SetSitemapAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            SitemapAddress = null;

            return;
        }

        SitemapAddress = InOneSpelling(address.Trim())
            ?? throw new ArgumentException(
                $"A site's sitemap must be a whole web address of at most {MaxSitemapAddressLength} "
                + "characters on one of the site's own hosts.",
                nameof(address));
    }

    /// <summary>Replaces the set of origins permitted to submit events.</summary>
    /// <param name="origins">The permitted origins. Null or empty restores the default.</param>
    public void ReplaceAllowedOrigins(IEnumerable<string>? origins)
    {
        _allowedOrigins.Clear();

        if (origins is null)
        {
            return;
        }

        _allowedOrigins.AddRange(
            origins.Where(origin => !string.IsNullOrWhiteSpace(origin))
                   .Select(NormalizeDomain));
    }

    /// <summary>Moves the site to a different organisation.</summary>
    /// <param name="organizationId">The organisation to move it to.</param>
    public void TransferTo(Guid organizationId) => OrganizationId = organizationId;

    /// <summary>
    /// Refuses a time zone this installation cannot resolve.
    /// </summary>
    /// <remarks>
    /// One definition, used wherever a zone is set. The identifier reaches the telemetry store as
    /// the zone its daily buckets are cut in, so an unrecognised one has to be refused where it is
    /// set rather than surfacing later as a report that will not run.
    /// </remarks>
    /// <param name="timeZoneId">IANA time zone identifier.</param>
    /// <exception cref="ArgumentException">
    /// The identifier is empty, whitespace, or not one this installation knows.
    /// </exception>
    private static void RequireKnownTimeZone(string timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _))
        {
            throw new ArgumentException(
                $"'{timeZoneId}' is not a known IANA time zone identifier.",
                nameof(timeZoneId));
        }
    }

    private static string NormalizeDomain(string value) =>
        value.Trim().TrimEnd('.').ToLowerInvariant();

    /// <summary>
    /// The address as it is kept, or nothing where it cannot be a sitemap of this site's.
    /// </summary>
    /// <param name="typed">The address, without the space around it.</param>
    /// <returns>The address in one spelling, or nothing where it is refused.</returns>
    private string? InOneSpelling(string typed)
    {
        if (typed.Length > MaxSitemapAddressLength
            || !Uri.TryCreate(typed, UriKind.Absolute, out var parsed)
            || !Publishes(parsed))
        {
            return null;
        }

        var spelled = $"{parsed.Scheme}://{parsed.IdnHost.TrimEnd('.')}{parsed.PathAndQuery}";

        return spelled.Length > MaxSitemapAddressLength ? null : spelled;
    }

    /// <summary>
    /// Whether an address is one that could be fetched from a named web server as anybody could.
    /// </summary>
    /// <remarks>
    /// The default port and a named host are what keep a reading off whatever else a machine answers
    /// on: another port on a website's own server, or an address written as a number, which no name
    /// server was ever asked about.
    /// </remarks>
    private static bool IsPlainWebAddress(Uri address) =>
        address.IsAbsoluteUri
        && (address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp)
        && address.UserInfo.Length == 0
        && address.IsDefaultPort
        && address.HostNameType == UriHostNameType.Dns;

    /// <summary>
    /// The host of an address as a name server would be asked for it, where it can be.
    /// </summary>
    /// <remarks>
    /// <see cref="Uri"/> parses a host it then cannot turn into ASCII, such as one with a joiner in a
    /// label, and only says so when it is asked for the ASCII form. It is asked here, once, so that no
    /// sitemap can make that the failure of whatever asked. The ASCII form has to be a name in its own
    /// right as well: some characters, such as a full-width less-than sign, are mapped to ones no name
    /// may hold, or to the dots of a number, and the address kept in that spelling could not be read
    /// back.
    /// </remarks>
    /// <param name="address">An absolute address with a named host.</param>
    /// <param name="host">The host, where the address has one that names a website.</param>
    /// <returns>Whether it does.</returns>
    private static bool TryReadHost(Uri address, out string host)
    {
        try
        {
            host = address.IdnHost.TrimEnd('.');

            return NamesAWebsite(host) && Uri.CheckHostName(host) == UriHostNameType.Dns;
        }
        catch (UriFormatException)
        {
            host = string.Empty;

            return false;
        }
    }

    /// <summary>
    /// A name the site is registered under, as an address's host would be compared with it.
    /// </summary>
    /// <remarks>
    /// A name that cannot cover anything becomes an empty string rather than being left out, and it
    /// matters which. Permitted hosts replace the domain only while there are any, so leaving out the
    /// last one would quietly put the whole domain back.
    /// </remarks>
    /// <param name="name">The name.</param>
    /// <returns>Its ASCII form, or an empty string, which matches no host read from an address.</returns>
    private static string CoveringName(string name)
    {
        try
        {
            var ascii = Ascii.IsValid(name) ? name : InternationalNames.GetAscii(name);

            return NamesAWebsite(ascii) ? ascii : string.Empty;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Whether a name could be a website's: in ASCII, of a length a name server can be asked about,
    /// and more than a bare suffix.
    /// </summary>
    private static bool NamesAWebsite(string name) =>
        name.Length <= MaxDomainLength
        && Ascii.IsValid(name)
        && name.Contains('.', StringComparison.Ordinal);
}
