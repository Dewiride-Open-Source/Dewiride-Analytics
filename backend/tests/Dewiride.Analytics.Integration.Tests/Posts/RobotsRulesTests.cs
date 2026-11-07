using System.Text;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Posts;

namespace Dewiride.Analytics.Integration.Tests.Posts;

/// <summary>
/// Covers following a website's robots.txt, RFC 9309 being the specification and its own examples the
/// first thing to get right.
/// </summary>
public sealed class RobotsRulesTests
{
    private const string Host = "https://example.com";

    /// <summary>The simple example in section 5.1 of the RFC.</summary>
    private const string RfcExample = """
        User-Agent: *
        Disallow: *.gif$
        Disallow: /example/
        Allow: /publications/

        User-Agent: foobot
        Disallow:/
        Allow:/example/page.html
        Allow:/example/allowed.gif

        User-Agent: barbot
        User-Agent: bazbot
        Disallow: /example/page.html

        User-Agent: quxbot
        """;

    [Theory]
    // Everybody else.
    [InlineData("otherbot", "/picture.gif", false)]
    [InlineData("otherbot", "/deep/down/picture.gif", false)]
    [InlineData("otherbot", "/picture.gif.html", true)]
    [InlineData("otherbot", "/example/page.html", false)]
    [InlineData("otherbot", "/publications/page.html", true)]
    [InlineData("otherbot", "/publications/picture.gif", true)]
    [InlineData("otherbot", "/anything", true)]
    // A rule with no space after its colon, and an allow more specific than a disallow.
    [InlineData("foobot", "/anything", false)]
    [InlineData("foobot", "/example/page.html", true)]
    [InlineData("foobot", "/example/allowed.gif", true)]
    [InlineData("foobot", "/example/other.gif", false)]
    // One group for two agents.
    [InlineData("barbot", "/example/page.html", false)]
    [InlineData("barbot", "/example/other.html", true)]
    [InlineData("bazbot", "/example/page.html", false)]
    [InlineData("bazbot", "/anything", true)]
    // A group with no rules, at the end of the file.
    [InlineData("quxbot", "/example/page.html", true)]
    [InlineData("quxbot", "/picture.gif", true)]
    public void The_Example_In_The_Specification_Means_What_It_Says(string agent, string path, bool permitted)
    {
        RobotsRules.Read(RfcExample, agent, whole: true).Permits(Address(path)).Should().Be(permitted);
    }

    /// <summary>The longest-match example in section 5.2 of the RFC.</summary>
    [Fact]
    public void The_Longest_Rule_That_Matches_Decides()
    {
        var rules = Read("""
            User-Agent: DewirideAnalytics
            Allow: /example/page/
            Disallow: /example/page/disallowed.gif
            """);

        rules.Permits(Address("/example/page/disallowed.gif")).Should().BeFalse();
        rules.Permits(Address("/example/page/other.gif")).Should().BeTrue();
    }

    /// <summary>
    /// Whichever way round they are written, because which rule decides is a question of the rules and
    /// not of where they stand in the file.
    /// </summary>
    /// <param name="rule">One of two rules.</param>
    /// <param name="other">The other.</param>
    [Theory]
    [InlineData("Allow: /a", "Disallow: /")]
    [InlineData("Disallow: /", "Allow: /a")]
    public void The_Order_Rules_Are_Written_In_Does_Not_Matter(string rule, string other)
    {
        var rules = Read($"User-Agent: *\n{rule}\n{other}\n");

        rules.Permits(Address("/a")).Should().BeTrue();
        rules.Permits(Address("/b")).Should().BeFalse();
    }

    [Theory]
    [InlineData("Allow: /p", "Disallow: /p")]
    [InlineData("Disallow: /p", "Allow: /p")]
    [InlineData("Allow: /*", "Disallow: /p")]
    [InlineData("Disallow: /p", "Allow: /*")]
    public void An_Allow_Wins_A_Tie(string rule, string other)
    {
        var rules = Read($"User-Agent: *\n{rule}\n{other}\n");

        rules.Permits(Address("/page")).Should().BeTrue();
    }

    [Theory]
    [InlineData("User-Agent: DewirideAnalytics")]
    [InlineData("user-agent: dewirideanalytics")]
    [InlineData("USER-AGENT: DEWIRIDEANALYTICS")]
    [InlineData("  User-Agent  :  DewirideAnalytics  ")]
    [InlineData("User-Agent: DewirideAnalytics/1.0")]
    [InlineData("User-Agent: DewirideAnalytics # that's us")]
    public void The_Readers_Name_Is_Found_However_It_Is_Written(string line)
    {
        var rules = Read($"{line}\nDisallow: /private\n\nUser-Agent: *\nDisallow: /\n");

        rules.Permits(Address("/private")).Should().BeFalse();
        rules.Permits(Address("/public")).Should().BeTrue();
    }

