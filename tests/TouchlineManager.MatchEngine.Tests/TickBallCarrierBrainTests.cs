using FluentAssertions;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;
using TouchlineManager.MatchEngine.Spatial;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Verifies the tick engine's ball-carrier brain: pressure and composure, the xG heuristic, the choice between shooting,
/// passing, crossing, dribbling, shielding and recycling, and the dispersion of the kick (Milestone 5).
/// </summary>
/// <remarks>
/// Every position in a scene is given in the carrier's side's own point of view (it attacks towards X = 10,000), whichever
/// end the side really plays; a scene mirrors them for the away side. Where a test needs the carrier to see only some of his
/// teammates it gives him Vision 1 (he sees 23 m) and leaves the rest farther away than that.
/// </remarks>
public sealed class TickBallCarrierBrainTests
{
    /// <summary>A 4-4-2 in the side's own point of view: goalkeeper first, then defence, midfield, attack.</summary>
    private static readonly TickAnchorSpec[] FourFourTwo =
    [
        new(500, 3_500, MatchPositionFamily.Goalkeeper),
        new(2_200, 900, MatchPositionFamily.Defence),
        new(2_000, 2_500, MatchPositionFamily.Defence),
        new(2_000, 4_500, MatchPositionFamily.Defence),
        new(2_200, 6_100, MatchPositionFamily.Defence),
        new(5_000, 900, MatchPositionFamily.Midfield),
        new(4_800, 2_600, MatchPositionFamily.Midfield),
        new(4_800, 4_400, MatchPositionFamily.Midfield),
        new(5_000, 6_100, MatchPositionFamily.Midfield),
        new(7_500, 2_600, MatchPositionFamily.Attack),
        new(7_500, 4_400, MatchPositionFamily.Attack),
    ];

    /// <summary>Everybody back in the half they would defend: the carrier's teammates at the start of a move.</summary>
    private static readonly SpatialPoint[] OwnHalf =
    [
        new(500, 3_500),
        new(2_000, 900),
        new(1_800, 2_500),
        new(1_800, 4_500),
        new(2_000, 6_100),
        new(4_200, 900),
        new(4_200, 2_600),
        new(4_200, 4_400),
        new(4_200, 6_100),
        new(4_500, 2_800),
        new(4_500, 4_200),
    ];

    /// <summary>The opposition parked on their own goal line: nobody near a lane or a carrier.</summary>
    private static readonly SpatialPoint[] DeepBlock =
    [
        new(9_800, 3_500),
        new(9_300, 900),
        new(9_300, 2_500),
        new(9_300, 4_500),
        new(9_300, 6_100),
        new(9_300, 1_700),
        new(9_300, 3_000),
        new(9_300, 4_000),
        new(9_300, 5_300),
        new(9_300, 2_200),
        new(9_300, 4_800),
    ];

    /// <summary>The opposition holding a high line at 8,200 with the lane through the middle left open.</summary>
    private static readonly SpatialPoint[] HighLine =
    [
        new(9_800, 3_500),
        new(8_200, 600),
        new(8_200, 1_300),
        new(8_200, 2_000),
        new(8_200, 2_700),
        new(8_200, 4_000),
        new(8_200, 4_300),
        new(8_200, 4_700),
        new(8_200, 5_400),
        new(8_200, 6_100),
        new(8_200, 6_600),
    ];

    /// <summary>A back four on the 18-yard line, a midfield four ahead of it, two forwards.</summary>
    private static readonly SpatialPoint[] CompactFourFourTwo =
    [
        new(9_500, 3_500),
        new(8_200, 900),
        new(8_200, 2_500),
        new(8_200, 4_500),
        new(8_200, 6_100),
        new(6_500, 900),
        new(6_500, 2_600),
        new(6_500, 4_400),
        new(6_500, 6_100),
        new(6_000, 2_700),
        new(6_000, 4_300),
    ];

    [Fact]
    public void A_sharp_mind_decides_every_tick_and_a_dull_one_every_other_tick()
    {
        var sharp = Skills(13);
        var dull = Skills(8);

        TickBallCarrierBrain.DecisionInterval(sharp, MatchTempo.Normal).Should().Be(1);
        TickBallCarrierBrain.DecisionInterval(dull, MatchTempo.Normal).Should().Be(2);
        TickBallCarrierBrain.DecisionInterval(Skills(10), MatchTempo.High).Should().Be(1, "a high tempo speeds the mind up");
        TickBallCarrierBrain.DecisionInterval(Skills(13), MatchTempo.Low).Should().Be(2, "a low tempo slows it down");
    }

