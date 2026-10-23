using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Application.Competition;
using Tactical11Manager.Application.World;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.World;
using Tactical11Manager.Domain.World.Generation;
using Tactical11Manager.Infrastructure.Persistence;
using Tactical11Manager.Infrastructure.Tests.World;

namespace Tactical11Manager.Infrastructure.Tests.World;

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
    /// <summary>The tier a full country provisions: tiers 1 and 2 are seeded (`WORLD-5`).</summary>
    private const int NewTier = WorldRuleSet.SeededTiers + 1;

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
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
            var world = await db.GameWorlds.SingleAsync();
            var season = await db.Seasons.SingleAsync(candidate => candidate.WorldId == world.Id);

            countryId = await db.Countries.OrderBy(country => country.SortOrder).Select(c => c.Id).FirstAsync();
            requestId = await CreateRequestAsync(scope, countryId, targetTier: NewTier, season.Id);
        }

        ProvisionDivisionResult result;

        await using (var scope = _fixture.CreateScope())
        {
            result = await scope.ServiceProvider.GetRequiredService<ProvisionDivision>()
                .ExecuteAsync(requestId, CancellationToken.None);
        }

        result.Outcome.Should().Be(ProvisionDivisionOutcome.Completed);
        result.TargetTier.Should().Be(NewTier);
        result.Clubs.Should().Be(WorldRuleSet.ClubsPerDivision, "PYR-4: a tier holds a full division of clubs");
        result.Players.Should().Be(WorldRuleSet.ClubsPerDivision * WorldRuleSet.GeneratorSquadTarget, "SQ-1: the generator produces its target squad per club");
        result.Accounts.Should().Be(WorldRuleSet.ClubsPerDivision);

        await using (var verify = _fixture.CreateScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            var division = await db.Divisions.SingleAsync(candidate =>
                candidate.CountryId == countryId && candidate.TierNumber == NewTier);

            division.Status.Should().Be(DivisionStatus.Active, "PYR-8: the tier is claimable once validated");

            var divisionSeason = await db.DivisionSeasons.SingleAsync(candidate => candidate.DivisionId == division.Id);

            (await db.ClubSeasonEntries.CountAsync(entry => entry.DivisionSeasonId == divisionSeason.Id))
                .Should().Be(WorldRuleSet.ClubsPerDivision);
            (await db.Matchdays.CountAsync(matchday => matchday.DivisionSeasonId == divisionSeason.Id))
                .Should().Be(WorldRuleSet.MatchdaysPerSeason, "CAL-1");
            (await db.Fixtures.CountAsync(fixture => fixture.MatchdayId != Guid.Empty
                && db.Matchdays.Any(m => m.Id == fixture.MatchdayId && m.DivisionSeasonId == divisionSeason.Id)))
                .Should().Be(WorldRuleSet.MatchdaysPerSeason * (WorldRuleSet.ClubsPerDivision / 2), "CAL-9: every round has half the division in fixtures");

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

            capacity!.LowestActiveTier.Should().Be(NewTier, "PYR-8: a claim now joins the new tier");
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
            var db = final.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            (await db.Divisions.CountAsync(candidate => candidate.CountryId == countryId && candidate.TierNumber == NewTier))
                .Should().Be(1, "PYR-3: one tier is created once");
        }
    }

    [Fact]
    public async Task Provisioning_names_a_new_tier_from_the_world_seed_so_it_cannot_collide()
    {
        // ADR-0033: club identity is seeded from the world seed, not from the per-tier provisioning request
        // seed. The request seed folds the target tier in, so using it would place tier 3's sixteen-slot
        // window independently on the shared name cycle and could overlap tier 1's — which the unique name
        // index rejects with 23505. This test names the tier's clubs deterministically from the world seed.
        Guid countryId;
        string poolKey;
        string code;
        Guid requestId;

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
            var world = await db.GameWorlds.SingleAsync();
            var season = await db.Seasons.SingleAsync(candidate => candidate.WorldId == world.Id);
            var country = await db.Countries.OrderBy(candidate => candidate.SortOrder).FirstAsync();

            countryId = country.Id;
            poolKey = country.NamePoolKey;
            code = country.Code;

            requestId = await CreateRequestAsync(scope, countryId, targetTier: NewTier, season.Id);
        }

        await using (var scope = _fixture.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ProvisionDivision>()
                .ExecuteAsync(requestId, CancellationToken.None);
        }

        await using (var verify = _fixture.CreateScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            var allNames = await db.Clubs
                .Where(club => club.CountryId == countryId)
                .Select(club => club.NormalizedName)
                .ToListAsync();

            allNames.Should().HaveCount(NewTier * WorldRuleSet.ClubsPerDivision, "PYR-11: three full tiers of clubs");
            allNames.Should().OnlyHaveUniqueItems(
                "ADR-0033: a provisioned tier must continue the name cycle of the tier above it");

            var newTierNames = await (
                from entry in db.ClubSeasonEntries
                join divisionSeason in db.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
                join division in db.Divisions on divisionSeason.DivisionId equals division.Id
                join club in db.Clubs on entry.ClubId equals club.Id
                where division.CountryId == countryId && division.TierNumber == NewTier
                select club.Name)
                .ToListAsync();

            var expected = ClubIdentityGenerator
                .GenerateDivision(WorldFixture.Seed, poolKey, code, tierNumber: NewTier, clubCount: WorldRuleSet.ClubsPerDivision)
                .Select(identity => identity.Name)
                .ToList();

            newTierNames.Should().BeEquivalentTo(
                expected,
                "ADR-0033: the worker names a tier's clubs from the world seed, not the request seed");
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
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
            var world = await db.GameWorlds.SingleAsync();
            var season = await db.Seasons.SingleAsync(candidate => candidate.WorldId == world.Id);

            countryId = await db.Countries.OrderBy(country => country.SortOrder).Select(c => c.Id).FirstAsync();
            requestId = await CreateRequestAsync(scope, countryId, targetTier: NewTier, season.Id);

            divisionSeasonId = Guid.Empty;
        }

        ProvisionDivisionResult result;

        await using (var scope = _fixture.CreateScope())
        {
            result = await scope.ServiceProvider.GetRequiredService<ProvisionDivision>()
                .ExecuteAsync(requestId, CancellationToken.None);

            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            divisionSeasonId = await (
                from division in db.Divisions
                join divisionSeason in db.DivisionSeasons on division.Id equals divisionSeason.DivisionId
                where division.CountryId == countryId && division.TierNumber == NewTier
                select divisionSeason.Id)
                .SingleAsync();
        }

        result.BackfilledRounds.Should().Be(2, "PYR-6: the passed matchdays are simulated in sequence");

        await using (var verify = _fixture.CreateScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

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
                .Should().Be(2 * (WorldRuleSet.ClubsPerDivision / 2), "MAT-7: two published rounds, half a division of fixtures each");

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
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
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
        : base("tactical11_provisioning")
    {
    }
}
