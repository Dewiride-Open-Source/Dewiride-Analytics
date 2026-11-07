using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using Dewiride.Analytics.Domain.Sites;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// One file of a sitemap, read a little at a time.
/// </summary>
/// <remarks>
/// <para>
/// The document type is prohibited and there is no resolver, so neither an entity that expands to
/// gigabytes nor one that names a file on this machine goes anywhere. A document that declares one is
/// refused before anything in it is read.
/// </para>
/// <para>
/// A malformed document ends in an <see cref="XmlException"/> partway through, after the entries
/// before the fault have been handed over, and that exception quotes the document: it is not for a log.
/// </para>
/// <para>
/// A file's own limit is fifty thousand entries, and a value is read in small pieces, so that the
/// memory this takes has nothing to do with how long a sitemap makes either of them. How long the file
/// itself may be is for the stream to say. The XML reader has a setting for that as well, and asked
/// for its data this way it checks that setting against the first thing it reads and never again.
/// </para>
/// <para>
/// A tag and a marked-out section are another matter, because the XML reader takes in the whole of one
/// before it hands anything over, and nothing here can ask it not to. <see cref="MarkupGuardStream"/> sits
/// under it and refuses a file holding one too long to be anything a sitemap has a use for.
/// </para>
/// <para>
/// What the XML reader keeps for a file does not go with how long the file is but with what is in it,
/// and it holds on to something for every open element, every attribute and every name it has seen.
/// A file within its limit, made of nothing but elements left open, took it to three gibibytes. A
/// sitemap is three or four levels deep and uses a few dozen names, so a file doing anything else
/// is refused as not being one, a long way short of that.
/// </para>
/// </remarks>
internal sealed class SitemapDocument : IDisposable
{
    /// <summary>The most entries the protocol allows in one file, and the most that are read.</summary>
    public const int MostEntries = 50_000;

    /// <summary>How much space may surround an address in the file, for a sitemap that indents its values.</summary>
    private const int MostSpaceAroundAValue = 2048;

    /// <summary>The most held of one value: longer than this and it is on its way to being skipped.</summary>
    private const int MostHeld = Site.MaxSitemapAddressLength + MostSpaceAroundAValue;

    /// <summary>How deeply a sitemap may nest, with room to spare for what publishers add to an entry.</summary>
    private const int MostDepth = 32;

    private const string PagesRoot = "urlset";
    private const string PartsRoot = "sitemapindex";
    private const string PageEntry = "url";
    private const string PartEntry = "sitemap";
    private const string LocationValue = "loc";
    private const string ModifiedValue = "lastmod";

    /// <summary>The two coarsest forms a W3C date can take, which say nothing more than when it changed.</summary>
    private static readonly string[] CoarseDates = ["yyyy", "yyyy-MM"];

    private readonly XmlReader _reader;
    private readonly char[] _piece = new char[1024];
    private readonly StringBuilder _value = new();
    private int _entries;

    /// <summary>
    /// Makes the charsets a sitemap can name available to the XML reader, whatever has or has not been
    /// read before this.
    /// </summary>
    static SitemapDocument() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private SitemapDocument(XmlReader reader, SitemapRootKind? kind)
    {
        _reader = reader;
        Kind = kind;
    }

    /// <summary>What the file is, or nothing where it is not a sitemap at all.</summary>
    public SitemapRootKind? Kind { get; }

    /// <summary>
    /// Whether the file listed more than is read, which is known once the entries have run out.
    /// </summary>
    public bool ListsMoreThanIsRead { get; private set; }

    /// <summary>
    /// Begins reading a file.
    /// </summary>
    /// <remarks>
    /// Whether it is a sitemap comes from its first element and nothing else. The namespace is left
    /// out of that because plenty of generators leave it out too. A feed, a web page and something
    /// that was never XML all come to the same thing.
    /// </remarks>
    /// <param name="body">The file, unpacked, which stays open.</param>
    /// <returns>The document, which the caller disposes.</returns>
    public static async Task<SitemapDocument> OpenAsync(Stream body)
    {
        var reader = XmlReader.Create(new MarkupGuardStream(body), new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 1024,
            NameTable = new FewNames(),
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            CloseInput = false,
        });

