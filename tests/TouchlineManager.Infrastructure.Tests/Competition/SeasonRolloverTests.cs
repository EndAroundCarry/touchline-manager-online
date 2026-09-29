using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Application.Competition;
using TouchlineManager.Application.World;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Finance;
using TouchlineManager.Domain.Squad;
using TouchlineManager.Domain.World;
using TouchlineManager.Domain.World.Generation;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.Infrastructure.Tests.World;

namespace TouchlineManager.Infrastructure.Tests.Competition;

/// <summary>
/// One season's rollover against real PostgreSQL 17 (`PR-1`…`PR-6`, master plan §7.5, ADR-0031).
/// </summary>
/// <remarks>
/// <para>
/// Its own world per test, because a rollover rewrites which tier every club plays in and the shared world
/// collection asserts what a freshly seeded pyramid looks like. The arrangement is the real one: a seeded
/// world with a provisioned second tier, every matchday of the closing season published, and the worker's
/// use case — not a hand-built shortcut — doing the movement.
/// </para>
/// <para>
/// Publication is fabricated rather than simulated, and that is a deliberate cost choice: the point of this
/// suite is movement, history, and resumability, not the engine (whose determinism Stage 5 pins), so the
/// closing season's fixtures are marked published with a deterministic score and the standings are rebuilt
/// from them with the same tool the live publication uses (`TBL-13`). That keeps a two-tier rollover a few
/// seconds rather than a few minutes of simulated football.
/// </para>
/// </remarks>
public sealed class SeasonRolloverTests : IAsyncLifetime, IDisposable
{
    private readonly RolloverFixture _fixture = new();

    /// <summary>Starts a freshly seeded world with a provisioned second tier.</summary>
    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var season = await db.Seasons.SingleAsync(candidate => candidate.WorldId == _fixture.WorldId);
        var countryId = await db.Countries
            .OrderBy(country => country.SortOrder)
            .Select(country => country.Id)
            .FirstAsync();

        var request = DivisionProvisioningRequest.Request(
            Guid.CreateVersion7(),
            countryId,
            targetTier: 2,
            season.Id,
            $"rollover-test-{countryId:N}",
            _fixture.Clock.UtcNow);

        scope.ServiceProvider.GetRequiredService<IDivisionProvisioningRequestRepository>().Add(request);
        await db.SaveChangesAsync(CancellationToken.None);

