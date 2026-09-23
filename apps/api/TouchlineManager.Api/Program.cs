using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using TouchlineManager.Api.Configuration;
using TouchlineManager.Api.Endpoints;
using TouchlineManager.Api.Health;
using TouchlineManager.Api.Middleware;
using TouchlineManager.Api.Telemetry;
using TouchlineManager.Application;
using TouchlineManager.Infrastructure;
using TouchlineManager.Infrastructure.Logging;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------------------------
// Logging. Structured with scopes, and every message redacted at the sink boundary so the list in
// docs/security/data-classification.md §4 is enforced by code rather than by memory.
// ---------------------------------------------------------------------------------------------
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
});
builder.Logging.AddLogRedaction();

// ---------------------------------------------------------------------------------------------
// Configuration. Validated at startup so a misconfigured deployment fails immediately instead of
// failing the first time a manager acts.
// ---------------------------------------------------------------------------------------------
builder.Services
    .AddOptions<CorsOptions>()
    .Bind(builder.Configuration.GetSection(CorsOptions.SectionName))
    .Validate(
        options => options.AllowedOrigins.Length > 0,
        "Cors:AllowedOrigins must list at least one exact origin (ADR-0002).")
    .Validate(
        options => options.AllowedOrigins.All(origin => !origin.Contains('*', StringComparison.Ordinal)),
        "Cors:AllowedOrigins must not contain a wildcard origin.")
    .ValidateOnStart();

builder.Services
    .AddOptions<DiagnosticsOptions>()
    .Bind(builder.Configuration.GetSection(DiagnosticsOptions.SectionName));

// ---------------------------------------------------------------------------------------------
// Application and infrastructure layers. The API is a composition root and contains no game
// formulas (ADR-0001, DEP-5).
// ---------------------------------------------------------------------------------------------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddTouchlineTelemetry(builder.Configuration, "touchline-api");

// ---------------------------------------------------------------------------------------------
// Web essentials.
// ---------------------------------------------------------------------------------------------
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        // RFC 9457 with a correlation ID attached, so a manager can quote one value in support and
        // we can find the exact request, job, and audit rows.
        if (context.HttpContext.Items[CorrelationIdMiddleware.ItemKey] is string correlationId)
        {
            context.ProblemDetails.Extensions["correlationId"] = correlationId;
        }

        context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    };
});

builder.Services
    .AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

builder.Services.AddOpenApi();

var allowedOrigins = builder.Configuration
    .GetSection(CorsOptions.SectionName)
    .Get<CorsOptions>()?.AllowedOrigins ?? [];

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .AllowCredentials()
    .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
    .WithHeaders("Content-Type", "If-Match", "Idempotency-Key", CorrelationIdMiddleware.HeaderName)
    .WithExposedHeaders("ETag", CorrelationIdMiddleware.HeaderName)));

var app = builder.Build();

// ---------------------------------------------------------------------------------------------
// Pipeline. Correlation is established first so every later log line and error response carries it.
// ---------------------------------------------------------------------------------------------
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<CorrelationIdMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// ---------------------------------------------------------------------------------------------
// Health. Liveness must not depend on the database; readiness must.
// ---------------------------------------------------------------------------------------------
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthResponseWriter.WriteAsync,
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync,
});

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = HealthResponseWriter.WriteAsync,
});

// ---------------------------------------------------------------------------------------------
// Endpoints. One route group per bounded module; each module attaches its endpoints as its stage
// lands, so a feature-incomplete module stays unreachable (master plan §17.12).
// ---------------------------------------------------------------------------------------------
var moduleGroups = app.MapModuleGroups();

var diagnostics = app.Services.GetRequiredService<IOptions<DiagnosticsOptions>>().Value;

if (diagnostics.EnableJobProbe)
{
    moduleGroups["ops"].MapJobProbe();
}

await app.RunAsync();

/// <summary>Exposed so integration tests can drive the real composition root.</summary>
public partial class Program;
