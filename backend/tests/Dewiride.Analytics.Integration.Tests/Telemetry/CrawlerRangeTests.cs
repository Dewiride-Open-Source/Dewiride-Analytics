using System.Net;
using Dewiride.Analytics.Classification.Identity;
using Dewiride.Analytics.Infrastructure.Crawlers;

namespace Dewiride.Analytics.Integration.Tests.Telemetry;

/// <summary>
/// Covers reading the address lists companies publish for their own crawlers, and looking one up.
/// </summary>
/// <remarks>
/// Here rather than with the unit suites for the same reason the network table is: the format is
/// somebody else's and this product fetches it unattended, so what is worth pinning is not the
/// arithmetic but the tolerance — and the consequence of getting it wrong is a company's name
/// attached to somebody else's traffic.
/// </remarks>
public sealed class CrawlerRangeTests
{
    /// <summary>The shape every one of these files has, whoever publishes it.</summary>
    private const string Published = """
        {
          "creationTime": "2026-08-18T23:56:36Z",
          "prefixes": [
            { "ipv4Prefix": "66.249.64.0/27" },
            { "ipv4Prefix": "34.100.182.96/28" },
            { "ipv6Prefix": "2001:4860:4801:10::/64" }
          ]
        }
        """;

    [Fact]
    public void Reads_The_Addresses_A_Company_Publishes()
    {
        var table = Build(("Google", Published));

        table.Find(IPAddress.Parse("66.249.64.9")).Should().Be("Google");
        table.Find(IPAddress.Parse("34.100.182.100")).Should().Be("Google");
        table.Find(IPAddress.Parse("2001:4860:4801:10::5")).Should().Be("Google");
    }

    /// <summary>
    /// The answer for almost every real visit. Nothing about it is a finding, and it must never
    /// read as a denial: the company may publish nothing, or may have commissioned the machine
    /// since it last republished.
    /// </summary>
    [Fact]
    public void An_Address_Nobody_Published_Belongs_To_Nobody()
    {
        var table = Build(("Google", Published));

        table.Find(IPAddress.Parse("203.0.113.7")).Should().BeNull();
        table.Find(IPAddress.Parse("66.249.65.9")).Should().BeNull();
    }

    /// <summary>
    /// A visitor arriving on the newer address family over a connection that carries an older
    /// address is the same visitor. Holding both in one shape is what makes that true rather than
    /// something each caller has to remember.
    /// </summary>
    [Fact]
    public void An_Older_Address_Carried_In_The_Newer_Form_Is_The_Same_Address()
    {
        var table = Build(("Google", Published));

        table.Find(IPAddress.Parse("::ffff:66.249.64.9")).Should().Be("Google");
    }

    /// <summary>
    /// Blocks nest: a company publishing a whole network and another publishing one machine inside
    /// it must both be answered correctly, and the more specific one wins.
    /// </summary>
    [Fact]
    public void The_Most_Specific_Block_Containing_An_Address_Is_The_Answer()
    {
        var table = Build(
            ("Amazon", Prefixes("""{ "ipv4Prefix": "52.0.0.0/16" }""")),
            ("Perplexity", Prefixes("""{ "ipv4Prefix": "52.0.4.20/32" }""")));

        table.Find(IPAddress.Parse("52.0.4.20")).Should().Be("Perplexity");
        table.Find(IPAddress.Parse("52.0.4.21")).Should().Be("Amazon");
    }

    /// <summary>
    /// Two companies claiming one block cannot both be right, and preferring whichever file was
    /// read first would put a name on somebody's traffic on the strength of a listing order.
    /// </summary>
    [Fact]
    public void A_Block_Two_Companies_Claim_Establishes_Nobody()
    {
        var table = Build(
            ("Google", Prefixes("""{ "ipv4Prefix": "198.51.100.0/24" }""")),
            ("Microsoft", Prefixes("""{ "ipv4Prefix": "198.51.100.0/24" }""")));

        table.Find(IPAddress.Parse("198.51.100.5")).Should().BeNull();
    }

    /// <summary>
    /// A file that has become a sign-in page, an error page, or half a download must not replace
    /// the copy already working, and the way a caller learns that is an empty read.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<!doctype html><title>Sign in</title>")]
    [InlineData("""{ "creationTime": "2026-08-18T23:56:36Z" }""")]
    [InlineData("""{ "prefixes": [] }""")]
    [InlineData("""{"creationTime":"2026-08-18T23:56:36Z","prefixes":[{"ipv4Prefix":"66.249.6""")]
    public void Something_That_Is_Not_A_List_Of_Addresses_Reads_As_Nothing(string text)
    {
        PublishedRangeFile.Read(text).Should().BeEmpty();
    }

    /// <summary>
    /// A file is read as far as it makes sense. A company appending one line nobody anticipated
    /// should cost this product that line, not its ability to recognise the company at all.
    /// </summary>
    [Fact]
    public void One_Line_Nobody_Anticipated_Costs_That_Line_And_Nothing_Else()
    {
        var table = Build(("Google", Prefixes(
            """{ "ipv4Prefix": "not an address" }""",
            """{ "ipv4Prefix": "66.249.64.0/99" }""",
            """{ "ipv4Prefix": "66.249.64.0" }""",
            "\"a string where an object should be\"",
            """{ "ipv4Prefix": "66.249.64.0/27" }""")));

        table.Count.Should().Be(1);
        table.Find(IPAddress.Parse("66.249.64.9")).Should().Be("Google");
    }

