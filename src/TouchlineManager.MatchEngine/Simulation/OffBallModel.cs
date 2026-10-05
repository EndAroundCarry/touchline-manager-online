using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Ratings;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Simulation;

/// <summary>
/// A player on the pitch and where he stands while the ball is at one point (`engine-v10`).
/// </summary>
/// <param name="Player">The player.</param>
/// <param name="Spot">Where the formation puts him, in pitch coordinates.</param>
internal readonly record struct OffBallPlayer(ActiveSlot Player, SpatialPoint Spot);

/// <summary>
/// Where players stand when the ball is not at their feet, and what that is worth to a pass (`engine-v10`).
/// </summary>
/// <remarks>
/// <para>
/// The engine used to have no positions at all. This model gives each player a spot from the formation resolver,
/// moved by where the ball is, and reads three things off it for a pass to a point: how open the receiver is
/// there, how far the ball takes the attack, and whether he can get to it. A fourth, the depth rule, says who may
/// be given the ball at all: a ball is not played back to a defender when the attack is near the other goal.
/// </para>
/// <para>
/// Pure and integer-only: it reads the state it is given and draws nothing, so using it never moves a play draw
/// or a derived stream. Every score is on the basis-point scale, 0…10,000, so the receiver chooser can weigh them
/// against one another.
/// </para>
/// </remarks>
internal static class OffBallModel
{
    /// <summary>
    /// Places one side's players for the moment the ball is at a point.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Resolved for each pass and not once a possession, because the block shifts with the ball. Order follows the
    /// side's active list, so a result is reproducible slot for slot.
    /// </para>
    /// <para>
    /// The away side is resolved in the home frame and flipped back. <see cref="TacticalFormationResolver"/> adds
    /// its forward and phase shifts to X without mirroring their sign for the away side, which attacks towards
    /// the low end of the pitch, so on its own it pulls an attacking away block back. The film still reads it as
    /// it is; the model must not, or the two sides would be placed by different rules.
    /// </para>
    /// </remarks>
    /// <param name="side">The players on the pitch.</param>
    /// <param name="isHome">Whether the side is at home.</param>
    /// <param name="hasPossession">Whether the side has the ball.</param>
    /// <param name="ball">Where the ball is, in pitch coordinates.</param>
    /// <param name="instructions">The side's instructions.</param>
    /// <param name="rules">The rules in force.</param>
    public static IReadOnlyList<OffBallPlayer> Place(
        IReadOnlyList<ActiveSlot> side,
        bool isHome,
        bool hasPossession,
        SpatialPoint ball,
        MatchInstructionsV1 instructions,
        EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(side);
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(rules);

        var placed = new List<OffBallPlayer>(side.Count);
        var frameBall = isHome ? ball : Mirror(ball);

        foreach (var player in side)
        {
            var spot = TacticalFormationResolver.ResolvePosition(
                player.Slot, isHome: true, hasPossession, frameBall, instructions, rules);

            placed.Add(new OffBallPlayer(player, isHome ? spot : Mirror(spot)));
        }

        return placed;
    }

    /// <summary>
    /// Gets how far the nearest defender is from a point, once his reading of the game is counted: a defender with
    /// the best Marking and Positioning covers a point as if he were nearer, a poor one as if he were further.
    /// </summary>
    /// <remarks>
    /// The goalkeeper marks nobody. When there is no defender to read, the point is as free as the pitch allows.
    /// </remarks>
    /// <param name="point">The point, in pitch coordinates.</param>
    /// <param name="defenders">The defending side, placed for this ball.</param>
    /// <param name="rules">The rules in force.</param>
    public static int NearestDefenderDistance(SpatialPoint point, IReadOnlyList<OffBallPlayer> defenders, EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(defenders);
        ArgumentNullException.ThrowIfNull(rules);

        var nearest = (long)SpatialPitch.PitchLength;

        foreach (var defender in defenders)
        {
            if (defender.Player.Slot.Family == MatchPositionFamily.Goalkeeper)
            {
                continue;
            }

            var edge = PositioningEdge.OfDefender(defender.Player, rules);
            var covered = (long)SpatialMath.Distance(point, defender.Spot) * EngineRulesV2.Certain / edge;

            nearest = Math.Min(nearest, covered);
        }

        return (int)nearest;
    }

