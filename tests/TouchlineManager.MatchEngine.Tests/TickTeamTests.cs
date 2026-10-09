using FluentAssertions;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.MatchEngine.Tests;

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

    private static int TickSpatialUnitsOf(int fixedUnits) => Spatial.TickSpatialUnits.ToUnits(fixedUnits);
}
