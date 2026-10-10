using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>
/// One slot's formation anchor in its own side's point of view, in pitch units.
/// </summary>
/// <remarks>
/// Own point of view means X runs 0 (own goal) to 10,000 (the goal the side attacks) and Y runs 0 (the side's left
/// hand) to 7,000 (its right), whichever end the side really defends. The tactics board stores the same orientation, so
/// the anchor is the board's coordinate rescaled onto the pitch and nothing more.
/// </remarks>
/// <param name="OwnX">The base X, towards the goal the side attacks.</param>
/// <param name="OwnY">The base Y, across the pitch from the side's left.</param>
/// <param name="Family">The position family, which sets how much of each shift the player takes.</param>
internal readonly record struct TickAnchorSpec(int OwnX, int OwnY, MatchPositionFamily Family)
{
    /// <summary>Builds the anchor for a team-sheet slot.</summary>
    /// <param name="slot">The slot, with the tactics board's 0..10,000 coordinates.</param>
    public static TickAnchorSpec From(MatchSlotV1 slot)
    {
        ArgumentNullException.ThrowIfNull(slot);

        return new TickAnchorSpec(
            slot.X * SpatialPitch.PitchLength / EngineRulesV2.SlotCoordinateScale,
            slot.Y * SpatialPitch.PitchWidth / EngineRulesV2.SlotCoordinateScale,
            slot.Family);
    }
}

/// <summary>
/// What a side's instructions do to the shape of its block, worked out once per side per match.
/// </summary>
/// <remarks>
/// Every number is an integer in pitch units or basis points, so the tick loop only reads them. See
/// <see cref="TickTacticalGeometry"/> for how each one is used.
/// </remarks>
internal readonly record struct TickTeamStyle
{
    /// <summary>Gets how far the block pushes up the pitch with the ball, in pitch units.</summary>
    public required int PossessionDepthShift { get; init; }

    /// <summary>Gets how far the block drops (negative) without the ball, in pitch units.</summary>
    public required int OutOfPossessionDepthShift { get; init; }

    /// <summary>Gets how far the defensive line is raised (positive) or dropped (negative), in pitch units.</summary>
    public required int LineShift { get; init; }

    /// <summary>Gets how much of the block follows the ball up and down the pitch with the ball, in percent.</summary>
    public required int PossessionCompactnessPercent { get; init; }

    /// <summary>Gets how much of the block follows the ball up and down the pitch without the ball, in percent.</summary>
    public required int OutOfPossessionCompactnessPercent { get; init; }

    /// <summary>Gets the lateral spread with the ball, in basis points of the board's spread (10,000 is as drawn).</summary>
    public required int PossessionWidthBasisPoints { get; init; }

    /// <summary>Gets the lateral spread without the ball, in basis points of the board's spread.</summary>
    public required int OutOfPossessionWidthBasisPoints { get; init; }

    /// <summary>Gets how far the whole block leans towards the focused wing in possession, in pitch units (positive is right).</summary>
    public required int FocusLateralShift { get; init; }

    /// <summary>Builds the style for a side's instructions.</summary>
    /// <param name="instructions">The side's instructions.</param>
    public static TickTeamStyle From(MatchInstructionsV1 instructions)
    {
        ArgumentNullException.ThrowIfNull(instructions);

        var (inPossession, outOfPossession) = instructions.Mentality switch
        {
            MatchMentality.Defensive => (400, -600),
            MatchMentality.Cautious => (500, -500),
            MatchMentality.Positive => (700, -350),
            MatchMentality.Attacking => (800, -300),
            _ => (600, -450),
        };

        var widthInPossession = instructions.Width switch
        {
            MatchWidth.Narrow => 11_500,
            MatchWidth.Wide => 13_500,
            _ => 12_500,
        };

        var widthOutOfPossession = instructions.Width switch
        {
            MatchWidth.Narrow => 7_500,
            MatchWidth.Wide => 8_500,
            _ => 8_000,
        };

        // Pass focus steers the shape: the centre pulls the block in, the wings push it out, and a lean towards one
        // flank slides the whole block that way.
        var (focusWidth, focusLean) = instructions.PassFocus switch
        {
            MatchPassFocus.Centre => (-1_000, 0),
            MatchPassFocus.CentreAndLeft => (-500, -300),
            MatchPassFocus.CentreAndRight => (-500, 300),
            MatchPassFocus.Wings => (500, 0),
            _ => (0, 0),
        };

        return new TickTeamStyle
        {
            PossessionDepthShift = inPossession,
            OutOfPossessionDepthShift = outOfPossession,
            LineShift = instructions.DefensiveLine switch
            {
                MatchDefensiveLine.High => 600,
                MatchDefensiveLine.Deep => -700,
                _ => 0,
            },
            PossessionCompactnessPercent = 24,
            OutOfPossessionCompactnessPercent = instructions.Pressing switch
            {
                MatchPressing.HighPress => 34,
                MatchPressing.LowBlock => 24,
                _ => 30,
            },
            PossessionWidthBasisPoints = widthInPossession + focusWidth,
            OutOfPossessionWidthBasisPoints = widthOutOfPossession,
            FocusLateralShift = focusLean,
        };
    }
}

