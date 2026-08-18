using Dewiride.Analytics.Api.Security;
using Dewiride.Analytics.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace Dewiride.Analytics.Api.Composition;

/// <summary>
/// Registers how somebody signs in and what they are allowed to do once they have.
/// </summary>
/// <remarks>
/// <para>
/// Sign-in is a cookie, not a bearer token. The dashboard is a first-party application served
/// from the same origin as this API, so a cookie the browser will not hand to script is strictly
/// safer than a token that has to be stored somewhere script can read. Cross-site request forgery
/// — the risk a cookie brings with it — is answered by <see cref="AntiforgeryGuard"/> and by the
/// cookie refusing to travel on a cross-site request.
/// </para>
/// <para>
/// Everything is closed unless it is opened. The fallback policy demands a signed-in caller on
/// every endpoint, and the handful that must work without one say so explicitly. The alternative
/// ordering — open unless closed — publishes an entire site's traffic the first time somebody
/// adds an endpoint and forgets a line.
/// </para>
/// </remarks>
internal static class AuthenticationRegistration
{
    /// <summary>
    /// How long a sign-in lasts without further activity.
    /// </summary>
    /// <remarks>
    /// Fourteen days with sliding renewal. Analytics is checked in bursts rather than daily, and
    /// a shorter window mostly succeeds in making people keep a password manager open on the
    /// sign-in page. The security stamp is re-checked periodically regardless, so changing a
    /// password still ends the other sessions.
    /// </remarks>
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(14);

    /// <summary>Name of the cookie that carries a sign-in.</summary>
    public const string SessionCookieName = "dewiride.session";

    /// <summary>
    /// Adds cookie sign-in, the closed-by-default authorisation policy and forgery protection.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IHostApplicationBuilder AddAuthentication(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies();

        builder.Services.ConfigureApplicationCookie(ConfigureSessionCookie);

        builder.Services.AddAuthorization(options => options.FallbackPolicy =
            new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());

        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = AntiforgeryGuard.HeaderName;
            options.Cookie.Name = "dewiride.forgery";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        return builder;
    }

    /// <summary>
    /// Adds the parts of sign-in that need a request to sign in to.
    /// </summary>
    /// <remarks>
    /// Handed to the control-plane registration, which owns the account store but deliberately
    /// knows nothing about HTTP. <see cref="SignInManager{TUser}"/> issues cookies and reads the
    /// current request, so it is registered from here.
    /// </remarks>
    /// <param name="accounts">The account store.</param>
    public static void AddSignIn(IdentityBuilder accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        accounts.AddSignInManager<SignInManager<ApplicationUser>>();
    }

    /// <summary>
    /// Sets how the sign-in cookie behaves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>SameSite=Lax</c> means the browser will not attach it to a request another site
    /// initiated other than an ordinary link, which is what makes a forged write from another
    /// page impossible before any token is checked.
    /// </para>
    /// <para>
    /// The secure flag follows the scheme the request arrived on rather than being forced on.
    /// Forcing it would break every install reached over plain HTTP on a local network, which is
    /// how a self-hoster first tries the product; a deployment behind a TLS-terminating proxy
    /// gets it automatically, because the forwarded-headers middleware has already told this
    /// process the original request was HTTPS.
    /// </para>
    /// </remarks>
    /// <param name="options">Cookie options to configure.</param>
    private static void ConfigureSessionCookie(CookieAuthenticationOptions options)
    {
        options.Cookie.Name = SessionCookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

        options.ExpireTimeSpan = SessionLifetime;
        options.SlidingExpiration = true;

        AnswerInsteadOfRedirecting(options);
    }

    /// <summary>
    /// Makes an unauthenticated or forbidden request end in a status code rather than a redirect.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This process serves data and never a page. The sign-in screen belongs to the dashboard
    /// application in front of it, so the sign-in address the cookie handler would otherwise send
    /// people to does not exist here, and following it produces nothing.
    /// </para>
    /// <para>
    /// Left alone that is not merely untidy. The framework only skips the redirect for endpoints
    /// it recognises as carrying data, so a request for an address that matches nothing at all —
    /// which is what a browser asks for the moment somebody types the server's address — is sent
    /// to a page that does not exist, which is itself unauthenticated, which redirects again. The
    /// browser gives up after twenty or so attempts and shows an error. Answering plainly ends
    /// that, and stops every refusal advertising a path that was never built.
    /// </para>
    /// </remarks>
    /// <param name="options">Cookie options to configure.</param>
    private static void AnswerInsteadOfRedirecting(CookieAuthenticationOptions options)
    {
        options.Events.OnRedirectToLogin = context => Answer(context, StatusCodes.Status401Unauthorized);
        options.Events.OnRedirectToAccessDenied = context => Answer(context, StatusCodes.Status403Forbidden);

        static Task Answer(RedirectContext<CookieAuthenticationOptions> context, int statusCode)
        {
            context.Response.StatusCode = statusCode;

            return Task.CompletedTask;
        }
    }
}
