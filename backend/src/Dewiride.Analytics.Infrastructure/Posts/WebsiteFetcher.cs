using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// Asks a website for something, and follows it as far as it may be followed.
/// </summary>
/// <remarks>
/// <para>
/// Redirects are followed here rather than by the handler, because each one is an address written by
/// the website and has to be held to the same bounds as the first, and robots.txt has the same say about
/// it. A redirect is a request like any other as far as a website's server goes, so each waits its turn.
/// A wait a redirect itself asks for is not heard: its turn comes when it comes.
/// </para>
/// <para>
/// Only a 200 or a 203 is the answer. The rest of the 2xx range says the request went well, and hands over
/// nothing that was asked for: a firewall's challenge answers 202 with a page of its own, and the others
/// have no page to give. Read as the answer, each of those would be a page that said nothing about itself,
/// or a sitemap that is not one, with nobody told what really happened. A website not letting the reader
/// in is what it comes to. A robots.txt is the exception, because the protocol has any success followed,
/// and what a file says is obeyed however it arrived.
/// </para>
/// <para>
/// What the website could not or would not do comes back as an outcome rather than an exception, so
/// asking can be written as one call and one look at what came of it. The only thing that comes back
/// as an exception is whoever asked no longer wanting the answer, or a fault here.
/// </para>
/// </remarks>
/// <param name="clients">Supplies the client.</param>
/// <param name="pacer">Keeps requests to one website apart.</param>
/// <param name="options">The time and the size a website is allowed.</param>
/// <param name="clock">The clock deadlines run on.</param>
internal sealed class WebsiteFetcher(
    IHttpClientFactory clients,
    Pacer pacer,
    IOptions<SitemapOptions> options,
    TimeProvider clock)
{
    /// <summary>How many redirects are followed in one asking.</summary>
    public const int MostRedirects = 5;

    /// <summary>
    /// How much of a robots.txt is read. The protocol says to read at least five hundred kibibytes,
    /// so this reads a little more, and no amount of compression gets past it.
    /// </summary>
    private const long RobotsBytes = 512 * 1024;

    private const string SitemapAccept = "application/xml, text/xml;q=0.9, */*;q=0.1";
    private const string PageAccept = "text/html, application/xhtml+xml;q=0.9";
    private const string RobotsAccept = "text/plain, */*;q=0.1";
    private const string AcceptedCompression = "gzip, br";

    /// <summary>
    /// Asks a website for something.
    /// </summary>
    /// <param name="bounds">Where this may go.</param>
    /// <param name="address">The address.</param>
    /// <param name="kind">What it is expected to be.</param>
    /// <param name="validators">What to ask with to be told if nothing has changed.</param>
    /// <param name="robots">
    /// Asks robots.txt about where a redirect leads, which nothing does where this is a robots.txt being asked
    /// for. It runs in the time this asking has, so that reading the file of the website's other name on the way
    /// adds nothing to how long a pass can run over its budget.
    /// </param>
    /// <param name="cancellationToken">Stops the asking, which then ends as a cancellation.</param>
    /// <returns>What came of it, which the caller disposes.</returns>
    public async Task<Fetched> FetchAsync(
        WebsiteBounds bounds,
        Uri address,
        FetchKind kind,
        WebsiteValidators validators,
        RobotsQuestion? robots,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(address);

        var admitted = bounds.Admits(address);

        // The first turn is waited for before the time starts. If anything holds it up it is another
        // reading of the same website, and not this website being slow.
        if (admitted)
        {
            await pacer.WaitTurnAsync(address.IdnHost, cancellationToken).ConfigureAwait(false);
        }

        var deadline = new FetchDeadline(TimeAllowedFor(kind, options.Value), clock, cancellationToken);

        if (!admitted)
        {
            return new Fetched(FetchOutcome.LeftTheWebsite, address, kind, deadline);
        }

        try
        {
            return await FollowAsync(bounds, address, kind, validators, robots, deadline).ConfigureAwait(false);
        }
        catch (Exception failure) when (WebsiteFailure.OutcomeOf(failure, kind, deadline) is { } outcome)
        {
            return new Fetched(outcome, address, kind, deadline);
        }
        catch
        {
            deadline.Dispose();
            throw;
        }
    }

    private static TimeSpan TimeAllowedFor(FetchKind kind, SitemapOptions settings) =>
        kind == FetchKind.Sitemap ? settings.SitemapTimeout : settings.PageTimeout;

    private static BodyLimits LimitsOf(FetchKind kind, SitemapOptions settings) => kind switch
    {
        FetchKind.Sitemap => new BodyLimits(settings.LargestSitemapBytes, settings.LargestSitemapBytes),

        // Twice over, so that the limit on what arrives only ever ends a body that has unpacked to
        // less than it arrived as, and never one that was merely not compressed.
        FetchKind.Page => new BodyLimits(2L * settings.PageBytesRead, settings.PageBytesRead),
        _ => new BodyLimits(RobotsBytes, RobotsBytes),
    };

    private async Task<Fetched> FollowAsync(
        WebsiteBounds bounds,
        Uri address,
        FetchKind kind,
        WebsiteValidators validators,
        RobotsQuestion? robots,
        FetchDeadline deadline)
    {
        var client = clients.CreateClient(WebsiteHttp.ClientName);
        var asked = new HashSet<string>(StringComparer.Ordinal) { address.AbsoluteUri };
        var current = address;

        while (true)
        {
            var response = await SendAsync(client, current, kind, validators, deadline.Token).ConfigureAwait(false);

            if (!TryReadRedirect(response, current, out var next))
            {
                var outcome = OutcomeOf(response, kind, validators);

                return await AnswerAsync(response, outcome, current, kind, deadline).ConfigureAwait(false);
            }

            response.Dispose();

            if (RefusalOf(next, bounds, asked) is { } refusal)
            {
                return new Fetched(refusal, current, kind, deadline);
            }

            if (robots is not null)
            {
                var verdict = await robots(next, deadline.Token).ConfigureAwait(false);

                if (!verdict.Permitted)
                {
                    return Fetched.StoppedOnTheWay(verdict, current, kind, deadline);
                }
            }

            await pacer.WaitTurnAsync(next.IdnHost, deadline.Token).ConfigureAwait(false);

            current = next;
        }
    }

    /// <summary>
    /// Why a redirect may not be followed, where it may not.
    /// </summary>
    /// <param name="next">Where it leads.</param>
    /// <param name="bounds">Where this may go.</param>
    /// <param name="asked">Every address asked so far, including the one that redirected.</param>
    private static FetchOutcome? RefusalOf(Uri next, WebsiteBounds bounds, HashSet<string> asked)
    {
        if (!bounds.Admits(next))
        {
            return FetchOutcome.LeftTheWebsite;
        }

        return asked.Count > MostRedirects || !asked.Add(next.AbsoluteUri)
            ? FetchOutcome.TooManyRedirects
            : null;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        Uri address,
        FetchKind kind,
        WebsiteValidators validators,
        CancellationToken deadline)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };

        request.Headers.TryAddWithoutValidation("Accept", AcceptOf(kind));
        request.Headers.TryAddWithoutValidation("Accept-Encoding", AcceptedCompression);
        validators.ApplyTo(request);

        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline)
            .ConfigureAwait(false);
    }

    private static string AcceptOf(FetchKind kind) => kind switch
    {
        FetchKind.Sitemap => SitemapAccept,
        FetchKind.Page => PageAccept,
        _ => RobotsAccept,
    };

    /// <summary>
    /// Where a response redirects to, if it is a redirect that says.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <param name="current">The address it answered for, which a relative redirect is read against.</param>
    /// <param name="next">Where it leads.</param>
    /// <returns>Whether it is one. A redirect that leads nowhere is a website with a fault at its end.</returns>
    private static bool TryReadRedirect(HttpResponseMessage response, Uri current, [NotNullWhen(true)] out Uri? next)
    {
        next = null;

        return response.StatusCode is HttpStatusCode.MovedPermanently
                or HttpStatusCode.Found
                or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect
                or HttpStatusCode.PermanentRedirect
            && response.Headers.Location is { } location
            && Uri.TryCreate(current, location, out next);
    }

    private async Task<Fetched> AnswerAsync(
        HttpResponseMessage response,
        FetchOutcome outcome,
        Uri address,
        FetchKind kind,
        FetchDeadline deadline)
    {
        if (outcome != FetchOutcome.Succeeded)
        {
            var retryAfter = RetryAfterOf(response);

            response.Dispose();

            return new Fetched(outcome, address, kind, deadline, retryAfter);
        }

        try
        {
            var limits = LimitsOf(kind, options.Value);
            var encodings = response.Content.Headers.ContentEncoding;
            var raw = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            var body = await Bodies
                .OpenAsync(raw, encodings, limits, lookInside: kind == FetchKind.Sitemap, deadline.Token)
                .ConfigureAwait(false);

            return new Fetched(response, body, limits, address, kind, deadline);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>
    /// What a response comes to.
    /// </summary>
    /// <remarks>
    /// A website saying nothing has changed when it was not asked is not to be believed, because whoever
    /// asked has nothing it could still be the same as, and would otherwise take it as read.
    /// </remarks>
    private static FetchOutcome OutcomeOf(HttpResponseMessage response, FetchKind kind, WebsiteValidators validators)
    {
        if (IsTheAnswer(response, kind))
        {
            return kind == FetchKind.Page && !SaysItIsAPage(response) ? FetchOutcome.NotAPage : FetchOutcome.Succeeded;
        }

        var outcome = OutcomeOf(response.StatusCode);

        return outcome == FetchOutcome.NotModified && !validators.AskIfChanged ? FetchOutcome.ServerError : outcome;
    }

    private static FetchOutcome OutcomeOf(HttpStatusCode status) => status switch
    {
        HttpStatusCode.NotModified => FetchOutcome.NotModified,
        HttpStatusCode.NotFound or HttpStatusCode.Gone => FetchOutcome.NotFound,
        HttpStatusCode.TooManyRequests => FetchOutcome.AskedToSlowDown,
        >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices => FetchOutcome.TurnedAway,
        >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError => FetchOutcome.TurnedAway,
        _ => FetchOutcome.ServerError,
    };

    private static bool IsTheAnswer(HttpResponseMessage response, FetchKind kind) =>
        kind == FetchKind.Robots
            ? response.IsSuccessStatusCode
            : response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NonAuthoritativeInformation;

    /// <summary>
    /// Whether a response says it is a web page, or says nothing, which a parser built to read
    /// whatever a browser would read can take in its stride.
    /// </summary>
    private static bool SaysItIsAPage(HttpResponseMessage response)
    {
        var type = response.Content.Headers.ContentType?.MediaType;

        return type is null
            || type.Equals("text/html", StringComparison.OrdinalIgnoreCase)
            || type.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase);
    }

    private TimeSpan? RetryAfterOf(HttpResponseMessage response)
    {
        var asked = response.Headers.RetryAfter;
        var wait = asked?.Delta ?? asked?.Date - clock.GetUtcNow();

        return wait > TimeSpan.Zero ? wait : null;
    }
}
