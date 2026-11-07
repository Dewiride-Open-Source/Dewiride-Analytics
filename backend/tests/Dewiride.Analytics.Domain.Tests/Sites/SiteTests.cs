using Dewiride.Analytics.Domain.Sites;

namespace Dewiride.Analytics.Domain.Tests.Sites;

/// <summary>
/// Covers the rules a site enforces about itself.
/// </summary>
/// <remarks>
/// Two of these carry more weight than the rest. The domain is normalised on the way in because
/// the collector matches an incoming report's host against it, so a stored value that differs by
/// case or a trailing dot silently rejects every report a site receives. The time zone is
/// validated where it is set because it reaches the telemetry store as the zone daily buckets are
/// cut in, and an unrecognised one would otherwise surface much later as a failed query.
/// </remarks>
public sealed class SiteTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 3, 14, 9, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Example.COM", "example.com")]
    [InlineData("  example.com  ", "example.com")]
    [InlineData("example.com.", "example.com")]
    [InlineData("Blog.Example.COM.", "blog.example.com")]
    public void Constructor_Normalises_The_Domain(string supplied, string expected)
    {
        var site = NewSite(domain: supplied);

        site.Domain.Should().Be(expected);
    }

    [Fact]
    public void Constructor_Defaults_The_Display_Name_To_The_Normalised_Domain()
    {
        var site = NewSite(domain: "Example.COM");

        site.DisplayName.Should().Be("example.com");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Rejects_A_Blank_Domain(string blank)
    {
        var act = () => NewSite(domain: blank);

        act.Should().Throw<ArgumentException>().WithParameterName("domain");
    }

    [Theory]
    [InlineData("Europe/London")]
    [InlineData("Asia/Kolkata")]
    [InlineData("America/Sao_Paulo")]
    [InlineData("Etc/UTC")]
    public void Constructor_Accepts_An_Iana_Time_Zone(string timeZoneId)
    {
        var site = NewSite(timeZoneId: timeZoneId);

        site.TimeZoneId.Should().Be(timeZoneId);
    }

    [Theory]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("GMT+5")]
    [InlineData("   ")]
    public void Constructor_Rejects_An_Unknown_Time_Zone(string unknown)
    {
        var act = () => NewSite(timeZoneId: unknown);

        act.Should().Throw<ArgumentException>().WithParameterName("timeZoneId");
    }

    [Fact]
    public void Constructor_Leaves_Query_String_Retention_Off()
    {
        var site = NewSite();

        site.RetainQueryStrings.Should().BeFalse();
    }

    /// <summary>
    /// Off until somebody who knows the site switches it on. What is kept of a press is the words
    /// on the control, and on a site that shows its visitors their own messages, names and
    /// accounts, those words are the visitors' own.
    /// </summary>
    [Fact]
    public void Constructor_Leaves_Click_Capture_Off()
    {
        var site = NewSite();

        site.CaptureClicks.Should().BeFalse();
    }

    [Fact]
    public void SetClickCapture_Turns_Capture_On_And_Off_Again()
    {
        var site = NewSite();

        site.SetClickCapture(true);
        site.CaptureClicks.Should().BeTrue();

        site.SetClickCapture(false);
        site.CaptureClicks.Should().BeFalse();
    }

    [Fact]
    public void Constructor_Leaves_The_Origin_List_Empty()
    {
        var site = NewSite();

        site.AllowedOrigins.Should().BeEmpty();
    }

    [Fact]
    public void SetDisplayName_Trims_The_Name()
    {
        var site = NewSite();

        site.SetDisplayName("  Example Blog  ");

        site.DisplayName.Should().Be("Example Blog");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SetDisplayName_Rejects_A_Blank_Name(string blank)
    {
        var site = NewSite();

        var act = () => site.SetDisplayName(blank);

        act.Should().Throw<ArgumentException>().WithParameterName("displayName");
    }

    /// <summary>
    /// The limit is the width the column is declared at, so a name the site accepts is a name the
    /// database can store. Refusing it here is what turns an over-long name into an answer
    /// somebody can act on rather than a save that reports nothing until it reaches PostgreSQL.
    /// </summary>
    [Fact]
    public void SetDisplayName_Accepts_A_Name_At_The_Limit()
    {
        var site = NewSite();
        var longest = new string('n', Site.MaxDisplayNameLength);

        site.SetDisplayName(longest);

        site.DisplayName.Should().Be(longest);
    }

    [Fact]
    public void SetDisplayName_Rejects_A_Name_Past_The_Limit()
    {
        var site = NewSite();

        var act = () => site.SetDisplayName(new string('n', Site.MaxDisplayNameLength + 1));

        act.Should().Throw<ArgumentException>().WithParameterName("displayName");
    }

    /// <summary>
    /// Length is measured on what will be stored. A name pasted with space around it is the same
    /// name, and refusing it for a width it does not have would be a rule about the clipboard.
    /// </summary>
    [Fact]
    public void SetDisplayName_Measures_The_Limit_After_Trimming()
    {
        var site = NewSite();
        var longest = new string('n', Site.MaxDisplayNameLength);

        site.SetDisplayName($"   {longest}   ");

        site.DisplayName.Should().Be(longest);
    }

    [Fact]
    public void SetDisplayName_Keeps_The_Existing_Name_When_The_New_One_Is_Refused()
    {
        var site = NewSite();
        site.SetDisplayName("Example Blog");

        var act = () => site.SetDisplayName(new string('n', Site.MaxDisplayNameLength + 1));

        act.Should().Throw<ArgumentException>();
        site.DisplayName.Should().Be("Example Blog");
    }

    [Theory]
    [InlineData("Europe/London")]
    [InlineData("Asia/Kolkata")]
    [InlineData("America/Sao_Paulo")]
    [InlineData("Etc/UTC")]
    public void SetTimeZone_Moves_The_Zone_Days_Are_Counted_In(string timeZoneId)
    {
        var site = NewSite(timeZoneId: "Europe/Berlin");

        site.SetTimeZone(timeZoneId);

        site.TimeZoneId.Should().Be(timeZoneId);
    }

    [Theory]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("GMT+5")]
    [InlineData("")]
    [InlineData("   ")]
    public void SetTimeZone_Rejects_A_Zone_This_Installation_Does_Not_Know(string unknown)
    {
        var site = NewSite();

        var act = () => site.SetTimeZone(unknown);

        act.Should().Throw<ArgumentException>().WithParameterName("timeZoneId");
    }

    /// <summary>
    /// A refused zone never reaches the site, so its days go on being cut where they were rather
    /// than on an identifier the telemetry store would not recognise on the next read.
    /// </summary>
    [Fact]
    public void SetTimeZone_Keeps_The_Existing_Zone_When_The_New_One_Is_Refused()
    {
        var site = NewSite(timeZoneId: "Asia/Kolkata");

        var act = () => site.SetTimeZone("Mars/Olympus_Mons");

        act.Should().Throw<ArgumentException>();
        site.TimeZoneId.Should().Be("Asia/Kolkata");
    }

    /// <summary>
    /// The zone decides where the days are cut and settles nothing else. Moving it must not
    /// disturb the hostname the collector matches every incoming report against.
    /// </summary>
    [Fact]
    public void SetTimeZone_Changes_Nothing_But_The_Zone()
    {
        var site = NewSite(domain: "blog.example.com", timeZoneId: "Etc/UTC");
        site.SetDisplayName("Example Blog");
        site.SetClickCapture(true);

        site.SetTimeZone("Asia/Kolkata");

        site.Domain.Should().Be("blog.example.com");
        site.DisplayName.Should().Be("Example Blog");
        site.CaptureClicks.Should().BeTrue();
    }

    [Fact]
    public void SetQueryStringRetention_Turns_Retention_On()
    {
        var site = NewSite();

        site.SetQueryStringRetention(true);

        site.RetainQueryStrings.Should().BeTrue();
    }

    [Fact]
    public void ReplaceAllowedOrigins_Normalises_Every_Entry()
    {
        var site = NewSite();
        string[] origins = ["Docs.Example.COM", "  cdn.example.com.  "];

        site.ReplaceAllowedOrigins(origins);

        site.AllowedOrigins.Should().Equal("docs.example.com", "cdn.example.com");
    }

    [Fact]
    public void ReplaceAllowedOrigins_Discards_Blank_Entries()
    {
        var site = NewSite();
        string[] origins = ["docs.example.com", "", "   "];

        site.ReplaceAllowedOrigins(origins);

        site.AllowedOrigins.Should().ContainSingle().Which.Should().Be("docs.example.com");
    }

    [Fact]
    public void ReplaceAllowedOrigins_Replaces_Rather_Than_Appends()
    {
        var site = NewSite();
        string[] first = ["docs.example.com"];
        string[] second = ["cdn.example.com"];

        site.ReplaceAllowedOrigins(first);
        site.ReplaceAllowedOrigins(second);

        site.AllowedOrigins.Should().ContainSingle().Which.Should().Be("cdn.example.com");
    }

    [Fact]
    public void ReplaceAllowedOrigins_With_Null_Restores_The_Default()
    {
        var site = NewSite();
        string[] origins = ["docs.example.com"];

        site.ReplaceAllowedOrigins(origins);
        site.ReplaceAllowedOrigins(null);

        site.AllowedOrigins.Should().BeEmpty();
    }

    [Fact]
    public void TransferTo_Moves_The_Site_To_Another_Organisation()
    {
        var site = NewSite();
        var destination = Guid.NewGuid();

        site.TransferTo(destination);

        site.OrganizationId.Should().Be(destination);
    }

    /// <summary>
    /// Nothing is fetched for a website until somebody who runs it names what to fetch.
    /// </summary>
    [Fact]
    public void Constructor_Leaves_The_Sitemap_Unnamed()
    {
        NewSite().SitemapAddress.Should().BeNull();
    }

    [Fact]
    public void A_Sitemap_On_The_Websites_Own_Address_Is_Kept()
    {
        var site = NewSite(domain: "example.com");

        site.SetSitemapAddress("https://example.com/sitemap.xml");

        site.SitemapAddress.Should().Be("https://example.com/sitemap.xml");
    }

    /// <summary>
    /// A website measured at <c>example.com</c> is published below it too, as the collector
    /// already takes it to be, and plenty of them keep their writing on a name of its own.
    /// </summary>
    [Fact]
    public void A_Sitemap_On_An_Address_Below_The_Website_Is_Kept()
    {
        var site = NewSite(domain: "example.com");

        site.SetSitemapAddress("https://blog.example.com/sitemap_index.xml");

        site.SitemapAddress.Should().Be("https://blog.example.com/sitemap_index.xml");
    }

    /// <summary>
    /// The one thing somebody with a settings panel must not be able to do: point this engine at
    /// a website that is not the one they say it is, however the address is dressed up to look it.
    /// </summary>
    [Theory]
    [InlineData("https://example.org/sitemap.xml")]
    [InlineData("https://notexample.com/sitemap.xml")]
    [InlineData("https://example.com.evil.test/sitemap.xml")]
    [InlineData("https://evil.test/?u=example.com")]
    [InlineData("https://evil.test/example.com/sitemap.xml")]
    [InlineData("https://evil.test#example.com")]
    [InlineData("https://evil.test\\@example.com/sitemap.xml")]
    public void A_Sitemap_Elsewhere_Is_Refused(string elsewhere)
    {
        var site = NewSite(domain: "example.com");

        var act = () => site.SetSitemapAddress(elsewhere);

        act.Should().Throw<ArgumentException>().WithParameterName("address");
        site.SitemapAddress.Should().BeNull();
    }

    [Theory]
    [InlineData("ftp://example.com/sitemap.xml")]
    [InlineData("file:///etc/passwd")]
    [InlineData("file://example.com/sitemap.xml")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/xml,sitemap")]
    [InlineData("/sitemap.xml")]
    [InlineData("sitemap.xml")]
    [InlineData("example.com/sitemap.xml")]
    [InlineData("//example.com/sitemap.xml")]
    public void Only_A_Web_Address_Can_Be_A_Sitemap(string notAWebAddress)
    {
        var site = NewSite(domain: "example.com");

        var act = () => site.SetSitemapAddress(notAWebAddress);

        act.Should().Throw<ArgumentException>().WithParameterName("address");
    }

    /// <summary>
    /// A name and a password in an address would be sent to whatever answers it, and an
    /// <c>example.com@</c> in front of somebody else's host is how an address is made to read as
    /// one it is not.
    /// </summary>
    [Theory]
    [InlineData("https://user@example.com/sitemap.xml")]
    [InlineData("https://user:secret@example.com/sitemap.xml")]
    [InlineData("https://:@example.com/sitemap.xml")]
    [InlineData("https://example.com@evil.test/sitemap.xml")]
    public void An_Address_Carrying_Credentials_Is_Refused(string carrying)
    {
        var site = NewSite(domain: "example.com");

        var act = () => site.SetSitemapAddress(carrying);

        act.Should().Throw<ArgumentException>().WithParameterName("address");
    }

    /// <summary>
    /// Whatever else a website's server answers on is no part of the website, and a port is what
    /// would turn this reader into a way of asking that server about it.
    /// </summary>
    [Theory]
    [InlineData("https://example.com:8443/sitemap.xml")]
    [InlineData("https://example.com:80/sitemap.xml")]
    [InlineData("http://example.com:443/sitemap.xml")]
    public void An_Address_On_Another_Port_Is_Refused(string elsewhere)
    {
        var site = NewSite(domain: "example.com");

        var act = () => site.SetSitemapAddress(elsewhere);

        act.Should().Throw<ArgumentException>().WithParameterName("address");
    }

    [Theory]
    [InlineData("https://example.com:443/sitemap.xml", "https://example.com/sitemap.xml")]
    [InlineData("http://example.com:80/sitemap.xml", "http://example.com/sitemap.xml")]
    public void The_Default_Port_Written_Out_Is_Accepted(string written, string kept)
    {
        var site = NewSite(domain: "example.com");

        site.SetSitemapAddress(written);

        site.SitemapAddress.Should().Be(kept);
    }

    /// <summary>
    /// An address written as a number was never asked of a name server, in any of the forms it can
    /// be spelled in. The first three are one machine.
    /// </summary>
    [Theory]
    [InlineData("http://127.0.0.1/sitemap.xml")]
    [InlineData("http://2130706433/sitemap.xml")]
    [InlineData("http://0x7f.0.0.1/sitemap.xml")]
    [InlineData("http://[::1]/sitemap.xml")]
    [InlineData("http://[::ffff:127.0.0.1]/sitemap.xml")]
    public void An_Address_Written_As_A_Number_Is_Refused(string numbered)
    {
        var site = NewSite(domain: "example.com");

        var act = () => site.SetSitemapAddress(numbered);

        act.Should().Throw<ArgumentException>().WithParameterName("address");
    }

    /// <summary>
    /// Nothing checks what a website is registered under, so somebody can register one as a number
    /// and name a sitemap on it: the address every cloud machine keeps its own credentials behind,
    /// for one. This is what refuses that at the door, before anything would have to refuse it at the
    /// network.
    /// </summary>
    [Theory]
    [InlineData("127.0.0.1", "http://127.0.0.1/sitemap.xml")]
    [InlineData("0.0.1", "http://127.0.0.1/sitemap.xml")]
    [InlineData("10.0.0.5", "http://10.0.0.5/sitemap.xml")]
    [InlineData("169.254.169.254", "http://169.254.169.254/latest/meta-data/")]
    public void A_Website_Registered_As_A_Number_Publishes_Nothing(string domain, string numbered)
    {
        var site = NewSite(domain: domain);

        site.Publishes(new Uri(numbered)).Should().BeFalse();

        var act = () => site.SetSitemapAddress(numbered);

        act.Should().Throw<ArgumentException>().WithParameterName("address");
    }

    /// <summary>
    /// One spelling, so that the same sitemap typed twice is the same sitemap, and saving it again
    /// does not throw away everything read from it.
    /// </summary>
    [Theory]
    [InlineData("HTTPS://Example.COM/Sitemap.xml#top", "https://example.com/Sitemap.xml")]
    [InlineData("https://example.com./sitemap.xml", "https://example.com/sitemap.xml")]
    [InlineData("https://example.com", "https://example.com/")]
    [InlineData("  https://example.com/sitemap.xml\n", "https://example.com/sitemap.xml")]
    public void A_Sitemap_Address_Is_Kept_Without_Its_Fragment_And_With_Its_Host_Lower_Cased(
        string typed,
        string kept)
    {
        var site = NewSite(domain: "example.com");

        site.SetSitemapAddress(typed);

        site.SitemapAddress.Should().Be(kept);
    }

    /// <summary>
    /// Some publishers serve their sitemap from a query string, WordPress's plain links among them,
    /// and without it the address means something else.
    /// </summary>
    [Fact]
    public void A_Query_String_Is_Kept_On_A_Sitemap()
    {
        var site = NewSite(domain: "example.com");

        site.SetSitemapAddress("https://example.com/?sitemap=index");

        site.SitemapAddress.Should().Be("https://example.com/?sitemap=index");
    }

    /// <summary>
    /// What is kept is what a name server is asked for, whichever form somebody typed it in and
    /// whichever form the website was added under.
    /// </summary>
    [Theory]
    [InlineData("bücher.example", "https://bücher.example/sitemap.xml")]
    [InlineData("bücher.example", "https://xn--bcher-kva.example/sitemap.xml")]
    [InlineData("xn--bcher-kva.example", "https://bücher.example/sitemap.xml")]
    [InlineData("xn--bcher-kva.example", "https://BÜCHER.example/sitemap.xml")]
    public void An_International_Address_Is_Kept_In_Its_Ascii_Form(string domain, string typed)
    {
        var site = NewSite(domain: domain);

        site.SetSitemapAddress(typed);

        site.SitemapAddress.Should().Be("https://xn--bcher-kva.example/sitemap.xml");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_Empty_Address_Removes_The_Sitemap(string? empty)
    {
        var site = NewSite(domain: "example.com");
        site.SetSitemapAddress("https://example.com/sitemap.xml");

        site.SetSitemapAddress(empty);

        site.SitemapAddress.Should().BeNull();
    }

    /// <summary>
    /// The limit is the width the column is declared at, so an address the site accepts is one the
    /// database can store.
    /// </summary>
    [Fact]
    public void An_Address_As_Long_As_Accepted_Is_Kept()
    {
        var site = NewSite(domain: "example.com");
        var longest = AddressOfLength(Site.MaxSitemapAddressLength);

        site.SetSitemapAddress(longest);

        site.SitemapAddress.Should().Be(longest);
    }

    [Fact]
    public void An_Address_Longer_Than_Accepted_Is_Refused()
    {
        var site = NewSite(domain: "example.com");

        var act = () => site.SetSitemapAddress(AddressOfLength(Site.MaxSitemapAddressLength + 1));

        act.Should().Throw<ArgumentException>().WithParameterName("address");
    }

    /// <summary>
    /// Escaping makes an address longer than it was typed, and what has to fit is what is stored.
    /// </summary>
    [Fact]
    public void An_Address_That_Only_Overruns_Once_Escaped_Is_Refused()
    {
        var site = NewSite(domain: "example.com");
        var prefix = "https://example.com/";
        var typed = prefix + new string('é', Site.MaxSitemapAddressLength - prefix.Length);

        typed.Length.Should().Be(Site.MaxSitemapAddressLength);

        var act = () => site.SetSitemapAddress(typed);

        act.Should().Throw<ArgumentException>().WithParameterName("address");
    }

    /// <summary>
    /// Measured as typed as well as stored, so that a long enough paste is refused without being
    /// parsed at all, however little of it would have been kept.
    /// </summary>
    [Fact]
    public void An_Address_Too_Long_As_Typed_Is_Refused_However_Little_Of_It_Would_Be_Kept()
    {
        var site = NewSite(domain: "example.com");
        var typed = $"https://example.com/sitemap.xml#{new string('f', Site.MaxSitemapAddressLength)}";

        var act = () => site.SetSitemapAddress(typed);

        act.Should().Throw<ArgumentException>().WithParameterName("address");
    }

    /// <summary>
    /// An address pasted with space around it is the same address, and refusing it for a length it
    /// does not have would be a rule about the clipboard.
    /// </summary>
    [Fact]
    public void An_Address_Is_Measured_Without_The_Space_Around_It()
    {
        var site = NewSite(domain: "example.com");
        var longest = AddressOfLength(Site.MaxSitemapAddressLength);

        site.SetSitemapAddress($"   {longest}   ");

        site.SitemapAddress.Should().Be(longest);
    }

    /// <summary>
    /// What is kept is stored and later shown on a screen, so whatever somebody pasted into the middle
    /// of an address comes out escaped. PostgreSQL would refuse a raw nought outright, and the save
    /// would fail with nothing to tell them why.
    /// </summary>
    [Theory]
    [InlineData("https://example.com/a\u0000b", "https://example.com/a%00b")]
    [InlineData("https://example.com/a\r\nb", "https://example.com/a%0D%0Ab")]
    [InlineData("https://example.com/a‮b", "https://example.com/a%E2%80%AEb")]
    [InlineData("https://example.com/?a=\u0000", "https://example.com/?a=%00")]
    public void Nothing_Unprintable_Is_Kept_In_A_Sitemap_Address(string typed, string kept)
    {
        var site = NewSite(domain: "example.com");

        site.SetSitemapAddress(typed);

        site.SitemapAddress.Should().Be(kept);
    }

    [Fact]
    public void A_Refused_Address_Leaves_The_Sitemap_As_It_Was()
    {
        var site = NewSite(domain: "example.com");
        site.SetSitemapAddress("https://example.com/sitemap.xml");

        var act = () => site.SetSitemapAddress("https://example.org/sitemap.xml");

        act.Should().Throw<ArgumentException>();
        site.SitemapAddress.Should().Be("https://example.com/sitemap.xml");
    }

    /// <summary>
    /// Where a website names the hosts it reports from, those are where it is published, as they are
    /// for the collector, and its own domain stops counting unless it is one of them.
    /// </summary>
    [Fact]
    public void Allowed_Origins_Decide_Where_A_Sitemap_May_Be()
    {
        var site = NewSite(domain: "example.com");
        string[] origins = ["docs.example.net", "bücher.example"];
        site.ReplaceAllowedOrigins(origins);

        site.SetSitemapAddress("https://docs.example.net/sitemap.xml");
        site.SitemapAddress.Should().Be("https://docs.example.net/sitemap.xml");

        site.SetSitemapAddress("https://xn--bcher-kva.example/sitemap.xml");
        site.SitemapAddress.Should().Be("https://xn--bcher-kva.example/sitemap.xml");

        var ownDomain = () => site.SetSitemapAddress("https://example.com/sitemap.xml");

        ownDomain.Should().Throw<ArgumentException>().WithParameterName("address");
    }

    /// <summary>
    /// Nothing checks what a website is registered under, so one registered as <c>com</c> would
    /// otherwise be published at every address below it.
    /// </summary>
    [Theory]
    [InlineData("com", "https://com/sitemap.xml")]
    [InlineData("com", "https://example.com/sitemap.xml")]
    [InlineData("com", "https://www.example.com/sitemap.xml")]
    [InlineData("localhost", "http://localhost/sitemap.xml")]
    [InlineData("localhost", "http://blog.localhost/sitemap.xml")]
    public void A_Website_Registered_As_A_Bare_Suffix_Publishes_Nothing(string domain, string address)
    {
        var site = NewSite(domain: domain);

        site.Publishes(new Uri(address)).Should().BeFalse();
    }

    /// <summary>
    /// A permitted host that covers nothing must not count as there being none, because the domain
    /// only stands in where there are none, and that would widen the list by shortening it.
    /// </summary>
    [Fact]
    public void A_Bare_Suffix_Among_The_Permitted_Hosts_Covers_Nothing_And_Brings_Nothing_Back()
    {
        var site = NewSite(domain: "example.com");
        string[] origins = ["com"];
        site.ReplaceAllowedOrigins(origins);

        site.Publishes(new Uri("https://evil.com/sitemap.xml")).Should().BeFalse();
        site.Publishes(new Uri("https://example.com/sitemap.xml")).Should().BeFalse();
    }

    /// <summary>
    /// <see cref="Uri"/> parses these, and only says they have no ASCII form when it is asked for
    /// one. Whatever a stranger's sitemap lists, the answer is no rather than an exception that ends
    /// the reading of everything after it.
    /// </summary>
    [Theory]
    [InlineData("https://exam‍ple.com/sitemap.xml")]
    [InlineData("https://www.exam‌ple.com/sitemap.xml")]
    [InlineData("https://ünd‍.example.com/sitemap.xml")]
    public void A_Name_With_No_Ascii_Form_Is_Refused_And_Nothing_Is_Thrown(string unwritable)
    {
        var site = NewSite(domain: "example.com");

        site.Publishes(new Uri(unwritable)).Should().BeFalse();

        var act = () => site.SetSitemapAddress(unwritable);

        act.Should().Throw<ArgumentException>().WithParameterName("address");
    }

    /// <summary>
    /// A name server cannot be asked about a host this long, and <see cref="Uri"/> accepts one. It
    /// is refused here because the column that keeps the host of a sitemap is the width of a name,
    /// and one row that does not fit would fail the statement that starts every website's reading.
    /// </summary>
    [Fact]
    public void A_Host_Longer_Than_A_Name_Server_Can_Be_Asked_About_Is_Refused()
    {
        var site = NewSite(domain: "example.com");
        var label = new string('a', 63);
        var tooLong = new Uri($"https://{label}.{label}.{label}.{label}.example.com/sitemap.xml");
        var longest = new Uri($"https://{label}.{label}.{label}.{new string('a', 49)}.example.com/sitemap.xml");

        tooLong.IdnHost.Length.Should().BeGreaterThan(Site.MaxDomainLength);
        longest.IdnHost.Length.Should().Be(Site.MaxDomainLength);

        site.Publishes(tooLong).Should().BeFalse();
        site.Publishes(longest).Should().BeTrue();
    }

    /// <summary>
    /// The rule behind the setting is the rule every redirect and every listed address goes through,
    /// so what a sitemap cannot be is an address nothing is fetched from.
    /// </summary>
    [Theory]
    [InlineData("https://example.org/page")]
    [InlineData("https://example.com:8443/page")]
    [InlineData("https://user@example.com/page")]
    [InlineData("http://127.0.0.1/page")]
    [InlineData("ftp://example.com/page")]
    public void What_Cannot_Be_A_Sitemap_Is_Not_Published_Either(string address)
    {
        NewSite(domain: "example.com").Publishes(new Uri(address)).Should().BeFalse();
    }

    /// <summary>
    /// A host whose ASCII form is no name, because a character in it becomes one no name may hold or
    /// the dots of a number, is refused. Kept in that spelling it could not be read back, and every
    /// reading of it would stumble before asking for anything.
    /// </summary>
    /// <param name="domain">What the website is registered as.</param>
    /// <param name="typed">The address as it was typed.</param>
    [Theory]
    [InlineData("example.com", "https://foo\uFF1C.example.com/sitemap.xml")]
    [InlineData("example.com", "https://foo\u3000.example.com/sitemap.xml")]
    [InlineData("example.com", "https://\u200F.example.com/sitemap.xml")]
    [InlineData("127.0.0.1", "http://127\u30020\u30020\u30021/sitemap.xml")]
    public void A_Host_That_Is_No_Name_In_Ascii_Cannot_Be_A_Sitemap(string domain, string typed)
    {
        var site = NewSite(domain: domain);

        var act = () => site.SetSitemapAddress(typed);

        act.Should().Throw<ArgumentException>().WithParameterName("address");
        site.SitemapAddress.Should().BeNull();
    }

    [Theory]
    [InlineData("https://example.com/page")]
    [InlineData("http://example.com/page")]
    [InlineData("https://www.example.com/page")]
    [InlineData("https://blog.example.com/page?x=1#top")]
    [InlineData("https://example.com./page")]
    public void A_Page_On_The_Websites_Own_Hosts_Is_Published(string address)
    {
        NewSite(domain: "example.com").Publishes(new Uri(address)).Should().BeTrue();
    }

    [Fact]
    public void An_Address_That_Is_Not_Whole_Is_Not_Published()
    {
        NewSite(domain: "example.com").Publishes(new Uri("/sitemap.xml", UriKind.Relative)).Should().BeFalse();
    }

    private static string AddressOfLength(int length)
    {
        const string Start = "https://example.com/";

        return Start + new string('s', length - Start.Length);
    }

    private static Site NewSite(string domain = "example.com", string timeZoneId = "Etc/UTC") =>
        new(Guid.NewGuid(), Guid.NewGuid(), domain, timeZoneId, CreatedAt);
}
