using Microsoft.Extensions.Diagnostics.HealthChecks;
using TouchlineManager.Infrastructure.Logging;

namespace TouchlineManager.Api.Health;

/// <summary>
/// Renders a health report as JSON, including per-check state so that "the API is up but the
/// database is not" is visible without reading logs.
/// </summary>
internal static class HealthResponseWriter
{
    /// <summary>Writes the report to the response.</summary>
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 2),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 2),
                description = entry.Value.Description,
                error = entry.Value.Exception is null ? null : LogRedactor.Redact(entry.Value.Exception.Message),
            }),
        };

        return context.Response.WriteAsJsonAsync(payload, context.RequestAborted);
    }
}
