using System.Diagnostics.CodeAnalysis;

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
    /// Whether the controls visitors operate are recorded. On by default: what somebody pressed
    /// is the question the tracker is installed to answer, and what is kept of it is the site's
    /// own name for its own control rather than anything about the person pressing it.
    /// </summary>
    public bool CaptureClicks { get; private set; } = true;

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
        CaptureClicks = true;

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
}
