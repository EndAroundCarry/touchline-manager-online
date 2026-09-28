using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Application.Competition;
using TouchlineManager.Application.World;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Squad;
using TouchlineManager.Domain.World;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.Infrastructure.Tests.World;

namespace TouchlineManager.Infrastructure.Tests.World;

/// <summary>
/// Automatic pyramid growth against real PostgreSQL 17 (`PYR-4`…`PYR-8`, `PYR-11`, ADR-0005).
/// </summary>
/// <remarks>
/// Its own world per test, because provisioning adds a whole tier and the shared world collection's tests
/// assert what a freshly seeded pyramid looks like. Each test runs the real worker use case, not the
/// materialiser, so it exercises generation, backfill, validation, and activation exactly as the worker does.
/// </remarks>
public sealed class ProvisioningTests : IAsyncLifetime, IDisposable
{
    private readonly ProvisioningFixture _fixture = new();

    /// <summary>Starts a freshly seeded world.</summary>
    public Task InitializeAsync() => _fixture.InitializeAsync();

    /// <summary>Stops and removes the world.</summary>
    public Task DisposeAsync() => _fixture.DisposeAsync();

    /// <inheritdoc />
    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Provisioning_generates_and_activates_a_claimable_tier()
    {
        Guid countryId;
        Guid requestId;

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
            var world = await db.GameWorlds.SingleAsync();
            var season = await db.Seasons.SingleAsync(candidate => candidate.WorldId == world.Id);

            countryId = await db.Countries.OrderBy(country => country.SortOrder).Select(c => c.Id).FirstAsync();
            requestId = await CreateRequestAsync(scope, countryId, targetTier: 2, season.Id);
        }

        ProvisionDivisionResult result;

        await using (var scope = _fixture.CreateScope())
        {
            result = await scope.ServiceProvider.GetRequiredService<ProvisionDivision>()
                .ExecuteAsync(requestId, CancellationToken.None);
        }

        result.Outcome.Should().Be(ProvisionDivisionOutcome.Completed);
        result.TargetTier.Should().Be(2);
        result.Clubs.Should().Be(18, "PYR-4: a tier holds eighteen clubs");
        result.Players.Should().Be(18 * 22, "SQ-1: the generator produces twenty-two players per club");
        result.Accounts.Should().Be(18);

        await using (var verify = _fixture.CreateScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            var division = await db.Divisions.SingleAsync(candidate =>
                candidate.CountryId == countryId && candidate.TierNumber == 2);

            division.Status.Should().Be(DivisionStatus.Active, "PYR-8: the tier is claimable once validated");

            var divisionSeason = await db.DivisionSeasons.SingleAsync(candidate => candidate.DivisionId == division.Id);

            (await db.ClubSeasonEntries.CountAsync(entry => entry.DivisionSeasonId == divisionSeason.Id))
                .Should().Be(18);
            (await db.Matchdays.CountAsync(matchday => matchday.DivisionSeasonId == divisionSeason.Id))
                .Should().Be(34, "CAL-1");
            (await db.Fixtures.CountAsync(fixture => fixture.MatchdayId != Guid.Empty
                && db.Matchdays.Any(m => m.Id == fixture.MatchdayId && m.DivisionSeasonId == divisionSeason.Id)))
                .Should().Be(306, "CAL-9: thirty-four rounds of nine fixtures");

            // Every new club registers a legal squad with two goalkeepers (SQ-2).
            foreach (var clubId in await db.ClubSeasonEntries
                .Where(entry => entry.DivisionSeasonId == divisionSeason.Id)
                .Select(entry => entry.ClubId)
                .ToListAsync())
            {
                var contracts = await db.PlayerContracts
                    .CountAsync(contract => contract.ClubId == clubId && contract.Status == ContractStatus.Active);

                contracts.Should().BeGreaterThanOrEqualTo(18, "SQ-2");
            }

            var request = await db.DivisionProvisioningRequests.SingleAsync(candidate => candidate.Id == requestId);
            request.Status.Should().Be(ProvisioningRequestStatus.Completed);

            (await db.GenerationRuns.CountAsync(run => run.Kind == GenerationRunKind.DivisionProvisioning))
                .Should().Be(1, "PYR-14: a provisioning run is recorded once");
        }

        // The tier is now the country's lowest active tier, so its clubs are offerable (WORLD-8, PYR-10).
        await using (var onboarding = _fixture.CreateScope())
        {
            var capacity = await onboarding.ServiceProvider.GetRequiredService<IOnboardingQueries>()
                .GetCountryCapacityAsync(countryId, CancellationToken.None);

            capacity!.LowestActiveTier.Should().Be(2, "PYR-8: a claim now joins the new tier");
        }

