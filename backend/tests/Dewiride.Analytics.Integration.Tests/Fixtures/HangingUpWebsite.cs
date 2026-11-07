using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// A website on this machine that reads a request and then closes the connection without a word, as an
/// overloaded server or a filter in front of one does.
/// </summary>
/// <remarks>
/// On a bare socket, because what the client does about it depends on how the connection ended. Closed in good
/// order, with nothing left unread, it looks like a server that closed an idle connection at the wrong
/// moment, and the handler sends the request again. A real server asked to abort a connection resets it instead,
/// and the handler does not.
/// </remarks>
internal sealed class HangingUpWebsite : IAsyncDisposable
{
    private const string Answer = "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\n\r\n";

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentQueue<string> _asked = new();
    private readonly string[] _answered;
    private readonly Task _accepting;

    /// <summary>Starts a website.</summary>
    /// <param name="answered">What it answers, which leaves the connection open. Anything else it hangs up on.</param>
    public HangingUpWebsite(params string[] answered)
    {
        _answered = answered;
        _listener.Start();
        _accepting = AcceptAsync();
    }

    /// <summary>The port it listens on.</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>The path and query of everything it was asked, in the order it was asked.</summary>
    public IReadOnlyList<string> Asked => [.. _asked];

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        _listener.Stop();

        await _accepting.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        _stopping.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(_stopping.Token).ConfigureAwait(false);

            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            var connection = client.GetStream();

            try
            {
                while (await ReadRequestAsync(connection).ConfigureAwait(false) is { } target)
                {
                    _asked.Enqueue(target);

                    if (!_answered.Contains(target))
                    {
                        return;
                    }

                    await connection.WriteAsync(Encoding.ASCII.GetBytes(Answer), _stopping.Token).ConfigureAwait(false);
                }
            }
            catch (Exception failure) when (failure is IOException or OperationCanceledException)
            {
                // Whoever was asking has gone, or this website is stopping.
            }
        }
    }

    /// <summary>
    /// Reads the whole of a request, so that closing the connection leaves nothing unread.
    /// </summary>
    /// <returns>What it asked for, or nothing where the connection has closed.</returns>
    private async Task<string?> ReadRequestAsync(NetworkStream connection)
    {
        var request = new StringBuilder();
        var buffer = new byte[4096];

        while (!request.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            var read = await connection.ReadAsync(buffer, _stopping.Token).ConfigureAwait(false);

            if (read == 0)
            {
                return null;
            }

            request.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }

        return request.ToString().Split(' ')[1];
    }
}
