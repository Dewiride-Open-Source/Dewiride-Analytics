using System.Net.Http.Headers;
using System.Text;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// What a website said about an answer it gave, for asking it next time whether anything has changed.
/// </summary>
/// <remarks>
/// A real entity tag is a hash or a version. One longer than is kept is left out, which costs no more
/// than reading the answer in full next time, and one the header parser could not read is the same. So is
/// one holding anything past ASCII, which a tag may and a request may not: the handler refuses to write it
/// and nothing leaves the machine, so that asking with it would read as the website not answering, every
/// time, for as long as nothing else could replace it.
/// </remarks>
/// <param name="ETag">Its entity tag, as it was sent.</param>
/// <param name="LastModified">When it says it last changed.</param>
internal readonly record struct WebsiteValidators(string? ETag, DateTimeOffset? LastModified)
{
    /// <summary>Whether these ask anything, which they do not when nothing has been kept to ask with.</summary>
    public bool AskIfChanged => ETag is not null || LastModified is not null;

    /// <summary>
    /// Reads what a response said.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <returns>What to ask with next time.</returns>
    public static WebsiteValidators From(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var tag = response.Headers.ETag?.ToString();

        return new WebsiteValidators(
            tag?.Length <= SitemapRead.MaxEtagLength && Ascii.IsValid(tag) ? tag : null,
            response.Content.Headers.LastModified);
    }

    /// <summary>
    /// Asks a request to be answered only if something has changed.
    /// </summary>
    /// <param name="request">The request.</param>
    public void ApplyTo(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (EntityTagHeaderValue.TryParse(ETag, out var tag))
        {
            request.Headers.IfNoneMatch.Add(tag);
        }

        request.Headers.IfModifiedSince = LastModified;
    }
}
