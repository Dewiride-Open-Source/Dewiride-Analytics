using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Extensibility;

/// <summary>
/// Refuses a request that changes something unless it proves it came from this application.
/// </summary>
/// <remarks>
/// <para>
/// A sign-in cookie is attached by the browser to every request to this origin, including one
/// another site caused. The proof required here is a value the caller could only have obtained by
/// reading a response from this application, sent back in a header that no ordinary form or image
/// can set. The cookie half of the pair is inaccessible to script, so neither half alone is
/// enough.
/// </para>
/// <para>
/// The framework's own middleware checks this only for endpoints that bind form data, and every
/// endpoint here takes JSON. Applied as a filter instead, per endpoint, so that leaving it off is
/// a visible decision in the route rather than an accident of content type.
/// </para>
/// <para>
/// It sits beside the edition seams rather than in the host because an edition adds endpoints of
/// its own through <see cref="IEditionEndpoints"/>, and those have to be able to uphold the same
/// guarantee. An edition whose writes were unguarded while running the same screens would be a
/// security advisory rather than a feature difference.
/// </para>
/// </remarks>
public static class AntiforgeryGuard
{
    /// <summary>Header the caller returns the request token in.</summary>
    public const string HeaderName = "X-Csrf-Token";

    /// <summary>
    /// Requires proof of origin before the endpoint runs.
    /// </summary>
    /// <param name="builder">The route being built.</param>
    /// <returns>The route, for chaining.</returns>
    public static RouteHandlerBuilder RequireProofOfOrigin(this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddEndpointFilter(ValidateAsync);
    }

    /// <summary>
    /// Issues a fresh token pair for the caller's next request.
    /// </summary>
    /// <remarks>
    /// The paired cookie is written to the response as a side effect. A token is tied to the
    /// identity that was current when it was issued, so this has to be called again after
    /// somebody signs in or the first write they attempt is refused.
    /// </remarks>
    /// <param name="antiforgery">The antiforgery service.</param>
    /// <param name="context">The request being answered.</param>
    /// <returns>The token to send back in <see cref="HeaderName"/>.</returns>
    public static string IssueToken(IAntiforgery antiforgery, HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(antiforgery);

        return antiforgery.GetAndStoreTokens(context).RequestToken!;
    }

    private static async ValueTask<object?> ValidateAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext).ConfigureAwait(false);
        }
        catch (AntiforgeryValidationException)
        {
            return Refused();
        }

        return await next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// The answer to a request that could not prove where it came from.
    /// </summary>
    /// <remarks>
    /// Says what to do about it, because the usual cause is an honest one: the page was left open
    /// long enough for the token to expire, or it was reloaded from a cache.
    /// </remarks>
    private static ProblemHttpResult Refused() =>
        TypedResults.Problem(
            title: "This request could not be verified.",
            detail: "Reload the page and try again.",
            statusCode: StatusCodes.Status400BadRequest);
}
