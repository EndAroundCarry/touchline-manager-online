using FootballManager.Api.Middleware;
using FootballManager.Application.Ops;
using FootballManager.Application.Time;
using FootballManager.Contracts.Ops;
using FootballManager.Infrastructure.Ops;
using FootballManager.Infrastructure.Persistence;
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
builder.Services.AddScoped<IJobQueue>(sp => new PostgresJobQueue(
    sp.GetRequiredService<GameDbContext>(),
    sp.GetRequiredService<IClock>(),
    builder.Configuration.GetValue<int>("Jobs:MaxAttempts", 5)));
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres", failureStatus: HealthStatus.Unhealthy, tags: new[] { "ready" });

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

MapModuleGroups(app);

if (app.Environment.IsDevelopment())
{
    var adminJobs = app.MapGroup("/api/v1/admin/jobs").WithTags("admin");
    adminJobs.MapPost("/noop", async (IJobQueue queue, IClock clock, CancellationToken cancellationToken) =>
    {
        var now = clock.UtcNow;
        var businessKey = $"dev-noop-{Guid.NewGuid():N}";
        await queue.EnqueueAsync(new JobRequest(NoOpJobHandler.Type, businessKey, DueAt: now), cancellationToken);
        return Results.Ok(new EnqueueNoOpJobResponse(businessKey, now));
    });
}

app.Run();

static void MapModuleGroups(WebApplication app)
{
    _ = app.MapGroup("/api/v1/auth").WithTags("auth");
    _ = app.MapGroup("/api/v1/world").WithTags("world");
    _ = app.MapGroup("/api/v1/squad").WithTags("squad");
    _ = app.MapGroup("/api/v1/competition").WithTags("competition");
    _ = app.MapGroup("/api/v1/match").WithTags("match");
    _ = app.MapGroup("/api/v1/market").WithTags("market");
    _ = app.MapGroup("/api/v1/finance").WithTags("finance");
    _ = app.MapGroup("/api/v1/comms").WithTags("comms");
    _ = app.MapGroup("/api/v1/ops").WithTags("ops");
}

public partial class Program
{
}
