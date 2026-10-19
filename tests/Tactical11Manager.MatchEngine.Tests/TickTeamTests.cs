using FluentAssertions;
using Tactical11Manager.MatchEngine.Configuration;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Simulation;
using Tactical11Manager.MatchEngine.Tick;

namespace Tactical11Manager.MatchEngine.Tests;

/// <summary>Verifies the seats a side's bodies sit in and how they follow the runtime's substitutions and cards (Milestone 8).</summary>
public sealed class TickTeamTests
{
    private static readonly EngineRulesV2 Rules = EngineRulesV2.Default;

    [Fact]
    public void The_goalkeeper_has_the_first_seat_and_the_rest_follow_in_slot_order()
    {
        var state = TickTestMatchState.Create();
        var team = new TickTeam(state.Home);

        team.Count.Should().Be(11);
        team.IsKeeper(0).Should().BeTrue();
        Enumerable.Range(1, 10).Should().OnlyContain(seat => !team.IsKeeper(seat));
        team.SlotNumber.Skip(1).Take(10).Should().BeInAscendingOrder();
        team.Id.Take(11).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void A_slot_entity_is_its_sides_slot_number_so_the_away_side_follows_the_home_side()
    {
        var state = TickTestMatchState.Create();
        var home = new TickTeam(state.Home);
        var away = new TickTeam(state.Away);

        Enumerable.Range(0, 11).Select(home.Entity).Should().BeEquivalentTo(Enumerable.Range(0, 11));
        Enumerable.Range(0, 11).Select(away.Entity).Should().BeEquivalentTo(Enumerable.Range(11, 11));
    }

    [Fact]
    public void A_player_who_is_sent_off_gives_up_his_seat_and_the_others_close_up()
    {
        var state = TickTestMatchState.Create();
        var team = new TickTeam(state.Home);
        var recording = new TickMatchRecording();
        var sentOff = team.Id[4];
        var stays = team.Id[5];

        state.Home.RemoveParticipant(sentOff, 30, Rules).Should().BeTrue();

        team.Sync(recording).Should().BeTrue();

        team.Count.Should().Be(10);
        team.SeatOf(sentOff).Should().Be(-1);
        team.SeatOf(stays).Should().Be(4, "the seat behind him moved up");
        team.IsKeeper(0).Should().BeTrue();
        recording.Rosters.Should().ContainSingle(stamp => stamp.Occupant == Guid.Empty);
    }

    [Fact]
    public void When_the_goalkeeper_is_sent_off_the_best_pair_of_hands_takes_the_gloves()
    {
        var state = TickTestMatchState.Create();
        var team = new TickTeam(state.Home);
        var keeper = team.Id[0];

        state.Home.RemoveParticipant(keeper, 30, Rules).Should().BeTrue();
        team.Sync(null);

        team.Count.Should().Be(10);
        team.IsKeeper(0).Should().BeTrue("every module assumes seat 0 is the goalkeeper");
        team.Id[0].Should().NotBe(keeper);
        Enumerable.Range(1, 9).Should().OnlyContain(seat => !team.IsKeeper(seat));
    }

    [Fact]
    public void A_substitute_takes_his_mans_seat_and_stands_where_he_stood()
    {
        var state = TickTestMatchState.Create();
        var team = new TickTeam(state.Home);
        var recording = new TickMatchRecording();
        var substitute = state.Home.Bench[0];
        var seat = 6;
        var slot = team.SlotNumber[seat];

        team.Body[seat] = team.Body[seat] with { X = 4_000_000, Y = 2_000_000 };
        state.Home.ReplaceOccupant(slot, substitute, 60, Rules);

        team.Sync(recording).Should().BeTrue();

        team.Count.Should().Be(11);
        team.Id[seat].Should().Be(substitute.ParticipantId);
        team.SlotNumber[seat].Should().Be(slot);
        team.Body[seat].X.Should().Be(4_000_000);
        team.Body[seat].Y.Should().Be(2_000_000);
        recording.Rosters.Should().ContainSingle(stamp => stamp.Occupant == substitute.ParticipantId && stamp.Entity == team.Entity(seat));
    }

    [Fact]
    public void Condition_follows_the_energy_a_player_has_spent()
    {
        var state = TickTestMatchState.Create();
        var team = new TickTeam(state.Home);
        var seat = 3;
        var start = team.ConditionOf(seat);

        team.PushCondition();
        team.ActiveOf(seat)!.Condition.ConditionBasisPoints.Should().Be(start, "a fresh player has lost nothing");

        // A quarter of the tank spent costs 130% of that in condition.
        team.Body[seat].Energy -= TickPlayerPhysics.EnergyFull / 4;
        team.PushCondition();

        team.ActiveOf(seat)!.Condition.ConditionBasisPoints.Should().Be(start - (2_500 * TickTeam.ConditionPerEnergyPercent / 100));
    }

    [Fact]
    public void A_tired_seat_plays_worse_after_a_refresh_and_a_fresh_one_does_not_change()
    {
        var state = TickTestMatchState.Create();
        var team = new TickTeam(state.Home);
        var tired = 3;
        var fresh = 4;
        var freshBefore = team.Skills[fresh];

        team.Skills[tired].Tackling.Should().Be(freshBefore.Tackling, "uniform sides: the seats start alike");

        // Half the tank gone costs 130% of that in condition: well under 90%, so the drop is real.
        team.Body[tired].Energy -= TickPlayerPhysics.EnergyFull / 2;
        team.RefreshSkills();

        var live = team.LiveConditionOf(tired);
        var drop = TickSkillTiredness.FullDropBasisPoints(live);

        live.Should().BeLessThan(team.ConditionOf(tired));
        drop.Should().BePositive();
        team.Skills[tired].Condition.Should().Be(live);
        team.Skills[tired].Tackling.Should().BeLessThan(freshBefore.Tackling);
        team.Skills[tired].Decisions.Should().BeLessThan(freshBefore.Decisions);
        team.Skills[tired].Decisions.Should().BeGreaterThan(team.Skills[tired].Tackling, "a mental skill loses half as much");
        team.Skills[tired].Pace.Should().Be(freshBefore.Pace);
        team.Skills[fresh].Should().Be(freshBefore);
    }

    [Fact]
    public void The_skills_follow_the_same_condition_the_runtime_is_told()
    {
        var state = TickTestMatchState.Create();
        var team = new TickTeam(state.Home);
        var seat = 5;

        team.Body[seat].Energy -= TickPlayerPhysics.EnergyFull / 3;
        team.PushCondition();
        team.RefreshSkills();

        team.Skills[seat].Condition.Should().Be(team.ActiveOf(seat)!.Condition.ConditionBasisPoints);
    }

    [Fact]
    public void A_substitute_comes_on_with_the_skills_his_own_condition_gives()
    {
        var state = TickTestMatchState.Create();
        var team = new TickTeam(state.Home);
        var substitute = state.Home.Bench[0];
        var seat = 6;

        team.Body[seat].Energy -= TickPlayerPhysics.EnergyFull / 2;
        team.RefreshSkills();
        state.Home.ReplaceOccupant(team.SlotNumber[seat], substitute, 60, Rules);
        team.Sync(null);

        team.Id[seat].Should().Be(substitute.ParticipantId);
        team.Skills[seat].Condition.Should().Be(team.ConditionOf(seat));
        team.Skills[seat].Natural.Should().BeSameAs(substitute.Attributes);
        team.LiveConditionOf(seat).Should().Be(team.ConditionOf(seat), "he has not run yet");
    }

    [Fact]
    public void A_sent_off_mans_tired_skills_leave_with_him_and_the_others_keep_theirs()
    {
        var state = TickTestMatchState.Create();
        var team = new TickTeam(state.Home);
        var stays = 6;

        team.Body[stays].Energy -= TickPlayerPhysics.EnergyFull / 2;
        team.RefreshSkills();

        var tired = team.Skills[stays];
        var id = team.Id[stays];

        state.Home.RemoveParticipant(team.Id[4], 30, Rules).Should().BeTrue();
        team.Sync(null);

        team.SeatOf(id).Should().Be(stays - 1);
        team.Skills[stays - 1].Should().Be(tired);
        team.Skills[stays - 1].Condition.Should().Be(team.LiveConditionOf(stays - 1));
    }

    [Fact]
    public void A_man_who_takes_the_gloves_keeps_his_tired_skills_and_his_raw_ones_stay_paired()
    {
        var state = TickTestMatchState.Create();
        var team = new TickTeam(state.Home);

        for (var seat = 1; seat < team.Count; seat++)
        {
            team.Body[seat].Energy -= TickPlayerPhysics.EnergyFull / 2;
        }

        team.RefreshSkills();
        state.Home.RemoveParticipant(team.Id[0], 30, Rules).Should().BeTrue();
        team.Sync(null);

        var keeper = team.Skills[0];

        keeper.Natural.Should().NotBeNull();
        keeper.Condition.Should().Be(team.LiveConditionOf(0));
        keeper.Uncurved().Tackling.Should().BeLessThan(keeper.Natural!.ValueOf(MatchAttributeName.Tackling) * TickPlayerSkills.Scale, "the raw set is tired too");
    }

    [Fact]
    public void The_raw_skills_a_free_kick_is_taken_on_are_tired_too()
    {
        var state = TickTestMatchState.Create();
        var team = new TickTeam(state.Home);
        var seat = 7;
        var sheet = team.Skills[seat].Natural!.ValueOf(MatchAttributeName.SetPieces) * TickPlayerSkills.Scale;

        var startDrop = TickSkillTiredness.FullDropBasisPoints(team.ConditionOf(seat));

        team.Skills[seat].Uncurved().SetPieces.Should().Be(((sheet * (10_000 - startDrop)) + 5_000) / 10_000, "a man who starts below full carries his drop from kick-off");

        team.Body[seat].Energy -= TickPlayerPhysics.EnergyFull / 2;
        team.RefreshSkills();

        var drop = TickSkillTiredness.FullDropBasisPoints(team.LiveConditionOf(seat));

        team.Skills[seat].Uncurved().SetPieces.Should().Be(((sheet * (10_000 - drop)) + 5_000) / 10_000);
    }

    [Fact]
    public void The_interval_gives_back_energy_up_to_what_the_player_started_with()
    {
        var state = TickTestMatchState.Create();
        var team = new TickTeam(state.Home);
        var seat = 2;
        var full = team.Body[seat].Energy;

        team.Body[seat].Energy = full - (TickPlayerPhysics.EnergyFull / 4);
        team.Rest(Rules.HalfTimeConditionRecoveryBasisPoints);

        team.Body[seat].Energy.Should().BeGreaterThan(full - (TickPlayerPhysics.EnergyFull / 4));
        team.Body[seat].Energy.Should().BeLessThanOrEqualTo(full);

        team.Rest(10_000);

        team.Body[seat].Energy.Should().Be(full, "a rest can never fill the tank past where it began");
    }

    [Fact]
    public void Both_sides_are_placed_in_their_own_halves_for_a_kick_off()
    {
        var state = TickTestMatchState.Create();
        var home = new TickTeam(state.Home);
        var away = new TickTeam(state.Away);

        home.PlaceForKickOff(homeKicksOff: true);
        away.PlaceForKickOff(homeKicksOff: true);

        for (var seat = 0; seat < 11; seat++)
        {
            TickSpatialUnitsOf(home.Body[seat].X).Should().BeLessThanOrEqualTo(5_000);
            TickSpatialUnitsOf(away.Body[seat].X).Should().BeGreaterThanOrEqualTo(5_000);
            home.Body[seat].Speed.Should().Be(0);
        }
    }

    [Fact]
    public void A_reshape_changes_the_positions_the_seats_ask_for_and_nothing_about_the_men_in_them()
    {
        var state = TickTestMatchState.Create();
        var team = new TickTeam(state.Home);
        var ids = team.Id.Take(11).ToArray();
        var slotNumbers = team.SlotNumber.Take(11).ToArray();
        var bodies = team.Body.Take(11).ToArray();
        var before = team.Spec.Take(11).ToArray();
        var shape = TestShapes.FourThreeThree;

        var fit = ShapeFit.Fit(state.Home.Active, shape, null, Rules);
        state.Home.Reshape(shape, fit, Rules);
        team.Reshape();

        team.Id.Take(11).Should().Equal(ids);
        team.SlotNumber.Take(11).Should().Equal(slotNumbers, "a slot number is a man's name on the pitch and a reshape does not rename it");
        team.Body.Take(11).Should().Equal(bodies);
        team.Spec.Take(11).Should().NotEqual(before);

        for (var seat = 0; seat < 11; seat++)
        {
            team.Spec[seat].Should().Be(TickAnchorSpec.From(shape.Single(slot => slot.SlotNumber == fit[team.Id[seat]])));
        }
    }

    [Fact]
    public void A_reshape_drops_what_was_worked_out_for_the_old_shape()
    {
        var state = TickTestMatchState.Create();
        var team = new TickTeam(state.Home);

        team.HasOffset[3] = true;
        team.SupporterMask = 0b101;
        team.RunnerMask = 0b110;
        team.PreviousPresser = 4;
        team.ReturnSlot = 7;
        team.ThirdSlot = 8;

        state.Home.Reshape(TestShapes.FourThreeThree, ShapeFit.Fit(state.Home.Active, TestShapes.FourThreeThree, null, Rules), Rules);
        team.Reshape();

        team.HasOffset.Should().OnlyContain(set => !set);
        team.SupporterMask.Should().Be(0);
        team.RunnerMask.Should().Be(0);
        team.PreviousPresser.Should().Be(-1);
        team.ReturnSlot.Should().Be(0);
        team.ThirdSlot.Should().Be(0);
    }

    [Fact]
    public void A_man_who_took_the_gloves_keeps_the_goalkeepers_spec_through_a_reshape()
    {
        var state = TickTestMatchState.Create();
        var team = new TickTeam(state.Home);

        state.Home.RemoveParticipant(team.Id[0], 30, Rules);
        team.Sync(null);

        var keeper = team.Id[0];
        var shape = TestShapes.FourThreeThree;

        state.Home.Reshape(shape, ShapeFit.Fit(state.Home.Active, shape, null, Rules), Rules);
        team.Reshape();

        team.Id[0].Should().Be(keeper);
        team.IsKeeper(0).Should().BeTrue("every module assumes seat 0 is the goalkeeper");
        Enumerable.Range(1, 9).Should().OnlyContain(seat => !team.IsKeeper(seat));
    }

    [Fact]
    public void A_restyle_follows_the_instructions_the_runtime_holds_now()
    {
        var state = TickTestMatchState.Create();
        var team = new TickTeam(state.Home);
        var before = team.Style;

        state.Home.ChangeInstructions(new MatchInstructionsV1 { Mentality = MatchMentality.Attacking, Pressing = MatchPressing.HighPress }, Rules);

        team.Style.Should().Be(before, "the style is only worked out again when asked");

        team.Restyle();

        team.Style.Should().Be(TickTeamStyle.From(state.Home.Instructions));
        team.Style.Should().NotBe(before);
    }

    private static int TickSpatialUnitsOf(int fixedUnits) => Spatial.TickSpatialUnits.ToUnits(fixedUnits);
}
