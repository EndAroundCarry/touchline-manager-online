using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Simulation;

/// <summary>
/// Set piece event outcome (penalty, direct free kick, corner).
/// </summary>
public readonly record struct SetPieceOutcome(
    bool IsGoal,
    bool WasSaved,
    bool HitWoodwork,
    Guid TakerId,
    Guid? GoalkeeperId,
    SpatialPoint BallOrigin,
    SpatialPoint BallTarget);

/// <summary>
/// Simulates set pieces: penalties, direct free kicks with defensive walls, and corners.
/// </summary>
public static class SetPieceDirector
{
    /// <summary>
    /// Simulates a penalty kick with goalkeeper dive resolution.
    /// </summary>
    public static SetPieceOutcome ResolvePenalty(
        MatchParticipantV1 taker,
        MatchParticipantV1 goalkeeper,
        bool takerIsHome,
        Pcg32 random)
    {
        var spot = takerIsHome
            ? new SpatialPoint(SpatialPitch.PenaltySpotAwayX, SpatialPitch.PenaltySpotY)
            : new SpatialPoint(SpatialPitch.PenaltySpotHomeX, SpatialPitch.PenaltySpotY);

        var finishing = taker.Attributes.ValueOf(MatchAttributeName.Finishing);
        var composure = taker.Attributes.ValueOf(MatchAttributeName.Composure);
        var reflexes = goalkeeper.Attributes.ValueOf(MatchAttributeName.Reflexes);

        var goalProb = 7500 + ((finishing + composure - reflexes) * 80);
        goalProb = Math.Clamp(goalProb, 6000, 9200); // 60-92% realistic penalty conversion rate

        var isGoal = random.RollBasisPoints(goalProb);
        var wasSaved = !isGoal && random.RollBasisPoints(7000); // 70% of misses are keeper saves

        var goalRange = SpatialPitch.GoalYMax - 100 - (SpatialPitch.GoalYMin + 100);
        var targetY = SpatialPitch.GoalYMin + 100 + random.NextInt(Math.Max(1, goalRange));
        var targetX = takerIsHome ? SpatialPitch.AwayGoalX : SpatialPitch.HomeGoalX;

        return new SetPieceOutcome(
            isGoal,
            wasSaved,
            !isGoal && !wasSaved,
            taker.ParticipantId,
            goalkeeper.ParticipantId,
            spot,
            new SpatialPoint(targetX, targetY));
    }
}
