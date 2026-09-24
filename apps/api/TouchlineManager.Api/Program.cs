using System.Diagnostics;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using TouchlineManager.Api.Auth;
using TouchlineManager.Api.Configuration;
using TouchlineManager.Api.Endpoints;
using TouchlineManager.Api.Health;
using TouchlineManager.Api.Http;
using TouchlineManager.Api.Middleware;
using TouchlineManager.Api.Telemetry;
using TouchlineManager.Application;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Contracts.Http;
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

// The token signing key is validated here as well as where it is consumed, so a deployment missing
// it fails at startup with a named setting rather than at the first login.
builder.Services
    .AddOptions<AuthOptions>()
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.SigningKey)
            && Encoding.UTF8.GetByteCount(options.SigningKey) >= 32,
        "Auth:SigningKey must be at least 32 bytes of key material (ADR-0002).")
    .Validate(
        options => options.AccessTokenLifetime > TimeSpan.Zero
            && options.RefreshTokenLifetime > TimeSpan.Zero
            && options.EmailVerificationLifetime > TimeSpan.Zero
            && options.PasswordResetLifetime > TimeSpan.Zero,
        "Auth token and link lifetimes must all be positive.")
    .ValidateOnStart();

// ---------------------------------------------------------------------------------------------
// Application and infrastructure layers. The API is a composition root and contains no game
// formulas (ADR-0001, DEP-5).
// ---------------------------------------------------------------------------------------------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddTouchlineTelemetry(builder.Configuration, "touchline-api");

// ---------------------------------------------------------------------------------------------
// Authentication and authorization. The stamp check inside the bearer handler is what makes a
// short-lived token revocable (ADR-0002).
// ---------------------------------------------------------------------------------------------
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IRequestContext, HttpRequestContext>();
builder.Services.AddScoped<AuthCookieWriter>();
builder.Services.AddAuthAuthentication(builder.Configuration);

// ---------------------------------------------------------------------------------------------
// Rate limiting. Only the endpoints an attacker would hammer are throttled here; the broader API
// limits belong to the hardening stage, where they can be tuned from load evidence rather than
// guessed at now.
// ---------------------------------------------------------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    var rateLimits = builder.Configuration
        .GetSection(RateLimitingOptions.SectionName)
        .Get<RateLimitingOptions>() ?? new RateLimitingOptions();

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        await ProblemResults
            .Code(
                StatusCodes.Status429TooManyRequests,
                ApiErrorCodes.TooManyRequests,
                "Too many requests.",
                "Slow down and try again shortly.")
            .ExecuteAsync(context.HttpContext);
    };

    options.AddPolicy(
        RateLimitPolicies.AuthSensitive,
        httpContext => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ClientPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimits.AuthPermitLimit,
                Window = TimeSpan.FromSeconds(rateLimits.AuthWindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
});

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
app.UseMiddleware<SecurityHeadersMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// Routing is explicit so the rate limiter and the authentication middleware below both see endpoint
// metadata — the limiter is attached to specific routes, and authorization policies come from route
// metadata, so both must run after matching.
app.UseRouting();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

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

moduleGroups["auth"].MapAuthEndpoints();
app.MapAccountEndpoints();

var diagnostics = app.Services.GetRequiredService<IOptions<DiagnosticsOptions>>().Value;

if (diagnostics.EnableJobProbe)
{
    moduleGroups["ops"].MapJobProbe();
}

await app.RunAsync();

/// <summary>
/// Partitions the rate limiter by client address, which is the only identity available before a
/// request is authenticated. A missing address shares one bucket rather than escaping the limit.
/// </summary>
static string ClientPartitionKey(HttpContext context) =>
    context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

/// <summary>Exposed so integration tests can drive the real composition root.</summary>
public partial class Program;
