namespace Dewiride.Analytics.Api.Composition;

/// <summary>
/// The few things the host itself says about how it was composed.
/// </summary>
internal static partial class StartupLog
{
    [LoggerMessage(
        EventId = 3003,
        Level = LogLevel.Information,
        Message = "Dewiride Analytics is starting in the {Edition} edition.")]
    public static partial void EditionStarting(ILogger logger, string edition);
}
