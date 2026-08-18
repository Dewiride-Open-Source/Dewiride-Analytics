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
