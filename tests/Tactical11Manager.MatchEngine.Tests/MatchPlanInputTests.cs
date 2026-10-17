using FluentAssertions;
using Tactical11Manager.MatchEngine;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Serialization;
using Tactical11Manager.MatchEngine.Simulation;
using Tactical11Manager.MatchEngine.Tick;

namespace Tactical11Manager.MatchEngine.Tests;

/// <summary>
/// Verifies the match plan as part of the input (its validation and its place in the canonical text), and how the tick engine plays a
/// match that carries one (`match-instructions-v1`, Milestone 2).
/// </summary>
public sealed class MatchPlanInputTests
{
    private static Guid Starter(int slot) => TestMatchFactory.Identity(10_100 + slot);

    private static Guid Bench(int number) => TestMatchFactory.Identity(10_150 + number);

    private static MatchInputV1 WithHomePlan(MatchInputV1 input, MatchPlanV1 plan) => input with { Home = input.Home with { MatchPlan = plan } };

    private static MatchInputV1 WithRule(MatchRuleV1 rule) => WithHomePlan(TestMatchFactory.Even(), new MatchPlanV1 { Rules = [rule] });

    private static MatchRuleV1 Chase(int ordinal = 1) => new()
    {
        Ordinal = ordinal,
        FromMinute = 60,
        Score = MatchRuleScore.Trailing,
        FormationCode = "4-3-3",
        Slots = TestShapes.FourThreeThree,
        Instructions = new MatchInstructionChangesV1 { Mentality = MatchMentality.Attacking, Pressing = MatchPressing.HighPress },
        Substitutions = [new MatchRuleSubstitutionV1 { Off = MatchRuleOff.MostTiredInFamily, OffFamily = MatchPositionFamily.Midfield, On = MatchRuleOn.BestFitInFamily, OnFamily = MatchPositionFamily.Attack }],
    };

    // ---- Validation ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_snapshot_with_a_well_formed_plan_is_accepted()
    {
        var input = WithRule(Chase() with
        {
            Overrides = [new MatchSlotOverrideV1(Starter(10), 11)],
        });

        var act = input.Validate;

        act.Should().NotThrow();
    }

    [Fact]
    public void Eleven_rules_are_refused()
    {
        var plan = new MatchPlanV1 { Rules = [.. Enumerable.Range(1, 11).Select(ordinal => new MatchRuleV1 { Ordinal = ordinal })] };

        var act = WithHomePlan(TestMatchFactory.Even(), plan).Validate;

        act.Should().Throw<InvalidMatchInputException>().WithMessage("*11 rules*");
    }

    [Fact]
    public void Four_substitutions_in_one_rule_are_refused()
    {
        MatchRuleSubstitutionV1 Swap(int slot, int bench) =>
            new() { Off = MatchRuleOff.Player, OffParticipantId = Starter(slot), On = MatchRuleOn.Player, OnParticipantId = Bench(bench) };

        var act = WithRule(new MatchRuleV1 { Ordinal = 1, Substitutions = [Swap(2, 2), Swap(3, 3), Swap(4, 4), Swap(5, 5)] }).Validate;

        act.Should().Throw<InvalidMatchInputException>().WithMessage("*4 substitutions*");
    }

    [Fact]
    public void A_new_shape_must_have_eleven_slots_with_only_the_goalkeeper_in_slot_one()
    {
        var short10 = Chase() with { Slots = [.. TestShapes.FourThreeThree.Take(10)] };
        var noKeeperFirst = Chase() with
        {
            Slots = [.. TestShapes.FourThreeThree.Select(slot => slot.SlotNumber switch
            {
                1 => slot with { Family = MatchPositionFamily.Defence, Role = MatchRole.CentreBack },
                _ => slot,
            })],
        };

        var twoKeepers = Chase() with
        {
            Slots = [.. TestShapes.FourThreeThree.Select(slot => slot.SlotNumber == 2 ? slot with { Family = MatchPositionFamily.Goalkeeper, Role = MatchRole.Goalkeeper } : slot)],
        };

        foreach (var rule in new[] { short10, noKeeperFirst, twoKeepers })
        {
            WithRule(rule).Validating().Should().Throw<InvalidMatchInputException>();
        }
    }

    [Fact]
    public void A_shape_with_a_slot_whose_role_does_not_belong_to_its_family_is_refused()
    {
        var rule = Chase() with
        {
            Slots = [.. TestShapes.FourThreeThree.Select(slot => slot.SlotNumber == 10 ? slot with { Family = MatchPositionFamily.Defence } : slot)],
        };

        WithRule(rule).Validating().Should().Throw<InvalidMatchInputException>().WithMessage("*TAC-8*");
    }

