using System.Net.Http.Json;
using Dewiride.Analytics.Api.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// A browser, as far as the dashboard is concerned.
/// </summary>
/// <remarks>
/// Keeps the two things a real browser keeps: the cookies the server set, and the token the
/// server issued for proving a request came from its own pages. The token changes whenever who is
/// signed in changes, so it is re-read from every answer that carries one — which is exactly what
/// the interface has to do.
/// </remarks>
internal sealed class Browser : IDisposable
{
    private readonly HttpClient _client;

    private Browser(HttpClient client)
    {
        _client = client;
        Token = string.Empty;
    }

    /// <summary>The most recent proof-of-origin token the server issued.</summary>
    public string Token { get; private set; }

    /// <summary>
    /// Opens the dashboard, which is the first call the interface makes.
    /// </summary>
    /// <param name="host">The host to talk to.</param>
    /// <returns>The client, holding a token and ready to sign in.</returns>
    public static async Task<Browser> OpenAsync(WebApplicationFactory<Program> host)
    {
        ArgumentNullException.ThrowIfNull(host);

        var browser = new Browser(host.CreateClient());
        await browser.DescribeAsync().ConfigureAwait(false);

        return browser;
    }

    /// <summary>Asks who is signed in and whether the install has been set up.</summary>
    /// <returns>What the server said.</returns>
    public async Task<SessionResponse> DescribeAsync()
    {
        var response = await _client.GetAsync(new Uri("/api/session", UriKind.Relative), Cancellation.Token)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var session = await response.Content
            .ReadFromJsonAsync<SessionResponse>(Cancellation.Token)
            .ConfigureAwait(false);

        Token = session!.Token;

        return session;
    }

    /// <summary>Sends a request that changes something, with proof of where it came from.</summary>
    /// <typeparam name="TBody">Type of the payload.</typeparam>
    /// <param name="path">Path to post to.</param>
    /// <param name="body">Payload to send.</param>
    /// <returns>The answer.</returns>
    public Task<HttpResponseMessage> PostAsync<TBody>(string path, TBody body) =>
        SendAsync(HttpMethod.Post, path, JsonContent.Create(body), Token);

    /// <summary>Sends a request that changes something, with a token that will not be accepted.</summary>
    /// <typeparam name="TBody">Type of the payload.</typeparam>
    /// <param name="path">Path to post to.</param>
    /// <param name="body">Payload to send.</param>
    /// <returns>The answer.</returns>
    public Task<HttpResponseMessage> PostWithoutProofAsync<TBody>(string path, TBody body) =>
        SendAsync(HttpMethod.Post, path, JsonContent.Create(body), token: null);

    /// <summary>Sends a request that replaces something, with proof of where it came from.</summary>
    /// <typeparam name="TBody">Type of the payload.</typeparam>
    /// <param name="path">Path to put to.</param>
    /// <param name="body">Payload to send.</param>
    /// <returns>The answer.</returns>
    public Task<HttpResponseMessage> PutAsync<TBody>(string path, TBody body) =>
        SendAsync(HttpMethod.Put, path, JsonContent.Create(body), Token);

    /// <summary>Sends a request that replaces something, with a token that will not be accepted.</summary>
    /// <typeparam name="TBody">Type of the payload.</typeparam>
    /// <param name="path">Path to put to.</param>
    /// <param name="body">Payload to send.</param>
    /// <returns>The answer.</returns>
    public Task<HttpResponseMessage> PutWithoutProofAsync<TBody>(string path, TBody body) =>
        SendAsync(HttpMethod.Put, path, JsonContent.Create(body), token: null);

    /// <summary>Sends a request that changes part of something, with proof of where it came from.</summary>
    /// <typeparam name="TBody">Type of the payload.</typeparam>
    /// <param name="path">Path to change.</param>
    /// <param name="body">Payload to send.</param>
    /// <returns>The answer.</returns>
    public Task<HttpResponseMessage> PatchAsync<TBody>(string path, TBody body) =>
        SendAsync(HttpMethod.Patch, path, JsonContent.Create(body), Token);

    /// <summary>Sends a request that changes part of something, with a token that will not be accepted.</summary>
    /// <typeparam name="TBody">Type of the payload.</typeparam>
    /// <param name="path">Path to change.</param>
    /// <param name="body">Payload to send.</param>
    /// <returns>The answer.</returns>
    public Task<HttpResponseMessage> PatchWithoutProofAsync<TBody>(string path, TBody body) =>
        SendAsync(HttpMethod.Patch, path, JsonContent.Create(body), token: null);

    /// <summary>Sends a request that removes something, with proof of where it came from.</summary>
    /// <param name="path">Path to delete.</param>
    /// <returns>The answer.</returns>
    public Task<HttpResponseMessage> DeleteAsync(string path) =>
        SendAsync(HttpMethod.Delete, path, content: null, Token);

    /// <summary>Sends a request that removes something, with a token that will not be accepted.</summary>
    /// <param name="path">Path to delete.</param>
    /// <returns>The answer.</returns>
    public Task<HttpResponseMessage> DeleteWithoutProofAsync(string path) =>
        SendAsync(HttpMethod.Delete, path, content: null, token: null);

    /// <summary>Ends the current sign-in, keeping the token the answer carries.</summary>
    /// <returns>The answer.</returns>
    public async Task<HttpResponseMessage> SignOutAsync()
    {
        var response = await SendAsync(HttpMethod.Delete, "/api/session", content: null, Token)
            .ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            var session = await response.Content
                .ReadFromJsonAsync<SessionResponse>(Cancellation.Token)
                .ConfigureAwait(false);

            Token = session!.Token;
        }

        return response;
    }

    /// <summary>Reads something.</summary>
    /// <param name="path">Path to read.</param>
    /// <returns>The answer.</returns>
    public Task<HttpResponseMessage> GetAsync(string path) =>
        _client.GetAsync(new Uri(path, UriKind.Relative), Cancellation.Token);

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        string? token)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative))
        {
            Content = content,
        };

        if (token is not null)
        {
            request.Headers.Add("X-Csrf-Token", token);
        }

        var response = await _client.SendAsync(request, Cancellation.Token).ConfigureAwait(false);

        return response;
    }
}
