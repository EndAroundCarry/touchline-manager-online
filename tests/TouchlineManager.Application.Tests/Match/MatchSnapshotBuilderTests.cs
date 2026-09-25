using FluentAssertions;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Match;
using TouchlineManager.Domain.Squad;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Serialization;

namespace TouchlineManager.Application.Tests.Match;

/// <summary>
/// The snapshot builder: what a fixture is frozen from, and how a side that cannot be fielded is repaired
/// (`MAT-1`, `DIS-6`, `INS-12`).
/// </summary>
/// <remarks>
/// Every test here is about a decision the builder makes without asking anybody: which of the named players
/// can actually play, who replaces the ones who cannot, and what shape a club with no plan takes the field
/// in. The engine's own validator runs at the end of every build, so a snapshot that passes is one the engine
/// would simulate — which makes these tests about the builder rather than about the shape of its output.
/// </remarks>
public sealed class MatchSnapshotBuilderTests
{
    private static readonly Guid FixtureId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SeasonId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid WorldId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static readonly Guid HomeClubId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AwayClubId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void A_club_with_no_plan_takes_the_field_in_the_default_formation()
    {
        var built = Build(Home(), Away());

        var home = built.Input.Home;

        home.Slots.Should().HaveCount(11);
        home.Squad.Should().HaveCount(18, "eleven starters and a bench of seven");
        home.Instructions.Mentality.Should().Be(MatchMentality.Balanced, "a club with no plan is instructed by nobody");
        home.Instructions.Pressing.Should().Be(MatchPressing.MidBlock);
        home.Instructions.Tackling.Should().Be(MatchTacklingStyle.Normal);

        home.Slots.Select(slot => slot.Role).Should().Equal(
            FormationLayouts
                .DefaultSlots(MatchSnapshotBuilder.DefaultFormation)
                .Select(slot => EngineVocabulary.Role(slot.Role)));
    }

    [Fact]
    public void A_club_with_no_plan_has_every_slot_recorded_as_the_builders_choice()
    {
        var built = Build(Home(), Away());

        var home = built.Repairs.Where(repair => repair.ClubId == HomeClubId).ToList();

        home.Should().HaveCount(18, "eleven starters and seven substitutes were all chosen here (DIS-7)");
        home.Should().OnlyContain(repair => repair.Reason == SnapshotRepairReason.SlotEmpty);
        home.Should().OnlyContain(repair => repair.ReplacedPlayerId == null);
        home.Should().OnlyContain(repair => repair.ReplacementPlayerId != null);
        home.Select(repair => repair.SlotNumber).Should().BeInAscendingOrder();
    }

    [Fact]
    public void A_snapshot_carries_both_clubs_repairs_apart()
    {
        var built = Build(Home(), Away());

        built.Repairs.Select(repair => repair.ClubId).Distinct().Should().BeEquivalentTo([HomeClubId, AwayClubId]);
    }

    [Fact]
    public void The_eleven_fields_exactly_one_recognised_goalkeeper()
    {
        var built = Build(Home(), Away());

        var starters = built.Input.Home.Starters();

        starters.Count(participant => participant.IsGoalkeeper).Should().Be(1, "the engine refuses anything else (SQ-2)");
        starters.Single(participant => participant.IsGoalkeeper).PlayerId.Should().Be(HomeKeeper);
    }

    [Fact]
    public void A_prepared_sheet_is_honoured_slot_for_slot()
    {
        var home = Home();
        var reserve = home.Players.First(player => player.PrimaryPosition == PlayerPosition.RightWinger);

        var prepared = home with
        {
            Selection =
            [
                new SnapshotSelectionRow(1, HomeKeeper, null),
                new SnapshotSelectionRow(3, reserve.PlayerId, null),
            ],
        };

        var built = Build(prepared, Away());

        built.Input.Home.Slots.Single(slot => slot.SlotNumber == 1).ParticipantId.Should().Be(HomeKeeper);
        built.Input.Home.Slots.Single(slot => slot.SlotNumber == 3).ParticipantId.Should().Be(reserve.PlayerId);
        built.Repairs
            .Should()
            .NotContain(repair => repair.ClubId == HomeClubId && repair.ReplacedPlayerId != null);
    }

    [Fact]
    public void An_unavailable_player_is_replaced_and_the_repair_names_both_players()
    {
        var home = Unavailable(Home(), HomeKeeper);

        var prepared = home with
        {
            Selection = [new SnapshotSelectionRow(1, HomeKeeper, null)],
        };

        var built = Build(prepared, Away());

        var repair = built.Repairs.Single(candidate => candidate.ClubId == HomeClubId && candidate.SlotNumber == 1);

        repair.Reason.Should().Be(SnapshotRepairReason.PlayerUnavailable);
        repair.ReplacedPlayerId.Should().Be(HomeKeeper);
        repair.ReplacementPlayerId
            .Should()
            .Be(HomeBackupKeeper, "the other goalkeeper is the only one who can take the slot");
    }

