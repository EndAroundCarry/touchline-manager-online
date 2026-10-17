using FluentAssertions;
using Tactical11Manager.MatchEngine.Configuration;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Simulation;

namespace Tactical11Manager.MatchEngine.Tests;

/// <summary>
/// Verifies the runner that carries out a side's match instructions: when a rule fires, what it does, and what it leaves alone
/// (`match-instructions-v1`, Milestone 2).
/// </summary>
public sealed class MatchPlanRunnerTests
{
    private static readonly EngineRulesV2 Rules = EngineRulesV2.Default;

    /// <summary>The home side's starter in a slot: slot 1 is the keeper, 2…5 the back four, 6…9 midfield, 10 and 11 the strikers.</summary>
    private static Guid Starter(int slot) => TestMatchFactory.Identity(10_100 + slot);

    /// <summary>The home side's substitute: 1 a keeper, 2 a centre back, 3 a left back, 4 and 5 midfielders, 6 a striker, 7 a winger.</summary>
    private static Guid Bench(int number) => TestMatchFactory.Identity(10_150 + number);

    private static MatchState Create(MatchPlanV1? home = null, MatchPlanV1? away = null)
    {
        var input = TestMatchFactory.Even();

        return TickTestMatchState.Create(
            input with
            {
                Home = input.Home with { MatchPlan = home ?? MatchPlanV1.None },
                Away = input.Away with { MatchPlan = away ?? MatchPlanV1.None },
            });
    }

    private static MatchPlanV1 Plan(params MatchRuleV1[] rules) => new() { Rules = rules };

    /// <summary>Puts the clock in a displayed minute, in the half that minute belongs to.</summary>
    private static void At(MatchState state, int minute)
    {
        state.InFirstHalf = minute <= 45;
        state.ClockSeconds = ((minute - 1) * 60) + 30;
        state.Minute.Should().Be(minute);
    }

    private static void Score(MatchState state, MatchSide side, int goals)
    {
        for (var goal = 0; goal < goals; goal++)
        {
            state.Emit(side, EngineEventType.Goal);
        }
    }

    private static void Tire(SideRuntime side, Guid id, int by)
    {
        var index = side.Active.FindIndex(slot => slot.Participant.ParticipantId == id);

        side.Active[index] = side.Active[index] with { Condition = side.Active[index].Condition.WithConditionDelta(-by) };
    }

    private static void SendOff(SideRuntime side, Guid id)
    {
        side.SentOff.Add(id);
        side.RemoveParticipant(id, 30, Rules);
    }

    private static List<EngineEventV1> Of(MatchState state, EngineEventType type) => [.. state.Events.Where(matchEvent => matchEvent.Type == type)];

    private static readonly MatchInstructionChangesV1 Attack = new() { Mentality = MatchMentality.Attacking };

    // ---- When a rule fires --------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_side_with_no_rules_is_left_alone()
    {
        var state = Create();
        var before = state.Home.Instructions;

        MatchPlanRunner.HasRules(state).Should().BeFalse();

        MatchPlanRunner.ConsiderBothSides(state);

        state.Events.Should().BeEmpty();
        state.Home.Instructions.Should().BeSameAs(before);
    }

    [Fact]
    public void A_rule_fires_once_and_not_again_when_its_conditions_still_hold()
    {
        var state = Create(Plan(new MatchRuleV1 { Ordinal = 1, Instructions = Attack }));

        MatchPlanRunner.Consider(state, MatchSide.Home);

        state.Home.Instructions.Mentality.Should().Be(MatchMentality.Attacking);
        state.Home.FiredRules.Should().Equal(1);

        state.Home.ChangeInstructions(state.Home.Instructions with { Mentality = MatchMentality.Defensive }, Rules);
        MatchPlanRunner.Consider(state, MatchSide.Home);

        state.Home.Instructions.Mentality.Should().Be(MatchMentality.Defensive, "the rule has been used");
        Of(state, EngineEventType.TacticalChange).Should().ContainSingle();
    }

    [Fact]
    public void Rules_are_checked_in_the_order_the_plan_lists_them_and_each_sees_what_the_one_before_left()
    {
        // Listed first is ordinal 2: it sets Defensive, then ordinal 1 moves one step up from there. Reading by ordinal would give Balanced + 1.
        var state = Create(
            Plan(
                new MatchRuleV1 { Ordinal = 2, Instructions = new MatchInstructionChangesV1 { Mentality = MatchMentality.Defensive } },
                new MatchRuleV1 { Ordinal = 1, Instructions = new MatchInstructionChangesV1 { MentalitySteps = 1 } }));

        MatchPlanRunner.Consider(state, MatchSide.Home);

        state.Home.Instructions.Mentality.Should().Be(MatchMentality.Cautious);
        Of(state, EngineEventType.TacticalChange).Select(matchEvent => matchEvent.RuleOrdinal).Should().Equal(2, 1);
    }