        // A retried run is a no-op: the request is terminal and no second tier exists (ADR-0003).
        await using (var retry = _fixture.CreateScope())
        {
            var again = await retry.ServiceProvider.GetRequiredService<ProvisionDivision>()
                .ExecuteAsync(requestId, CancellationToken.None);

            again.Outcome.Should().Be(ProvisionDivisionOutcome.AlreadyCompleted);
        }

        await using (var final = _fixture.CreateScope())
        {
            var db = final.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            (await db.Divisions.CountAsync(candidate => candidate.CountryId == countryId && candidate.TierNumber == 2))
                .Should().Be(1, "PYR-3: one tier is created once");
        }
    }

    [Fact]
    public async Task Provisioning_backfills_passed_matchdays_as_bootstrap()
    {
        // Move the clock past rounds one and two (the first is 2026-10-06 19:00 UTC).
        _fixture.Clock.UtcNow = new DateTimeOffset(2026, 10, 8, 20, 0, 0, TimeSpan.Zero);

        Guid countryId;
        Guid requestId;
        Guid divisionSeasonId;

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
            var world = await db.GameWorlds.SingleAsync();
            var season = await db.Seasons.SingleAsync(candidate => candidate.WorldId == world.Id);

            countryId = await db.Countries.OrderBy(country => country.SortOrder).Select(c => c.Id).FirstAsync();
            requestId = await CreateRequestAsync(scope, countryId, targetTier: 2, season.Id);

            divisionSeasonId = Guid.Empty;
        }

        ProvisionDivisionResult result;

        await using (var scope = _fixture.CreateScope())
        {
            result = await scope.ServiceProvider.GetRequiredService<ProvisionDivision>()
                .ExecuteAsync(requestId, CancellationToken.None);

            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            divisionSeasonId = await (
                from division in db.Divisions
                join divisionSeason in db.DivisionSeasons on division.Id equals divisionSeason.DivisionId
                where division.CountryId == countryId && division.TierNumber == 2
                select divisionSeason.Id)
                .SingleAsync();
        }

        result.BackfilledRounds.Should().Be(2, "PYR-6: the passed matchdays are simulated in sequence");

        await using (var verify = _fixture.CreateScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            var rounds = await db.Matchdays
                .Where(matchday => matchday.DivisionSeasonId == divisionSeasonId)
                .OrderBy(matchday => matchday.RoundNumber)
                .ToListAsync();

            var bootstrapByRound = await db.Fixtures
                .Where(fixture => rounds.Select(r => r.Id).Contains(fixture.MatchdayId))
                .GroupBy(fixture => fixture.MatchdayId)
                .Select(group => new { MatchdayId = group.Key, Bootstrap = group.All(f => f.IsBootstrap) })
                .ToListAsync();

            var bootstrapRounds = rounds
                .Where(round => bootstrapByRound.Single(x => x.MatchdayId == round.Id).Bootstrap)
                .Select(round => round.RoundNumber)
                .OrderBy(number => number)
                .ToList();

            bootstrapRounds.Should().Equal([1, 2], "PYR-7: only the passed rounds are generated history");

            // Every backfilled result is published, and the table reconciles with what it published (TBL-13).
            (await db.Fixtures.CountAsync(fixture =>
                rounds.Select(r => r.Id).Contains(fixture.MatchdayId) && fixture.Status == FixtureStatus.Published))
                .Should().Be(18, "MAT-7: two published rounds of nine fixtures");

            var rebuild = await verify.ServiceProvider.GetRequiredService<RebuildDivisionProjections>()
                .ExecuteAsync(divisionSeasonId, apply: false, CancellationToken.None);

            rebuild.Outcome.Should().Be(ProjectionRebuildOutcome.Reconciled, "TBL-13");
        }
    }

    private static async Task<Guid> CreateRequestAsync(
        AsyncServiceScope scope,
        Guid countryId,
        int targetTier,
        Guid seasonId)
    {
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IDivisionProvisioningRequestRepository>();
        var now = DateTimeOffset.UtcNow;

        var request = DivisionProvisioningRequest.Request(
            Guid.CreateVersion7(),
            countryId,
            targetTier,
            seasonId,
            $"provision-test-{countryId:N}-{targetTier}",
            now);

        repository.Add(request);
        await db.SaveChangesAsync(CancellationToken.None);

        return request.Id;
    }
}

/// <summary>The provisioning tests' own seeded world.</summary>
public sealed class ProvisioningFixture : WorldFixture
{
    /// <summary>Creates the fixture against its own database.</summary>
    public ProvisioningFixture()
        : base("touchline_provisioning")
    {
    }
}
