using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Application.World;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;
using TouchlineManager.Domain.Squad.Generation;
using TouchlineManager.Domain.World;
using TouchlineManager.Domain.World.Generation;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Infrastructure.Tests.World;

/// <summary>
/// The Stage 3 exit criteria for world generation (master plan §16 Stage 3).
/// </summary>
/// <remarks>
/// Runs against a real, freshly seeded PostgreSQL 17 database, because the claims being verified are
/// about rows: 108 clubs with unique identities, one entry per club per season, one funded account per
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
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

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

        clubs.Should().HaveCount(108, "six countries times eighteen clubs");
        clubs.Select(club => club.NormalizedName).Should().OnlyHaveUniqueItems();
        clubs.Select(club => club.Slug).Should().OnlyHaveUniqueItems();
        clubs.Should().OnlyContain(club => club.Reputation >= 1 && club.Reputation <= 100);
        clubs.Select(club => club.CountryId).Distinct().Should().HaveCount(6);
    }

    [Fact]
    public async Task Every_club_has_a_funded_account_and_exactly_one_season_entry()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var clubIds = await db.Clubs
            .Where(club => club.WorldId == Fixture.WorldId)
            .Select(club => club.Id)
            .ToListAsync();

        var accounts = await db.ClubAccounts
            .Where(account => clubIds.Contains(account.ClubId))
            .ToListAsync();

        accounts.Should().HaveCount(108);
        accounts.Should().OnlyContain(account => account.CashMinor == WorldRuleSet.OpeningCashMinorTier1);
        accounts.Should().OnlyContain(account => account.ReservedMinor == 0);

        var entries = await db.ClubSeasonEntries
            .Where(entry => clubIds.Contains(entry.ClubId))
            .ToListAsync();

        entries.Should().HaveCount(108);
        entries.Should().OnlyContain(entry => entry.InitialControlType == ClubControlType.Ai);
        entries.Select(entry => entry.ClubId).Should().OnlyHaveUniqueItems(
            "one club appears once in a season, which the database also enforces");
    }

    [Fact]
    public async Task Every_club_was_generated_from_the_world_seed()
    {
        // The reproducibility contract (FIC-7, PYR-14): the seeder must not have invented names of its own,
        // so re-running the generator with the recorded seed must reproduce what is in the database.
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        foreach (var country in Fixture.Countries)
        {
            var persisted = await db.Clubs
                .Where(club => club.CountryId == country.Id)
                .OrderBy(club => club.Name)
                .Select(club => club.Name)
                .ToListAsync();

            var regenerated = ClubIdentityGenerator
                .GenerateDivision(WorldFixture.Seed, country.NamePoolKey, country.Code, tierNumber: 1, 18)
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
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var persisted = await db.Clubs
            .Where(club => club.WorldId == Fixture.WorldId)
            .Select(club => club.BadgeSeed)
            .ToListAsync();

        var other = ClubIdentityGenerator
            .GenerateDivision("some-other-world", "england", "ENG", tierNumber: 1, 18)
            .Select(identity => identity.BadgeSeed)
            .ToList();

        other.Should().NotIntersectWith(persisted);
    }

    [Fact]
    public async Task The_first_season_starts_on_a_matchday_and_runs_the_configured_calendar()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var season = await db.Seasons.SingleAsync(candidate => candidate.WorldId == Fixture.WorldId);

        season.SequenceNumber.Should().Be(1);
        season.Status.Should().Be(SeasonStatus.Active);
        season.RuleSetVersion.Should().Be(WorldRuleSet.Version);

        var firstMatchday = DateOnly.FromDateTime(season.StartsAt.UtcDateTime);

        SeasonCalendar.IsMatchday(firstMatchday).Should().BeTrue("CAL-2 fixes matchdays to Tue, Thu, and Sun");
        season.StartsAt.Offset.Should().Be(TimeSpan.Zero, "the calendar is stored in UTC (TIME-1)");
        SeasonCalendar.KickoffAt(firstMatchday).Should().Be(season.StartsAt);
        season.RolloverEndsAt.Should().Be(season.EndsAt.AddDays(WorldRuleSet.RolloverDays));
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
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
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
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var run = await db.GenerationRuns.SingleAsync(candidate => candidate.Seed == WorldFixture.Seed);

        run.Kind.Should().Be(GenerationRunKind.WorldBootstrap);
        run.Status.Should().Be(GenerationRunStatus.Succeeded);
        run.GeneratorVersion.Should().Be(
            WorldBootstrapGenerator.Version,
            "the bootstrap produces clubs and players, so the run records the version that covers both (FIC-8)");
        run.CountriesCreated.Should().Be(6);
        run.ClubsCreated.Should().Be(108);
        run.PlayersCreated.Should().Be(108 * WorldRuleSet.GeneratorSquadTarget, "SQ-1");
        run.AccountsCreated.Should().Be(108);
        run.InputHash.Should().HaveLength(64);
        run.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Every_club_has_a_legal_twenty_two_player_squad()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var squadSizes = await db.PlayerContracts
            .Where(contract => contract.Status == ContractStatus.Active)
            .GroupBy(contract => contract.ClubId)
            .Select(group => new { ClubId = group.Key, Count = group.Count() })
            .ToListAsync();

        squadSizes.Should().HaveCount(108);
        squadSizes.Should().OnlyContain(club => club.Count == WorldRuleSet.GeneratorSquadTarget, "SQ-1");
    }

    [Fact]
    public async Task Every_club_registers_at_least_two_goalkeepers()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

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
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var playerCount = await db.Players.CountAsync(player => player.WorldId == Fixture.WorldId);

        playerCount.Should().Be(108 * WorldRuleSet.GeneratorSquadTarget, "SQ-1");

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
        states.Should().OnlyContain(row => row.DevelopmentRemainder == 0, "TRN-10");
    }

    [Fact]
    public async Task Every_seeded_squad_is_exactly_what_the_generator_produces()
    {
        // The reproducibility contract for players (FIC-7, PYR-14): re-running the generator with the
        // recorded ordinal must reproduce the names and attributes that are in the database.
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

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
