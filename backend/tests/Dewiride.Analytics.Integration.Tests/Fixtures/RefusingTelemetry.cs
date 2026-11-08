using System.Reflection;

namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// A telemetry store that refuses every question, and counts how many it was asked.
/// </summary>
/// <remarks>
/// For the answers that must come from the control plane alone: put in place of the real store, it fails
/// any request that reaches it and says how many tried. Public and unsealed because a proxy is generated
/// as a type derived from it.
/// </remarks>
public class RefusingTelemetry : DispatchProxy
{
    private int _asked;

    /// <summary>How many questions it was asked.</summary>
    public int Asked => Volatile.Read(ref _asked);

    /// <inheritdoc />
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        Interlocked.Increment(ref _asked);

        throw new InvalidOperationException($"The telemetry store was asked {targetMethod?.Name}.");
    }
}
