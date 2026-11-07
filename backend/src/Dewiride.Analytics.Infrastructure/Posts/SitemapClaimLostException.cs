namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// A write from a pass that no longer holds its claim, which changed nothing.
/// </summary>
/// <remarks>
/// Not a failure of any kind. The address was changed or cleared, the website was removed, or the claim
/// ran out and somebody else may have taken it, and in every one of those the only thing left for the
/// pass to do is stop. It is an exception so that it can stop from anywhere, and it says nothing else
/// because there is nothing else to say.
/// </remarks>
internal sealed class SitemapClaimLostException()
    : InvalidOperationException("The claim on this website's sitemap is no longer held.");
