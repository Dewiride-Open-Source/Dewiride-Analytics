namespace Dewiride.Analytics.Api.Contracts;

/// <summary>
/// A batch of observations from a reporter running on the customer's own server.
/// </summary>
/// <remarks>
/// <para>
/// No site is named. Which site this reports for is decided by the key the caller presented, so a
/// reporter cannot file traffic under a site its key was not issued for, and there is nothing in
/// the body for anyone to change.
/// </para>
/// <para>
/// A batch rather than a single observation because a server sees every request to the site,
/// including the ones no browser ever renders, and one HTTP call per page view back to the
/// collector would cost more than the measuring is worth. A batch of one is perfectly valid and
/// is what an edge function that cannot hold state between requests will send.
/// </para>
/// </remarks>
public sealed record ServerCollectRequest
{
    /// <summary>
    /// Which reporter this is, where it is one this product ships.
    /// </summary>
    /// <remarks>
    /// Recorded as provenance, because what a surface is able to observe decides how its evidence
    /// may be read. An unrecognised name is stored as a server-side reporter of unstated identity
    /// rather than refused, so that adding a surface to a later release does not turn an older
    /// engine into one that rejects it.
    /// </remarks>
    public string? Surface { get; init; }

    /// <summary>The observations, oldest first.</summary>
    public IReadOnlyList<ServerObservation>? Events { get; init; }
}

/// <summary>
/// One request the customer's server observed.
/// </summary>
/// <remarks>
/// Everything here is asserted by the caller, including the values the collector would otherwise
/// read from the connection. That is the whole purpose of the key: a reporter sits between the
/// visitor and this endpoint, so the address and user agent it forwards are the visitor's rather
/// than its own, and only somebody the site trusts may make that substitution. Nothing on this
/// type may be interpolated into SQL, rendered as HTML, or placed in a model prompt as
/// instructions.
/// </remarks>
public sealed record ServerObservation
{
    /// <summary>What is being reported: <c>pageview</c>, <c>engagement</c> or <c>exit</c>.</summary>
    public string? Kind { get; init; }

    /// <summary>Absolute URL that was requested.</summary>
    public string? Url { get; init; }

    /// <summary>Referring URL the visitor's browser sent, where there was one.</summary>
    public string? Referrer { get; init; }

    /// <summary>The visitor's network address, not the reporter's.</summary>
    public string? IpAddress { get; init; }

    /// <summary>The user agent the visitor sent, not the reporter's.</summary>
    public string? UserAgent { get; init; }

    /// <summary>
    /// Status the site returned.
    /// </summary>
    /// <remarks>
    /// Worth more than it looks. Security scanners are recognised almost entirely by streams of
    /// requests to paths that were never there, and this is the only kind of surface that can see
    /// one.
    /// </remarks>
    public short? StatusCode { get; init; }

    /// <summary>Content type of the response.</summary>
    public string? ContentType { get; init; }

    /// <summary>Bytes sent in the response.</summary>
    public long? ResponseBytes { get; init; }

    /// <summary>Primary language the visitor's browser asked for.</summary>
    public string? Language { get; init; }

    /// <summary>
    /// When the reporter observed the request, in Unix milliseconds.
    /// </summary>
    /// <remarks>
    /// Never used as the event's time — the collector stamps that on receipt. It is kept so that
    /// the gap between the two is recorded, which is how a batch delivered late is told apart
    /// from one delivered promptly.
    /// </remarks>
    public long? ObservedAt { get; init; }

    /// <summary>Identifier stamped into the served page so a later browser report can be matched to it.</summary>
    public string? CorrelationId { get; init; }
}

/// <summary>
/// What became of a batch.
/// </summary>
/// <remarks>
/// This endpoint answers plainly, unlike the browser collector, which says nothing at all. The
/// difference is that this caller has already proved it holds a key for the site, so there is
/// nothing left for the answer to disclose — and whoever is writing the reporter needs to know
/// whether it is working.
/// </remarks>
/// <param name="Accepted">Observations that were stored.</param>
/// <param name="Rejected">
/// Observations that could not be used, because they were malformed or named a page on a
/// hostname this site does not cover.
/// </param>
public sealed record ServerCollectResponse(int Accepted, int Rejected);