    /// <summary>
    /// The guard that matters most. A file that had somehow come to claim the whole internet would
    /// otherwise hand every visitor on it one company's name, at the firmest band this product has.
    /// </summary>
    [Theory]
    [InlineData("""{ "ipv4Prefix": "0.0.0.0/0" }""")]
    [InlineData("""{ "ipv4Prefix": "66.0.0.0/8" }""")]
    [InlineData("""{ "ipv6Prefix": "::/0" }""")]
    [InlineData("""{ "ipv6Prefix": "2001::/16" }""")]
    public void A_Block_Broad_Enough_To_Claim_The_Internet_Is_Refused(string prefix)
    {
        PublishedRangeFile.Read(Prefixes(prefix)).Should().BeEmpty();
    }

    /// <summary>
    /// A block written with its host bits still set plainly means the network it starts with.
    /// Dropping it would silently stop this product recognising the machines behind it.
    /// </summary>
    [Fact]
    public void A_Block_Written_Untidily_Still_Means_The_Network_It_Starts_With()
    {
        var table = Build(("Apple", Prefixes("""{ "ipv4Prefix": "17.58.99.123/24" }""")));

        table.Find(IPAddress.Parse("17.58.99.1")).Should().Be("Apple");
    }

    [Fact]
    public void A_Table_Nothing_Has_Loaded_Answers_That_It_Knows_Nobody()
    {
        CrawlerRangeTable.Empty.Find(IPAddress.Parse("66.249.64.9")).Should().BeNull();
        CrawlerRangeTable.Empty.Count.Should().Be(0);
    }

    /// <summary>
    /// Every file kept on disk needs a name of its own, or one company's addresses would overwrite
    /// another's and the product would confidently report the wrong company.
    /// </summary>
    [Fact]
    public void Every_Local_Copy_Has_A_Name_Of_Its_Own()
    {
        CrawlerRangeFiles.All.Select(file => file.Name).Should().OnlyHaveUniqueItems();
        CrawlerRangeFiles.All.Should().OnlyContain(
            file => file.Name == Path.GetFileName(file.Name) && file.Name.EndsWith(".json"));
    }

    /// <summary>
    /// The list of files is derived from the catalogue rather than written out again, so a company
    /// added to one and forgotten in the other cannot happen — this holds that derivation.
    /// </summary>
    [Fact]
    public void Every_Published_List_In_The_Catalogue_Is_Kept()
    {
        var catalogued = CrawlerCatalogue.Proofs.SelectMany(proof => proof.PublishedRanges);

        CrawlerRangeFiles.All.Select(file => file.Address).Should().BeEquivalentTo(catalogued);
    }

    /// <summary>
    /// Nothing an install can be given by hand or fetch unattended should be able to break it, so
    /// the store answers rather than throws for anything that is not an address at all.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-address")]
    [InlineData("66.249.64.9, 10.0.0.1")]
    public void Something_That_Is_Not_An_Address_Establishes_Nobody(string? value)
    {
        var store = new CrawlerRangeStore();

        store.Publish(Build(("Google", Published)));

        store.OperatorOf(value).Should().BeNull();
    }

    [Fact]
    public void A_Store_Answers_From_Whatever_Was_Published_Last()
    {
        var store = new CrawlerRangeStore();

        store.OperatorOf("66.249.64.9").Should().BeNull();

        store.Publish(Build(("Google", Published)));
        store.OperatorOf("66.249.64.9").Should().Be("Google");

        store.Publish(CrawlerRangeTable.Empty);
        store.OperatorOf("66.249.64.9").Should().BeNull();
    }

    /// <summary>
    /// The other way a company vouches for a machine. Companies that publish no list document a
    /// domain their machines answer to instead, which is settled one address at a time and has to
    /// reach the collector by the same door as a published one.
    /// </summary>
    [Fact]
    public void An_Address_Settled_One_At_A_Time_Is_Answered_For_Too()
    {
        var store = new CrawlerRangeStore();

        store.Learn("Yandex", IPAddress.Parse("77.88.5.1"));

        store.OperatorOf("77.88.5.1").Should().Be("Yandex");
        store.OperatorOf("::ffff:77.88.5.1").Should().Be("Yandex");
        store.OperatorOf("77.88.5.2").Should().BeNull();
        store.Established.Should().Be(1);
    }

    /// <summary>
    /// An address established one at a time was never published as a set, so it cannot be compared
    /// against a newer one. Letting it expire with the files is what stops an address a company has
    /// stopped using from carrying its name for as long as the process runs.
    /// </summary>
    [Fact]
    public void What_Was_Settled_One_At_A_Time_Expires_With_The_Published_Lists()
    {
        var store = new CrawlerRangeStore();

        store.Learn("Yandex", IPAddress.Parse("77.88.5.1"));
        store.Publish(Build(("Google", Published)));

        store.OperatorOf("77.88.5.1").Should().Be("Yandex");

        store.Forget();

        store.OperatorOf("77.88.5.1").Should().BeNull();
        store.OperatorOf("66.249.64.9").Should().Be("Google");
    }

    /// <summary>
    /// A list a company publishes about all of its machines is the stronger of the two statements,
    /// and the order has to be stated even though the two have never disagreed.
    /// </summary>
    [Fact]
    public void A_Published_List_Answers_Before_Anything_Settled_One_At_A_Time()
    {
        var store = new CrawlerRangeStore();

        store.Publish(Build(("Google", Published)));
        store.Learn("Yandex", IPAddress.Parse("66.249.64.9"));

        store.OperatorOf("66.249.64.9").Should().Be("Google");
    }

    private static CrawlerRangeTable Build(params (string Operator, string Json)[] published) =>
        CrawlerRangeTable.Build(published.SelectMany(
            source => PublishedRangeFile.Read(source.Json),
            (source, block) => new ClaimedBlock(source.Operator, block)));

    private static string Prefixes(params string[] entries) =>
        $$"""{ "creationTime": "2026-08-18T23:56:36Z", "prefixes": [ {{string.Join(", ", entries)}} ] }""";
}
