using System.Text;
using System.Xml;
using Dewiride.Analytics.Infrastructure.Posts;
using Dewiride.Analytics.Integration.Tests.Fixtures;

namespace Dewiride.Analytics.Integration.Tests.Posts;

/// <summary>
/// Covers what a file of a sitemap can make the reader spend. A deadline holds whatever a file does, and a
/// website serving five hundred of them can have each finish just inside it, so what one costs while inside
/// it matters as much as whether it ends.
/// </summary>
/// <remarks>
/// A file costs what it takes to go through it, however it is written. These bounds are many times what any of
/// them takes, even with the compiler warming up on a loaded machine, and well short of what space in
/// a tag comes to when nothing refuses it, which goes with the square of how much of it there is.
/// </remarks>
[Collection(AloneOnTheProcessorDefinition.Name)]
public sealed class SitemapCostTests
{
    private const int OneMebibyte = 1024 * 1024;
    private const int LargestFile = 50 * OneMebibyte;

    private static readonly TimeSpan MostProcessor = TimeSpan.FromSeconds(10);

    private static readonly Dictionary<string, Func<string>> Shapes = new()
    {
        ["space in a start tag"] = () => "<urlset><url" + new string(' ', 24 * OneMebibyte) + "/></urlset>",
        ["space in a start tag, as long as a file gets"] = () => "<urlset><url" + new string(' ', LargestFile) + "/></urlset>",
        ["space in an end tag"] = () => "<urlset><url></url" + new string(' ', LargestFile) + "></urlset>",
        ["space in the first tag"] = () => "<urlset" + new string(' ', LargestFile) + "></urlset>",
        ["tags full of space, each as long as is let through"] = () =>
            Repeated("<url" + new string(' ', MarkupGuardStream.LongestStretch - 16) + "/>", LargestFile / MarkupGuardStream.LongestStretch),
        ["character references"] = () => "<urlset><url><loc>" + Repeated("&#32;", LargestFile / 5) + "</loc></url></urlset>",
        ["sections full of openings, each as long as is let through"] = () =>
            Repeated("<a><![CDATA[" + new string('<', MarkupGuardStream.LongestStretch - 16) + "]]></a>", LargestFile / MarkupGuardStream.LongestStretch),
    };

    /// <summary>The name of each shape.</summary>
    public static TheoryData<string> ShapeNames => [.. Shapes.Keys];

    /// <summary>
    /// The whole of going through a file built to cost as much as it can, in a moment of a processor. Those
    /// nothing can be said for are refused, and those that fit what is let through are read.
    /// </summary>
    /// <param name="shape">The name of the shape.</param>
    [Theory]
    [MemberData(nameof(ShapeNames))]
    public async Task A_File_Made_To_Be_Expensive_Costs_What_It_Takes_To_Go_Through_It(string shape)
    {
        var file = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?>" + Shapes[shape]());
        var before = Environment.CpuUsage.TotalTime;

        await GoThroughAsync(file);

        (Environment.CpuUsage.TotalTime - before).Should().BeLessThan(MostProcessor);
    }

    private static string Repeated(string each, int times) =>
        new StringBuilder(each.Length * times).Insert(0, each, times).ToString();

    private static async Task GoThroughAsync(byte[] file)
    {
        try
        {
            using var document = await SitemapDocument.OpenAsync(new MemoryStream(file));

            await foreach (var _ in document.EntriesAsync(Cancellation.Token))
            {
                // Reading on to the end of the file, whatever it lists, is what is being measured.
            }
        }
        catch (XmlException)
        {
            // Refused, which is one of the two ways of being cheap.
        }
    }
}