    /// <summary>
    /// Gets how open a receiver is at a point: 0 with a defender on him, 10,000 when none is within
    /// <see cref="EngineRulesV2.OffBallOpennessFullDistance"/>.
    /// </summary>
    /// <remarks>
    /// The receiver's own Positioning is his gain: a player who finds space is as open as if the nearest defender
    /// were further off, by the same edge that picks a shooter (`engine-v10`).
    /// </remarks>
    /// <param name="point">Where he receives the ball, in pitch coordinates.</param>
    /// <param name="receiver">The receiver.</param>
    /// <param name="defenders">The defending side, placed for this ball.</param>
    /// <param name="rules">The rules in force.</param>
    public static int Openness(SpatialPoint point, ActiveSlot receiver, IReadOnlyList<OffBallPlayer> defenders, EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(receiver);

        var free = (long)NearestDefenderDistance(point, defenders, rules) * PositioningEdge.Of(receiver, rules) / EngineRulesV2.Certain;

        return Score(free, rules.OffBallOpennessFullDistance);
    }

    /// <summary>Gets whether the player on the ball is under pressure: a defender is within the pressure distance.</summary>
    /// <param name="holder">Where the holder stands, in pitch coordinates.</param>
    /// <param name="defenders">The defending side, placed for this ball.</param>
    /// <param name="rules">The rules in force.</param>
    public static bool IsUnderPressure(SpatialPoint holder, IReadOnlyList<OffBallPlayer> defenders, EngineRulesV2 rules) =>
        NearestDefenderDistance(holder, defenders, rules) < rules.OffBallPressureDistance;

    /// <summary>
    /// Gets how far a receiver can get to a pass, in pitch units: the rules' reach, widened or narrowed by his
    /// Positioning.
    /// </summary>
    /// <param name="receiver">The receiver.</param>
    /// <param name="rules">The rules in force.</param>
    public static int MaxReach(ActiveSlot receiver, EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(receiver);
        ArgumentNullException.ThrowIfNull(rules);

        return (int)((long)rules.OffBallReachDistance * PositioningEdge.Of(receiver, rules) / EngineRulesV2.Certain);
    }

    /// <summary>Gets whether a receiver can get to a pass played to a point.</summary>
    /// <param name="receiver">The receiver and where he stands.</param>
    /// <param name="target">Where the ball is played to, in pitch coordinates.</param>
    /// <param name="rules">The rules in force.</param>
    public static bool IsWithinReach(OffBallPlayer receiver, SpatialPoint target, EngineRulesV2 rules)
    {
        var reach = (long)MaxReach(receiver.Player, rules);

        return SpatialMath.DistanceSquared(receiver.Spot, target) <= reach * reach;
    }

    /// <summary>
    /// Gets how easily a receiver gets to a pass played to a point: 10,000 when he is already there, 0 at the
    /// limit of his reach, and 0 beyond it.
    /// </summary>
    /// <param name="receiver">The receiver and where he stands.</param>
    /// <param name="target">Where the ball is played to, in pitch coordinates.</param>
    /// <param name="rules">The rules in force.</param>
    public static int ReachScore(OffBallPlayer receiver, SpatialPoint target, EngineRulesV2 rules)
    {
        var reach = MaxReach(receiver.Player, rules);

        if (reach < 1)
        {
            return 0;
        }

        var covered = SpatialMath.Distance(receiver.Spot, target);

        return EngineRulesV2.Certain - Score(covered, reach);
    }