    /// <summary>A name only starting the same way is somebody else's.</summary>
    /// <param name="name">The name in the file.</param>
    [Theory]
    [InlineData("DewirideAnalyticsBot")]
    [InlineData("Dewiride")]
    [InlineData("OtherDewirideAnalytics")]
    [InlineData("Dewiride-Analytics")]
    public void Another_Agents_Name_Is_Another_Agents(string name)
    {
        Read($"User-Agent: {name}\nDisallow: /\n").Permits(Address("/page")).Should().BeTrue();
    }

    /// <summary>Figure 2 of the RFC.</summary>
    [Fact]
    public void Every_Group_Naming_The_Reader_Is_Read_As_One()
    {
        var rules = Read("""
            User-Agent: DewirideAnalytics
            Disallow: /foo
            Disallow: /bar

            User-Agent: *
            Disallow: /everybody

            User-Agent: DewirideAnalytics
            Disallow: /baz
            """);

        rules.Permits(Address("/foo")).Should().BeFalse();
        rules.Permits(Address("/bar")).Should().BeFalse();
        rules.Permits(Address("/baz")).Should().BeFalse();
        rules.Permits(Address("/everybody")).Should().BeTrue();
    }

    /// <summary>Figure 3 of the RFC.</summary>
    [Fact]
    public void Where_No_Group_Names_The_Reader_The_Groups_For_Everybody_Are_Read()
    {
        var rules = Read("""
            User-Agent: *
            Disallow: /foo
            Disallow: /bar

            User-Agent: BazBot
            Disallow: /baz

            User-Agent: *
            Disallow: /qux
            """);

        rules.Permits(Address("/foo")).Should().BeFalse();
        rules.Permits(Address("/bar")).Should().BeFalse();
        rules.Permits(Address("/qux")).Should().BeFalse();
        rules.Permits(Address("/baz")).Should().BeTrue();
    }

    /// <summary>
    /// A website that wants to wave one reader through writes exactly this, and falling back to
    /// everybody's rules would do the opposite of what it asked.
    /// </summary>
    [Fact]
    public void A_Group_Naming_The_Reader_With_No_Rules_Still_Takes_Precedence()
    {
        var rules = Read("""
            User-Agent: *
            Disallow: /

            User-Agent: DewirideAnalytics
            """);

        rules.Permits(Address("/page")).Should().BeTrue();
    }