    [Fact]
    public void Pressure_grows_as_a_defender_closes_and_as_he_runs_at_the_carrier()
    {
        var carrier = Standing(5_000, 3_500);

        int Pressure(int distance, int closingCentimetresPerSecond)
        {
            var defender = Standing(5_000 + distance, 3_500);

            defender.Heading = TickTrigonometry.HalfTurn;
            defender.Speed = TickSpatialUnits.SpeedToFixedPerTick(closingCentimetresPerSecond);

            return TickBallCarrierBrain.MeasurePressure(carrier, [Standing(9_000, 3_500), defender]);
        }

        Pressure(1_500, 0).Should().Be(0, "nobody is near");
        Pressure(100, 0).Should().Be(TickBallCarrierBrain.DistancePressure, "at contact the distance is complete");
        Pressure(300, 0).Should().BeLessThan(Pressure(100, 0)).And.BeGreaterThan(Pressure(600, 0));
        Pressure(300, 700).Should().BeGreaterThan(Pressure(300, 0), "the defender is running at him");
        Pressure(100, 900).Should().BeLessThanOrEqualTo(10_000);
    }

    [Fact]
    public void The_goalkeeper_never_presses_and_a_second_defender_adds_to_the_pressure()
    {
        var carrier = Standing(9_000, 3_500);

        TickBallCarrierBrain.MeasurePressure(carrier, [Standing(9_050, 3_500)])
            .Should().Be(0, "index 0 is the goalkeeper");

        var one = TickBallCarrierBrain.MeasurePressure(carrier, [Standing(0, 0), Standing(9_300, 3_500)]);
        var two = TickBallCarrierBrain.MeasurePressure(carrier, [Standing(0, 0), Standing(9_300, 3_500), Standing(9_000, 3_300)]);

        two.Should().BeGreaterThan(one);
    }

    [Fact]
    public void Composure_scales_the_pressure_a_player_feels()
    {
        TickBallCarrierBrain.EffectivePressure(10_000, 20).Should().Be(6_000);
        TickBallCarrierBrain.EffectivePressure(10_000, 10).Should().Be(10_000);
        TickBallCarrierBrain.EffectivePressure(10_000, 1).Should().Be(13_600);
    }

    [Fact]
    public void The_error_angle_shrinks_with_skill_grows_with_pressure_and_never_reaches_zero()
    {
        TickBallCarrierBrain.ErrorAngle(20, 20, TickBallCarrierBrain.PassBaseError, 0)
            .Should().BeGreaterThan(0, "the plan's formula gives a perfect player no error; the floor keeps him human");
        TickBallCarrierBrain.ErrorAngle(20, 20, TickBallCarrierBrain.PassBaseError, 0)
            .Should().BeLessThan(TickBallCarrierBrain.ErrorAngle(10, 10, TickBallCarrierBrain.PassBaseError, 0))
            .And.BeLessThan(TickBallCarrierBrain.ErrorAngle(1, 1, TickBallCarrierBrain.PassBaseError, 0));
        TickBallCarrierBrain.ErrorAngle(10, 10, TickBallCarrierBrain.PassBaseError, 10_000)
            .Should().BeGreaterThan(TickBallCarrierBrain.ErrorAngle(10, 10, TickBallCarrierBrain.PassBaseError, 0));
        TickBallCarrierBrain.ErrorAngle(10, 10, TickBallCarrierBrain.PassBaseError, 0)
            .Should().Be(15, "30 units at 50% of the error");
    }

    [Fact]
    public void The_xG_falls_with_distance_and_angle_and_blockers_and_rises_with_finishing()
    {
        var average = Skills(10);

        int Goals(int x, int y, int blockers = 0, TickPlayerSkills? skills = null) =>
            TickBallCarrierBrain.ExpectedGoals(x, y, blockers, skills ?? average);

        Goals(8_900, 3_500).Should().BeGreaterThan(Goals(8_000, 3_500));
        Goals(8_000, 3_500).Should().BeGreaterThan(Goals(7_700, 3_500));
        Goals(8_900, 3_500).Should().BeGreaterThan(Goals(8_900, 1_800), "a central shot sees more goal");
        Goals(9_700, 1_500).Should().BeLessThan(Goals(9_000, 3_500) / 4, "a tight angle is worth little");
        Goals(8_900, 3_500, blockers: 1).Should().BeLessThan(Goals(8_900, 3_500));
        Goals(8_900, 3_500, blockers: 2).Should().BeLessThan(Goals(8_900, 3_500, blockers: 1));
        Goals(8_900, 3_500, skills: Skills(18)).Should().BeGreaterThan(Goals(8_900, 3_500));
        Goals(6_500, 3_500).Should().Be(0, "beyond 26.5 m there is no shot");
        Goals(10_100, 3_500).Should().Be(0, "level with the goal line is no shot");
    }

