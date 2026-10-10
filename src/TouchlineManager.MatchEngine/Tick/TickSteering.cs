using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>What open play adds to a man's steering (`tick-film-v1`, Milestone 5); the default adds nothing.</summary>
internal readonly ref struct TickSteerContext
{
    /// <summary>Gets the other side's players, whom he keeps clear of unless he is <see cref="Engaged"/>; empty for no personal space.</summary>
    public ReadOnlySpan<TickPlayerState> Opponents { get; init; }

    /// <summary>Gets whether he is in a duel or on the ball (the carrier, the presser, the man sent to meet a pass): such a man goes at the opponent.</summary>
    public bool Engaged { get; init; }

    /// <summary>Gets whether he has a micro-target to walk to while he is in position.</summary>
    public bool HasMicro { get; init; }

    /// <summary>Gets the point he walks to while he is in position, in pitch units.</summary>
    public SpatialPoint Micro { get; init; }

    /// <summary>Gets the share of top speed he walks to it at, or 0 for a walk.</summary>
    public int MicroPaceBasisPoints { get; init; }
}

/// <summary>
/// Steering for the tick engine: turns "stand on that anchor" into the move a player asks the physics for
/// (`tick-engine-v1`, Milestone 2).
/// </summary>
/// <remarks>
/// <para>
/// Four behaviours are added together into the velocity the player <em>wants</em>, all in fixed units per tick:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>Arrive.</b> Towards the anchor at a holding pace (55% of top speed within 5 m, rising to 90% from 30 m), but never
/// faster than <c>√(2 · braking · distance)</c>, so he glides onto the anchor instead of overshooting and shaking; a man holding
/// his place brakes at 60% of what he has (so he eases in), one sent at a pace brakes in full. He is content anywhere within 1 m of
/// it: a shape does not need to be exact, and the slack is what lets two players given the same anchor stand beside each other
/// instead of fighting for the spot. A man in position who has a micro-target (<see cref="TickSteerContext"/>) does not stand
/// there: within 10 m of his anchor he walks, at 18% of his top speed or the pace his order gives, to the micro-target (`tick-film-v1`,
/// Milestone 5).
/// </description></item>
/// <item><description>
/// <b>Separation.</b> Each teammate within 2 m pushes him away, in proportion to how far inside that circle the
/// teammate stands, up to 40% of his top speed. Two players never settle on top of each other, and a crowd drifts
/// apart rather than stacking. A man who is not engaged in a duel or on the ball is pushed the same way by an opponent within
/// 2.5 m, at twice the strength, so a marker or a striker on the offside line does not stand on the man he faces.
/// </description></item>
/// <item><description>
/// <b>Velocity blending.</b> The result is <c>desired × 55% + current × 45%</c>, so a sudden change of anchor (the
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
    public const int InertiaBasisPoints = 4_500;

    /// <summary>
    /// The share of his braking a man holding his place uses to ease onto it, in percent: he starts slowing early and does not stop dead. A man sent at
    /// a pace brakes in full: easing in a presser kept him inside contact range, challenging again and again, and raised the fouls by a third.
    /// </summary>
    public const int ArriveBrakingPercent = 60;

    /// <summary>The pace of a player walking about his place, in basis points of top speed (about 1.5 m/s): inside the walk band, so it recovers energy.</summary>
    public const int WalkBasisPoints = 1_800;

    /// <summary>The distance within which opponents who are not engaged push each other apart (2.5 m), in fixed units.</summary>
    public static readonly int OpponentSpaceRadius = TickSpatialUnits.CentimetresToFixed(250);

    /// <summary>
    /// The strength of the push from an opponent, as a share of the push from a teammate, in percent. A half-strength push at 1.5 m (the plan's first
    /// number) left a striker held a metre short of the offside line standing beside the centre-back he faces: the pull to his place won.
    /// </summary>
    public const int OpponentSpacePercent = 200;

    /// <summary>
    /// How far from his anchor (10 m) a player may be and still walk about his micro-target: beyond it he is out of position and goes back.
    /// </summary>
    public static readonly int MicroLeash = TickSpatialUnits.CentimetresToFixed(1_000);

    /// <summary>The distance from the micro-target (0.4 m) within which a player is there, in fixed units.</summary>
    private static readonly int MicroContentRadius = TickSpatialUnits.CentimetresToFixed(40);

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
    /// <param name="context">What the open play adds: the opponents to keep clear of, whether he is engaged, and his micro-target.</param>
    public static TickMoveIntent Steer(
        int selfIndex,
        ReadOnlySpan<TickPlayerState> team,
        in TickPlayerProfile profile,
        SpatialPoint anchor,
        int paceBasisPoints = 0,
        in TickSteerContext context = default)
    {
        var self = team[selfIndex];
        var topSpeed = TickPlayerPhysics.EffectiveTopSpeed(self, profile);

        // A man holding his place eases onto it; one sent somewhere at a pace (the presser, the marker, the man meeting a pass) brakes in full.
        var braking = paceBasisPoints > 0 ? profile.Deceleration : (long)profile.Deceleration * ArriveBrakingPercent / 100;

        // Arrive: aim at the anchor, slow enough to stop on it.
        var dx = (long)TickSpatialUnits.ToFixed(anchor.X) - self.X;
        var dy = (long)TickSpatialUnits.ToFixed(anchor.Y) - self.Y;
        var distance = SpatialMath.Sqrt((dx * dx) + (dy * dy));

        long desiredX = 0;
        long desiredY = 0;

        if (context.HasMicro && distance <= MicroLeash)
        {
            // In position, he does not stand: he walks (or jogs, when the order says so) to a nearby point and keeps adjusting.
            var mx = (long)TickSpatialUnits.ToFixed(context.Micro.X) - self.X;
            var my = (long)TickSpatialUnits.ToFixed(context.Micro.Y) - self.Y;
            var microDistance = SpatialMath.Sqrt((mx * mx) + (my * my));

            if (microDistance > MicroContentRadius)
            {
                var cap = (long)topSpeed * (context.MicroPaceBasisPoints > 0 ? context.MicroPaceBasisPoints : WalkBasisPoints) / BasisPoints;
                var wanted = Math.Min(cap, SpatialMath.Sqrt(2L * braking * (microDistance - MicroContentRadius)));

                desiredX = mx * wanted / microDistance;
                desiredY = my * wanted / microDistance;
            }
        }
        else if (distance > ContentRadius)
        {
            // He is content anywhere within the radius, so the run is only as long as what lies beyond it.
            var cap = (long)topSpeed * (paceBasisPoints > 0 ? paceBasisPoints : HoldSpeedBasisPoints(distance)) / BasisPoints;
            var wanted = Math.Min(cap, SpatialMath.Sqrt(2L * braking * (distance - ContentRadius)));

            desiredX = dx * wanted / distance;
            desiredY = dy * wanted / distance;
        }

        // Separation: lean away from every teammate inside the circle.
        long pushX = 0;
        long pushY = 0;
        var maxPush = (long)topSpeed * SeparationBasisPoints / BasisPoints;

        for (var other = 0; other < team.Length; other++)
        {
            if (other == selfIndex)
            {
                continue;
            }

            var awayX = (long)self.X - team[other].X;
            var awayY = (long)self.Y - team[other].Y;
            var apart = (awayX * awayX) + (awayY * awayY);

            // Most of his side is far off: only a root for the ones inside the circle.
            if (apart >= (long)SeparationRadius * SeparationRadius)
            {
                continue;
            }

            var gap = SpatialMath.Sqrt(apart);

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

        // Personal space: a man who is not in a duel does not stand on an opponent either, at half the strength.
        if (!context.Engaged)
        {
            var opponentPush = maxPush * OpponentSpacePercent / 100;
            var limit = (long)OpponentSpaceRadius * OpponentSpaceRadius;

            for (var other = 0; other < context.Opponents.Length; other++)
            {
                var awayX = (long)self.X - context.Opponents[other].X;
                var awayY = (long)self.Y - context.Opponents[other].Y;
                var apart = (awayX * awayX) + (awayY * awayY);

                if (apart >= limit)
                {
                    continue;
                }

                var gap = SpatialMath.Sqrt(apart);

                if (gap == 0)
                {
                    awayX = 0;
                    awayY = (selfIndex & 1) == 0 ? 1 : -1;
                    gap = 1;
                }

                var strength = opponentPush * (OpponentSpaceRadius - gap) / OpponentSpaceRadius;

                pushX += awayX * strength / gap;
                pushY += awayY * strength / gap;
            }
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
        var length = SpatialMath.Sqrt((x * x) + (y * y));

        if (length > maximum && length > 0)
        {
            x = x * maximum / length;
            y = y * maximum / length;
        }
    }
}