    /// <summary>
    /// Gets how far a pass takes the ball up the pitch, in pitch units, on the side's own scale: positive when it
    /// is forward, negative when it is back.
    /// </summary>
    /// <param name="holder">Where the ball is, in pitch coordinates.</param>
    /// <param name="target">Where it is played to, in pitch coordinates.</param>
    /// <param name="isHome">Whether the side attacks towards the high end of the pitch.</param>
    public static int Progress(SpatialPoint holder, SpatialPoint target, bool isHome) =>
        PassagePlanner.AttackingX(target.X, isHome) - PassagePlanner.AttackingX(holder.X, isHome);

    /// <summary>
    /// Gets how much a pass is worth for taking the attack forward: nothing for a ball sideways or back, the
    /// whole score for <see cref="EngineRulesV2.OffBallProgressFullGain"/> or more.
    /// </summary>
    /// <param name="holder">Where the ball is, in pitch coordinates.</param>
    /// <param name="target">Where it is played to, in pitch coordinates.</param>
    /// <param name="isHome">Whether the side attacks towards the high end of the pitch.</param>
    /// <param name="rules">The rules in force.</param>
    public static int ProgressScore(SpatialPoint holder, SpatialPoint target, bool isHome, EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return Score(Progress(holder, target, isHome), rules.OffBallProgressFullGain);
    }

    /// <summary>
    /// Gets whether a player may be given the ball at all, by how far back it goes and how far up the pitch the
    /// attack is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A ball back or across by up to <see cref="EngineRulesV2.BackPassFreeDepth"/> is fine for anyone. Further
    /// back, up to <see cref="EngineRulesV2.BackPassMaxDepth"/>, only a midfielder or an attacker may have it, and
    /// only when the holder is under pressure. Further back than that, nobody.
    /// </para>
    /// <para>
    /// A defender is only a receiver while the holder is still building up, at or short of
    /// <see cref="EngineRulesV2.DefenderReceiveMaxHolderX"/>: with the attack near the other box the ball does
    /// not go back to a centre half on the halfway line. The goalkeeper is never given the ball here.
    /// </para>
    /// </remarks>
    /// <param name="family">The receiver's position family.</param>
    /// <param name="holder">Where the ball is, in pitch coordinates.</param>
    /// <param name="receivePoint">Where the receiver would take it, in pitch coordinates.</param>
    /// <param name="isHome">Whether the side attacks towards the high end of the pitch.</param>
    /// <param name="holderUnderPressure">Whether the player on the ball is under pressure.</param>
    /// <param name="rules">The rules in force.</param>
    public static bool PassesDepthRule(
        MatchPositionFamily family,
        SpatialPoint holder,
        SpatialPoint receivePoint,
        bool isHome,
        bool holderUnderPressure,
        EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        if (family == MatchPositionFamily.Goalkeeper)
        {
            return false;
        }

        var holderX = PassagePlanner.AttackingX(holder.X, isHome);
        var depth = holderX - PassagePlanner.AttackingX(receivePoint.X, isHome);

        if (depth <= rules.BackPassFreeDepth)
        {
            return family != MatchPositionFamily.Defence || holderX <= rules.DefenderReceiveMaxHolderX;
        }

        return depth <= rules.BackPassMaxDepth
            && holderUnderPressure
            && family != MatchPositionFamily.Defence;
    }

    /// <summary>Turns a point round the centre of the pitch, which is the same player seen from the other end.</summary>
    private static SpatialPoint Mirror(SpatialPoint point) =>
        new(SpatialPitch.PitchLength - point.X, SpatialPitch.PitchWidth - point.Y);

    /// <summary>Scales a distance to the basis-point score it earns against a full distance, 0…10,000.</summary>
    private static int Score(long value, int full) =>
        (int)Math.Clamp(value * EngineRulesV2.Certain / full, 0, EngineRulesV2.Certain);
}