    [Fact]
    public void A_striker_alone_at_eleven_metres_shoots_for_the_far_corner_beyond_the_line()
    {
        var scene = new Scene(true, 9, Move(OwnHalf, (9, 8_900, 3_500)), DeepBlock);
        var decision = scene.Decide();

        decision.Action.Should().Be(TickCarrierAction.Shoot);
        decision.Receiver.Should().Be(-1);
        decision.Target.X.Should().Be(SpatialPitch.PitchLength + TickBallCarrierBrain.ShotOvershoot);
        decision.Target.Y.Should().BeInRange(SpatialPitch.GoalYMin + 100, SpatialPitch.GoalYMax - 100, "inside the posts");

        var mirrored = new Scene(false, 9, Move(OwnHalf, (9, 8_900, 3_500)), DeepBlock).Decide();

        mirrored.Action.Should().Be(TickCarrierAction.Shoot);
        mirrored.Target.X.Should().Be(-TickBallCarrierBrain.ShotOvershoot, "the away side shoots at the home goal");
        mirrored.Target.Y.Should().Be(SpatialPitch.PitchWidth - decision.Target.Y);
    }

    [Fact]
    public void A_shot_from_the_right_is_aimed_at_the_far_corner_which_is_the_left_post_and_vice_versa()
    {
        var fromLow = new Scene(true, 9, Move(OwnHalf, (9, 8_900, 2_500)), DeepBlock).Decide();
        var fromHigh = new Scene(true, 9, Move(OwnHalf, (9, 8_900, 4_500)), DeepBlock).Decide();

        fromLow.Action.Should().Be(TickCarrierAction.Shoot);
        fromHigh.Action.Should().Be(TickCarrierAction.Shoot);
        fromLow.Target.Y.Should().BeGreaterThan(SpatialPitch.GoalYCenter);
        fromHigh.Target.Y.Should().BeLessThan(SpatialPitch.GoalYCenter);
    }

    [Fact]
    public void A_player_never_shoots_from_beyond_range_and_a_goalkeeper_never_shoots()
    {
        var far = new Scene(true, 6, Move(OwnHalf, (6, 6_800, 3_500)), DeepBlock).Decide();

        far.Action.Should().NotBe(TickCarrierAction.Shoot);

        var keeper = new Scene(true, 0, Move(OwnHalf, (0, 9_000, 3_500)), DeepBlock).Decide();

        keeper.Action.Should().NotBe(TickCarrierAction.Shoot).And.NotBe(TickCarrierAction.Dribble);
    }

    [Fact]
    public void An_attacking_side_values_a_shot_more_than_a_defensive_one()
    {
        var attacking = new Scene(true, 9, Move(OwnHalf, (9, 8_900, 3_500)), DeepBlock) { Mentality = MatchMentality.Attacking }.Decide();
        var defensive = new Scene(true, 9, Move(OwnHalf, (9, 8_900, 3_500)), DeepBlock) { Mentality = MatchMentality.Defensive }.Decide();

        attacking.Action.Should().Be(TickCarrierAction.Shoot);
        defensive.Action.Should().Be(TickCarrierAction.Shoot);
        attacking.Utility.Should().BeGreaterThan(defensive.Utility);
    }

    [Fact]
    public void A_shot_through_a_crowd_is_worth_less_than_the_same_shot_with_a_clear_line()
    {
        // Eleven metres out, where a shot is worth taking: the crowd stands on the line to goal, so each man of it
        // halves the chance the shot gets through (the keeper already counts as one of them, Milestone 9).
        var clear = new Scene(true, 9, Move(OwnHalf, (9, 8_900, 3_500)), DeepBlock).Decide();
        var crowded = new Scene(true, 9, Move(OwnHalf, (9, 8_900, 3_500)), Move(DeepBlock, (6, 9_000, 3_400), (7, 9_050, 3_600))).Decide();

        clear.Action.Should().Be(TickCarrierAction.Shoot);
        crowded.Utility.Should().BeLessThan(clear.Utility);
    }

    [Fact]
    public void An_isolated_winger_at_the_byline_crosses_into_the_area_instead_of_running_out_of_play()
    {
        var scene = new Scene(
            true,
            8,
            Move(OwnHalf, (8, 9_700, 6_300), (9, 9_000, 3_000), (10, 9_000, 4_200)),
            DeepBlock);
        var decision = scene.Decide();

        decision.Action.Should().Be(TickCarrierAction.Cross);
        decision.Receiver.Should().BeOneOf([9, 10], "a striker in the area is the target");
        decision.Target.X.Should().BeGreaterThanOrEqualTo(SpatialPitch.PitchLength - SpatialPitch.PenaltyBoxWidth);
        decision.Target.Y.Should().BeInRange(SpatialPitch.PenaltyBoxYMin, SpatialPitch.PenaltyBoxYMax);
    }

    [Fact]
    public void A_winger_with_nobody_in_the_area_still_crosses_to_the_penalty_spot()
    {
        var decision = new Scene(true, 8, Move(OwnHalf, (8, 9_500, 6_300)), DeepBlock).Decide();

        decision.Action.Should().Be(TickCarrierAction.Cross);
        decision.Receiver.Should().Be(-1);
        decision.Target.Should().Be(new SpatialPoint(TickBallCarrierBrain.CrossFallbackX, SpatialPitch.GoalYCenter));
    }