    [Fact]
    public void A_player_who_is_no_longer_eligible_is_replaced()
    {
        var stranger = Guid.CreateVersion7();

        var prepared = Home() with
        {
            Selection = [new SnapshotSelectionRow(1, stranger, null)],
        };

        var built = Build(prepared, Away());

        var repair = built.Repairs.Single(candidate => candidate.ClubId == HomeClubId && candidate.SlotNumber == 1);

        repair.Reason.Should().Be(SnapshotRepairReason.PlayerIneligible);
        repair.ReplacedPlayerId.Should().Be(stranger);
        repair.ReplacementPlayerId.Should().Be(HomeKeeper);
    }

    [Fact]
    public void A_player_named_twice_keeps_the_first_slot_and_is_repaired_in_the_second()
    {
        var prepared = Home() with
        {
            Selection =
            [
                new SnapshotSelectionRow(1, HomeKeeper, null),
                new SnapshotSelectionRow(3, HomeKeeper, null),
            ],
        };

        var built = Build(prepared, Away());

        built.Input.Home.Slots.Single(slot => slot.SlotNumber == 1).ParticipantId.Should().Be(HomeKeeper);
        built.Repairs
            .Should()
            .Contain(repair => repair.ClubId == HomeClubId
                && repair.SlotNumber == 3
                && repair.Reason == SnapshotRepairReason.PlayerDuplicated);
    }

    [Fact]
    public void An_outfield_player_named_in_goal_is_repaired_as_a_missing_goalkeeper()
    {
        var home = Home();
        var defender = home.Players.First(player => player.PrimaryPosition == PlayerPosition.CentreBack);

        var prepared = home with
        {
            Selection = [new SnapshotSelectionRow(1, defender.PlayerId, null)],
        };

        var built = Build(prepared, Away());

        var repair = built.Repairs.Single(candidate => candidate.ClubId == HomeClubId && candidate.SlotNumber == 1);

        repair.Reason.Should().Be(SnapshotRepairReason.GoalkeeperRequired);
        repair.ReplacedPlayerId.Should().Be(defender.PlayerId);
        repair.ReplacementPlayerId.Should().Be(HomeKeeper);
    }

    [Fact]
    public void A_goalkeeper_named_outfield_is_repaired_as_a_surplus_goalkeeper()
    {
        var prepared = Home() with
        {
            Selection = [new SnapshotSelectionRow(5, HomeBackupKeeper, null)],
        };

        var built = Build(prepared, Away());

        built.Repairs
            .Should()
            .Contain(repair => repair.ClubId == HomeClubId
                && repair.SlotNumber == 5
                && repair.Reason == SnapshotRepairReason.GoalkeeperSurplus
                && repair.ReplacedPlayerId == HomeBackupKeeper);

        built.Input.Home.Starters().Count(participant => participant.IsGoalkeeper).Should().Be(1);
    }

    [Fact]
    public void A_club_with_no_available_goalkeeper_cannot_field_a_side()
    {
        var home = Home() with
        {
            Players = [.. Home().Players.Where(player => player.PrimaryPosition != PlayerPosition.Goalkeeper)],
        };

        var act = () => Build(home, Away());

        act.Should()
            .Throw<UnplayableSquadException>()
            .Where(exception => exception.ClubId == HomeClubId && exception.Message.Contains("goalkeeper"));
    }

