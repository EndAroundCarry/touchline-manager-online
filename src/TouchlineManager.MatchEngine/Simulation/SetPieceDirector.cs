using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;
using TouchlineManager.MatchEngine.Ratings;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Simulation;

/// <summary>The outcome of a struck set piece: a penalty or a direct free kick.</summary>
/// <param name="Attempted">Whether the piece was struck at goal at all; a free kick out of range is crossed instead.</param>
/// <param name="IsGoal">Whether the strike went in.</param>
/// <param name="WasSaved">Whether the goalkeeper stopped it.</param>
/// <param name="HitWoodwork">Whether it struck the post or the bar.</param>
/// <param name="TakerId">The player who struck it.</param>
/// <param name="GoalkeeperId">The goalkeeper who faced it, when there was one.</param>
/// <param name="Zone">Where the strike was taken from.</param>
public readonly record struct SetPieceOutcome(
    bool Attempted,
    bool IsGoal,
    bool WasSaved,
    bool HitWoodwork,
    Guid TakerId,
    Guid? GoalkeeperId,
    ShotZone Zone);

/// <summary>
/// Resolves the skill-dependent parts of set pieces: how likely a penalty is to go in, and how a direct free kick
/// fares against a wall (master plan Stage 2).
/// </summary>
/// <remarks>
/// <para>
/// The director resolves outcomes; the simulation that called it owns the clock, the event log, and the
/// bookings, which is what keeps a set piece legible in the event stream the same way an open-play shot
/// is. Nothing here touches the match state directly.
/// </para>
/// <para>
/// Every chance is bounded: a penalty can never be a formality, and a direct free kick can never be
/// impossible. Players are read through their effective skills (<see cref="EffectiveSkill"/>), and the contests
/// use <see cref="EngineRulesV2.ShotContestReference"/>, so a taker's skill against the goalkeeper's counts as
/// much here as it does in an open-play shot.
/// </para>
/// </remarks>
public static class SetPieceDirector
{
    /// <summary>
    /// Computes the chance a penalty goes in: the taker's finishing and composure against the goalkeeper's reflexes.
    /// </summary>
    /// <param name="taker">The penalty taker.</param>
    /// <param name="goalkeeper">The goalkeeper, when the defending side still has one.</param>
    /// <param name="rules">The rules in force.</param>
    /// <returns>The goal chance, in basis points.</returns>
    public static int PenaltyGoalChance(ActiveSlot taker, ActiveSlot? goalkeeper, EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(taker);
        ArgumentNullException.ThrowIfNull(rules);

        var goalChance = rules.PenaltyGoalBasisPoints;

        if (goalkeeper is not null)
        {
            // Composure under pressure against reflexes on the line.
            var contest = ((EffectiveSkill.Hundredths(taker, MatchAttributeName.Finishing, rules)
                    + EffectiveSkill.Hundredths(taker, MatchAttributeName.Composure, rules)) / 2)
                - EffectiveSkill.Hundredths(goalkeeper, MatchAttributeName.Reflexes, rules);

            goalChance += Probability.Swing(
                contest,
                rules.PenaltyQualitySwingBasisPoints,
                rules.ShotContestReference * EffectiveSkill.Scale);
        }

        return Probability.Band(goalChance, rules.PenaltyMinGoalBasisPoints, rules.PenaltyMaxGoalBasisPoints);
    }

    /// <summary>
    /// Simulates a direct free kick against a defensive wall.
    /// </summary>
    /// <param name="taker">The set-piece taker.</param>
    /// <param name="goalkeeper">The goalkeeper, when the defending side still has one.</param>
    /// <param name="attackingX">How far up the pitch the kick is taken from, on the attacking side's own scale.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="random">The generator.</param>
    /// <returns>
    /// The outcome, with <see cref="SetPieceOutcome.Zone"/> holding where the kick was struck from. A kick
    /// from outside shooting range is returned as a non-attempt: the ball is crossed rather than struck,
    /// and the caller resolves what the cross produced.
    /// </returns>
    public static SetPieceOutcome ResolveDirectFreeKick(
        ActiveSlot taker,
        ActiveSlot? goalkeeper,
        int attackingX,
        EngineRulesV2 rules,
        Pcg32 random)
    {
        ArgumentNullException.ThrowIfNull(taker);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(random);

        var takerId = taker.Participant.ParticipantId;
        var keeperId = goalkeeper?.Participant.ParticipantId;

        if (attackingX < rules.FreeKickShootingRangeX || !random.RollBasisPoints(rules.FreeKickAttemptBasisPoints))
        {
            return new SetPieceOutcome(
                Attempted: false,
                IsGoal: false,
                WasSaved: false,
                HitWoodwork: false,
                takerId,
                keeperId,
                Zone: ShotZone.WideLeft);
        }

        var keeperSkill = goalkeeper is null
            ? EffectiveSkill.Scale
            : EffectiveSkill.Hundredths(goalkeeper, MatchAttributeName.Reflexes, rules);

        var contest = EffectiveSkill.Hundredths(taker, MatchAttributeName.SetPieces, rules) - keeperSkill;

        var goalChance = Probability.Band(
            rules.FreeKickGoalBasisPoints
            + Probability.Swing(
                contest,
                rules.FreeKickQualitySwingBasisPoints,
                rules.ShotContestReference * EffectiveSkill.Scale),
            rules.MinShotGoalBasisPoints,
            rules.MaxShotGoalBasisPoints);

        var isGoal = random.RollBasisPoints(goalChance);

        var wasSaved = false;
        var hitWoodwork = false;

        if (!isGoal)
        {
            // A free kick beats the keeper or the wall or neither: shares of the non-goal outcomes, with the
            // remainder drifting harmlessly wide.
            var roll = random.NextBasisPoints();
            var blockedShare = rules.FreeKickSavedShareBasisPoints + rules.FreeKickBlockedShareBasisPoints;
            var woodworkShare = blockedShare + rules.FreeKickWoodworkShareBasisPoints;

            wasSaved = roll < rules.FreeKickSavedShareBasisPoints;
            hitWoodwork = roll >= blockedShare && roll < woodworkShare;
        }

        return new SetPieceOutcome(Attempted: true, isGoal, wasSaved, hitWoodwork, takerId, keeperId, ShotZone.Central);
    }
}