    [Fact]
    public void A_cross_picks_the_striker_with_the_most_room_and_never_one_who_is_offside()
    {
        // Striker 9 has a defender on him; striker 10 is free. The back line is at 9,300 so nobody is offside here.
        var crowded = Move(DeepBlock, (6, 8_950, 3_000), (3, 9_300, 5_200));
        var decision = new Scene(true, 8, Move(OwnHalf, (8, 9_500, 6_300), (9, 8_900, 3_000), (10, 8_900, 4_300)), crowded).Decide();

        decision.Action.Should().Be(TickCarrierAction.Cross);
        decision.Receiver.Should().Be(10);

        // Pull the back line to 8,500 and put striker 10 beyond it and the ball: he is offside, and a reading carrier does not pick him.
        var line = Move(DeepBlock, (1, 8_500, 900), (2, 8_500, 2_500), (3, 8_500, 4_500), (4, 8_500, 6_100), (5, 8_500, 1_700), (6, 8_500, 3_000), (7, 8_500, 4_000), (8, 8_500, 5_300), (9, 8_500, 2_200), (10, 8_500, 4_800));
        var offside = new Scene(true, 8, Move(OwnHalf, (8, 8_000, 6_300), (9, 8_300, 3_000), (10, 9_000, 4_200)), line).Decide();

        offside.Receiver.Should().NotBe(10);
    }

    [Fact]
    public void Under_a_press_with_the_way_forward_shut_the_carrier_gives_the_ball_back()
    {
        // The carrier sees 23 m: his two centre-backs, with a presser on top of him and the forwards out of sight.
        var attackers = Move(OwnHalf, (6, 3_000, 3_500), (5, 8_000, 900), (7, 8_000, 4_400), (8, 8_000, 6_100), (9, 8_000, 2_800), (10, 8_000, 4_200));
        var defenders = Move(DeepBlock, (9, 3_150, 3_500));
        var scene = new Scene(true, 6, attackers, defenders).WithVision(6, 1).DefenderMoving(9, TickTrigonometry.HalfTurn, 700);
        var decision = scene.Decide();

        decision.Action.Should().Be(TickCarrierAction.Recycle);
        decision.Target.X.Should().BeLessThan(3_000, "backwards");
        decision.Receiver.Should().BeOneOf([2, 3]);
    }

    [Fact]
    public void The_same_carrier_with_time_on_the_ball_carries_it_forward_instead()
    {
        var attackers = Move(OwnHalf, (6, 3_000, 3_500), (5, 8_000, 900), (7, 8_000, 4_400), (8, 8_000, 6_100), (9, 8_000, 2_800), (10, 8_000, 4_200));
        var decision = new Scene(true, 6, attackers, DeepBlock).WithVision(6, 1).Decide();

        decision.Action.Should().Be(TickCarrierAction.Dribble);
        decision.Target.X.Should().BeGreaterThan(3_000);
        decision.PaceBasisPoints.Should().BeGreaterThan(0);
    }

    [Fact]
    public void A_high_tempo_carries_the_ball_harder_than_a_low_one()
    {
        var attackers = Move(OwnHalf, (6, 3_000, 3_500), (5, 8_000, 900), (7, 8_000, 4_400), (8, 8_000, 6_100), (9, 8_000, 2_800), (10, 8_000, 4_200));
        var fast = new Scene(true, 6, attackers, DeepBlock) { Tempo = MatchTempo.High }.WithVision(6, 1).Decide();
        var slow = new Scene(true, 6, attackers, DeepBlock) { Tempo = MatchTempo.Low }.WithVision(6, 1).Decide();

        fast.Action.Should().Be(TickCarrierAction.Dribble);
        slow.Action.Should().Be(TickCarrierAction.Dribble);
        fast.Utility.Should().BeGreaterThan(slow.Utility);
        fast.PaceBasisPoints.Should().BeGreaterThan(slow.PaceBasisPoints);
    }

    [Fact]
    public void A_dribble_never_runs_into_a_defender_standing_on_its_line()
    {
        var attackers = Move(OwnHalf, (6, 5_000, 3_500), (5, 500, 6_900), (7, 500, 6_800), (8, 500, 6_700), (9, 500, 6_600), (10, 500, 6_500));
        var blocked = Move(DeepBlock, (9, 5_800, 3_500), (6, 5_700, 3_000), (7, 5_700, 4_000));
        var decision = new Scene(true, 6, attackers, blocked).WithVision(6, 1).Decide();

        if (decision.Action == TickCarrierAction.Dribble)
        {
            // Wherever he goes, it is not through the man in front of him.
            var line = TickOffBallSupport.SegmentDistanceSquared(5_800, 3_500, 5_000, 3_500, decision.Target.X, decision.Target.Y);

            line.Should().BeGreaterThan(290L * 290);
        }
    }

