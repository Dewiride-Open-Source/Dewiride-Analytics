using System.IO.Compression;

namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// Bodies compressed the way a website's server compresses them, and the way one trying something on
/// does.
/// </summary>
internal static class Gzipped
{
    /// <summary>
    /// One section of a compressed body with nothing in it, by the format's own description. The
    /// framework writes no section at all for nothing, and a website can write as many of these as it
    /// likes: each one is twenty bytes in and nothing out.
    /// </summary>
    private static readonly byte[] NothingAtAll =
    [
        0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF,
        0x03, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
    ];

    /// <summary>Compresses some bytes.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>Them, compressed.</returns>
    public static byte[] Bytes(byte[] bytes)
    {
        using var output = new MemoryStream();

        using (var compressor = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            compressor.Write(bytes);
        }

        return output.ToArray();
    }

    /// <summary>Makes a body out of sections with nothing in them.</summary>
    /// <param name="length">At least how long to make it.</param>
    /// <returns>The body, which unpacks to nothing at all.</returns>
    public static byte[] EmptySections(int length)
    {
        var sections = length / NothingAtAll.Length + 1;
        var body = new byte[sections * NothingAtAll.Length];

        for (var section = 0; section < sections; section++)
        {
            NothingAtAll.CopyTo(body, section * NothingAtAll.Length);
        }

        return body;
    }
}
