using Dewiride.Analytics.Application.Ingest;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Classification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dewiride.Analytics.Application;

/// <summary>
/// Registers the use cases this layer provides.
/// </summary>
/// <remarks>
/// Only the use cases. Everything they depend on is a port implemented elsewhere, which is what
/// keeps this layer testable without a database and independent of how the product is hosted.
/// </remarks>
public static class ApplicationRegistration
{
    /// <summary>
    /// Adds the application's use cases.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IHostApplicationBuilder AddApplicationServices(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddScoped<EventIngestor>();

        // One classifier for the process: it is immutable once built from the compiled ruleset,
        // performs no I/O, and is safe to use from as many threads as ask.
        builder.Services.AddSingleton(TrafficClassifier.Current());
        builder.Services.AddScoped<SessionClassifier>();

        return builder;
    }
}
