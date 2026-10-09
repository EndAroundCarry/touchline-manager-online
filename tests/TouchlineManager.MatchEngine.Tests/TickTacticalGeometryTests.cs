using FluentAssertions;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Spatial;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Verifies the tick engine's dynamic formation anchors and steering: the block moves as one, wingers spread and tuck,
/// and players glide onto their anchors without overshooting or stacking (Milestone 2).
/// </summary>
public sealed class TickTacticalGeometryTests
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

    [Fact]
    public void A_slot_becomes_an_anchor_on_the_pitch_by_rescaling_the_boards_coordinates()
    {
        var slot = new MatchSlotV1
        {
            SlotNumber = 2,
            Family = MatchPositionFamily.Defence,
            Role = MatchRole.FullBack,
            X = 2_200,
            Y = 1_500,
            ParticipantId = Guid.NewGuid(),
        };

        var spec = TickAnchorSpec.From(slot);

        spec.OwnX.Should().Be(2_200);
        spec.OwnY.Should().Be(1_050, "the board's width is 10,000 but the pitch's is 7,000");
        spec.Family.Should().Be(MatchPositionFamily.Defence);
    }

    [Fact]
    public void The_away_side_is_the_home_side_mirrored_on_both_axes()
    {
        var style = TickTeamStyle.From(new MatchInstructionsV1());

        foreach (var spec in FourFourTwo)
        {
            var home = TickTacticalGeometry.Resolve(spec, style, isHome: true, hasPossession: true, 3_000, 2_000);
            var away = TickTacticalGeometry.Resolve(spec, style, isHome: false, hasPossession: true, 7_000, 5_000);

            away.X.Should().Be(SpatialPitch.PitchLength - home.X);
            away.Y.Should().Be(SpatialPitch.PitchWidth - home.Y);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void When_the_ball_goes_from_one_penalty_box_to_the_other_all_ten_outfield_players_advance_as_one_block(bool isHome)
    {
        var style = TickTeamStyle.From(new MatchInstructionsV1());
        var ownBox = isHome ? 800 : 9_200;
        var theirBox = isHome ? 9_200 : 800;
        var back = new SpatialPoint[FourFourTwo.Length];
        var front = new SpatialPoint[FourFourTwo.Length];

        TickTacticalGeometry.ResolveTeam(FourFourTwo, style, isHome, hasPossession: true, ownBox, 3_500, back);
        TickTacticalGeometry.ResolveTeam(FourFourTwo, style, isHome, hasPossession: true, theirBox, 3_500, front);

        var direction = isHome ? 1 : -1;

        for (var index = 1; index < FourFourTwo.Length; index++)
        {
            ((front[index].X - back[index].X) * direction).Should().BeGreaterThan(1_500, $"player {index} follows the ball up");
        }

        // The block keeps its depth order: defenders behind midfielders behind attackers, at both ends.
        foreach (var anchors in new[] { back, front })
        {
            Depth(anchors, 1, 4, isHome).Max().Should().BeLessThan(Depth(anchors, 5, 8, isHome).Min());
            Depth(anchors, 5, 8, isHome).Max().Should().BeLessThan(Depth(anchors, 9, 10, isHome).Min());
        }

        // The block does not stretch or collapse: its length stays within a quarter of itself.
        var backLength = Depth(back, 1, 10, isHome).Max() - Depth(back, 1, 10, isHome).Min();
        var frontLength = Depth(front, 1, 10, isHome).Max() - Depth(front, 1, 10, isHome).Min();

        frontLength.Should().BeInRange(backLength * 3 / 4, backLength * 5 / 4);
    }

    [Fact]
    public void Wingers_spread_wide_in_attack_and_tuck_in_in_defence_while_central_players_hold_the_middle()
    {
        var style = TickTeamStyle.From(new MatchInstructionsV1());
        var winger = FourFourTwo[5];
        var centre = FourFourTwo[6];

        var attacking = TickTacticalGeometry.Resolve(winger, style, true, hasPossession: true, 5_000, 3_500);
        var defending = TickTacticalGeometry.Resolve(winger, style, true, hasPossession: false, 5_000, 3_500);
        var centreAttacking = TickTacticalGeometry.Resolve(centre, style, true, hasPossession: true, 5_000, 3_500);
        var centreDefending = TickTacticalGeometry.Resolve(centre, style, true, hasPossession: false, 5_000, 3_500);

        var attackingReach = Math.Abs(attacking.Y - SpatialPitch.GoalYCenter);
        var defendingReach = Math.Abs(defending.Y - SpatialPitch.GoalYCenter);

        attackingReach.Should().BeGreaterThan(Math.Abs(winger.OwnY - SpatialPitch.GoalYCenter), "he stretches the pitch with the ball");
        defendingReach.Should().BeLessThan(Math.Abs(winger.OwnY - SpatialPitch.GoalYCenter), "he tucks in without it");
        (attackingReach - defendingReach).Should().BeGreaterThan(1_000);
        Math.Abs(centreAttacking.Y - centreDefending.Y).Should().BeLessThan(attackingReach - defendingReach);
    }

    [Fact]
    public void The_whole_block_slides_towards_the_ball_across_the_pitch_and_harder_without_it()
    {
        var style = TickTeamStyle.From(new MatchInstructionsV1());
        var spec = FourFourTwo[7];

        var centreBall = TickTacticalGeometry.Resolve(spec, style, true, hasPossession: false, 5_000, 3_500);
        var nearBall = TickTacticalGeometry.Resolve(spec, style, true, hasPossession: false, 5_000, 500);

        (centreBall.Y - nearBall.Y).Should().BeGreaterThan(300, "the block shifts to the ball's side");
    }

    [Fact]
    public void Mentality_pushes_the_block_up_with_the_ball_and_a_defensive_one_drops_it_further_without()
    {
        var attacking = TickTeamStyle.From(new MatchInstructionsV1 { Mentality = MatchMentality.Attacking });
        var defensive = TickTeamStyle.From(new MatchInstructionsV1 { Mentality = MatchMentality.Defensive });
        var spec = FourFourTwo[6];

        X(spec, attacking, true).Should().BeGreaterThan(X(spec, defensive, true));
        X(spec, attacking, false).Should().BeGreaterThan(X(spec, defensive, false));
        X(spec, defensive, true).Should().BeGreaterThan(X(spec, defensive, false), "possession always pushes up");
        attacking.PossessionDepthShift.Should().BeInRange(400, 800);
        defensive.OutOfPossessionDepthShift.Should().BeInRange(-600, -300);
    }

    [Fact]
    public void The_defensive_line_instruction_moves_defenders_fully_midfielders_partly_and_attackers_little()
    {
        var high = TickTeamStyle.From(new MatchInstructionsV1 { DefensiveLine = MatchDefensiveLine.High });
        var normal = TickTeamStyle.From(new MatchInstructionsV1());
        var deep = TickTeamStyle.From(new MatchInstructionsV1 { DefensiveLine = MatchDefensiveLine.Deep });

        var defender = FourFourTwo[2];
        var midfielder = FourFourTwo[6];
        var attacker = FourFourTwo[9];

        (X(defender, high, true) - X(defender, normal, true)).Should().Be(600);
        (X(defender, deep, true) - X(defender, normal, true)).Should().Be(-700);
        (X(midfielder, high, true) - X(midfielder, normal, true)).Should().Be(360);
        (X(attacker, high, true) - X(attacker, normal, true)).Should().Be(180);
    }

    [Fact]
    public void Width_and_pass_focus_set_how_far_the_block_spreads_and_which_flank_it_leans_to()
    {
        var winger = FourFourTwo[8];
        var narrow = TickTeamStyle.From(new MatchInstructionsV1 { Width = MatchWidth.Narrow });
        var wide = TickTeamStyle.From(new MatchInstructionsV1 { Width = MatchWidth.Wide });
        var leftLean = TickTeamStyle.From(new MatchInstructionsV1 { PassFocus = MatchPassFocus.CentreAndLeft });
        var rightLean = TickTeamStyle.From(new MatchInstructionsV1 { PassFocus = MatchPassFocus.CentreAndRight });

        Y(winger, wide, true).Should().BeGreaterThan(Y(winger, narrow, true));
        Y(winger, wide, false).Should().BeGreaterThan(Y(winger, narrow, false));
        Y(FourFourTwo[6], rightLean, true).Should().BeGreaterThan(Y(FourFourTwo[6], leftLean, true), "leaning right is towards high Y");
        Y(FourFourTwo[6], rightLean, false).Should().Be(Y(FourFourTwo[6], leftLean, false), "focus only applies in possession");
    }

    [Fact]
    public void The_goalkeeper_follows_the_ball_a_little_and_never_leaves_his_area()
    {
        var style = TickTeamStyle.From(new MatchInstructionsV1 { Mentality = MatchMentality.Attacking, DefensiveLine = MatchDefensiveLine.High });
        var keeper = FourFourTwo[0];

        foreach (var ballX in new[] { 0, 2_500, 5_000, 7_500, 10_000 })
        {
            var anchor = TickTacticalGeometry.Resolve(keeper, style, true, hasPossession: true, ballX, 3_500);

            anchor.X.Should().BeInRange(TickTacticalGeometry.Margin, TickTacticalGeometry.GoalkeeperLimit);
        }

        var deep = TickTacticalGeometry.Resolve(keeper, style, true, true, 0, 3_500);
        var high = TickTacticalGeometry.Resolve(keeper, style, true, true, 10_000, 3_500);

        (high.X - deep.X).Should().BeInRange(200, 400);
    }

    [Fact]
    public void Every_anchor_stays_inside_the_pitch_whatever_the_ball_and_the_instructions()
    {
        var style = TickTeamStyle.From(new MatchInstructionsV1
        {
            Mentality = MatchMentality.Attacking,
            Width = MatchWidth.Wide,
            DefensiveLine = MatchDefensiveLine.High,
            PassFocus = MatchPassFocus.Wings,
        });

        foreach (var spec in FourFourTwo)
        {
            foreach (var isHome in new[] { true, false })
            {
                foreach (var ballX in new[] { 0, 5_000, 10_000 })
                {
                    foreach (var ballY in new[] { 0, 3_500, 7_000 })
                    {
                        var anchor = TickTacticalGeometry.Resolve(spec, style, isHome, hasPossession: ballX > 5_000, ballX, ballY);

                        anchor.X.Should().BeInRange(TickTacticalGeometry.Margin, SpatialPitch.PitchLength - TickTacticalGeometry.Margin);
                        anchor.Y.Should().BeInRange(TickTacticalGeometry.Margin, SpatialPitch.PitchWidth - TickTacticalGeometry.Margin);
                    }
                }
            }
        }
    }

    [Fact]
    public void A_side_steered_to_its_anchors_arrives_without_overshooting_and_holds_still()
    {
        var style = TickTeamStyle.From(new MatchInstructionsV1());
        var anchors = new SpatialPoint[FourFourTwo.Length];
        var team = Kickoff(FourFourTwo, isHome: true);
        var profiles = Profiles();

        TickTacticalGeometry.ResolveTeam(FourFourTwo, style, true, hasPossession: true, 7_000, 3_500, anchors);

        var previous = Distances(team, anchors);

        for (var tick = 0; tick < 600; tick++)
        {
            TickSteering.StepTeam(team, profiles, anchors);
        }

        for (var index = 0; index < team.Length; index++)
        {
            var left = Distance(team[index], anchors[index]);

            left.Should().BeLessThan(TickSpatialUnits.ToFixed(250), $"player {index} settles within 2.5 m of his anchor");
            team[index].Speed.Should().BeLessThan(TickSpatialUnits.SpeedToFixedPerTick(100), $"player {index} stands still");
            left.Should().BeLessThanOrEqualTo(previous[index]);
        }
    }

    [Fact]
    public void Two_players_given_the_same_anchor_do_not_stack_and_a_lone_player_is_not_disturbed()
    {
        var profiles = Profiles();
        var team = new[]
        {
            TickPlayerState.Standing(3_000, 3_000, 0, 10_000),
            TickPlayerState.Standing(3_000, 4_000, 0, 10_000),
        };
        var anchors = new[] { new SpatialPoint(5_000, 3_500), new SpatialPoint(5_000, 3_500) };

        for (var tick = 0; tick < 400; tick++)
        {
            TickSteering.StepTeam(team, profiles, anchors);
        }

        Distance(team[0], team[1]).Should().BeGreaterThan(TickSteering.SeparationRadius * 6 / 10, "they keep apart");
        Distance(team[0], team[1]).Should().BeLessThan(TickSteering.SeparationRadius * 2, "but stay together at the anchor");

        var lone = new[] { TickPlayerState.Standing(3_000, 3_000, 0, 10_000) };

        for (var tick = 0; tick < 400; tick++)
        {
            TickSteering.StepTeam(lone, profiles, [new SpatialPoint(5_000, 3_500)]);
        }

        Distance(lone[0], new SpatialPoint(5_000, 3_500)).Should().BeLessThan(TickSpatialUnits.ToFixed(120), "he is content within a metre");
    }

    [Fact]
    public void A_player_standing_exactly_on_a_teammate_is_pushed_apart_deterministically()
    {
        var profiles = Profiles();
        var anchors = new[] { new SpatialPoint(5_000, 3_500), new SpatialPoint(5_000, 3_500) };

        static TickPlayerState[] Stacked() =>
        [
            TickPlayerState.Standing(5_000, 3_500, 0, 10_000),
            TickPlayerState.Standing(5_000, 3_500, 0, 10_000),
        ];

        var first = Stacked();
        var second = Stacked();

        for (var tick = 0; tick < 50; tick++)
        {
            TickSteering.StepTeam(first, profiles, anchors);
            TickSteering.StepTeam(second, profiles, anchors);
        }

        Distance(first[0], first[1]).Should().BeGreaterThan(0);
        first.Should().Equal(second);
    }

    [Fact]
    public void A_change_of_anchor_bends_the_path_rather_than_snapping_it()
    {
        var profile = Profiles()[0];
        var team = new[] { TickPlayerState.Standing(3_000, 3_500, 0, 10_000) };

        for (var tick = 0; tick < 40; tick++)
        {
            TickSteering.StepTeam(team, [profile], [new SpatialPoint(9_000, 3_500)]);
        }

        var cruising = team[0].Speed;

        TickSteering.StepTeam(team, [profile], [new SpatialPoint(1_000, 3_500)]);

        team[0].Speed.Should().BeGreaterThan(0, "he cannot reverse in one tick");
        team[0].Speed.Should().BeGreaterThan(cruising / 2);
        TickTrigonometry.Difference(0, team[0].Heading).Should().BeInRange(-120, 120, "the turn rate limits the swing");
    }

    [Fact]
    public void The_order_of_the_players_does_not_change_where_they_end_up()
    {
        var style = TickTeamStyle.From(new MatchInstructionsV1());
        var anchors = new SpatialPoint[FourFourTwo.Length];
        var forward = Kickoff(FourFourTwo, isHome: true);
        var profiles = Profiles();

        TickTacticalGeometry.ResolveTeam(FourFourTwo, style, true, hasPossession: false, 2_000, 1_000, anchors);

        var reversedSpecs = FourFourTwo.Reverse().ToArray();
        var reversed = Kickoff(reversedSpecs, isHome: true);
        var reversedAnchors = anchors.Reverse().ToArray();
        var reversedProfiles = profiles.Reverse().ToArray();

        for (var tick = 0; tick < 100; tick++)
        {
            TickSteering.StepTeam(forward, profiles, anchors);
            TickSteering.StepTeam(reversed, reversedProfiles, reversedAnchors);
        }

        for (var index = 0; index < forward.Length; index++)
        {
            var mirror = reversed[forward.Length - 1 - index];

            forward[index].X.Should().BeInRange(mirror.X - 2_000, mirror.X + 2_000);
            forward[index].Y.Should().BeInRange(mirror.Y - 2_000, mirror.Y + 2_000);
        }
    }

    [Fact]
    public void A_whole_match_of_anchors_and_steering_is_deterministic_and_allocates_nothing()
    {
        var home = TickTeamStyle.From(new MatchInstructionsV1 { Mentality = MatchMentality.Positive });
        var away = TickTeamStyle.From(new MatchInstructionsV1 { Pressing = MatchPressing.HighPress });
        var profiles = Profiles();
        var homeAnchors = new SpatialPoint[FourFourTwo.Length];
        var awayAnchors = new SpatialPoint[FourFourTwo.Length];

        int Run(TickPlayerState[] homeTeam, TickPlayerState[] awayTeam)
        {
            var hash = 17;

            for (var tick = 0; tick < 54_000; tick++)
            {
                // A ball that sweeps up and down and across the pitch, and possession that changes every 20 s.
                var ballX = 5_000 + (int)(4_500L * TickTrigonometry.Sin(tick * 3) / TickTrigonometry.Scale);
                var ballY = 3_500 + (int)(3_000L * TickTrigonometry.Cos(tick * 5) / TickTrigonometry.Scale);
                var homeHasBall = (tick / 200) % 2 == 0;

                TickTacticalGeometry.ResolveTeam(FourFourTwo, home, true, homeHasBall, ballX, ballY, homeAnchors);
                TickTacticalGeometry.ResolveTeam(FourFourTwo, away, false, !homeHasBall, ballX, ballY, awayAnchors);
                TickSteering.StepTeam(homeTeam, profiles, homeAnchors);
                TickSteering.StepTeam(awayTeam, profiles, awayAnchors);

                hash = unchecked((hash * 31) + homeTeam[tick % 11].X);
                hash = unchecked((hash * 31) + awayTeam[tick % 11].Y);
            }

            return hash;
        }

        var first = Run(Kickoff(FourFourTwo, true), Kickoff(FourFourTwo, false));
        var homeTeam = Kickoff(FourFourTwo, true);
        var awayTeam = Kickoff(FourFourTwo, false);

        // A second, unmeasured run: tier-1 compilation of the steering happens in the background, and its allocations
        // are charged to this thread, where they would be read as the geometry's.
        Run(Kickoff(FourFourTwo, true), Kickoff(FourFourTwo, false));

        var before = GC.GetAllocatedBytesForCurrentThread();
        var second = Run(homeTeam, awayTeam);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        second.Should().Be(first);
        allocated.Should().BeLessThan(256, "two sides for 54,000 ticks must not allocate");
    }

    private static int X(TickAnchorSpec spec, TickTeamStyle style, bool hasPossession) =>
        TickTacticalGeometry.Resolve(spec, style, true, hasPossession, 5_000, 3_500).X;

    private static int Y(TickAnchorSpec spec, TickTeamStyle style, bool hasPossession) =>
        TickTacticalGeometry.Resolve(spec, style, true, hasPossession, 5_000, 3_500).Y;

    /// <summary>Gets how far up the pitch each of a range of anchors is, from the side's own point of view.</summary>
    private static int[] Depth(SpatialPoint[] anchors, int first, int last, bool isHome) =>
        [.. anchors.Skip(first).Take(last - first + 1).Select(anchor => isHome ? anchor.X : SpatialPitch.PitchLength - anchor.X)];

    private static TickPlayerState[] Kickoff(TickAnchorSpec[] specs, bool isHome) =>
        [
            .. specs.Select(spec => TickPlayerState.Standing(
                isHome ? spec.OwnX : SpatialPitch.PitchLength - spec.OwnX,
                isHome ? spec.OwnY : SpatialPitch.PitchWidth - spec.OwnY,
                isHome ? 0 : TickTrigonometry.HalfTurn,
                10_000)),
        ];

    private static TickPlayerProfile[] Profiles()
    {
        var values = Enumerable.Repeat(10, MatchAttributeNames.Count).ToArray();

        return [.. Enumerable.Repeat(TickPlayerProfile.From(PlayerAttributesV1.From(values)), TickTacticalGeometry.TeamSize)];
    }

    private static long Distance(TickPlayerState player, SpatialPoint anchor)
    {
        var dx = (long)player.X - TickSpatialUnits.ToFixed(anchor.X);
        var dy = (long)player.Y - TickSpatialUnits.ToFixed(anchor.Y);

        return SpatialMath.Sqrt((dx * dx) + (dy * dy));
    }

    private static long Distance(TickPlayerState one, TickPlayerState other)
    {
        var dx = (long)one.X - other.X;
        var dy = (long)one.Y - other.Y;

        return SpatialMath.Sqrt((dx * dx) + (dy * dy));
    }

    private static long[] Distances(TickPlayerState[] team, SpatialPoint[] anchors) =>
        [.. team.Select((player, index) => Distance(player, anchors[index]))];
}
