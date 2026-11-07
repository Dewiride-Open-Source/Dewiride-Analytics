using System.Xml;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// What a failure asking a website for something comes to.
/// </summary>
/// <remarks>
/// <para>
/// One list, because a website can fail in any of these ways at any point before the last byte, and
/// the same failure has to come to the same answer whether it came while connecting or halfway through
/// the body. An answer is written where somebody can read it, and a failure that has an answer is
/// never logged: the messages of several of these quote whatever the website sent.
/// </para>
/// <para>
/// Anything not on the list is a fault here and not at the website, and has no answer.
/// </para>
/// </remarks>
internal static class WebsiteFailure
{
    /// <summary>
    /// What a failure comes to.
    /// </summary>
    /// <param name="failure">What was thrown.</param>
    /// <param name="kind">What was being asked for.</param>
    /// <param name="deadline">The time the asking had.</param>
    /// <returns>
    /// What to say about it, or nothing where it should go on up: whoever asked stopped waiting, or
    /// this is nobody's idea of a failure at a website.
    /// </returns>
    public static FetchOutcome? OutcomeOf(Exception failure, FetchKind kind, FetchDeadline deadline)
    {
        ArgumentNullException.ThrowIfNull(deadline);

        if (deadline.OwnerGaveUp)
        {
            return null;
        }

        // Past the deadline the response has been disposed underneath whatever was reading it, which
        // can surface as nearly anything. All of them mean the time ran out.
        return deadline.Passed ? FetchOutcome.Unreachable : Classify(failure, kind);
    }

    /// <summary>
    /// What a failure comes to, when the time has not run out.
    /// </summary>
    /// <param name="failure">What was thrown.</param>
    /// <param name="kind">What was being asked for.</param>
    /// <returns>What to say about it, or nothing where it is not a failure at a website.</returns>
    public static FetchOutcome? Classify(Exception failure, FetchKind kind) => failure switch
    {
        AddressRefusedException => FetchOutcome.PrivateAddress,
        ResponseTooLargeException => FetchOutcome.TooLarge,
        HttpRequestException request => OfRequest(request),
        InvalidDataException => Unreadable(kind),
        XmlException => FetchOutcome.NotASitemap,
        OperationCanceledException or IOException => FetchOutcome.Unreachable,
        _ => null,
    };

    private static FetchOutcome OfRequest(HttpRequestException request)
    {
        if (request.InnerException is AddressRefusedException)
        {
            return FetchOutcome.PrivateAddress;
        }

        return request.HttpRequestError switch
        {
            HttpRequestError.SecureConnectionError => FetchOutcome.Insecure,
            HttpRequestError.ConfigurationLimitExceeded => FetchOutcome.TooLarge,
            _ => FetchOutcome.Unreachable,
        };
    }

    /// <summary>
    /// A body that could not be unpacked is not what was asked for.
    /// </summary>
    /// <remarks>
    /// A robots.txt nobody can read says nothing about what may be read, which is what the protocol
    /// calls unreachable, and not what it calls unavailable.
    /// </remarks>
    private static FetchOutcome Unreadable(FetchKind kind) => kind switch
    {
        FetchKind.Sitemap => FetchOutcome.NotASitemap,
        FetchKind.Page => FetchOutcome.NotAPage,
        _ => FetchOutcome.Unreachable,
    };
}
