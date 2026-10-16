using FluentAssertions;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Spatial;
using Tactical11Manager.MatchEngine.Tick;

namespace Tactical11Manager.MatchEngine.Tests;

/// <summary>
/// Verifies combination play (`tick-film-v1`, Milestone 7): who runs for the ball back after a short pass to a pressed team-mate, who
/// runs past a pressed forward with his back to goal, and what the off-ball AI does with the orders.
/// </summary>
/// <remarks>Every scene is in the attacking side's own point of view (it attacks towards X = 10,000) and mirrored for the away side.</remarks>
public sealed class TickCombinationsTests
{
    /// <summary>A 4-4-2, goalkeeper first: seats 1-4 defence, 5-8 midfield, 9-10 attack.</summary>
    private static readonly TickAnchorSpec[] FourFourTwo =
    [
        new(500, 3_500, MatchPositionFamily.Goalkeeper),
        new(2_200, 900, MatchPositionFamily.Defence),
        new(2_000, 2_500, MatchPositionFamily.Defence),
        new(2_000, 4_500, MatchPositionFamily.Defence),
        new(2_200, 6_100, MatchPositionFamily.Defence),
        new(5_000, 900, MatchPositionFamily.Midfield, MatchRole.WideMidfielder),
        new(4_800, 2_600, MatchPositionFamily.Midfield),
        new(4_800, 4_400, MatchPositionFamily.Midfield),
        new(5_000, 6_100, MatchPositionFamily.Midfield, MatchRole.WideMidfielder),
        new(7_500, 2_600, MatchPositionFamily.Attack),
        new(7_500, 4_400, MatchPositionFamily.Attack),
    ];

    /// <summary>The side behind the ball: nobody anywhere near the scenes below.</summary>
    private static readonly SpatialPoint[] Back =
    [
        new(300, 3_500),
        new(2_000, 900),
        new(1_800, 2_500),
        new(1_800, 4_500),
        new(2_000, 6_100),
        new(2_500, 900),
        new(4_800, 2_600),
        new(4_500, 4_400),
        new(2_500, 6_100),
        new(5_000, 2_000),
        new(5_000, 5_000),
    ];

    /// <summary>The opposition far away, goalkeeper on his line.</summary>
    private static readonly SpatialPoint[] Away =
    [
        new(9_800, 3_500),
        new(9_000, 900),
        new(9_000, 2_500),
        new(9_000, 4_500),
        new(9_000, 6_100),
        new(8_500, 900),
        new(8_500, 2_600),
        new(8_500, 4_400),
        new(8_500, 6_100),
        new(8_200, 2_600),
        new(8_200, 4_400),
    ];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_short_pass_to_a_team_mate_with_a_marker_on_him_sends_the_passer_past_the_marker(bool isHome)
    {
        // The midfielder (seat 6) plays to the forward (seat 9), who has a defender (seat 9) 3 m goal-side of him; the passer is on his left.
        var attackers = Bodies(isHome, Move(Back, (6, 5_300, 3_700), (9, 6_000, 3_000)));
        var defenders = Bodies(isHome, Move(Away, (9, 6_300, 3_000)), toward: false);

        TickCombinations.TryReturnRun(isHome, attackers, FourFourTwo, defenders, 6, 9, out var target).Should().BeTrue();

        var own = Own(isHome, target);

        own.X.Should().Be(6_300 + TickCombinations.ReturnAhead, "beyond the man who is on him");
        own.Y.Should().Be(3_000 + TickCombinations.ReturnSide, "round the side the passer is on");
    }

