using FluentAssertions;
using Tactical11Manager.Application.Abstractions.Competition;
using Tactical11Manager.Application.Match;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.MatchEngine.Configuration;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Serialization;

namespace Tactical11Manager.Application.Tests.Match;

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
    public void A_snapshot_freezes_the_colours_a_manager_chose_and_leaves_the_rest_to_the_palette()
    {
        var built = Build(
            Home() with { PrimaryColour = "#c0392b", SecondaryColour = "#fcd116" },
            Away());

        built.Input.Home.PrimaryColour.Should().Be("#c0392b");
        built.Input.Home.SecondaryColour.Should().Be("#fcd116");
        built.Input.Away.PrimaryColour.Should().BeNull("a club whose manager chose nothing wears its generated colours");
        built.Input.Away.SecondaryColour.Should().BeNull();
    }

    [Fact]
    public void A_kit_colour_never_changes_the_snapshots_hashes_so_it_never_changes_a_result()
    {
        var plain = Build(Home(), Away());
        var dressed = Build(
            Home() with { PrimaryColour = "#c0392b", SecondaryColour = "#fcd116" },
            Away() with { PrimaryColour = "#12284c", SecondaryColour = "#ffffff" });

        CanonicalMatchSerializer.ContentHash(dressed.Input)
            .Should().Be(CanonicalMatchSerializer.ContentHash(plain.Input), "the seed is derived from it");
        CanonicalMatchSerializer.InputHash(dressed.Input)
            .Should().Be(CanonicalMatchSerializer.InputHash(plain.Input));
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
        built.Input.HomeAdvantageBasisPoints.Should().Be(EngineRulesV2.Default.HomeAdvantageBasisPoints);
        built.Input.FormulaConfigurationHash.Should().Be(EngineConfiguration.HashOf(EngineRulesV2.Default));
        built.Input.Seed.Should().Be(0, "the seed is derived from the built snapshot, so it cannot be part of it");
    }

    [Fact]
    public void A_plan_with_no_match_instructions_and_the_assistant_on_leaves_the_side_exactly_as_it_was()
    {
        var plain = Build(Home(), Away());
        var disabled = Build(Home() with { MatchInstructions = [Rule("Off") with { IsEnabled = false, Changes = Push() }] }, Away());

        plain.Input.Home.MatchPlan.Should().BeSameAs(MatchPlanV1.None);
        disabled.Input.Home.MatchPlan.Should().BeSameAs(MatchPlanV1.None, "an instruction that is off is left out");
        CanonicalMatchSerializer.CanonicalText(disabled.Input).Should().NotContain("plan.", "no rule means no plan lines, so the hash is what it always was");
        CanonicalMatchSerializer.InputHash(disabled.Input).Should().Be(CanonicalMatchSerializer.InputHash(plain.Input));
    }

    [Fact]
    public void Turning_the_assistant_off_alone_is_a_plan()
    {
        var built = Build(Home() with { AssistantFatigueSubstitutions = false }, Away());

        built.Input.Home.MatchPlan.AssistantFatigueSubstitutions.Should().BeFalse();
        built.Input.Home.MatchPlan.Rules.Should().BeEmpty();
        built.Input.Home.MatchPlan.HasContent.Should().BeTrue();
    }

    [Fact]
    public void A_match_instruction_reaches_the_engine_with_its_shape_instructions_and_substitutions()
    {
        var first = Build(Home(), Away());
        var starter = first.Input.Home.Slots.Single(slot => slot.SlotNumber == 6).ParticipantId;
        var benched = first.Input.Home.Squad.Single(participant => participant.ShirtNumber == 14).ParticipantId;

        var rule = Rule("Chase the game") with
        {
            FromMinute = 60,
            UntilMinute = 85,
            Score = MatchInstructionScore.Trailing,
            ScoreMargin = 2,
            Formation = FormationPreset.ThreeFourThree,
            Overrides = [new MatchInstructionOverride(starter, 11)],
            Changes = new MatchInstructionChanges { Mentality = Mentality.Positive, MentalitySteps = 1, Pressing = Pressing.HighPress, CounterAttack = true },
            Substitutions =
            [
                new MatchInstructionSubstitution(MatchInstructionOff.Player, starter, null, MatchInstructionOn.Player, benched, null),
                new MatchInstructionSubstitution(
                    MatchInstructionOff.MostTiredInFamily, null, PositionFamily.Midfield, MatchInstructionOn.BestFitInFamily, null, PositionFamily.Attack),
            ],
        };

        var built = Build(Home() with { MatchInstructions = [rule] }, Away());

        var plan = built.Input.Home.MatchPlan;
        var engineRule = plan.Rules.Should().ContainSingle().Subject;

        plan.AssistantFatigueSubstitutions.Should().BeTrue();
        engineRule.Ordinal.Should().Be(1);
        engineRule.FromMinute.Should().Be(60);
        engineRule.UntilMinute.Should().Be(85);
        engineRule.Score.Should().Be(MatchRuleScore.Trailing);
        engineRule.ScoreMargin.Should().Be(2);
        engineRule.Event.Should().Be(MatchRuleEvent.None);
        engineRule.FormationCode.Should().Be("3-4-3");
        engineRule.Slots.Should().HaveCount(11);
        engineRule.Slots!.Select(slot => slot.Role).Should().Equal(
            FormationLayouts.DefaultSlots(FormationPreset.ThreeFourThree).Select(slot => EngineVocabulary.Role(slot.Role)));
        engineRule.Slots!.Single(slot => slot.SlotNumber == 1).Family.Should().Be(MatchPositionFamily.Goalkeeper);
        engineRule.Overrides.Should().Equal(new MatchSlotOverrideV1(starter, 11));
        engineRule.Instructions!.Mentality.Should().Be(MatchMentality.Positive);
        engineRule.Instructions.MentalitySteps.Should().Be(1);
        engineRule.Instructions.Pressing.Should().Be(MatchPressing.HighPress);
        engineRule.Instructions.CounterAttack.Should().BeTrue();
        engineRule.Instructions.Tempo.Should().BeNull("an instruction the rule does not name is not changed");
        engineRule.Substitutions.Should().HaveCount(2);
        engineRule.Substitutions[0].Off.Should().Be(MatchRuleOff.Player);
        engineRule.Substitutions[0].OffParticipantId.Should().Be(starter);
        engineRule.Substitutions[0].OnParticipantId.Should().Be(benched);
        engineRule.Substitutions[1].Off.Should().Be(MatchRuleOff.MostTiredInFamily);
        engineRule.Substitutions[1].OffFamily.Should().Be(MatchPositionFamily.Midfield);
        engineRule.Substitutions[1].OnFamily.Should().Be(MatchPositionFamily.Attack);
        CanonicalMatchSerializer.InputHash(built.Input).Should().NotBe(CanonicalMatchSerializer.InputHash(first.Input));
    }

    [Fact]
    public void An_instruction_keeps_the_place_the_manager_gave_it_when_an_earlier_one_is_off()
    {
        var built = Build(
            Home() with
            {
                MatchInstructions =
                [
                    Rule("Off") with { IsEnabled = false, Changes = Push() },
                    Rule("Second") with { Changes = Push() },
                    Rule("Third") with { Changes = Push(), FromMinute = 80 },
                ],
            },
            Away());

        built.Input.Home.MatchPlan.Rules.Select(rule => rule.Ordinal).Should().Equal(2, 3);
    }

    [Fact]
    public void Placements_and_substitutions_naming_players_who_are_not_on_the_sheet_are_dropped()
    {
        var first = Build(Home(), Away());
        var starter = first.Input.Home.Slots.Single(slot => slot.SlotNumber == 6).ParticipantId;
        var stranger = Guid.Parse("99999999-9999-9999-9999-999999999999");

        var rule = Rule("Mixed") with
        {
            Formation = FormationPreset.FourThreeThree,
            Overrides = [new MatchInstructionOverride(stranger, 5), new MatchInstructionOverride(starter, 6)],
            Substitutions =
            [
                new MatchInstructionSubstitution(MatchInstructionOff.Player, stranger, null, MatchInstructionOn.BestFitInFamily, null, PositionFamily.Attack),
                new MatchInstructionSubstitution(MatchInstructionOff.Player, starter, null, MatchInstructionOn.Player, stranger, null),
                new MatchInstructionSubstitution(MatchInstructionOff.Player, starter, null, MatchInstructionOn.BestFitInFamily, null, PositionFamily.Attack),
            ],
        };

        var built = Build(Home() with { MatchInstructions = [rule] }, Away());

        var engineRule = built.Input.Home.MatchPlan.Rules.Should().ContainSingle().Subject;

        engineRule.Overrides.Should().Equal(new MatchSlotOverrideV1(starter, 6));
        engineRule.Substitutions.Should().ContainSingle().Which.OffParticipantId.Should().Be(starter);
    }

    [Fact]
    public void An_instruction_that_ends_up_changing_nothing_is_dropped()
    {
        var stranger = Guid.Parse("99999999-9999-9999-9999-999999999999");

        var rule = Rule("Gone") with
        {
            Substitutions = [new MatchInstructionSubstitution(MatchInstructionOff.Player, stranger, null, MatchInstructionOn.BestFitInFamily, null, PositionFamily.Attack)],
        };

        var built = Build(Home() with { MatchInstructions = [rule] }, Away());

        built.Input.Home.MatchPlan.Should().BeSameAs(MatchPlanV1.None);
    }

    [Theory]
    [InlineData(Mentality.Defensive, MatchMentality.Defensive)]
    [InlineData(Mentality.Balanced, MatchMentality.Balanced)]
    [InlineData(Mentality.Attacking, MatchMentality.Attacking)]
    public void The_opponents_mentality_arrives_as_the_engines(Mentality domain, MatchMentality engine)
    {
        var rule = Rule("Answer") with
        {
            Event = MatchInstructionEvent.OpponentMentalityAtLeast,
            EventThreshold = (int)domain,
            Changes = Push(),
        };

        var built = Build(Home() with { MatchInstructions = [rule] }, Away());

        built.Input.Home.MatchPlan.Rules.Single().EventThreshold.Should().Be((int)engine);
    }

    [Fact]
    public void Both_clubs_carry_their_own_plans()
    {
        var built = Build(
            Home() with { MatchInstructions = [Rule("Home push") with { Changes = Push() }] },
            Away() with { MatchInstructions = [Rule("Away dig in") with { Changes = new MatchInstructionChanges { MentalitySteps = -1 } }] });

        built.Input.Home.MatchPlan.Rules.Single().Instructions!.MentalitySteps.Should().Be(1);
        built.Input.Away.MatchPlan.Rules.Single().Instructions!.MentalitySteps.Should().Be(-1);
    }

    private static MatchInstructionDefinition Rule(string name) => new() { Name = name };

    private static MatchInstructionChanges Push() => new() { MentalitySteps = 1 };

    private static readonly Guid HomeKeeper = Guid.Parse("11111111-1111-1111-1111-000000000001");
    private static readonly Guid HomeBackupKeeper = Guid.Parse("11111111-1111-1111-1111-000000000002");

    private static BuiltSnapshot Build(ClubSideSource home, ClubSideSource away) =>
        MatchSnapshotBuilder.Build(
            new FixtureSidesSnapshot(FixtureId, SeasonId, WorldId, home, away, []),
            EngineRulesV2.Default);

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
