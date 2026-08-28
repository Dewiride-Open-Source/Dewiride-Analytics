using System.Collections.Concurrent;
using System.Net;
using Dewiride.Analytics.Application.Telemetry;

namespace Dewiride.Analytics.Infrastructure.Crawlers;

/// <summary>
/// Holds whichever crawler addresses are currently established, and answers the ingest path from them.
/// </summary>
/// <remarks>
/// <para>
/// Two sources, and they are the two ways a company can vouch for its own machines. Most publish a
/// file listing them, which arrives here as a table. The rest publish a domain their machines
/// answer to, which cannot be listed in advance because it is a question asked of one address at a
/// time — so those arrive one at a time, from the pass that judges finished visits, and are
/// remembered here so the next report from the same machine is recognised as it arrives.
/// </para>
/// <para>
/// Empty until something loads, and usable while empty: a first run has fetched nothing yet, and an
/// install with no way out to the internet may never fetch anything. Both answer that the address
/// establishes nobody, which is the same answer the vast majority of real visits get and which the
/// engine already knows how to reason about.
/// </para>
/// <para>
/// Replacing the table needs none of the care the place database needs. It is an ordinary managed
/// object holding no file, so the table it replaces is collected once the last lookup using it
/// returns, and a lookup already under way finishes against the table it started with — which is a
/// correct answer, just a few hours old.
/// </para>
/// </remarks>
internal sealed class CrawlerRangeStore : ICrawlerAddressDirectory
{
    /// <summary>
    /// How many separately established addresses are held at once.
    /// </summary>
    /// <remarks>
    /// Not a tuning knob but a bound on what a visitor can make this process hold. Every entry had
    /// to survive a check that only its operator could arrange, so this is far past any real
    /// crawler fleet — but the addresses arrive from whoever is crawling the site, and nothing that
    /// grows on that input is left unbounded.
    /// </remarks>
    private const int MostRemembered = 50_000;

    private readonly ConcurrentDictionary<UInt128, string> _established = new();

    private CrawlerRangeTable _table = CrawlerRangeTable.Empty;

    /// <summary>The table in service.</summary>
    public CrawlerRangeTable Table => Volatile.Read(ref _table);

    /// <summary>How many addresses were established one at a time rather than from a file.</summary>
    public int Established => _established.Count;

    /// <summary>
    /// Puts a newly built table into service.
    /// </summary>
    /// <param name="table">The table.</param>
    public void Publish(CrawlerRangeTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        Volatile.Write(ref _table, table);
    }

    /// <summary>
    /// Remembers one address whose operator has been established by the name it answers to.
    /// </summary>
    /// <param name="operatorName">The company, spelt as the crawler catalogue spells it.</param>
    /// <param name="address">The address.</param>
    public void Learn(string operatorName, IPAddress address)
    {
        ArgumentException.ThrowIfNullOrEmpty(operatorName);
        ArgumentNullException.ThrowIfNull(address);

        if (_established.Count < MostRemembered)
        {
            _established[PublishedRangeFile.KeyOf(address)] = operatorName;
        }
    }

    /// <summary>
    /// Drops everything established one address at a time.
    /// </summary>
    /// <remarks>
    /// Called when the published files are reloaded, so both sources are as old as each other and
    /// neither goes on vouching for a machine indefinitely. An address a company has stopped using
    /// leaves within a refresh interval, and one it still uses is established again the next time
    /// a visit from it is judged — at no cost, because the answer to the question is remembered
    /// separately for a day.
    /// </remarks>
    public void Forget() => _established.Clear();

    /// <inheritdoc />
    public string? OperatorOf(string? ipAddress) =>
        IPAddress.TryParse(ipAddress, out var address) ? Find(address) : null;

    /// <summary>
    /// Looks an address up in both sources, the published files first.
    /// </summary>
    /// <remarks>
    /// The order settles nothing in practice — the two have never disagreed and could only do so
    /// if a company's file and its own name server contradicted each other — but it has to be
    /// stated, and a list a company publishes about all of its machines is the stronger of the two.
    /// </remarks>
    private string? Find(IPAddress address) =>
        Table.Find(address)
        ?? (_established.TryGetValue(PublishedRangeFile.KeyOf(address), out var found) ? found : null);
}
