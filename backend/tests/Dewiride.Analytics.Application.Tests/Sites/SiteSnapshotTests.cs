using System.Collections.Immutable;
using System.Text.Json;
using Dewiride.Analytics.Application.Sites;

namespace Dewiride.Analytics.Application.Tests.Sites;

/// <summary>
/// Covers the read model the collector resolves a site into.
/// </summary>
/// <remarks>
/// The collector resolves a site on every report, so the answer is cached, and a cache is free to
/// keep what it was given by writing it out and reading it back. Anything that does not survive
/// that round trip comes back at its default without an error anywhere — and the settings that
/// would be lost are the ones deciding whether query strings are kept and which origins may
/// report for the site. This test is the guard on that.
/// </remarks>
public sealed class SiteSnapshotTests
{
    [Fact]
    public void Survives_Being_Written_Out_And_Read_Back()
    {
        var snapshot = new SiteSnapshot
        {
            Id = Guid.Parse("0197c0de-0000-7000-8000-000000000001"),
            Domain = "example.com",
            RetainQueryStrings = true,
            CaptureClicks = true,
            AllowedOrigins = ["docs.example.com", "cdn.example.com"],
        };

        var restored = JsonSerializer.Deserialize<SiteSnapshot>(JsonSerializer.Serialize(snapshot));

        restored.Should().NotBeNull();
        restored.Id.Should().Be(snapshot.Id);
        restored.Domain.Should().Be("example.com");
        restored.RetainQueryStrings.Should().BeTrue();
        restored.AllowedOrigins.Should().Equal("docs.example.com", "cdn.example.com");
    }

    [Fact]
    public void Survives_The_Round_Trip_With_No_Declared_Origins()
    {
        var snapshot = new SiteSnapshot
        {
            Id = Guid.Parse("0197c0de-0000-7000-8000-000000000002"),
            Domain = "example.com",
            RetainQueryStrings = false,
            CaptureClicks = false,
            AllowedOrigins = [],
        };

        var restored = JsonSerializer.Deserialize<SiteSnapshot>(JsonSerializer.Serialize(snapshot));

        restored.Should().NotBeNull();
        restored.AllowedOrigins.Should().BeEmpty();
        restored.AllowedOrigins.IsDefault.Should().BeFalse();
    }

    /// <summary>
    /// Every member is required, so a payload missing one is refused rather than filled in with a
    /// default that would read as a real setting.
    /// </summary>
    [Fact]
    public void Refuses_A_Payload_That_Omits_A_Setting()
    {
        const string incomplete = """{"Id":"0197c0de-0000-7000-8000-000000000003","Domain":"example.com"}""";

        var act = () => JsonSerializer.Deserialize<SiteSnapshot>(incomplete);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Two_Snapshots_Of_The_Same_Site_Describe_The_Same_Settings()
    {
        ImmutableArray<string> origins = ["docs.example.com"];
        var id = Guid.Parse("0197c0de-0000-7000-8000-000000000004");

        var first = new SiteSnapshot
        {
            Id = id,
            Domain = "example.com",
            RetainQueryStrings = true,
            CaptureClicks = true,
            AllowedOrigins = origins,
        };

        var second = first with { };

        second.Should().Be(first);
    }
}
