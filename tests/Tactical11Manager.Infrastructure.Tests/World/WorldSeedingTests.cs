using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Application.World;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.Squad.Generation;
using Tactical11Manager.Domain.World;
using Tactical11Manager.Domain.World.Generation;
using Tactical11Manager.Infrastructure.Persistence;

namespace Tactical11Manager.Infrastructure.Tests.World;

/// <summary>
/// The Stage 3 exit criteria for world generation (master plan §16 Stage 3).
/// </summary>
/// <remarks>
/// Runs against a real, freshly seeded PostgreSQL 17 database, because the claims being verified are
/// about rows: every club with a unique identity, one entry per club per season, one funded account per
/// club. A generator test can prove determinism; only this can prove the seeder put the result in the
/// database.
/// </remarks>
[Collection(WorldCollection.Name)]
public sealed class WorldSeedingTests : WorldTestBase
{
    /// <summary>Initializes the tests.</summary>
    public WorldSeedingTests(WorldFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task The_world_holds_six_countries_with_one_full_tier_each()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var countries = await db.Countries.Where(country => country.WorldId == Fixture.WorldId).ToListAsync();

        countries.Should().HaveCount(6);
        countries.Select(country => country.Code).Should().BeEquivalentTo("ENG", "ESP", "GER", "ITA", "FRA", "ROU");

        var divisions = await db.Divisions
            .Where(division => countries.Select(country => country.Id).Contains(division.CountryId))
            .ToListAsync();

        divisions.Should().HaveCount(6).And.OnlyContain(division => division.TierNumber == 1);
        divisions.Should().OnlyContain(
            division => division.Status == DivisionStatus.Active,
            "a seeded tier is claimable immediately, unlike a provisioned one (PYR-8)");

        var clubs = await db.Clubs.Where(club => club.WorldId == Fixture.WorldId).ToListAsync();

        clubs.Should().HaveCount(6 * WorldRuleSet.ClubsPerDivision, "six countries times a division of clubs");
        clubs.Select(club => club.NormalizedName).Should().OnlyHaveUniqueItems();
        clubs.Select(club => club.Slug).Should().OnlyHaveUniqueItems();
        clubs.Should().OnlyContain(club => club.Reputation >= 1 && club.Reputation <= 100);
        clubs.Select(club => club.CountryId).Distinct().Should().HaveCount(6);
    }

    [Fact]
    public async Task Every_club_has_a_funded_account_and_exactly_one_season_entry()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var clubIds = await db.Clubs
            .Where(club => club.WorldId == Fixture.WorldId)
            .Select(club => club.Id)
            .ToListAsync();

        var accounts = await db.ClubAccounts
            .Where(account => clubIds.Contains(account.ClubId))
            .ToListAsync();

        accounts.Should().HaveCount(6 * WorldRuleSet.ClubsPerDivision);
        accounts.Should().OnlyContain(account => account.CashMinor == WorldRuleSet.OpeningCashMinorTier1);
        accounts.Should().OnlyContain(account => account.ReservedMinor == 0);
        accounts.Should().OnlyContain(
            account => account.LastLedgerSequence == 1,
            "the balance is one opening ledger entry, not a value written onto the row (FIN-18)");

        var entries = await db.ClubSeasonEntries
            .Where(entry => clubIds.Contains(entry.ClubId))
            .ToListAsync();

