using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;
using TouchlineManager.MatchEngine.Ratings;

namespace TouchlineManager.MatchEngine.Simulation;

/// <summary>
/// A side's mutable state during its match: who is on the pitch, who is left, and what has happened to them.
/// </summary>
/// <remarks>
/// The thing the simulation mutates. Occupancy, condition, cards, goals, and minutes all live here rather
/// than being recomputed from the event stream, because the simulation needs the current answer on every
/// possession; the counters that only need a final answer — shots, corners, fouls — are derived from the
/// events at the end instead, so the score and the statistics cannot disagree (MAT-5).
/// </remarks>
internal sealed class SideRuntime
{
    /// <summary>Gets which end the side plays.</summary>
    public required MatchSide Which { get; init; }

    /// <summary>Gets the resolved starting lineup.</summary>
    public required MatchLineup Lineup { get; init; }

    /// <summary>Gets the players currently on the pitch, in slot-number order.</summary>
    public List<ActiveSlot> Active { get; init; } = [];

    /// <summary>Gets the substitutes who have not yet come on.</summary>
    public List<MatchParticipantV1> Bench { get; init; } = [];

    /// <summary>Gets yellow cards, by participant.</summary>
    public Dictionary<Guid, int> Yellows { get; } = [];

    /// <summary>Gets the players who have been sent off.</summary>
    public HashSet<Guid> SentOff { get; } = [];

    /// <summary>Gets the minute each participant came on.</summary>
    public Dictionary<Guid, int> EnteredMinute { get; } = [];

    /// <summary>Gets the minute each participant left the pitch permanently.</summary>
    public Dictionary<Guid, int> LeftMinute { get; } = [];

    /// <summary>Gets the goals each participant has scored.</summary>
    public Dictionary<Guid, int> Goals { get; } = [];

    /// <summary>
    /// Gets each participant's morale at kickoff, which is the baseline the scoreline's drift is measured
    /// from. Without it, "morale may only drift so far" has nothing to be a drift from, and a heavy defeat
    /// would walk morale down to the floor over ninety minutes.
    /// </summary>
    public Dictionary<Guid, int> MoraleBaseline { get; } = [];

    /// <summary>Gets how many fixtures each injured participant will miss.</summary>
    public Dictionary<Guid, int> AbsenceFixtures { get; } = [];

    /// <summary>
    /// Gets the injured players who are waiting to be replaced. An injury forces a substitution, but whether
    /// one can be made is the planner's decision, so the injury only records the obligation.
    /// </summary>
    public HashSet<Guid> PendingInjurySubstitutions { get; } = [];

    /// <summary>Gets how many substitutions the side has made.</summary>
    public int Substitutions { get; set; }

    /// <summary>Gets how many seconds of possession the side has had.</summary>
    public int PossessionSeconds { get; set; }

    /// <summary>Gets the side's current unit ratings.</summary>
    public MatchUnitRatings Ratings { get; set; } = MatchUnitRatings.Neutral;

    /// <summary>Gets the club's identity.</summary>
    public Guid ClubId => Lineup.ClubId;

    /// <summary>Gets the club's name.</summary>
    public string ClubName => Lineup.ClubName;

    /// <summary>Gets the side's instructions.</summary>
    public MatchInstructionsV1 Instructions => Lineup.Instructions;

    /// <summary>Gets the players on the pitch who can be given the ball.</summary>
    public IReadOnlyList<ActiveSlot> Outfield =>
        [.. Active.Where(slot => slot.Slot.Family != MatchPositionFamily.Goalkeeper)];

    /// <summary>Gets the player in goal, or null when a sending-off has left nobody there.</summary>
    public ActiveSlot? Goalkeeper =>
        Active.FirstOrDefault(slot => slot.Slot.Family == MatchPositionFamily.Goalkeeper);

    /// <summary>Recalculates the side's ratings from its current occupancy and condition.</summary>
    /// <param name="rules">The rules in force.</param>
    public void RecalculateRatings(EngineRulesV1 rules) =>
        Ratings = UnitRatingCalculator.Calculate(
            Active,
            Instructions,
            Which == MatchSide.Home,
            rules);

    /// <summary>Replaces the player in a slot, keeping the slot's role and familiarity recalculated.</summary>
    /// <param name="slotNumber">The slot to change.</param>
    /// <param name="replacement">The player coming on.</param>
    /// <param name="minute">The minute the change happens.</param>
    /// <param name="rules">The rules in force.</param>
    public void ReplaceOccupant(int slotNumber, MatchParticipantV1 replacement, int minute, EngineRulesV1 rules)
    {
        ArgumentNullException.ThrowIfNull(replacement);

        for (var index = 0; index < Active.Count; index++)
        {
            if (Active[index].Slot.SlotNumber != slotNumber)
            {
                continue;
            }

            var outgoing = Active[index].Participant;

            LeftMinute[outgoing.ParticipantId] = minute;
            EnteredMinute.TryAdd(replacement.ParticipantId, minute);

            Active[index] = new ActiveSlot
            {
                Slot = Active[index].Slot,
                Participant = replacement,
                FamiliarityBasisPoints = LineupResolver.FamiliarityOf(replacement, Active[index].Slot, rules),
                Condition = PlayerCondition.From(replacement.State),
            };

            RecalculateRatings(rules);

            return;
        }

        throw new InvalidOperationException($"The side has no slot {slotNumber} to replace.");
    }

