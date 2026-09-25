using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Ratings;

namespace TouchlineManager.MatchEngine.Simulation;

/// <summary>
/// Resolves a chance on goal into a goal, a save, a block, the woodwork, or a miss (master plan §8.4).
/// </summary>
/// <remarks>
/// <para>
/// The last step of a possession, and the only place a goal is ever produced — `MAT-4` forbids the
/// independent per-minute roll that would make the score a separate process from the play. Everything here
/// reads ratings that were computed from attributes, so a goal is attributable to a shooter, a goalkeeper,
/// and a position on the pitch rather than to a draw.
/// </para>
/// <para>
/// Order matters and is part of the engine version. The outcome is decided in two stages: first whether the
/// shot beats the goalkeeper, then — for a shot that does not — which way it failed. The second stage draws
/// only when it is reached, so a goal consumes one draw and a miss consumes two, and changing that ordering
/// changes every historical result.
/// </para>
/// </remarks>
internal static class ChanceSimulator
{
    /// <summary>Resolves a chance created from open play.</summary>
    /// <param name="state">The match state.</param>
    /// <param name="side">The side attacking.</param>
    public static void ResolveOpenPlay(MatchState state, MatchSide side)
    {
        var attacker = state.SideOf(side);
        var defender = state.OpponentOf(side);

        var zone = ChooseZone(state);
        var shooter = ChooseShooter(state, attacker, MatchAttributeName.Finishing);

        if (shooter is null)
        {
            return;
        }

        var goalChance = GoalChance(state, defender, shooter, zone, headed: false);

        Resolve(state, side, shooter, zone, goalChance);
    }

    /// <summary>
    /// Resolves a penalty, which is its own event pair: the award, then the outcome.
    /// </summary>
    /// <param name="state">The match state.</param>
    /// <param name="side">The side awarded the penalty.</param>
    public static void ResolvePenalty(MatchState state, MatchSide side)
    {
        var attacker = state.SideOf(side);
        var defender = state.OpponentOf(side);
        var taker = PenaltyTaker(attacker);

        state.Emit(side, EngineEventType.PenaltyAwarded, taker?.Participant.ParticipantId, zone: ShotZone.Central);

        if (taker is null)
        {
            return;
        }

        var goalkeeper = defender.Goalkeeper?.Participant.ParticipantId;

        if (state.Random.RollBasisPoints(state.Rules.PenaltyGoalBasisPoints))
        {
            Score(state, side, taker);
            state.Emit(
                side,
                EngineEventType.PenaltyGoal,
                taker.Participant.ParticipantId,
                goalkeeper,
                ShotZone.Central,
                state.Rules.PenaltyGoalBasisPoints);
        }
        else
        {
            state.Emit(
                side,
                EngineEventType.PenaltyMissed,
                taker.Participant.ParticipantId,
                goalkeeper,
                ShotZone.Central,
                state.Rules.PenaltyGoalBasisPoints);
        }
    }

    /// <summary>Resolves a header from a corner.</summary>
    /// <param name="state">The match state.</param>
    /// <param name="side">The side taking the corner.</param>
    public static void ResolveCorner(MatchState state, MatchSide side)
    {
        var attacker = state.SideOf(side);
        var defender = state.OpponentOf(side);
        var headerer = ChooseShooter(state, attacker, MatchAttributeName.Heading);

        if (headerer is null)
        {
            return;
        }

        var goalChance = GoalChance(state, defender, headerer, ShotZone.Central, headed: true);

        Resolve(state, side, headerer, ShotZone.Central, goalChance);
    }

    private static void Resolve(
        MatchState state,
        MatchSide side,
        ActiveSlot shooter,
        ShotZone zone,
        int goalChance)
    {
        var opponent = state.OpponentOf(side);
        var goalkeeper = opponent.Goalkeeper?.Participant.ParticipantId;
        var shooterId = shooter.Participant.ParticipantId;

        if (state.Random.RollBasisPoints(goalChance))
        {
            Score(state, side, shooter);
            state.Emit(side, EngineEventType.Goal, shooterId, goalkeeper, zone, qualityBasisPoints: goalChance);

            return;
        }

        // Not a goal. The woodwork and a block are decided before the goalkeeper is asked, because a shot
        // that hits the post never reaches them and a shot that is blocked never gets there either.
        var roll = state.Random.NextBasisPoints();
        var woodwork = state.Rules.WoodworkShareBasisPoints;

        if (roll < woodwork)
        {
            state.Emit(side, EngineEventType.Woodwork, shooterId, goalkeeper, zone, qualityBasisPoints: goalChance);

            return;
        }

        if (roll < woodwork + state.Rules.BlockedShareBasisPoints)
        {
            state.Emit(side, EngineEventType.ShotBlocked, shooterId, goalkeeper, zone, qualityBasisPoints: goalChance);

            return;
        }

        var quality = Probability.Differential(
            shooter.Participant.Attributes.ValueOf(MatchAttributeName.Finishing),
            KeeperQuality(opponent, state.Rules));

        var saveChance = Probability.Band(
            state.Rules.BaseSaveBasisPoints
                + Probability.Swing(
                    quality,
                    state.Rules.ShotQualitySwingBasisPoints,
                    state.Rules.RatingDifferentialReference),
            state.Rules.MinSaveBasisPoints,
            state.Rules.MaxSaveBasisPoints);

        if (state.Random.RollBasisPoints(saveChance))
        {
            state.Emit(side, EngineEventType.ShotSaved, shooterId, goalkeeper, zone, qualityBasisPoints: goalChance);
        }
        else
        {
            state.Emit(side, EngineEventType.ShotOffTarget, shooterId, goalkeeper, zone, qualityBasisPoints: goalChance);
        }
    }

