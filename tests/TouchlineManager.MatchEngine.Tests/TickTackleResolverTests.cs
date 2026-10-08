using FluentAssertions;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;
using TouchlineManager.MatchEngine.Spatial;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Verifies the tick engine's tackle: who may challenge, what the contest is worth, and what each result does to the
/// bodies, the ball and the log (Milestone 3).
/// </summary>
public sealed class TickTackleResolverTests
{
    private static readonly EngineRulesV2 Rules = EngineRulesV2.Default;

    [Fact]
    public void A_defender_may_challenge_only_inside_ninety_units_of_the_carrier_and_not_while_off_balance()
    {
        var carrier = TickPlayerState.Standing(5_000, 3_500, 0, 10_000);

        TickTackleResolver.InContact(TickPlayerState.Standing(5_089, 3_500, 0, 10_000), carrier).Should().BeTrue();
        TickTackleResolver.InContact(TickPlayerState.Standing(5_090, 3_500, 0, 10_000), carrier).Should().BeFalse("the plan says less than 90");
        TickTackleResolver.InContact(TickPlayerState.Standing(5_065, 3_565, 0, 10_000), carrier).Should().BeFalse("diagonally that is 92 units");

        var stumbling = TickPlayerState.Standing(5_050, 3_500, 0, 10_000);

        stumbling.Lockout = 3;

        TickTackleResolver.InContact(stumbling, carrier).Should().BeFalse();
    }

    [Fact]
    public void A_better_tackler_wins_more_and_a_better_dribbler_wins_less_within_the_duel_band()
    {
        var even = Skills();
        var win = TickTackleResolver.WinChanceBasisPoints(even, even, MatchTacklingStyle.Normal, Rules);

        win.Should().Be(Rules.BaseGroundDuelBasisPoints, "equals are an even contest");

        var elite = TickTackleResolver.WinChanceBasisPoints(Skills(tackling: 20, strength: 20, aggression: 20), even, MatchTacklingStyle.Normal, Rules);
        var weak = TickTackleResolver.WinChanceBasisPoints(Skills(tackling: 3, strength: 3, aggression: 3), even, MatchTacklingStyle.Normal, Rules);
        var wizard = TickTackleResolver.WinChanceBasisPoints(even, Skills(dribbling: 20, agility: 20, composure: 20), MatchTacklingStyle.Normal, Rules);

        elite.Should().BeGreaterThan(win);
        weak.Should().BeLessThan(win);
        wizard.Should().BeLessThan(win);

        foreach (var chance in new[] { elite, weak, wizard })
        {
            chance.Should().BeInRange(Rules.DuelMinWinBasisPoints, Rules.DuelMaxWinBasisPoints);
        }

        TickTackleResolver.WinChanceBasisPoints(Skills(20, 20, 20), Skills(dribbling: 1, agility: 1, composure: 1), MatchTacklingStyle.Aggressive, Rules)
            .Should().Be(Rules.DuelMaxWinBasisPoints);
        TickTackleResolver.WinChanceBasisPoints(Skills(1, 1, 1), Skills(dribbling: 20, agility: 20, composure: 20), MatchTacklingStyle.StayOnFeet, Rules)
            .Should().Be(Rules.DuelMinWinBasisPoints);
    }