    [Fact]
    public void A_placement_must_name_a_squad_member_and_an_outfield_slot_once()
    {
        var stranger = Chase() with { Overrides = [new MatchSlotOverrideV1(TestMatchFactory.Identity(1), 5)] };
        var inGoal = Chase() with { Overrides = [new MatchSlotOverrideV1(Starter(10), 1)] };
        var twice = Chase() with { Overrides = [new MatchSlotOverrideV1(Starter(10), 5), new MatchSlotOverrideV1(Starter(9), 5)] };

        foreach (var rule in new[] { stranger, inGoal, twice })
        {
            WithRule(rule).Validating().Should().Throw<InvalidMatchInputException>();
        }
    }

    [Fact]
    public void Placements_without_a_new_shape_are_refused()
    {
        var rule = new MatchRuleV1 { Ordinal = 1, Overrides = [new MatchSlotOverrideV1(Starter(10), 5)] };

        WithRule(rule).Validating().Should().Throw<InvalidMatchInputException>();
    }

    [Theory]
    [InlineData(-1, null, 1)]
    [InlineData(91, null, 1)]
    [InlineData(60, 59, 1)]
    [InlineData(60, 91, 1)]
    [InlineData(60, null, 0)]
    [InlineData(60, null, 6)]
    public void Minutes_and_margins_outside_their_ranges_are_refused(int from, int? until, int margin)
    {
        var rule = new MatchRuleV1 { Ordinal = 1, FromMinute = from, UntilMinute = until, ScoreMargin = margin };

        WithRule(rule).Validating().Should().Throw<InvalidMatchInputException>();
    }

    [Fact]
    public void A_rule_must_have_a_place_in_the_list_and_not_share_it()
    {
        var zero = new MatchPlanV1 { Rules = [new MatchRuleV1 { Ordinal = 0 }] };
        var shared = new MatchPlanV1 { Rules = [new MatchRuleV1 { Ordinal = 1 }, new MatchRuleV1 { Ordinal = 1 }] };

        WithHomePlan(TestMatchFactory.Even(), zero).Validating().Should().Throw<InvalidMatchInputException>();
        WithHomePlan(TestMatchFactory.Even(), shared).Validating().Should().Throw<InvalidMatchInputException>();
    }

    [Fact]
    public void Events_need_what_they_wait_for()
    {
        var badly = new MatchRuleV1 { Ordinal = 1, Event = MatchRuleEvent.PlayingBadly };
        var tired = new MatchRuleV1 { Ordinal = 1, Event = MatchRuleEvent.Tired };
        var mentality = new MatchRuleV1 { Ordinal = 1, Event = MatchRuleEvent.OpponentMentalityAtLeast, EventThreshold = 9 };

        foreach (var rule in new[] { badly, tired, mentality })
        {
            WithRule(rule).Validating().Should().Throw<InvalidMatchInputException>();
        }

        WithRule(badly with { EventFamily = MatchPositionFamily.Attack }).Validating().Should().NotThrow();
        WithRule(tired with { EventThreshold = 6_000 }).Validating().Should().NotThrow();
    }

    [Fact]
    public void A_substitution_must_name_squad_members_or_families_and_the_subject_needs_an_event()
    {
        var stranger = new MatchRuleSubstitutionV1 { Off = MatchRuleOff.Player, OffParticipantId = TestMatchFactory.Identity(1), On = MatchRuleOn.Player, OnParticipantId = Bench(2) };
        var noFamily = new MatchRuleSubstitutionV1 { Off = MatchRuleOff.MostTiredInFamily, On = MatchRuleOn.Player, OnParticipantId = Bench(2) };
        var subjectOfNothing = new MatchRuleSubstitutionV1 { Off = MatchRuleOff.Subject, On = MatchRuleOn.Player, OnParticipantId = Bench(2) };

        foreach (var substitution in new[] { stranger, noFamily, subjectOfNothing })
        {
            WithRule(new MatchRuleV1 { Ordinal = 1, Substitutions = [substitution] }).Validating().Should().Throw<InvalidMatchInputException>();
        }

        WithRule(new MatchRuleV1 { Ordinal = 1, Event = MatchRuleEvent.OwnBooked, Substitutions = [subjectOfNothing] }).Validating().Should().NotThrow();
    }