    private static void Score(MatchState state, MatchSide side, ActiveSlot scorer)
    {
        var runtime = state.SideOf(side);
        var scorerId = scorer.Participant.ParticipantId;

        runtime.Goals.TryGetValue(scorerId, out var goals);
        runtime.Goals[scorerId] = goals + 1;

        state.AddGoalStoppage();

        // Scoring lifts the scorers and drops the conceders, bounded so a rout cannot empty a side's morale.
        runtime.ShiftMorale(state.Rules.MoraleGainPerGoalBasisPoints, state.Rules.MaxMoraleDriftBasisPoints);
        state
            .OpponentOf(side)
            .ShiftMorale(-state.Rules.MoraleLossPerConcededGoalBasisPoints, state.Rules.MaxMoraleDriftBasisPoints);
    }

    /// <summary>
    /// Computes the goal probability of one shot from the shooter, the goalkeeper, and the position.
    /// </summary>
    private static int GoalChance(
        MatchState state,
        SideRuntime defender,
        ActiveSlot shooter,
        ShotZone zone,
        bool headed)
    {
        ArgumentNullException.ThrowIfNull(defender);

        var rules = state.Rules;

        var zoneMultiplier = zone switch
        {
            ShotZone.Central => rules.CentralZoneMultiplierBasisPoints,
            ShotZone.InsideLeft or ShotZone.InsideRight => rules.InsideZoneMultiplierBasisPoints,
            ShotZone.WideLeft or ShotZone.WideRight => rules.WideZoneMultiplierBasisPoints,
            _ => rules.InsideZoneMultiplierBasisPoints,
        };

        var baseChance = Probability.Apply(rules.BaseShotGoalBasisPoints, zoneMultiplier);

        var contest = headed
            ? Probability.Differential(
                shooter.Participant.Attributes.ValueOf(MatchAttributeName.Heading),
                KeeperQuality(defender, rules))
            : Probability.Differential(
                shooter.Participant.Attributes.ValueOf(MatchAttributeName.Finishing),
                KeeperQuality(defender, rules));

        return Probability.Band(
            baseChance + Probability.Swing(contest, rules.ShotQualitySwingBasisPoints, rules.RatingDifferentialReference),
            rules.MinShotGoalBasisPoints,
            rules.MaxShotGoalBasisPoints);
    }

    /// <summary>
    /// Reads the defending side's goalkeeping as a single attribute-scale value.
    /// </summary>
    /// <remarks>
    /// The Goalkeeping unit rating divided back down to the attribute scale, so it can be compared against a
    /// shooter's own attribute. One measure for every shot is deliberate: the alternative — reading specific
    /// goalkeeper attributes per shot type — would let a headed chance and a placed shot disagree about how
    /// good the same goalkeeper is.
    /// <para>
    /// A side whose goalkeeper has been sent off has no player in the Goalkeeping unit, so this returns the
    /// floor and every shot against them is close to a formality. That is the correct shape: `MAT-6` has no
    /// mechanism for naming a new goalkeeper mid-match, so a side that loses theirs is in trouble.
    /// </para>
    /// </remarks>
    private static int KeeperQuality(SideRuntime defender, EngineRulesV1 rules) =>
        defender.Ratings.Goalkeeping / rules.AttributeRatingFactor;

    /// <summary>Chooses where the shot came from, weighted towards the middle of the pitch.</summary>
    private static ShotZone ChooseZone(MatchState state)
    {
        var roll = state.Random.NextInt(100);

        return roll switch
        {
            < 40 => ShotZone.Central,
            < 60 => ShotZone.InsideLeft,
            < 80 => ShotZone.InsideRight,
            < 90 => ShotZone.WideLeft,
            _ => ShotZone.WideRight,
        };
    }

    /// <summary>
    /// Chooses the shooter, weighted by the attribute the chance asks for.
    /// </summary>
    /// <remarks>
    /// The weight is deliberately the attribute itself rather than a flat draw over the eleven: a side's
    /// best finisher takes more of its shots, which is what makes the Finishing rating mean something. A
    /// goalkeeper is excluded — a goalkeeper taking a shot from open play is not a thing this engine models
    /// — and an attribute of 1 is floored at 1 by the picker so nobody is impossible.
    /// </remarks>
    private static ActiveSlot? ChooseShooter(MatchState state, SideRuntime side, MatchAttributeName attribute)
    {
        var outfield = side.Outfield;

        return WeightedPick.From(
            outfield,
            slot => slot.Participant.Attributes.ValueOf(attribute),
            state.Random);
    }

    /// <summary>
    /// Chooses the penalty taker: the best finisher on the pitch, ties broken by identity.
    /// </summary>
    /// <remarks>
    /// A designated taker rather than a draw, because a penalty shootout's taker is a decision and not a
    /// chance event, and because a stable choice is reproducible without consuming a draw.
    /// </remarks>
    private static ActiveSlot? PenaltyTaker(SideRuntime side) =>
        side.Outfield.Count == 0
            ? null
            : side.Outfield
                .OrderByDescending(slot => slot.Participant.Attributes.ValueOf(MatchAttributeName.Finishing))
                .ThenBy(slot => slot.Participant.ParticipantId)
                .First();
}
