using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// A website on this machine, which answers what a test tells it to and remembers what it was asked.
/// </summary>
/// <remarks>
/// A real server on a real socket, so that what is proved about reading a website is proved of the
/// handler, the headers and the connection as they are. It is reached through
/// <see cref="LoopbackEndpoints"/>, because the reader refuses this machine by any other route.
/// </remarks>
internal sealed class LoopbackWebsite : IAsyncDisposable
{
    private readonly WebApplication _server;
    private readonly ConcurrentDictionary<string, RequestDelegate> _answers = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<SeenRequest> _seen = new();

    private LoopbackWebsite(WebApplication server) => _server = server;

    /// <summary>The port it listens on.</summary>
    public int Port { get; private set; }

    /// <summary>Everything it was asked, in the order it was asked.</summary>
    public IReadOnlyList<SeenRequest> Seen => [.. _seen];

    /// <summary>
    /// Starts a website.
    /// </summary>
    /// <param name="certificate">What to answer securely with, where it is to.</param>
    /// <returns>The website, which knows no pages yet and answers 404 until it is told some.</returns>
    public static async Task<LoopbackWebsite> StartAsync(X509Certificate2? certificate = null)
    {
        var builder = WebApplication.CreateSlimBuilder();

        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            // A header can hold bytes past ASCII, and left to itself this server would refuse to send what
            // plenty of others do.
            kestrel.ResponseHeaderEncodingSelector = _ => Encoding.Latin1;

            kestrel.Listen(IPAddress.Loopback, 0, listener =>
            {
                if (certificate is not null)
                {
                    listener.UseHttps(AnswersSecurelyWith(certificate));
                }
            });
        });

        var server = builder.Build();
        var website = new LoopbackWebsite(server);

        server.Run(website.AnswerAsync);

        await server.StartAsync(Cancellation.Token).ConfigureAwait(false);

        website.Port = new Uri(server.Services.GetRequiredService<IServer>()
            .Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.Single()).Port;

        return website;
    }

    /// <summary>
    /// Serves a certificate as it is. Left to itself, the server goes looking for whatever the certificate says
    /// is missing from its chain, and a test about what the reader goes looking for would hear the server.
    /// </summary>
    private static TlsHandshakeCallbackOptions AnswersSecurelyWith(X509Certificate2 certificate)
    {
        var context = SslStreamCertificateContext.Create(certificate, additionalCertificates: null, offline: true);
        var answering = new SslServerAuthenticationOptions { ServerCertificateContext = context };

        return new TlsHandshakeCallbackOptions { OnConnection = _ => ValueTask.FromResult(answering) };
    }

    /// <summary>Tells it how to answer for an address.</summary>
    /// <param name="target">The path and query, exactly as a request names them.</param>
    /// <param name="answer">How to answer.</param>
    public void Answer(string target, RequestDelegate answer) => _answers[target] = answer;

    /// <summary>Tells it to answer an address with some text.</summary>
    /// <param name="target">The path and query.</param>
    /// <param name="body">The text.</param>
    /// <param name="contentType">What to say it is.</param>
    public void Serve(string target, string body, string contentType = "application/xml") =>
        Serve(target, Encoding.UTF8.GetBytes(body), contentType);

    /// <summary>Tells it to answer an address with some bytes.</summary>
    /// <param name="target">The path and query.</param>
    /// <param name="body">The bytes.</param>
    /// <param name="contentType">What to say they are.</param>
    /// <param name="contentEncoding">What to say they were compressed with.</param>
    public void Serve(string target, byte[] body, string contentType, string? contentEncoding = null) =>
        Answer(target, async context =>
        {
            context.Response.ContentType = contentType;

            if (contentEncoding is not null)
            {
                context.Response.Headers.ContentEncoding = contentEncoding;
            }

            await context.Response.Body.WriteAsync(body, context.RequestAborted).ConfigureAwait(false);
        });

    /// <summary>Tells it to answer an address with nothing but a status.</summary>
    /// <param name="target">The path and query.</param>
    /// <param name="status">The status.</param>
    public void Status(string target, int status) => Answer(target, context =>
    {
        context.Response.StatusCode = status;

        return Task.CompletedTask;
    });

    /// <summary>Tells it to redirect an address somewhere.</summary>
    /// <param name="target">The path and query.</param>
    /// <param name="location">Where to.</param>
    /// <param name="status">Which kind of redirect.</param>
    public void Redirect(string target, string location, int status = StatusCodes.Status302Found) =>
        Answer(target, context =>
        {
            context.Response.StatusCode = status;
            context.Response.Headers.Location = location;

            return Task.CompletedTask;
        });

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await _server.StopAsync(stopping.Token).ConfigureAwait(false);
        await _server.DisposeAsync().ConfigureAwait(false);
    }

    private async Task AnswerAsync(HttpContext context)
    {
        var target = context.Features.GetRequiredFeature<IHttpRequestFeature>().RawTarget;

        _seen.Enqueue(new SeenRequest(
            context.Request.Host.Host,
            target,
            context.Request.Protocol,
            context.Request.Headers.ToDictionary(
                header => header.Key,
                header => header.Value.ToString(),
                StringComparer.OrdinalIgnoreCase),
            TimeProvider.System.GetTimestamp()));

        if (_answers.TryGetValue(target, out var answer))
        {
            await answer(context).ConfigureAwait(false);
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
        }
    }
}

/// <summary>
/// One request a website received.
/// </summary>
/// <param name="Host">The host it was asked of, by its header.</param>
/// <param name="Target">The path and query, exactly as it was named.</param>
/// <param name="Protocol">The protocol it arrived over.</param>
/// <param name="Headers">Its headers.</param>
/// <param name="At">When it arrived, on the system's timestamp.</param>
internal sealed record SeenRequest(
    string Host,
    string Target,
    string Protocol,
    IReadOnlyDictionary<string, string> Headers,
    long At);
