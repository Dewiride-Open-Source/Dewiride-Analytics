using System.Buffers;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// What a website answered with, or what came of asking it.
/// </summary>
/// <remarks>
/// <para>
/// It owns the one deadline the whole of the asking runs under, which covers the headers and every
/// byte of the body. The HTTP client's own timeout stops at the headers once the body is being
/// streamed, and a website can start its answer at once and then drip it out for as long as it likes.
/// </para>
/// <para>
/// When the deadline passes the response is disposed as well as the reads being cancelled. A reader
/// with no way of being cancelled is unblocked by the connection going away from under it, so the
/// deadline holds even for a read that was somehow not asked under it.
/// </para>
/// <para>
/// Dispose this as soon as the answer has been read. The connection and the deadline are both held
/// until it is.
/// </para>
/// </remarks>
internal sealed class Fetched : IDisposable
{
    private readonly FetchKind _kind;
    private readonly BodyLimits _limits;
    private readonly FetchDeadline _deadline;
    private readonly HttpResponseMessage? _response;
    private readonly CancellationTokenRegistration _abandonOnDeadline;
    private byte[]? _rented;

    /// <summary>What came of asking, with no body to read.</summary>
    /// <param name="outcome">What came of it.</param>
    /// <param name="address">The address that came of it.</param>
    /// <param name="kind">What was asked for.</param>
    /// <param name="deadline">The time the asking had, which this takes over.</param>
    /// <param name="retryAfter">How long the website asked to be left alone for, where it did.</param>
    public Fetched(
        FetchOutcome outcome,
        Uri address,
        FetchKind kind,
        FetchDeadline deadline,
        TimeSpan? retryAfter = null)
    {
        Outcome = outcome;
        Address = address;
        RetryAfter = retryAfter;
        Body = Stream.Null;
        _kind = kind;
        _deadline = deadline;
    }

    /// <summary>An answer with a body to read.</summary>
    /// <param name="response">The response, which this takes over.</param>
    /// <param name="body">Its body, opened, which this takes over.</param>
    /// <param name="limits">The most of it that is read.</param>
    /// <param name="address">The address that answered.</param>
    /// <param name="kind">What was asked for.</param>
    /// <param name="deadline">The time the asking has, which this takes over.</param>
    public Fetched(
        HttpResponseMessage response,
        Stream body,
        BodyLimits limits,
        Uri address,
        FetchKind kind,
        FetchDeadline deadline)
        : this(FetchOutcome.Succeeded, address, kind, deadline)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(deadline);

