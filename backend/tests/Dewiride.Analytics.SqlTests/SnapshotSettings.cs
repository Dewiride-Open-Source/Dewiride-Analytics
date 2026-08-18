using System.Runtime.CompilerServices;
using DiffEngine;

namespace Dewiride.Analytics.SqlTests;

/// <summary>
/// Configures snapshot comparison for this assembly.
/// </summary>
internal static class SnapshotSettings
{
    /// <summary>
    /// Runs before the first test.
    /// </summary>
    /// <remarks>
    /// Launching a diff tool is the right behaviour at a developer's desk and the wrong one
    /// everywhere else: on a build agent it opens a window nobody sees and holds the run open
    /// until it is killed. Approving a changed statement is a deliberate act — read the received
    /// file, then move it over the approved one — so the tool is not needed here either.
    /// </remarks>
    [ModuleInitializer]
    public static void Initialize() => DiffRunner.Disabled = true;
}
