using System.Collections.Immutable;
using System.Net;

namespace Dewiride.Analytics.Infrastructure.Crawlers;

/// <summary>One company's claim on one block of addresses.</summary>
/// <param name="Operator">The company, spelt as the crawler catalogue spells it.</param>
/// <param name="Block">The block it publishes.</param>
internal readonly record struct ClaimedBlock(string Operator, AddressBlock Block);

/// <summary>
/// Every published crawler address, arranged so that one can be looked up per page view.
/// </summary>
/// <remarks>
/// <para>
/// Built once when a file changes and read on the ingest path, so the shape is chosen entirely for
/// the read. Blocks are gathered into one group per length; each group holds its networks sorted,
/// so answering a group is one mask and one binary search, and the groups are visited longest
/// first, so the first group that answers is the most specific block containing the address. A
/// dozen or so lengths exist across every file together, which puts a lookup at a few hundred
/// comparisons of a single number.
/// </para>
/// <para>
/// A block two companies both claim is dropped rather than awarded to either. It has never
/// happened and it should not: these are files companies publish about their own machines. If it
/// does, the honest reading is that the address establishes nothing, and quietly preferring
/// whichever file was read first would put a company's name on somebody's traffic on the strength
/// of a directory listing order.
/// </para>
/// </remarks>
internal sealed class CrawlerRangeTable
{
    private readonly ImmutableArray<LengthGroup> _groups;

    private CrawlerRangeTable(ImmutableArray<LengthGroup> groups, int count)
    {
        _groups = groups;
        Count = count;
    }

    /// <summary>A table that recognises nothing, which is what an install starts with.</summary>
    public static CrawlerRangeTable Empty { get; } = new([], 0);

    /// <summary>How many blocks are in service.</summary>
    public int Count { get; }

    /// <summary>
    /// Arranges published blocks for lookup.
    /// </summary>
    /// <param name="claims">Every block, with the company that publishes it.</param>
    /// <returns>The table.</returns>
    public static CrawlerRangeTable Build(IEnumerable<ClaimedBlock> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        var byLength = new Dictionary<int, Dictionary<UInt128, string?>>();

        foreach (var claim in claims)
        {
            Record(byLength, claim);
        }

        var groups = ImmutableArray.CreateBuilder<LengthGroup>(byLength.Count);
        var count = 0;

        foreach (var length in byLength.Keys.OrderDescending())
        {
            var settled = byLength[length]
                .Where(entry => entry.Value is not null)
                .OrderBy(entry => entry.Key)
                .ToArray();

            if (settled.Length == 0)
            {
                continue;
            }

            groups.Add(new LengthGroup(
                PublishedRangeFile.MaskOf(length),
                [.. settled.Select(entry => entry.Key)],
                [.. settled.Select(entry => entry.Value!)]));

            count += settled.Length;
        }

        return new CrawlerRangeTable(groups.DrainToImmutable(), count);
    }

    /// <summary>
    /// Finds whose crawlers an address belongs to.
    /// </summary>
    /// <param name="address">The address the request arrived from.</param>
    /// <returns>The company, or <see langword="null"/> where no published block contains it.</returns>
    public string? Find(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (_groups.IsEmpty)
        {
            return null;
        }

        var key = PublishedRangeFile.KeyOf(address);

        foreach (var group in _groups)
        {
            var found = Array.BinarySearch(group.Networks, key & group.Mask);

            if (found >= 0)
            {
                return group.Operators[found];
            }
        }

        return null;
    }

    /// <summary>
    /// Files one claim, marking a block no company can be said to own where two claim it.
    /// </summary>
    private static void Record(Dictionary<int, Dictionary<UInt128, string?>> byLength, ClaimedBlock claim)
    {
        if (!byLength.TryGetValue(claim.Block.PrefixLength, out var networks))
        {
            networks = [];
            byLength[claim.Block.PrefixLength] = networks;
        }

        if (!networks.TryGetValue(claim.Block.Network, out var held))
        {
            networks[claim.Block.Network] = claim.Operator;
            return;
        }

        if (held is not null && !string.Equals(held, claim.Operator, StringComparison.Ordinal))
        {
            networks[claim.Block.Network] = null;
        }
    }

    /// <summary>Every block of one length, sorted so a search can find one.</summary>
    private readonly record struct LengthGroup(UInt128 Mask, UInt128[] Networks, string[] Operators);
}
