using Microsoft.AspNetCore.Mvc.Testing;

namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// Asks what stops an installation starting.
/// </summary>
/// <remarks>
/// A test host runs the product on a thread of its own and then asks it for its lifetime. A product
/// that refused at once may already be disposed by then, and the same refusal then comes back as
/// being unable to ask, so it is tried again until the refusal itself is seen.
/// </remarks>
internal static class HostRefusal
{
    private const int Attempts = 5;

    /// <summary>
    /// Starts a host that should not start.
    /// </summary>
    /// <param name="host">Makes a host, as many times as it takes.</param>
    /// <returns>What stopped it, or nothing where it started.</returns>
    public static Exception? Of(Func<WebApplicationFactory<Program>> host)
    {
        ArgumentNullException.ThrowIfNull(host);

        Exception? refusal = null;

        for (var attempt = 0; attempt < Attempts && refusal is null or ObjectDisposedException; attempt++)
        {
            using var starting = host();

            refusal = Record.Exception(() => starting.CreateClient());
        }

        return refusal;
    }
}