    /// <summary>
    /// Takes a player off the pitch without a replacement, which is what a sending-off does.
    /// </summary>
    /// <param name="participantId">The player leaving.</param>
    /// <param name="minute">The minute it happens.</param>
    /// <param name="rules">The rules in force.</param>
    /// <returns>Whether the player was on the pitch.</returns>
    public bool RemoveParticipant(Guid participantId, int minute, EngineRulesV1 rules)
    {
        var index = Active.FindIndex(slot => slot.Participant.ParticipantId == participantId);

        if (index < 0)
        {
            return false;
        }

        Active.RemoveAt(index);
        LeftMinute[participantId] = minute;
        RecalculateRatings(rules);

        return true;
    }

    /// <summary>Records that a player was booked.</summary>
    /// <param name="participantId">The player.</param>
    /// <returns>Whether this was their second booking, which sends them off.</returns>
    public bool Book(Guid participantId)
    {
        Yellows.TryGetValue(participantId, out var count);
        Yellows[participantId] = count + 1;

        return count + 1 >= 2;
    }

    /// <summary>
    /// Shifts every player on the pitch's morale by a signed amount, bounded by how far it may drift from
    /// its kickoff value.
    /// </summary>
    /// <param name="delta">The shift, which may be negative.</param>
    /// <param name="maxDriftBasisPoints">How far morale may move from its baseline.</param>
    public void ShiftMorale(int delta, int maxDriftBasisPoints)
    {
        for (var index = 0; index < Active.Count; index++)
        {
            var slot = Active[index];
            var baseline = MoraleBaseline.TryGetValue(slot.Participant.ParticipantId, out var recorded)
                ? recorded
                : slot.Condition.MoraleBasisPoints;

            var target = int.Clamp(
                slot.Condition.MoraleBasisPoints + delta,
                baseline - maxDriftBasisPoints,
                baseline + maxDriftBasisPoints);

            Active[index] = slot with
            {
                Condition = new PlayerCondition(
                    slot.Condition.ConditionBasisPoints,
                    slot.Condition.FatigueBasisPoints,
                    target,
                    slot.Condition.SharpnessBasisPoints),
            };
        }
    }

    /// <summary>
    /// Applies the per-possession load to every player on the pitch: condition spent, fatigue gained, and
    /// sharpness earned.
    /// </summary>
    /// <remarks>
    /// The load is the side's own instructions' doing, so a side that presses high and plays fast tires
    /// faster — which is the cost side of `INS-9` and the reason a manager has to rotate a squad rather
    /// than pick the same eleven every week.
    /// </remarks>
    /// <param name="rules">The rules in force.</param>
    public void ApplyLoad(EngineRulesV1 rules)
    {
        var conditionLoss = rules.ConditionLossPerPossessionBasisPoints;

        conditionLoss = Instructions.Tempo switch
        {
            MatchTempo.High => Probability.Apply(conditionLoss, rules.HighTempoConditionLossMultiplierBasisPoints),
            MatchTempo.Low => Probability.Apply(conditionLoss, rules.LowTempoConditionLossMultiplierBasisPoints),
            _ => conditionLoss,
        };

        conditionLoss = Instructions.Pressing switch
        {
            MatchPressing.HighPress =>
                Probability.Apply(conditionLoss, rules.HighPressConditionLossMultiplierBasisPoints),
            MatchPressing.LowBlock =>
                Probability.Apply(conditionLoss, rules.LowBlockConditionLossMultiplierBasisPoints),
            _ => conditionLoss,
        };

        var fatigueGain = rules.FatigueGainPerPossessionBasisPoints;

        for (var index = 0; index < Active.Count; index++)
        {
            var slot = Active[index];

            Active[index] = slot with
            {
                Condition = slot.Condition
                    .WithConditionDelta(-conditionLoss)
                    .WithFatigueDelta(fatigueGain)
                    .WithSharpnessDelta(rules.SharpnessGainPerPossessionBasisPoints),
            };
        }
    }

    /// <summary>Applies the half-time recovery to every player on the pitch.</summary>
    /// <param name="rules">The rules in force.</param>
    public void ApplyHalfTimeRecovery(EngineRulesV1 rules)
    {
        for (var index = 0; index < Active.Count; index++)
        {
            var slot = Active[index];

            Active[index] = slot with
            {
                Condition = slot.Condition
                    .WithConditionDelta(rules.HalfTimeConditionRecoveryBasisPoints)
                    .WithFatigueDelta(-rules.HalfTimeFatigueRecoveryBasisPoints),
            };
        }
    }
}

/// <summary>
/// Chooses an element from a weighted list in a fixed order.
/// </summary>
/// <remarks>
/// The selection weights are the attributes that make a player likely to be the one involved — finishing for
/// a shooter, aggression for a fouler — so the simulation's most important choices are about who a player is
/// rather than a flat draw. Walking the caller's list in its own order, rather than a dictionary's, is what
/// keeps the choice reproducible: an unordered iteration here would change results between runs on the same
/// seed without changing a single formula.
/// </remarks>
internal static class WeightedPick
{
    /// <summary>Picks one item, weighted, consuming exactly one draw whenever the list is not empty.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">The candidates, in a caller-fixed order.</param>
    /// <param name="weightOf">The selection weight of each candidate.</param>
    /// <param name="random">The generator.</param>
    /// <returns>The chosen item, or <see langword="null"/> when there are none.</returns>
    public static T? From<T>(IReadOnlyList<T> items, Func<T, int> weightOf, Pcg32 random)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(weightOf);
        ArgumentNullException.ThrowIfNull(random);

        if (items.Count == 0)
        {
            return default;
        }

        long total = 0;

        foreach (var item in items)
        {
            total += Math.Max(1, weightOf(item));
        }

        var draw = random.NextInt((int)Math.Min(total, int.MaxValue));

        long running = 0;

        foreach (var item in items)
        {
            running += Math.Max(1, weightOf(item));

            if (draw < running)
            {
                return item;
            }
        }

        return items[^1];
    }
}
