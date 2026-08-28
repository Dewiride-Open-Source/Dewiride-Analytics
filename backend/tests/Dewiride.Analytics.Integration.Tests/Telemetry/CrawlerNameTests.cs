using System.Net;
using Dewiride.Analytics.Infrastructure.Crawlers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Dewiride.Analytics.Integration.Tests.Telemetry;

/// <summary>
/// Covers settling whose crawlers an address belongs to from the name it answers to.
/// </summary>
/// <remarks>
/// <para>
/// Here rather than with the unit suites because the thing being pinned is a rule about somebody
/// else's name server, and the real one answers differently on different days. What matters is not
/// that a lookup happens but what is done with the two answers it gives: that a name counts only
/// where it sits on a label boundary, that it counts only where it points back, and that neither
/// half on its own puts a company's name beside somebody's traffic.
/// </para>
/// <para>
/// Every one of those has an attack behind it. Anybody may make their own machine answer to
/// <c>crawl-1-2-3-4.googlebot.com</c>, may register <c>not-googlebot.com</c>, and may point a name
/// they hold at an address they do not. The three together are why this check is written the way
/// it is, and why getting any of them wrong would be this product vouching for an impostor.
/// </para>
/// </remarks>
public sealed class CrawlerNameTests
{
    private const string Googlebot = "crawl-66-249-64-9.googlebot.com";
    private const string GoogleAddress = "66.249.64.9";
    private const string Google = "Google";

    [Fact]
    public async Task A_Name_Below_A_Companys_Domain_That_Points_Back_Is_That_Company()
    {
        var answers = new Answers().Says(GoogleAddress, Googlebot, GoogleAddress);

        var settled = await Ask(answers, GoogleAddress);

        settled.Should().ContainKey(GoogleAddress).WhoseValue.Should().Be(Google);
    }

    /// <summary>
    /// Several companies document the domain itself rather than something under it, so a name that
    /// is the domain has to count exactly as one below it does.
    /// </summary>
    [Fact]
    public async Task A_Name_That_Is_The_Domain_Itself_Is_That_Company()
    {
        var answers = new Answers().Says("77.88.5.1", "yandex.ru", "77.88.5.1");

        var settled = await Ask(answers, "77.88.5.1");

        settled.Should().ContainKey("77.88.5.1").WhoseValue.Should().Be("Yandex");
    }

    /// <summary>
    /// The whole security of the reading half. Both of these are names anybody may register and
    /// point at their own machine, and a match written as "ends with the string" would hand them
    /// somebody else's name.
    /// </summary>
    [Theory]
    [InlineData("not-googlebot.com")]
    [InlineData("evil-googlebot.com")]
    [InlineData("googlebot.com.example.net")]
    [InlineData("crawl.googlebot.com.impostor.test")]
    [InlineData("example.net")]
    public async Task A_Name_That_Merely_Resembles_A_Companys_Domain_Is_Nobodys(string hostName)
    {
        var answers = new Answers().Says("203.0.113.7", hostName, "203.0.113.7");

        var settled = await Ask(answers, "203.0.113.7");

        settled.Should().BeEmpty();
    }

    /// <summary>
    /// The half that cannot be forged by whoever holds the address. Making a machine answer to a
    /// name is free; making that name resolve back to it is not.
    /// </summary>
    [Fact]
    public async Task A_Name_That_Points_Somewhere_Else_Establishes_Nobody()
    {
        var answers = new Answers().Says("203.0.113.7", Googlebot, GoogleAddress);

        var settled = await Ask(answers, "203.0.113.7");

        settled.Should().BeEmpty();
    }

    [Fact]
    public async Task A_Name_That_Points_Nowhere_At_All_Establishes_Nobody()
    {
        var answers = new Answers().Says(GoogleAddress, Googlebot);

        var settled = await Ask(answers, GoogleAddress);

        settled.Should().BeEmpty();
    }

    /// <summary>The ordinary case: almost every address answers to nothing worth knowing.</summary>
    [Fact]
    public async Task An_Address_That_Answers_To_No_Name_Establishes_Nobody()
    {
        var settled = await Ask(new Answers(), "198.51.100.4");

        settled.Should().BeEmpty();
    }

    /// <summary>
    /// Names are not case-sensitive and a resolver may or may not put the root's own dot on the
    /// end. Both spellings are the same name and both have to settle the same company.
    /// </summary>
    [Theory]
    [InlineData("Crawl-66-249-64-9.GoogleBot.COM")]
    [InlineData("crawl-66-249-64-9.googlebot.com.")]
    public async Task A_Name_Spelt_Differently_Is_The_Same_Name(string hostName)
    {
        var answers = new Answers().Says(GoogleAddress, hostName, GoogleAddress);

        var settled = await Ask(answers, GoogleAddress);

        settled.Should().ContainKey(GoogleAddress).WhoseValue.Should().Be(Google);
    }

