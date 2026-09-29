using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using TouchlineManager.Application;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Jobs;
using TouchlineManager.Infrastructure;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Worker.IntegrationTests;

/// <summary>
/// The AI transfer market's materialiser is registered and places the day's job (`TRF-12`, master plan §7.2).
/// </summary>
/// <remarks>
/// A regression guard: <c>AiMarketScheduler</c> existed but was never registered as a hosted service, so
/// <c>market.evaluate-ai</c> could never be produced and the AI never traded. This starts the worker's own
/// composition with only the AI-market scheduler enabled and asserts the row appears.
/// </remarks>
public sealed class AiMarketSchedulerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("touchline_ai_market_scheduler")
        .WithUsername("touchline_app")
        .WithPassword("integration_test_password")
        .Build();

    private readonly WorkerClock _clock = new();

    private IHost? _host;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var builder = Host.CreateApplicationBuilder();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = _container.GetConnectionString(),
            ["Worker:PollIntervalSeconds"] = "1",
            ["Worker:MaxIdlePollIntervalSeconds"] = "1",
            ["Worker:LeaseSeconds"] = "30",

            // Only the AI market runs; every other deadline is off so the materialiser under test is alone.
            ["AiMarket:EnableEvaluation"] = "true",
            ["AiMarket:CheckIntervalSeconds"] = "30",
            ["Matchday:EnableMatchdayWorker"] = "false",
            ["Training:EnableDailyProgression"] = "false",
            ["AiClubs:EnableEvaluation"] = "false",
            ["Finance:EnableWeeklyRun"] = "false",
            ["Auctions:EnableAuctions"] = "false",
            ["Provisioning:EnableProvisioning"] = "false",
            ["Inactivity:EnableEvaluation"] = "false",
            ["Reminders:EnableReminders"] = "false",
            ["Outbox:EnableDispatch"] = "false",
            ["Rollover:EnableRollover"] = "false",
        });

        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddJobQueueWorker();

        // Last registration wins, so the scheduler keys the day off the test's clock.
        builder.Services.AddSingleton<IClock>(_clock);

        _host = builder.Build();

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>().Database.MigrateAsync();
        }

        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        await _container.DisposeAsync();
    }

    [Fact]
    public async Task The_ai_market_scheduler_materialises_the_daily_evaluation()
    {
        var day = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        var businessKey = AiMarketJobTypes.DayKey(day);

        var materialised = await WaitForJobAsync(businessKey, TimeSpan.FromSeconds(30));

        materialised.Should()
            .BeTrue("the AI market's daily evaluation must be materialised by the worker (TRF-12)");

        await using var scope = _host!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var job = await db.Jobs.SingleAsync(candidate => candidate.BusinessKey == businessKey);

        job.JobType.Should().Be(AiMarketJobTypes.Evaluate);
    }

    private async Task<bool> WaitForJobAsync(string businessKey, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = _host!.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            if (await db.Jobs.AnyAsync(job => job.BusinessKey == businessKey))
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        return false;
    }

    /// <summary>A clock the test drives, so the scheduler keys one deterministic day.</summary>
    private sealed class WorkerClock : IClock
    {
        /// <inheritdoc />
        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
    }
}
