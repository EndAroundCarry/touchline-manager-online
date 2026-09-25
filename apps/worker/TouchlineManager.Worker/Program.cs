using TouchlineManager.Application;
using TouchlineManager.Infrastructure;
using TouchlineManager.Infrastructure.Logging;
using TouchlineManager.Worker.Telemetry;

var builder = Host.CreateApplicationBuilder(args);

// The same structured logging and the same redaction filter as the API: a secret must not be able
// to escape through the worker's logs either (data-classification §4).
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
});
builder.Logging.AddLogRedaction();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddJobQueueWorker();
builder.Services.AddWorkerTelemetry(builder.Configuration, "touchline-worker");

// Real time, unless a non-production environment has opted into a compressed clock. This throws rather
// than falls back in Production, so a compressed configuration can never reach a live world (TIME-6).
var clock = builder.Services.AddGameClock(builder.Configuration, builder.Environment);

var host = builder.Build();

if (clock.IsCompressed)
{
    host.Services
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("TouchlineManager.Worker")
        .LogWarning(
            "A compressed clock is in force: game time runs {Rate}x from {Anchor}, in {Environment}. "
            + "The queue's deadlines, the calendar, and every job are read against it (TIME-6).",
            clock.Rate,
            clock.RealAnchorUtc,
            builder.Environment.EnvironmentName);
}

await host.RunAsync();
