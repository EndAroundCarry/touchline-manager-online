using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Ratings;

namespace TouchlineManager.MatchEngine.Simulation;

/// <summary>
/// Decides whether a player breaks down, and for how long (master plan §8.4, `DIS-1`).
/// </summary>
/// <remarks>
/// <para>
/// The chance is the baseline scaled by how tired the squad on the pitch is, so a substitution made for
/// fatigue also lowers the side's injury exposure — which is the second reason a manager rotates. A squad
/// left on the pitch at 40% condition all season is a squad that spends the second half of it in the
/// treatment room.
/// </para>
/// <para>
/// The absence is measured in fixtures rather than days (`TRN-12`): the fixture calendar is what actually
/// rules a player out, and a number of days would mean something different in a season played at three
/// matchdays a week than in one played weekly.
/// </para>
/// </remarks>
internal static class InjurySimulator
{
    /// <summary>
    /// Rolls for an injury among the players on both sides, and resolves one if it happens.
    /// </summary>
    /// <param name="state">The match state.</param>
    /// <returns>Whether anybody was injured.</returns>
    public static bool TryResolveInjury(MatchState state)
    {
        var rules = state.Rules;

        // Both sides are candidates, because a player breaks down whether or not their team has the ball.
        var candidates = new List<(SideRuntime Side, MatchSide Which, ActiveSlot Slot)>();

        foreach (var which in new[] { MatchSide.Home, MatchSide.Away })
        {
            foreach (var slot in state.SideOf(which).Active)
            {
                candidates.Add((state.SideOf(which), which, slot));
            }
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        var chance = Probability.Band(
            Probability.Apply(rules.BaseInjuryPerPossessionBasisPoints, AverageFatigueMultiplier(candidates, rules)),
            0,
            rules.MaxInjuryProbabilityBasisPoints);

        if (!state.Random.RollBasisPoints(chance))
        {
            return false;
        }

        var selected = WeightedPick.From(
            candidates,
            candidate => InjuryWeight(candidate.Slot, rules),
            state.Random);

        var (runtime, side, victim) = selected;
        var participantId = victim.Participant.ParticipantId;

        var absence = state.Random.NextRange(rules.MinInjuryAbsenceFixtures, rules.MaxInjuryAbsenceFixtures);

        runtime.AbsenceFixtures[participantId] = absence;
        runtime.PendingInjurySubstitutions.Add(participantId);

        state.Emit(side, EngineEventType.Injury, participantId, absenceFixtures: absence);
        state.AddInjuryStoppage();

        return true;
    }

    /// <summary>
    /// Averages every player on the pitch's fatigue multiplier, so a tired side is likelier to lose somebody.
    /// </summary>
    private static int AverageFatigueMultiplier(
        List<(SideRuntime Side, MatchSide Which, ActiveSlot Slot)> candidates,
        EngineRulesV1 rules)
    {
        long total = 0;

        foreach (var candidate in candidates)
        {
            total += FatigueMultiplier(candidate.Slot, rules);
        }

        return (int)(total / candidates.Count);
    }

    /// <summary>
    /// Weights which player is injured, from how tired they are.
    /// </summary>
    /// <remarks>
    /// Never zero: a fresh player can still pull up, so the floor keeps a fit squad exposed to the ordinary
    /// risk rather than making injury purely a fatigue mechanic.
    /// </remarks>
    private static int InjuryWeight(ActiveSlot slot, EngineRulesV1 rules) => FatigueMultiplier(slot, rules);

    /// <summary>
    /// How much a player's fatigue multiplies the injury chance: one when fresh, the rules' ceiling at full
    /// fatigue.
    /// </summary>
    private static int FatigueMultiplier(ActiveSlot slot, EngineRulesV1 rules)
    {
        var range = rules.FatigueInjuryMultiplierBasisPoints - EngineRulesV1.Certain;

        return EngineRulesV1.Certain
            + (int)(((long)range * slot.Condition.FatigueBasisPoints) / EngineRulesV1.Certain);
    }
}