    [Fact]
    public void A_rule_is_judged_on_the_match_the_rules_before_it_left()
    {
        // The first rule takes the tired midfielder off, so the second, which waits for a tired player, finds nobody.
        var state = Create(
            Plan(
                new MatchRuleV1
                {
                    Ordinal = 1,
                    Substitutions = [new MatchRuleSubstitutionV1 { Off = MatchRuleOff.Player, OffParticipantId = Starter(8), On = MatchRuleOn.Player, OnParticipantId = Bench(4) }],
                },
                new MatchRuleV1 { Ordinal = 2, Event = MatchRuleEvent.Tired, EventThreshold = 6_000, Instructions = Attack }));

        Tire(state.Home, Starter(8), 3_000);

        MatchPlanRunner.Consider(state, MatchSide.Home);

        state.Home.FiredRules.Should().Equal(1);
        state.Home.Instructions.Mentality.Should().Be(MatchMentality.Balanced);
    }

    [Theory]
    [InlineData(MatchRuleScore.Any, 1, 0, 0, true)]
    [InlineData(MatchRuleScore.Any, 1, 0, 4, true)]
    [InlineData(MatchRuleScore.Leading, 2, 2, 0, true)]
    [InlineData(MatchRuleScore.Leading, 2, 3, 1, true)]
    [InlineData(MatchRuleScore.Leading, 2, 2, 1, false)]
    [InlineData(MatchRuleScore.Level, 1, 1, 1, true)]
    [InlineData(MatchRuleScore.Level, 1, 1, 0, false)]
    [InlineData(MatchRuleScore.Trailing, 1, 0, 1, true)]
    [InlineData(MatchRuleScore.Trailing, 2, 0, 1, false)]
    [InlineData(MatchRuleScore.Trailing, 2, 1, 3, true)]
    [InlineData(MatchRuleScore.NotWinning, 1, 1, 1, true)]
    [InlineData(MatchRuleScore.NotWinning, 1, 0, 1, true)]
    [InlineData(MatchRuleScore.NotWinning, 1, 2, 1, false)]
    [InlineData(MatchRuleScore.NotLosing, 1, 1, 1, true)]
    [InlineData(MatchRuleScore.NotLosing, 1, 2, 1, true)]
    [InlineData(MatchRuleScore.NotLosing, 1, 0, 1, false)]
    public void A_score_condition_is_read_from_the_goals_of_the_sides_point_of_view(
        MatchRuleScore score,
        int margin,
        int homeGoals,
        int awayGoals,
        bool fires)
    {
        var state = Create(Plan(new MatchRuleV1 { Ordinal = 1, Score = score, ScoreMargin = margin, Instructions = Attack }));

        Score(state, MatchSide.Home, homeGoals);
        Score(state, MatchSide.Away, awayGoals);

        MatchPlanRunner.Consider(state, MatchSide.Home);

        state.Home.FiredRules.Contains(1).Should().Be(fires);
    }

    [Fact]
    public void The_away_side_reads_the_score_from_its_own_end()
    {
        var state = Create(away: Plan(new MatchRuleV1 { Ordinal = 1, Score = MatchRuleScore.Trailing, Instructions = Attack }));

        Score(state, MatchSide.Home, 1);

        MatchPlanRunner.Consider(state, MatchSide.Away);

        state.Away.Instructions.Mentality.Should().Be(MatchMentality.Attacking);
    }

    [Fact]
    public void Losing_by_one_from_the_sixtieth_minute_fires_at_the_first_stoppage_after_it_and_not_before()
    {
        var state = Create(Plan(new MatchRuleV1 { Ordinal = 1, FromMinute = 60, Score = MatchRuleScore.Trailing, Instructions = Attack }));

        At(state, 30);
        Score(state, MatchSide.Away, 1);
        MatchPlanRunner.Consider(state, MatchSide.Home);
        state.Home.FiredRules.Should().BeEmpty("it is not the sixtieth minute yet");

        At(state, 59);
        MatchPlanRunner.Consider(state, MatchSide.Home);
        state.Home.FiredRules.Should().BeEmpty();

        At(state, 61);
        MatchPlanRunner.Consider(state, MatchSide.Home);
        state.Home.Instructions.Mentality.Should().Be(MatchMentality.Attacking);
        Of(state, EngineEventType.TacticalChange).Single().Minute.Should().Be(61);
    }

