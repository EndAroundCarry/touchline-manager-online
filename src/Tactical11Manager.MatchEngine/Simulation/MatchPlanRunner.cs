using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Ratings;

namespace Tactical11Manager.MatchEngine.Simulation;

/// <summary>
/// Carries out a side's match instructions: at a stoppage, and at half-time, it checks each rule the side has not yet used and does what
/// the ones whose conditions hold say (`match-instructions-v1`).
/// </summary>
/// <remarks>
/// <para>
/// A pure function of the match so far: it reads the clock, the score, the cards, the players' condition and live rating, and it draws
/// nothing from the random stream, so a match with a plan is exactly as reproducible as one without.
/// </para>
/// <para>
/// Rules are checked in the manager's order, and each is judged on the match as the rules before it left it (a substitution one
/// rule made is on the pitch when the next is looked at). A rule fires at most once, even when part of what it asks cannot be done: a
/// named player who has gone is skipped, and so is a substitution the side has no substitutes or changes left for. What a rule does is, in
/// this order, its substitutions, its new shape (fitted over the players who are on the pitch by then) and its instruction changes.
/// </para>
/// <para>
/// The runner changes the side's runtime state only. The tick engine brings its team into line afterwards (<c>Stoppage</c>).
/// </para>
/// </remarks>
internal static class MatchPlanRunner
{
    /// <summary>Gets whether either side has a rule to carry out.</summary>
    /// <param name="state">The match state.</param>
    public static bool HasRules(MatchState state) =>
        state.Input.Home.MatchPlan.Rules.Count > 0 || state.Input.Away.MatchPlan.Rules.Count > 0;

    /// <summary>Considers both sides' rules, the home side first.</summary>
    /// <param name="state">The match state.</param>
    public static void ConsiderBothSides(MatchState state)
    {
        Consider(state, MatchSide.Home);
        Consider(state, MatchSide.Away);
    }

    /// <summary>Considers one side's rules at the current moment.</summary>
    /// <param name="state">The match state.</param>
    /// <param name="which">The side.</param>
    public static void Consider(MatchState state, MatchSide which)
    {
        var plan = state.Input.SideOf(which).MatchPlan;

        if (plan.Rules.Count == 0)
        {
            return;
        }

        var side = state.SideOf(which);

        foreach (var rule in plan.Rules)
        {
            if (side.FiredRules.Contains(rule.Ordinal) || !Holds(state, which, side, rule, out var subject))
            {
                continue;
            }

            side.FiredRules.Add(rule.Ordinal);
            Apply(state, which, side, rule, subject);
        }
    }

    /// <summary>
    /// Tells whether a rule's conditions all hold now.
    /// </summary>
    /// <param name="state">The match state.</param>
    /// <param name="which">The side.</param>
    /// <param name="side">The side's runtime state.</param>
    /// <param name="rule">The rule.</param>
    /// <param name="subject">The player the rule's event is about, or null when it is about nobody.</param>
    private static bool Holds(MatchState state, MatchSide which, SideRuntime side, MatchRuleV1 rule, out Guid? subject)
    {
        subject = null;

        var minute = state.Minute;

        if (minute < rule.FromMinute || (rule.UntilMinute is { } until && minute > until))
        {
            return false;
        }

        if (!ScoreHolds(state, which, rule))
        {
            return false;
        }

        var opponent = state.OpponentOf(which);

        switch (rule.Event)
        {
            case MatchRuleEvent.None:
                return true;

            case MatchRuleEvent.OwnSentOff:
                return side.SentOff.Count > 0;

            case MatchRuleEvent.OpponentSentOff:
                return opponent.SentOff.Count > 0;

            case MatchRuleEvent.OpponentMentalityAtLeast:
                return (int)opponent.Instructions.Mentality >= rule.EventThreshold!.Value;

            case MatchRuleEvent.OpponentMentalityAtMost:
                return (int)opponent.Instructions.Mentality <= rule.EventThreshold!.Value;

            case MatchRuleEvent.OwnBooked:
                subject = Lowest(
                    Candidates(side, rule.EventFamily).Where(slot => side.Yellows.TryGetValue(slot.Participant.ParticipantId, out var cards) && cards > 0),
                    static slot => slot.LiveRatingBasisPoints);

                return subject is not null;

            case MatchRuleEvent.PlayingBadly:
                subject = Lowest(
                    Candidates(side, rule.EventFamily).Where(slot => rule.EventThreshold is not { } threshold || slot.LiveRatingBasisPoints < threshold),
                    static slot => slot.LiveRatingBasisPoints);

                return subject is not null;

            case MatchRuleEvent.Tired:
                subject = Lowest(
                    Candidates(side, rule.EventFamily).Where(slot => slot.Condition.ConditionBasisPoints < rule.EventThreshold!.Value),
                    static slot => slot.Condition.ConditionBasisPoints);

                return subject is not null;

            default:
                return false;
        }
    }

    private static bool ScoreHolds(MatchState state, MatchSide which, MatchRuleV1 rule)
    {
        if (rule.Score == MatchRuleScore.Any)
        {
            return true;
        }

        var lead = state.GoalsOf(which) - state.GoalsOf(MatchInputV1.OpponentOf(which));

        return rule.Score switch
        {
            MatchRuleScore.Leading => lead >= rule.ScoreMargin,
            MatchRuleScore.Level => lead == 0,
            MatchRuleScore.Trailing => -lead >= rule.ScoreMargin,
            MatchRuleScore.NotWinning => lead <= 0,
            MatchRuleScore.NotLosing => lead >= 0,
            _ => false,
        };
    }

