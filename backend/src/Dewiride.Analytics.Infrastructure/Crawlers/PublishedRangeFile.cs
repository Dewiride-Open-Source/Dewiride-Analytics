using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Dewiride.Analytics.Infrastructure.Crawlers;

/// <summary>
/// One block of addresses, in the single 128-bit form both address families are held in.
/// </summary>
/// <remarks>
/// Older addresses are carried in their mapped form, so a range written as <c>/24</c> in one family
/// is a <c>/120</c> here. Holding one shape rather than two is what lets a lookup be one masked
/// comparison whichever family a visitor arrived on.
/// </remarks>
/// <param name="Network">The first address in the block, with every host bit cleared.</param>
/// <param name="PrefixLength">How many leading bits the block fixes, out of 128.</param>
internal readonly record struct AddressBlock(UInt128 Network, int PrefixLength);

/// <summary>
/// Reads a file of crawler addresses as the companies that publish them write it.
/// </summary>
/// <remarks>
/// <para>
/// There is one format, and every company that publishes such a file uses it: an object with a
/// <c>prefixes</c> array whose members carry either an <c>ipv4Prefix</c> or an <c>ipv6Prefix</c>.
/// Google defined it and the rest followed, which is a piece of luck this reader depends on and
/// a test pins.
/// </para>
/// <para>
/// Everything is treated as somebody else's output rather than as a contract. A member that is not
/// an object, a prefix that will not parse, a length that is not a number, a block so large that
/// it would claim a swathe of the internet for one company: each is skipped and the rest of the
/// file is read, because a company appending one malformed line should cost this product that
/// line and not its ability to recognise the company.
/// </para>
/// </remarks>
internal static class PublishedRangeFile
{
    /// <summary>Field naming a block in the older address family.</summary>
    private const string OlderFamily = "ipv4Prefix";

    /// <summary>Field naming a block in the newer one.</summary>
    private const string NewerFamily = "ipv6Prefix";

    /// <summary>The array every one of these files keeps its blocks in.</summary>
    private const string Prefixes = "prefixes";

    /// <summary>How many leading bits an older address occupies once it is held in the newer form.</summary>
    private const int MappedOffset = 96;

    /// <summary>
    /// Shortest block accepted in the older family.
    /// </summary>
    /// <remarks>
    /// One company genuinely publishes a block this size, because it owns one of the original
    /// allocations. Nothing legitimate is broader, and a file that had somehow come to say
    /// <c>0.0.0.0/0</c> would otherwise hand every visitor on the internet that company's name.
    /// </remarks>
    private const int ShortestOlderPrefix = 16;

    /// <summary>Shortest block accepted in the newer family, on the same reasoning.</summary>
    private const int ShortestNewerPrefix = 32;

    /// <summary>
    /// Reads every block a file lists.
    /// </summary>
    /// <param name="json">The file's contents.</param>
    /// <returns>
    /// The blocks, or an empty result where the text is not one of these files at all. Empty is
    /// how a caller learns not to put the file into service, so a web page served in a file's
    /// stead never replaces the copy already working.
    /// </returns>
    public static ImmutableArray<AddressBlock> Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(Prefixes, out var prefixes)
                && prefixes.ValueKind == JsonValueKind.Array
                    ? Blocks(prefixes)
                    : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static ImmutableArray<AddressBlock> Blocks(JsonElement prefixes)
    {
        var found = ImmutableArray.CreateBuilder<AddressBlock>(prefixes.GetArrayLength());

        foreach (var entry in prefixes.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (TryBlock(entry, OlderFamily, out var older))
            {
                found.Add(older);
            }
            else if (TryBlock(entry, NewerFamily, out var newer))
            {
                found.Add(newer);
            }
        }

        return found.DrainToImmutable();
    }

    private static bool TryBlock(JsonElement entry, string field, out AddressBlock block)
    {
        block = default;

        return entry.TryGetProperty(field, out var value)
            && value.ValueKind == JsonValueKind.String
            && TryParse(value.GetString(), out block);
    }

    /// <summary>
    /// Parses one block, written as an address, a slash, and a length.
    /// </summary>
    /// <remarks>
    /// Parsed by hand rather than by the framework's own network type, which refuses a block whose
    /// address carries bits below its length. That refusal is right for a value somebody typed and
    /// wrong for a file somebody else generated: a block written as <c>1.2.3.4/24</c> plainly means
    /// the twenty-four bits it starts with, and dropping it would silently stop recognising the
    /// machines behind it. The host bits are cleared here instead.
    /// </remarks>
    private static bool TryParse(string? value, out AddressBlock block)
    {
        block = default;

        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var slash = value.IndexOf('/', StringComparison.Ordinal);

        if (slash <= 0
            || !IPAddress.TryParse(value.AsSpan(0, slash), out var address)
            || !int.TryParse(value.AsSpan(slash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var length))
        {
            return false;
        }

        return TryBuild(address, length, out block);
    }

    private static bool TryBuild(IPAddress address, int length, out AddressBlock block)
    {
        block = default;

        var older = address.AddressFamily == AddressFamily.InterNetwork;
        var shortest = older ? ShortestOlderPrefix : ShortestNewerPrefix;
        var longest = older ? 32 : 128;

        if (length < shortest || length > longest)
        {
            return false;
        }

        var mapped = older ? length + MappedOffset : length;

        block = new AddressBlock(KeyOf(address) & MaskOf(mapped), mapped);
        return true;
    }

    /// <summary>
    /// Reduces an address to the number a comparison is made on.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>Its 128-bit form, older addresses being carried in their mapped shape.</returns>
    public static UInt128 KeyOf(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        var target = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        if (target.AddressFamily == AddressFamily.InterNetwork)
        {
            target = target.MapToIPv6();
        }

        Span<byte> bytes = stackalloc byte[16];

        return target.TryWriteBytes(bytes, out var written) && written == bytes.Length
            ? BinaryPrimitives.ReadUInt128BigEndian(bytes)
            : UInt128.Zero;
    }

    /// <summary>
    /// The mask that keeps a block's fixed bits and clears the rest.
    /// </summary>
    /// <param name="prefixLength">How many leading bits the block fixes, out of 128.</param>
    /// <returns>The mask.</returns>
    public static UInt128 MaskOf(int prefixLength) =>
        prefixLength <= 0 ? UInt128.Zero : UInt128.MaxValue << (128 - prefixLength);
}
