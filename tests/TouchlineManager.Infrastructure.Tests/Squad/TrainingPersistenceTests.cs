using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Application.Squad;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;
using TouchlineManager.Domain.World;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Infrastructure.Tests.Squad;

/// <summary>
/// The training read and the progression roster load, against real PostgreSQL 17 (master plan §10.4,
/// §7.2; `TRN-1`, `TRN-2`, `TRN-9`).
/// </summary>
/// <remarks>
/// These prove the ports' SQL: that a club's plan and each player's focus come back together with the
/// squad in one read, and that the progression roster falls back to the implicit default plan for a club
/// that has not set one, so the daily run has something legal to apply.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class TrainingPersistenceTests
{
    private readonly PostgresFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public TrainingPersistenceTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task The_training_read_returns_the_plan_and_each_players_focus()
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
                TrainingFocus.Tactical,
                TrainingIntensity.Intense,
                DateOnly.FromDateTime(_fixture.Clock.UtcNow.UtcDateTime),
                _fixture.Clock.UtcNow));

            db.PlayerTrainingFocuses.Add(PlayerTrainingFocus.Set(
                Guid.CreateVersion7(),
                focusedId,
                clubId,
                AttributeFamily.Technical,
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
        snapshot.Plan!.TeamFocus.Should().Be(TrainingFocus.Tactical, "TRN-1");
        snapshot.Plan.Intensity.Should().Be(TrainingIntensity.Intense);
        snapshot.Plan.Version.Should().Be(1);

        snapshot.Players.Should().HaveCount(2);

        var focused = snapshot.Players.Single(player => player.Id == focusedId);
        focused.FocusFamily.Should().Be(AttributeFamily.Technical, "TRN-2");
        focused.FocusVersion.Should().Be(1);

        var plain = snapshot.Players.Single(player => player.Id == plainId);
        plain.FocusFamily.Should().BeNull("a player with no focus trains with the team");
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
                TrainingFocus.Fitness,
                TrainingIntensity.Light,
                DateOnly.FromDateTime(_fixture.Clock.UtcNow.UtcDateTime),
                _fixture.Clock.UtcNow));

            db.PlayerTrainingFocuses.Add(PlayerTrainingFocus.Set(
                Guid.CreateVersion7(),
                focusedId,
                withPlanClubId,
                AttributeFamily.Physical,
                DateOnly.FromDateTime(_fixture.Clock.UtcNow.UtcDateTime),
                _fixture.Clock.UtcNow));

            await db.SaveChangesAsync();
        }

        await using var scope = _fixture.CreateScope();
        var rosters = await scope.ServiceProvider
            .GetRequiredService<ITrainingRepository>()
            .LoadRostersAsync(CancellationToken.None);

        var withPlan = rosters.Single(roster => roster.ClubId == withPlanClubId);

        withPlan.TeamFocus.Should().Be(TrainingFocus.Fitness, "the club's own plan is carried");
        withPlan.Intensity.Should().Be(TrainingIntensity.Light);
        withPlan.Players.Should().HaveCount(2);
        withPlan.Players.Single(player => player.Player.Id == focusedId).IndividualFocus
            .Should().Be(AttributeFamily.Physical, "TRN-2");

        // A club that has never set a plan still progresses, on the implicit default the mapper defines.
        var withoutPlan = rosters.Single(roster => roster.ClubId == withoutPlanClubId);

        withoutPlan.TeamFocus.Should().Be(TrainingMapping.DefaultTeamFocus);
        withoutPlan.Intensity.Should().Be(TrainingMapping.DefaultIntensity);
        withoutPlan.Players.Should().NotBeEmpty();
        withoutPlan.Players.Should().OnlyContain(player => player.IndividualFocus == null);
    }

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