    [Fact]
    public void A_mentality_may_move_two_steps_and_no_more()
    {
        MatchRuleV1 Steps(int steps) => new() { Ordinal = 1, Instructions = new MatchInstructionChangesV1 { MentalitySteps = steps } };

        WithRule(Steps(2)).Validating().Should().NotThrow();
        WithRule(Steps(-2)).Validating().Should().NotThrow();
        WithRule(Steps(3)).Validating().Should().Throw<InvalidMatchInputException>();
        WithRule(Steps(-3)).Validating().Should().Throw<InvalidMatchInputException>();
    }

    // ---- The canonical form -------------------------------------------------------------------------------------------------------

    [Fact]
    public void An_input_without_a_plan_writes_no_plan_lines_and_so_hashes_as_it_did_before_plans_existed()
    {
        var plain = TestMatchFactory.Even();
        var none = WithHomePlan(plain, MatchPlanV1.None);

        CanonicalMatchSerializer.CanonicalText(plain).Should().NotContain("plan.");
        CanonicalMatchSerializer.InputHash(none).Should().Be(CanonicalMatchSerializer.InputHash(plain));
        MatchPlanV1.None.HasContent.Should().BeFalse();
    }

    [Fact]
    public void Rules_and_a_switched_off_assistant_are_written_and_change_the_hash()
    {
        var plain = TestMatchFactory.Even();
        var ruled = WithRule(Chase());
        var assistantOff = WithHomePlan(plain, new MatchPlanV1 { AssistantFatigueSubstitutions = false });

        CanonicalMatchSerializer.CanonicalText(ruled).Should().Contain("home.plan.rule.1.fromMinute=60").And.Contain("home.plan.rule.1.formation=4-3-3");
        CanonicalMatchSerializer.CanonicalText(assistantOff).Should().Contain("home.plan.assistantFatigueSubstitutions=false");
        CanonicalMatchSerializer.InputHash(ruled).Should().NotBe(CanonicalMatchSerializer.InputHash(plain));
        CanonicalMatchSerializer.InputHash(assistantOff).Should().NotBe(CanonicalMatchSerializer.InputHash(plain));
        CanonicalMatchSerializer.CanonicalText(ruled).Should().NotContain("away.plan");
    }

    [Fact]
    public void A_rules_name_is_for_presentation_only_and_never_changes_the_hash()
    {
        var unnamed = WithRule(Chase());
        var named = WithRule(Chase() with { Name = "Chase the game" });

        CanonicalMatchSerializer.CanonicalText(named).Should().NotContain("Chase the game");
        CanonicalMatchSerializer.InputHash(named).Should().Be(CanonicalMatchSerializer.InputHash(unnamed));
    }

    [Fact]
    public void The_order_of_the_rules_is_part_of_the_hash_because_it_is_the_order_they_are_checked_in()
    {
        var first = Chase(1) with { FromMinute = 20 };
        var second = Chase(2) with { FromMinute = 30 };
        var one = WithHomePlan(TestMatchFactory.Even(), new MatchPlanV1 { Rules = [first, second] });
        var other = WithHomePlan(TestMatchFactory.Even(), new MatchPlanV1 { Rules = [second, first] });

        CanonicalMatchSerializer.InputHash(one).Should().NotBe(CanonicalMatchSerializer.InputHash(other));
    }

    // ---- The tick engine ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_team_follows_the_runtime_after_a_rule_fires_its_anchors_move_and_its_style_is_worked_out_again()
    {
        var state = TickTestMatchState.Create(WithRule(Chase() with { FromMinute = 0, Score = MatchRuleScore.Any, Substitutions = [] }));
        var team = new TickTeam(state.Home);
        var style = team.Style;

        team.AdoptPlanChanges().Should().BeFalse("nothing has changed yet");

        MatchPlanRunner.Consider(state, MatchSide.Home);

        team.AdoptPlanChanges().Should().BeTrue();
        team.Style.Should().NotBe(style).And.Be(TickTeamStyle.From(state.Home.Instructions));

        for (var seat = 0; seat < team.Count; seat++)
        {
            var slot = state.Home.Active.Single(candidate => candidate.Participant.ParticipantId == team.Id[seat]).Slot;

            team.Spec[seat].Should().Be(TickAnchorSpec.From(slot));
        }

        team.AdoptPlanChanges().Should().BeFalse("it is adopted once");
    }

