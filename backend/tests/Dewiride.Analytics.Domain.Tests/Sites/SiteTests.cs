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
    /// On, so that what somebody sees on the dashboard is what the product does out of the box.
    /// What is kept of a press is the site's own name for its own control, and there is a switch
    /// for anybody who would rather it were not kept at all.
    /// </summary>
    [Fact]
    public void Constructor_Leaves_Click_Capture_On()
    {
        var site = NewSite();

        site.CaptureClicks.Should().BeTrue();
    }

    [Fact]
    public void SetClickCapture_Turns_Capture_Off()
    {
        var site = NewSite();

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

    private static Site NewSite(string domain = "example.com", string timeZoneId = "Etc/UTC") =>
        new(Guid.NewGuid(), Guid.NewGuid(), domain, timeZoneId, CreatedAt);
}
