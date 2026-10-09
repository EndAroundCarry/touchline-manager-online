using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>
/// Steering for the tick engine: turns "stand on that anchor" into the move a player asks the physics for
/// (`tick-engine-v1`, Milestone 2).
/// </summary>
/// <remarks>
/// <para>
/// Three behaviours are added together into the velocity the player <em>wants</em>, all in fixed units per tick:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>Arrive.</b> Towards the anchor at a holding pace (55% of top speed within 5 m, rising to 90% from 30 m), but never
/// faster than <c>√(2 · braking · distance)</c>, so he glides onto the anchor instead of overshooting and shaking. He is
/// content anywhere within 1 m of it: a shape does not need to be exact, and the slack is what lets two players given
/// the same anchor stand beside each other instead of fighting for the spot.
/// </description></item>
/// <item><description>
/// <b>Separation.</b> Each teammate within 2 m pushes him away, in proportion to how far inside that circle the
/// teammate stands, up to 40% of his top speed. Two players never settle on top of each other, and a crowd drifts
/// apart rather than stacking.
/// </description></item>
/// <item><description>
/// <b>Velocity blending.</b> The result is <c>desired × 70% + current × 30%</c>, so a sudden change of anchor (the
/// ball switching sides, the possession flipping) bends his path instead of snapping it.
/// </description></item>
/// </list>
/// <para>
/// The blended velocity is then expressed as a <see cref="TickMoveIntent"/> (a point along it and a share of top speed),
/// and <see cref="TickPlayerPhysics.Step"/> applies acceleration, turning and tiredness to it. Steering decides where he
/// would like to be going; physics decides what his legs can do about it.
/// </para>
/// <para>
/// Everything is integer arithmetic over spans, so a side's tick allocates nothing.
/// </para>
/// </remarks>
internal static class TickSteering
{
    /// <summary>The distance within which teammates push each other apart (2 m), in fixed units.</summary>
    public static readonly int SeparationRadius = TickSpatialUnits.CentimetresToFixed(200);

    /// <summary>The strongest separation push, as a share of top speed, in basis points.</summary>
    public const int SeparationBasisPoints = 4_000;

    /// <summary>The weight of the player's current velocity in the blend, in basis points.</summary>
    public const int InertiaBasisPoints = 3_000;

    /// <summary>The share of top speed used to hold position within <see cref="NearDistance"/>, in basis points.</summary>
    public const int NearSpeedBasisPoints = 5_500;

    /// <summary>The share of top speed used to recover position from <see cref="FarDistance"/> or more, in basis points.</summary>
    public const int FarSpeedBasisPoints = 9_000;

    private const int BasisPoints = 10_000;

    /// <summary>How far ahead of himself, in pitch units, the intent's target lies. Only its direction matters.</summary>
    private const int LookAheadUnits = 600;

    /// <summary>Within this distance (1 m) of his anchor a player is in position and stands still, unless pushed.</summary>
    private static readonly int ContentRadius = TickSpatialUnits.CentimetresToFixed(100);

    private static readonly int NearDistance = TickSpatialUnits.CentimetresToFixed(500);

    private static readonly int FarDistance = TickSpatialUnits.CentimetresToFixed(3_000);

    /// <summary>Gets the share of top speed a player uses to hold or recover his anchor from a distance.</summary>
    /// <param name="distance">The distance to the anchor, in fixed units.</param>
    public static int HoldSpeedBasisPoints(long distance)
    {
        if (distance <= NearDistance)
        {
            return NearSpeedBasisPoints;
        }

        if (distance >= FarDistance)
        {
            return FarSpeedBasisPoints;
        }

        return NearSpeedBasisPoints
            + (int)((FarSpeedBasisPoints - NearSpeedBasisPoints) * (distance - NearDistance) / (FarDistance - NearDistance));
    }

