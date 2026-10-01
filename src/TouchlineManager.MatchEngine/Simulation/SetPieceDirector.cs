using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;
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
/// Simulates set pieces: penalties with the keeper's dive, direct free kicks against a wall, and the
/// contested headers a corner produces (master plan Stage 2).
/// </summary>
/// <remarks>
/// <para>
/// The director resolves outcomes; the simulation that called it owns the clock, the event log, and the
/// bookings, which is what keeps a set piece legible in the event stream the same way an open-play shot
/// is. Nothing here touches the match state directly.
/// </para>
/// <para>
/// Every chance is bounded: a penalty can never be a formality, and a direct free kick can never be
/// impossible. The goalkeeper is read on the attribute scale, like every other contest in the engine, so
/// a great keeper shows up here the same way they show up in open play.
/// </para>
/// </remarks>
public static class SetPieceDirector
{
    /// <summary>Simulates a penalty kick with the goalkeeper's dive.</summary>
    /// <param name="taker">The penalty taker.</param>
    /// <param name="goalkeeper">The goalkeeper, when the defending side still has one.</param>
    /// <param name="takerIsHome">Whether the taker's side is at home.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="random">The generator.</param>
    public static SetPieceOutcome ResolvePenalty(
        MatchParticipantV1 taker,
        MatchParticipantV1? goalkeeper,
        bool takerIsHome,
        EngineRulesV2 rules,
        Pcg32 random)
    {
        ArgumentNullException.ThrowIfNull(taker);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(random);

        var goalChance = rules.PenaltyGoalBasisPoints;

        if (goalkeeper is not null)
        {
            // Composure under pressure against reflexes on the line, on the attribute scale.
            var contest = ((taker.Attributes.ValueOf(MatchAttributeName.Finishing)
                    + taker.Attributes.ValueOf(MatchAttributeName.Composure)) / 2)
                - goalkeeper.Attributes.ValueOf(MatchAttributeName.Reflexes);

            goalChance += Probability.Swing(contest, rules.ShotQualitySwingBasisPoints / 2, rules.RatingDifferentialReference);
        }

        goalChance = Probability.Band(goalChance, 5_500, 9_400);

        var isGoal = random.RollBasisPoints(goalChance);
        var wasSaved = false;
        var hitWoodwork = false;

        if (!isGoal)
        {
            // Most penalties that do not go in are stopped; the rest are missed or strike the frame.
            var roll = random.NextBasisPoints();

            if (roll < rules.BaseSaveBasisPoints)
            {
                wasSaved = true;
            }
            else if (roll < rules.BaseSaveBasisPoints + rules.WoodworkShareBasisPoints)
            {
                hitWoodwork = true;
            }
        }

        return new SetPieceOutcome(Attempted: true, isGoal, wasSaved, hitWoodwork, taker.ParticipantId, goalkeeper?.ParticipantId, ShotZone.Central);
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
        MatchParticipantV1 taker,
        MatchParticipantV1? goalkeeper,
        int attackingX,
        bool takerIsHome,
        EngineRulesV2 rules,
        Pcg32 random)
    {
        ArgumentNullException.ThrowIfNull(taker);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(random);

        if (attackingX < rules.FreeKickShootingRangeX || !random.RollBasisPoints(rules.FreeKickAttemptBasisPoints))
        {
            return new SetPieceOutcome(
                Attempted: false,
                IsGoal: false,
                WasSaved: false,
                HitWoodwork: false,
                taker.ParticipantId,
                goalkeeper?.ParticipantId,
                Zone: ShotZone.WideLeft);
        }

        var contest = taker.Attributes.ValueOf(MatchAttributeName.SetPieces)
            - (goalkeeper is null ? 1 : goalkeeper.Attributes.ValueOf(MatchAttributeName.Reflexes));

        var goalChance = Probability.Band(
            rules.FreeKickGoalBasisPoints
            + Probability.Swing(contest, rules.FreeKickQualitySwingBasisPoints, rules.RatingDifferentialReference),
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

        return new SetPieceOutcome(Attempted: true, isGoal, wasSaved, hitWoodwork, taker.ParticipantId, goalkeeper?.ParticipantId, ShotZone.Central);
    }

    /// <summary>
    /// Resolves the contested header a corner delivers: whether the attacker beats the defender to it.
    /// </summary>
    /// <param name="attacker">The attacking runner.</param>
    /// <param name="defender">The defender marking them.</param>
    /// <param name="attackerIsHome">Whether the attacking side is at home.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="random">The generator.</param>
    /// <returns>Whether the attacker won the header and the corner became a chance.</returns>
    public static bool ResolveCornerHeader(
        MatchParticipantV1 attacker,
        MatchParticipantV1 defender,
        bool attackerIsHome,
        EngineRulesV2 rules,
        Pcg32 random)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(defender);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(random);

        return DuelResolver
            .ResolveAerialDuel(attacker, defender, attackerIsHome, rules, random)
            .AttackerWon;
    }
}
