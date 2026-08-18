using Dewiride.Analytics.Domain.Sites;

namespace Dewiride.Analytics.Domain.Tests.Sites;

/// <summary>
/// Proves what a server key remembers and what it refuses to become.
/// </summary>
public sealed class SiteIngestKeyTests
{
    private static readonly DateTimeOffset Created = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_New_Key_Works_And_Has_Never_Been_Used()
    {
        var key = Issue();

        key.IsRevoked.Should().BeFalse();
        key.RevokedAt.Should().BeNull();
        key.LastUsedAt.Should().BeNull();
        key.CreatedAt.Should().Be(Created);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_Key_Without_A_Name_Is_Refused(string name)
    {
        var issuing = () => new SiteIngestKey(Guid.NewGuid(), Guid.NewGuid(), name, "hash", "abcd", Created);

        issuing.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// The hash and the preview are what the key is looked up and recognised by. A key carrying
    /// neither authorises nothing and can never be told apart from any other, so it is not a key.
    /// </summary>
    [Theory]
    [InlineData("", "abcd")]
    [InlineData("hash", "")]
    public void A_Key_Without_A_Stored_Form_Is_Refused(string hash, string preview)
    {
        var issuing = () => new SiteIngestKey(Guid.NewGuid(), Guid.NewGuid(), "Cloudflare", hash, preview, Created);

        issuing.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_Long_Name_Is_Shortened_Rather_Than_Refused()
    {
        var key = Issue(name: new string('n', SiteIngestKey.MaxNameLength + 40));

        key.Name.Should().HaveLength(SiteIngestKey.MaxNameLength);
    }

    [Fact]
    public void Surrounding_Space_Is_Not_Part_Of_The_Name()
    {
        Issue(name: "  Cloudflare  ").Name.Should().Be("Cloudflare");
    }

    [Fact]
    public void Withdrawing_A_Key_Stops_It_Working()
    {
        var key = Issue();
        var withdrawn = Created.AddDays(30);

        key.Revoke(withdrawn);

        key.IsRevoked.Should().BeTrue();
        key.RevokedAt.Should().Be(withdrawn);
    }

    /// <summary>
    /// Two people pressing the same button, or one request retried, must not move the moment a
    /// key stopped working — that time is the answer to "was this key live when that traffic
    /// arrived", and it can only be answered once.
    /// </summary>
    [Fact]
    public void Withdrawing_A_Key_Twice_Keeps_The_First_Time()
    {
        var key = Issue();
        var first = Created.AddDays(30);

        key.Revoke(first);
        key.Revoke(first.AddDays(5));

        key.RevokedAt.Should().Be(first);
    }

    [Fact]
    public void Using_A_Key_Records_When_It_Was_Last_Used()
    {
        var key = Issue();
        var seen = Created.AddHours(2);

        key.RecordUse(seen);
        key.RecordUse(seen.AddHours(1));

        key.LastUsedAt.Should().Be(seen.AddHours(1));
    }

    private static SiteIngestKey Issue(string name = "Cloudflare") =>
        new(Guid.NewGuid(), Guid.NewGuid(), name, new string('a', 64), "wxyz", Created);
}