    [Fact]
    public void A_rule_with_an_until_minute_lapses_after_it()
    {
        var state = Create(Plan(new MatchRuleV1 { Ordinal = 1, FromMinute = 10, UntilMinute = 20, Instructions = Attack }));

        At(state, 5);
        MatchPlanRunner.Consider(state, MatchSide.Home);
        At(state, 21);
        MatchPlanRunner.Consider(state, MatchSide.Home);

        state.Home.FiredRules.Should().BeEmpty();

        At(state, 20);
        MatchPlanRunner.Consider(state, MatchSide.Home);

        state.Home.FiredRules.Should().Equal(1);
    }

    [Fact]
    public void The_interval_reads_the_clock_at_the_forty_fifth_minute()
    {
        var state = Create(Plan(new MatchRuleV1 { Ordinal = 1, FromMinute = 45, Instructions = Attack }));

        state.InFirstHalf = true;
        state.ClockSeconds = (47 * 60) + 10;
        MatchPlanRunner.ConsiderBothSides(state);

        state.Home.Instructions.Mentality.Should().Be(MatchMentality.Attacking);
        Of(state, EngineEventType.TacticalChange).Single().Minute.Should().Be(45);
    }

    // ---- Events -------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void An_own_sending_off_is_what_an_own_red_card_rule_waits_for()
    {
        var state = Create(Plan(new MatchRuleV1 { Ordinal = 1, Event = MatchRuleEvent.OwnSentOff, Instructions = Attack }));

        MatchPlanRunner.Consider(state, MatchSide.Home);
        state.Home.FiredRules.Should().BeEmpty();

        SendOff(state.Away, state.Away.Active[3].Participant.ParticipantId);
        MatchPlanRunner.Consider(state, MatchSide.Home);
        state.Home.FiredRules.Should().BeEmpty("the other side's red card is not his own");

        SendOff(state.Home, Starter(5));
        MatchPlanRunner.Consider(state, MatchSide.Home);
        state.Home.FiredRules.Should().Equal(1);
    }

    [Fact]
    public void An_opponent_sending_off_is_what_an_opponent_red_card_rule_waits_for()
    {
        var state = Create(Plan(new MatchRuleV1 { Ordinal = 1, Event = MatchRuleEvent.OpponentSentOff, Instructions = Attack }));

        SendOff(state.Home, Starter(5));
        MatchPlanRunner.Consider(state, MatchSide.Home);
        state.Home.FiredRules.Should().BeEmpty();

        SendOff(state.Away, state.Away.Active[3].Participant.ParticipantId);
        MatchPlanRunner.Consider(state, MatchSide.Home);

        state.Home.Instructions.Mentality.Should().Be(MatchMentality.Attacking);
    }

    [Fact]
    public void A_booking_names_the_booked_player_who_is_still_on_and_the_family_narrows_it()
    {
        var swap = new MatchRuleSubstitutionV1
        {
            Off = MatchRuleOff.Subject,
            On = MatchRuleOn.Player,
            OnParticipantId = Bench(2),
        };

        var state = Create(Plan(new MatchRuleV1 { Ordinal = 1, Event = MatchRuleEvent.OwnBooked, EventFamily = MatchPositionFamily.Defence, Substitutions = [swap] }));

        state.Home.Yellows[Starter(10)] = 1;
        MatchPlanRunner.Consider(state, MatchSide.Home);
        state.Home.FiredRules.Should().BeEmpty("a booked striker is not a booked defender");

        state.Home.Yellows[Starter(3)] = 1;
        MatchPlanRunner.Consider(state, MatchSide.Home);

        var substitution = Of(state, EngineEventType.Substitution).Single();
        substitution.ParticipantId.Should().Be(Starter(3));
        substitution.SecondaryParticipantId.Should().Be(Bench(2));
        substitution.SubstitutionReason.Should().Be(MatchSubstitutionReason.Tactical);
    }

    [Fact]
    public void When_two_players_are_booked_the_one_rated_lowest_is_the_subject()
    {
        var swap = new MatchRuleSubstitutionV1 { Off = MatchRuleOff.Subject, On = MatchRuleOn.Player, OnParticipantId = Bench(2) };
        var state = Create(Plan(new MatchRuleV1 { Ordinal = 1, Event = MatchRuleEvent.OwnBooked, Substitutions = [swap] }));

        state.Home.Yellows[Starter(3)] = 1;
        state.Home.Yellows[Starter(4)] = 1;
        state.Home.AdjustLiveRating(Starter(4), -500);

        MatchPlanRunner.Consider(state, MatchSide.Home);

        Of(state, EngineEventType.Substitution).Single().ParticipantId.Should().Be(Starter(4));
    }