        try
        {
            await reader.MoveToContentAsync().ConfigureAwait(false);

            return new SitemapDocument(reader, KindOf(reader));
        }
        catch (XmlException)
        {
            return new SitemapDocument(reader, null);
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Reads the entries of the file.
    /// </summary>
    /// <remarks>
    /// An entry with no address, or one too long to be an address, is skipped. Whatever else an entry
    /// holds is skipped too: a picture's <c>loc</c> or a video's date inside a page's entry is in another
    /// namespace, and further down.
    /// </remarks>
    /// <param name="cancellationToken">Stops between entries. Nothing can stop a read in hand but the stream.</param>
    /// <returns>The entries, in the order they are listed.</returns>
    public async IAsyncEnumerable<SitemapEntry> EntriesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (Kind is null || _reader.IsEmptyElement)
        {
            yield break;
        }

        var entryName = Kind == SitemapRootKind.Index ? PartEntry : PageEntry;
        var rootNamespace = _reader.NamespaceURI;

        await _reader.ReadAsync().ConfigureAwait(false);

        while (_reader.NodeType != XmlNodeType.EndElement && !_reader.EOF)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsOnElement(entryName, rootNamespace))
            {
                await PassOverAsync().ConfigureAwait(false);
                continue;
            }

            if (_entries == MostEntries)
            {
                ListsMoreThanIsRead = true;
                yield break;
            }

            _entries++;

            if (await ReadEntryAsync(rootNamespace).ConfigureAwait(false) is { } entry)
            {
                yield return entry;
            }

            // Moving past an entry is what finds out the file breaks after it, so it comes after the
            // entry has been handed over, or a file cut off at any point would lose the last it listed.
            await _reader.ReadAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _reader.Dispose();

    private static SitemapRootKind? KindOf(XmlReader reader)
    {
        if (reader.NodeType != XmlNodeType.Element)
        {
            return null;
        }

        return reader.LocalName switch
        {
            PagesRoot => SitemapRootKind.UrlSet,
            PartsRoot => SitemapRootKind.Index,
            _ => null,
        };
    }

    private bool IsOnElement(string name, string @namespace) =>
        _reader.NodeType == XmlNodeType.Element && _reader.LocalName == name && _reader.NamespaceURI == @namespace;

    /// <summary>
    /// Reads one entry, and stops on the last of its tags without moving past it.
    /// </summary>
    /// <param name="rootNamespace">The namespace the entry's own values are in.</param>
    /// <returns>The entry, or nothing where it has no address that could be one.</returns>
    private async Task<SitemapEntry?> ReadEntryAsync(string rootNamespace)
    {
        string? location = null;
        DateTimeOffset? modified = null;

        if (_reader.IsEmptyElement)
        {
            return null;
        }

        await _reader.ReadAsync().ConfigureAwait(false);

        while (_reader.NodeType != XmlNodeType.EndElement && !_reader.EOF)
        {
            var name = _reader.NodeType == XmlNodeType.Element && _reader.NamespaceURI == rootNamespace
                ? _reader.LocalName
                : null;

            switch (name)
            {
                case LocationValue:
                    location = await ReadValueAsync().ConfigureAwait(false);
                    break;

                case ModifiedValue:
                    modified = ReadModified(await ReadValueAsync().ConfigureAwait(false));
                    break;

                default:
                    await PassOverAsync().ConfigureAwait(false);
                    break;
            }
        }

        return string.IsNullOrEmpty(location) ? null : new SitemapEntry(location, modified);
    }