    [Fact]
    public void The_bench_carries_at_most_one_goalkeeper_and_never_the_same_player_twice()
    {
        var built = Build(Home(), Away());

        var starters = built.Input.Home.Starters().Select(participant => participant.ParticipantId).ToHashSet();
        var bench = built.Input.Home.Squad
            .Where(participant => !starters.Contains(participant.ParticipantId))
            .ToList();

        bench.Should().HaveCount(7);
        bench.Count(participant => participant.IsGoalkeeper).Should().BeLessThanOrEqualTo(1);
        bench.Select(participant => participant.ParticipantId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void A_bench_entry_that_cannot_play_is_dropped_rather_than_replaced()
    {
        var home = Home();
        var injured = home.Players.First(player => player.PrimaryPosition == PlayerPosition.Striker);

        var prepared = Unavailable(home, injured.PlayerId) with
        {
            Selection = [new SnapshotSelectionRow(12, injured.PlayerId, null)],
        };

        var built = Build(prepared, Away());

        built.Repairs
            .Should()
            .Contain(repair => repair.ClubId == HomeClubId
                && repair.SlotNumber == 12
                && repair.Reason == SnapshotRepairReason.PlayerUnavailable
                && repair.ReplacementPlayerId == null);
    }

    [Fact]
    public void The_same_facts_always_build_the_same_snapshot()
    {
        var first = Build(Home(), Away());
        var second = Build(Home(), Away());

        CanonicalMatchSerializer.ContentHash(first.Input)
            .Should()
            .Be(
                CanonicalMatchSerializer.ContentHash(second.Input),
                "a snapshot is identified by its whole content (MAT-9)");

        second.Repairs.Should().Equal(first.Repairs);
    }

    [Fact]
    public void A_plan_that_is_not_eleven_slots_is_refused()
    {
        var shortPlan = FormationLayouts
            .DefaultSlots(FormationPreset.FourFourTwo)
            .Take(10)
            .Select(slot => new SnapshotSlotRow(
                slot.SlotNumber,
                slot.PositionFamily,
                slot.Role,
                slot.NormalizedX,
                slot.NormalizedY))
            .ToList();

        var home = Home() with
        {
            Slots = shortPlan,
            Instructions = TeamInstructionSet.Neutral,
        };

        var act = () => Build(home, Away());

        act.Should()
            .Throw<UnplayableSquadException>()
            .Where(exception => exception.Message.Contains("10 starting slots"));
    }

    [Fact]
    public void The_snapshot_records_the_fixture_it_was_built_for()
    {
        var built = Build(Home(), Away());

        built.Input.FixtureId.Should().Be(FixtureId);
        built.Input.WorldId.Should().Be(WorldId);
        built.Input.SeasonId.Should().Be(SeasonId);
        built.Input.HomeAdvantageBasisPoints.Should().Be(EngineRulesV1.Default.HomeAdvantageBasisPoints);
        built.Input.FormulaConfigurationHash.Should().Be(EngineConfiguration.HashOf(EngineRulesV1.Default));
        built.Input.Seed.Should().Be(0, "the seed is derived from the built snapshot, so it cannot be part of it");
    }

    private static readonly Guid HomeKeeper = Guid.Parse("11111111-1111-1111-1111-000000000001");
    private static readonly Guid HomeBackupKeeper = Guid.Parse("11111111-1111-1111-1111-000000000002");

    private static BuiltSnapshot Build(ClubSideSource home, ClubSideSource away) =>
        MatchSnapshotBuilder.Build(
            new FixtureSidesSnapshot(FixtureId, SeasonId, WorldId, home, away, []),
            EngineRulesV1.Default);

    /// <summary>An opponent with a full squad and nothing prepared.</summary>
    private static ClubSideSource Away() =>
        Side(
            AwayClubId,
            "Away",
            Guid.Parse("22222222-2222-2222-2222-000000000001"),
            Guid.Parse("22222222-2222-2222-2222-000000000002"));

    /// <summary>The home club's side source.</summary>
    private static ClubSideSource Home() => Side(HomeClubId, "Home", HomeKeeper, HomeBackupKeeper);

    /// <summary>Marks one player as carrying an open injury or suspension.</summary>
    private static ClubSideSource Unavailable(ClubSideSource side, Guid playerId) => side with
    {
        Players =
        [
            .. side.Players.Select(player => player.PlayerId == playerId
                ? player with { IsAvailable = false }
                : player),
        ],
    };

    /// <summary>Builds a club's side source: eighteen players, the first two of them goalkeepers.</summary>
    private static ClubSideSource Side(Guid clubId, string name, params Guid[] goalkeepers)
    {
        var players = new List<SnapshotPlayerRow>();

        foreach (var goalkeeper in goalkeepers)
        {
            players.Add(Player(goalkeeper, PlayerPosition.Goalkeeper));
        }

        players.AddRange(
        [
            Player(GuidFor(clubId, 3), PlayerPosition.RightBack),
            Player(GuidFor(clubId, 4), PlayerPosition.CentreBack),
            Player(GuidFor(clubId, 5), PlayerPosition.CentreBack),
            Player(GuidFor(clubId, 6), PlayerPosition.LeftBack),
            Player(GuidFor(clubId, 7), PlayerPosition.RightBack),
            Player(GuidFor(clubId, 8), PlayerPosition.DefensiveMidfielder),
            Player(GuidFor(clubId, 9), PlayerPosition.CentralMidfielder),
            Player(GuidFor(clubId, 10), PlayerPosition.CentralMidfielder),
            Player(GuidFor(clubId, 11), PlayerPosition.CentralMidfielder),
            Player(GuidFor(clubId, 12), PlayerPosition.AttackingMidfielder),
            Player(GuidFor(clubId, 13), PlayerPosition.RightWinger),
            Player(GuidFor(clubId, 14), PlayerPosition.LeftWinger),
            Player(GuidFor(clubId, 15), PlayerPosition.Striker),
            Player(GuidFor(clubId, 16), PlayerPosition.Striker),
            Player(GuidFor(clubId, 17), PlayerPosition.Striker),
            Player(GuidFor(clubId, 18), PlayerPosition.RightWinger),
        ]);

        return new ClubSideSource(clubId, name, null, [], [], players);
    }

    private static Guid GuidFor(Guid clubId, int slot) =>
        Guid.Parse($"{clubId.ToString("N")[..28]}{slot:D4}");

    private static SnapshotPlayerRow Player(Guid playerId, PlayerPosition position) => new(
        playerId,
        $"Player {playerId.ToString("N")[^4..]}",
        $"P{playerId.ToString("N")[^2..]}",
        position,
        [],
        [.. Enumerable.Repeat(10, 28)],
        10_000,
        0,
        PlayerState.NeutralBasisPoints,
        PlayerState.NeutralBasisPoints,
        true);
}
