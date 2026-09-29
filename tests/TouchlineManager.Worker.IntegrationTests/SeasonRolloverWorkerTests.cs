using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using TouchlineManager.Application;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Competition;
using TouchlineManager.Application.World;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.World.Generation;
using TouchlineManager.Infrastructure;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Worker.IntegrationTests;

/// <summary>
/// A season rolled over by the worker alone: the scheduler materialises the rollover once the season's
/// deadline passes, the queue claims it, and the handler closes the season and opens the next one
/// (`PR-4`, master plan §7.2, §7.5, ADR-0031).
/// </summary>
/// <remarks>
/// Nothing here calls the use case. The scheduler turns the calendar into a job, the queue claims it, and the
/// handler does the work — the arrangement every season's close depends on: the database row is the deadline
/// and the worker is the only thing that advances it (ADR-0001, ADR-0003). Every other scheduler is switched
/// off so the only thing the worker can do is the one thing under test, and the closing season's matchdays are
/// fabricated rather than simulated because the engine's determinism belongs to Stage 5's suite.
/// </remarks>
public sealed class SeasonRolloverWorkerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("touchline_rollover_worker")
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
            ["Worker:LeaseSeconds"] = "240",
            ["Worker:MaxConcurrentJobs"] = "4",

            // Only the rollover runs; every other deadline is off so the worker has one thing to do.
            ["Rollover:EnableRollover"] = "true",
            ["Rollover:CheckIntervalSeconds"] = "30",
            ["Matchday:EnableMatchdayWorker"] = "false",
            ["Training:EnableDailyProgression"] = "false",
            ["AiClubs:EnableEvaluation"] = "false",
            ["Auctions:EnableAuctions"] = "false",
            ["AiMarket:EnableEvaluation"] = "false",
            ["Provisioning:EnableProvisioning"] = "false",
            ["Inactivity:EnableEvaluation"] = "false",
            ["Reminders:EnableReminders"] = "false",
            ["Outbox:EnableDispatch"] = "false",
        });

        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddJobQueueWorker();

        // Last registration wins, so the worker, the scheduler, and the use cases all read the test's clock.
        builder.Services.AddSingleton<IClock>(_clock);

        _host = builder.Build();

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            await db.Database.MigrateAsync();

            var seeder = scope.ServiceProvider.GetRequiredService<SeedWorld>();

            var seeded = await seeder.ExecuteAsync(new SeedWorldRequest("worker-rollover-seed"), CancellationToken.None);

            _clock.WorldId = seeded.WorldId;

            var season = await db.Seasons.SingleAsync(candidate => candidate.WorldId == seeded.WorldId);

            await PublishSeasonAsync(scope.ServiceProvider, season.Id);

            // The season's final matchday has passed, so the scheduler finds it due the moment it looks.
            _clock.UtcNow = season.EndsAt.AddMinutes(1);
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
    public async Task The_worker_rolls_the_season_over_when_its_deadline_passes()
    {
        var rolled = await WaitForRolloverCompletedAsync(TimeSpan.FromMinutes(2));

        rolled.Should().BeTrue(
            "the scheduler materialises the rollover, the queue claims it, and the handler closes the season");

        await using var scope = _host!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var world = await db.GameWorlds.SingleAsync();

        world.CurrentSeasonNumber.Should().Be(2, "TIME-3: the game year advanced at rollover");

        var closing = await db.Seasons.SingleAsync(candidate => candidate.SequenceNumber == 1);
        var next = await db.Seasons.SingleAsync(candidate => candidate.SequenceNumber == 2);

        closing.Status.Should().Be(SeasonStatus.Completed, "PR-6");
        next.Status.Should().Be(SeasonStatus.Active, "onboarding reopens");

        // The rollover job is the only job the worker ran, and it reached a terminal state (ADR-0003).
        var jobs = await db.Jobs
            .Where(job => job.JobType == "competition.season-rollover")
            .Select(job => job.Status)
            .ToListAsync();

        jobs.Should().ContainSingle().Which.Should().Be("completed");

        // Every active tier begins the next season with a schedule (CAL-1, PR-5).
        var nextDivisionSeasons = await db.DivisionSeasons
            .Where(divisionSeason => divisionSeason.SeasonId == next.Id)
            .Select(divisionSeason => divisionSeason.Id)
            .ToListAsync();

        nextDivisionSeasons.Should().HaveCount(6, "one per seeded country");

        foreach (var divisionSeasonId in nextDivisionSeasons)
        {
            (await db.Matchdays.CountAsync(matchday => matchday.DivisionSeasonId == divisionSeasonId))
                .Should()
                .Be(34);
        }
    }

    /// <summary>Marks every matchday of a season published with a deterministic score and rebuilds the tables.</summary>
    private async Task PublishSeasonAsync(IServiceProvider services, Guid seasonId)
    {
        var db = services.GetRequiredService<TouchlineManagerDbContext>();
        var now = _clock.UtcNow;

        var divisionSeasonIds = await db.DivisionSeasons
            .Where(divisionSeason => divisionSeason.SeasonId == seasonId)
            .Select(divisionSeason => divisionSeason.Id)
            .ToListAsync();

        foreach (var divisionSeasonId in divisionSeasonIds)
        {
            var matchdays = await db.Matchdays
                .Where(matchday => matchday.DivisionSeasonId == divisionSeasonId)
                .ToListAsync();
            var matchdayIds = matchdays.Select(matchday => matchday.Id).ToList();

            var fixtures = await db.Fixtures
                .Where(fixture => matchdayIds.Contains(fixture.MatchdayId))
                .ToListAsync();

            foreach (var fixture in fixtures)
            {
                var key = fixture.Id.ToString("D");
                var home = (int)(DeterministicDigest.SeedOf(key, "home") % 4);
                var away = (int)(DeterministicDigest.SeedOf(key, "away") % 4);

                fixture.Lock(now);
                fixture.Stage(home, away, Guid.CreateVersion7(), now);
                fixture.Publish(now);
            }

            foreach (var matchday in matchdays)
            {
                matchday.MarkStaged(now);
                matchday.Publish(now);
            }
        }

        await db.SaveChangesAsync(CancellationToken.None);

        var rebuild = services.GetRequiredService<RebuildDivisionProjections>();

        foreach (var divisionSeasonId in divisionSeasonIds)
        {
            await rebuild.ExecuteAsync(divisionSeasonId, apply: true, CancellationToken.None);
        }
    }

    /// <summary>Waits for the rollover job to reach a terminal state.</summary>
    private async Task<bool> WaitForRolloverCompletedAsync(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = _host!.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            var completed = await db.Jobs.AnyAsync(
                job => job.JobType == "competition.season-rollover" && job.Status == "completed");

            if (completed)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        return false;
    }

    /// <summary>A clock the test drives, so a deadline months away is due now (TIME-2).</summary>
    private sealed class WorkerClock : IClock
    {
        /// <inheritdoc />
        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

        /// <summary>Gets the identity of the seeded world, read from the database after seeding.</summary>
        public Guid WorldId { get; set; }
    }
}
