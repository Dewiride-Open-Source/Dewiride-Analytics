using Dewiride.Analytics.Application.Posts;

namespace Dewiride.Analytics.Application.Tests.Posts;

/// <summary>
/// Covers the name a kept month of posts gives the list of posts it was counted for.
/// </summary>
/// <remarks>
/// The name is kept with every month, so it has to say the same of the same list however that list is
/// read out, and something else of any other — otherwise a month is counted again on every run, or never
/// again after a post is found.
/// </remarks>
public sealed class PostFingerprintTests
{
    [Fact]
    public void The_Same_Paths_In_Any_Order_Or_Repeated_Have_One_Fingerprint()
    {
        var listed = PostFingerprint.Of(["/a/", "/b/"]);

        PostFingerprint.Of(["/b/", "/a/"]).Should().Be(listed);
        PostFingerprint.Of(["/b/", "/a/", "/b/"]).Should().Be(listed);
    }

    [Theory]
    [InlineData("/a/", "/b/", "/c/")]
    [InlineData("/a/")]
    [InlineData("/a/", "/B/")]
    public void Another_List_Has_Another_Fingerprint(params string[] paths)
    {
        PostFingerprint.Of([.. paths]).Should().NotBe(PostFingerprint.Of(["/a/", "/b/"]));
    }

    /// <summary>
    /// The first sixteen bytes of the SHA-256 of the paths in ordinal order, one to a line, in lower-case
    /// hexadecimal: written here as worked out by another program, since a change to it counts every
    /// month of posts still held again.
    /// </summary>
    [Fact]
    public void A_Fingerprint_Is_Half_A_Sha_256_Of_The_Paths_One_To_A_Line()
    {
        PostFingerprint.Of(["/b/", "/a/"]).Should().Be("dab5eac0ace9303d24ea2ee58888df6a");
        PostFingerprint.Of([]).Should().Be("e3b0c44298fc1c149afbf4c8996fb924");
    }
}
