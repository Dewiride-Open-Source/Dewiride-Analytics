using System.Text;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// Asks a website's robots.txt about addresses, reading it once for each host a pass reaches.
/// </summary>
/// <remarks>
/// <para>
/// One of these lasts as long as one pass over one website and is used from one place at a time. Read
/// afresh each pass, the file is never held for anything like the day the protocol allows.
/// </para>
/// <para>
/// What comes of asking for the file follows RFC 9309. A file that is not there, or that the server
/// will not give out, says nothing and everything may be read. A server in trouble, one that can not
/// be reached, and a file that can not be read at all leave it undefined, and nothing may be read.
/// </para>
/// <para>
/// Being told to come back later is not one of those answers that say nothing. A 429 says nothing about
/// whether there is a file, which the protocol's letter passes over and Google's own reading of it does
/// not, and any other answer asking for a wait has asked for one. So nothing may be read until the wait
/// is over, and the wait goes with the verdict for whoever is asking to hear.
/// </para>
/// <para>
/// A redirect is followed only as far as the website's own addresses go, like every other request, and
/// where it would have to go further the file counts as undefined too. The protocol's advice is to
/// follow it across hosts, and its reason does not apply to something that has undertaken to stay on
/// the website.
/// </para>
/// </remarks>
/// <param name="fetcher">Asks for the file.</param>
/// <param name="bounds">Where it may be asked for.</param>
internal sealed class RobotsPass(WebsiteFetcher fetcher, WebsiteBounds bounds)
{
    private readonly Dictionary<string, HostFile> _files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether an address may be read.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <param name="cancellationToken">Stops the asking, which then ends as a cancellation.</param>
    /// <returns>What the website says about it.</returns>
    public async ValueTask<RobotsVerdict> AskAsync(Uri address, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (!bounds.Admits(address))
        {
            return new RobotsVerdict(false, FetchOutcome.LeftTheWebsite);
        }

        var host = address.IdnHost;

        if (!_files.TryGetValue(host, out var file))
        {
            file = await ReadFileAsync(new Uri(address, RobotsRules.FilePath), cancellationToken).ConfigureAwait(false);
            _files[host] = file;
        }

        return file.Rules is null
            ? new RobotsVerdict(false, file.Trouble, file.RetryAfter)
            : new RobotsVerdict(file.Rules.Permits(address), null);
    }

    /// <summary>
    /// What an answer that is not the file comes to.
    /// </summary>
    private static HostFile OtherThanTheFile(Fetched fetched) => fetched.Outcome switch
    {
        FetchOutcome.NotFound or FetchOutcome.TurnedAway => fetched.RetryAfter is null
            ? new HostFile(RobotsRules.PermitEverything, null, null)
            : new HostFile(null, FetchOutcome.TurnedAway, fetched.RetryAfter),
        FetchOutcome.LeftTheWebsite or FetchOutcome.TooManyRedirects => new HostFile(null, FetchOutcome.RobotsUnreadable, null),
        _ => new HostFile(null, fetched.Outcome, fetched.RetryAfter),
    };

    private async Task<HostFile> ReadFileAsync(Uri file, CancellationToken cancellationToken)
    {
        // Where the file's own redirects lead is not asked about. It would be asking the file about itself, and
        // what the protocol says of a redirect here is to follow it.
        using var fetched = await fetcher.FetchAsync(bounds, file, FetchKind.Robots, default, null, cancellationToken)
            .ConfigureAwait(false);

        if (fetched.Outcome != FetchOutcome.Succeeded)
        {
            return OtherThanTheFile(fetched);
        }

        try
        {
            var read = await fetched.ReadPrefixAsync().ConfigureAwait(false);
            var text = Encoding.UTF8.GetString(read.Bytes);

            return new HostFile(RobotsRules.Read(text, WebsiteHttp.ProductToken, read.Whole), null, null);
        }
        catch (Exception failure) when (fetched.OutcomeOf(failure) is { } outcome)
        {
            return new HostFile(null, outcome, null);
        }
    }

    /// <summary>
    /// What became of asking one host for its file.
    /// </summary>
    /// <param name="Rules">What it says, or nothing where it could not be read.</param>
    /// <param name="Trouble">What stopped it being read, where something did.</param>
    /// <param name="RetryAfter">How long the website asked to be left alone for, where it did.</param>
    private readonly record struct HostFile(RobotsRules? Rules, FetchOutcome? Trouble, TimeSpan? RetryAfter);
}

/// <summary>
/// What a website's robots.txt says about one address.
/// </summary>
/// <param name="Permitted">Whether it may be read.</param>
/// <param name="Trouble">
/// What stopped the file being read, where something did. Nothing may be read this pass, and this is
/// what to say about it, because the website has not actually asked for anything.
/// </param>
/// <param name="RetryAfter">
/// How long the website asked to be left alone for, where it did. It only ever comes with
/// <paramref name="Trouble"/>, so that an answer that may be read is an answer with no wait to hear.
/// </param>
internal readonly record struct RobotsVerdict(bool Permitted, FetchOutcome? Trouble, TimeSpan? RetryAfter = null);

/// <summary>
/// Asks a website's robots.txt about an address.
/// </summary>
/// <param name="address">The address.</param>
/// <param name="cancellationToken">Stops the asking, which then ends as a cancellation.</param>
/// <returns>What the website says about it.</returns>
internal delegate ValueTask<RobotsVerdict> RobotsQuestion(Uri address, CancellationToken cancellationToken);
