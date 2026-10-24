using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Tactical11Manager.Application;
using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Market;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Application.Competition;
using Tactical11Manager.Application.Jobs;
using Tactical11Manager.Application.Squad;
using Tactical11Manager.Application.World;
using Tactical11Manager.Domain.Auth;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.World;
using Tactical11Manager.Domain.World.Generation;
using Tactical11Manager.Infrastructure;
using Tactical11Manager.Infrastructure.Persistence;

namespace Tactical11Manager.Worker.IntegrationTests;

/// <summary>
/// Five consecutive seasons rolled over by the worker alone, and each one reconciled (`PR-1`–`PR-6`,
/// master plan §7.5, §16 Stage 12, ADR-0031, ADR-0035).
/// </summary>
/// <remarks>
/// <para>
/// The stage's last exit criterion: "at least five consecutive automated staging seasons reconcile
/// cleanly". Nothing here calls the rollover use case to do the work — the test enqueues the season's real
/// job (the same business key the calendar's scheduler would use) and the worker claims and executes it, as
/// a staging world would. After every rollover the next season is asserted complete (a full division, a
/// full-length schedule, an opening table), the closing season's projections are reconciled with the
/// same dry run preflight uses, and money, squads, and history are checked.
/// </para>
/// <para>
/// The seasons are played by fabrication, not simulation, exactly as <c>SeasonRolloverTests</c> does: the
/// point of the run is continuity across seasons, and the matchday pipeline and the engine are pinned by
/// their own suites. The world is seeded for real, with tiers 1 and 2 in every country so
/// the movement has an adjacent pair to act between, and two human tenures are attached so the
/// human/AI mix the stage asks for is real.
/// </para>
/// </remarks>
public sealed class StagingSeasonRunTests : IAsyncLifetime
{
    private const int Seasons = 5;

    /// <summary>Every country opens with tiers 1 and 2 (`WORLD-5`).</summary>
    private static readonly int ClubsInWorld = LaunchCountries.All.Count * WorldRuleSet.SeededTiers * WorldRuleSet.ClubsPerDivision;

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("tactical11_staging_run")
        .WithUsername("tactical11_app")
        .WithPassword("integration_test_password")
        .Build();

    private readonly WorkerClock _clock = new();

    private IHost? _host;
    private Guid _worldId;
    private Guid _countryId;
    private Guid _seasonId;

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

            // The rollover scheduler runs, as it would in staging; every other deadline is off so the run is
            // the rollover and nothing else.
            ["Rollover:EnableRollover"] = "true",
            ["Rollover:CheckIntervalSeconds"] = "30",
            ["Matchday:EnableMatchdayWorker"] = "false",
            ["Training:EnableDailyProgression"] = "false",
            ["AiClubs:EnableEvaluation"] = "false",
            ["Finance:EnableWeeklyRun"] = "false",
            ["Fans:EnableWeeklyReview"] = "false",
            ["FreeAgents:EnableBatches"] = "false",
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

        // Last registration wins, so the worker, the schedulers, and the use cases all read the test's clock.
        builder.Services.AddSingleton<IClock>(_clock);

        _host = builder.Build();

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            await db.Database.MigrateAsync();

            var seeded = await scope.ServiceProvider.GetRequiredService<SeedWorld>()
                .ExecuteAsync(new SeedWorldRequest("staging-run-seed"), CancellationToken.None);

            _worldId = seeded.WorldId;

            _seasonId = await db.Seasons
                .Where(season => season.WorldId == _worldId)
                .Select(season => season.Id)
                .SingleAsync();

            _countryId = await db.Countries
                .OrderBy(country => country.SortOrder)
                .Select(country => country.Id)
                .FirstAsync();

            await AttachManagerAsync(scope, tierNumber: 1);
            await AttachManagerAsync(scope, tierNumber: 2);

