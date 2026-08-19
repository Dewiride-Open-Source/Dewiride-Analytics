using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;

namespace Dewiride.Analytics.Infrastructure.Network;

/// <summary>
/// Which autonomous system an address belongs to, and who runs it.
/// </summary>
/// <remarks>
/// <para>
/// Held as sorted parallel arrays and searched by halving, rather than as a tree of objects. The
/// published table is around half a million ranges and this is asked once per accepted event, so
/// the shape that costs least to walk is the one worth building — twenty-odd comparisons over
/// contiguous memory, with nothing allocated on the way.
/// </para>
/// <para>
/// Ranges are stored as whole numbers rather than as addresses. Both families sort correctly that
/// way, which is what makes halving the search legitimate, and neither an address object nor a
/// comparison between two of them is created while a visitor is waiting.
/// </para>
/// </remarks>
internal sealed class AutonomousSystemTable
{
    /// <summary>Columns in one line of the published table.</summary>
    private const int ExpectedFields = 5;

    private readonly uint[] _shortStarts;
    private readonly uint[] _shortEnds;
    private readonly UInt128[] _longStarts;
    private readonly UInt128[] _longEnds;
    private readonly uint[] _shortNumbers;
    private readonly uint[] _longNumbers;
    private readonly string[] _shortOwners;
    private readonly string[] _longOwners;

    private AutonomousSystemTable(Ranges<uint> shortRanges, Ranges<UInt128> longRanges)
    {
        _shortStarts = [.. shortRanges.Starts];
        _shortEnds = [.. shortRanges.Ends];
        _shortNumbers = [.. shortRanges.Numbers];
        _shortOwners = [.. shortRanges.Owners];

        _longStarts = [.. longRanges.Starts];
        _longEnds = [.. longRanges.Ends];
        _longNumbers = [.. longRanges.Numbers];
        _longOwners = [.. longRanges.Owners];
    }

    /// <summary>How many ranges were read. Zero means the file held nothing usable.</summary>
    public int Count => _shortStarts.Length + _longStarts.Length;

    /// <summary>
    /// Reads a published table.
    /// </summary>
    /// <param name="path">The compressed file, as it is published.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The table.</returns>
    /// <remarks>
    /// Lines that cannot be read are skipped rather than fatal. This is somebody else's file,
    /// refreshed hourly, and one malformed row in half a million is not a reason to leave every
    /// visitor's network unresolved until a person intervenes.
    /// </remarks>
    public static async Task<AutonomousSystemTable> LoadAsync(string path, CancellationToken cancellationToken)
    {
        var shortRanges = new Ranges<uint>();
        var longRanges = new Ranges<UInt128>();
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);

