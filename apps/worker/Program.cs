using FootballManager.Application.Ops;
using FootballManager.Application.Time;
using FootballManager.Infrastructure.Ops;
using FootballManager.Infrastructure.Persistence;
using FootballManager.Worker.Jobs;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Missing required configuration 'ConnectionStrings:Postgres'.");

builder.Logging.AddJsonConsole(console =>
{
    console.UseUtcTimestamp = true;
    console.TimestampFormat = "O";
});

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddDbContext<GameDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<IJobHandler, NoOpJobHandler>();
builder.Services.AddSingleton(new JobWorkerOptions(
    LeaseDuration: TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("Jobs:LeaseSeconds", 60)),
    PollInterval: TimeSpan.FromMilliseconds(builder.Configuration.GetValue<int>("Jobs:PollIntervalMs", 1000)),
    RetryBaseSeconds: builder.Configuration.GetValue<int>("Jobs:RetryBaseSeconds", 2),
    RetryMaxSeconds: builder.Configuration.GetValue<int>("Jobs:RetryMaxSeconds", 60)));
builder.Services.AddHostedService<JobWorker>();
builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres", failureStatus: HealthStatus.Unhealthy, tags: new[] { "ready" });

var app = builder.Build();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

await app.RunAsync();

public partial class Program
{
}