        await scope.ServiceProvider.GetRequiredService<ProvisionDivision>()
            .ExecuteAsync(request.Id, CancellationToken.None);
    }

    /// <summary>Stops and removes the world.</summary>
    public Task DisposeAsync() => _fixture.DisposeAsync();

    /// <inheritdoc />
    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task A_finished_season_rolls_over_promoting_and_relegating_and_opening_the_next_season()
    {
        Guid seasonId;
        Guid countryId;
        Guid tierOneDivisionId;
        Guid tierTwoDivisionId;

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            seasonId = await db.Seasons
                .Where(candidate => candidate.WorldId == _fixture.WorldId)
                .Select(candidate => candidate.Id)
                .SingleAsync();
            countryId = await db.Countries
                .OrderBy(country => country.SortOrder)
                .Select(country => country.Id)
                .FirstAsync();
            tierOneDivisionId = await db.Divisions
                .Where(division => division.CountryId == countryId && division.TierNumber == 1)
                .Select(division => division.Id)
                .SingleAsync();
            tierTwoDivisionId = await db.Divisions
                .Where(division => division.CountryId == countryId && division.TierNumber == 2)
                .Select(division => division.Id)
                .SingleAsync();
        }

        await using (var scope = _fixture.CreateScope())
        {
            var rollover = scope.ServiceProvider.GetRequiredService<RunSeasonRollover>();

            // The season has not been played yet, so preflight refuses it. The refusal is transient — the
            // exception is not a permanent job failure — and the rollover row is left at its first checkpoint
            // for the retry to resume.
            await FluentActions
                .Awaiting(() => rollover.ExecuteAsync(seasonId, CancellationToken.None))
                .Should()
                .ThrowAsync<InvalidOperationException>("PR-4: an unplayed season cannot roll over");

            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
            var rolloverRow = await db.SeasonRollovers.SingleAsync(row => row.SeasonId == seasonId);

            rolloverRow.Phase.Should().Be(SeasonRolloverPhase.Started, "the freeze never ran");
            (await db.Seasons.SingleAsync(candidate => candidate.Id == seasonId)).Status
                .Should().Be(SeasonStatus.Active, "claims are still open because the season did not freeze");
        }

        await PublishSeasonAsync(seasonId);

        // The expected movement, read from the closing standings the fabricated results produced.
        List<Guid> tierOneOrder;
        List<Guid> tierTwoOrder;

        await using (var scope = _fixture.CreateScope())
        {
            tierOneOrder = await RankedClubsAsync(scope, tierOneDivisionId, seasonId);
            tierTwoOrder = await RankedClubsAsync(scope, tierTwoDivisionId, seasonId);
        }

        var expectedPromoted = tierTwoOrder.Take(3).ToList();
        var expectedRelegated = tierOneOrder.TakeLast(3).ToList();

        SeasonRolloverResult result;

        await using (var scope = _fixture.CreateScope())
        {
            result = await scope.ServiceProvider.GetRequiredService<RunSeasonRollover>()
                .ExecuteAsync(seasonId, CancellationToken.None);
        }

        result.Outcome.Should().Be(SeasonRolloverOutcome.Completed);
        result.NextSeasonId.Should().NotBeNull();
        result.Promotions.Should().Be(3, "PR-1: three up between the two tiers");
        result.Relegations.Should().Be(3, "PR-1: three down");

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
            var world = await db.GameWorlds.SingleAsync();
            var closing = await db.Seasons.SingleAsync(candidate => candidate.Id == seasonId);
            var next = await db.Seasons.SingleAsync(candidate => candidate.Id == result.NextSeasonId!.Value);

            world.CurrentSeasonNumber.Should().Be(2, "TIME-3: the game year advances at rollover");
            closing.Status.Should().Be(SeasonStatus.Completed, "PR-6: history from here is immutable");
            next.SequenceNumber.Should().Be(2);
            next.GameYear.Should().Be(closing.GameYear + 1);
            next.Status.Should().Be(SeasonStatus.Active, "onboarding reopens in the next season");
            SeasonCalendar.IsMatchday(DateOnly.FromDateTime(next.StartsAt.UtcDateTime))
                .Should()
                .BeTrue("CAL-6: the next season starts on the first matchday after the rollover window");

            // Every active tier has a next-season instance holding eighteen clubs, a thirty-four-round
            // schedule, and an opening table.
            var nextDivisionSeasons = await db.DivisionSeasons
                .Where(divisionSeason => divisionSeason.SeasonId == next.Id)
                .ToListAsync();

            nextDivisionSeasons.Should().HaveCount(7, "six seeded tiers and the provisioned second tier");

            foreach (var divisionSeason in nextDivisionSeasons)
            {
                (await db.ClubSeasonEntries.CountAsync(entry => entry.DivisionSeasonId == divisionSeason.Id))
                    .Should()
                    .Be(18, "PR-1: a tier holds eighteen clubs");
                (await db.Matchdays.CountAsync(matchday => matchday.DivisionSeasonId == divisionSeason.Id))
                    .Should()
                    .Be(34, "CAL-1");
                (await db.Standings.CountAsync(standing => standing.DivisionSeasonId == divisionSeason.Id))
                    .Should()
                    .Be(18);
            }

            // The closing entries carry their final rank and movement flags exactly once (PR-4).
            var closingEntries = await db.ClubSeasonEntries
                .Where(entry => entry.SeasonId == seasonId)
                .ToListAsync();

            closingEntries.Should().OnlyContain(entry => entry.FinalRank != null, "every entry was closed");
            closingEntries.Count(entry => entry.IsPromoted).Should().Be(3);
            closingEntries.Count(entry => entry.IsRelegated).Should().Be(3);

            foreach (var clubId in expectedPromoted)
            {
                closingEntries.Single(entry => entry.ClubId == clubId).IsPromoted.Should().BeTrue();
            }

            foreach (var clubId in expectedRelegated)
            {
                closingEntries.Single(entry => entry.ClubId == clubId).IsRelegated.Should().BeTrue();
            }

            // The promoted clubs sit in the next season's top tier and the relegated ones in the second.
            var nextTierOneSeason = await db.DivisionSeasons.SingleAsync(candidate =>
                candidate.DivisionId == tierOneDivisionId && candidate.SeasonId == next.Id);
            var nextTierTwoSeason = await db.DivisionSeasons.SingleAsync(candidate =>
                candidate.DivisionId == tierTwoDivisionId && candidate.SeasonId == next.Id);

            var nextTierOneClubs = await db.ClubSeasonEntries
                .Where(entry => entry.DivisionSeasonId == nextTierOneSeason.Id)
                .Select(entry => entry.ClubId)
                .ToListAsync();
            var nextTierTwoClubs = await db.ClubSeasonEntries
                .Where(entry => entry.DivisionSeasonId == nextTierTwoSeason.Id)
                .Select(entry => entry.ClubId)
                .ToListAsync();

            nextTierOneClubs.Should().Contain(expectedPromoted, "PR-3: movement follows the club");
            nextTierOneClubs.Should().NotContain(expectedRelegated);
            nextTierTwoClubs.Should().Contain(expectedRelegated);
            nextTierTwoClubs.Should().NotContain(expectedPromoted);

            // The closing season's own schedule is untouched history (PR-6).
            var closingMatchdayIds = await db.Matchdays
                .Where(matchday => matchday.DivisionSeasonId ==
                    db.DivisionSeasons.Single(ds => ds.DivisionId == tierOneDivisionId && ds.SeasonId == seasonId).Id)
                .Select(matchday => matchday.Id)
                .ToListAsync();

            (await db.Fixtures.CountAsync(fixture => closingMatchdayIds.Contains(fixture.MatchdayId)
                && fixture.Status == FixtureStatus.Published))
                .Should()
                .Be(306, "the played season's results are the record of what happened");

            // The rollover settles the closing season's money (FIN-5, master plan §6.8): one final-position
            // award and one finance summary for every closing club.
            (await db.LedgerEntries.CountAsync(entry => entry.Category == LedgerCategory.PositionAward))
                .Should()
                .Be(126, "every closing club earns a final-position award");
            (await db.ClubSeasonFinances.CountAsync(summary => summary.SeasonId == seasonId))
                .Should()
                .Be(126, "every closing club has a season finance summary");

            // The clubs are unmanaged, so the AI renewed their expiring contracts (CON-6).
            (await db.PlayerContracts.CountAsync(contract =>
                contract.Status == ContractStatus.Closed
                && contract.ClosedReason == PlayerContractCloseReasons.Renewed))
                .Should()
                .BeGreaterThan(0, "an unmanaged club renews its expiring players");
        }

        // A redelivered rollover is a no-op: the next season stays the second one and nothing moves twice.
        await using (var scope = _fixture.CreateScope())
        {
            var again = await scope.ServiceProvider.GetRequiredService<RunSeasonRollover>()
                .ExecuteAsync(seasonId, CancellationToken.None);

            again.Outcome.Should().Be(SeasonRolloverOutcome.AlreadyCompleted);
        }

        await using (var final = _fixture.CreateScope())
        {
            var db = final.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            (await db.GameWorlds.SingleAsync()).CurrentSeasonNumber.Should().Be(2, "the year advanced once");
            (await db.Seasons.CountAsync(candidate => candidate.WorldId == _fixture.WorldId))
                .Should()
                .Be(2, "exactly one next season was created");
            (await db.SeasonRollovers.CountAsync(row => row.SeasonId == seasonId)).Should().Be(1);
            (await db.ClubSeasonEntries.CountAsync(entry => entry.SeasonId == seasonId && entry.IsPromoted))
                .Should()
                .Be(3, "the movement was not re-applied");
        }
    }

    /// <summary>Marks every matchday of a season published with a deterministic score and rebuilds the tables.</summary>
    private async Task PublishSeasonAsync(Guid seasonId)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var now = _fixture.Clock.UtcNow;

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

        // The table is a projection of published results (TBL-13), so it is rebuilt from the fabricated ones
        // exactly as the live publication would, and preflight then reconciles.
        var rebuild = scope.ServiceProvider.GetRequiredService<RebuildDivisionProjections>();

        foreach (var divisionSeasonId in divisionSeasonIds)
        {
            var reconciled = await rebuild.ExecuteAsync(divisionSeasonId, apply: true, CancellationToken.None);

            reconciled.Outcome.Should().Be(ProjectionRebuildOutcome.Rebuilt);
        }
    }

    private static async Task<List<Guid>> RankedClubsAsync(
        AsyncServiceScope scope,
        Guid divisionId,
        Guid seasonId)
    {
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var divisionSeasonId = await db.DivisionSeasons
            .Where(divisionSeason => divisionSeason.DivisionId == divisionId && divisionSeason.SeasonId == seasonId)
            .Select(divisionSeason => divisionSeason.Id)
            .SingleAsync();

        return await db.Standings
            .Where(standing => standing.DivisionSeasonId == divisionSeasonId)
            .OrderBy(standing => standing.Rank)
            .Select(standing => standing.ClubId)
            .ToListAsync();
    }
}

/// <summary>The rollover tests' own seeded world.</summary>
public sealed class RolloverFixture : WorldFixture
{
    /// <summary>Creates the fixture against its own database.</summary>
    public RolloverFixture()
        : base("touchline_rollover")
    {
    }
}