            // Give every AI club a side (INS-12). The seeding creates no plan, and a provisioning pass may
            // already have supplied one; the evaluation only fills gaps, so this either writes the missing
            // sides or writes nothing, and the run then asserts each survived the five seasons untouched.
            await scope.ServiceProvider.GetRequiredService<EvaluateAiClubs>()
                .ExecuteAsync(CancellationToken.None);
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
    public async Task Five_consecutive_seasons_roll_over_and_reconcile()
    {
        var seasonId = _seasonId;

        for (var closed = 1; closed <= Seasons; closed++)
        {
            await FabricateSeasonAsync(seasonId);

            // The season's own deadline has passed, and so has the play-off's semi-final lock, so no bracket is drawn and the rollover is
            // due: the real job is enqueued and the worker runs the resumable machine (the trigger is the scheduler's own
            // materialisation, ADR-0034).
            _clock.UtcNow = await RolloverDueAtAsync(seasonId);

            await using (var scope = _host!.Services.CreateAsyncScope())
            {
                var trigger =scope.ServiceProvider.GetRequiredService<TriggerSeasonRollover>();
                var triggered = await trigger.ExecuteAsync(seasonId, CancellationToken.None);

                triggered.Outcome.Should().Be(TriggerSeasonRolloverOutcome.Enqueued);
            }

            await WaitForRolloverCompletedAsync(seasonId);

            seasonId = await AssertSeasonReconciledAsync(closed);
        }

        await AssertRunCompleteAsync();
    }

