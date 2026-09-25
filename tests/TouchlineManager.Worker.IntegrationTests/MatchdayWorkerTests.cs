using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using TouchlineManager.Application;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.World;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Infrastructure;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Worker.IntegrationTests;

/// <summary>
/// A complete matchday, run by the worker alone: the calendar becomes jobs, the jobs lock, simulate, and
/// publish, and the table moves (`CAL-3`, `MAT-7`, `TBL-13`, master plan §7.2–§7.4).
/// </summary>
/// <remarks>
/// <para>
/// Nothing here calls a use case. The scheduler materialises the round's deadlines from the seeded calendar,
/// the queue claims them, and the handlers do the work — which is the property the whole stage rests on: the
/// database row is the deadline and the worker is the only thing that advances it (ADR-0001, ADR-0003).
/// </para>
/// <para>
/// The clock is the test's, set past the first kickoff so a round that would otherwise be days away is due
/// now. That is the same trick every deadline test needs, and it is why the clock is injected rather than
/// read (TIME-2).
/// </para>
/// </remarks>
public sealed class MatchdayWorkerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("touchline")
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
            ["Matchday:EnableMatchdayWorker"] = "true",
            ["Matchday:CheckIntervalSeconds"] = "30",
            ["Matchday:MaterializeHorizonDays"] = "21",

            // The season starts on a matchday the clock is then moved past, so the first round is due the
            // moment the worker looks at the calendar.
            ["World:FirstSeasonStartDate"] = "2026-10-06",
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

            await seeder.ExecuteAsync(new SeedWorldRequest("worker-matchday-seed"), CancellationToken.None);

            // The clock moves to a minute after the season's first kickoff, so the round that would otherwise
            // be days away is due now — and its lock is already late, which is the state a redeploy leaves.
            var firstKickoff = await db.Matchdays.MinAsync(matchday => matchday.KickoffAt);

            _clock.UtcNow = firstKickoff.AddMinutes(1);
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
    public async Task The_worker_locks_simulates_and_publishes_a_whole_matchday()
    {
        var published = await WaitForPublishedRoundAsync(TimeSpan.FromMinutes(3));

        published.Should().BeTrue(
            "the scheduler materialises the round, the queue claims it, and the handlers lock, simulate, and publish it");

        await using var scope = _host!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var matchday = await db.Matchdays
            .Where(candidate => candidate.RoundNumber == 1
                && candidate.PublicationStatus == MatchdayPublicationStatus.Published)
            .OrderBy(candidate => candidate.KickoffAt)
            .FirstAsync();

        var fixtures = await db.Fixtures
            .Where(fixture => fixture.MatchdayId == matchday.Id)
            .ToListAsync();

        fixtures.Should().HaveCount(9);
        fixtures.Should().OnlyContain(fixture => fixture.Status == FixtureStatus.Published);
        fixtures.Should().OnlyContain(fixture => fixture.HomeScore != null && fixture.AwayScore != null);
        fixtures.Should().OnlyContain(fixture => fixture.MatchId != null);

        var matchIds = fixtures.Select(fixture => fixture.MatchId!.Value).ToList();

        (await db.Matches.CountAsync(match => matchIds.Contains(match.Id))).Should().Be(9);
        (await db.InputSnapshots.CountAsync(snapshot => fixtures.Select(f => f.Id).Contains(snapshot.FixtureId)))
            .Should()
            .Be(9);

        var standings = await db.Standings
            .Where(standing => standing.DivisionSeasonId == matchday.DivisionSeasonId)
            .OrderBy(standing => standing.Rank)
            .ToListAsync();

        standings.Should().HaveCount(18);
        standings.Sum(standing => standing.Played).Should().Be(18, "every club in the division played once");
        standings.Select(standing => standing.Rank).Should().Equal(Enumerable.Range(1, 18));

        // The three jobs the round needed all reached a terminal state, which is what "the worker advanced the
        // deadline" means when the evidence is read back (ADR-0003). The lock and the resolution come due at
        // the same instant in this arrangement, so which of them runs first is not fixed — the resolver takes
        // the snapshot itself when it wins the race (§7.3) — and the assertion waits for both to finish.
        var matchdayKeyPart = $"matchday:{matchday.Id:D}";

        (await WaitForJobsCompletedAsync(matchdayKeyPart, TimeSpan.FromSeconds(30)))
            .Should()
            .BeTrue("every job the round needed must reach a terminal state");

        var jobs = await db.Jobs
            .Where(job => job.BusinessKey.StartsWith(matchdayKeyPart))
            .Select(job => new { job.JobType, job.Status })
            .ToListAsync();

        jobs.Should().HaveCount(3, "a round is locked, resolved, and published by exactly one job each");
        jobs.Select(job => job.JobType).Should().BeEquivalentTo(
        [
            "competition.lock-matchday",
            "competition.resolve-matchday",
            "competition.publish-matchday",
        ]);
        jobs.Should().OnlyContain(job => job.Status == "completed");
    }

    /// <summary>Waits for every job of one round to be completed.</summary>
    private async Task<bool> WaitForJobsCompletedAsync(string matchdayKeyPart, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = _host!.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            var completed = await db.Jobs.CountAsync(
                job => job.BusinessKey.StartsWith(matchdayKeyPart) && job.Status == "completed");

            if (completed == 3)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        return false;
    }

    /// <summary>Waits for the worker to publish any one round of the seeded season.</summary>
    private async Task<bool> WaitForPublishedRoundAsync(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = _host!.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            var published = await db.Matchdays.AnyAsync(
                matchday => matchday.RoundNumber == 1
                    && matchday.PublicationStatus == MatchdayPublicationStatus.Published);

            if (published)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        return false;
    }

    /// <summary>A clock the test drives, so a deadline days away is due now (TIME-2).</summary>
    private sealed class WorkerClock : IClock
    {
        /// <inheritdoc />
        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
    }
}
