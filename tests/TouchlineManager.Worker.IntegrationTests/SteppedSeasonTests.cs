using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using TouchlineManager.Application;
using TouchlineManager.Application.Ops;
using TouchlineManager.Application.World;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Infrastructure;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Worker.IntegrationTests;

/// <summary>
/// A stepped world plays real rounds, a press at a time, through the real worker (ADR-0049, master plan §16
/// Stage 15).
/// </summary>
/// <remarks>
/// <para>
/// The stepped clock is the point: the world is frozen at a configured instant, and each press enqueues the
/// worker's real advance job, which sets the stored instant and materialises the round about to kick off.
/// Nothing here fabricates a result — the real lock, resolution, and publication run, from frozen snapshots,
/// with the real engine — because the mechanism under test is the stepping, not the matchday pipeline, and the
/// only honest way to prove it drives that pipeline is to let it.
/// </para>
/// <para>
/// It plays a few rounds rather than a whole season: the six divisions share one cadence, so three presses are
/// eighteen published matchdays and a hundred and sixty-two simulated fixtures — enough to prove the stepped
/// clock advances the calendar, and bounded enough to stay a fast test. Stepping past the season deadline is
/// covered by the rollover scheduler's own materialiser, which the advance job invokes alongside the others.
/// </para>
/// </remarks>
public sealed class SteppedSeasonTests : IAsyncLifetime
{
    private const int Rounds = 3;
    private const int Divisions = 6;
    private const int FixturesPerRound = Divisions * 9;

    /// <summary>An instant just before the seeded world's first matchday (2026-10-06).</summary>
    private static readonly DateTimeOffset InitialNow = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("touchline_stepped")
        .WithUsername("touchline_app")
        .WithPassword("integration_test_password")
        .Build();

    private IHost? _host;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // A non-production environment, because a stepped clock is refused in Production (TIME-6).
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = _container.GetConnectionString(),

            // The stepped clock, frozen just before the world's first kickoff.
            ["Clock:Mode"] = "Stepped",
            ["Clock:InitialNowUtc"] = InitialNow.ToString("O"),

            // A fast worker: the advance job and the round's lock/resolve/publish must run promptly.
            ["Worker:PollIntervalSeconds"] = "1",
            ["Worker:MaxIdlePollIntervalSeconds"] = "1",
            ["Worker:LeaseSeconds"] = "240",
            ["Worker:MaxConcurrentJobs"] = "4",

            ["Matchday:EnableMatchdayWorker"] = "true",
            ["Matchday:CheckIntervalSeconds"] = "30",

            // Everything else off, so the run is the stepped clock and the matchday pipeline only.
            ["Rollover:EnableRollover"] = "false",
            ["Training:EnableDailyProgression"] = "false",
            ["AiClubs:EnableEvaluation"] = "false",
            ["Finance:EnableWeeklyRun"] = "false",
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

        // The real stepped clock, as a staging host would choose it.
        builder.Services.AddGameClock(builder.Configuration, builder.Environment);

        _host = builder.Build();

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            await db.Database.MigrateAsync();

            await scope.ServiceProvider
                .GetRequiredService<SeedWorld>()
                .ExecuteAsync(new SeedWorldRequest("stepped-season-seed"), CancellationToken.None);
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
    public async Task Pressing_next_matchday_plays_a_real_round_through_the_worker()
    {
        var lastTarget = InitialNow;

        for (var round = 1; round <= Rounds; round++)
        {
            var target = await NextPendingKickoffAsync();

            await using (var scope = _host!.Services.CreateAsyncScope())
            {
                var result = await scope.ServiceProvider
                    .GetRequiredService<AdvanceGameClock>()
                    .ExecuteAsync("matchday", CancellationToken.None);

                result.Target.Should().Be("matchday");
                result.TargetInstantUtc.Should().Be(target, "the step lands on the next round's kickoff");
                result.Enqueued.Should().BeTrue();
            }

            await WaitUntilRoundPublishedAsync(target);

            lastTarget = target;

            await using (var scope = _host.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

                (await db.Matchdays.CountAsync(matchday =>
                        matchday.KickoffAt == target
                        && matchday.PublicationStatus == MatchdayPublicationStatus.Published))
                    .Should().Be(Divisions, $"all six divisions play round {round} at once");
            }
        }

        await using var finalScope = _host!.Services.CreateAsyncScope();
        var context = finalScope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        // The clock is stored, not per-process: it advanced to the last round's kickoff.
        (await context.GameClocks.Select(clock => clock.GameNow).SingleAsync())
            .Should().Be(lastTarget, "the step persists the instant every host reads (ADR-0049)");

        // Every round played by the presses is published, and every fixture carries a result.
        (await context.Matchdays.CountAsync(matchday =>
                matchday.PublicationStatus == MatchdayPublicationStatus.Published))
            .Should().Be(Rounds * Divisions);

        (await context.Fixtures.CountAsync(fixture => fixture.Status == FixtureStatus.Published))
            .Should().Be(Rounds * FixturesPerRound);

        (await context.Fixtures.CountAsync(fixture =>
                fixture.Status == FixtureStatus.Published && fixture.HomeScore == null))
            .Should().Be(0, "a published fixture always has a score");
    }

    /// <summary>Finds the kickoff of the earliest round that has not been played.</summary>
    private async Task<DateTimeOffset> NextPendingKickoffAsync()
    {
        await using var scope = _host!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        return await db.Matchdays
            .Where(matchday => matchday.PublicationStatus == MatchdayPublicationStatus.Pending)
            .MinAsync(matchday => matchday.KickoffAt);
    }

    /// <summary>Waits for every division's round at one instant to publish, as the worker plays it.</summary>
    private async Task WaitUntilRoundPublishedAsync(DateTimeOffset kickoff)
    {
        var deadline = DateTime.UtcNow.AddSeconds(45);

        while (DateTime.UtcNow < deadline)
        {
            await using var scope = _host!.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            var outstanding = await db.Matchdays.CountAsync(matchday =>
                matchday.KickoffAt == kickoff
                && matchday.PublicationStatus != MatchdayPublicationStatus.Published);

            if (outstanding == 0)
            {
                return;
            }

            await Task.Delay(250);
        }

        await using var diagnosticScope = _host!.Services.CreateAsyncScope();
        var context = diagnosticScope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var jobs = await context.Jobs
            .Select(job => new { job.JobType, job.Status, job.AttemptCount, job.LastError })
            .ToListAsync();
        var grouped = string.Join(
            ", ",
            jobs.GroupBy(job => $"{job.JobType}/{job.Status}")
                .Select(group => $"{group.Key}={group.Count()}"));
        var errors = string.Join(
            " | ",
            jobs.Where(job => job.LastError is not null)
                .Select(job => $"{job.JobType}: {job.LastError}"));

        throw new TimeoutException(
            $"The round at {kickoff:u} did not publish. Jobs: {grouped}. Errors: {errors}");
    }
}