    [Fact]
    public void A_calm_strong_player_under_moderate_pressure_with_no_way_forward_shields_the_ball()
    {
        var attackers = Move(OwnHalf, (6, 6_000, 3_500), (5, 4_800, 3_500), (0, 500, 3_500), (1, 500, 900), (2, 500, 2_500), (3, 500, 4_500), (4, 500, 6_100), (7, 500, 6_300), (8, 500, 6_500), (9, 500, 6_700), (10, 500, 6_900));
        var defenders = Move(DeepBlock, (9, 6_250, 3_500));
        var scene = new Scene(true, 6, attackers, defenders)
            .WithVision(6, 1)
            .WithSkill(6, skills => skills with { Composure = 20, Strength = 20 });
        var decision = scene.Decide();

        decision.Action.Should().Be(TickCarrierAction.Shield);
        decision.Target.X.Should().BeLessThan(6_000, "turned back into the man, away from him");
        decision.PaceBasisPoints.Should().BeInRange(1, 5_000);

        var worn = new Scene(true, 6, attackers, defenders) { ShieldTicks = 40 }
            .WithVision(6, 1)
            .WithSkill(6, skills => skills with { Composure = 20, Strength = 20 })
            .Decide();

        worn.Action.Should().NotBe(TickCarrierAction.Shield, "he cannot hold the ball for ever");
    }

    [Fact]
    public void A_rattled_player_with_nothing_on_hoofs_it_clear_and_a_composed_one_does_not()
    {
        var attackers = Move(OwnHalf, (6, 3_000, 3_500), (0, 8_000, 500), (1, 8_000, 900), (2, 8_000, 1_500), (3, 8_000, 2_000), (4, 8_000, 2_500), (5, 8_000, 5_000), (7, 8_000, 5_500), (8, 8_000, 6_100), (9, 8_000, 6_500), (10, 8_000, 6_900));
        var defenders = Move(DeepBlock, (9, 3_150, 3_500), (6, 3_200, 3_200), (7, 3_200, 3_800));

        var rattled = new Scene(true, 6, attackers, defenders).WithVision(6, 1).WithSkill(6, skills => skills with { Composure = 1 }).Decide();
        var composed = new Scene(true, 6, attackers, defenders).WithVision(6, 1).WithSkill(6, skills => skills with { Composure = 20 }).Decide();

        rattled.Action.Should().Be(TickCarrierAction.Clear);
        rattled.Target.X.Should().BeGreaterThan(3_000, "upfield");
        composed.Action.Should().NotBe(TickCarrierAction.Clear);
    }

    [Fact]
    public void A_sprinting_forward_behind_the_line_is_played_in_ahead_of_himself_by_a_player_who_sees_it()
    {
        var attackers = Move(OwnHalf, (6, 5_000, 3_500), (9, 6_800, 3_000), (10, 5_500, 5_200));
        var orders = new TickAttackingOrder[11];

        orders[9] = new TickAttackingOrder(TickAttackingRole.Runner, new SpatialPoint(9_000, 3_000), 10_000);

        var sees = new Scene(true, 6, attackers, CompactFourFourTwo) { Orders = orders }
            .Moving(9, 0, 800)
            .WithVision(6, 16)
            .Decide();

        sees.Action.Should().Be(TickCarrierAction.ThroughBall);
        sees.Receiver.Should().Be(9);
        sees.Target.X.Should().BeGreaterThan(7_000, "ahead of where he is now (6,800)");
        sees.Target.X.Should().BeLessThan(SpatialPitch.PitchLength);

        var blind = new Scene(true, 6, attackers, CompactFourFourTwo) { Orders = orders }
            .Moving(9, 0, 800)
            .WithVision(6, 3)
            .Decide();

        blind.Action.Should().NotBe(TickCarrierAction.ThroughBall, "he does not see the run");
    }

    [Fact]
    public void A_reader_of_the_game_does_not_play_to_a_man_in_an_offside_position_and_a_careless_one_does()
    {
        var attackers = Move(OwnHalf, (6, 6_000, 3_500), (9, 8_600, 3_000), (5, 500, 6_900), (7, 500, 6_800), (8, 500, 6_700), (10, 500, 6_600));
        var defenders = HighLine;

        var scene = new Scene(true, 6, attackers, defenders).WithVision(6, 12);
        var aware = scene.Decide();
        var careless = new Scene(true, 6, attackers, defenders)
            .WithVision(6, 12)
            .WithSkill(6, skills => skills with { Decisions = 4, Anticipation = 4 })
            .Decide();

        aware.Receiver.Should().NotBe(9);
        careless.Receiver.Should().Be(9);
        scene.IsOffside(9).Should().BeTrue();
        scene.IsOffside(5).Should().BeFalse("he is behind the ball");
    }

    [Fact]
    public void The_pass_focus_steers_the_ball_to_the_lane_the_manager_asked_for()
    {
        // Two receivers equally far forward, one down the middle and one on the wing, both open; the carrier is a master passer.
        var attackers = Move(OwnHalf, (6, 5_000, 3_500), (9, 6_500, 3_700), (10, 6_500, 6_100), (5, 500, 500), (7, 500, 600), (8, 500, 700));

        int Receiver(MatchPassFocus focus) =>
            new Scene(true, 6, attackers, DeepBlock) { Focus = focus }
                .WithSkill(6, skills => skills with { Passing = 20, Technique = 20 })
                .WithVision(6, 20)
                .Decide()
                .Receiver;

        Receiver(MatchPassFocus.Centre).Should().Be(9);
        Receiver(MatchPassFocus.Wings).Should().Be(10);
    }

