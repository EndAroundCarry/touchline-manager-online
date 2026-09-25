using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;
using TouchlineManager.Domain.World;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Infrastructure.Tests.Squad;

/// <summary>
/// The tactics persistence: the plan's concurrency token and the screen's read, against real PostgreSQL
/// 17 (`CONC-1`, master plan §10.4).
/// </summary>
/// <remarks>
/// The concurrency token is the one guarantee the API tests cannot prove: a save with a stale version is
/// refused by the use case's own comparison before the database is reached, so only a direct EF write
/// shows that the row's <c>version</c> really is the token that makes the update conditional.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class TacticsPersistenceTests
{
    private readonly PostgresFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public TacticsPersistenceTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_save_with_a_stale_plan_version_is_refused()
    {
        Guid clubId;

        await using (var seeding = _fixture.CreateScope())
        {
            var db = seeding.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            (clubId, _) = await ArrangeAsync(seeding);

            db.TacticalPlans.Add(Plan(clubId, isDefault: true));
            await db.SaveChangesAsync();
        }

        await using var first = _fixture.CreateScope();
        await using var second = _fixture.CreateScope();

        var firstDb = first.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var secondDb = second.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        // Two editors load the same version.
        var firstPlan = await firstDb.TacticalPlans.SingleAsync(plan => plan.ClubId == clubId);
        var secondPlan = await secondDb.TacticalPlans.SingleAsync(plan => plan.ClubId == clubId);

        firstPlan.Revise("First save", FormationPreset.FourThreeThree, Instructions(), _fixture.Clock.UtcNow);
        await firstDb.SaveChangesAsync();

        secondPlan.Revise("Second save", FormationPreset.FiveThreeTwo, Instructions(), _fixture.Clock.UtcNow);

        // The second save carries the version it read, which is no longer current, so it must not land.
        var act = async () => await secondDb.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>("the plan version is the concurrency token");
    }

    [Fact]
    public async Task The_tactics_read_returns_the_plans_their_slots_and_the_selectable_squad()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var (clubId, (availableId, injuredId)) = await ArrangeAsync(scope);

        var plan = Plan(clubId, isDefault: true);
        db.TacticalPlans.Add(plan);

        var slots = FormationLayouts.DefaultSlots(FormationPreset.FourFourTwo)
            .Select(slot => TacticalSlot.Place(
                Guid.CreateVersion7(),
                plan.Id,
                slot.SlotNumber,
                slot.PositionFamily,
                slot.Role,
                slot.NormalizedX,
                slot.NormalizedY,
                slot.SlotNumber switch
                {
                    1 => availableId,
                    2 => injuredId,
                    _ => null,
                },
                _fixture.Clock.UtcNow))
            .ToList();

        foreach (var slot in slots)
        {
            db.TacticalSlots.Add(slot);
        }

        await db.SaveChangesAsync();

        var queries = scope.ServiceProvider.GetRequiredService<ITacticsQueries>();
        var snapshot = await queries.GetTacticsAsync(clubId, CancellationToken.None);

        snapshot.Should().NotBeNull();
        snapshot!.ClubId.Should().Be(clubId);
        snapshot.SeasonNumber.Should().BeGreaterThan(0);

        snapshot.Plans.Should().ContainSingle();
        snapshot.Plans[0].IsDefault.Should().BeTrue("INS-11");
        snapshot.Plans[0].Slots.Should().HaveCount(FormationLayouts.SlotCount);
        snapshot.Plans[0].Slots.Select(slot => slot.SlotNumber).Should().BeInAscendingOrder();

        var goalkeeper = snapshot.Plans[0].Slots.Single(slot => slot.SlotNumber == 1);
        goalkeeper.AssignedPlayer.Should().NotBeNull();
        goalkeeper.AssignedPlayer!.Id.Should().Be(availableId);
        goalkeeper.AssignedPlayer.IsUnavailable.Should().BeFalse();

        var injured = snapshot.Plans[0].Slots.Single(slot => slot.SlotNumber == 2);
        injured.AssignedPlayer.Should().NotBeNull();
        injured.AssignedPlayer!.IsUnavailable.Should().BeTrue("an open injury marks the player unavailable (TRN-12)");

        snapshot.Plans[0].Slots.Where(slot => slot.SlotNumber > 2)
            .Should().OnlyContain(slot => slot.AssignedPlayer == null, "the rest of the template is empty");

        snapshot.SelectablePlayers.Should().HaveCount(2);
        snapshot.SelectablePlayers.Should().OnlyContain(player => !string.IsNullOrWhiteSpace(player.FullName));
        snapshot.SelectablePlayers.Single(player => player.Id == injuredId).IsUnavailable.Should().BeTrue();
    }

    private static TeamInstructionSet Instructions() => new()
    {
        Mentality = Mentality.Balanced,
        Tempo = Tempo.Normal,
        Passing = PassingStyle.MixedPassing,
        Width = Width.Normal,
        Pressing = Pressing.MidBlock,
        DefensiveLine = DefensiveLine.Normal,
        Tackling = TacklingStyle.Normal,
        TimeWasting = TimeWasting.Off,
    };

    private static TacticalPlan Plan(Guid clubId, bool isDefault) => TacticalPlan.Create(
        Guid.CreateVersion7(),
        clubId,
        "Default shape",
        FormationPreset.FourFourTwo,
        Instructions(),
        isDefault,
        DateTimeOffset.UnixEpoch);

    /// <summary>
    /// Creates a world, country, season, club, and two players — one fit, one injured — each with a
    /// contract and a registration at the club.
    /// </summary>
    private async Task<(Guid ClubId, (Guid AvailableId, Guid InjuredId) Players)> ArrangeAsync(
        AsyncServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var now = _fixture.Clock.UtcNow;

        var worldId = Guid.CreateVersion7();
        db.GameWorlds.Add(GameWorld.Create(worldId, $"Tactics World {Guid.NewGuid():N}", now));

        var countryId = Guid.CreateVersion7();
        db.Countries.Add(Country.Create(countryId, worldId, LaunchCountries.All[0], now));

        var seasonId = Guid.CreateVersion7();
        db.Seasons.Add(Season.Create(
            seasonId,
            worldId,
            sequenceNumber: 1,
            gameYear: 2026,
            WorldRuleSet.Version,
            SeasonCalendar.FirstMatchdayOnOrAfter(new DateOnly(2026, 10, 1)),
            now));

        var clubId = Guid.CreateVersion7();
        db.Clubs.Add(Club.Generate(
            clubId,
            worldId,
            countryId,
            new ClubIdentity($"Tactics Vale {Guid.NewGuid():N}", "TAV", "Vale", "Vale", $"seed-{clubId:N}"),
            tier: 1,
            foundingGameYear: 2026,
            now));

        var availableId = Guid.CreateVersion7();
        var injuredId = Guid.CreateVersion7();

        db.Players.Add(Player.Generate(availableId, worldId, Identity("Keeper One", PlayerPosition.Goalkeeper), now));
        db.Players.Add(Player.Generate(injuredId, worldId, Identity("Defender One", PlayerPosition.CentreBack), now));

        foreach (var playerId in new[] { availableId, injuredId })
        {
            db.PlayerAttributes.Add(PlayerAttributes.Create(
                playerId,
                PlayerAttributeSet.FromValues([.. Enumerable.Repeat(10, AttributeNames.Count)])));
            db.PlayerStates.Add(PlayerState.Open(playerId));
            db.PlayerContracts.Add(PlayerContract.Sign(
                Guid.CreateVersion7(), playerId, clubId, 1, 3, 1_500, SquadStatus.FirstTeam, now));
            db.PlayerRegistrations.Add(PlayerRegistration.Register(
                Guid.CreateVersion7(), playerId, clubId, seasonId, effectiveFixtureBoundaryRound: 0, now));
        }

        db.PlayerUnavailabilities.Add(PlayerUnavailability.Open(
            Guid.CreateVersion7(),
            injuredId,
            clubId,
            UnavailabilityType.Injury,
            InjurySeverity.Minor,
            remainingFixtures: 2,
            sourceFixtureId: null,
            now));

        await db.SaveChangesAsync();

        return (clubId, (availableId, injuredId));
    }

    private static PlayerIdentity Identity(string name, PlayerPosition position) => new(
        name,
        name,
        "ENG",
        $"seed-{name}",
        BirthGameYear: 2000,
        BirthDayOfYear: 90,
        PreferredFoot.Right,
        HeightCm: 182,
        WeightKg: 78,
        position,
        [],
        Potential: 15,
        Reputation: 12);
}