    [Fact]
    public void There_is_no_return_run_for_a_free_receiver_a_long_pass_a_defender_passer_or_a_run_that_goes_nowhere()
    {
        var pressed = Move(Away, (9, 6_300, 3_000));

        Run(Move(Back, (6, 5_300, 3_700), (9, 6_000, 3_000)), pressed).Should().BeTrue("the control: the scene above");
        Run(Move(Back, (6, 5_300, 3_700), (9, 6_000, 3_000)), Move(Away, (9, 6_900, 3_000))).Should().BeFalse("a marker 9 m off is no pressure");
        Run(Move(Back, (6, 3_000, 3_700), (9, 6_000, 3_000)), pressed).Should().BeFalse("30 m is not a short pass");
        Run(Move(Back, (6, 5_900, 3_700), (9, 6_000, 3_000)), pressed).Should().BeTrue("a short ball is");
        Run(Move(Back, (2, 5_000, 3_700), (9, 6_000, 3_000)), pressed, passer: 2).Should().BeFalse("a defender stays at home");
        Run(Move(Back, (6, 8_300, 3_700), (9, 7_500, 3_000)), Move(Away, (9, 7_700, 3_000)))
            .Should().BeFalse("the passer is already as far up the pitch as the run would take him");

        static bool Run(SpatialPoint[] own, SpatialPoint[] opponents, int passer = 6) =>
            TickCombinations.TryReturnRun(true, Bodies(true, own), FourFourTwo, Bodies(true, opponents, toward: false), passer, 9, out _);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_pass_forward_into_a_pressed_forward_with_his_back_to_goal_sends_the_nearest_midfielder_past_him(bool isHome)
    {
        // Seat 6 plays to seat 10, who has a defender at his shoulder on the goal side; seat 7 is the nearest midfielder.
        var attackers = Bodies(isHome, Move(Back, (6, 4_500, 3_500), (7, 5_500, 3_000), (10, 6_500, 3_800)));
        var defenders = Bodies(isHome, Move(Away, (9, 6_800, 3_900)), toward: false);

        TickCombinations.TryThirdMan(isHome, attackers, FourFourTwo, defenders, 6, 10, out var seat, out var target).Should().BeTrue();

        seat.Should().Be(7);
        Own(isHome, target).X.Should().Be(6_500 + TickCombinations.ThirdManAhead);
        Own(isHome, target).Y.Should().Be(3_800 - TickCombinations.ThirdManSide, "to the side away from his marker");
    }

    [Fact]
    public void There_is_no_third_man_when_the_forward_is_free_faces_goal_is_not_a_forward_or_nobody_can_come()
    {
        var own = Move(Back, (6, 4_500, 3_500), (7, 5_500, 3_000), (10, 6_500, 3_800));
        var marked = Move(Away, (9, 6_800, 3_900));

        Third(own, marked).Should().BeTrue("the control");
        Third(own, Move(Away, (9, 7_800, 3_900))).Should().BeFalse("a marker 13 m off does not press him");
        Third(own, Move(Away, (9, 6_300, 3_900))).Should().BeFalse("a marker behind him: he faces goal");
        Third(Move(own, (6, 6_400, 3_500)), marked).Should().BeFalse("a flat ball is not a pass into his feet from behind");
        Third(Move(own, (7, 3_000, 6_500), (5, 500, 500), (8, 500, 600)), marked).Should().BeFalse("no midfielder within 30 m");
        Third(own, marked, receiver: 9).Should().BeFalse("seat 9 is not where the ball went");

        static bool Third(SpatialPoint[] attackers, SpatialPoint[] opponents, int receiver = 10) =>
            TickCombinations.TryThirdMan(true, Bodies(true, attackers), FourFourTwo, Bodies(true, opponents, toward: false), 6, receiver, out _, out _);
    }

    [Fact]
    public void The_support_ai_gives_the_returning_passer_and_the_third_man_their_orders_in_flight_and_when_the_ball_is_held()
    {
        var attackers = Bodies(true, Move(Back, (6, 5_300, 3_700), (7, 5_500, 3_000), (9, 6_000, 3_000), (10, 6_500, 3_800)));
        var defenders = Bodies(true, Away, toward: false);
        var back = new SpatialPoint(7_000, 3_400);
        var ahead = new SpatialPoint(7_500, 3_400);

        // The ball is in the air: nobody has it, and the two still run.
        var inFlight = Assign(attackers, defenders, carrier: -1, returnSeat: 6, back, thirdSeat: 7, ahead);

        inFlight[6].Role.Should().Be(TickAttackingRole.Return);
        inFlight[6].PaceBasisPoints.Should().Be(TickOffBallSupport.ReturnPaceBasisPoints);
        inFlight[6].Target.Should().Be(back);
        inFlight[7].Role.Should().Be(TickAttackingRole.ThirdMan);
        inFlight[7].Target.Should().Be(ahead);
        inFlight.Where((order, index) => index is not (6 or 7)).Should().OnlyContain(order => order.Role != TickAttackingRole.Return && order.Role != TickAttackingRole.ThirdMan);

        // The forward has it: the triangle's corners come from the others, and the two keep their runs.
        var held = Assign(attackers, defenders, carrier: 10, returnSeat: 6, back, thirdSeat: 7, ahead);

        held[10].Role.Should().Be(TickAttackingRole.Carrier);
        held[6].Role.Should().Be(TickAttackingRole.Return);
        held[7].Role.Should().Be(TickAttackingRole.ThirdMan);

        // He gets the ball back: he is the carrier, not a runner.
        var back6 = Assign(attackers, defenders, carrier: 6, returnSeat: 6, back, thirdSeat: 0, default);

        back6[6].Role.Should().Be(TickAttackingRole.Carrier);

        // And the goalkeeper never runs.
        Assign(attackers, defenders, carrier: -1, returnSeat: 0, back, thirdSeat: 0, ahead)
            .Should().OnlyContain(order => order.Role != TickAttackingRole.Return && order.Role != TickAttackingRole.ThirdMan);
    }

    [Fact]
    public void A_run_is_kept_onside_of_the_line_as_it_stands()
    {
        var attackers = Bodies(true, Move(Back, (6, 5_300, 3_700)));
        var defenders = Bodies(true, Move(Away, (1, 7_000, 900), (2, 7_000, 2_500), (3, 7_000, 4_500), (4, 7_000, 6_100), (5, 7_000, 900), (6, 7_000, 2_600), (7, 7_000, 4_400), (8, 7_000, 6_100), (9, 7_000, 2_600), (10, 7_000, 4_400)), toward: false);
        var run = Assign(attackers, defenders, carrier: -1, returnSeat: 6, new SpatialPoint(9_000, 3_400), thirdSeat: 0, default);

        run[6].Target.X.Should().BeLessThanOrEqualTo(7_000 - TickOffBallSupport.OnsideMargin, "no farther than the last defender, less the margin");
        run[6].Target.Y.Should().Be(3_400);
    }

    [Fact]
    public void A_whole_run_of_combination_checks_allocates_nothing()
    {
        var attackers = Bodies(true, Move(Back, (6, 5_300, 3_700), (7, 5_500, 3_000), (9, 6_000, 3_000), (10, 6_500, 3_800)));
        var defenders = Bodies(true, Move(Away, (9, 6_300, 3_000)), toward: false);
        var total = 0;

        TickCombinations.TryReturnRun(true, attackers, FourFourTwo, defenders, 6, 9, out _);

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var round = 0; round < 20_000; round++)
        {
            total += TickCombinations.TryReturnRun(true, attackers, FourFourTwo, defenders, 6, 9, out _) ? 1 : 0;
            total += TickCombinations.TryThirdMan(true, attackers, FourFourTwo, defenders, 6, 10, out _, out _) ? 1 : 0;
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().BeLessThan(256);
        total.Should().BeGreaterThan(0);
    }

    private static TickAttackingOrder[] Assign(
        TickPlayerState[] attackers,
        TickPlayerState[] defenders,
        int carrier,
        int returnSeat,
        SpatialPoint returnTarget,
        int thirdSeat,
        SpatialPoint thirdTarget)
    {
        var style = TickTeamStyle.From(new MatchInstructionsV1());
        var anchors = new SpatialPoint[FourFourTwo.Length];
        var skills = Enumerable.Repeat(
            TickPlayerSkills.From(PlayerAttributesV1.From(Enumerable.Repeat(10, MatchAttributeNames.Count).ToArray())),
            FourFourTwo.Length).ToArray();
        var orders = new TickAttackingOrder[FourFourTwo.Length];

        TickTacticalGeometry.ResolveTeam(FourFourTwo, style, true, hasPossession: true, 5_000, 3_500, anchors);
        TickOffBallSupport.Assign(
            new TickAttackingSituation
            {
                IsHome = true,
                Mentality = MatchMentality.Balanced,
                Passing = MatchPassingStyle.MixedPassing,
                Attackers = attackers,
                Specs = FourFourTwo,
                Anchors = anchors,
                Skills = skills,
                Defenders = defenders,
                CarrierIndex = carrier,
                ReturnSeat = returnSeat,
                ReturnTarget = returnTarget,
                ThirdSeat = thirdSeat,
                ThirdTarget = thirdTarget,
            },
            orders);

        return orders;
    }

    private static SpatialPoint[] Move(SpatialPoint[] layout, params (int Index, int X, int Y)[] moves)
    {
        var copy = (SpatialPoint[])layout.Clone();

        foreach (var (index, x, y) in moves)
        {
            copy[index] = new SpatialPoint(x, y);
        }

        return copy;
    }

    /// <summary>Turns a layout given in the side's own point of view into bodies on the pitch, facing the way the side attacks (or, for the opposition, the way it does not).</summary>
    private static TickPlayerState[] Bodies(bool isHome, SpatialPoint[] own, bool toward = true) =>
        [.. own.Select(point =>
        {
            var at = isHome ? point : new SpatialPoint(SpatialPitch.PitchLength - point.X, SpatialPitch.PitchWidth - point.Y);
            var facing = toward == isHome ? 0 : TickTrigonometry.HalfTurn;

            return TickPlayerState.Standing(at.X, at.Y, facing, 10_000);
        })];

    /// <summary>Turns a point on the pitch back into the side's own point of view (the conversion is its own inverse).</summary>
    private static SpatialPoint Own(bool isHome, SpatialPoint point) =>
        isHome ? point : new SpatialPoint(SpatialPitch.PitchLength - point.X, SpatialPitch.PitchWidth - point.Y);
}