    /// <summary>Works out the move one player asks for to hold an anchor.</summary>
    /// <param name="selfIndex">His index in <paramref name="team"/>.</param>
    /// <param name="team">His side's players, as they stand at the start of the tick.</param>
    /// <param name="profile">His athletic limits.</param>
    /// <param name="anchor">The anchor to hold, in pitch units.</param>
    /// <param name="paceBasisPoints">
    /// The share of top speed to travel at, in basis points, or 0 for the holding pace of <see cref="HoldSpeedBasisPoints"/>
    /// (the defensive AI asks for more when it sends a player to close the ball down).
    /// </param>
    public static TickMoveIntent Steer(
        int selfIndex,
        ReadOnlySpan<TickPlayerState> team,
        in TickPlayerProfile profile,
        SpatialPoint anchor,
        int paceBasisPoints = 0)
    {
        var self = team[selfIndex];
        var topSpeed = TickPlayerPhysics.EffectiveTopSpeed(self, profile);

        // Arrive: aim at the anchor, slow enough to stop on it.
        var dx = (long)TickSpatialUnits.ToFixed(anchor.X) - self.X;
        var dy = (long)TickSpatialUnits.ToFixed(anchor.Y) - self.Y;
        var distance = SpatialMath.Sqrt((dx * dx) + (dy * dy));

        long desiredX = 0;
        long desiredY = 0;

        if (distance > ContentRadius)
        {
            // He is content anywhere within the radius, so the run is only as long as what lies beyond it.
            var cap = (long)topSpeed * (paceBasisPoints > 0 ? paceBasisPoints : HoldSpeedBasisPoints(distance)) / BasisPoints;
            var braking = 2L * profile.Deceleration * (distance - ContentRadius);
            var wanted = braking >= cap * cap ? cap : SpatialMath.Sqrt(braking);

            desiredX = dx * wanted / distance;
            desiredY = dy * wanted / distance;
        }

        // Separation: lean away from every teammate inside the circle. The squared distance is tested first,
        // because a teammate outside the circle costs no square root and most teammates are outside it.
        long pushX = 0;
        long pushY = 0;
        var maxPush = (long)topSpeed * SeparationBasisPoints / BasisPoints;
        var separationSquared = (long)SeparationRadius * SeparationRadius;

        for (var other = 0; other < team.Length; other++)
        {
            if (other == selfIndex)
            {
                continue;
            }

            var awayX = (long)self.X - team[other].X;
            var awayY = (long)self.Y - team[other].Y;
            var gapSquared = (awayX * awayX) + (awayY * awayY);

            if (gapSquared >= separationSquared)
            {
                continue;
            }

            var gap = SpatialMath.Sqrt(gapSquared);

            if (gap == 0)
            {
                // Standing exactly on a teammate: the lower index steps up the pitch's Y, the higher down it.
                awayX = 0;
                awayY = other < selfIndex ? 1 : -1;
                gap = 1;
            }

            var strength = maxPush * (SeparationRadius - gap) / SeparationRadius;

            pushX += awayX * strength / gap;
            pushY += awayY * strength / gap;
        }

        LimitLength(ref pushX, ref pushY, maxPush);
        desiredX += pushX;
        desiredY += pushY;

        // Blend: bend the old velocity towards the new wish rather than snapping to it.
        var blendedX = ((desiredX * (BasisPoints - InertiaBasisPoints)) + ((long)self.VelocityX * InertiaBasisPoints)) / BasisPoints;
        var blendedY = ((desiredY * (BasisPoints - InertiaBasisPoints)) + ((long)self.VelocityY * InertiaBasisPoints)) / BasisPoints;

        LimitLength(ref blendedX, ref blendedY, topSpeed);

        var length = SpatialMath.Sqrt((blendedX * blendedX) + (blendedY * blendedY));
        var here = new SpatialPoint(TickSpatialUnits.ToUnits(self.X), TickSpatialUnits.ToUnits(self.Y));

        if (length == 0 || topSpeed == 0)
        {
            return new TickMoveIntent(here.X, here.Y, 0, Arrive: false);
        }

        return new TickMoveIntent(
            here.X + (int)(blendedX * LookAheadUnits / length),
            here.Y + (int)(blendedY * LookAheadUnits / length),
            (int)Math.Min(BasisPoints, length * BasisPoints / topSpeed),
            Arrive: false);
    }

    /// <summary>Moves a whole side one tick towards its anchors.</summary>
    /// <remarks>
    /// Every player's intent is worked out from where the side stood at the start of the tick, and only then are the
    /// bodies moved, so the result does not depend on the order of the players.
    /// </remarks>
    /// <param name="team">The side's players, updated in place (at most <see cref="TickTacticalGeometry.TeamSize"/>).</param>
    /// <param name="profiles">Their athletic limits, in the same order.</param>
    /// <param name="anchors">The anchors to hold, in the same order, in pitch units.</param>
    /// <param name="paces">
    /// Optional: the share of top speed each player travels at, in basis points, in the same order (0 or an empty span is
    /// the holding pace).
    /// </param>
    public static void StepTeam(
        Span<TickPlayerState> team,
        ReadOnlySpan<TickPlayerProfile> profiles,
        ReadOnlySpan<SpatialPoint> anchors,
        ReadOnlySpan<int> paces = default)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(team.Length, TickTacticalGeometry.TeamSize);

        Span<TickMoveIntent> intents = stackalloc TickMoveIntent[TickTacticalGeometry.TeamSize];

        for (var index = 0; index < team.Length; index++)
        {
            intents[index] = Steer(index, team, profiles[index], anchors[index], paces.IsEmpty ? 0 : paces[index]);
        }

        for (var index = 0; index < team.Length; index++)
        {
            TickPlayerPhysics.Step(ref team[index], profiles[index], intents[index]);
        }
    }

    /// <summary>Shortens a vector to a maximum length, keeping its direction.</summary>
    private static void LimitLength(ref long x, ref long y, long maximum)
    {
        var squared = (x * x) + (y * y);

        // Most vectors are inside the limit, and the squared comparison decides that without a square root.
        if (squared <= maximum * maximum)
        {
            return;
        }

        var length = SpatialMath.Sqrt(squared);

        if (length > maximum && length > 0)
        {
            x = x * maximum / length;
            y = y * maximum / length;
        }
    }
}