/// <summary>
/// Dynamic formation anchors for the tick engine: where each player's shape position is at this moment
/// (`tick-engine-v1`, Milestone 2).
/// </summary>
/// <remarks>
/// <para>
/// The anchor is the board's position for the slot, moved four ways, all from the side's own point of view:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>Ball progress.</b> The block follows the ball up the pitch: <c>(ball X − 5,000) × compactness / 100</c>, where
/// compactness is 24% with the ball, and 24% / 30% / 34% without it under a low block / mid block / high press. A
/// midfielder takes all of it, a defender 90%, an attacker 80% (so the team stretches a little when it attacks).
/// </description></item>
/// <item><description>
/// <b>Phase.</b> With the ball the block pushes up by +400 (Defensive) to +800 (Attacking) units; without it, it drops
/// by −300 (Attacking) to −600 (Defensive) units.
/// </description></item>
/// <item><description>
/// <b>Defensive line.</b> A high line adds +600, a deep one −700. A defender takes all of it, a midfielder 60%, an
/// attacker 30%, so a high line also squeezes the space between the lines.
/// </description></item>
/// <item><description>
/// <b>Width.</b> The player's distance from the middle of the pitch is multiplied by 1.15 to 1.35 with the ball and 0.75
/// to 0.85 without it, so wingers spread wide in attack and tuck in to defend while the central players barely move.
/// Pass focus nudges that factor and leans the block towards a favoured flank.
/// </description></item>
/// </list>
/// <para>
/// One addition beyond the plan: the whole block slides sideways towards the ball (8% of its offset from the middle with
/// the ball, 20% without), because a team that defends the far side of the pitch is not compact.
/// </para>
/// <para>
/// The goalkeeper takes an eighth of the ball shift and none of the phase, line or width shifts, and never leaves the
/// box. The pure functions here touch nothing and allocate nothing.
/// </para>
/// <para>
/// The blended overloads (`tick-film-v1`, Milestone 5) take the shape part-way between the one with the ball and the one without it, and
/// the loop hands them not the ball but a smoothed reference to where it is going, so the block bends with the play and does not start
/// and stop with each pass.
/// </para>
/// </remarks>
internal static class TickTacticalGeometry
{
    /// <summary>The players in a side.</summary>
    public const int TeamSize = 11;

    /// <summary>The margin any anchor keeps from a touchline or goal line, in pitch units.</summary>
    public const int Margin = 150;

    /// <summary>The nearest to his own goal line a defender's shape position drops, in pitch units.</summary>
    public const int DefenceMinimumX = 800;

    /// <summary>The nearest to the opponents' goal line a player's shape position rises, in pitch units.</summary>
    public const int AttackMarginX = 700;

    /// <summary>The deepest into the pitch a goalkeeper's anchor may go, in pitch units.</summary>
    public const int GoalkeeperLimit = 1_100;

    private const int BasisPoints = 10_000;

    /// <summary>The share of the ball shift each family takes, in percent.</summary>
    private static int BallShiftPercent(MatchPositionFamily family) => family switch
    {
        MatchPositionFamily.Goalkeeper => 12,
        MatchPositionFamily.Defence => 90,
        MatchPositionFamily.Attack => 80,
        _ => 100,
    };

    /// <summary>The share of the defensive-line shift each family takes, in percent.</summary>
    private static int LineShiftPercent(MatchPositionFamily family) => family switch
    {
        MatchPositionFamily.Defence => 100,
        MatchPositionFamily.Midfield => 60,
        MatchPositionFamily.Attack => 30,
        _ => 0,
    };

    /// <summary>Resolves one player's anchor in absolute pitch coordinates.</summary>
    /// <param name="spec">His board anchor.</param>
    /// <param name="style">His side's style.</param>
    /// <param name="isHome">Whether the side is at home (attacking towards high X).</param>
    /// <param name="hasPossession">Whether the side has the ball.</param>
    /// <param name="ballXUnits">The ball's X, in absolute pitch units.</param>
    /// <param name="ballYUnits">The ball's Y, in absolute pitch units.</param>
    public static SpatialPoint Resolve(
        in TickAnchorSpec spec,
        in TickTeamStyle style,
        bool isHome,
        bool hasPossession,
        int ballXUnits,
        int ballYUnits)
    {
        // The ball, as the side sees it: forward is up, and Y counts from its own left.
        var ownBallX = isHome ? ballXUnits : SpatialPitch.PitchLength - ballXUnits;
        var ownBallY = isHome ? ballYUnits : SpatialPitch.PitchWidth - ballYUnits;
        var middleY = SpatialPitch.GoalYCenter;

        var compactness = hasPossession ? style.PossessionCompactnessPercent : style.OutOfPossessionCompactnessPercent;
        var ballShift = (ownBallX - (SpatialPitch.PitchLength / 2)) * compactness / 100 * BallShiftPercent(spec.Family) / 100;

        int ownX;
        int ownY;

        if (spec.Family == MatchPositionFamily.Goalkeeper)
        {
            ownX = Math.Clamp(spec.OwnX + ballShift, Margin, GoalkeeperLimit);
            ownY = spec.OwnY + ((ownBallY - middleY) * 10 / 100);
        }
        else
        {
            var phaseShift = hasPossession ? style.PossessionDepthShift : style.OutOfPossessionDepthShift;
            var lineShift = style.LineShift * LineShiftPercent(spec.Family) / 100;

            ownX = spec.OwnX + ballShift + phaseShift + lineShift;

            var widthBasisPoints = hasPossession ? style.PossessionWidthBasisPoints : style.OutOfPossessionWidthBasisPoints;
            var lean = hasPossession ? style.FocusLateralShift : 0;
            var slide = (ownBallY - middleY) * (hasPossession ? 8 : 20) / 100;

            ownY = middleY + ((spec.OwnY - middleY) * widthBasisPoints / BasisPoints) + lean + slide;
        }

        ownX = Math.Clamp(ownX, spec.Family == MatchPositionFamily.Defence ? DefenceMinimumX : Margin, SpatialPitch.PitchLength - AttackMarginX);
        ownY = Math.Clamp(ownY, Margin, SpatialPitch.PitchWidth - Margin);

        return isHome
            ? new SpatialPoint(ownX, ownY)
            : new SpatialPoint(SpatialPitch.PitchLength - ownX, SpatialPitch.PitchWidth - ownY);
    }

