namespace Dewiride.Analytics.Domain.Telemetry;

/// <summary>
/// The kind of device a visit was made from.
/// </summary>
/// <remarks>
/// Deliberately coarse. What a customer wants to know is whether their writing is being read on a
/// phone or at a desk, and every finer distinction than this rests on either a string the visitor
/// wrote or a measurement precise enough to help identify them. Four buckets answer the question
/// without either.
/// </remarks>
public enum DeviceClass
{
    /// <summary>Nothing observed said what this was. A correct answer, not a gap to be filled.</summary>
    Unknown = 0,

    /// <summary>A handheld device.</summary>
    Phone = 1,

    /// <summary>A touch device with a screen too large to be held in one hand.</summary>
    Tablet = 2,

    /// <summary>A laptop or a desktop computer.</summary>
    Desktop = 3,

    /// <summary>
    /// Something that is none of the above — a television, a console, a watch, a program that
    /// named itself honestly as neither a browser nor a device.
    /// </summary>
    Other = 4,
}