    [Fact]
    public void A_player_playing_badly_is_one_whose_rating_is_under_the_threshold()
    {
        var swap = new MatchRuleSubstitutionV1 { Off = MatchRuleOff.Subject, On = MatchRuleOn.Player, OnParticipantId = Bench(4) };
        var state = Create(Plan(new MatchRuleV1 { Ordinal = 1, Event = MatchRuleEvent.PlayingBadly, EventThreshold = 5_500, Substitutions = [swap] }));

        MatchPlanRunner.Consider(state, MatchSide.Home);
        state.Home.FiredRules.Should().BeEmpty("everyone starts on 6.0");

        state.Home.AdjustLiveRating(Starter(7), -800);
        MatchPlanRunner.Consider(state, MatchSide.Home);

        Of(state, EngineEventType.Substitution).Single().ParticipantId.Should().Be(Starter(7));
    }

    [Fact]
    public void Without_a_threshold_the_lowest_rated_of_the_family_is_always_named()
    {
        var swap = new MatchRuleSubstitutionV1 { Off = MatchRuleOff.Subject, On = MatchRuleOn.Player, OnParticipantId = Bench(6) };
        var state = Create(Plan(new MatchRuleV1 { Ordinal = 1, Event = MatchRuleEvent.PlayingBadly, EventFamily = MatchPositionFamily.Attack, Substitutions = [swap] }));

        state.Home.AdjustLiveRating(Starter(11), -100);
        state.Home.AdjustLiveRating(Starter(7), -900);

        MatchPlanRunner.Consider(state, MatchSide.Home);

        Of(state, EngineEventType.Substitution).Single().ParticipantId.Should().Be(Starter(11), "the midfielder is rated lower, but he is not an attacker");
    }

    [Fact]
    public void A_tired_player_is_one_whose_condition_is_under_the_threshold_and_the_most_tired_is_the_subject()
    {
        var swap = new MatchRuleSubstitutionV1 { Off = MatchRuleOff.Subject, On = MatchRuleOn.Player, OnParticipantId = Bench(4) };
        var state = Create(Plan(new MatchRuleV1 { Ordinal = 1, Event = MatchRuleEvent.Tired, EventThreshold = 6_000, Substitutions = [swap] }));

        MatchPlanRunner.Consider(state, MatchSide.Home);
        state.Home.FiredRules.Should().BeEmpty("everyone is on 80%");

        Tire(state.Home, Starter(8), 2_500);
        Tire(state.Home, Starter(6), 3_000);
        MatchPlanRunner.Consider(state, MatchSide.Home);

        Of(state, EngineEventType.Substitution).Single().ParticipantId.Should().Be(Starter(6));
    }

    [Fact]
    public void An_equally_tired_pair_is_split_by_the_lower_slot_number()
    {
        var swap = new MatchRuleSubstitutionV1 { Off = MatchRuleOff.Subject, On = MatchRuleOn.Player, OnParticipantId = Bench(4) };
        var state = Create(Plan(new MatchRuleV1 { Ordinal = 1, Event = MatchRuleEvent.Tired, EventThreshold = 6_000, Substitutions = [swap] }));

        Tire(state.Home, Starter(9), 3_000);
        Tire(state.Home, Starter(7), 3_000);

        MatchPlanRunner.Consider(state, MatchSide.Home);

        Of(state, EngineEventType.Substitution).Single().ParticipantId.Should().Be(Starter(7));
    }

    [Fact]
    public void The_keeper_is_never_the_subject_unless_the_goalkeeper_family_is_asked_for()
    {
        var state = Create(Plan(new MatchRuleV1 { Ordinal = 1, Event = MatchRuleEvent.Tired, EventThreshold = 6_000, Instructions = Attack }));

        Tire(state.Home, Starter(1), 4_000);
        MatchPlanRunner.Consider(state, MatchSide.Home);
        state.Home.FiredRules.Should().BeEmpty();

        var keepers = Create(Plan(new MatchRuleV1 { Ordinal = 1, Event = MatchRuleEvent.Tired, EventFamily = MatchPositionFamily.Goalkeeper, EventThreshold = 6_000, Instructions = Attack }));

        Tire(keepers.Home, Starter(1), 4_000);
        MatchPlanRunner.Consider(keepers, MatchSide.Home);
        keepers.Home.FiredRules.Should().Equal(1);
    }