    [Fact]
    public void An_aggressive_style_wins_more_and_fouls_more_and_staying_on_the_feet_does_the_reverse()
    {
        var one = Skills();

        var aggressiveWin = TickTackleResolver.WinChanceBasisPoints(one, one, MatchTacklingStyle.Aggressive, Rules);
        var normalWin = TickTackleResolver.WinChanceBasisPoints(one, one, MatchTacklingStyle.Normal, Rules);
        var cautiousWin = TickTackleResolver.WinChanceBasisPoints(one, one, MatchTacklingStyle.StayOnFeet, Rules);

        aggressiveWin.Should().BeGreaterThan(normalWin);
        normalWin.Should().BeGreaterThan(cautiousWin);

        var aggressiveFoul = TickTackleResolver.FoulChanceBasisPoints(one, MatchTacklingStyle.Aggressive, Rules);
        var normalFoul = TickTackleResolver.FoulChanceBasisPoints(one, MatchTacklingStyle.Normal, Rules);
        var cautiousFoul = TickTackleResolver.FoulChanceBasisPoints(one, MatchTacklingStyle.StayOnFeet, Rules);

        aggressiveFoul.Should().BeGreaterThan(normalFoul);
        normalFoul.Should().BeGreaterThan(cautiousFoul);

        // A hothead is more likely to foul than a clean tackler.
        TickTackleResolver.FoulChanceBasisPoints(Skills(tackling: 5, aggression: 20), MatchTacklingStyle.Normal, Rules)
            .Should().BeGreaterThan(TickTackleResolver.FoulChanceBasisPoints(Skills(tackling: 20, aggression: 5), MatchTacklingStyle.Normal, Rules));
    }

    [Fact]
    public void One_draw_decides_the_whole_challenge_and_the_outcomes_fall_in_the_shares_the_chances_give()
    {
        var defender = Skills(tackling: 14, strength: 12, aggression: 12);
        var carrier = Skills(dribbling: 12);
        var win = TickTackleResolver.WinChanceBasisPoints(defender, carrier, MatchTacklingStyle.Normal, Rules);
        var foul = TickTackleResolver.FoulChanceBasisPoints(defender, MatchTacklingStyle.Normal, Rules);
        var counts = new int[4];

        for (var roll = 0; roll < EngineRulesV2.Certain; roll++)
        {
            counts[(int)TickTackleResolver.Resolve(defender, carrier, MatchTacklingStyle.Normal, Rules, roll)]++;
        }

        (counts[(int)TickTackleOutcome.Won] + counts[(int)TickTackleOutcome.PokedLoose]).Should().Be(win);
        counts[(int)TickTackleOutcome.PokedLoose].Should().BeInRange(
            (win * TickTackleResolver.PokedLoosePercent / 100) - 1,
            (win * TickTackleResolver.PokedLoosePercent / 100) + 1);
        counts[(int)TickTackleOutcome.Foul].Should().BeInRange(
            ((EngineRulesV2.Certain - win) * foul / EngineRulesV2.Certain) - 2,
            ((EngineRulesV2.Certain - win) * foul / EngineRulesV2.Certain) + 2);
        counts.Sum().Should().Be(EngineRulesV2.Certain);

        // From a stream the same draws always give the same challenges, one draw each.
        var one = new Pcg32(7);
        var other = new Pcg32(7);

        for (var challenge = 0; challenge < 200; challenge++)
        {
            TickTackleResolver.Resolve(defender, carrier, MatchTacklingStyle.Normal, Rules, one)
                .Should().Be(TickTackleResolver.Resolve(defender, carrier, MatchTacklingStyle.Normal, Rules, other.NextBasisPoints()));
        }

        one.NextBasisPoints().Should().Be(other.NextBasisPoints(), "each challenge took exactly one draw");
    }

    [Fact]
    public void Over_many_challenges_the_better_tackler_takes_the_ball_more_often()
    {
        var elite = Skills(tackling: 18, strength: 16, aggression: 14);
        var ordinary = Skills();
        var dribbler = Skills(dribbling: 16, agility: 16, composure: 14);
        var random = new Pcg32(42);
        var eliteWon = 0;
        var ordinaryWon = 0;
        var againstDribbler = 0;

        for (var challenge = 0; challenge < 3_000; challenge++)
        {
            eliteWon += HasBall(TickTackleResolver.Resolve(elite, ordinary, MatchTacklingStyle.Normal, Rules, random)) ? 1 : 0;
            ordinaryWon += HasBall(TickTackleResolver.Resolve(ordinary, ordinary, MatchTacklingStyle.Normal, Rules, random)) ? 1 : 0;
            againstDribbler += HasBall(TickTackleResolver.Resolve(ordinary, dribbler, MatchTacklingStyle.Normal, Rules, random)) ? 1 : 0;
        }

        eliteWon.Should().BeGreaterThan(ordinaryWon + 300);
        againstDribbler.Should().BeLessThan(ordinaryWon - 300);
    }