    /// <summary>
    /// Resolves one player's anchor part-way between the shape with the ball and the shape without it, so a turnover bends the block
    /// into its new shape instead of switching it in one tick.
    /// </summary>
    /// <param name="spec">His board anchor.</param>
    /// <param name="style">His side's style.</param>
    /// <param name="isHome">Whether the side is at home (attacking towards high X).</param>
    /// <param name="possessionBlendBasisPoints">How much of the side's shape is the one with the ball: 0 is without it, 10,000 is with it.</param>
    /// <param name="ballXUnits">The ball reference's X, in absolute pitch units.</param>
    /// <param name="ballYUnits">The ball reference's Y, in absolute pitch units.</param>
    public static SpatialPoint Resolve(
        in TickAnchorSpec spec,
        in TickTeamStyle style,
        bool isHome,
        int possessionBlendBasisPoints,
        int ballXUnits,
        int ballYUnits)
    {
        if (possessionBlendBasisPoints >= BasisPoints)
        {
            return Resolve(spec, style, isHome, true, ballXUnits, ballYUnits);
        }

        if (possessionBlendBasisPoints <= 0)
        {
            return Resolve(spec, style, isHome, false, ballXUnits, ballYUnits);
        }

        var with = Resolve(spec, style, isHome, true, ballXUnits, ballYUnits);
        var without = Resolve(spec, style, isHome, false, ballXUnits, ballYUnits);

        return new SpatialPoint(
            without.X + ((with.X - without.X) * possessionBlendBasisPoints / BasisPoints),
            without.Y + ((with.Y - without.Y) * possessionBlendBasisPoints / BasisPoints));
    }

    /// <summary>Resolves the anchors of a whole side part-way between its two shapes (see the blended <c>Resolve</c>).</summary>
    /// <param name="specs">The side's board anchors, one per player.</param>
    /// <param name="style">The side's style.</param>
    /// <param name="isHome">Whether the side is at home.</param>
    /// <param name="possessionBlendBasisPoints">How much of the side's shape is the one with the ball, 0..10,000.</param>
    /// <param name="ballXUnits">The ball reference's X, in absolute pitch units.</param>
    /// <param name="ballYUnits">The ball reference's Y, in absolute pitch units.</param>
    /// <param name="anchors">Receives one anchor per spec, in the same order.</param>
    public static void ResolveTeam(
        ReadOnlySpan<TickAnchorSpec> specs,
        in TickTeamStyle style,
        bool isHome,
        int possessionBlendBasisPoints,
        int ballXUnits,
        int ballYUnits,
        Span<SpatialPoint> anchors)
    {
        for (var index = 0; index < specs.Length; index++)
        {
            anchors[index] = Resolve(specs[index], style, isHome, possessionBlendBasisPoints, ballXUnits, ballYUnits);
        }
    }

    /// <summary>Resolves the anchors of a whole side.</summary>
    /// <param name="specs">The side's board anchors, one per player.</param>
    /// <param name="style">The side's style.</param>
    /// <param name="isHome">Whether the side is at home.</param>
    /// <param name="hasPossession">Whether the side has the ball.</param>
    /// <param name="ballXUnits">The ball's X, in absolute pitch units.</param>
    /// <param name="ballYUnits">The ball's Y, in absolute pitch units.</param>
    /// <param name="anchors">Receives one anchor per spec, in the same order.</param>
    public static void ResolveTeam(
        ReadOnlySpan<TickAnchorSpec> specs,
        in TickTeamStyle style,
        bool isHome,
        bool hasPossession,
        int ballXUnits,
        int ballYUnits,
        Span<SpatialPoint> anchors)
    {
        for (var index = 0; index < specs.Length; index++)
        {
            anchors[index] = Resolve(specs[index], style, isHome, hasPossession, ballXUnits, ballYUnits);
        }
    }
}