        _response = response;
        _limits = limits;
        Body = body;
        Validators = WebsiteValidators.From(response);
        ContentType = response.Content.Headers.ContentType?.ToString();
        _abandonOnDeadline = deadline.Token.Register(response.Dispose);
    }

    /// <summary>What came of asking.</summary>
    public FetchOutcome Outcome { get; }

    /// <summary>
    /// Whether this ended at a redirect, on what robots.txt said about where it leads or could not say about
    /// it. That is <see cref="FetchOutcome.RobotsDisallowed"/> for the one, and what stopped the file being
    /// read for the other, which is not what the address that was asked for answered.
    /// </summary>
    public bool StoppedByRobots { get; private init; }

    /// <summary>The address that answered, which redirects may have moved on from the one asked for.</summary>
    public Uri Address { get; }

    /// <summary>What to ask with next time, to be told if nothing has changed.</summary>
    public WebsiteValidators Validators { get; }

    /// <summary>How long the website asked to be left alone for, where it did.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>What the website says the body is, where it says.</summary>
    public string? ContentType { get; }

    /// <summary>
    /// The body, unpacked and limited, every read of which runs under the deadline. It may fail for
    /// any of the reasons a website can fail: ask <see cref="OutcomeOf"/> what a failure comes to.
    /// </summary>
    public Stream Body { get; }

    /// <summary>Cancelled when the time is up, or when whoever asked stops waiting.</summary>
    public CancellationToken Deadline => _deadline.Token;

    /// <summary>A redirect that was not followed, because of what robots.txt had to say about where it leads.</summary>
    /// <param name="verdict">What it had to say.</param>
    /// <param name="address">The address that redirected.</param>
    /// <param name="kind">What was asked for.</param>
    /// <param name="deadline">The time the asking had, which this takes over.</param>
    /// <returns>What came of it.</returns>
    public static Fetched StoppedOnTheWay(RobotsVerdict verdict, Uri address, FetchKind kind, FetchDeadline deadline) =>
        new(verdict.Trouble ?? FetchOutcome.RobotsDisallowed, address, kind, deadline, verdict.RetryAfter)
        {
            StoppedByRobots = true,
        };

    /// <summary>
    /// What a failure while reading the body comes to.
    /// </summary>
    /// <param name="failure">What was thrown.</param>
    /// <returns>
    /// What to say about it, or nothing where it should go on up: whoever asked stopped waiting, or
    /// this is nobody's idea of a failure at a website.
    /// </returns>
    public FetchOutcome? OutcomeOf(Exception failure) => WebsiteFailure.OutcomeOf(failure, _kind, _deadline);

    /// <summary>
    /// Reads as much of the body as is read, and lets go of the connection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Coming to the end of what is read is not a failure, because a page says what it is at the top
    /// of itself. The limit on what arrives can still end a page, if what arrives has unpacked to
    /// almost nothing by then.
    /// </para>
    /// <para>
    /// A robots.txt keeps what it has at that point as well, whichever of the two limits it came to.
    /// The protocol asks for the first five hundred kibibytes of one to be followed, however it was
    /// sent.
    /// </para>
    /// </remarks>
    /// <returns>What was read, which stays good until this is disposed.</returns>
    public async Task<BodyPrefix> ReadPrefixAsync()
    {
        var capacity = (int)_limits.Unpacked;

        _rented = ArrayPool<byte>.Shared.Rent(capacity);

        var length = 0;
        var whole = false;

        try
        {
            while (length < capacity && !whole)
            {
                var read = await Body.ReadAsync(_rented.AsMemory(length, capacity - length), Deadline)
                    .ConfigureAwait(false);

                length += read;
                whole = read == 0;
            }
        }
        catch (ResponseTooLargeException) when (_kind == FetchKind.Robots)
        {
            // What has been read by now is the file, as far as it is read.
        }

        await Body.DisposeAsync().ConfigureAwait(false);
        _response?.Dispose();

        return new BodyPrefix(new ArraySegment<byte>(_rented, 0, length), whole);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Waits for the deadline's own disposal of the response, should that be under way right now.
        _abandonOnDeadline.Dispose();

        Body.Dispose();
        _response?.Dispose();
        _deadline.Dispose();

        if (_rented is not null)
        {
            ArrayPool<byte>.Shared.Return(_rented);
            _rented = null;
        }
    }
}

/// <summary>
/// What came of asking a website for something.
/// </summary>
public enum FetchOutcome
{
    /// <summary>It answered, and there is a body to read.</summary>
    Succeeded = 1,

    /// <summary>It has not changed since the validators it was asked with.</summary>
    NotModified = 2,

    /// <summary>There is nothing at that address.</summary>
    NotFound = 3,

    /// <summary>It would not let the reader in.</summary>
    TurnedAway = 4,

    /// <summary>It answered that something had gone wrong at its end.</summary>
    ServerError = 5,

    /// <summary>It did not answer, or stopped answering, or did not finish in time.</summary>
    Unreachable = 6,

    /// <summary>Its certificate could not be trusted.</summary>
    Insecure = 7,

    /// <summary>What it sent when asked for a sitemap can not be read as one.</summary>
    NotASitemap = 8,

    /// <summary>What it sent when asked for a page is not one, or can not be read as one.</summary>
    NotAPage = 9,

    /// <summary>It sent more than is read.</summary>
    TooLarge = 10,

    /// <summary>The address, or something it redirected to, is off the website.</summary>
    LeftTheWebsite = 11,

    /// <summary>It redirected too many times, or in a circle.</summary>
    TooManyRedirects = 12,

    /// <summary>Its name leads into a private network.</summary>
    PrivateAddress = 13,

    /// <summary>It said it is being asked too often, which goes for everything else about to be asked of it.</summary>
    AskedToSlowDown = 14,

    /// <summary>Its robots.txt asks for what a redirect led to not to be read.</summary>
    RobotsDisallowed = 15,

    /// <summary>Its robots.txt redirects off the website, or never lands, so nothing is known about what may be read.</summary>
    RobotsUnreadable = 16,
}

/// <summary>
/// What a website is asked for.
/// </summary>
public enum FetchKind
{
    /// <summary>One file of a sitemap, read whole.</summary>
    Sitemap = 1,

    /// <summary>One page, read as far as what it says about itself.</summary>
    Page = 2,

    /// <summary>Its robots.txt, read as far as the protocol requires.</summary>
    Robots = 3,
}

/// <summary>
/// As much of a body as was read.
/// </summary>
/// <param name="Bytes">The bytes.</param>
/// <param name="Whole">Whether that was all of it.</param>
internal readonly record struct BodyPrefix(ArraySegment<byte> Bytes, bool Whole);
