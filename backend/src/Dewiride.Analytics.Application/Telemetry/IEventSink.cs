using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Application.Telemetry;

/// <summary>
/// Accepts validated events for durable storage.
/// </summary>
/// <remarks>
/// The collector depends on this rather than on a store, so that the write path can later
/// gain batching, a queue or a write-ahead buffer without the ingest use case changing. Those
/// are deliberately absent today: at the volumes this product's customers generate, a
/// synchronous write is simpler, has fewer failure modes, and loses nothing on a crash.
/// The trigger for revisiting is recorded in the plan rather than left to intuition.
/// </remarks>
public interface IEventSink
{
    /// <summary>
    /// Writes one event.
    /// </summary>
    /// <param name="rawEvent">The validated event.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task WriteAsync(RawEvent rawEvent, CancellationToken cancellationToken);

    /// <summary>
    /// Writes a batch of events, used by the log importer and the traffic generator.
    /// </summary>
    /// <param name="rawEvents">The validated events.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task WriteBatchAsync(IReadOnlyCollection<RawEvent> rawEvents, CancellationToken cancellationToken);
}