    [Theory]
    [InlineData(MatchRuleEvent.OpponentMentalityAtLeast, MatchMentality.Positive, MatchMentality.Attacking, true)]
    [InlineData(MatchRuleEvent.OpponentMentalityAtLeast, MatchMentality.Positive, MatchMentality.Balanced, false)]
    [InlineData(MatchRuleEvent.OpponentMentalityAtMost, MatchMentality.Cautious, MatchMentality.Defensive, true)]
    [InlineData(MatchRuleEvent.OpponentMentalityAtMost, MatchMentality.Cautious, MatchMentality.Balanced, false)]
    public void An_opponents_mentality_is_read_as_it_stands_now(MatchRuleEvent kind, MatchMentality threshold, MatchMentality opponent, bool fires)
    {
        var state = Create(Plan(new MatchRuleV1 { Ordinal = 1, Event = kind, EventThreshold = (int)threshold, Instructions = Attack }));

        state.Away.ChangeInstructions(state.Away.Instructions with { Mentality = opponent }, Rules);
        MatchPlanRunner.Consider(state, MatchSide.Home);

        state.Home.FiredRules.Contains(1).Should().Be(fires);
    }

    // ---- What a rule does ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Instructions_are_each_no_change_or_a_value_and_the_mentality_may_move_by_steps()
    {
        var state = Create(Plan(new MatchRuleV1
        {
            Ordinal = 1,
            Instructions = new MatchInstructionChangesV1 { MentalitySteps = -1, Pressing = MatchPressing.HighPress, CounterAttack = true },
        }));

        var before = state.Home.Instructions;

        MatchPlanRunner.Consider(state, MatchSide.Home);

        state.Home.Instructions.Should().Be(
            before with { Mentality = MatchMentality.Cautious, Pressing = MatchPressing.HighPress, CounterAttack = true });

        var change = Of(state, EngineEventType.TacticalChange).Single();
        change.Mentality.Should().Be(MatchMentality.Cautious);
        change.FormationCode.Should().BeNull("the shape was not changed");
        change.RuleOrdinal.Should().Be(1);
    }

    [Theory]
    [InlineData(MatchMentality.Attacking, 2, MatchMentality.Attacking)]
    [InlineData(MatchMentality.Positive, 2, MatchMentality.Attacking)]
    [InlineData(MatchMentality.Cautious, -2, MatchMentality.Defensive)]
    [InlineData(MatchMentality.Defensive, -1, MatchMentality.Defensive)]
    [InlineData(MatchMentality.Balanced, 2, MatchMentality.Attacking)]
    public void Relative_mentality_stops_at_the_two_ends(MatchMentality start, int steps, MatchMentality expected)
    {
        var changes = new MatchInstructionChangesV1 { MentalitySteps = steps };

        changes.ApplyTo(new MatchInstructionsV1 { Mentality = start }).Mentality.Should().Be(expected);
    }

    [Fact]
    public void A_rule_that_changes_nothing_leaves_no_tactical_change_behind()
    {
        var state = Create(Plan(new MatchRuleV1 { Ordinal = 1, Instructions = new MatchInstructionChangesV1 { MentalitySteps = 1 } }));
        state.Home.ChangeInstructions(state.Home.Instructions with { Mentality = MatchMentality.Attacking }, Rules);
        state.Home.TakeInstructionsChange();

        MatchPlanRunner.Consider(state, MatchSide.Home);

        state.Home.FiredRules.Should().Equal(1);
        Of(state, EngineEventType.TacticalChange).Should().BeEmpty("Attacking plus a step is Attacking: nothing changed");
        state.Home.TakeInstructionsChange().Should().BeFalse();
    }

    [Fact]
    public void A_new_shape_is_fitted_over_the_players_on_the_pitch_and_they_keep_their_slot_numbers()
    {
        var shape = TestShapes.FourThreeThree;
        var state = Create(Plan(new MatchRuleV1 { Ordinal = 3, FormationCode = "4-3-3", Slots = shape }));
        var expected = ShapeFit.Fit([.. state.Home.Active], shape, null, Rules);
        var numbers = state.Home.Active.ToDictionary(slot => slot.Participant.ParticipantId, slot => slot.Slot.SlotNumber);

        MatchPlanRunner.Consider(state, MatchSide.Home);

        foreach (var slot in state.Home.Active)
        {
            var target = shape.Single(candidate => candidate.SlotNumber == expected[slot.Participant.ParticipantId]);

            slot.Slot.SlotNumber.Should().Be(numbers[slot.Participant.ParticipantId]);
            (slot.Slot.Family, slot.Slot.Role, slot.Slot.X, slot.Slot.Y).Should().Be((target.Family, target.Role, target.X, target.Y));
        }

        state.Home.Active.Count(slot => slot.Slot.Role == MatchRole.Winger).Should().Be(2);
        state.Home.Active.Single(slot => slot.Slot.Family == MatchPositionFamily.Goalkeeper).Participant.ParticipantId.Should().Be(Starter(1));

        var change = Of(state, EngineEventType.TacticalChange).Single();
        change.FormationCode.Should().Be("4-3-3");
        change.Mentality.Should().BeNull("the mentality was not changed");
        change.RuleOrdinal.Should().Be(3);
        state.Home.TakeShapeChange().Should().BeTrue();
        state.Home.TakeInstructionsChange().Should().BeFalse();
    }