    [Fact]
    public void A_clean_tackle_puts_the_ball_at_the_defender_s_feet_and_leaves_the_carrier_off_balance()
    {
        var (ball, defender, carrier) = Challenge();

        TickTackleResolver.Apply(TickTackleOutcome.Won, ref defender, ref carrier, defenderIndex: 14, ball);

        ball.Mode.Should().Be(TickBallMode.Controlled);
        ball.ControllerIndex.Should().Be(14);
        carrier.Lockout.Should().Be(TickTackleResolver.DispossessedLockoutTicks);
        carrier.Speed.Should().BeLessThan(TickSpatialUnits.SpeedToFixedPerTick(700) / 2 + 1);
        defender.Lockout.Should().Be(0);
    }

    [Fact]
    public void A_poked_ball_runs_loose_away_from_the_carrier_towards_the_defender()
    {
        var (ball, defender, carrier) = Challenge();

        TickTackleResolver.Apply(TickTackleOutcome.PokedLoose, ref defender, ref carrier, defenderIndex: 14, ball);

        ball.Mode.Should().Be(TickBallMode.Loose);
        ball.ControllerIndex.Should().Be(-1);
        ball.VelocityX.Should().BeLessThan(0, "the defender is behind the carrier, so it runs back that way");
        ball.GroundSpeed.Should().BeInRange(
            TickSpatialUnits.SpeedToFixedPerTick(TickTackleResolver.PokeSpeedCentimetresPerSecond) - 5,
            TickSpatialUnits.SpeedToFixedPerTick(TickTackleResolver.PokeSpeedCentimetresPerSecond) + 5);
        carrier.Lockout.Should().Be(TickTackleResolver.DispossessedLockoutTicks);
    }

    [Fact]
    public void A_carrier_who_rides_the_challenge_leaves_the_defender_stumbling_and_keeps_the_ball()
    {
        var (ball, defender, carrier) = Challenge();
        var runningSpeed = defender.Speed;

        TickTackleResolver.Apply(TickTackleOutcome.Beaten, ref defender, ref carrier, defenderIndex: 14, ball);

        ball.Mode.Should().Be(TickBallMode.Controlled);
        ball.ControllerIndex.Should().Be(3, "the carrier still has it");
        defender.Lockout.Should().Be(TickTackleResolver.BeatenLockoutTicks);
        defender.Speed.Should().Be(runningSpeed / 2);
        carrier.Lockout.Should().Be(0);
    }

    [Fact]
    public void A_foul_stops_everything_where_it_stands()
    {
        var (ball, defender, carrier) = Challenge();
        var spot = ball.GroundPoint;

        TickTackleResolver.Apply(TickTackleOutcome.Foul, ref defender, ref carrier, defenderIndex: 14, ball);

        ball.Mode.Should().Be(TickBallMode.Loose);
        ball.GroundSpeed.Should().Be(0);
        ball.GroundPoint.Should().Be(spot);
        defender.Speed.Should().Be(0);
        carrier.Speed.Should().Be(0);
    }

    [Fact]
    public void An_off_balance_player_runs_at_half_pace_until_he_has_recovered()
    {
        var profile = TickPlayerProfile.From(PlayerAttributesV1.From(Enumerable.Repeat(10, MatchAttributeNames.Count).ToArray()));
        var fresh = TickPlayerState.Standing(2_000, 3_500, 0, 10_000);
        var stumbling = TickPlayerState.Standing(2_000, 3_500, 0, 10_000);

        stumbling.Lockout = TickTackleResolver.BeatenLockoutTicks;

        var run = new TickMoveIntent(9_000, 3_500, 10_000, Arrive: false);

        for (var tick = 0; tick < TickTackleResolver.BeatenLockoutTicks; tick++)
        {
            TickPlayerPhysics.Step(ref fresh, profile, run);
            TickPlayerPhysics.Step(ref stumbling, profile, run);
        }

        stumbling.Lockout.Should().Be(0);
        stumbling.Speed.Should().BeLessThanOrEqualTo((TickPlayerPhysics.EffectiveTopSpeed(stumbling, profile) / 2) + 100, "tiredness moved the ceiling a hair");
        stumbling.X.Should().BeLessThan(fresh.X);

        for (var tick = 0; tick < 60; tick++)
        {
            TickPlayerPhysics.Step(ref stumbling, profile, run);
        }

        stumbling.Speed.Should().BeGreaterThan(TickPlayerPhysics.EffectiveTopSpeed(stumbling, profile) * 9 / 10);
    }

