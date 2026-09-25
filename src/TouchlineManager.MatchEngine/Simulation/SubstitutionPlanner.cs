using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Ratings;

namespace TouchlineManager.MatchEngine.Simulation;

/// <summary>
/// Makes the engine's substitutions: forced ones for injury, and chosen ones for fatigue (master plan §8.4).
/// </summary>
/// <remarks>
/// <para>
/// Human managers make no live changes in the MVP (`MAT-6`), so every substitution here is the engine's
/// decision. That makes this the one place where a match is coached while it is being played, and its rules
/// are deliberately conservative: an injury is always dealt with, and a tired player is only replaced when
/// the bench offers somebody meaningfully fresher who can actually play the position.
/// </para>
/// <para>
/// The planner never spends a substitution it does not have (`SQ-5`), and it considers its candidates in the
/// snapshot's own bench order with identity as the tie-break, so the same match always makes the same
/// changes.
/// </para>
/// </remarks>
internal static class SubstitutionPlanner
{
    /// <summary>
    /// Considers both sides for a change at the current moment.
    /// </summary>
    /// <param name="state">The match state.</param>
    public static void ConsiderBothSides(MatchState state)
    {
        foreach (var which in new[] { MatchSide.Home, MatchSide.Away })
        {
            ResolveInjuries(state, which);
            ConsiderFatigue(state, which);
        }
    }

    /// <summary>
    /// Replaces injured players, who cannot continue whether or not a substitution is available.
    /// </summary>
    private static void ResolveInjuries(MatchState state, MatchSide which)
    {
        var side = state.SideOf(which);
        var rules = state.Rules;

        // A set has no order, so the injured are dealt with in identity order. Iterating a hash set here
        // would make which injury is treated first depend on hashing, which is exactly the kind of thing that
        // changes a result between runs without changing a formula.
        foreach (var participantId in side.PendingInjurySubstitutions.OrderBy(id => id).ToList())
        {
            side.PendingInjurySubstitutions.Remove(participantId);

            var injured = side.Active.FirstOrDefault(
                slot => slot.Participant.ParticipantId == participantId);

            if (injured is null)
            {
                continue;
            }

            var replacement = side.Substitutions < rules.MaxSubstitutions
                ? BestReplacement(state, side, injured.Slot, rules)
                : null;

            if (replacement is null)
            {
                // Nobody to bring on. The player still cannot continue, so the side finishes a player short.
                side.RemoveParticipant(participantId, state.Minute, rules);

                continue;
            }

            Substitute(state, which, side, injured, replacement, MatchSubstitutionReason.Injury, rules);
        }
    }

    /// <summary>
    /// Replaces the most tired player at a substitution window, when the bench offers a better option.
    /// </summary>
    private static void ConsiderFatigue(MatchState state, MatchSide which)
    {
        var side = state.SideOf(which);
        var rules = state.Rules;

        if (side.Substitutions >= rules.MaxSubstitutions || side.Bench.Count == 0)
        {
            return;
        }

        if (!IsWindow(state))
        {
            return;
        }

        var tired = side.Active
            .Where(slot => slot.Condition.ConditionBasisPoints < rules.ConditionSubstitutionThresholdBasisPoints)
            .OrderBy(slot => slot.Condition.ConditionBasisPoints)
            .ThenBy(slot => slot.Slot.SlotNumber)
            .FirstOrDefault();

        if (tired is null)
        {
            return;
        }

        var replacement = BestReplacement(state, side, tired.Slot, rules);

        if (replacement is null)
        {
            return;
        }

        // Only worth doing if the replacement is meaningfully fresher. Without this, a bench of equally
        // exhausted players would burn all five substitutions on changes that help nobody.
        var advantage = replacement.State.ConditionBasisPoints - tired.Condition.ConditionBasisPoints;

        if (advantage < rules.MinimumConditionAdvantageBasisPoints)
        {
            return;
        }

        Substitute(state, which, side, tired, replacement, MatchSubstitutionReason.Fatigue, rules);
    }

    /// <summary>Gets whether a substitution window fell in the minutes that just elapsed.</summary>
    private static bool IsWindow(MatchState state)
    {
        foreach (var window in state.Rules.SubstitutionWindows)
        {
            if (window > state.LastPlannerMinute && window <= state.Minute)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Chooses the substitute who best suits a slot, from their familiarity with it and their freshness.
    /// </summary>
    /// <remarks>
    /// Familiarity first and freshness second, multiplied rather than weighted: a fresh player who cannot play
    /// the position is not a replacement, and the product says so without a coefficient to argue about.
    /// </remarks>
    private static MatchParticipantV1? BestReplacement(
        MatchState state,
        SideRuntime side,
        MatchSlotV1 slot,
        EngineRulesV1 rules)
    {
        MatchParticipantV1? best = null;
        long bestScore = -1;

        foreach (var candidate in side.Bench)
        {
            var familiarity = LineupResolver.FamiliarityOf(candidate, slot, rules);
            var score = (long)familiarity * candidate.State.ConditionBasisPoints;

            var better = score > bestScore
                || (score == bestScore
                    && best is not null
                    && candidate.ParticipantId.CompareTo(best.ParticipantId) < 0);

            if (better)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private static void Substitute(
        MatchState state,
        MatchSide which,
        SideRuntime side,
        ActiveSlot outgoing,
        MatchParticipantV1 replacement,
        MatchSubstitutionReason reason,
        EngineRulesV1 rules)
    {
        var slotNumber = outgoing.Slot.SlotNumber;

        side.Bench.Remove(replacement);
        side.ReplaceOccupant(slotNumber, replacement, state.Minute, rules);
        side.Substitutions++;

        state.Emit(
            which,
            EngineEventType.Substitution,
            outgoing.Participant.ParticipantId,
            replacement.ParticipantId,
            substitutionReason: reason);

        state.AddSubstitutionStoppage();
    }
}