        await using var file = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);

        await using var expanded = new GZipStream(file, CompressionMode.Decompress);
        using var lines = new StreamReader(expanded);

        while (await lines.ReadLineAsync(cancellationToken).ConfigureAwait(false) is string line)
        {
            Accept(line, shortRanges, longRanges, owners);
        }

        shortRanges.Sort();
        longRanges.Sort();

        return new AutonomousSystemTable(shortRanges, longRanges);
    }

    /// <summary>
    /// Finds the autonomous system an address sits in.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>
    /// The number and its operator, or a zero number and an empty operator when the address falls
    /// in no published range — which is the honest answer for a private address and for the parts
    /// of the space nobody has been allocated.
    /// </returns>
    public (uint Number, string Owner) Find(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return Locate(ToNumber(address), _shortStarts, _shortEnds, _shortNumbers, _shortOwners);
        }

        return address.IsIPv4MappedToIPv6
            ? Locate(ToNumber(address.MapToIPv4()), _shortStarts, _shortEnds, _shortNumbers, _shortOwners)
            : Locate(ToLongNumber(address), _longStarts, _longEnds, _longNumbers, _longOwners);
    }

    /// <summary>
    /// Finds the one range that could contain a value, and reports it only if it does.
    /// </summary>
    /// <remarks>
    /// Ranges do not overlap, so the last one beginning at or before the value is the only
    /// candidate. Whether it actually reaches the value is a separate question, and the reason
    /// the ends are kept: the published table leaves gaps.
    /// </remarks>
    private static (uint Number, string Owner) Locate<T>(
        T value,
        T[] starts,
        T[] ends,
        uint[] numbers,
        string[] owners)
        where T : IComparable<T>
    {
        var low = 0;
        var high = starts.Length - 1;
        var found = -1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);

            if (starts[middle].CompareTo(value) <= 0)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found >= 0 && ends[found].CompareTo(value) >= 0
            ? (numbers[found], owners[found])
            : (0, string.Empty);
    }

    private static void Accept(
        string line,
        Ranges<uint> shortRanges,
        Ranges<UInt128> longRanges,
        Dictionary<string, string> owners)
    {
        var fields = line.Split('\t');

        if (fields.Length < ExpectedFields
            || !uint.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || number == 0
            || !IPAddress.TryParse(fields[0], out var first)
            || !IPAddress.TryParse(fields[1], out var last)
            || first.AddressFamily != last.AddressFamily)
        {
            return;
        }

        var owner = Pooled(fields[4], owners);

        if (first.AddressFamily == AddressFamily.InterNetwork)
        {
            shortRanges.Add(ToNumber(first), ToNumber(last), number, owner);
        }
        else
        {
            longRanges.Add(ToLongNumber(first), ToLongNumber(last), number, owner);
        }
    }

    /// <summary>
    /// Returns one shared instance of a repeated operator name.
    /// </summary>
    /// <remarks>
    /// A few thousand operators hold half a million ranges between them, so without this the
    /// table would carry hundreds of thousands of copies of a few thousand strings.
    /// </remarks>
    private static string Pooled(string value, Dictionary<string, string> pool)
    {
        var trimmed = WithoutNumber(value.Trim());

        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        if (pool.TryGetValue(trimmed, out var existing))
        {
            return existing;
        }

        pool[trimmed] = trimmed;
        return trimmed;
    }

    /// <summary>
    /// Drops the autonomous system number some operators put at the front of their own name.
    /// </summary>
    /// <remarks>
    /// The published table is inconsistent about it — <c>GOOGLE</c> beside
    /// <c>AS20712 Andrews &amp; Arnold Ltd</c> — and the number is already its own column. Left
    /// in, half the operators on a screen would carry a wire identifier in front of their name
    /// and the other half would not.
    /// </remarks>
    private static string WithoutNumber(string description)
    {
        if (!description.StartsWith("AS", StringComparison.Ordinal))
        {
            return description;
        }

        var after = 2;

        while (after < description.Length && char.IsAsciiDigit(description[after]))
        {
            after++;
        }

        // Only when digits were actually found and a name follows them. "ASDA" starts with two
        // letters and a name, and is a network operator's name rather than a number.
        return after > 2 && after < description.Length && description[after] == ' '
            ? description[(after + 1)..]
            : description;
    }

    private static uint ToNumber(IPAddress address)
    {
        Span<byte> octets = stackalloc byte[4];
        address.TryWriteBytes(octets, out _);

        return BinaryPrimitives.ReadUInt32BigEndian(octets);
    }

    private static UInt128 ToLongNumber(IPAddress address)
    {
        Span<byte> octets = stackalloc byte[16];
        address.TryWriteBytes(octets, out _);

        return BinaryPrimitives.ReadUInt128BigEndian(octets);
    }

    /// <summary>Ranges being accumulated, before they are frozen into the arrays above.</summary>
    private sealed class Ranges<T>
        where T : IComparable<T>
    {
        public List<T> Starts { get; } = [];

        public List<T> Ends { get; } = [];

        public List<uint> Numbers { get; } = [];

        public List<string> Owners { get; } = [];

        public void Add(T first, T last, uint number, string owner)
        {
            Starts.Add(first);
            Ends.Add(last);
            Numbers.Add(number);
            Owners.Add(owner);
        }

        /// <summary>
        /// Puts the ranges in ascending order, carrying every parallel list with them.
        /// </summary>
        /// <remarks>
        /// The published table is already sorted, and this does not assume so. Searching by
        /// halving is only correct on sorted input, and the alternative to spending a second here
        /// is a lookup that silently returns the wrong operator if the publisher ever changes
        /// their mind about the order.
        /// </remarks>
        public void Sort()
        {
            var order = Enumerable.Range(0, Starts.Count).ToArray();
            var starts = Starts;

            Array.Sort(order, (left, right) => starts[left].CompareTo(starts[right]));

            Reorder(Starts, order);
            Reorder(Ends, order);
            Reorder(Numbers, order);
            Reorder(Owners, order);
        }

        private static void Reorder<TItem>(List<TItem> values, int[] order)
        {
            var sorted = new TItem[order.Length];

            for (var index = 0; index < order.Length; index++)
            {
                sorted[index] = values[order[index]];
            }

            values.Clear();
            values.AddRange(sorted);
        }
    }
}
