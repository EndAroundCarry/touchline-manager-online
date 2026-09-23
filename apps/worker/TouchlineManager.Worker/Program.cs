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

var host = builder.Build();

await host.RunAsync();
