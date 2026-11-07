using System.Xml;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// Refuses a file holding a stretch no sitemap has any use for, before the XML reader has taken it in.
/// </summary>
/// <remarks>
/// <para>
/// The XML reader holds the whole of a tag, and the whole of a marked-out section, before it hands anything
/// over, and each time it has to ask for more of one it goes back over what it already has. So one tag costs
/// memory that doubles as it grows, and time that goes with the square of its length. Nothing stops a tag full
/// of nothing but space inside a file's own limits: it compresses to a thousandth of its size and reads as a
/// perfectly good sitemap, and a website can serve five hundred of them to a reading.
/// </para>
/// <para>
/// Nothing in a tag may be a <c>&lt;</c>, so a stretch with no such byte in it is as long as the longest tag
/// could be, and no text between tags a sitemap has any use for comes near that either. A marked-out section can
/// hold as many of them as it likes, so its stretch runs from where it opens to where it closes.
/// </para>
/// <para>
/// This looks at bytes, under whatever charset the file names, which means looking past the zero bytes of the
/// charsets that spread each character out. A charset that writes <c>&lt;</c> as some other byte altogether, as
/// EBCDIC does, is refused at the same length, and nobody has a sitemap in one of those. The opening of a section
/// written inside a comment or a processing instruction is taken for the real thing, which only matters to
/// a file that then goes on for the whole of this length with no closing of one, and has no reason to.
/// </para>
/// <para>
/// What comes of it is what comes of any other file that is not a sitemap, and what the XML reader says about
/// one: an <see cref="XmlException"/>.
/// </para>
/// </remarks>
/// <param name="body">The file, which stays open.</param>
internal sealed class MarkupGuardStream(Stream body) : ForwardOnlyStream
{
    /// <summary>The longest stretch let through, far past the longest address and whatever a publisher adds beside it.</summary>
    public const int LongestStretch = 1024 * 1024;

    private const byte Opening = (byte)'<';
    private const byte SectionEndsWith = (byte)'>';
    private const byte SectionClosing = (byte)']';
    private const int ClosingsBeforeTheEnd = 2;

    private static ReadOnlySpan<byte> SectionOpening => "<![CDATA["u8;

    private int _stretch;
    private int _openingMatched;
    private int _closingsInARow;
    private bool _inASection;

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        var read = body.Read(buffer);

        Watch(buffer[..read]);

        return read;
    }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        var read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

        Watch(buffer.Span[..read]);

        return read;
    }

    private void Watch(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            _stretch++;

            if (value != 0)
            {
                Take(value);
            }

            if (_stretch > LongestStretch)
            {
                throw new XmlException("This goes on for longer than anything in a sitemap does.");
            }
        }
    }

    private void Take(byte value)
    {
        if (_inASection)
        {
            TakeInASection(value);
        }
        else
        {
            TakeOutsideASection(value);
        }
    }

    private void TakeInASection(byte value)
    {
        if (value == SectionEndsWith && _closingsInARow >= ClosingsBeforeTheEnd)
        {
            _inASection = false;
            _stretch = 0;
        }

        _closingsInARow = value == SectionClosing ? _closingsInARow + 1 : 0;
    }

    private void TakeOutsideASection(byte value)
    {
        if (value == Opening)
        {
            _stretch = 0;
            _openingMatched = 1;

            return;
        }

        _openingMatched = _openingMatched > 0 && value == SectionOpening[_openingMatched] ? _openingMatched + 1 : 0;

        if (_openingMatched == SectionOpening.Length)
        {
            _inASection = true;
            _openingMatched = 0;
            _closingsInARow = 0;
            _stretch = 0;
        }
    }
}
