using Tactical11Manager.MatchEngine.Configuration;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Ratings;

namespace Tactical11Manager.MatchEngine.Simulation;

/// <summary>
/// Makes the engine's substitutions: forced ones for injury, and chosen ones for fatigue (master plan §8.4).
/// </summary>
/// <remarks>
/// <para>
/// A manager makes no live changes (`MAT-6`): what he decides beforehand is a match plan, and <see cref="MatchPlanRunner"/> carries
/// it out (in the tick engine). The substitutions made here are the assistant's, and its rules are deliberately conservative: an
/// injury is always dealt with, and a tired player is only replaced when the bench offers somebody meaningfully fresher who can
/// actually play the position.
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
    /// <remarks>
    /// The order for each side is: injuries, which are always dealt with first; then, in the tick engine, the side's match rules
    /// (<see cref="MatchPlanRunner"/>); then the assistant's fatigue substitution, unless the side's plan has switched it off. The
    /// possession engine ignores match plans, so it never runs the rules and its assistant is always on.
    /// </remarks>
    /// <param name="state">The match state.</param>
    /// <param name="applyMatchPlans">Whether the sides' match plans are carried out: the tick engine's stoppages say so.</param>
    public static void ConsiderBothSides(MatchState state, bool applyMatchPlans = false)
    {
        foreach (var which in new[] { MatchSide.Home, MatchSide.Away })
        {
            ResolveInjuries(state, which);

            if (applyMatchPlans)
            {
                MatchPlanRunner.Consider(state, which);
            }

            if (!applyMatchPlans || state.Input.SideOf(which).MatchPlan.AssistantFatigueSubstitutions)
            {
                ConsiderFatigue(state, which);
            }
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
                ? BestReplacement(side, injured.Slot, rules)
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

        var replacement = BestReplacement(side, tired.Slot, rules);

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
    /// <param name="side">The side whose bench is looked at.</param>
    /// <param name="slot">The slot the substitute is to fill.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="family">The family the substitute must naturally belong to, or null for any (a match rule asks for a forward).</param>
    internal static MatchParticipantV1? BestReplacement(
        SideRuntime side,
        MatchSlotV1 slot,
        EngineRulesV2 rules,
        MatchPositionFamily? family = null)
    {
        MatchParticipantV1? best = null;
        long bestScore = -1;

        foreach (var candidate in side.Bench)
        {
            if (family is { } wanted && candidate.Position.FamilyOf() != wanted)
            {
                continue;
            }

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

    /// <summary>
    /// Takes a player off and brings a substitute on in his slot, counts the change against the side's five and records it. The one
    /// place a substitution is made, whoever decided on it: an injury, the assistant's fatigue rule, or a match rule (`Tactical`).
    /// </summary>
    /// <param name="state">The match state.</param>
    /// <param name="which">The side.</param>
    /// <param name="side">The side's runtime state.</param>
    /// <param name="outgoing">The player coming off.</param>
    /// <param name="replacement">The substitute, who must be on the side's bench.</param>
    /// <param name="reason">Why the change is made.</param>
    /// <param name="rules">The rules in force.</param>
    internal static void Substitute(
        MatchState state,
        MatchSide which,
        SideRuntime side,
        ActiveSlot outgoing,
        MatchParticipantV1 replacement,
        MatchSubstitutionReason reason,
        EngineRulesV2 rules)
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
