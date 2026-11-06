using Microsoft.Extensions.Logging;

namespace Dewiride.Analytics.Infrastructure.ClickHouse;

/// <summary>
/// The log the store's driver writes to, without what it says of a statement somebody stopped
/// waiting for.
/// </summary>
/// <remarks>
/// <para>
/// The driver writes every statement that ends in an exception as an error, a cancellation
/// included. A reader who leaves a screen while it is loading cancels a statement, and so does an
/// engine told to stop while one is in hand, and neither is a failure: written as errors, they are
/// what somebody searching a healthy installation's log for trouble finds.
/// </para>
/// <para>
/// A statement the engine itself gave up on, because it ran longer than it is allowed, reaches the
/// driver as a cancellation too. That one is a failure, and it is told apart by what it carries: a
/// client that ran out of time puts a <see cref="TimeoutException"/> inside it. It is written, with
/// the identifier of the statement, which is all that names a statement the store never answered.
/// Every other failure is written exactly as the driver writes it.
/// </para>
/// </remarks>
/// <param name="inner">The host's own log.</param>
internal sealed class StoreDriverLogging(ILoggerFactory inner) : ILoggerFactory
{
    /// <summary>
    /// Whether a statement ended because somebody stopped waiting for it, rather than because it
    /// failed or ran out of the time it is allowed.
    /// </summary>
    /// <param name="exception">What the statement ended in, if anything.</param>
    /// <returns><see langword="true"/> where nothing went wrong with the statement itself.</returns>
    public static bool IsAbandoned(Exception? exception) =>
        exception is OperationCanceledException { InnerException: not TimeoutException };

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) =>
        new WithoutAbandoned(inner.CreateLogger(categoryName));

    /// <inheritdoc />
    public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);

    /// <inheritdoc />
    public void Dispose()
    {
        // The host made the log this writes to, and the host disposes it.
    }

    /// <summary>One of the driver's logs, which says nothing of a statement somebody abandoned.</summary>
    /// <param name="log">The host's log under the same name.</param>
    private sealed class WithoutAbandoned(ILogger log) : ILogger
    {
        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => log.BeginScope(state);

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => log.IsEnabled(logLevel);

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsAbandoned(exception))
            {
                log.Log(logLevel, eventId, state, exception, formatter);
            }
        }
    }
}