    [Fact]
    public void Direct_passing_values_a_long_forward_ball_more_than_short_passing_does()
    {
        var attackers = Move(OwnHalf, (6, 5_000, 3_500), (9, 7_600, 3_500), (5, 500, 500), (7, 500, 600), (8, 500, 700), (10, 500, 800));

        TickCarrierDecision Play(MatchPassingStyle style) =>
            new Scene(true, 6, attackers, DeepBlock) { Passing = style }
                .WithSkill(6, skills => skills with { Passing = 20, Technique = 20 })
                .WithVision(6, 20)
                .Decide();

        var direct = Play(MatchPassingStyle.DirectPassing);
        var shortStyle = Play(MatchPassingStyle.ShortPassing);

        direct.Action.Should().Be(TickCarrierAction.Pass);
        shortStyle.Action.Should().Be(TickCarrierAction.Pass);
        direct.Utility.Should().BeGreaterThan(shortStyle.Utility);
    }

    [Fact]
    public void A_pass_through_a_defender_scores_below_the_same_pass_down_a_clear_lane()
    {
        var attackers = Move(OwnHalf, (6, 5_000, 3_500), (9, 7_000, 3_500), (5, 500, 500), (7, 500, 600), (8, 500, 700), (10, 500, 800));
        var cut = Move(DeepBlock, (6, 6_000, 3_520));

        var open = new Scene(true, 6, attackers, DeepBlock).WithVision(6, 20).Decide();
        var blocked = new Scene(true, 6, attackers, cut).WithVision(6, 20).Decide();

        open.Action.Should().Be(TickCarrierAction.Pass);
        open.Receiver.Should().Be(9);
        blocked.Utility.Should().BeLessThan(open.Utility);
    }

    [Fact]
    public void The_away_side_decides_the_same_as_the_home_side_in_a_mirrored_scene()
    {
        var attackers = Move(OwnHalf, (6, 3_000, 3_500), (5, 8_000, 900), (7, 8_000, 4_400), (8, 8_000, 6_100), (9, 8_000, 2_800), (10, 8_000, 4_200));
        var home = new Scene(true, 6, attackers, DeepBlock).WithVision(6, 1).Decide();
        var away = new Scene(false, 6, attackers, DeepBlock).WithVision(6, 1).Decide();

        away.Action.Should().Be(home.Action);
        away.Receiver.Should().Be(home.Receiver);
        away.Utility.Should().Be(home.Utility);
        away.Target.Should().Be(new SpatialPoint(SpatialPitch.PitchLength - home.Target.X, SpatialPitch.PitchWidth - home.Target.Y));
    }

    [Fact]
    public void Executing_a_ground_pass_by_a_master_lands_it_on_the_receiver_and_a_poor_passer_scatters_it()
    {
        double MeanMiss(int skill, int pressure)
        {
            long total = 0;

            for (ulong seed = 1; seed <= 150; seed++)
            {
                var ball = new TickBallPhysics();

                ball.PlaceAt(5_000, 3_500);

                var decision = new TickCarrierDecision(TickCarrierAction.Pass, 9, new SpatialPoint(6_800, 3_500), 0, 0, pressure);

                TickBallCarrierBrain.Execute(decision, Skills(skill), ball, new Pcg32(seed)).Should().BeTrue();

                for (var tick = 0; tick < 300 && ball.UnitX < 6_800; tick++)
                {
                    ball.Step();
                }

                total += Math.Abs(ball.UnitY - 3_500);
            }

            return total / 150.0;
        }

        var master = MeanMiss(20, 0);
        var poor = MeanMiss(1, 0);
        var pressed = MeanMiss(10, 10_000);
        var calm = MeanMiss(10, 0);

        master.Should().BeLessThan(60, "he places the ball");
        poor.Should().BeGreaterThan(master * 3);
        pressed.Should().BeGreaterThan(calm * 1.5, "pressure doubles the spread");
    }

    [Fact]
    public void A_long_pass_is_lofted_and_a_short_one_is_rolled()
    {
        var shortBall = new TickBallPhysics();
        var longBall = new TickBallPhysics();

        shortBall.PlaceAt(5_000, 3_500);
        longBall.PlaceAt(5_000, 3_500);

        TickBallCarrierBrain.Execute(new TickCarrierDecision(TickCarrierAction.Pass, 9, new SpatialPoint(6_500, 3_500), 0, 0, 0), Skills(15), shortBall, new Pcg32(5));
        TickBallCarrierBrain.Execute(new TickCarrierDecision(TickCarrierAction.Pass, 9, new SpatialPoint(9_000, 3_500), 0, 0, 0), Skills(15), longBall, new Pcg32(5));

        shortBall.Mode.Should().Be(TickBallMode.Loose);
        longBall.Mode.Should().Be(TickBallMode.Flight);
    }

