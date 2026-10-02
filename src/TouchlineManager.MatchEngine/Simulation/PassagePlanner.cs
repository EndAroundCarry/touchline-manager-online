using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Simulation;

/// <summary>
/// One possession's planned geometry: where the ball starts, where the pressure point is, and where each
/// outcome would leave it (`engine-v4`).
/// </summary>
/// <remarks>
/// The plan is drawn from the possession's own derived stream, never the play stream, so the ball's path is a
/// fact of the seed without being a fact that can move the scoreline. The outcome decides which of the
/// anchors below is used; the path between them is the same whichever one it is.
/// </remarks>
/// <param name="Approach">The ball's path from the start to the pressure point, in order.</param>
/// <param name="ApproachEndsInCross">Whether the last approach touch is a cross rather than a ground pass.</param>
/// <param name="PressurePoint">Where the defending side engages and a foul would be committed.</param>
/// <param name="TurnoverPoint">Where a plain turnover leaves the ball.</param>
/// <param name="OffsidePoint">Where an offside is given.</param>
/// <param name="CornerPoint">The corner flag a corner is taken from.</param>
/// <param name="HeaderPoint">Where a corner is delivered to be headed.</param>
/// <param name="ShotPoint">Where an open-play shot is taken from.</param>
/// <param name="PenaltySpot">The penalty spot.</param>
/// <param name="Zone">The shot zone the possession's chance, if it has one, is taken from.</param>
internal sealed record PlannedPassage(
    IReadOnlyList<SpatialPoint> Approach,
    bool ApproachEndsInCross,
    SpatialPoint PressurePoint,
    SpatialPoint TurnoverPoint,
    SpatialPoint OffsidePoint,
    SpatialPoint CornerPoint,
    SpatialPoint HeaderPoint,
    SpatialPoint ShotPoint,
    SpatialPoint PenaltySpot,
    ShotZone Zone);

/// <summary>
/// Builds the ball path a possession is played along (`engine-v4`).
/// </summary>
/// <remarks>
/// <para>
/// The old model gave every possession an absolute random point in the possessing side's own half, so the
/// "ball" never related to the play. This planner replaces it with a real passage: the ball begins where the
/// last one left it — or at a restart — progresses forward through three to eight touches with lateral drift,
/// and reaches the final third by the time a chance is taken. Everything here is drawn from a
/// per-possession stream derived from the match seed and the possession ordinal, so it is reproducible and
/// yet cannot shift a single play draw.
/// </para>
/// <para>
/// The pressure point is the one spatial fact the outcome formulas read: a foul happens where the defender
/// engages, and its distance up the pitch is what decides whether the resulting free kick is in shooting
/// range. That band is a rule constant so the distribution is tunable, not a side effect of where the ball
/// happened to be.
/// </para>
/// </remarks>
internal static class PassagePlanner
{
    /// <summary>A stride that keeps each possession's geometry stream distinct from the seed and the others'.</summary>
    private const ulong StreamStride = 1_000_003UL;

    /// <summary>Creates the per-possession geometry stream for the possession about to be played.</summary>
    /// <param name="state">The match state.</param>
    /// <returns>The stream, seeded from the match seed and the possession ordinal.</returns>
    public static Pcg32 CreateStream(MatchState state) =>
        new(unchecked((state.Input.Seed * StreamStride) + (ulong)state.PossessionOrdinal));