    /// <summary>
    /// Reads the text of an element, a piece at a time, keeping none of it once it has gone on longer
    /// than an address can.
    /// </summary>
    /// <returns>The text, or nothing where it was too long to be what it says it is.</returns>
    private async Task<string?> ReadValueAsync()
    {
        if (_reader.IsEmptyElement)
        {
            await _reader.ReadAsync().ConfigureAwait(false);

            return null;
        }

        _value.Clear();
        await _reader.ReadAsync().ConfigureAwait(false);

        var tooLong = false;

        while (_reader.NodeType != XmlNodeType.EndElement && !_reader.EOF)
        {
            if (_reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA)
            {
                tooLong |= !await TakeTextAsync().ConfigureAwait(false);
            }

            await PassOverAsync().ConfigureAwait(false);
        }

        await _reader.ReadAsync().ConfigureAwait(false);

        var value = _value.ToString().Trim();

        return tooLong || value.Length > Site.MaxSitemapAddressLength ? null : value;
    }

    /// <summary>
    /// Takes in one stretch of text.
    /// </summary>
    /// <returns>Whether all of it fitted.</returns>
    private async Task<bool> TakeTextAsync()
    {
        var fitted = true;

        for (
            var read = await _reader.ReadValueChunkAsync(_piece, 0, _piece.Length).ConfigureAwait(false);
            read > 0;
            read = await _reader.ReadValueChunkAsync(_piece, 0, _piece.Length).ConfigureAwait(false))
        {
            if (_value.Length + read > MostHeld)
            {
                fitted = false;
            }
            else
            {
                _value.Append(_piece, 0, read);
            }
        }

        return fitted;
    }

    /// <summary>
    /// Moves past whatever the reader is on, and everything inside it, watching how deep that goes.
    /// </summary>
    private async Task PassOverAsync()
    {
        if (_reader.NodeType != XmlNodeType.Element || _reader.IsEmptyElement)
        {
            await _reader.ReadAsync().ConfigureAwait(false);

            return;
        }

        var depth = _reader.Depth;

        do
        {
            await _reader.ReadAsync().ConfigureAwait(false);

            if (_reader.Depth > MostDepth)
            {
                throw new XmlException("This nests more deeply than any sitemap does.");
            }
        }
        while (_reader.Depth > depth && !_reader.EOF);

        await _reader.ReadAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Reads when a sitemap says something changed. One that can not be read is the same as one left
    /// out, which is allowed.
    /// </summary>
    private static DateTimeOffset? ReadModified(string? value)
    {
        if (PublishedDate.TryParse(value, TimeZoneInfo.Utc, out var modified))
        {
            return modified;
        }

        return DateTimeOffset.TryParseExact(
                value,
                CoarseDates,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out modified)
            ? modified
            : null;
    }

    /// <summary>
    /// Every name a file uses, of which there may only be so many, none of them long.
    /// </summary>
    /// <remarks>
    /// The XML reader keeps each name it sees for the life of the document, and turns every element's
    /// attributes into objects of their own. No element can repeat an attribute, so a limit on names is
    /// also a limit on how many attributes one element can bring with it.
    /// </remarks>
    private sealed class FewNames : XmlNameTable
    {
        private const int MostNames = 1024;
        private const int LongestName = 1024;

        private readonly NameTable _names = new();
        private int _count;

        /// <inheritdoc />
        public override string Add(char[] array, int offset, int length) =>
            _names.Get(array, offset, length) ?? AddNew(length, () => _names.Add(array, offset, length));

        /// <inheritdoc />
        public override string Add(string array) => _names.Get(array) ?? AddNew(array.Length, () => _names.Add(array));

        /// <inheritdoc />
        public override string? Get(char[] array, int offset, int length) => _names.Get(array, offset, length);

        /// <inheritdoc />
        public override string? Get(string array) => _names.Get(array);

        private string AddNew(int length, Func<string> add)
        {
            if (++_count > MostNames || length > LongestName)
            {
                throw new XmlException("This uses more names, or longer ones, than any sitemap does.");
            }

            return add();
        }
    }
}

/// <summary>
/// One entry of a sitemap.
/// </summary>
/// <param name="Location">Its address, unescaped, and not checked in any way.</param>
/// <param name="LastModified">When the sitemap says it last changed, where it says.</param>
internal sealed record SitemapEntry(string Location, DateTimeOffset? LastModified);
