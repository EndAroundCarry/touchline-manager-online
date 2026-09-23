using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace TouchlineManager.Api.Telemetry;

/// <summary>
/// OpenTelemetry wiring shared by the API and the worker.
/// </summary>
/// <remarks>
/// Export is opt-in: with no <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> configured, instrumentation is
/// still registered (so it is exercised in tests and can be scraped later) but nothing leaves the
/// process. That keeps local development and CI free of a collector.
/// </remarks>
internal static class TelemetryExtensions
{
    private const string EndpointEnvironmentVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>Registers tracing and metrics for a service.</summary>
    public static IServiceCollection AddTouchlineTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var endpoint = configuration[EndpointEnvironmentVariable];
        var otlpEndpoint = string.IsNullOrWhiteSpace(endpoint)
            ? null
            : new Uri(endpoint, UriKind.Absolute);

        var openTelemetry = services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName));

        openTelemetry.WithTracing(tracing =>
        {
            tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation();

            if (otlpEndpoint is not null)
            {
                tracing.AddOtlpExporter(exporter => exporter.Endpoint = otlpEndpoint);
            }
        });

        openTelemetry.WithMetrics(metrics =>
        {
            metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation();

            if (otlpEndpoint is not null)
            {
                metrics.AddOtlpExporter(exporter => exporter.Endpoint = otlpEndpoint);
            }
        });

        return services;
    }
}
