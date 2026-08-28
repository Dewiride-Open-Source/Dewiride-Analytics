using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Infrastructure.Network;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Infrastructure.Crawlers;

/// <summary>
/// Settles whose crawlers an address belongs to by the name it answers to.
/// </summary>
/// <remarks>
/// <para>
/// Two questions, and the identity is in the pair rather than in either one. What name does this
/// address answer to, and does that name point back at this address? Only the holder of a block of
/// addresses can decide what they answer to, and only the holder of a domain can decide which
/// addresses it points at — so an answer that survives both questions came from somebody who
/// controls both, which is the company or nobody. One question on its own proves nothing: anybody
/// may make their own address answer to a name they do not hold, and anybody may point a name they
/// do hold at an address they do not.
/// </para>
/// <para>
/// Answers are remembered because the same fleet visits over and over, and because the ones worth
/// remembering most are the ones that came to nothing: a single determined visitor would otherwise
/// be a question about the same address for every visit it made.
/// </para>
/// <para>
/// A settled address is also handed to the address directory, so the next report from that machine
/// is recognised as it arrives rather than after its visit ends. That is what makes an identity
/// found this way last: the collector stamps it onto the stored activity, where it outlives the
/// address it was worked out from and is still there when the visit is judged again under a later
/// ruleset.
/// </para>
/// </remarks>
/// <param name="resolver">Asks the machine's own name server.</param>
/// <param name="addresses">Where a settled address is remembered for the ingest path.</param>
/// <param name="options">Whether to ask, how long to wait, and how much to remember.</param>
/// <param name="clock">Source of the staleness comparison.</param>
/// <param name="logger">Log sink.</param>
internal sealed partial class CrawlerNameLookup(
    IReverseNameLookup resolver,
    CrawlerRangeStore addresses,
    IOptions<CrawlerNameOptions> options,
    TimeProvider clock,
    ILogger<CrawlerNameLookup> logger) : ICrawlerNameLookup
{
    /// <summary>What an installation that may not ask anybody answers with.</summary>
    private static readonly IReadOnlyDictionary<string, string> Nothing =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<UInt128, Answer> _answers = new();

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> OperatorsOfAsync(
        IReadOnlyCollection<string> ipAddresses,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ipAddresses);

        var settings = options.Value;

        if (!settings.Enabled)
        {
            return Nothing;
        }

        var asking = Unanswered(ipAddresses, settings);
        var found = new ConcurrentDictionary<UInt128, string>();

        if (asking.Length > 0)
        {
            await AskAllAsync(asking, settings, found, cancellationToken).ConfigureAwait(false);
        }

        return Collect(ipAddresses, settings, found);
    }

    /// <summary>
    /// Picks out the addresses worth asking about, one question per address.
    /// </summary>
    /// <remarks>
    /// Duplicates, private addresses and anything already answered for fall out here. A pass
    /// routinely holds several visits from one machine, and an install behind a proxy that does not
    /// pass the visitor's address through sees nothing but addresses no name server can say
    /// anything about.
    /// </remarks>
    private ImmutableArray<Candidate> Unanswered(
        IReadOnlyCollection<string> ipAddresses,
        CrawlerNameOptions settings)
    {
        var asking = ImmutableArray.CreateBuilder<Candidate>();
        var seen = new HashSet<UInt128>();
        var stale = clock.GetUtcNow() - settings.RememberFor;

        foreach (var text in ipAddresses)
        {
            if (!RoutableAddress.TryRead(text, out var address))
            {
                continue;
            }

            var key = PublishedRangeFile.KeyOf(address);

            if (_answers.TryGetValue(key, out var answer) && answer.At > stale)
            {
                continue;
            }

            if (seen.Add(key))
            {
                asking.Add(new Candidate(address, key));
            }
        }

        return asking.DrainToImmutable();
    }

    /// <summary>
    /// Puts every outstanding question, a few at a time.
    /// </summary>
    /// <remarks>
    /// Few enough at once that a site being judged from its beginning does not arrive at somebody's
    /// name server as a burst, and enough that a pass with a few hundred questions finishes in
    /// seconds.
    /// </remarks>
    private async Task AskAllAsync(
        ImmutableArray<Candidate> asking,
        CrawlerNameOptions settings,
        ConcurrentDictionary<UInt128, string> found,
        CancellationToken cancellationToken)
    {
        var unanswerable = 0;

        await Parallel.ForEachAsync(
                asking,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = settings.LookupsAtOnce,
                    CancellationToken = cancellationToken,
                },
                async (candidate, token) =>
                {
                    if (!await AskAsync(candidate, settings, found, token).ConfigureAwait(false))
                    {
                        Interlocked.Increment(ref unanswerable);
                    }
                })
            .ConfigureAwait(false);

        // Every single question going unanswered is not a run of slow name servers, it is an
        // install with no way to reach one — which is worth saying once rather than staying silent
        // while a whole class of crawler quietly goes unrecognised.
        if (unanswerable == asking.Length)
        {
            Log.NoNameServer(logger, asking.Length);
        }
    }

    /// <summary>
    /// Builds the answer, keyed by the addresses the caller wrote.
    /// </summary>
    /// <remarks>
    /// Assembled from what is known rather than as the questions are answered, so that two visits
    /// whose addresses were written differently — one in the older family's own notation and one in
    /// its mapped form — are both settled by the one question that was actually asked.
    /// </remarks>
    private Dictionary<string, string> Collect(
        IReadOnlyCollection<string> ipAddresses,
        CrawlerNameOptions settings,
        ConcurrentDictionary<UInt128, string> found)
    {
        var settled = new Dictionary<string, string>(StringComparer.Ordinal);
        var stale = clock.GetUtcNow() - settings.RememberFor;

        foreach (var text in ipAddresses)
        {
            if (!RoutableAddress.TryRead(text, out var address))
            {
                continue;
            }

            var key = PublishedRangeFile.KeyOf(address);

            if (found.TryGetValue(key, out var settledNow))
            {
                settled[text] = settledNow;
            }
            else if (_answers.TryGetValue(key, out var answer)
                && answer.At > stale
                && answer.Operator is not null)
            {
                settled[text] = answer.Operator;
            }
        }

        return settled;
    }

    /// <summary>
    /// Asks about one address, within its own allowance.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> where nothing answered at all, which is the one outcome that says
    /// something about this installation rather than about the address.
    /// </returns>
    private async Task<bool> AskAsync(
        Candidate candidate,
        CrawlerNameOptions settings,
        ConcurrentDictionary<UInt128, string> found,
        CancellationToken cancellationToken)
    {
        using var allowance = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        allowance.CancelAfter(settings.LookupTimeout);

        try
        {
            var resolved = await resolver.ResolveAsync(candidate.Address, allowance.Token).ConfigureAwait(false);
            var operatorName = Established(resolved, candidate.Key);

            Remember(candidate.Key, operatorName, settings);

            if (operatorName is not null)
            {
                found[candidate.Key] = operatorName;
                addresses.Learn(operatorName, candidate.Address);
            }

            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // This address's own allowance running out, not the host stopping. Deliberately not
            // remembered: nothing was established either way, and recording silence as an answer
            // would let one slow moment settle an address for a day.
            return false;
        }
    }

    /// <summary>
    /// Decides what a pair of name-server answers establishes.
    /// </summary>
    /// <param name="resolved">What the name server said.</param>
    /// <param name="key">The address that was asked about.</param>
    /// <returns>The company, or <see langword="null"/> where the pair establishes nobody.</returns>
    private static string? Established(ResolvedName resolved, UInt128 key)
    {
        var operatorName = ConfirmingHosts.OperatorOf(resolved.HostName);

        return operatorName is not null && PointsBack(resolved.Addresses, key) ? operatorName : null;
    }

    /// <summary>
    /// Whether the name the address answers to points back at that address.
    /// </summary>
    /// <remarks>
    /// The half that cannot be forged by whoever holds the address. Anybody may make their machine
    /// answer to <c>crawl-1-2-3-4.googlebot.com</c>; nobody but Google can make that name resolve
    /// to their machine.
    /// </remarks>
    private static bool PointsBack(ImmutableArray<IPAddress> found, UInt128 key) =>
        found.Any(candidate => PublishedRangeFile.KeyOf(candidate) == key);

    /// <summary>
    /// Keeps an answer, within the bound on how many are kept.
    /// </summary>
    /// <remarks>
    /// The addresses come from whoever is crawling the site, so the limit is what stops a large
    /// fleet deciding how much memory this process uses. Stale answers are dropped first; if that
    /// frees nothing, the answer is simply not kept, and the address is asked about again next
    /// time. Neither outcome changes what any visit is judged to be.
    /// </remarks>
    private void Remember(UInt128 key, string? operatorName, CrawlerNameOptions settings)
    {
        if (_answers.Count >= settings.RememberedAnswers)
        {
            Forget(settings);

            if (_answers.Count >= settings.RememberedAnswers)
            {
                return;
            }
        }

        _answers[key] = new Answer(operatorName, clock.GetUtcNow());
    }

    private void Forget(CrawlerNameOptions settings)
    {
        var stale = clock.GetUtcNow() - settings.RememberFor;

        foreach (var entry in _answers)
        {
            if (entry.Value.At <= stale)
            {
                _answers.TryRemove(entry.Key, out _);
            }
        }
    }

    /// <summary>One address still to be asked about.</summary>
    /// <param name="Address">The parsed form, for the name server.</param>
    /// <param name="Key">The single number both address families reduce to.</param>
    private readonly record struct Candidate(IPAddress Address, UInt128 Key);

    /// <summary>What was established about one address, and when.</summary>
    /// <param name="Operator">The company, or <see langword="null"/> where it was nobody's.</param>
    /// <param name="At">When the question was answered.</param>
    private readonly record struct Answer(string? Operator, DateTimeOffset At);

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 1206,
            Level = LogLevel.Warning,
            Message = "None of the {Count} address(es) checked against the names their operators "
                + "publish could be looked up at all. Crawlers that publish no list of addresses "
                + "cannot be recognised until this installation can reach a name server.")]
        public static partial void NoNameServer(ILogger logger, int count);
    }
}
