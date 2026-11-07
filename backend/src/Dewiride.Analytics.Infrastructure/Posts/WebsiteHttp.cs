using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// The HTTP client a website's pages are read with.
/// </summary>
/// <remarks>
/// <para>
/// The engine's other downloads come from a fixed list of addresses chosen here. This is the one
/// whose addresses are chosen by a stranger, so it goes nowhere on its own: it follows no redirect,
/// keeps no cookie, sends no credential, asks no proxy and tells nobody what the engine was doing when
/// it asked. A proxy would resolve the name for itself, which would put the check of where a
/// connection may go out of reach.
/// </para>
/// <para>
/// Every connection is opened here, by address, to where <see cref="IPublicEndpoints"/> said it may
/// go. The name is not looked up a second time, so there is no moment at which it can answer
/// differently. The server is still asked for the name, both in the encryption handshake and in the
/// request.
/// </para>
/// <para>
/// A certificate is checked with what the website sent and what this machine trusts, and nothing else. A
/// certificate can name an address its issuer is kept at, and the framework would fetch that itself, with a
/// client of its own that none of the above applies to: any address and any port a stranger cares to write
/// into a certificate, this machine's own network included. So a website that leaves out part of its chain
/// has a certificate that can not be trusted, where a browser might go and find the rest. Whether a
/// certificate has been revoked is not asked, because asking goes to an address the certificate names too.
/// </para>
/// <para>
/// It speaks HTTP/1.1 and nothing newer. Each request says so, because a newer protocol can open its
/// connection some other way than through the callback that does the checking.
/// </para>
/// <para>
/// One request gets one connection opened for it. Left to itself the handler sends a request again, below
/// anything that could pace it, when a connection closes before any answer has come: up to four times, a
/// millisecond apart, to a server that may be hanging up because it is struggling. So a second connection
/// for a request is refused, which is that website not having answered. A request that went out on a
/// connection already open can still be sent the once more, on a new one, because a server closing an idle
/// connection at that moment is what the retry is for, and RFC 9110 lets an idempotent request be retried
/// and asks only that a failed retry is not.
/// </para>
/// <para>
/// Its logging is taken off. The factory's own lines carry the address of every request at the level
/// the engine writes at, and every one of those addresses was written by somebody else.
/// </para>
/// </remarks>
internal static class WebsiteHttp
{
    /// <summary>Name the client is registered under.</summary>
    public const string ClientName = "website-reader";

    /// <summary>
    /// What the reader calls itself: to a website's server, and when it looks for its own name in a
    /// website's robots.txt.
    /// </summary>
    public const string ProductToken = "DewirideAnalytics";

    /// <summary>Where anybody seeing the reader in their logs can find out what it is.</summary>
    private const string AboutTheReader =
        "https://github.com/Dewiride-Open-Source/Dewiride-Analytics#reading-a-websites-sitemap";

    private const int LargestHeadersInKilobytes = 64;
    private const int ConnectionsPerServer = 2;
    private const int LargestDrainInBytes = 64 * 1024;

    /// <summary>Marks a request a connection has already been opened for.</summary>
    private static readonly HttpRequestOptionsKey<bool> ConnectedOnce = new("Dewiride.Analytics.ConnectedOnce");

    /// <summary>
    /// How the reader introduces itself: its name first, what it is in a word a website's logs and this
    /// product's own judging both know a program that fetches pages by, and where to read about it.
    /// </summary>
    public static string UserAgent { get; } = $"{ProductToken}/{ReleaseVersion()} (sitemap fetcher; +{AboutTheReader})";

