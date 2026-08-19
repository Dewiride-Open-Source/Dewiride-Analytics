using System.IO.Compression;
using System.Net;
using System.Text;
using Dewiride.Analytics.Infrastructure.Network;
using Dewiride.Analytics.Integration.Tests.Fixtures;

namespace Dewiride.Analytics.Integration.Tests.Telemetry;

/// <summary>
/// Covers reading the published table of which network an address belongs to.
/// </summary>
/// <remarks>
/// Here rather than with the unit suites because it reads a real compressed file from disk, in
/// the format iptoasn.com publishes. The format is somebody else's and is refreshed hourly, so
/// what is worth pinning is not the arithmetic but the tolerance: a table half a million rows long
/// that this product downloads unattended will contain lines nobody anticipated.
/// </remarks>
public sealed class AutonomousSystemTableTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"dewiride-asn-{Guid.NewGuid():N}");

    [Fact]
    public async Task Finds_The_Network_An_Address_Belongs_To()
    {
        var table = await LoadAsync(
            "1.0.0.0\t1.0.0.255\t13335\tUS\tCLOUDFLARENET",
            "8.8.8.0\t8.8.8.255\t15169\tUS\tGOOGLE");

        table.Find(IPAddress.Parse("8.8.8.8")).Should().Be((15169u, "GOOGLE"));
        table.Find(IPAddress.Parse("1.0.0.42")).Should().Be((13335u, "CLOUDFLARENET"));
    }

    /// <summary>
    /// The table leaves gaps, so the range beginning nearest below an address is only the answer
    /// if it actually reaches that address.
    /// </summary>
    [Fact]
    public async Task An_Address_In_A_Gap_Belongs_To_Nobody()
    {
        var table = await LoadAsync("8.8.8.0\t8.8.8.255\t15169\tUS\tGOOGLE");

        table.Find(IPAddress.Parse("9.9.9.9")).Should().Be((0u, string.Empty));
    }

    [Fact]
    public async Task Finds_A_Network_In_The_Longer_Address_Family()
    {
        var table = await LoadAsync("2001:4860::\t2001:4860:ffff:ffff:ffff:ffff:ffff:ffff\t15169\tUS\tGOOGLE");

        table.Find(IPAddress.Parse("2001:4860:4860::8888")).Should().Be((15169u, "GOOGLE"));
    }

    /// <summary>
    /// A visitor reaching a dual-stack listener over the older family arrives wearing the newer
    /// family's clothes, and would otherwise be looked up in the wrong table and found in none.
    /// </summary>
    [Fact]
    public async Task An_Older_Address_Wearing_The_Newer_Familys_Form_Is_Still_Found()
    {
        var table = await LoadAsync("8.8.8.0\t8.8.8.255\t15169\tUS\tGOOGLE");

        table.Find(IPAddress.Parse("::ffff:8.8.8.8")).Should().Be((15169u, "GOOGLE"));
    }

    /// <summary>
    /// Half the operators in the published table put their own number in front of their name and
    /// half do not. The number is already its own column, and leaving it in would put a wire
    /// identifier in front of some names on a screen and not others.
    /// </summary>
    [Fact]
    public async Task An_Operators_Own_Number_Is_Not_Part_Of_Its_Name()
    {
        var table = await LoadAsync(
            "81.2.69.0\t81.2.69.255\t20712\tGB\tAS20712 Andrews & Arnold Ltd",
            "8.8.8.0\t8.8.8.255\t15169\tUS\tGOOGLE");

        table.Find(IPAddress.Parse("81.2.69.142")).Should().Be((20712u, "Andrews & Arnold Ltd"));
        table.Find(IPAddress.Parse("8.8.8.8")).Should().Be((15169u, "GOOGLE"));
    }

    /// <summary>
    /// A name beginning with the same two letters as a number would be is a name, not a number.
    /// </summary>
    [Fact]
    public async Task A_Name_That_Merely_Begins_Like_A_Number_Is_Left_Alone()
    {
        var table = await LoadAsync("81.2.69.0\t81.2.69.255\t42\tGB\tASDA Stores Ltd");

        table.Find(IPAddress.Parse("81.2.69.1")).Should().Be((42u, "ASDA Stores Ltd"));
    }

    /// <summary>
    /// Ranges the publisher marks as belonging to nobody carry a number of nought and a
    /// description saying so, and reporting that as a network operator would put the words
    /// "Not routed" on a screen as though they named a company.
    /// </summary>
    [Fact]
    public async Task Ranges_Belonging_To_Nobody_Are_Left_Out()
    {
        var table = await LoadAsync(
            "0.0.0.0\t0.255.255.255\t0\tNone\tNot routed",
            "8.8.8.0\t8.8.8.255\t15169\tUS\tGOOGLE");

        table.Count.Should().Be(1);
        table.Find(IPAddress.Parse("0.0.0.1")).Should().Be((0u, string.Empty));
    }

    /// <summary>
    /// Half a million rows fetched unattended from somebody else's server will eventually contain
    /// a line nobody anticipated. One of them is not a reason to leave every visitor's network
    /// unresolved until a person intervenes.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("8.8.8.0\t8.8.8.255")]
    [InlineData("not-an-address\t8.8.8.255\t15169\tUS\tGOOGLE")]
    [InlineData("8.8.8.0\t2001:4860::\t15169\tUS\tGOOGLE")]
    [InlineData("8.8.8.0\t8.8.8.255\tnot-a-number\tUS\tGOOGLE")]
    public async Task A_Line_That_Cannot_Be_Read_Is_Skipped_Rather_Than_Fatal(string bad)
    {
        var table = await LoadAsync(bad, "1.1.1.0\t1.1.1.255\t13335\tUS\tCLOUDFLARENET");

        table.Count.Should().Be(1);
        table.Find(IPAddress.Parse("1.1.1.1")).Should().Be((13335u, "CLOUDFLARENET"));
    }

    /// <summary>
    /// Searching by halving is only correct on sorted input. The publisher sorts the file today
    /// and this does not depend on them continuing to.
    /// </summary>
    [Fact]
    public async Task Ranges_Arriving_Out_Of_Order_Are_Still_Found()
    {
        var table = await LoadAsync(
            "200.0.0.0\t200.0.0.255\t3\tUS\tThird",
            "1.0.0.0\t1.0.0.255\t1\tUS\tFirst",
            "100.0.0.0\t100.0.0.255\t2\tUS\tSecond");

        table.Find(IPAddress.Parse("1.0.0.1")).Should().Be((1u, "First"));
        table.Find(IPAddress.Parse("100.0.0.1")).Should().Be((2u, "Second"));
        table.Find(IPAddress.Parse("200.0.0.1")).Should().Be((3u, "Third"));
    }

    [Fact]
    public async Task An_Empty_File_Reads_As_An_Empty_Table()
    {
        var table = await LoadAsync();

        table.Count.Should().Be(0);
        table.Find(IPAddress.Parse("8.8.8.8")).Should().Be((0u, string.Empty));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>Writes the lines out in the form the publisher serves them, then reads them back.</summary>
    private async Task<AutonomousSystemTable> LoadAsync(params string[] lines)
    {
        Directory.CreateDirectory(_directory);

        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.tsv.gz");

        await using (var file = File.Create(path))
        await using (var packed = new GZipStream(file, CompressionLevel.Fastest))
        {
            await packed.WriteAsync(Encoding.UTF8.GetBytes(string.Join('\n', lines)), Cancellation.Token);
        }

        return await AutonomousSystemTable.LoadAsync(path, Cancellation.Token);
    }
}
