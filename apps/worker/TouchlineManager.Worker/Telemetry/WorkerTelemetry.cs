using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using TouchlineManager.Infrastructure.Telemetry;

namespace TouchlineManager.Worker.Telemetry;

/// <summary>
/// OpenTelemetry wiring for the worker.
/// </summary>
/// <remarks>
/// The worker does not host ASP.NET Core, so it registers HTTP client instrumentation (for email
/// dispatch and any provider calls) but no request instrumentation. Export is opt-in through
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>, so local runs and CI need no collector.
/// </remarks>
internal static class WorkerTelemetry
{
    private const string EndpointEnvironmentVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>Registers tracing and metrics for the worker.</summary>
    public static IServiceCollection AddWorkerTelemetry(
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
            tracing.AddHttpClientInstrumentation();

            if (otlpEndpoint is not null)
            {
                tracing.AddOtlpExporter(exporter => exporter.Endpoint = otlpEndpoint);
            }
        });

        openTelemetry.WithMetrics(metrics =>
        {
            metrics
                .AddHttpClientInstrumentation()

                // The operational funnel counters (`F-54`, ADR-0041). The retention transitions happen in
                // the worker, so this host must register the same meter as the API.
                .AddMeter(OperationalMetrics.MeterName);

            if (otlpEndpoint is not null)
            {
                metrics.AddOtlpExporter(exporter => exporter.Endpoint = otlpEndpoint);
            }
        });

        return services;
    }
}