    [Fact]
    public void The_managers_own_placements_are_made_before_the_automatic_fit()
    {
        var shape = TestShapes.FourThreeThree;
        var placement = new MatchSlotOverrideV1(Starter(10), 11);
        var state = Create(Plan(new MatchRuleV1 { Ordinal = 1, FormationCode = "4-3-3", Slots = shape, Overrides = [placement] }));

        MatchPlanRunner.Consider(state, MatchSide.Home);

        var placed = state.Home.Active.Single(slot => slot.Participant.ParticipantId == Starter(10));
        (placed.Slot.X, placed.Slot.Y).Should().Be((shape[10].X, shape[10].Y));
        placed.Slot.SlotNumber.Should().Be(10, "he keeps his own number");
    }

    [Fact]
    public void A_substitution_names_who_comes_off_and_who_comes_on_and_is_counted()
    {
        var swap = new MatchRuleSubstitutionV1 { Off = MatchRuleOff.Player, OffParticipantId = Starter(9), On = MatchRuleOn.Player, OnParticipantId = Bench(7) };
        var state = Create(Plan(new MatchRuleV1 { Ordinal = 1, Substitutions = [swap] }));

        MatchPlanRunner.Consider(state, MatchSide.Home);

        var substitution = Of(state, EngineEventType.Substitution).Single();
        (substitution.ParticipantId, substitution.SecondaryParticipantId, substitution.SubstitutionReason)
            .Should().Be((Starter(9), Bench(7), MatchSubstitutionReason.Tactical));
        state.Home.Active.Select(slot => slot.Participant.ParticipantId).Should().Contain(Bench(7)).And.NotContain(Starter(9));
        state.Home.Bench.Select(participant => participant.ParticipantId).Should().NotContain(Bench(7));
        state.Home.Substitutions.Should().Be(1);
        Of(state, EngineEventType.TacticalChange).Should().BeEmpty("a substitution alone is not a tactical change");
    }

    [Fact]
    public void A_family_selector_takes_the_most_tired_off_and_the_best_fitting_fresh_substitute_on()
    {
        var swap = new MatchRuleSubstitutionV1
        {
            Off = MatchRuleOff.MostTiredInFamily,
            OffFamily = MatchPositionFamily.Midfield,
            On = MatchRuleOn.BestFitInFamily,
            OnFamily = MatchPositionFamily.Attack,
        };

        var state = Create(Plan(new MatchRuleV1 { Ordinal = 1, Substitutions = [swap] }));

        Tire(state.Home, Starter(8), 1_000);
        Tire(state.Home, Starter(9), 2_500);
        Tire(state.Home, Starter(2), 4_000);

        MatchPlanRunner.Consider(state, MatchSide.Home);

        var substitution = Of(state, EngineEventType.Substitution).Single();
        substitution.ParticipantId.Should().Be(Starter(9), "the defender is more tired, but he is not a midfielder");
        substitution.SecondaryParticipantId.Should().Be(Bench(6), "both attackers fit a midfield slot equally badly and are equally fresh, so identity decides");
    }

    [Fact]
    public void The_lowest_rated_of_a_family_comes_off()
    {
        var swap = new MatchRuleSubstitutionV1
        {
            Off = MatchRuleOff.LowestRatedInFamily,
            OffFamily = MatchPositionFamily.Attack,
            On = MatchRuleOn.Player,
            OnParticipantId = Bench(6),
        };

        var state = Create(Plan(new MatchRuleV1 { Ordinal = 1, Substitutions = [swap] }));

        state.Home.AdjustLiveRating(Starter(10), 300);
        state.Home.AdjustLiveRating(Starter(11), -300);

        MatchPlanRunner.Consider(state, MatchSide.Home);

        Of(state, EngineEventType.Substitution).Single().ParticipantId.Should().Be(Starter(11));
    }

