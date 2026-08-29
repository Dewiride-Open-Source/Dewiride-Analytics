namespace Dewiride.Analytics.Extensibility;

/// <summary>
/// The named allowances the host counts requests against.
/// </summary>
/// <remarks>
/// Named here because an edition adds endpoints of its own through <see cref="IEditionEndpoints"/>
/// and has to be able to put them under the same allowance as the host's. An endpoint that anybody
/// can reach and that nothing counts is how a form open to the world becomes a way of working
/// through a list.
/// </remarks>
public static class RateLimitPolicies
{
    /// <summary>
    /// Anything to do with an account, counted per network address.
    /// </summary>
    /// <remarks>
    /// One allowance across signing in, setting an installation up, asking for a way back in and
    /// creating an account, because they are the same act from the point of view of somebody
    /// working through addresses: each one answers a question about who has an account here.
    /// </remarks>
    public const string Accounts = "accounts";
    /// <summary>
    /// A screen that asks again on its own, counted per signed-in person.
    /// </summary>
    /// <remarks>
    /// Per person rather than per network address, because several people behind one office
    /// address are several readers and would otherwise ration each other. What is being bounded is
    /// not somebody working through addresses but a screen that renews itself: left alone, a tab a
    /// laptop went to sleep on wakes up and asks for everything it missed, and a dozen tabs left
    /// open overnight are a dozen readings a minute of a site nobody is looking at.
    /// </remarks>
    public const string Live = "live";
}