    [Fact]
    public void A_plan_that_never_fires_leaves_the_match_exactly_as_it_was()
    {
        // Minute 0 is never displayed, so a rule that must fire in it never does.
        var never = new MatchRuleV1 { Ordinal = 1, FromMinute = 0, UntilMinute = 0, Instructions = new MatchInstructionChangesV1 { Mentality = MatchMentality.Attacking } };
        var plain = TickPlay.Play(TickPlay.Fixture(), record: false);
        var planned = TickPlay.Play(WithHomePlan(TickPlay.Fixture(), new MatchPlanV1 { Rules = [never] }), record: false);

        planned.Result.Events.Select(matchEvent => (matchEvent.Type, matchEvent.Minute, matchEvent.ParticipantId))
            .Should().Equal(plain.Result.Events.Select(matchEvent => (matchEvent.Type, matchEvent.Minute, matchEvent.ParticipantId)));
        (planned.Result.HomeGoals, planned.Result.AwayGoals).Should().Be((plain.Result.HomeGoals, plain.Result.AwayGoals));
    }

    [Fact]
    public void A_rule_changes_nothing_before_it_fires_and_shows_in_the_very_next_frames()
    {
        var rule = new MatchRuleV1
        {
            Ordinal = 1,
            FromMinute = 30,
            FormationCode = "4-3-3",
            Slots = TestShapes.FourThreeThree,
            Instructions = new MatchInstructionChangesV1 { Mentality = MatchMentality.Attacking },
        };

        var control = TickPlay.Play(TickPlay.Fixture());
        var played = TickPlay.Play(WithHomePlan(TickPlay.Fixture(), new MatchPlanV1 { Rules = [rule] }));

        var change = played.Result.Events.Single(matchEvent => matchEvent.Type == EngineEventType.TacticalChange);
        change.Minute.Should().BeGreaterThanOrEqualTo(30);
        (change.FormationCode, change.Mentality, change.RuleOrdinal).Should().Be(("4-3-3", MatchMentality.Attacking, 1));

        var frame = played.Recording.Events.Single(stamp => stamp.Sequence == change.Sequence).Frame;
        var plain = control.Recording;
        var recording = played.Recording;

        for (var at = 0; at < frame; at++)
        {
            for (var entity = 0; entity < TickMatchRecording.Entities; entity++)
            {
                recording.PlayerX(at, entity).Should().Be(plain.PlayerX(at, entity), $"frame {at} is before the rule fired");
                recording.PlayerY(at, entity).Should().Be(plain.PlayerY(at, entity));
            }
        }

        var moved = Enumerable.Range(frame, 100).Any(at =>
            Enumerable.Range(0, TickMatchRecording.Entities).Any(entity =>
                recording.PlayerX(at, entity) != plain.PlayerX(at, entity) || recording.PlayerY(at, entity) != plain.PlayerY(at, entity)));

        moved.Should().BeTrue("the players walk to the new anchors");
    }

    [Fact]
    public void A_change_of_shape_and_a_substitution_make_nobody_teleport_on_the_recording_the_film_is_cut_from()
    {
        var rule = new MatchRuleV1
        {
            Ordinal = 1,
            FromMinute = 30,
            FormationCode = "4-3-3",
            Slots = TestShapes.FourThreeThree,
            Instructions = new MatchInstructionChangesV1 { Mentality = MatchMentality.Attacking },
            Substitutions = [new MatchRuleSubstitutionV1 { Off = MatchRuleOff.Player, OffParticipantId = Starter(11), On = MatchRuleOn.Player, OnParticipantId = Bench(7) }],
        };

        foreach (var seed in new ulong[] { 1, 2, 3 })
        {
            var played = TickPlay.Play(WithHomePlan(TickPlay.Fixture(seed), new MatchPlanV1 { Rules = [rule] }));
            var recording = played.Recording;
            var change = played.Result.Events.Single(matchEvent => matchEvent.Type == EngineEventType.TacticalChange);
            var changeFrame = recording.Events.Single(stamp => stamp.Sequence == change.Sequence).Frame;
            var jumps = 0;
            var biggestStep = 0.0;

            for (var frame = 1; frame < recording.FrameCount; frame++)
            {
                var placed = recording.Flags(frame).HasFlag(TickFrameFlags.PlayersPlaced);

                for (var entity = 0; entity < TickMatchRecording.Entities; entity++)
                {
                    if (recording.PlayerX(frame, entity) < 0 || recording.PlayerX(frame - 1, entity) < 0)
                    {
                        continue;
                    }

                    var step = Math.Sqrt(
                        Math.Pow(recording.PlayerX(frame, entity) - recording.PlayerX(frame - 1, entity), 2)
                        + Math.Pow(recording.PlayerY(frame, entity) - recording.PlayerY(frame - 1, entity), 2));

                    // The same limit the film's own check uses: a sprint is under a metre a tick, a keeper's dive a few.
                    if (step > 450 && !placed)
                    {
                        jumps++;
                    }

                    if (frame >= changeFrame && frame < changeFrame + 600 && !placed)
                    {
                        biggestStep = Math.Max(biggestStep, step);
                    }
                }
            }

            jumps.Should().Be(0, $"seed {seed}: players walk to their new places, they are not put there");
            biggestStep.Should().BeLessThanOrEqualTo(450, $"seed {seed}: nobody covers a pitch's width in the seconds after the change");
        }
    }