    [Fact]
    public void A_named_player_who_was_sent_off_is_skipped_and_the_rest_of_the_rule_still_happens()
    {
        var state = Create(
            Plan(
                new MatchRuleV1
                {
                    Ordinal = 1,
                    Instructions = Attack,
                    Substitutions =
                    [
                        new MatchRuleSubstitutionV1 { Off = MatchRuleOff.Player, OffParticipantId = Starter(10), On = MatchRuleOn.Player, OnParticipantId = Bench(6) },
                        new MatchRuleSubstitutionV1 { Off = MatchRuleOff.Player, OffParticipantId = Starter(11), On = MatchRuleOn.Player, OnParticipantId = Bench(7) },
                    ],
                }));

        SendOff(state.Home, Starter(10));

        MatchPlanRunner.Consider(state, MatchSide.Home);

        Of(state, EngineEventType.Substitution).Select(matchEvent => matchEvent.ParticipantId).Should().Equal(Starter(11));
        state.Home.Bench.Select(participant => participant.ParticipantId).Should().Contain(Bench(6), "his replacement was never brought on");
        state.Home.Instructions.Mentality.Should().Be(MatchMentality.Attacking);
    }

    [Fact]
    public void Someone_who_is_not_on_the_bench_is_never_brought_on()
    {
        var swap = new MatchRuleSubstitutionV1 { Off = MatchRuleOff.Player, OffParticipantId = Starter(9), On = MatchRuleOn.Player, OnParticipantId = Starter(8) };
        var state = Create(Plan(new MatchRuleV1 { Ordinal = 1, Substitutions = [swap] }));

        MatchPlanRunner.Consider(state, MatchSide.Home);

        state.Events.Should().BeEmpty();
        state.Home.FiredRules.Should().BeEquivalentTo(new[] { 1 }, "the rule has been used even though nothing could be done");
    }

    [Fact]
    public void The_five_substitution_cap_holds_whoever_asks()
    {
        MatchRuleSubstitutionV1 Swap(int slot, int bench) =>
            new() { Off = MatchRuleOff.Player, OffParticipantId = Starter(slot), On = MatchRuleOn.Player, OnParticipantId = Bench(bench) };

        var state = Create(
            Plan(
                new MatchRuleV1 { Ordinal = 1, Substitutions = [Swap(2, 2), Swap(3, 3), Swap(4, 4)] },
                new MatchRuleV1 { Ordinal = 2, Substitutions = [Swap(5, 5), Swap(6, 6), Swap(7, 7)] }));

        MatchPlanRunner.Consider(state, MatchSide.Home);

        Of(state, EngineEventType.Substitution).Should().HaveCount(Rules.MaxSubstitutions);
        state.Home.Substitutions.Should().Be(Rules.MaxSubstitutions);
        state.Home.FiredRules.Should().Equal(1, 2);
    }

    [Fact]
    public void Once_the_side_has_made_five_substitutions_a_rule_still_changes_the_shape_and_the_instructions_but_changes_nobody()
    {
        MatchRuleSubstitutionV1 Swap(int slot, int bench) =>
            new() { Off = MatchRuleOff.Player, OffParticipantId = Starter(slot), On = MatchRuleOn.Player, OnParticipantId = Bench(bench) };

        var shape = TestShapes.FourThreeThree;
        var state = Create(
            Plan(
                new MatchRuleV1 { Ordinal = 1, Substitutions = [Swap(2, 2), Swap(3, 3), Swap(4, 4)] },
                new MatchRuleV1 { Ordinal = 2, Substitutions = [Swap(5, 5), Swap(6, 6)] },
                new MatchRuleV1
                {
                    Ordinal = 3,
                    FormationCode = "4-3-3",
                    Slots = shape,
                    Instructions = Attack,
                    Substitutions = [Swap(7, 7)],
                }));

        MatchPlanRunner.Consider(state, MatchSide.Home);

        state.Home.Substitutions.Should().Be(Rules.MaxSubstitutions);
        Of(state, EngineEventType.Substitution).Should().HaveCount(Rules.MaxSubstitutions, "the sixth is not made");
        Of(state, EngineEventType.Substitution).Select(matchEvent => matchEvent.ParticipantId).Should().NotContain(Starter(7));
        state.Home.Bench.Select(participant => participant.ParticipantId).Should().Contain(Bench(7));

        // The third rule's other changes were made.
        state.Home.FiredRules.Should().Equal(1, 2, 3);
        state.Home.Instructions.Mentality.Should().Be(MatchMentality.Attacking);
        state.Home.Active.Select(slot => (slot.Slot.X, slot.Slot.Y)).Should().BeEquivalentTo(shape.Select(slot => (slot.X, slot.Y)));

        var change = Of(state, EngineEventType.TacticalChange).Single();

        change.RuleOrdinal.Should().Be(3);
        change.FormationCode.Should().Be("4-3-3");
    }