    /// <summary>Plans one possession's geometry.</summary>
    /// <param name="state">The match state.</param>
    /// <param name="side">The side in possession.</param>
    /// <param name="derived">The possession's geometry stream.</param>
    /// <returns>The plan, with every point already in pitch coordinates.</returns>
    public static PlannedPassage Plan(MatchState state, MatchSide side, Pcg32 derived)
    {
        var rules = state.Rules;
        var isHome = side == MatchSide.Home;

        var start = StartPoint(state, side, rules);
        var startAttackX = AttackingX(start.X, isHome);
        var startAttackY = AttackingY(start.Y, isHome);

        // The pressure point is where the defender engages. Its band spans the middle and attacking thirds so
        // a foul can genuinely be committed in free-kick range; it never falls behind where the ball already is.
        var pressureAttackX = Math.Clamp(
            derived.NextRange(rules.PressurePointXMinBasisPoints, rules.PressurePointXMaxBasisPoints),
            startAttackX,
            SpatialPitch.PitchLength);
        var pressureAttackY = derived.NextRange(0, SpatialPitch.PitchWidth);
        var pressure = FromAttack(pressureAttackX, pressureAttackY, isHome);

        var (approach, approachEndsInCross) = Approach(derived, rules, start, startAttackX, startAttackY, pressureAttackX, pressureAttackY, isHome);

        var zone = ChooseZone(derived);
        var shotPoint = FromAttack(
            derived.NextRange(rules.ShotFinalThirdXMinBasisPoints, rules.ShotFinalThirdXMaxBasisPoints),
            ZoneY(zone, derived, rules),
            isHome);

        var cornerY = derived.NextInt(2) == 0 ? 0 : SpatialPitch.PitchWidth;

        return new PlannedPassage(
            Approach: approach,
            ApproachEndsInCross: approachEndsInCross,
            PressurePoint: pressure,
            TurnoverPoint: FromAttack(rules.TurnoverMiddleThirdXBasisPoints, derived.NextRange(0, SpatialPitch.PitchWidth), isHome),
            OffsidePoint: FromAttack(rules.OffsideLineXBasisPoints, derived.NextRange(0, SpatialPitch.PitchWidth), isHome),
            CornerPoint: FromAttack(SpatialPitch.PitchLength, cornerY, isHome),
            HeaderPoint: FromAttack(rules.ShotFinalThirdXMinBasisPoints, SpatialPitch.PitchWidth / 2, isHome),
            ShotPoint: shotPoint,
            PenaltySpot: FromAttack(SpatialPitch.PenaltySpotAwayX, SpatialPitch.PenaltySpotY, isHome),
            Zone: zone);
    }

    /// <summary>Gets the point the possession starts from: a restart, or where the last one left the ball.</summary>
    private static SpatialPoint StartPoint(MatchState state, MatchSide side, EngineRulesV2 rules)
    {
        if (state.RestartFromCentre)
        {
            state.RestartFromCentre = false;

            return SpatialPoint.Center;
        }

        if (state.GoalAreaRestartSide == side)
        {
            state.GoalAreaRestartSide = null;

            return FromAttack(rules.GoalAreaXBasisPoints, SpatialPitch.PitchWidth / 2, side == MatchSide.Home);
        }

        return state.Ball.GroundPoint.Clamp();
    }

    /// <summary>Builds the ball's touches from the start to the pressure point.</summary>
    /// <returns>The path, and whether its final approach touch is a cross.</returns>
    private static (IReadOnlyList<SpatialPoint> Points, bool EndsInCross) Approach(
        Pcg32 derived,
        EngineRulesV2 rules,
        SpatialPoint start,
        int startAttackX,
        int startAttackY,
        int pressureAttackX,
        int pressureAttackY,
        bool isHome)
    {
        var points = new List<SpatialPoint> { start };

        // How many touches the distance takes, from a drawn per-touch advance, bounded by the rules' touch
        // count. A possession already in the final third needs fewer.
        var advance = Math.Max(1, derived.NextRange(rules.MinTouchAdvanceBasisPoints, rules.MaxTouchAdvanceBasisPoints));
        var distance = Math.Max(0, pressureAttackX - startAttackX);
        var minIntermediates = Math.Max(1, rules.MinPassageTouches - 2);
        var maxIntermediates = Math.Max(minIntermediates, rules.MaxPassageTouches - 2);
        var intermediates = distance <= 0
            ? 0
            : Math.Clamp((distance + advance - 1) / advance, minIntermediates, maxIntermediates);

        var endsInCross = derived.RollBasisPoints(rules.CrossShareOfPassageBasisPoints);

        for (var index = 1; index <= intermediates; index++)
        {
            var fraction = index * EngineRulesV2.Certain / (intermediates + 1);
            var x = Lerp(startAttackX, pressureAttackX, fraction);
            var y = Lerp(startAttackY, pressureAttackY, fraction) + LateralDrift(derived, rules);

            points.Add(FromAttack(int.Clamp(x, 0, SpatialPitch.PitchLength), int.Clamp(y, 0, SpatialPitch.PitchWidth), isHome));
        }

        points.Add(FromAttack(pressureAttackX, pressureAttackY, isHome));

        return (points, endsInCross);
    }

