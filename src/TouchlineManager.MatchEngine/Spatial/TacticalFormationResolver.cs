using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Spatial;

/// <summary>
/// Resolves a player's dynamic spatial pitch position for a possession: their formation anchor, moved by
/// the ball's location, the phase of play, and the side's instructions (master plan Stage 2).
/// </summary>
/// <remarks>
/// <para>
/// The resolver is pure: it maps a slot onto the normalized pitch for one moment of the match and touches
/// nothing. The simulation calls it to know where the play is, and the highlight director consumes the
/// same normalized coordinates the tactics board stores, so a replay and a team sheet never disagree
/// about where a player stands.
/// </para>
/// </remarks>
public static class TacticalFormationResolver
{
    /// <summary>The margin any resolved position keeps from a touchline, in normalized units.</summary>
    public const int TouchlineMargin = 150;

    /// <summary>
    /// Resolves one slot's position for one possession.
    /// </summary>
    /// <param name="slot">The slot, whose coordinates are the normalized values the tactics board stores.</param>
    /// <param name="isHome">Whether the side is at home.</param>
    /// <param name="hasPossession">Whether the side has the ball this possession.</param>
    /// <param name="ballPosition">Where the ball is, in pitch coordinates.</param>
    /// <param name="instructions">The side's instructions.</param>
    /// <param name="rules">The rules in force.</param>
    /// <returns>The position, clamped strictly inside the pitch.</returns>
    public static SpatialPoint ResolvePosition(
        MatchSlotV1 slot,
        bool isHome,
        bool hasPossession,
        SpatialPoint ballPosition,
        MatchInstructionsV1 instructions,
        EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(rules);

        // The slot's coordinates are normalized 0..10_000 on both axes, with X running towards the goal the
        // side attacks. The away side is mirrored on both axes, so both sides always advance the same way.
        var anchor = Orient(slot.X, slot.Y, isHome);

        // How far up the pitch the ball is from the side's own point of view: 0 near their own goal, 10_000
        // at the goal they attack.
        var ballProgress = isHome ? ballPosition.X : SpatialPitch.PitchLength - ballPosition.X;

        // The block shifts with the ball: a twelfth of the distance the ball is from halfway, so play flows
        // up and down the pitch as a unit rather than teleporting between phases.
        var ballShift = ((ballProgress - (SpatialPitch.PitchLength / 2)) * 12) / 100;

        var mentalityShift = instructions.Mentality switch
        {
            MatchMentality.Attacking => 700,
            MatchMentality.Positive => 400,
            MatchMentality.Defensive => -400,
            MatchMentality.Cautious => -200,
            _ => 200,
        };

        var phaseShift = hasPossession ? mentalityShift : -mentalityShift / 2;

        var lineShift = instructions.DefensiveLine switch
        {
            MatchDefensiveLine.High => 350,
            MatchDefensiveLine.Deep => -500,
            _ => 0,
        };

        // A high press converges on the ball's line vertically, a fifth of the way across.
        var pressSqueeze = !hasPossession && instructions.Pressing == MatchPressing.HighPress
            ? (ballPosition.Y - anchor.Y) / 5
            : 0;

        // The shifts above are from the side's own point of view, where positive is up the pitch. The home side
        // attacks towards high X; the away side attacks towards low X, so its shift is subtracted.
        var xOffset = phaseShift + lineShift + ballShift;
        var yOffset = pressSqueeze;

        // A goalkeeper tracks the ball's line but stays near their goal, whatever the phase asks of them.
        if (slot.Family == MatchPositionFamily.Goalkeeper)
        {
            xOffset /= 4;
        }

        // Width instructions stretch the block in possession and compress it out of possession.
        var widthFactor = instructions.Width switch
        {
            MatchWidth.Wide => 1_250,
            MatchWidth.Narrow => 850,
            _ => 1_000,
        };

        if (!hasPossession)
        {
            widthFactor = 2_000 - widthFactor;
        }

        yOffset += ((anchor.Y - SpatialPitch.GoalYCenter) * (widthFactor - 1_000)) / 1_000;

        var position = new SpatialPoint(
            ClampAxis(isHome ? anchor.X + xOffset : anchor.X - xOffset),
            int.Clamp(anchor.Y + yOffset, TouchlineMargin, SpatialPitch.PitchWidth - TouchlineMargin));

        return position;
    }

    /// <summary>Mirrors tactical coordinates for the away side, which attacks the other way.</summary>
    /// <remarks>
    /// The slot's X runs 0..10_000 along the pitch, but its Y is the tactics board's normalized 0..10_000
    /// (`TAC-9`), while the pitch model's width is the real 0..7_000 — so the Y is scaled onto the pitch as
    /// well as mirrored. Both sides then stand where the team sheet says, whatever end they defend.
    /// </remarks>
    /// <param name="x">Position towards the goal the side attacks, 0..10_000.</param>
    /// <param name="y">Position across the pitch, 0..10_000 normalized.</param>
    /// <param name="isHome">Whether the coordinates are the home side's.</param>
    public static SpatialPoint Orient(int x, int y, bool isHome)
    {
        var pitchY = (y * SpatialPitch.PitchWidth) / EngineRulesV2.SlotCoordinateScale;

        return isHome
            ? new SpatialPoint(x, pitchY)
            : new SpatialPoint(SpatialPitch.PitchLength - x, SpatialPitch.PitchWidth - pitchY);
    }

    /// <summary>Clamps an X position inside the pitch.</summary>
    private static int ClampAxis(int x)
    {
        var clamped = int.Clamp(x, TouchlineMargin, SpatialPitch.PitchLength - TouchlineMargin);

        return clamped;
    }
}