    [Fact]
    public void A_cross_and_a_clearance_leave_the_foot_in_the_air()
    {
        foreach (var action in new[] { TickCarrierAction.Cross, TickCarrierAction.Clear })
        {
            var ball = new TickBallPhysics();

            ball.PlaceAt(9_500, 6_300);

            TickBallCarrierBrain.Execute(new TickCarrierDecision(action, -1, new SpatialPoint(8_900, 3_500), 0, 0, 0), Skills(12), ball, new Pcg32(9))
                .Should().BeTrue();
            ball.Mode.Should().Be(TickBallMode.Flight);
        }
    }

    [Fact]
    public void A_dribble_and_a_shield_are_steered_by_the_caller_and_take_nothing_from_the_random_stream()
    {
        var used = new Pcg32(77);
        var fresh = new Pcg32(77);
        var ball = new TickBallPhysics();

        ball.PlaceAt(5_000, 3_500);

        TickBallCarrierBrain.Execute(new TickCarrierDecision(TickCarrierAction.Dribble, -1, new SpatialPoint(6_000, 3_500), 0, 8_000, 0), Skills(10), ball, used)
            .Should().BeFalse();
        TickBallCarrierBrain.Execute(new TickCarrierDecision(TickCarrierAction.Shield, -1, new SpatialPoint(5_000, 3_500), 0, 3_000, 0), Skills(10), ball, used)
            .Should().BeFalse();

        used.NextUInt32().Should().Be(fresh.NextUInt32());
    }

    [Fact]
    public void A_finisher_in_front_of_goal_scores_more_often_than_a_poor_one()
    {
        int Goals(int skill)
        {
            var goals = 0;

            for (ulong seed = 1; seed <= 100; seed++)
            {
                var ball = new TickBallPhysics();

                ball.PlaceAt(9_200, 3_500);

                var decision = new Scene(true, 9, Move(OwnHalf, (9, 9_200, 3_500)), DeepBlock)
                    .WithSkill(9, skills => skills with { Finishing = skill, Technique = skill })
                    .Decide();

                decision.Action.Should().Be(TickCarrierAction.Shoot);
                TickBallCarrierBrain.Execute(decision, Skills(skill), ball, new Pcg32(seed));

                for (var tick = 0; tick < 60; tick++)
                {
                    var boundary = ball.Step();

                    if (boundary == TickBallBoundary.GoalAwayEnd)
                    {
                        goals++;
                        break;
                    }

                    if (boundary != TickBallBoundary.InPlay)
                    {
                        break;
                    }
                }
            }

            return goals;
        }

        var master = Goals(20);
        var poor = Goals(1);

        // The scene shoots under a defender's pressure, so even a master's placement is only about 64% (Milestone 9):
        // the spread is drawn from the pressure-scaled chance, not a guaranteed on-target shot.
        master.Should().BeGreaterThan(50, "a master places most shots from eight metres on target");
        poor.Should().BeLessThan(master - 20, "a poor striker scatters them");
    }

    [Fact]
    public void A_swapped_listing_of_the_same_players_gives_the_same_decision_for_the_swapped_receiver()
    {
        var attackers = Move(OwnHalf, (6, 5_000, 3_500), (9, 7_000, 3_500), (5, 500, 500), (7, 500, 600), (8, 500, 700), (10, 500, 800));
        var original = new Scene(true, 6, attackers, DeepBlock).WithVision(6, 20).Decide();
        var swapped = Move(attackers, (9, attackers[10].X, attackers[10].Y), (10, attackers[9].X, attackers[9].Y));
        var other = new Scene(true, 6, swapped, DeepBlock).WithVision(6, 20).Decide();

        other.Action.Should().Be(original.Action);
        other.Receiver.Should().Be(original.Receiver == 9 ? 10 : original.Receiver);
        other.Target.Should().Be(original.Target);
    }

    [Fact]
    public void A_season_of_decisions_is_repeatable_and_allocates_nothing()
    {
        int Run(Scene[] scenes)
        {
            var hash = 17;

            for (var tick = 0; tick < 54_000; tick++)
            {
                var decision = scenes[tick % scenes.Length].Decide();

                hash = unchecked((hash * 31) + (int)decision.Action + (decision.Target.X * 7) + decision.Target.Y + decision.Receiver);
            }

            return hash;
        }

        Scene[] Scenes() =>
        [
            new(true, 9, Move(OwnHalf, (9, 8_900, 3_500)), DeepBlock),
            new(false, 6, Move(OwnHalf, (6, 3_000, 3_500)), Move(DeepBlock, (9, 3_150, 3_500))),
            new(true, 8, Move(OwnHalf, (8, 9_700, 6_300), (9, 9_000, 3_000)), DeepBlock),
            new(true, 6, Move(OwnHalf, (6, 5_000, 3_500), (9, 6_800, 3_000)), CompactFourFourTwo),
        ];

        var first = Run(Scenes());
        var scenes = Scenes();

        // A second, unmeasured run: tier-1 compilation of the decision code happens in the background, and its
        // allocations are charged to this thread, where they would be read as the brain's.
        Run(scenes);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var second = Run(scenes);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        second.Should().Be(first);
        allocated.Should().BeLessThan(256, "54,000 decisions must not allocate");
    }