    /// <summary>Maps a point on the attacking side's own scale to a pitch coordinate.</summary>
    /// <param name="attackingX">The distance from the attacker's own goal, 0…10,000.</param>
    /// <param name="attackingY">The position across the pitch, 0…7,000.</param>
    /// <param name="isHome">Whether the attacker plays towards the right of the shared pitch.</param>
    private static SpatialPoint FromAttack(int attackingX, int attackingY, bool isHome)
    {
        var x = isHome ? attackingX : SpatialPitch.PitchLength - attackingX;
        var y = isHome ? attackingY : SpatialPitch.PitchWidth - attackingY;

        return new SpatialPoint(
            int.Clamp(x, 0, SpatialPitch.PitchLength),
            int.Clamp(y, 0, SpatialPitch.PitchWidth));
    }

    /// <summary>Gets how far up the pitch a pitch point is on a given side's own scale.</summary>
    private static int AttackingX(int x, bool isHome) =>
        isHome ? x : SpatialPitch.PitchLength - x;

    /// <summary>Gets where across the pitch a pitch point is on a given side's own scale.</summary>
    private static int AttackingY(int y, bool isHome) =>
        isHome ? y : SpatialPitch.PitchWidth - y;

    private static int Lerp(int from, int to, int fractionBasisPoints) =>
        (int)(from + (((long)(to - from) * fractionBasisPoints) / EngineRulesV2.Certain));

    private static int LateralDrift(Pcg32 derived, EngineRulesV2 rules)
    {
        var draw = derived.NextRange(-rules.MaxTouchLateralDriftBasisPoints, rules.MaxTouchLateralDriftBasisPoints);

        return (int)(((long)draw * SpatialPitch.PitchWidth) / EngineRulesV2.Certain);
    }

    /// <summary>Chooses the shot zone, weighted towards the middle of the pitch as before.</summary>
    private static ShotZone ChooseZone(Pcg32 derived)
    {
        var roll = derived.NextInt(100);

        return roll switch
        {
            < 40 => ShotZone.Central,
            < 60 => ShotZone.InsideLeft,
            < 80 => ShotZone.InsideRight,
            < 90 => ShotZone.WideLeft,
            _ => ShotZone.WideRight,
        };
    }

    /// <summary>Places a shot across the pitch inside its zone's band.</summary>
    private static int ZoneY(ShotZone zone, Pcg32 derived, EngineRulesV2 rules)
    {
        var middle = SpatialPitch.PitchWidth / 2;

        return zone switch
        {
            ShotZone.Central => derived.NextRange(rules.ShotCentralBandYMinBasisPoints, rules.ShotCentralBandYMaxBasisPoints),
            ShotZone.InsideLeft => derived.NextRange(rules.ShotInsideBandYMinBasisPoints, middle),
            ShotZone.InsideRight => derived.NextRange(middle, rules.ShotInsideBandYMaxBasisPoints),
            ShotZone.WideLeft => derived.NextRange(rules.ShotWideBandYMinBasisPoints, rules.ShotInsideBandYMinBasisPoints),
            _ => derived.NextRange(rules.ShotInsideBandYMaxBasisPoints, rules.ShotWideBandYMaxBasisPoints),
        };
    }
}