    /// <summary>Gets the players on the pitch a family narrows to: all the outfield players when no family is named.</summary>
    private static IEnumerable<ActiveSlot> Candidates(SideRuntime side, MatchPositionFamily? family) =>
        side.Active.Where(slot => family is { } named ? slot.Slot.Family == named : slot.Slot.Family != MatchPositionFamily.Goalkeeper);

    /// <summary>Gets the player with the smallest measure, the lower slot number deciding a tie, or null when there are none.</summary>
    private static Guid? Lowest(IEnumerable<ActiveSlot> players, Func<ActiveSlot, int> measure) =>
        players.OrderBy(measure).ThenBy(slot => slot.Slot.SlotNumber).FirstOrDefault()?.Participant.ParticipantId;

    private static void Apply(MatchState state, MatchSide which, SideRuntime side, MatchRuleV1 rule, Guid? subject)
    {
        foreach (var substitution in rule.Substitutions)
        {
            Substitute(state, which, side, rule.Ordinal, substitution, subject);
        }

        var before = side.Instructions;
        var reshaped = false;

        if (rule.Slots is not null)
        {
            var overrides = rule.Overrides.Count == 0
                ? null
                : rule.Overrides.ToDictionary(placement => placement.ParticipantId, placement => placement.SlotNumber);

            side.Reshape(rule.Slots, ShapeFit.Fit(side.Active, rule.Slots, overrides, state.Rules), state.Rules);
            reshaped = true;
        }

        var after = rule.Instructions is { } changes ? changes.ApplyTo(before) : before;
        var restyled = after != before;

        if (restyled)
        {
            side.ChangeInstructions(after, state.Rules);
        }

        if (reshaped || restyled)
        {
            state.Emit(
                which,
                EngineEventType.TacticalChange,
                formationCode: reshaped ? rule.FormationCode : null,
                mentality: after.Mentality != before.Mentality ? after.Mentality : null,
                ruleOrdinal: rule.Ordinal,
                instructionChanges: ChangedInstructions(before, after));
        }
    }

    /// <summary>
    /// Names the instructions, apart from the mentality, that differ between two sets as <c>name=value</c> pairs joined by <c>;</c>, in a fixed
    /// order, or null when none do. What the commentary says a tactical change did.
    /// </summary>
    internal static string? ChangedInstructions(MatchInstructionsV1 before, MatchInstructionsV1 after)
    {
        var changed = new List<string>();

        void Note<T>(string name, T was, T now)
            where T : notnull
        {
            if (!EqualityComparer<T>.Default.Equals(was, now))
            {
                changed.Add($"{name}={now}");
            }
        }

        Note("tempo", before.Tempo, after.Tempo);
        Note("passing", before.Passing, after.Passing);
        Note("width", before.Width, after.Width);
        Note("passFocus", before.PassFocus, after.PassFocus);
        Note("pressing", before.Pressing, after.Pressing);
        Note("defensiveLine", before.DefensiveLine, after.DefensiveLine);
        Note("tackling", before.Tackling, after.Tackling);
        Note("timeWasting", before.TimeWasting, after.TimeWasting);
        Note("counterAttack", before.CounterAttack, after.CounterAttack);

        return changed.Count == 0 ? null : string.Join(';', changed);
    }

    /// <summary>Makes one of a rule's substitutions, or leaves it out when nobody can be taken off or brought on.</summary>
    private static void Substitute(MatchState state, MatchSide which, SideRuntime side, int ruleOrdinal, MatchRuleSubstitutionV1 substitution, Guid? subject)
    {
        var rules = state.Rules;

        if (side.Substitutions >= rules.MaxSubstitutions || side.Bench.Count == 0)
        {
            return;
        }

        var outgoing = substitution.Off switch
        {
            MatchRuleOff.Player => OnPitch(side, substitution.OffParticipantId),
            MatchRuleOff.Subject => OnPitch(side, subject),
            MatchRuleOff.MostTiredInFamily => OnPitch(
                side,
                Lowest(Candidates(side, substitution.OffFamily), static slot => slot.Condition.ConditionBasisPoints)),
            MatchRuleOff.LowestRatedInFamily => OnPitch(
                side,
                Lowest(Candidates(side, substitution.OffFamily), static slot => slot.LiveRatingBasisPoints)),
            _ => null,
        };

        if (outgoing is null)
        {
            return;
        }

        var replacement = substitution.On == MatchRuleOn.Player
            ? side.Bench.FirstOrDefault(candidate => candidate.ParticipantId == substitution.OnParticipantId)
            : SubstitutionPlanner.BestReplacement(side, outgoing.Slot, rules, substitution.OnFamily);

        if (replacement is null)
        {
            return;
        }

        SubstitutionPlanner.Substitute(state, which, side, outgoing, replacement, MatchSubstitutionReason.Tactical, rules, ruleOrdinal);
    }

    private static ActiveSlot? OnPitch(SideRuntime side, Guid? participantId) =>
        participantId is null
            ? null
            : side.Active.FirstOrDefault(slot => slot.Participant.ParticipantId == participantId);
}