    /// <summary>Runs a season's rollover a second time and asserts it moves nothing twice (ADR-0031).</summary>
    [Fact]
    public async Task A_redelivered_rollover_in_the_run_moves_nothing_twice()
    {
        await FabricateSeasonAsync(_seasonId);
        _clock.UtcNow = await RolloverDueAtAsync(_seasonId);

        await using (var scope = _host!.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<TriggerSeasonRollover>()
                .ExecuteAsync(_seasonId, CancellationToken.None);
        }

        await WaitForRolloverCompletedAsync(_seasonId);

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var again = await scope.ServiceProvider.GetRequiredService<RunSeasonRollover>()
                .ExecuteAsync(_seasonId, CancellationToken.None);

            again.Outcome.Should().Be(SeasonRolloverOutcome.AlreadyCompleted);
        }

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            (await db.Seasons.CountAsync(season => season.WorldId == _worldId))
                .Should()
                .Be(2, "the second delivery created no third season");
            (await db.ClubSeasonEntries.CountAsync(entry => entry.SeasonId == _seasonId && entry.IsPromoted))
                .Should()
                .Be(LaunchCountries.All.Count * WorldRuleSet.DirectPromotionPlaces, "the movement was not re-applied");
        }
    }

    /// <summary>The rollover has completed; reads the next season, checks it is complete, and returns its id.</summary>
    private async Task<Guid> AssertSeasonReconciledAsync(int closedSequenceNumber)
    {
        Guid nextSeasonId;

        await using (var scope = _host!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            var world = await db.GameWorlds.SingleAsync();
            world.CurrentSeasonNumber.Should()
                .Be(closedSequenceNumber + 1, "TIME-3: the game year advanced exactly once");

            var closing = await db.Seasons.SingleAsync(season => season.WorldId == _worldId
                && season.SequenceNumber == closedSequenceNumber);
            closing.Status.Should().Be(SeasonStatus.Completed, "PR-6: history from here is immutable");

            var next = await db.Seasons.SingleAsync(season => season.WorldId == _worldId
                && season.SequenceNumber == closedSequenceNumber + 1);
            next.Status.Should().Be(SeasonStatus.Active, "onboarding reopens in the next season");
            next.GameYear.Should().Be(closing.GameYear + 1);
            SeasonCalendar.IsMatchday(DateOnly.FromDateTime(next.StartsAt.UtcDateTime))
                .Should()
                .BeTrue("CAL-6: the next season starts on the first matchday after the rollover window");

            nextSeasonId = next.Id;

            // Every active tier begins the new season complete: a full division, a full-length schedule,
            // and an opening table (PR-1..PR-5, CAL-1).
            var nextDivisionSeasons = await db.DivisionSeasons
                .Where(divisionSeason => divisionSeason.SeasonId == next.Id)
                .ToListAsync();

            nextDivisionSeasons.Should().HaveCount(LaunchCountries.All.Count * WorldRuleSet.SeededTiers, "two seeded tiers in each of nine countries");

            foreach (var divisionSeason in nextDivisionSeasons)
            {
                (await db.ClubSeasonEntries.CountAsync(entry => entry.DivisionSeasonId == divisionSeason.Id))
                    .Should()
                    .Be(WorldRuleSet.ClubsPerDivision);
                (await db.Matchdays.CountAsync(matchday => matchday.DivisionSeasonId == divisionSeason.Id))
                    .Should()
                    .Be(WorldRuleSet.MatchdaysPerSeason, "CAL-1");
                (await db.Fixtures.CountAsync(fixture =>
                    db.Matchdays.Any(matchday => matchday.Id == fixture.MatchdayId
                        && matchday.DivisionSeasonId == divisionSeason.Id)))
                    .Should()
                    .Be(WorldRuleSet.MatchdaysPerSeason * (WorldRuleSet.ClubsPerDivision / 2));
                (await db.Standings.CountAsync(standing => standing.DivisionSeasonId == divisionSeason.Id))
                    .Should()
                    .Be(WorldRuleSet.ClubsPerDivision);
            }

            // The closing season's projections reconcile, the preflight the rollover's own freeze phase runs
            // (TBL-13) — recomputed here from the published results the fabricated play left behind.
            var closingDivisionSeasonIds = await db.DivisionSeasons
                .Where(divisionSeason => divisionSeason.SeasonId == closing.Id)
                .Select(divisionSeason => divisionSeason.Id)
                .ToListAsync();

            var rebuild = scope.ServiceProvider.GetRequiredService<RebuildDivisionProjections>();

            foreach (var divisionSeasonId in closingDivisionSeasonIds)
            {
                var reconciled = await rebuild.ExecuteAsync(divisionSeasonId, apply: false, CancellationToken.None);

                reconciled.Outcome.Should().Be(ProjectionRebuildOutcome.Reconciled);
            }

            // Two up and two down between the one adjacent pair (no bracket was drawn); every closing entry carries its rank, and
            // the played season's results stay the record of what happened (PR-1, PR-4, PR-6).
            var closingEntries = await db.ClubSeasonEntries
                .Where(entry => entry.SeasonId == closing.Id)
                .ToListAsync();

            closingEntries.Should().OnlyContain(entry => entry.FinalRank != null, "every entry was closed");
            closingEntries.Count(entry => entry.IsPromoted).Should().Be(LaunchCountries.All.Count * WorldRuleSet.DirectPromotionPlaces);
            closingEntries.Count(entry => entry.IsRelegated).Should().Be(LaunchCountries.All.Count * WorldRuleSet.DirectRelegationPlaces);

            var closingFixtures = await db.Fixtures.CountAsync(fixture =>
                fixture.Status == FixtureStatus.Published
                && db.Matchdays.Any(matchday => matchday.Id == fixture.MatchdayId
                    && closingDivisionSeasonIds.Contains(matchday.DivisionSeasonId)));

            closingFixtures.Should()
                .Be(closingDivisionSeasonIds.Count * WorldRuleSet.MatchdaysPerSeason
                    * (WorldRuleSet.ClubsPerDivision / 2));

            // The season's money is settled once (FIN-5, FIN-19).
            (await db.ClubSeasonFinances.CountAsync(summary => summary.SeasonId == closing.Id))
                .Should()
                .Be(ClubsInWorld, "every closing club has a finance summary");
            (await db.LedgerEntries.CountAsync(entry => entry.Category == LedgerCategory.PositionAward))
                .Should()
                .Be(ClubsInWorld * closedSequenceNumber, "every closing club earns a position award each season");
        }

        return nextSeasonId;
    }

    /// <summary>The run's end state: six seasons, five completed rollovers, and every invariant intact.</summary>
    private async Task AssertRunCompleteAsync()
    {
        await using var scope = _host!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        (await db.GameWorlds.SingleAsync()).CurrentSeasonNumber.Should().Be(Seasons + 1);
        (await db.Seasons.CountAsync(season => season.WorldId == _worldId)).Should().Be(Seasons + 1);
        (await db.Seasons.CountAsync(season => season.WorldId == _worldId
            && season.Status == SeasonStatus.Completed)).Should().Be(Seasons);
        (await db.SeasonRollovers.CountAsync(row => row.WorldId == _worldId
            && row.Phase == SeasonRolloverPhase.Completed)).Should().Be(Seasons);

        // Two up and two down in every country every season, and never a duplicate.
        (await db.ClubSeasonEntries.CountAsync(entry => entry.IsPromoted)).Should().Be(Seasons * LaunchCountries.All.Count * WorldRuleSet.DirectPromotionPlaces);
        (await db.ClubSeasonEntries.CountAsync(entry => entry.IsRelegated)).Should().Be(Seasons * LaunchCountries.All.Count * WorldRuleSet.DirectRelegationPlaces);

        // Every club still has a legal squad (SQ-2, SQ-8).
        var roster = scope.ServiceProvider.GetRequiredService<IRosterQueries>();
        var clubIds = await db.Clubs.OrderBy(club => club.Id).Select(club => club.Id).ToListAsync();

        foreach (var clubId in clubIds)
        {
            var composition = await roster.GetCompositionAsync(clubId, CancellationToken.None);

            composition.Should().NotBeNull();
            SquadLegality.IsWithinBounds(composition!.RegisteredCount)
                .Should()
                .BeTrue("SQ-2: every club stays within the registered-squad bounds");
            composition.RegisteredCount.Should().BeGreaterThanOrEqualTo(WorldRuleSet.SquadMinimumRegistered);
            composition.GoalkeeperCount.Should().BeGreaterThanOrEqualTo(WorldRuleSet.MinimumGoalkeepers);
        }

        // Every account's balances replay from its ledger, and none went negative (FIN-18, FIN-13).
        var accounts = await db.ClubAccounts.ToListAsync();
        var ledger = await db.LedgerEntries.ToListAsync();
        var byClub = ledger.GroupBy(entry => entry.ClubId).ToDictionary(group => group.Key, group => group.ToList());

        accounts.Should().HaveCount(ClubsInWorld);
        accounts.Should().OnlyContain(account => account.CashMinor >= 0 && account.ReservedMinor >= 0);

        foreach (var account in accounts)
        {
            var entries = byClub[account.ClubId];

            entries.Sum(entry => entry.CashDeltaMinor).Should().Be(account.CashMinor);
            entries.Sum(entry => entry.ReservedDeltaMinor).Should().Be(account.ReservedMinor);
        }

        // The human/AI mix did what the rules say: unmanaged clubs renewed, and the board renewed a present manager's expiring players.
        (await db.PlayerContracts.CountAsync(contract => contract.Status == ContractStatus.Closed
            && contract.ClosedReason == PlayerContractCloseReasons.Renewed))
            .Should()
            .BeGreaterThan(0, "CON-6: an unmanaged club renews its expiring players");
        (await db.PlayerContracts.CountAsync(contract => contract.Status == ContractStatus.Closed
            && contract.ClosedReason == PlayerContractCloseReasons.Expired
            && db.ClubTenures.Any(tenure => tenure.ClubId == contract.ClubId
                && tenure.ControlStatus == ClubTenureControlStatus.Active)))
            .Should()
            .Be(0, "CON-11: the board renews a present manager's expiring player, so none is lost");

        // Every AI club still has the default side the AI supplied (INS-12); the two held clubs are the
        // manager's, and the run created no new clubs, so the plans written before it survive it.
        var aiClubIds = await db.Clubs
            .Where(club => !db.ClubTenures.Any(tenure => tenure.ClubId == club.Id
                && tenure.ControlStatus == ClubTenureControlStatus.Active))
            .Select(club => club.Id)
            .ToListAsync();
        var plannedClubIds = await db.TacticalPlans
            .Where(plan => plan.IsDefault && aiClubIds.Contains(plan.ClubId))
            .Select(plan => plan.ClubId)
            .Distinct()
            .ToListAsync();

        plannedClubIds.Should().BeEquivalentTo(aiClubIds, "INS-12: every club with no active tenure has a side");

        // Nothing the run enqueued was left dead-lettered.
        (await db.Jobs.CountAsync(job => job.JobType == SeasonRolloverJobTypes.Rollover
            && job.Status == "dead_letter")).Should().Be(0);
    }

    /// <summary>Marks every matchday of a season published deterministically and rebuilds the projections.</summary>
    private async Task FabricateSeasonAsync(Guid seasonId)
    {
        await using var scope = _host!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
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

        var rebuild = scope.ServiceProvider.GetRequiredService<RebuildDivisionProjections>();

        foreach (var divisionSeasonId in divisionSeasonIds)
        {
            var reconciled = await rebuild.ExecuteAsync(divisionSeasonId, apply: true, CancellationToken.None);

            reconciled.Outcome.Should().BeOneOf(
                ProjectionRebuildOutcome.Rebuilt,
                ProjectionRebuildOutcome.Reconciled);
        }
    }

    /// <summary>
    /// The first instant the season has nothing left to wait for: the semi-finals' team sheets have locked, so a league that finished
    /// without a bracket moves its direct places only (a late pair, `PR-4`).
    /// </summary>
    private async Task<DateTimeOffset> RolloverDueAtAsync(Guid seasonId)
    {
        await using var scope = _host!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var startsAt = await db.Seasons.Where(season => season.Id == seasonId).Select(season => season.StartsAt).SingleAsync();

        return SeasonCalendar.PlayoffKickoffs(DateOnly.FromDateTime(startsAt.UtcDateTime))[0].AddHours(1);
    }

    /// <summary>Waits for the season's rollover job to reach a terminal state, reporting a dead letter.</summary>
    private async Task WaitForRolloverCompletedAsync(Guid seasonId)
    {
        var businessKey = SeasonRolloverJobTypes.RolloverKey(seasonId);
        var deadline = DateTimeOffset.UtcNow.AddMinutes(3);
        var lastSeen = "no job";

        while (DateTimeOffset.UtcNow < deadline)
        {
            await using (var scope = _host!.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

                var job = await db.Jobs
                    .Where(candidate => candidate.BusinessKey == businessKey)
                    .Select(candidate => new { candidate.Status, candidate.LastError })
                    .FirstOrDefaultAsync();

                lastSeen = job is null ? "no job" : $"{job.Status}: {job.LastError}";

                if (job is null)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250));
                    continue;
                }

                if (job.Status == "completed")
                {
                    return;
                }

                job.Status.Should().NotBe("dead_letter", $"the rollover job failed: {job.LastError}");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        throw new TimeoutException($"The rollover for season {seasonId} did not complete in time ({lastSeen}).");
    }

    /// <summary>Attaches a fresh manager to the first club of a tier, so a human holds a club in the run.</summary>
    private async Task AttachManagerAsync(AsyncServiceScope scope, int tierNumber)
    {
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var now = _clock.UtcNow;

        var clubId = await (
            from entry in db.ClubSeasonEntries
            join divisionSeason in db.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
            join division in db.Divisions on divisionSeason.DivisionId equals division.Id
            where division.CountryId == _countryId
                && division.TierNumber == tierNumber
                && divisionSeason.SeasonId == _seasonId
            orderby entry.ClubId
            select entry.ClubId)
            .FirstAsync();

        var userId = Guid.CreateVersion7();
        var suffix = userId.ToString("N")[..12];

        var user = User.Register(
            userId,
            $"{suffix}@example.com",
            $"Tier{tierNumber}{suffix}",
            "hash",
            $"stamp-{suffix}",
            now);
        user.MarkEmailVerified(now);
        db.Users.Add(user);

        var managerId = Guid.CreateVersion7();
        db.Managers.Add(Manager.Create(managerId, userId, "en-GB", "Europe/London", now));

        db.ClubTenures.Add(ClubTenure.Start(
            Guid.CreateVersion7(),
            clubId,
            managerId,
            $"staging-run-{suffix}",
            now));

        await db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>A clock the test drives, so a season's deadline months away is due now (TIME-2).</summary>
    private sealed class WorkerClock : IClock
    {
        /// <inheritdoc />
        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
    }
}
