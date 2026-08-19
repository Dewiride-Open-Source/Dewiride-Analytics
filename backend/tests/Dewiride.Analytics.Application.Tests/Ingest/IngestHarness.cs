using System.Collections.Immutable;
using Dewiride.Analytics.Application.Ingest;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Domain.Telemetry;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Dewiride.Analytics.Application.Tests.Ingest;

/// <summary>
/// Assembles an <see cref="EventIngestor"/> over controllable collaborators.
/// </summary>
/// <remarks>
/// The clock is frozen rather than read, because every timestamp and every derived value on a
/// stored event depends on it and an assertion against a moving clock is an assertion against
/// nothing. The sink records instead of verifying, so a test can state what was stored rather
/// than restate the call that stored it.
/// </remarks>
internal sealed class IngestHarness
{
    /// <summary>The instant every ingest in these tests is stamped with.</summary>
    public static readonly DateTimeOffset Now = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Identifier of the site the harness resolves.</summary>
    public static readonly Guid SiteId = new("0197c0de-0000-7000-8000-000000000001");

    /// <summary>What the network lookup resolves the harness's address to.</summary>
    public static readonly NetworkAttributes Somewhere =
        new("IN", "MH", "Pune", 24560, "Bharti Airtel");

    private readonly ISiteCatalog _catalog = Substitute.For<ISiteCatalog>();
    private readonly IVisitorKeyFactory _visitorKeys = Substitute.For<IVisitorKeyFactory>();
    private readonly INetworkLookup _network = Substitute.For<INetworkLookup>();
    private readonly RecordingSink _sink = new();

    private IngestHarness(SiteSnapshot? site, string? visitorKey, NetworkAttributes network)
    {
        _catalog.FindAsync(SiteId, Arg.Any<CancellationToken>()).Returns(site);
        _visitorKeys
            .Derive(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset>())
            .Returns(visitorKey);
        _network.Resolve(Arg.Any<string?>()).Returns(network);

        Ingestor = new EventIngestor(_catalog, _visitorKeys, _network, _sink, new FakeTimeProvider(Now));
    }

    /// <summary>The subject under test.</summary>
    public EventIngestor Ingestor { get; }

    /// <summary>Events the ingestor stored, in the order it stored them.</summary>
    public IReadOnlyList<RawEvent> Stored => _sink.Written;

    /// <summary>The single event the ingestor stored.</summary>
    public RawEvent Single => _sink.Written.Should().ContainSingle().Subject;

    /// <summary>Builds a harness whose site catalog resolves one site.</summary>
    /// <param name="domain">The site's primary hostname.</param>
    /// <param name="retainQueryStrings">Whether the site keeps query strings.</param>
    /// <param name="captureClicks">Whether the site records the controls its visitors operate.</param>
    /// <param name="allowedOrigins">Origins the site permits, or empty for its own domain only.</param>
    /// <param name="visitorKey">The key the visitor-key factory returns.</param>
    /// <param name="network">What the address resolves to, or nothing when it resolves to nothing.</param>
    /// <returns>The harness.</returns>
    public static IngestHarness ForSite(
        string domain = "example.com",
        bool retainQueryStrings = false,
        bool captureClicks = true,
        IEnumerable<string>? allowedOrigins = null,
        string? visitorKey = "9f2a1c4e8b6d0a3f",
        NetworkAttributes? network = null) =>
        new(
            new SiteSnapshot
            {
                Id = SiteId,
                Domain = domain,
                RetainQueryStrings = retainQueryStrings,
                CaptureClicks = captureClicks,
                AllowedOrigins = allowedOrigins?.ToImmutableArray() ?? [],
            },
            visitorKey,
            network ?? Somewhere);

    /// <summary>Builds a harness whose site catalog resolves nothing.</summary>
    /// <returns>The harness.</returns>
    public static IngestHarness WithNoSuchSite() =>
        new(site: null, visitorKey: null, network: NetworkAttributes.Unresolved);

    /// <summary>Runs one ingest.</summary>
    /// <param name="command">The report.</param>
    /// <param name="context">What the server observed, or a browser report from the site itself.</param>
    /// <returns>The outcome.</returns>
    public Task<IngestOutcome> IngestAsync(IngestCommand command, IngestContext? context = null) =>
        Ingestor.IngestAsync(command, context ?? BrowserRequest(), CancellationToken.None);

    /// <summary>Builds a report of the shape the browser tracker sends.</summary>
    /// <param name="url">Absolute URL of the page.</param>
    /// <param name="kind">What is being reported.</param>
    /// <returns>The report.</returns>
    public static IngestCommand PageView(string url = "https://example.com/posts/hello", EventKind kind = EventKind.PageView) =>
        new()
        {
            SiteId = SiteId,
            Kind = kind,
            Url = url,
        };

    /// <summary>Builds the server-side half of a browser request.</summary>
    /// <param name="origin">Value of the Origin header, or null when the browser sent none.</param>
    /// <returns>What the server observed.</returns>
    public static IngestContext BrowserRequest(string? origin = "https://example.com") => new()
    {
        Surface = IngestSurface.BrowserTracker,
        // A whole one rather than an abbreviation. Its later half is where a browser names
        // itself, so a shortened string would make every test about what a visit was made on
        // agree with the wrong answer.
        UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
            + "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
        IpAddress = "203.0.113.7",
        RequestOrigin = origin,
    };

    private sealed class RecordingSink : IEventSink
    {
        public List<RawEvent> Written { get; } = [];

        public Task WriteAsync(RawEvent rawEvent, CancellationToken cancellationToken)
        {
            Written.Add(rawEvent);
            return Task.CompletedTask;
        }

        public Task WriteBatchAsync(IReadOnlyCollection<RawEvent> rawEvents, CancellationToken cancellationToken)
        {
            Written.AddRange(rawEvents);
            return Task.CompletedTask;
        }
    }
}