    [Fact]
    public void A_foul_goes_through_the_possession_engine_s_discipline_onto_the_log()
    {
        var state = TickTestMatchState.Create();
        var fouler = state.Home.Outfield.First(slot => slot.Slot.Family == MatchPositionFamily.Defence).Participant.ParticipantId;

        var outcome = TickTackleResolver.PunishFoul(state, MatchSide.Home, fouler);

        outcome.FoulCommitted.Should().BeTrue();
        outcome.FoulerId.Should().Be(fouler);

        var foul = state.Events.Should().Contain(matchEvent => matchEvent.Type == EngineEventType.Foul).Subject;

        foul.Side.Should().Be(MatchSide.Home);
        foul.ParticipantId.Should().Be(fouler);
    }

    [Fact]
    public void Over_many_fouls_the_cards_come_at_the_rate_the_possession_engine_gives()
    {
        var yellows = 0;
        var fouls = 0;

        for (var seed = 1UL; seed <= 400UL; seed++)
        {
            var state = TickTestMatchState.Create(seed);
            var fouler = state.Home.Outfield.First(slot => slot.Slot.Family == MatchPositionFamily.Defence).Participant.ParticipantId;

            var outcome = TickTackleResolver.PunishFoul(state, MatchSide.Home, fouler);

            fouls++;
            yellows += outcome.CardShown ? 1 : 0;
        }

        // About one foul in six is booked (YellowCardPerFoulBasisPoints is 1,600).
        ((double)yellows / fouls).Should().BeInRange(0.10, 0.24);
    }

    private static bool HasBall(TickTackleOutcome outcome) => outcome is TickTackleOutcome.Won or TickTackleOutcome.PokedLoose;

    private static TickPlayerSkills Skills(
        int tackling = 10,
        int strength = 10,
        int aggression = 10,
        int dribbling = 10,
        int agility = 10,
        int composure = 10)
    {
        var values = Enumerable.Repeat(10, MatchAttributeNames.Count).ToArray();

        values[(int)MatchAttributeName.Tackling] = tackling;
        values[(int)MatchAttributeName.Strength] = strength;
        values[(int)MatchAttributeName.Aggression] = aggression;
        values[(int)MatchAttributeName.Dribbling] = dribbling;
        values[(int)MatchAttributeName.Agility] = agility;
        values[(int)MatchAttributeName.Composure] = composure;

        return TickPlayerSkills.From(PlayerAttributesV1.From(values));
    }

    /// <summary>A carrier (player 3) running at pace with the ball at his feet, and a defender 0.5 m behind him.</summary>
    private static (TickBallPhysics Ball, TickPlayerState Defender, TickPlayerState Carrier) Challenge()
    {
        var carrier = TickPlayerState.Standing(5_000, 3_500, 0, 10_000);
        var defender = TickPlayerState.Standing(4_950, 3_500, 0, 10_000);

        carrier.Speed = TickSpatialUnits.SpeedToFixedPerTick(700);
        defender.Speed = TickSpatialUnits.SpeedToFixedPerTick(700);

        var ball = new TickBallPhysics();

        ball.PlaceAt(5_000, 3_500);
        ball.Attach(3);
        ball.Carry(carrier.X, carrier.Y, carrier.Heading, carrier.Speed);

        return (ball, defender, carrier);
    }
}
