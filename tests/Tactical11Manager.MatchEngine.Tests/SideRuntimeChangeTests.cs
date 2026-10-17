using FluentAssertions;
using Tactical11Manager.MatchEngine.Configuration;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Simulation;

namespace Tactical11Manager.MatchEngine.Tests;

/// <summary>Verifies a side can change its instructions, its shape and its personnel part-way through a match (`match-instructions-v1`, Milestone 1).</summary>
public sealed class SideRuntimeChangeTests
{
    private static readonly EngineRulesV2 Rules = EngineRulesV2.Default;

    [Fact]
    public void A_side_plays_on_the_instructions_it_kicked_off_with_until_they_are_changed()
    {
        var state = TickTestMatchState.Create();

        state.Home.Instructions.Should().BeSameAs(state.Home.Lineup.Instructions);

        var changed = new MatchInstructionsV1 { Mentality = MatchMentality.Defensive, TimeWasting = MatchTimeWasting.On };
        state.Home.ChangeInstructions(changed, Rules);

        state.Home.Instructions.Should().BeSameAs(changed);
        state.Home.Lineup.Instructions.Should().NotBeSameAs(changed, "the lineup is what the side started with and stays so");
        state.Away.Instructions.Should().BeSameAs(state.Away.Lineup.Instructions);
    }

    [Fact]
    public void Changing_the_instructions_keeps_everybodys_condition_and_works_the_ratings_out_again()
    {
        var state = TickTestMatchState.Create();
        var conditions = state.Home.Active.Select(slot => slot.Condition).ToList();
        var ratings = state.Home.Ratings;

        state.Home.ChangeInstructions(new MatchInstructionsV1 { Mentality = MatchMentality.Attacking, Pressing = MatchPressing.HighPress }, Rules);

        state.Home.Active.Select(slot => slot.Condition).Should().Equal(conditions);
        state.Home.Ratings.Should().NotBe(ratings);
    }

    [Fact]
    public void Time_wasting_that_is_situational_follows_the_instructions_that_hold_now()
    {
        var state = TickTestMatchState.Create();

        state.Home.WastingTime.Should().BeFalse();

        state.Home.ChangeInstructions(new MatchInstructionsV1 { TimeWasting = MatchTimeWasting.On }, Rules);

        state.Home.WastingTime.Should().BeTrue();
    }

    [Fact]
    public void A_reshape_moves_each_player_into_his_new_slot_and_keeps_his_slot_number()
    {
        var state = TickTestMatchState.Create();
        var shape = TestShapes.FourThreeThree;
        var fit = ShapeFit.Fit(state.Home.Active, shape, null, Rules);
        var numbers = state.Home.Active.ToDictionary(slot => slot.Participant.ParticipantId, slot => slot.Slot.SlotNumber);
        var conditions = state.Home.Active.ToDictionary(slot => slot.Participant.ParticipantId, slot => slot.Condition);

        state.Home.Reshape(shape, fit, Rules);

        state.Home.Active.Should().HaveCount(11);

        foreach (var active in state.Home.Active)
        {
            var id = active.Participant.ParticipantId;
            var target = shape.Single(slot => slot.SlotNumber == fit[id]);

            active.Slot.SlotNumber.Should().Be(numbers[id]);
            active.Slot.Family.Should().Be(target.Family);
            active.Slot.Role.Should().Be(target.Role);
            active.Slot.X.Should().Be(target.X);
            active.Slot.Y.Should().Be(target.Y);
            active.Slot.ParticipantId.Should().Be(id);
            active.Condition.Should().Be(conditions[id]);
            active.FamiliarityBasisPoints.Should().Be(LineupResolver.FamiliarityOf(active.Participant, active.Slot, Rules));
        }
    }

    [Fact]
    public void A_player_a_reshape_does_not_mention_keeps_his_slot()
    {
        var state = TickTestMatchState.Create();
        var shape = TestShapes.FourThreeThree;
        var mentioned = ShapeFit.Fit(state.Home.Active, shape, null, Rules);
        var left = state.Home.Active[4];

        mentioned.Remove(left.Participant.ParticipantId);
        state.Home.Reshape(shape, mentioned, Rules);

        state.Home.Active.Single(slot => slot.Participant.ParticipantId == left.Participant.ParticipantId).Slot.Should().Be(left.Slot);
    }

    [Fact]
    public void A_reshape_to_a_slot_the_shape_does_not_have_is_refused()
    {
        var state = TickTestMatchState.Create();
        var one = state.Home.Active[3].Participant.ParticipantId;

        var act = () => state.Home.Reshape(TestShapes.FourThreeThree, new Dictionary<Guid, int> { [one] = 12 }, Rules);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_substitution_a_rule_makes_is_recorded_as_tactical_and_counts_against_the_five()
    {
        var state = TickTestMatchState.Create();
        var off = state.Home.Active[6];
        var on = state.Home.Bench[3];

        SubstitutionPlanner.Substitute(state, MatchSide.Home, state.Home, off, on, MatchSubstitutionReason.Tactical, Rules);

        state.Home.Substitutions.Should().Be(1);
        state.Home.Bench.Should().NotContain(on);
        state.Home.Active.Should().Contain(slot => slot.Participant.ParticipantId == on.ParticipantId && slot.Slot.SlotNumber == off.Slot.SlotNumber);
        state.Events.Should().ContainSingle(matchEvent => matchEvent.Type == EngineEventType.Substitution
            && matchEvent.SubstitutionReason == MatchSubstitutionReason.Tactical
            && matchEvent.ParticipantId == off.Participant.ParticipantId
            && matchEvent.SecondaryParticipantId == on.ParticipantId);
    }
}