    private static TickPlayerState Standing(int x, int y) => TickPlayerState.Standing(x, y, 0, 10_000);

    private static SpatialPoint[] Move(SpatialPoint[] layout, params (int Index, int X, int Y)[] moves)
    {
        var copy = (SpatialPoint[])layout.Clone();

        foreach (var (index, x, y) in moves)
        {
            copy[index] = new SpatialPoint(x, y);
        }

        return copy;
    }

    private static TickPlayerSkills Skills(int value) =>
        TickPlayerSkills.From(PlayerAttributesV1.From(Enumerable.Repeat(value, MatchAttributeNames.Count).ToArray()));

    private static TickPlayerSkills[] SkillsOfAll(int value) =>
        [.. Enumerable.Repeat(Skills(value), 11)];

    /// <summary>One side with the ball, the defenders it faces, and everything the brain reads.</summary>
    private sealed class Scene
    {
        private readonly bool _isHome;

        public Scene(bool isHome, int carrier, SpatialPoint[] attackersOwn, SpatialPoint[] defendersOwn)
        {
            _isHome = isHome;
            Carrier = carrier;
            Skills = SkillsOfAll(10);
            Attackers = [.. attackersOwn.Select(point => TickPlayerState.Standing(Mirror(point).X, Mirror(point).Y, isHome ? 0 : TickTrigonometry.HalfTurn, 10_000))];
            Defenders = [.. defendersOwn.Select(point => TickPlayerState.Standing(Mirror(point).X, Mirror(point).Y, isHome ? TickTrigonometry.HalfTurn : 0, 10_000))];
            Orders = [];
        }

        public MatchMentality Mentality { get; init; } = MatchMentality.Balanced;

        public MatchTempo Tempo { get; init; } = MatchTempo.Normal;

        public MatchPassingStyle Passing { get; init; } = MatchPassingStyle.MixedPassing;

        public MatchPassFocus Focus { get; init; } = MatchPassFocus.Balanced;

        public int ShieldTicks { get; init; }

        public TickAttackingOrder[] Orders { get; init; }

        public TickPlayerState[] Attackers { get; }

        public TickPlayerState[] Defenders { get; }

        public TickPlayerSkills[] Skills { get; }

        public int Carrier { get; }

        public Scene WithVision(int index, int vision) => WithSkill(index, skills => skills with { Vision = vision });

        public Scene WithSkill(int index, Func<TickPlayerSkills, TickPlayerSkills> change)
        {
            Skills[index] = change(Skills[index]);

            return this;
        }

        /// <summary>Sets a teammate running: heading in the side's own point of view, speed in cm/s.</summary>
        public Scene Moving(int index, int heading, int centimetresPerSecond)
        {
            var player = Attackers[index];

            player.Heading = TickTrigonometry.Normalize(_isHome ? heading : heading + TickTrigonometry.HalfTurn);
            player.Speed = TickSpatialUnits.SpeedToFixedPerTick(centimetresPerSecond);
            Attackers[index] = player;

            return this;
        }

        /// <summary>Sets a defender running: heading in the carrier's side's own point of view, speed in cm/s.</summary>
        public Scene DefenderMoving(int index, int heading, int centimetresPerSecond)
        {
            var player = Defenders[index];

            player.Heading = TickTrigonometry.Normalize(_isHome ? heading : heading + TickTrigonometry.HalfTurn);
            player.Speed = TickSpatialUnits.SpeedToFixedPerTick(centimetresPerSecond);
            Defenders[index] = player;

            return this;
        }

        public bool IsOffside(int receiver) =>
            TickBallCarrierBrain.IsReceiverOffside(Situation(), receiver);

        public TickCarrierDecision Decide() => TickBallCarrierBrain.Decide(Situation());

        private TickCarrierSituation Situation() =>
            new()
            {
                IsHome = _isHome,
                Mentality = Mentality,
                Tempo = Tempo,
                Passing = Passing,
                Focus = Focus,
                Attackers = Attackers,
                Specs = FourFourTwo,
                Skills = Skills,
                Defenders = Defenders,
                CarrierIndex = Carrier,
                Orders = Orders,
                ShieldTicks = ShieldTicks,
            };

        private SpatialPoint Mirror(SpatialPoint point) =>
            _isHome ? point : new SpatialPoint(SpatialPitch.PitchLength - point.X, SpatialPitch.PitchWidth - point.Y);
    }
}
