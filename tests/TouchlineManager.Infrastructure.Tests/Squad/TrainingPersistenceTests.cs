using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Application.Squad;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;
using TouchlineManager.Domain.Squad.Training;
using TouchlineManager.Domain.World;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Infrastructure.Tests.Squad;

/// <summary>
/// The training read and the progression roster load, against real PostgreSQL 17 (master plan §10.4,
/// §7.2; `TRN-1`, `TRN-2`, `TRN-9`).
/// </summary>
/// <remarks>
/// These prove the ports' SQL: that a club's plan and each player's programme and attributes come back
/// together with the squad in one read, that a player's recent training days come back oldest first, and
/// that the progression roster falls back to the implicit default plan for a club that has not set one, so
/// the daily run has something legal to apply.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class TrainingPersistenceTests
{
    private readonly PostgresFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public TrainingPersistenceTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task The_training_read_returns_the_plan_and_each_players_programme_and_attributes()
    {
        Guid clubId;
        Guid focusedId;
        Guid plainId;

        await using (var seeding = _fixture.CreateScope())
        {
            var db = seeding.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            (clubId, focusedId, plainId) = await ArrangeAsync(seeding);

            db.TrainingPlans.Add(TrainingPlan.Set(
                Guid.CreateVersion7(),
                clubId,
                TrainingIntensity.Intense,
                DateOnly.FromDateTime(_fixture.Clock.UtcNow.UtcDateTime),
                _fixture.Clock.UtcNow));

            db.PlayerTrainingFocuses.Add(PlayerTrainingFocus.Set(
                Guid.CreateVersion7(),
                focusedId,
                clubId,
                TrainingProgramme.Forward,
                DateOnly.FromDateTime(_fixture.Clock.UtcNow.UtcDateTime),
                _fixture.Clock.UtcNow));

            await db.SaveChangesAsync();
        }

        await using var scope = _fixture.CreateScope();
        var snapshot = await scope.ServiceProvider
            .GetRequiredService<ITrainingQueries>()
            .GetTrainingAsync(clubId, CancellationToken.None);

        snapshot.Should().NotBeNull();
        snapshot!.ClubId.Should().Be(clubId);
        snapshot.Plan.Should().NotBeNull();
        snapshot.Plan!.Intensity.Should().Be(TrainingIntensity.Intense, "TRN-1");
        snapshot.Plan.Version.Should().Be(1);

        snapshot.Players.Should().HaveCount(2);

        var focused = snapshot.Players.Single(player => player.Id == focusedId);
        focused.Programme.Should().Be(TrainingProgramme.Forward, "TRN-2");
        focused.FocusVersion.Should().Be(1);
        focused.Attributes.Values.Should().HaveCount(AttributeNames.Count).And.OnlyContain(value => value == 10);

        var plain = snapshot.Players.Single(player => player.Id == plainId);
        plain.Programme.Should().BeNull("a player with no override trains the position default");
        plain.FocusVersion.Should().BeNull();
    }

    [Fact]
    public async Task The_roster_load_carries_the_club_plan_and_an_implicit_default()
    {
        Guid withPlanClubId;
        Guid withoutPlanClubId;
        Guid focusedId;

        await using (var seeding = _fixture.CreateScope())
        {
            var db = seeding.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            (withPlanClubId, focusedId, _) = await ArrangeAsync(seeding);

            (withoutPlanClubId, _, _) = await ArrangeAnotherClubAsync(seeding);

            db.TrainingPlans.Add(TrainingPlan.Set(
                Guid.CreateVersion7(),
                withPlanClubId,
                TrainingIntensity.Light,
                DateOnly.FromDateTime(_fixture.Clock.UtcNow.UtcDateTime),
                _fixture.Clock.UtcNow));

            db.PlayerTrainingFocuses.Add(PlayerTrainingFocus.Set(
                Guid.CreateVersion7(),
                focusedId,
                withPlanClubId,
                TrainingProgramme.Physical,
                DateOnly.FromDateTime(_fixture.Clock.UtcNow.UtcDateTime),
                _fixture.Clock.UtcNow));

            await db.SaveChangesAsync();
        }

        await using var scope = _fixture.CreateScope();
        var rosters = await scope.ServiceProvider
            .GetRequiredService<ITrainingRepository>()
            .LoadRostersAsync(CancellationToken.None);

        var withPlan = rosters.Single(roster => roster.ClubId == withPlanClubId);

        withPlan.Intensity.Should().Be(TrainingIntensity.Light, "the club's own plan is carried");
        withPlan.Players.Should().HaveCount(2);
        withPlan.Players.Single(player => player.Player.Id == focusedId).Programme
            .Should().Be(TrainingProgramme.Physical, "the manager's override is carried (TRN-1)");

        // A club that has never set a plan still progresses, on the implicit default the mapper defines.
        var withoutPlan = rosters.Single(roster => roster.ClubId == withoutPlanClubId);

        withoutPlan.Intensity.Should().Be(TrainingMapping.DefaultIntensity);
        withoutPlan.Players.Should().NotBeEmpty();
        withoutPlan.Players.Should().OnlyContain(
            player => player.Programme == null,
            "a player with no override trains the position default");
    }

    [Fact]
    public async Task A_programme_override_survives_a_round_trip_and_a_revision()
    {
        Guid clubId;
        Guid playerId;

        await using (var seeding = _fixture.CreateScope())
        {
            var db = seeding.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            (clubId, playerId, _) = await ArrangeAsync(seeding);

            db.PlayerTrainingFocuses.Add(PlayerTrainingFocus.Set(
                Guid.CreateVersion7(),
                playerId,
                clubId,
                TrainingProgramme.Forward,
                DateOnly.FromDateTime(_fixture.Clock.UtcNow.UtcDateTime),
                _fixture.Clock.UtcNow));

            await db.SaveChangesAsync();
        }

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
            var focus = await db.PlayerTrainingFocuses.SingleAsync(row => row.PlayerId == playerId);

            focus.Programme.Should().Be(TrainingProgramme.Forward);

            focus.Revise(
                TrainingProgramme.Winger,
                DateOnly.FromDateTime(_fixture.Clock.UtcNow.UtcDateTime),
                _fixture.Clock.UtcNow);

            await db.SaveChangesAsync();
        }

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
            var focus = await db.PlayerTrainingFocuses.AsNoTracking().SingleAsync(row => row.PlayerId == playerId);

            focus.Programme.Should().Be(TrainingProgramme.Winger);
            focus.Version.Should().Be(2);

            var retiredFamily = await db.Database
                .SqlQuery<bool>($"select focus_family is null as \"Value\" from squad.player_training_focus where player_id = {playerId}")
                .SingleAsync();

            retiredFamily.Should().BeTrue("the retired family column is never written");
        }
    }

    [Fact]
    public async Task A_plan_is_stored_without_the_retired_team_focus()
    {
        Guid clubId;

        await using (var seeding = _fixture.CreateScope())
        {
            var db = seeding.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            (clubId, _, _) = await ArrangeAsync(seeding);

            db.TrainingPlans.Add(TrainingPlan.Set(
                Guid.CreateVersion7(),
                clubId,
                TrainingIntensity.Intense,
                DateOnly.FromDateTime(_fixture.Clock.UtcNow.UtcDateTime),
                _fixture.Clock.UtcNow));

            await db.SaveChangesAsync();
        }

        await using var scope = _fixture.CreateScope();
        var scopedDb = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var teamFocusIsNull = await scopedDb.Database
            .SqlQuery<bool>($"select team_focus is null as \"Value\" from squad.training_plans where club_id = {clubId}")
            .SingleAsync();

        teamFocusIsNull.Should().BeTrue("the plan holds only an intensity; the column awaits the contract migration");
    }

    [Fact]
    public async Task The_player_training_read_returns_the_regime_and_the_latest_days_oldest_first()
    {
        Guid clubId;
        Guid playerId;

        await using (var seeding = _fixture.CreateScope())
        {
            var db = seeding.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            (clubId, playerId, _) = await ArrangeAsync(seeding);

            db.TrainingPlans.Add(TrainingPlan.Set(
                Guid.CreateVersion7(),
                clubId,
                TrainingIntensity.Intense,
                DateOnly.FromDateTime(_fixture.Clock.UtcNow.UtcDateTime),
                _fixture.Clock.UtcNow));

            db.PlayerTrainingFocuses.Add(PlayerTrainingFocus.Set(
                Guid.CreateVersion7(),
                playerId,
                clubId,
                TrainingProgramme.Mental,
                DateOnly.FromDateTime(_fixture.Clock.UtcNow.UtcDateTime),
                _fixture.Clock.UtcNow));

            // Inserted newest first, so the order the read returns is the query's and not the insert's.
            for (var offset = 4; offset >= 0; offset--)
            {
                db.PlayerTrainingDays.Add(PlayerTrainingDay.Record(
                    Guid.CreateVersion7(),
                    playerId,
                    new DateOnly(2026, 10, 1).AddDays(offset),
                    offset >= 3 ? TrainingProgramme.Mental : TrainingProgramme.Defender,
                    TrainingIntensity.Normal,
                    Outcome(
                        developmentMilli: 100 * (offset + 1),
                        declineMilli: 0,
                        new AttributeChange(AttributeName.Composure, 1))));
            }

            await db.SaveChangesAsync();
        }

        await using var scope = _fixture.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<ITrainingQueries>();

        var snapshot = await queries.GetPlayerTrainingAsync(playerId, days: 3, CancellationToken.None);

        snapshot.Should().NotBeNull();
        snapshot!.ClubId.Should().Be(clubId, "the read is authorized against the player's own club");
        snapshot.PrimaryPosition.Should().Be(PlayerPosition.CentreBack);
        snapshot.Programme.Should().Be(TrainingProgramme.Mental);
        snapshot.Intensity.Should().Be(TrainingIntensity.Intense);

        snapshot.Days.Select(day => day.Day).Should().Equal(
            new DateOnly(2026, 10, 3),
            new DateOnly(2026, 10, 4),
            new DateOnly(2026, 10, 5));
        snapshot.Days.Select(day => day.Programme).Should().Equal(
            TrainingProgramme.Defender,
            TrainingProgramme.Mental,
            TrainingProgramme.Mental);
        snapshot.Days[0].AttributeChanges.Should().Equal(new AttributeChange(AttributeName.Composure, 1));
        snapshot.Days[0].DevelopmentMilli.Should().Be(300);

        var unknown = await queries.GetPlayerTrainingAsync(Guid.CreateVersion7(), days: 10, CancellationToken.None);

        unknown.Should().BeNull("a player without an active contract has no training to read");
    }

    [Fact]
    public async Task A_training_day_round_trips_and_a_player_has_one_row_per_day()
    {
        Guid playerId;

        await using (var seeding = _fixture.CreateScope())
        {
            var db = seeding.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            (_, playerId, _) = await ArrangeAsync(seeding);

            var day = new DateOnly(2026, 10, 1);

            db.PlayerTrainingDays.Add(PlayerTrainingDay.Record(
                Guid.CreateVersion7(),
                playerId,
                day,
                TrainingProgramme.Defender,
                TrainingIntensity.Intense,
                Outcome(
                    developmentMilli: 1_234,
                    declineMilli: 56,
                    new AttributeChange(AttributeName.Tackling, 1),
                    new AttributeChange(AttributeName.Pace, -1),
                    new AttributeChange(AttributeName.Heading, 2))));

            await db.SaveChangesAsync();
        }

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
            var row = await db.PlayerTrainingDays.AsNoTracking().SingleAsync(candidate => candidate.PlayerId == playerId);

            row.Day.Should().Be(new DateOnly(2026, 10, 1));
            row.Programme.Should().Be(TrainingProgramme.Defender);
            row.Intensity.Should().Be(TrainingIntensity.Intense);
            row.DevelopmentMilli.Should().Be(1_234);
            row.DeclineMilli.Should().Be(56);
            row.PointsGained.Should().Be(3);
            row.PointsLost.Should().Be(1);
            row.ParseChanges().Should().Equal(
                new AttributeChange(AttributeName.Tackling, 1),
                new AttributeChange(AttributeName.Pace, -1),
                new AttributeChange(AttributeName.Heading, 2));
        }

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            db.PlayerTrainingDays.Add(PlayerTrainingDay.Record(
                Guid.CreateVersion7(),
                playerId,
                new DateOnly(2026, 10, 1),
                TrainingProgramme.Defender,
                TrainingIntensity.Normal,
                Outcome(0, 0)));

            var act = () => db.SaveChangesAsync();

            var exception = await act.Should().ThrowAsync<DbUpdateException>();

            (exception.Which.InnerException as PostgresException)?.ConstraintName
                .Should().Be("ux_player_training_days_player_day", "a retried day cannot record twice");
        }
    }

    [Fact]
    public async Task The_database_refuses_an_unknown_programme_and_a_negative_amount()
    {
        Guid playerId;

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        (_, playerId, _) = await ArrangeAsync(scope);

        var unknownProgramme = () => db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            insert into squad.player_training_days
                (id, player_id, day, programme, intensity, development_milli, decline_milli, points_gained, points_lost, attribute_changes)
            values ({Guid.CreateVersion7()}, {playerId}, {new DateOnly(2026, 10, 2)}, 'balanced', 'normal', 0, 0, 0, 0, '[]'::jsonb)
            """);

        var negativeAmount = () => db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            insert into squad.player_training_days
                (id, player_id, day, programme, intensity, development_milli, decline_milli, points_gained, points_lost, attribute_changes)
            values ({Guid.CreateVersion7()}, {playerId}, {new DateOnly(2026, 10, 3)}, 'forward', 'normal', -1, 0, 0, 0, '[]'::jsonb)
            """);

        (await unknownProgramme.Should().ThrowAsync<PostgresException>())
            .Which.ConstraintName.Should().Be("ck_player_training_days_programme");
        (await negativeAmount.Should().ThrowAsync<PostgresException>())
            .Which.ConstraintName.Should().Be("ck_player_training_days_amounts");
    }

    [Fact]
    public async Task The_decline_remainder_is_stored_with_the_player_state()
    {
        Guid playerId;

        await using (var seeding = _fixture.CreateScope())
        {
            var db = seeding.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            (_, playerId, _) = await ArrangeAsync(seeding);

            var state = await db.PlayerStates.SingleAsync(row => row.PlayerId == playerId);

            state.DeclineRemainder.Should().Be(0, "a new state carries no decline");
            state.ApplyProgression(8_000, 100, 5_000, 5_000, 250, 640, new DateOnly(2026, 10, 1));

            await db.SaveChangesAsync();
        }

        await using var scope = _fixture.CreateScope();
        var reloaded = await scope.ServiceProvider
            .GetRequiredService<TouchlineManagerDbContext>()
            .PlayerStates.AsNoTracking().SingleAsync(row => row.PlayerId == playerId);

        reloaded.DevelopmentRemainder.Should().Be(250);
        reloaded.DeclineRemainder.Should().Be(640, "TRN-16");
    }

    private static DailyProgressionOutcome Outcome(
        int developmentMilli,
        int declineMilli,
        params AttributeChange[] changes) =>
        new(
            PlayerAttributeSet.FromValues([.. Enumerable.Repeat(10, AttributeNames.Count)]),
            ConditionBp: 8_000,
            FatigueBp: 100,
            MoraleBp: 5_000,
            MatchSharpnessBp: 5_000,
            DevelopmentRemainder: 0,
            DeclineRemainder: 0,
            developmentMilli,
            declineMilli,
            changes);

    private Task<(Guid ClubId, Guid FocusedId, Guid PlainId)> ArrangeAsync(AsyncServiceScope scope) =>
        ArrangeClubAsync(scope, $"Training Vale {Guid.NewGuid():N}");

    private Task<(Guid ClubId, Guid FocusedId, Guid PlainId)> ArrangeAnotherClubAsync(AsyncServiceScope scope) =>
        ArrangeClubAsync(scope, $"Training Hollow {Guid.NewGuid():N}");

    /// <summary>
    /// Creates (or reuses) a world and season, then a country, a club, and two contracted players.
    /// </summary>
    private async Task<(Guid ClubId, Guid FocusedId, Guid PlainId)> ArrangeClubAsync(
        AsyncServiceScope scope,
        string clubName)
    {
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var now = _fixture.Clock.UtcNow;

        var world = await db.GameWorlds.OrderBy(candidate => candidate.CreatedAt).FirstOrDefaultAsync();

        if (world is null)
        {
            world = GameWorld.Create(Guid.CreateVersion7(), $"Training World {Guid.NewGuid():N}", now);
            db.GameWorlds.Add(world);
        }

        var country = await db.Countries.FirstOrDefaultAsync(candidate => candidate.WorldId == world.Id);

        if (country is null)
        {
            country = Country.Create(Guid.CreateVersion7(), world.Id, LaunchCountries.All[0], now);
            db.Countries.Add(country);
        }

        var season = await db.Seasons.FirstOrDefaultAsync(
            candidate => candidate.WorldId == world.Id && candidate.SequenceNumber == world.CurrentSeasonNumber);

        if (season is null)
        {
            season = Season.Create(
                Guid.CreateVersion7(),
                world.Id,
                sequenceNumber: 1,
                gameYear: 2026,
                WorldRuleSet.Version,
                SeasonCalendar.FirstMatchdayOnOrAfter(new DateOnly(2026, 10, 1)),
                now);

            db.Seasons.Add(season);
        }

        var clubId = Guid.CreateVersion7();
        db.Clubs.Add(Club.Generate(
            clubId,
            world.Id,
            country.Id,
            new ClubIdentity(clubName, "TRN", "Vale", "Vale", $"seed-{clubId:N}"),
            tier: 1,
            foundingGameYear: 2026,
            now));

        var focusedId = Guid.CreateVersion7();
        var plainId = Guid.CreateVersion7();

        db.Players.Add(Player.Generate(focusedId, world.Id, Identity("Focused One"), now));
        db.Players.Add(Player.Generate(plainId, world.Id, Identity("Plain One"), now));

        foreach (var playerId in new[] { focusedId, plainId })
        {
            db.PlayerAttributes.Add(PlayerAttributes.Create(
                playerId,
                PlayerAttributeSet.FromValues([.. Enumerable.Repeat(10, AttributeNames.Count)])));
            db.PlayerStates.Add(PlayerState.Open(playerId));
            db.PlayerContracts.Add(PlayerContract.Sign(
                Guid.CreateVersion7(), playerId, clubId, 1, 3, 1_500, SquadStatus.FirstTeam, now));
        }

        await db.SaveChangesAsync();

        return (clubId, focusedId, plainId);
    }

    private static PlayerIdentity Identity(string name) => new(
        name,
        name,
        "ENG",
        $"seed-{name}",
        BirthGameYear: 2002,
        BirthDayOfYear: 90,
        PreferredFoot.Right,
        HeightCm: 180,
        WeightKg: 76,
        PlayerPosition.CentreBack,
        [],
        Potential: 16,
        Reputation: 12);
}