        entries.Should().HaveCount(6 * WorldRuleSet.ClubsPerDivision);
        entries.Should().OnlyContain(entry => entry.InitialControlType == ClubControlType.Ai);
        entries.Select(entry => entry.ClubId).Should().OnlyHaveUniqueItems(
            "one club appears once in a season, which the database also enforces");
    }

    [Fact]
    public async Task Every_club_opens_with_a_snack_bar_and_a_merchandise_shop_at_level_one()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var clubIds = await db.Clubs
            .Where(club => club.WorldId == Fixture.WorldId)
            .Select(club => club.Id)
            .ToListAsync();

        var buildings = await db.ClubStructures
            .Where(building => clubIds.Contains(building.ClubId))
            .ToListAsync();

        buildings.Should().HaveCount(2 * 6 * WorldRuleSet.ClubsPerDivision, "two buildings for each club (STRC-1)");
        buildings.Should().OnlyContain(building => building.Level == 1 && !building.IsBuilding);

        foreach (var kind in StructureKinds.All)
        {
            buildings.Where(building => building.Kind == kind).Select(building => building.ClubId)
                .Should().BeEquivalentTo(clubIds, $"every club has a {kind.ToCode()}");
        }
    }

    [Fact]
    public async Task Every_club_was_generated_from_the_world_seed()
    {
        // The reproducibility contract (FIC-7, PYR-14): the seeder must not have invented names of its own,
        // so re-running the generator with the recorded seed must reproduce what is in the database.
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        foreach (var country in Fixture.Countries)
        {
            var persisted = await db.Clubs
                .Where(club => club.CountryId == country.Id)
                .OrderBy(club => club.Name)
                .Select(club => club.Name)
                .ToListAsync();

            var regenerated = ClubIdentityGenerator
                .GenerateDivision(WorldFixture.Seed, country.NamePoolKey, country.Code, tierNumber: 1, WorldRuleSet.ClubsPerDivision)
                .Select(identity => identity.Name)
                .Order(StringComparer.Ordinal)
                .ToList();

            persisted.Should().Equal(regenerated, "the seeded world must be exactly what the seed generates");
        }
    }

    [Fact]
    public async Task A_different_seed_would_have_generated_a_different_pyramid()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var persisted = await db.Clubs
            .Where(club => club.WorldId == Fixture.WorldId)
            .Select(club => club.BadgeSeed)
            .ToListAsync();

        var other = ClubIdentityGenerator
            .GenerateDivision("some-other-world", "england", "ENG", tierNumber: 1, WorldRuleSet.ClubsPerDivision)
            .Select(identity => identity.BadgeSeed)
            .ToList();

        other.Should().NotIntersectWith(persisted);
    }

    [Fact]
    public async Task The_first_season_starts_on_a_matchday_and_runs_the_configured_calendar()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var season = await db.Seasons.SingleAsync(candidate => candidate.WorldId == Fixture.WorldId);

        season.SequenceNumber.Should().Be(1);
        season.Status.Should().Be(SeasonStatus.Active);
        season.RuleSetVersion.Should().Be(WorldRuleSet.Version);

        var firstMatchday = DateOnly.FromDateTime(season.StartsAt.UtcDateTime);

        SeasonCalendar.IsMatchday(firstMatchday).Should().BeTrue("CAL-2 fixes matchdays to Tue, Thu, and Sun");
        season.StartsAt.Offset.Should().Be(TimeSpan.Zero, "the calendar is stored in UTC (TIME-1)");
        SeasonCalendar.KickoffAt(firstMatchday).Should().Be(season.StartsAt);
        season.RolloverEndsAt.Should().Be(season.StartsAt.AddDays(7 * WorldRuleSet.SeasonCycleWeeks));
        season.GameYear.Should().Be(firstMatchday.Year, "the first season's game year is its own start year");
    }

    [Fact]
    public async Task A_seeded_tier_offers_every_club_to_a_new_manager()
    {
        await using var scope = Fixture.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<IOnboardingQueries>();

        foreach (var country in Fixture.Countries)
        {
            var capacity = await queries.GetCountryCapacityAsync(country.Id, CancellationToken.None);

            capacity.Should().NotBeNull();
            capacity!.LowestActiveTier.Should().Be(1);
            capacity.ClubsInLowestTier.Should().Be(WorldRuleSet.ClubsPerDivision);
            capacity.HumanOccupiedClubs.Should().Be(0, "every club starts under AI control (WORLD-5)");

            var clubs = await queries.GetAvailableClubsAsync(capacity.DivisionSeasonId, CancellationToken.None);

            clubs.Should().HaveCount(WorldRuleSet.ClubsPerDivision);
            clubs.Should().OnlyContain(club => club.IsAvailable);
        }
    }

    [Fact]
    public async Task Seeding_an_already_seeded_world_changes_nothing()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var seeder = scope.ServiceProvider.GetRequiredService<SeedWorld>();

        var before = await db.Clubs.CountAsync(club => club.WorldId == Fixture.WorldId);

        var result = await seeder.ExecuteAsync(
            new SeedWorldRequest("a-completely-different-seed"),
            CancellationToken.None);

        result.Outcome.Should().Be(SeedWorldOutcome.AlreadySeeded);
        result.WorldId.Should().Be(Fixture.WorldId);

        var after = await db.Clubs.CountAsync(club => club.WorldId == Fixture.WorldId);

        after.Should().Be(before, "one world is a product rule, not a row identity (WORLD-1)");

        var worlds = await db.GameWorlds.CountAsync();

        worlds.Should().Be(1);
    }

    [Fact]
    public async Task The_seed_run_is_recorded_with_its_seed_and_generator_version()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var run = await db.GenerationRuns.SingleAsync(candidate => candidate.Seed == WorldFixture.Seed);

        run.Kind.Should().Be(GenerationRunKind.WorldBootstrap);
        run.Status.Should().Be(GenerationRunStatus.Succeeded);
        run.GeneratorVersion.Should().Be(
            WorldBootstrapGenerator.Version,
            "the bootstrap produces clubs and players, so the run records the version that covers both (FIC-8)");
        run.CountriesCreated.Should().Be(6);
        run.ClubsCreated.Should().Be(6 * WorldRuleSet.ClubsPerDivision);
        run.PlayersCreated.Should().Be(6 * WorldRuleSet.ClubsPerDivision * WorldRuleSet.GeneratorSquadTarget, "SQ-1");
        run.AccountsCreated.Should().Be(6 * WorldRuleSet.ClubsPerDivision);
        run.InputHash.Should().HaveLength(64);
        run.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Every_club_has_a_legal_squad_of_the_target_size()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var squadSizes = await db.PlayerContracts
            .Where(contract => contract.Status == ContractStatus.Active)
            .GroupBy(contract => contract.ClubId)
            .Select(group => new { ClubId = group.Key, Count = group.Count() })
            .ToListAsync();

        squadSizes.Should().HaveCount(6 * WorldRuleSet.ClubsPerDivision);
        squadSizes.Should().OnlyContain(club => club.Count == WorldRuleSet.GeneratorSquadTarget, "SQ-1");
    }

    [Fact]
    public async Task Every_club_has_one_left_and_one_right_midfielder()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var wide = await db.PlayerContracts
            .Where(contract => contract.Status == ContractStatus.Active)
            .Join(
                db.Players,
                contract => contract.PlayerId,
                player => player.Id,
                (contract, player) => new { contract.ClubId, player.PrimaryPosition })
            .Where(entry => entry.PrimaryPosition == PlayerPosition.LeftMidfielder
                || entry.PrimaryPosition == PlayerPosition.RightMidfielder)
            .ToListAsync();

        var byClub = wide.GroupBy(entry => entry.ClubId).ToList();

        byClub.Should().HaveCount(6 * WorldRuleSet.ClubsPerDivision, "every club has wide midfielders");
        byClub.Should().OnlyContain(
            club => club.Count(entry => entry.PrimaryPosition == PlayerPosition.LeftMidfielder) == 1
                && club.Count(entry => entry.PrimaryPosition == PlayerPosition.RightMidfielder) == 1,
            "SQ-1: one left and one right midfielder a club");
    }

    [Fact]
    public async Task Every_club_registers_at_least_two_goalkeepers()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var clubs = await db.Clubs
            .Where(club => club.WorldId == Fixture.WorldId)
            .Select(club => club.Id)
            .ToListAsync();

        foreach (var clubId in clubs)
        {
            var keepers = await db.PlayerContracts
                .Where(contract => contract.ClubId == clubId && contract.Status == ContractStatus.Active)
                .Join(
                    db.Players,
                    contract => contract.PlayerId,
                    player => player.Id,
                    (_, player) => player.PrimaryPosition)
                .CountAsync(position => position == PlayerPosition.Goalkeeper);

            keepers.Should().BeGreaterThanOrEqualTo(WorldRuleSet.MinimumGoalkeepers, "SQ-2");
        }
    }

    [Fact]
    public async Task Every_player_has_attributes_state_one_active_contract_and_one_active_registration()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var playerCount = await db.Players.CountAsync(player => player.WorldId == Fixture.WorldId);

        playerCount.Should().Be(6 * WorldRuleSet.ClubsPerDivision * WorldRuleSet.GeneratorSquadTarget, "SQ-1");

        (await db.PlayerAttributes.CountAsync()).Should().Be(playerCount);
        (await db.PlayerStates.CountAsync()).Should().Be(playerCount);
        (await db.PlayerContracts.CountAsync(contract => contract.Status == ContractStatus.Active))
            .Should().Be(playerCount, "SQ-6: one active contract per player");
        (await db.PlayerRegistrations.CountAsync(registration => registration.Status == RegistrationStatus.Active))
            .Should().Be(playerCount, "SQ-6: one active registration per player");

        var attributes = await db.PlayerAttributes.ToListAsync();

        attributes.Should().OnlyContain(row => row.ToSet().IsWithinScale, "TRN-4");
        attributes.Should().OnlyContain(row => row.ChecksumMatches(), "an attribute row is written with its checksum");

        var states = await db.PlayerStates.ToListAsync();

        states.Should().OnlyContain(row => row.ConditionBp <= WorldRuleSet.StateBasisPointsMax, "TRN-5");
        states.Should().OnlyContain(row => row.AttributeProgress.All(progress => progress == 0), "TRN-10");
    }

    [Fact]
    public async Task Every_seeded_squad_is_exactly_what_the_generator_produces()
    {
        // The reproducibility contract for players (FIC-7, PYR-14): re-running the generator with the
        // recorded ordinal must reproduce the names and attributes that are in the database.
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var country = Fixture.Countries[0];
        var season = await db.Seasons.SingleAsync(candidate => candidate.WorldId == Fixture.WorldId);

        var identities = ClubIdentityGenerator.GenerateDivision(
            WorldFixture.Seed, country.NamePoolKey, country.Code, tierNumber: 1, WorldRuleSet.ClubsPerDivision);

        const int ordinal = 3;
        var clubId = await db.Clubs
            .Where(club => club.CountryId == country.Id && club.Name == identities[ordinal].Name)
            .Select(club => club.Id)
            .SingleAsync();

        var persisted = await db.PlayerContracts
            .Where(contract => contract.ClubId == clubId && contract.Status == ContractStatus.Active)
            .Join(
                db.Players,
                contract => contract.PlayerId,
                player => player.Id,
                (_, player) => player)
            .Join(
                db.PlayerAttributes,
                player => player.Id,
                attributes => attributes.PlayerId,
                (player, attributes) => new { player.FullName, Attributes = attributes.ToSet() })
            .ToListAsync();

        var regenerated = PlayerGenerator
            .GenerateSquad(new SquadGenerationRequest(
                WorldFixture.Seed,
                country.NamePoolKey,
                country.Code,
                Fixture.WorldId,
                clubId,
                ordinal,
                Tier: 1,
                season.Id,
                season.SequenceNumber,
                season.GameYear,
                Fixture.Clock.UtcNow))
            .Select(member => new { member.Player.FullName, Attributes = member.Attributes.ToSet() })
            .ToList();

        persisted.Should().BeEquivalentTo(regenerated, "the seeded squad must be exactly what the seed generates");
    }
}
