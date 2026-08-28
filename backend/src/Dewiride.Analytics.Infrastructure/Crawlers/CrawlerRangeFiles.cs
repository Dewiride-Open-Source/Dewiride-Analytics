using System.Collections.Immutable;
using System.Text;
using Dewiride.Analytics.Classification.Identity;

namespace Dewiride.Analytics.Infrastructure.Crawlers;

/// <summary>One published file of crawler addresses, and what a local copy of it is called.</summary>
/// <param name="Operator">The company that publishes it.</param>
/// <param name="Address">Where it publishes it.</param>
/// <param name="Name">What the copy on disk is called.</param>
internal readonly record struct CrawlerRangeFile(string Operator, string Address, string Name);

/// <summary>
/// The files this installation keeps a copy of, taken from the crawler catalogue.
/// </summary>
/// <remarks>
/// <para>
/// Derived rather than listed a second time. Which companies publish a way of checking their
/// crawlers is part of what the engine reasons with, and a copy of that list here would be the
/// thing that goes stale — an address added to the catalogue and forgotten here would be a company
/// the product claims to be able to recognise and never fetches anything for.
/// </para>
/// <para>
/// A local copy is named after the company and the file, so somebody looking in the directory can
/// see what is there and an install with no way out to the internet can be given the files by hand.
/// </para>
/// </remarks>
internal static class CrawlerRangeFiles
{
    /// <summary>Every file this installation keeps a copy of.</summary>
    public static ImmutableArray<CrawlerRangeFile> All { get; } =
    [
        .. CrawlerCatalogue.Proofs
            .SelectMany(
                proof => proof.PublishedRanges,
                (proof, address) => new CrawlerRangeFile(proof.Operator, address, NameOf(proof.Operator, address))),
    ];

    /// <summary>
    /// What the local copy of one file is called.
    /// </summary>
    /// <param name="operatorName">The company that publishes it.</param>
    /// <param name="address">Where it publishes it.</param>
    /// <returns>A file name carrying both, with nothing in it that could name a directory.</returns>
    private static string NameOf(string operatorName, string address)
    {
        var published = new Uri(address, UriKind.Absolute);

        return Safe(operatorName) + "-" + Safe(published.Segments[^1]);
    }

    /// <summary>
    /// Reduces a name to the characters a file name may safely carry.
    /// </summary>
    /// <remarks>
    /// Letters, digits and full stops survive; everything else becomes a hyphen, and runs of
    /// hyphens collapse. Both inputs are constants from the catalogue rather than anything a
    /// visitor sends, so this is not a defence — it is what turns "Common Crawl" into something
    /// that reads well in a directory listing on every operating system.
    /// </remarks>
    private static string Safe(string value)
    {
        var built = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (char.IsAsciiLetterOrDigit(character) || character == '.')
            {
                built.Append(char.ToLowerInvariant(character));
            }
            else if (built.Length > 0 && built[^1] != '-')
            {
                built.Append('-');
            }
        }

        return built.ToString().Trim('-');
    }
}
