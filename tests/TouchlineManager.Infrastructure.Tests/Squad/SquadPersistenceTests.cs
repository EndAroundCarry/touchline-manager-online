using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;
using TouchlineManager.Domain.World;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Infrastructure.Tests.Squad;

/// <summary>
/// The squad schema's database-level guarantees, against real PostgreSQL 17 (`SQ-6`, `TRN-4`,
/// `TRN-5..TRN-7`, `TAC-9`, `INS-11`).
/// </summary>
/// <remarks>
/// These are the invariants an aggregate cannot provide on its own: two partial unique indexes are what
/// stop a player holding two active contracts or registrations when a transfer and a renewal race, and
/// the range and coordinate checks are what keep a bad write from reaching the engine.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class SquadPersistenceTests
{
    private readonly PostgresFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public SquadPersistenceTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_player_cannot_hold_two_active_contracts()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var (clubId, playerId) = await ArrangeSquadPlayerAsync(scope);

        db.PlayerContracts.Add(PlayerContract.Sign(
            Guid.CreateVersion7(), playerId, clubId, 1, 2, 2_000, SquadStatus.Rotation, _fixture.Clock.UtcNow));

        var act = async () => await db.SaveChangesAsync();
        var exception = await act.Should().ThrowAsync<DbUpdateException>();

        ConstraintOf(exception).Should().Be("ux_player_contracts_active_player", "SQ-6");
    }

    [Fact]
    public async Task A_player_cannot_hold_two_active_registrations()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var (clubId, playerId) = await ArrangeSquadPlayerAsync(scope);
        var seasonId = await db.Seasons.Select(season => season.Id).FirstAsync();

        db.PlayerRegistrations.Add(PlayerRegistration.Register(
            Guid.CreateVersion7(), playerId, clubId, seasonId, effectiveFixtureBoundaryRound: 5, _fixture.Clock.UtcNow));

        var act = async () => await db.SaveChangesAsync();
        var exception = await act.Should().ThrowAsync<DbUpdateException>();

        ConstraintOf(exception).Should().Be("ux_player_registrations_active_player", "SQ-6");
    }

    [Fact]
    public async Task An_attribute_outside_the_displayed_scale_is_rejected()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var (_, playerId) = await ArrangeSquadPlayerAsync(scope);

        var act = async () => await db.Database.ExecuteSqlRawAsync(
            "update squad.player_attributes set finishing = 21 where player_id = {0}",
            playerId);

        var exception = await act.Should().ThrowAsync<PostgresException>();

        exception.Which.ConstraintName.Should().Be("ck_player_attributes_range_technical", "TRN-4");
    }

    [Fact]
    public async Task A_state_value_outside_its_basis_point_scale_is_rejected()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var (_, playerId) = await ArrangeSquadPlayerAsync(scope);

        var act = async () => await db.Database.ExecuteSqlRawAsync(
            "update squad.player_state set condition_bp = 10001 where player_id = {0}",
            playerId);

        var exception = await act.Should().ThrowAsync<PostgresException>();

        exception.Which.ConstraintName.Should().Be("ck_player_state_basis_points", "TRN-5");
    }

    [Fact]
    public async Task A_club_cannot_have_two_default_plans()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var (clubId, _) = await ArrangeSquadPlayerAsync(scope);

        db.TacticalPlans.Add(Plan(clubId, "First", isDefault: true));
        await db.SaveChangesAsync();

        db.TacticalPlans.Add(Plan(clubId, "Second", isDefault: true));

        var act = async () => await db.SaveChangesAsync();
        var exception = await act.Should().ThrowAsync<DbUpdateException>();

        ConstraintOf(exception).Should().Be("ux_tactical_plans_default_club", "INS-11");
    }

    [Fact]
    public async Task A_club_cannot_have_two_training_plans()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var (clubId, _) = await ArrangeSquadPlayerAsync(scope);
        var today = DateOnly.FromDateTime(_fixture.Clock.UtcNow.UtcDateTime);

        db.TrainingPlans.Add(TrainingPlan.Set(
            Guid.CreateVersion7(), clubId, TrainingFocus.Balanced, TrainingIntensity.Normal, today, _fixture.Clock.UtcNow));
        await db.SaveChangesAsync();

        db.TrainingPlans.Add(TrainingPlan.Set(
            Guid.CreateVersion7(), clubId, TrainingFocus.Recovery, TrainingIntensity.Light, today, _fixture.Clock.UtcNow));

        var act = async () => await db.SaveChangesAsync();
        var exception = await act.Should().ThrowAsync<DbUpdateException>();

        ConstraintOf(exception).Should().Be("ux_training_plans_club");
    }

    [Fact]
    public async Task A_slot_coordinate_outside_the_pitch_is_rejected()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var (clubId, _) = await ArrangeSquadPlayerAsync(scope);

        var plan = Plan(clubId, "Shape", isDefault: false);
        db.TacticalPlans.Add(plan);

        await db.SaveChangesAsync();

        var act = async () => await db.Database.ExecuteSqlRawAsync(
            "insert into squad.tactical_slots "
            + "(id, plan_id, slot_number, position_family, role, normalized_x, normalized_y, created_at, updated_at, version) "
            + "values ({0}, {1}, 7, 'attack', 'striker', 10001, 5000, now(), now(), 1)",
            Guid.CreateVersion7(),
            plan.Id);

        var exception = await act.Should().ThrowAsync<PostgresException>();

        exception.Which.ConstraintName.Should().Be("ck_tactical_slots_coordinates", "TAC-9");
    }

    [Fact]
    public async Task A_plan_cannot_hold_two_slots_with_the_same_number()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var (clubId, _) = await ArrangeSquadPlayerAsync(scope);

        var plan = Plan(clubId, "Shape", isDefault: false);
        db.TacticalPlans.Add(plan);

        db.TacticalSlots.Add(Slot(plan.Id, slotNumber: 9));
        await db.SaveChangesAsync();

        db.TacticalSlots.Add(Slot(plan.Id, slotNumber: 9));

        var act = async () => await db.SaveChangesAsync();
        var exception = await act.Should().ThrowAsync<DbUpdateException>();

        ConstraintOf(exception).Should().Be("ux_tactical_slots_plan_slot");
    }

    [Fact]
    public async Task A_player_cannot_be_assigned_to_two_slots_in_one_plan()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var (clubId, playerId) = await ArrangeSquadPlayerAsync(scope);

        var plan = Plan(clubId, "Shape", isDefault: false);
        db.TacticalPlans.Add(plan);

        db.TacticalSlots.Add(Slot(plan.Id, slotNumber: 9, playerId));
        await db.SaveChangesAsync();

        db.TacticalSlots.Add(Slot(plan.Id, slotNumber: 10, playerId));

        var act = async () => await db.SaveChangesAsync();
        var exception = await act.Should().ThrowAsync<DbUpdateException>();

        ConstraintOf(exception).Should().Be("ux_tactical_slots_plan_player", "INS-10");
    }

    [Fact]
    public async Task A_team_sheet_round_trips_with_its_entries()
    {
        // The sheet now references a real fixture, so the foreign key added with the prepare-match screen is
        // exercised here rather than assumed (ADR-0011, MIG-7), and the nullable role override has to
        // survive a write and a read.
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var (clubId, playerId) = await ArrangeSquadPlayerAsync(scope);
        var fixtureId = await ArrangeFixtureAsync(db, clubId);

        var plan = Plan(clubId, "Shape", isDefault: true);
        db.TacticalPlans.Add(plan);

        var sheet = FixtureTeamSheet.Draft(
            Guid.CreateVersion7(), fixtureId, clubId, plan.Id, plan.Version, _fixture.Clock.UtcNow);

        db.FixtureTeamSheets.Add(sheet);
        db.TeamSheetEntries.Add(TeamSheetEntry.Select(
            Guid.CreateVersion7(), sheet.Id, playerId, TeamSheetDesignation.Starter, slotNumber: 1,
            roleOverride: null, _fixture.Clock.UtcNow));

        await db.SaveChangesAsync();

        var stored = await db.FixtureTeamSheets.SingleAsync(candidate => candidate.Id == sheet.Id);

        stored.Status.Should().Be(TeamSheetStatus.Draft);
        stored.FixtureId.Should().Be(fixtureId);
        stored.TacticalPlanVersion.Should().Be(plan.Version);
        stored.LockedAt.Should().BeNull();

        var entries = await db.TeamSheetEntries
            .Where(entry => entry.TeamSheetId == sheet.Id)
            .ToListAsync();

        entries.Should().HaveCount(1);
        entries[0].PlayerId.Should().Be(playerId);
        entries[0].SlotNumber.Should().Be(1);
        entries[0].RoleOverride.Should().BeNull("a null role override round-trips as null");

        var assigned = await db.TacticalSlots.CountAsync(slot => slot.AssignedPlayerId == playerId);
        assigned.Should().Be(0);
    }

    [Fact]
    public async Task A_team_sheet_must_reference_a_real_fixture()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var (clubId, _) = await ArrangeSquadPlayerAsync(scope);

        var plan = Plan(clubId, "Shape", isDefault: true);
        db.TacticalPlans.Add(plan);

        db.FixtureTeamSheets.Add(FixtureTeamSheet.Draft(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            clubId,
            plan.Id,
            plan.Version,
            _fixture.Clock.UtcNow));

        var act = async () => await db.SaveChangesAsync();
        var exception = await act.Should().ThrowAsync<DbUpdateException>();

        ConstraintOf(exception).Should().Be(
            "FK_fixture_team_sheets_fixtures_fixture_id",
            "a prepared side belongs to a fixture that exists");
    }

    /// <summary>Reads the constraint name out of a failed write.</summary>
    private static string? ConstraintOf(
        FluentAssertions.Specialized.ExceptionAssertions<DbUpdateException> exception) =>
        (exception.Which.InnerException as PostgresException)?.ConstraintName;

    private static TacticalPlan Plan(Guid clubId, string name, bool isDefault) => TacticalPlan.Create(
        Guid.CreateVersion7(),
        clubId,
        name,
        FormationPreset.FourFourTwo,
        new TeamInstructionSet
        {
            Mentality = Mentality.Balanced,
            Tempo = Tempo.Normal,
            Passing = PassingStyle.MixedPassing,
            Width = Width.Normal,
            Pressing = Pressing.MidBlock,
            DefensiveLine = DefensiveLine.Normal,
            Tackling = TacklingStyle.Normal,
            TimeWasting = TimeWasting.Off,
        },
        isDefault,
        DateTimeOffset.UnixEpoch);

    private static TacticalSlot Slot(Guid planId, int slotNumber, Guid? assignedPlayerId = null) => TacticalSlot.Place(
        Guid.CreateVersion7(),
        planId,
        slotNumber,
        PositionFamily.Attack,
        PlayerRole.Striker,
        5_000,
        1_000,
        assignedPlayerId,
        DateTimeOffset.UnixEpoch);

    /// <summary>
    /// Creates a division, a season instance, a matchday, a second club, and one fixture between the given
    /// club and it, so a team sheet has a real fixture to reference.
    /// </summary>
    private async Task<Guid> ArrangeFixtureAsync(TouchlineManagerDbContext db, Guid homeClubId)
    {
        var now = _fixture.Clock.UtcNow;

        var club = await db.Clubs.SingleAsync(candidate => candidate.Id == homeClubId);
        var world = await db.GameWorlds.SingleAsync(candidate => candidate.Id == club.WorldId);
        var season = await db.Seasons.FirstAsync(candidate => candidate.WorldId == world.Id);

        var divisionId = Guid.CreateVersion7();
        var division = Division.Provision(divisionId, club.CountryId, tierNumber: 1, "Testland", season.Id, now);
        division.Activate(now);
        db.Divisions.Add(division);

        var divisionSeasonId = Guid.CreateVersion7();
        var divisionSeason = DivisionSeason.Create(
            divisionSeasonId,
            divisionId,
            season.Id,
            $"schedule-{divisionSeasonId:N}",
            $"tie-draw-{divisionSeasonId:N}",
            $"tie-hash-{divisionSeasonId:N}",
            now);
        divisionSeason.Activate(now);
        db.DivisionSeasons.Add(divisionSeason);

        var awayClubId = Guid.CreateVersion7();
        db.Clubs.Add(Club.Generate(
            awayClubId,
            world.Id,
            club.CountryId,
            new ClubIdentity($"Rival {Guid.NewGuid():N}", "RIV", "Rival", "Rival", $"rival-{awayClubId:N}"),
            tier: 1,
            foundingGameYear: season.GameYear,
            now));

        var kickoff = now.AddDays(3);
        var matchdayId = Guid.CreateVersion7();
        db.Matchdays.Add(Matchday.Schedule(matchdayId, divisionSeasonId, roundNumber: 1, kickoff, now));

        var fixtureId = Guid.CreateVersion7();
        db.Fixtures.Add(Fixture.Schedule(fixtureId, matchdayId, homeClubId, awayClubId, kickoff, now));

        await db.SaveChangesAsync();

        return fixtureId;
    }

    /// <summary>Creates a world, country, season, club, and one player with all four satellite rows.</summary>
    private async Task<(Guid ClubId, Guid PlayerId)> ArrangeSquadPlayerAsync(AsyncServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var now = _fixture.Clock.UtcNow;

        var worldId = Guid.CreateVersion7();
        db.GameWorlds.Add(GameWorld.Create(worldId, $"World {Guid.NewGuid():N}", now));
        await db.SaveChangesAsync();

        var countryId = Guid.CreateVersion7();
        db.Countries.Add(Country.Create(countryId, worldId, LaunchCountries.All[0], now));
        await db.SaveChangesAsync();

        var seasonId = Guid.CreateVersion7();
        db.Seasons.Add(Season.Create(
            seasonId,
            worldId,
            sequenceNumber: 1,
            gameYear: 2026,
            WorldRuleSet.Version,
            SeasonCalendar.FirstMatchdayOnOrAfter(new DateOnly(2026, 10, 1)),
            now));
        await db.SaveChangesAsync();

        var clubId = Guid.CreateVersion7();
        db.Clubs.Add(Club.Generate(
            clubId,
            worldId,
            countryId,
            new ClubIdentity($"Squad Vale {Guid.NewGuid():N}", "SQV", "Vale", "Vale", $"seed-{clubId:N}"),
            tier: 1,
            foundingGameYear: 2026,
            now));
        await db.SaveChangesAsync();

        var playerId = Guid.CreateVersion7();
        db.Players.Add(Player.Generate(playerId, worldId, Identity(), now));
        db.PlayerAttributes.Add(PlayerAttributes.Create(
            playerId,
            PlayerAttributeSet.FromValues([.. Enumerable.Repeat(10, AttributeNames.Count)])));
        db.PlayerStates.Add(PlayerState.Open(playerId));
        db.PlayerContracts.Add(PlayerContract.Sign(
            Guid.CreateVersion7(), playerId, clubId, 1, 3, 1_500, SquadStatus.FirstTeam, now));
        db.PlayerRegistrations.Add(PlayerRegistration.Register(
            Guid.CreateVersion7(), playerId, clubId, seasonId, effectiveFixtureBoundaryRound: 0, now));

        await db.SaveChangesAsync();

        return (clubId, playerId);
    }

    private static PlayerIdentity Identity() => new(
        "Squad Player",
        "S. Player",
        "ENG",
        "squad-name-seed",
        BirthGameYear: 2000,
        BirthDayOfYear: 90,
        PreferredFoot.Right,
        HeightCm: 180,
        WeightKg: 76,
        PlayerPosition.CentreBack,
        [],
        Potential: 14,
        Reputation: 12);
}