    [Fact]
    public void A_rule_with_only_substitutions_does_nothing_and_says_nothing_once_the_cap_is_reached()
    {
        MatchRuleSubstitutionV1 Swap(int slot, int bench) =>
            new() { Off = MatchRuleOff.Player, OffParticipantId = Starter(slot), On = MatchRuleOn.Player, OnParticipantId = Bench(bench) };

        var state = Create(
            Plan(
                new MatchRuleV1 { Ordinal = 1, Substitutions = [Swap(2, 2), Swap(3, 3), Swap(4, 4)] },
                new MatchRuleV1 { Ordinal = 2, Substitutions = [Swap(5, 5), Swap(6, 6)] },
                new MatchRuleV1 { Ordinal = 3, Substitutions = [Swap(7, 7)] }));

        MatchPlanRunner.Consider(state, MatchSide.Home);

        Of(state, EngineEventType.Substitution).Should().HaveCount(Rules.MaxSubstitutions);
        Of(state, EngineEventType.TacticalChange).Should().BeEmpty();
        state.Home.FiredRules.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void A_rule_makes_its_substitutions_before_it_fits_the_shape_so_the_substitute_is_placed_in_it()
    {
        var shape = TestShapes.FourThreeThree;
        var swap = new MatchRuleSubstitutionV1 { Off = MatchRuleOff.Player, OffParticipantId = Starter(11), On = MatchRuleOn.Player, OnParticipantId = Bench(6) };
        var state = Create(Plan(new MatchRuleV1
        {
            Ordinal = 1,
            FormationCode = "4-3-3",
            Slots = shape,
            Overrides = [new MatchSlotOverrideV1(Bench(6), 9)],
            Substitutions = [swap],
        }));

        MatchPlanRunner.Consider(state, MatchSide.Home);

        var substitute = state.Home.Active.Single(slot => slot.Participant.ParticipantId == Bench(6));
        substitute.Slot.SlotNumber.Should().Be(11, "he took his man's number");
        (substitute.Slot.Role, substitute.Slot.X, substitute.Slot.Y).Should().Be((MatchRole.Winger, shape[8].X, shape[8].Y));
        Of(state, EngineEventType.Substitution).Should().ContainSingle();
        Of(state, EngineEventType.Substitution).Single().Sequence.Should().BeLessThan(Of(state, EngineEventType.TacticalChange).Single().Sequence);
    }

    // ---- The assistant and the call order -----------------------------------------------------------------------------------------

    private static MatchState TiredSecondHalf(MatchPlanV1 home)
    {
        var state = Create(home);

        Tire(state.Home, Starter(8), 3_000);
        At(state, 46);
        state.LastPlannerMinute = 45;

        return state;
    }

    [Fact]
    public void The_assistant_makes_a_fatigue_substitution_at_a_window_unless_the_plan_switches_it_off()
    {
        var on = TiredSecondHalf(MatchPlanV1.None);
        SubstitutionPlanner.ConsiderBothSides(on, applyMatchPlans: true);

        Of(on, EngineEventType.Substitution).Should().ContainSingle().Which.SubstitutionReason.Should().Be(MatchSubstitutionReason.Fatigue);

        var off = TiredSecondHalf(new MatchPlanV1 { AssistantFatigueSubstitutions = false });
        SubstitutionPlanner.ConsiderBothSides(off, applyMatchPlans: true);

        Of(off, EngineEventType.Substitution).Should().BeEmpty();
    }

    [Fact]
    public void The_possession_engines_planner_ignores_the_plan_altogether()
    {
        var state = TiredSecondHalf(new MatchPlanV1 { AssistantFatigueSubstitutions = false, Rules = [new MatchRuleV1 { Ordinal = 1, Instructions = Attack }] });

        SubstitutionPlanner.ConsiderBothSides(state);

        Of(state, EngineEventType.Substitution).Should().ContainSingle("the assistant is always on there");
        state.Home.FiredRules.Should().BeEmpty();
        state.Home.Instructions.Mentality.Should().Be(MatchMentality.Balanced);
    }

    [Fact]
    public void An_injury_is_dealt_with_first_then_the_rules_then_the_assistant()
    {
        var plan = new MatchPlanV1
        {
            Rules =
            [
                new MatchRuleV1
                {
                    Ordinal = 1,
                    Substitutions = [new MatchRuleSubstitutionV1 { Off = MatchRuleOff.Player, OffParticipantId = Starter(9), On = MatchRuleOn.Player, OnParticipantId = Bench(7) }],
                },
            ],
        };

        var state = TiredSecondHalf(plan);
        state.Home.PendingInjurySubstitutions.Add(Starter(3));

        SubstitutionPlanner.ConsiderBothSides(state, applyMatchPlans: true);

        Of(state, EngineEventType.Substitution)
            .Select(matchEvent => matchEvent.SubstitutionReason)
            .Should().Equal(MatchSubstitutionReason.Injury, MatchSubstitutionReason.Tactical, MatchSubstitutionReason.Fatigue);
    }
}
