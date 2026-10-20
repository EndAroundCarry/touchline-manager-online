using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Tactical11Manager.Application;
using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Jobs;
using Tactical11Manager.Domain.Market;
using Tactical11Manager.Infrastructure;
using Tactical11Manager.Infrastructure.Persistence;

namespace Tactical11Manager.Worker.IntegrationTests;

/// <summary>
/// The free-agent pool's materialiser is registered and places this week's and next week's jobs (`FA-5`,
/// master plan §7.2).
/// </summary>
/// <remarks>
/// The same regression guard as the AI market's: a scheduler that exists but is never registered as a hosted
/// service never produces its job. This starts the worker's own composition with only the free-agent scheduler
/// enabled and asserts the rows appear, due at Monday's auction window.
/// </remarks>
public sealed class FreeAgentSchedulerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("tactical11_free_agent_scheduler")
        .WithUsername("tactical11_app")
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

            // Only the free-agent pool runs; every other deadline is off so the materialiser under test is alone.
            ["FreeAgents:EnableBatches"] = "true",
            ["FreeAgents:CheckIntervalSeconds"] = "30",
            ["AiMarket:EnableEvaluation"] = "false",
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

        // Last registration wins, so the scheduler keys the week off the test's clock.
        builder.Services.AddSingleton<IClock>(_clock);

        _host = builder.Build();

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>().Database.MigrateAsync();
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
    public async Task The_free_agent_scheduler_materialises_this_weeks_and_next_weeks_batch()
    {
        // Wednesday 23 September 2026: the current week opened on Monday the 21st.
        var thisWeek = new DateOnly(2026, 9, 21);
        var nextWeek = new DateOnly(2026, 9, 28);

        var materialised = await WaitForJobAsync(FreeAgentJobTypes.WeekKey(nextWeek), TimeSpan.FromSeconds(30));

        materialised.Should().BeTrue("the free-agent pool must be materialised by the worker (FA-5)");

        await using var scope = _host!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var current = await db.Jobs.SingleAsync(job => job.BusinessKey == FreeAgentJobTypes.WeekKey(thisWeek));
        var next = await db.Jobs.SingleAsync(job => job.BusinessKey == FreeAgentJobTypes.WeekKey(nextWeek));

        current.JobType.Should().Be(FreeAgentJobTypes.Batch);
        current.DueAt.Should().Be(FreeAgentWeeks.OpensAt(thisWeek));
        next.DueAt.Should().Be(FreeAgentWeeks.OpensAt(nextWeek));
    }

    private async Task<bool> WaitForJobAsync(string businessKey, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = _host!.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            if (await db.Jobs.AnyAsync(job => job.BusinessKey == businessKey))
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        return false;
    }

    /// <summary>A clock the test drives, so the scheduler keys one deterministic week.</summary>
    private sealed class WorkerClock : IClock
    {
        /// <inheritdoc />
        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
    }
}