    /// <summary>
    /// Registers the client.
    /// </summary>
    /// <remarks>
    /// The client waits as long as whoever is using it says to, and nothing else times it. A timeout
    /// here would stop only at the end of the headers, while what a website can drip out for as long
    /// as it likes is the body, so <see cref="Fetched"/> keeps one deadline for the two together.
    /// </remarks>
    /// <param name="services">The service collection, holding the endpoints and the options.</param>
    public static void AddClient(IServiceCollection services) =>
        services.AddHttpClient(ClientName)
            .UseSocketsHttpHandler((handler, provider) => Configure(
                handler,
                provider.GetRequiredService<IPublicEndpoints>(),
                provider.GetRequiredService<IOptions<SitemapOptions>>().Value))
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .RemoveAllLoggers()
            .ConfigureHttpClient(client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestVersion = HttpVersion.Version11;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
            });

    /// <summary>
    /// Settles what a handler does on its own, which is as little as it can.
    /// </summary>
    /// <remarks>
    /// A page cut off at its prefix leaves the rest of its body in the connection. The handler is told
    /// to give up on that connection rather than read a mebibyte of somebody's page to save it.
    /// </remarks>
    /// <param name="handler">The handler.</param>
    /// <param name="endpoints">Where a connection may go.</param>
    /// <param name="options">The settings it takes its time limit from.</param>
    private static void Configure(SocketsHttpHandler handler, IPublicEndpoints endpoints, SitemapOptions options)
    {
        handler.AllowAutoRedirect = false;
        handler.UseCookies = false;
        handler.UseProxy = false;
        handler.Credentials = null;
        handler.PreAuthenticate = false;
        handler.AutomaticDecompression = DecompressionMethods.None;
        handler.ActivityHeadersPropagator = null;
        handler.ConnectTimeout = options.ConnectTimeout;
        handler.MaxResponseHeadersLength = LargestHeadersInKilobytes;
        handler.MaxConnectionsPerServer = ConnectionsPerServer;
        handler.MaxResponseDrainSize = LargestDrainInBytes;
        handler.PooledConnectionLifetime = TimeSpan.FromMinutes(1);
        handler.PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30);
        handler.SslOptions.CertificateChainPolicy = new X509ChainPolicy
        {
            DisableCertificateDownloads = true,
            RevocationMode = X509RevocationMode.NoCheck,
        };
        handler.ConnectCallback = (context, cancellationToken) =>
            ConnectOnceAsync(context, endpoints, options.ConnectTimeout, cancellationToken);
    }

    /// <summary>
    /// Opens the one connection a request gets.
    /// </summary>
    private static async ValueTask<Stream> ConnectOnceAsync(
        SocketsHttpConnectionContext context,
        IPublicEndpoints endpoints,
        TimeSpan connectTimeout,
        CancellationToken cancellationToken)
    {
        var request = context.InitialRequestMessage;

        if (request.Options.TryGetValue(ConnectedOnce, out _))
        {
            throw new IOException("The website hung up on this request, and it is not asked twice.");
        }

        request.Options.Set(ConnectedOnce, true);

        var vetted = await endpoints.ResolveAsync(context.DnsEndPoint, cancellationToken).ConfigureAwait(false);
        var eachHas = connectTimeout / Math.Max(vetted.Count, 1);

        return await FirstToAnswerAsync(vetted, eachHas, ConnectToAsync, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Connects to the first vetted endpoint that answers, and to nothing that was not vetted.
    /// </summary>
    /// <remarks>
    /// One that stays silent gets its share of the time and no more. Where a name has several addresses and
    /// one of them drops whatever is sent to it, the time for connecting would otherwise be gone before the
    /// next one was ever tried, and the website would read as not answering while a browser finds it at once.
    /// </remarks>
    /// <param name="vetted">Where it may go.</param>
    /// <param name="eachHas">How long each has to answer.</param>
    /// <param name="connect">Connects to one of them.</param>
    /// <param name="cancellationToken">Stops all of it, which then ends as a cancellation.</param>
    /// <returns>The connection.</returns>
    internal static async ValueTask<Stream> FirstToAnswerAsync(
        IReadOnlyList<IPEndPoint> vetted,
        TimeSpan eachHas,
        Func<IPEndPoint, CancellationToken, Task<Stream>> connect,
        CancellationToken cancellationToken)
    {
        SocketException? lastFailure = null;

        foreach (var endpoint in vetted)
        {
            using var share = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            share.CancelAfter(eachHas);

            try
            {
                return await connect(endpoint, share.Token).ConfigureAwait(false);
            }
            catch (SocketException failure)
            {
                lastFailure = failure;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastFailure = new SocketException((int)SocketError.TimedOut);
            }
        }

        throw lastFailure ?? new SocketException((int)SocketError.HostNotFound);
    }

    private static async Task<Stream> ConnectToAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

        try
        {
            await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);

            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The version people would recognise, without the commit the build appends to it.
    /// </summary>
    private static string ReleaseVersion()
    {
        var version = typeof(WebsiteHttp).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";

        return version.Split('+')[0];
    }
}
