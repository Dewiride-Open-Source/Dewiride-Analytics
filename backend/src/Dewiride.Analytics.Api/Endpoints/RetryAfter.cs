using System.Globalization;

namespace Dewiride.Analytics.Api.Endpoints;

/// <summary>
/// Writes how long a caller turned away should wait, as the <c>Retry-After</c> header says it.
/// </summary>
internal static class RetryAfter
{
    /// <summary>
    /// Writes a wait as whole seconds.
    /// </summary>
    /// <remarks>
    /// Rounded up, and never below one, because the header counts in whole seconds and a caller
    /// told nought would try again at once, into the same window that has just turned it away.
    /// </remarks>
    /// <param name="wait">How long until the next allowance opens.</param>
    /// <returns>The header's value.</returns>
    public static string Seconds(TimeSpan wait) =>
        Math.Max(1, (long)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
}