    [Fact]
    public void A_rule_due_at_the_forty_fifth_minute_fires_in_it_before_the_second_half_starts()
    {
        var rule = new MatchRuleV1
        {
            Ordinal = 1,
            FromMinute = 45,
            FormationCode = "4-3-3",
            Slots = TestShapes.FourThreeThree,
        };

        // Minute 45 also covers the first half's added time, so a stoppage there fires it before the whistle, or the interval does.
        foreach (var seed in Enumerable.Range(1, 3))
        {
            var played = TickPlay.Play(WithHomePlan(TickPlay.Fixture((ulong)seed), new MatchPlanV1 { Rules = [rule] }), record: false);
            var events = played.Result.Events;
            var change = events.Single(matchEvent => matchEvent.Type == EngineEventType.TacticalChange);

            change.Minute.Should().Be(45);
            change.Sequence.Should().BeLessThan(events.Single(matchEvent => matchEvent.Type == EngineEventType.SecondHalfStart).Sequence);
        }
    }

    [Fact]
    public void The_interval_carries_out_the_rules_that_are_due_and_the_teams_adopt_them_before_the_second_half()
    {
        var rule = new MatchRuleV1
        {
            Ordinal = 1,
            FromMinute = 45,
            FormationCode = "4-3-3",
            Slots = TestShapes.FourThreeThree,
            Instructions = new MatchInstructionChangesV1 { Mentality = MatchMentality.Attacking },
            Substitutions = [new MatchRuleSubstitutionV1 { Off = MatchRuleOff.Player, OffParticipantId = Starter(11), On = MatchRuleOn.Player, OnParticipantId = Bench(7) }],
        };

        var state = TickTestMatchState.Create(WithHomePlan(TickPlay.Fixture(), new MatchPlanV1 { Rules = [rule] }));
        state.InFirstHalf = true;
        state.ClockSeconds = 47 * 60;

        var loop = new TickMatchLoop(state);
        var home = loop.Teams[0];

        loop.TakeInterval();

        state.Events.Select(matchEvent => (matchEvent.Type, matchEvent.Minute)).Should().Equal(
            (EngineEventType.Substitution, 45),
            (EngineEventType.TacticalChange, 45));
        state.Home.Instructions.Mentality.Should().Be(MatchMentality.Attacking);
        home.Style.Should().Be(TickTeamStyle.From(state.Home.Instructions));
        home.Id[home.SeatOfSlot(11)].Should().Be(Bench(7));

        for (var seat = 0; seat < home.Count; seat++)
        {
            home.Spec[seat].Should().Be(TickAnchorSpec.From(state.Home.Active.Single(slot => slot.Participant.ParticipantId == home.Id[seat]).Slot));
        }
    }

    [Fact]
    public void The_possession_engine_plays_a_plan_as_if_it_were_not_there()
    {
        var plain = TestMatchFactory.Even();
        var planned = WithRule(Chase() with { FromMinute = 0, Score = MatchRuleScore.Any });

        var one = MatchSimulator.Simulate(plain, TestMatchFactory.Rules);
        var other = MatchSimulator.Simulate(planned, TestMatchFactory.Rules);

        other.Events.Select(matchEvent => (matchEvent.Type, matchEvent.Minute, matchEvent.ParticipantId))
            .Should().Equal(one.Events.Select(matchEvent => (matchEvent.Type, matchEvent.Minute, matchEvent.ParticipantId)));
        other.Events.Should().NotContain(matchEvent => matchEvent.Type == EngineEventType.TacticalChange);
    }
}

internal static class ValidationExtensions
{
    /// <summary>Gets the snapshot's validation as an action, so a test can assert on whether it throws.</summary>
    /// <param name="input">The snapshot.</param>
    public static Action Validating(this MatchInputV1 input) => input.Validate;
}