    [Fact]
    public void A_Group_Naming_The_Reader_With_An_Empty_Rule_Permits_Everything()
    {
        var rules = Read("""
            User-Agent: *
            Disallow: /

            User-Agent: DewirideAnalytics
            Disallow:
            """);

        rules.Permits(Address("/page")).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n\n\n")]
    [InlineData("# nothing but a comment\n")]
    [InlineData("User-Agent: SomebodyElse\nDisallow: /\n")]
    [InlineData("This is not a robots.txt at all.\n<html><body>Not found</body></html>\n")]
    public void A_File_With_Nothing_To_Say_To_The_Reader_Permits_Everything(string text)
    {
        Read(text).Permits(Address("/page")).Should().BeTrue();
    }

    /// <summary>
    /// The syntax allows blank lines between the agent lines of one group, so the first of these
    /// names both agents, and a rule does not stop being one for being written after a blank line.
    /// </summary>
    [Fact]
    public void Blank_Lines_Do_Not_End_A_Group()
    {
        var rules = Read("""
            User-Agent: SomebodyElse

            User-Agent: DewirideAnalytics

            Disallow: /private

            Disallow: /secret
            """);

        rules.Permits(Address("/private")).Should().BeFalse();
        rules.Permits(Address("/secret")).Should().BeFalse();
    }

    [Fact]
    public void Lines_About_Something_Else_Do_Not_End_A_Group()
    {
        var rules = Read("""
            User-Agent: DewirideAnalytics
            Crawl-delay: 10
            Sitemap: https://example.com/sitemap.xml
            Disallow: /private
            Something: else
            Disallow: /secret
            """);

        rules.Permits(Address("/private")).Should().BeFalse();
        rules.Permits(Address("/secret")).Should().BeFalse();
    }

    [Fact]
    public void A_Rule_Before_Any_Group_Is_Nobodys()
    {
        var rules = Read("""
            Disallow: /early
            User-Agent: DewirideAnalytics
            Disallow: /private
            """);

        rules.Permits(Address("/early")).Should().BeTrue();
        rules.Permits(Address("/private")).Should().BeFalse();
    }

    [Theory]
    [InlineData("User-Agent: DewirideAnalytics\nDisallow: /private\n")]
    [InlineData("User-Agent: DewirideAnalytics\r\nDisallow: /private\r\n")]
    [InlineData("User-Agent: DewirideAnalytics\rDisallow: /private\r")]
    [InlineData("\uFEFFUser-Agent: DewirideAnalytics\nDisallow: /private\n")]
    [InlineData("User-Agent: DewirideAnalytics\n\tDisallow\t:\t/private\t\n")]
    [InlineData("User-Agent: DewirideAnalytics\nDISALLOW: /private # not for you\n")]
    [InlineData("# A comment\nUser-Agent: DewirideAnalytics # Another\n# And another\nDisallow: /private#page\n")]
    public void However_A_File_Is_Laid_Out_Its_Rules_Are_Read(string text)
    {
        Read(text).Permits(Address("/private")).Should().BeFalse();
    }

    [Fact]
    public void The_Comparison_Is_Case_Sensitive()
    {
        var rules = Read("User-Agent: *\nDisallow: /Private\n");

        rules.Permits(Address("/Private")).Should().BeFalse();
        rules.Permits(Address("/private")).Should().BeTrue();
    }

    [Theory]
    // A rule matches from the first character of the path, and to its end only if it says so.
    [InlineData("/fish", "/fish", false)]
    [InlineData("/fish", "/fish.html", false)]
    [InlineData("/fish", "/fish/salmon.html", false)]
    [InlineData("/fish", "/Fish.asp", true)]
    [InlineData("/fish", "/catfish", true)]
    [InlineData("fish", "/fish", true)]
    // A wildcard stands for anything, a slash included, or for nothing at all.
    [InlineData("/*", "/anything", false)]
    [InlineData("*", "/anything", false)]
    [InlineData("/fish*", "/fish", false)]
    [InlineData("/*.php", "/index.php", false)]
    [InlineData("/*.php", "/folder/filename.php?parameters", false)]
    [InlineData("/*.php", "/windows.PHP", true)]
    [InlineData("/fish*.php", "/fish.php", false)]
    [InlineData("/fish*.php", "/fishheads/catfish.php?parameters", false)]
    [InlineData("/a*b*c", "/aXbYc", false)]
    [InlineData("/a*b*c", "/acb", true)]
    [InlineData("/a**b", "/aXb", false)]
    // The earliest place a piece fits is as good as any later one.
    [InlineData("/*ab*abc", "/xabxabcx", false)]
    [InlineData("/*aab", "/aaab", false)]
    [InlineData("/*abcabd", "/abcabcabd", false)]
    [InlineData("/*abcabd", "/abcabcabe", true)]
    // A dollar sign at the end means the path ends there too.
    [InlineData("/*.php$", "/filename.php", false)]
    [InlineData("/*.php$", "/folder/filename.php", false)]
    [InlineData("/*.php$", "/filename.php?parameters", true)]
    [InlineData("/*.php$", "/filename.php/", true)]
    [InlineData("/fish$", "/fish", false)]
    [InlineData("/fish$", "/fish.html", true)]
    [InlineData("/$", "/", false)]
    [InlineData("/$", "/page", true)]
    [InlineData("/*$", "/page", false)]
    // Anywhere else it is a dollar sign.
    [InlineData("/price$now", "/price$now", false)]
    [InlineData("/price$now", "/pricenow", true)]
    // A rule written for a query string has no question mark to say so.
    [InlineData("/*sort=", "/products?sort=asc", false)]
    [InlineData("/*?", "/products?a=1", false)]
    [InlineData("/*?", "/products", true)]
    [InlineData("/*&page=", "/products?a=1&page=2", false)]
    [InlineData("/products?a=", "/products?a=1", false)]
    public void Wildcards_And_End_Markers_Mean_What_They_Mean(string pattern, string path, bool permitted)
    {
        Read($"User-Agent: *\nDisallow: {pattern}\n").Permits(Address(path)).Should().Be(permitted);
    }

    [Theory]
    // Figure 4 of the RFC.
    [InlineData("/foo/bar?baz=quz", "/foo/bar?baz=quz")]
    [InlineData("/foo/bar?baz=https%3A%2F%2Ffoo.bar", "/foo/bar?baz=https://foo.bar")]
    [InlineData("/foo/bar?baz=https://foo.bar", "/foo/bar?baz=https://foo.bar")]
    [InlineData("/foo/bar/%E3%83%84", "/foo/bar/ツ")]
    [InlineData("/foo/bar/ツ", "/foo/bar/%E3%83%84")]
    [InlineData("/foo/bar/ツ", "/foo/bar/ツ")]
    [InlineData("/foo/bar/%E3%83%84", "/foo/bar/%E3%83%84")]
    [InlineData("/foo/bar/%62%61%7A", "/foo/bar/baz")]
    [InlineData("/foo/bar/baz", "/foo/bar/%62%61%7A")]
    // Figure 6 of the RFC.
    [InlineData("/path/file-with-a-%2A.html", "/path/file-with-a-*.html")]
    [InlineData("/path/foo-%24", "/path/foo-$")]
    // Two spellings of one escape.
    [InlineData("/caf%c3%a9", "/caf%C3%A9")]
    [InlineData("/caf%C3%A9", "/caf%c3%a9")]
    [InlineData("/%7Euser", "/~user")]
    [InlineData("/%7euser", "/~user")]
    // Things a rule may write as they are that an address has to escape.
    [InlineData("/with space", "/with%20space")]
    [InlineData("/quote\"d", "/quote%22d")]
    public void Two_Spellings_Of_One_Address_Are_One_Address(string pattern, string path)
    {
        Read($"User-Agent: *\nDisallow: {pattern}\n").Permits(Address(path)).Should().BeFalse();
    }

    [Theory]
    // An escaped reserved character is not the character, in a path.
    [InlineData("/a%3Bb", "/a%3Cb")]
    // An escape that does not name the same thing.
    [InlineData("/%E3%83%84", "/%E3%83%85")]
    // A wildcard only stands in for one when it is written plainly.
    [InlineData("/path/file-with-a-%2A.html", "/path/file-with-a-b.html")]
    public void Different_Addresses_Stay_Different(string pattern, string path)
    {
        Read($"User-Agent: *\nDisallow: {pattern}\n").Permits(Address(path)).Should().BeTrue();
    }

    [Fact]
    public void A_Percent_Sign_That_Starts_No_Escape_Is_A_Percent_Sign()
    {
        var rules = Read("User-Agent: *\nDisallow: /100%\nDisallow: /5%z\n");

        rules.Permits(Address("/100%25")).Should().BeFalse();
        rules.Permits(Address("/5%25z")).Should().BeFalse();
        rules.Permits(Address("/100")).Should().BeTrue();
    }

    [Fact]
    public void The_Query_String_Is_Part_Of_What_A_Rule_Is_Matched_Against()
    {
        var rules = Read("User-Agent: *\nDisallow: /sitemap.xml?private\n");

        rules.Permits(Address("/sitemap.xml?private=1")).Should().BeFalse();
        rules.Permits(Address("/sitemap.xml?public=1")).Should().BeTrue();
    }

    [Fact]
    public void The_File_Itself_May_Always_Be_Asked_For()
    {
        Read("User-Agent: *\nDisallow: /\n").Permits(Address("/robots.txt")).Should().BeTrue();
    }

    /// <summary>
    /// The start of a rule is not the rule. An allow cut short permits more than was ever written, so a
    /// file that ran past what is read keeps only the lines that ended.
    /// </summary>
    [Fact]
    public void Where_A_File_Was_Cut_Off_Its_Unfinished_Last_Line_Is_Left_Out()
    {
        const string text = "User-Agent: *\nDisallow: /\nAllow: /public/pages/first\nAllow: /pub";

        RobotsRules.Read(text, WebsiteHttp.ProductToken, whole: false)
            .Permits(Address("/public/pages/second")).Should().BeFalse();

        RobotsRules.Read(text, WebsiteHttp.ProductToken, whole: true)
            .Permits(Address("/public/pages/second")).Should().BeTrue();
    }

    [Fact]
    public void Where_A_File_Was_Cut_Off_At_The_End_Of_A_Line_That_Line_Stands()
    {
        const string text = "User-Agent: *\nDisallow: /private\n";

        RobotsRules.Read(text, WebsiteHttp.ProductToken, whole: false)
            .Permits(Address("/private")).Should().BeFalse();
    }

    /// <summary>
    /// There is no telling which of the rules left out would have decided, so when in doubt the website's
    /// wishes win and nothing is read.
    /// </summary>
    [Fact]
    public void More_Rules_Than_Are_Kept_Permit_Nothing()
    {
        var text = new StringBuilder("User-Agent: DewirideAnalytics\n");

        for (var rule = 0; rule <= 10_000; rule++)
        {
            text.Append("Disallow: /").Append(rule).Append('\n');
        }

        Read(text.ToString()).Permits(Address("/not-mentioned")).Should().BeFalse();
    }

    [Fact]
    public void As_Many_Rules_As_Are_Kept_Are_Followed()
    {
        var text = new StringBuilder("User-Agent: DewirideAnalytics\n");

        for (var rule = 0; rule < 10_000; rule++)
        {
            text.Append("Disallow: /").Append(rule).Append('/').Append('\n');
        }

        var rules = Read(text.ToString());

        rules.Permits(Address("/not-mentioned")).Should().BeTrue();
        rules.Permits(Address("/9999/page")).Should().BeFalse();
    }

    /// <summary>
    /// Only rules for the reader count against what is kept, so a file saying a great deal to other
    /// crawlers costs this reader nothing.
    /// </summary>
    [Fact]
    public void Rules_For_Somebody_Else_Do_Not_Count_Towards_What_Is_Kept()
    {
        var text = new StringBuilder("User-Agent: SomebodyElse\n");

        for (var rule = 0; rule <= 10_000; rule++)
        {
            text.Append("Disallow: /").Append(rule).Append('\n');
        }

        text.Append("User-Agent: DewirideAnalytics\nDisallow: /private\n");

        var rules = Read(text.ToString());

        rules.Permits(Address("/public")).Should().BeTrue();
        rules.Permits(Address("/private")).Should().BeFalse();
    }

    /// <summary>
    /// No address read here is that long, so such a rule has nothing to match, and nothing is spent
    /// getting it ready to try.
    /// </summary>
    [Fact]
    public void A_Rule_Longer_Than_Any_Address_Is_Left_Out()
    {
        var rules = Read($"User-Agent: *\nDisallow: /{new string('a', 3 * Site.MaxSitemapAddressLength)}\n");

        rules.Permits(Address("/" + new string('a', 2000))).Should().BeTrue();
    }

    /// <summary>
    /// What a matcher that goes back and tries again takes more than the age of the universe over. The
    /// limits here are the most a file can ask for and the longest address that is read, and the
    /// allowance is generous: this is about not being in a different league, not about being quick.
    /// </summary>
    /// <param name="shape">Which pathological rule to try.</param>
    [Theory]
    [InlineData(PathologicalShape.ManyStars)]
    [InlineData(PathologicalShape.ManyStarsEachRuleOfMany)]
    [InlineData(PathologicalShape.OneLongRepeatingRun)]
    public void A_Pathological_Rule_Does_Not_Take_Long_To_Answer(PathologicalShape shape)
    {
        var path = "/" + new string('a', Site.MaxSitemapAddressLength - 50);
        var address = Address(path);
        var rules = Read(FileOf(shape));

        var began = TimeProvider.System.GetTimestamp();

        rules.Permits(address).Should().BeTrue();

        TimeProvider.System.GetElapsedTime(began).Should().BeLessThan(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Reading_A_File_The_Size_Of_The_Limit_Does_Not_Take_Long()
    {
        var text = "User-Agent: *\n" + string.Concat(Enumerable.Repeat("Disallow: /*a*a*a*a*a*a*a*b\n", 10_000));
        var began = TimeProvider.System.GetTimestamp();

        Read(text).Permits(Address("/" + new string('a', 2000))).Should().BeTrue();

        TimeProvider.System.GetElapsedTime(began).Should().BeLessThan(TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// What a website's robots.txt is, for the rules above: a rule that nearly matches an address made
    /// of one letter, over and over.
    /// </summary>
    public enum PathologicalShape
    {
        /// <summary>A star between every two letters, and an end that never comes.</summary>
        ManyStars,

        /// <summary>The same dozen stars, in as many rules as are kept.</summary>
        ManyStarsEachRuleOfMany,

        /// <summary>One long run that matches almost all the way, wherever it is tried.</summary>
        OneLongRepeatingRun,
    }

    private static string FileOf(PathologicalShape shape) => shape switch
    {
        PathologicalShape.ManyStars =>
            $"User-Agent: *\nDisallow: /{string.Concat(Enumerable.Repeat("*a", 2000))}*b\n",

        PathologicalShape.ManyStarsEachRuleOfMany =>
            "User-Agent: *\n" + string.Concat(Enumerable.Repeat(
                $"Disallow: /{string.Concat(Enumerable.Repeat("*a", 12))}*b\n",
                10_000)),

        _ => $"User-Agent: *\nDisallow: /*{new string('a', 1000)}b\n",
    };

    private static RobotsRules Read(string text) => RobotsRules.Read(text, WebsiteHttp.ProductToken, whole: true);

    private static Uri Address(string path) => new(Host + path);
}