    /// <summary>
    /// A visitor arriving on the newer address family over a connection carrying an older address
    /// is the same visitor, and the name server answers about the older one.
    /// </summary>
    [Fact]
    public async Task An_Older_Address_Carried_In_The_Newer_Form_Is_The_Same_Address()
    {
        var answers = new Answers().Says(GoogleAddress, Googlebot, GoogleAddress);

        var settled = await Ask(answers, "::ffff:66.249.64.9");

        settled.Should().ContainKey("::ffff:66.249.64.9").WhoseValue.Should().Be(Google);
    }

    /// <summary>
    /// What an install behind a proxy that does not pass the visitor's address through sees on
    /// every single visit. No name server has anything to say about these, and asking would be a
    /// question per visit for ever.
    /// </summary>
    [Theory]
    [InlineData("10.1.2.3")]
    [InlineData("192.168.0.5")]
    [InlineData("127.0.0.1")]
    [InlineData("172.16.9.9")]
    [InlineData("fd00::1")]
    [InlineData("not an address")]
    [InlineData("")]
    public async Task An_Address_Nobody_Could_Be_Visiting_From_Is_Never_Asked_About(string value)
    {
        var answers = new Answers();

        var settled = await Ask(answers, value);

        settled.Should().BeEmpty();
        answers.Asked.Should().Be(0);
    }

    /// <summary>
    /// A pass routinely holds several visits from one machine, and one question settles all of them.
    /// </summary>
    [Fact]
    public async Task One_Address_Twice_In_A_Batch_Is_One_Question()
    {
        var answers = new Answers().Says(GoogleAddress, Googlebot, GoogleAddress);

        var settled = await Ask(answers, GoogleAddress, GoogleAddress, GoogleAddress);

        settled.Should().ContainKey(GoogleAddress);
        answers.Asked.Should().Be(1);
    }

    /// <summary>
    /// The same fleet visits over and over. Asking again for every visit would turn one busy
    /// crawler into a stream of questions to its operator's name server.
    /// </summary>
    [Fact]
    public async Task An_Address_Already_Settled_Is_Not_Asked_About_Again()
    {
        var answers = new Answers().Says(GoogleAddress, Googlebot, GoogleAddress);
        var lookup = Lookup(answers, new CrawlerNameOptions(), out _, out _);

        await lookup.OperatorsOfAsync([GoogleAddress], CancellationToken.None);
        var settled = await lookup.OperatorsOfAsync([GoogleAddress], CancellationToken.None);

        settled.Should().ContainKey(GoogleAddress).WhoseValue.Should().Be(Google);
        answers.Asked.Should().Be(1);
    }

    /// <summary>
    /// The answer worth remembering most. A determined visitor from one address would otherwise be
    /// a question about that address for every visit it ever made.
    /// </summary>
    [Fact]
    public async Task An_Address_That_Settled_As_Nobodys_Is_Not_Asked_About_Again()
    {
        var answers = new Answers();
        var lookup = Lookup(answers, new CrawlerNameOptions(), out _, out _);

        await lookup.OperatorsOfAsync(["198.51.100.4"], CancellationToken.None);
        var settled = await lookup.OperatorsOfAsync(["198.51.100.4"], CancellationToken.None);

        settled.Should().BeEmpty();
        answers.Asked.Should().Be(1);
    }

    /// <summary>
    /// A company commissioning a machine is exactly the case a remembered answer would keep
    /// getting wrong, so what is remembered has to expire.
    /// </summary>
    [Fact]
    public async Task An_Answer_Old_Enough_To_Have_Changed_Is_Asked_About_Again()
    {
        var answers = new Answers();
        var settings = new CrawlerNameOptions { RememberFor = TimeSpan.FromHours(1) };
        var lookup = Lookup(answers, settings, out var clock, out _);

        await lookup.OperatorsOfAsync([GoogleAddress], CancellationToken.None);

        clock.Advance(TimeSpan.FromHours(2));
        answers.Says(GoogleAddress, Googlebot, GoogleAddress);

        var settled = await lookup.OperatorsOfAsync([GoogleAddress], CancellationToken.None);

        settled.Should().ContainKey(GoogleAddress).WhoseValue.Should().Be(Google);
        answers.Asked.Should().Be(2);
    }

    /// <summary>
    /// What an install with no way out to the internet — or one whose operator would rather no
    /// visitor's address left the building — behaves like. It measures everything and settles
    /// nothing, which is the honest answer rather than a degraded one.
    /// </summary>
    [Fact]
    public async Task An_Installation_That_May_Not_Ask_Asks_Nothing()
    {
        var answers = new Answers().Says(GoogleAddress, Googlebot, GoogleAddress);
        var lookup = Lookup(answers, new CrawlerNameOptions { Enabled = false }, out _, out _);

        var settled = await lookup.OperatorsOfAsync([GoogleAddress], CancellationToken.None);

        settled.Should().BeEmpty();
        answers.Asked.Should().Be(0);
    }

