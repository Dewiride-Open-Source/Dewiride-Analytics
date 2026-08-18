using System.Reflection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Dewiride.Analytics.Api.Observability;

/// <summary>
/// Wires logs, metrics and traces to the OpenTelemetry protocol.
/// </summary>
/// <remarks>
/// One protocol and no vendor. A self-hoster points this at whatever they already run and a
/// hosted deployment points it at a managed collector, without either being a different build.
/// Nothing is exported until an endpoint is configured, so the default installation carries the
/// instrumentation and none of the traffic.
/// </remarks>
internal static class ObservabilityRegistration
{
    /// <summary>Name this process reports itself under.</summary>
    private const string ServiceName = "dewiride-analytics-api";

    /// <summary>
    /// The standard variable a collector's address is supplied in, honoured so that operators can
    /// use the configuration they already know.
    /// </summary>
    private const string ExporterEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>
    /// Adds instrumentation, and the exporter when a collector has been configured.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IHostApplicationBuilder AddObservability(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        var telemetry = builder.Services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName, serviceVersion: Version()))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options => options.Filter = IsWorthTracing)
                .AddHttpClientInstrumentation());

        if (!string.IsNullOrWhiteSpace(builder.Configuration[ExporterEndpointKey]))
        {
            telemetry.UseOtlpExporter();
        }

        return builder;
    }

    /// <summary>
    /// Keeps the probes out of the traces.
    /// </summary>
    /// <remarks>
    /// An orchestrator polls readiness every few seconds forever. Traced, those requests would be
    /// most of what anybody looking for a real problem has to read through.
    /// </remarks>
    private static bool IsWorthTracing(HttpContext context) =>
        !context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);

    private static string Version() =>
        typeof(ObservabilityRegistration).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";
}