    /// <summary>
    /// What makes an identity found this way last. Once the collector knows the address, it stamps
    /// the answer onto stored activity, where it outlives the address it was worked out from.
    /// </summary>
    [Fact]
    public async Task A_Settled_Address_Is_Recognised_By_The_Collector_Afterwards()
    {
        var answers = new Answers().Says(GoogleAddress, Googlebot, GoogleAddress);
        var lookup = Lookup(answers, new CrawlerNameOptions(), out _, out var store);

        store.OperatorOf(GoogleAddress).Should().BeNull();

        await lookup.OperatorsOfAsync([GoogleAddress], CancellationToken.None);

        store.OperatorOf(GoogleAddress).Should().Be(Google);
        store.Established.Should().Be(1);
    }

    /// <summary>
    /// A slow name server costs one address and is not an answer about it. Recording silence as
    /// "nobody" would let one bad moment settle an address for a day.
    /// </summary>
    [Fact]
    public async Task A_Question_That_Went_Unanswered_Settles_Nothing_And_Is_Asked_Again()
    {
        var answers = new Answers { Pause = TimeSpan.FromMinutes(1) };
        var settings = new CrawlerNameOptions { LookupTimeout = TimeSpan.FromMilliseconds(50) };
        var lookup = Lookup(answers, settings, out _, out _);

        var settled = await lookup.OperatorsOfAsync([GoogleAddress], CancellationToken.None);

        settled.Should().BeEmpty();

        answers.Pause = TimeSpan.Zero;
        answers.Says(GoogleAddress, Googlebot, GoogleAddress);

        var again = await lookup.OperatorsOfAsync([GoogleAddress], CancellationToken.None);

        again.Should().ContainKey(GoogleAddress).WhoseValue.Should().Be(Google);
    }

    /// <summary>
    /// Two companies cannot both be answered for one name, so no company's domain may sit below
    /// another's. If one ever did, whichever happened to sort first would take the traffic.
    /// </summary>
    [Fact]
    public void No_Companys_Domain_Sits_Below_Another_Companys()
    {
        foreach (var claim in ConfirmingHosts.Claims)
        {
            var overlapping = ConfirmingHosts.Claims
                .Where(other => !string.Equals(other.Operator, claim.Operator, StringComparison.Ordinal))
                .Where(other => other.Suffix.EndsWith(claim.Dotted, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(other.Suffix, claim.Suffix, StringComparison.OrdinalIgnoreCase));

            overlapping.Should().BeEmpty(
                "no company's domain may sit below another's, and {0} is claimed by {1}",
                claim.Suffix,
                claim.Operator);
        }
    }

    /// <summary>
    /// Shared infrastructure is not an identity. Google documents the domain its App Engine
    /// applications answer from alongside its own, and anybody may run one there.
    /// </summary>
    [Fact]
    public void A_Domain_Anybody_Can_Answer_From_Is_Nobodys()
    {
        ConfirmingHosts.OperatorOf("203-0-113-7.gae.googleusercontent.com").Should().BeNull();
    }

    private static async Task<IReadOnlyDictionary<string, string>> Ask(Answers answers, params string[] addresses)
    {
        var lookup = Lookup(answers, new CrawlerNameOptions(), out _, out _);

        return await lookup.OperatorsOfAsync(addresses, CancellationToken.None);
    }

    private static CrawlerNameLookup Lookup(
        Answers answers,
        CrawlerNameOptions settings,
        out FakeTimeProvider clock,
        out CrawlerRangeStore store)
    {
        clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero));
        store = new CrawlerRangeStore();

        return new CrawlerNameLookup(
            answers,
            store,
            Options.Create(settings),
            clock,
            NullLogger<CrawlerNameLookup>.Instance);
    }

    /// <summary>
    /// A name server that says exactly what a test tells it to.
    /// </summary>
    /// <remarks>
    /// Both halves are set separately on purpose, because every case worth pinning is one where
    /// they disagree: a name that resolves elsewhere, a name that resolves nowhere, an address
    /// with no name at all.
    /// </remarks>
    private sealed class Answers : IReverseNameLookup
    {
        private readonly Dictionary<string, ResolvedName> _replies = new(StringComparer.Ordinal);

        private int _asked;

        /// <summary>How many addresses were actually asked about.</summary>
        public int Asked => Volatile.Read(ref _asked);

        /// <summary>How long a question takes to answer.</summary>
        public TimeSpan Pause { get; set; }

        /// <summary>Sets what one address answers to, and where that name points.</summary>
        public Answers Says(string address, string hostName, params string[] pointsAt)
        {
            _replies[address] = new ResolvedName(
                hostName,
                [.. pointsAt.Select(IPAddress.Parse)]);

            return this;
        }

        public async Task<ResolvedName> ResolveAsync(IPAddress address, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _asked);

            if (Pause > TimeSpan.Zero)
            {
                await Task.Delay(Pause, cancellationToken);
            }

            return _replies.TryGetValue(address.ToString(), out var reply) ? reply : ResolvedName.None;
        }
    }
}
