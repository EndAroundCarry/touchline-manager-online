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

    /// <summary>Gets the goals each participant has set up.</summary>
    /// <remarks>
    /// Kept beside the goals rather than read back from the event stream, which names only the scorer: an
    /// assist is a decision the simulation made when the goal was scored, so it is recorded there.
    /// </remarks>
    public Dictionary<Guid, int> Assists { get; } = [];

    /// <summary>
    /// Gets each participant's morale at kickoff, which is the baseline the scoreline's drift is measured
    /// from. Without it, "morale may only drift so far" has nothing to be a drift from, and a heavy defeat
    /// would walk morale down to the floor over ninety minutes.
    /// </summary>
    public Dictionary<Guid, int> MoraleBaseline { get; } = [];

    /// <summary>Gets how many fixtures each injured participant will miss.</summary>
    public Dictionary<Guid, int> AbsenceFixtures { get; } = [];

    /// <summary>
    /// Gets each participant's condition at the moment they left the match (`engine-v3`).
    /// </summary>
    /// <remarks>
    /// Recorded when a player is substituted or sent off, and for everybody still on the pitch at full
    /// time when the result is built. It is the match center's condition bar and the load calculator's
    /// input, so it is captured once where the fact is known rather than reconstructed later.
    /// </remarks>
    public Dictionary<Guid, int> FinalConditions { get; } = [];

    /// <summary>
    /// Gets the injured players who are waiting to be replaced. An injury forces a substitution, but whether
    /// one can be made is the planner's decision, so the injury only records the obligation.
    /// </summary>
    public HashSet<Guid> PendingInjurySubstitutions { get; } = [];

    /// <summary>
    /// Gets how many players are short on the pitch, which multiplies the condition the load costs.
    /// </summary>
    /// <remarks>
    /// A sending-off, or an injury the bench could not replace, leaves the vacated zone covered by
    /// teammates running further (master plan Stage 2). The simulation keeps the count here so the load
    /// formula stays one formula, and the cost is bounded to the rules' multiplier rather than growing
    /// without limit.
    /// </remarks>
    public int ShorthandedCount => Math.Max(0, MatchInputV1.StartersOnPitch - Active.Count);

    /// <summary>Gets how many substitutions the side has made.</summary>
    public int Substitutions { get; set; }

    /// <summary>Gets how many seconds of possession the side has had.</summary>
    public int PossessionSeconds { get; set; }

    /// <summary>Gets the side's current unit ratings.</summary>
    public MatchUnitRatings Ratings { get; set; } = MatchUnitRatings.Neutral;

    /// <summary>
    /// Gets each participant's live match rating at the moment they left the match (`engine-v3`).
    /// </summary>
    /// <remarks>
    /// Populated by the simulation as players leave the pitch and read when the result is built for those
    /// still on. A bench player who never came on has no entry, which is why the dictionary lookup fails
    /// rather than defaulting: the absence is the fact.
    /// </remarks>
    public Dictionary<Guid, int> LiveRatings { get; } = [];

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

    /// <summary>
    /// Gets or sets whether the side led when its ratings were last refreshed, which is what makes a
    /// situational time-wasting instruction apply (`engine-v6`).
    /// </summary>
    public bool Leading { get; set; }

    /// <summary>Gets whether the side's time-wasting instruction is in force: always when on, only while ahead when situational.</summary>
    public bool WastingTime => Instructions.TimeWasting switch
    {
        MatchTimeWasting.On => true,
        MatchTimeWasting.Situational => Leading,
        _ => false,
    };

    /// <summary>Recalculates the side's ratings from its current occupancy and condition.</summary>
    /// <param name="rules">The rules in force.</param>
    public void RecalculateRatings(EngineRulesV2 rules) =>
        Ratings = UnitRatingCalculator.Calculate(
            Active,
            Instructions.TimeWasting == MatchTimeWasting.Situational && !Leading
                ? Instructions with { TimeWasting = MatchTimeWasting.Off }
                : Instructions,
            Which == MatchSide.Home,
            rules);

    /// <summary>Replaces the player in a slot, keeping the slot's role and familiarity recalculated.</summary>
    /// <param name="slotNumber">The slot to change.</param>
    /// <param name="replacement">The player coming on.</param>
    /// <param name="minute">The minute the change happens.</param>
    /// <param name="rules">The rules in force.</param>
    public void ReplaceOccupant(int slotNumber, MatchParticipantV1 replacement, int minute, EngineRulesV2 rules)
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
            FinalConditions[outgoing.ParticipantId] = Active[index].Condition.ConditionBasisPoints;
            LiveRatings[outgoing.ParticipantId] = Active[index].LiveRatingBasisPoints;
            EnteredMinute.TryAdd(replacement.ParticipantId, minute);

            Active[index] = new ActiveSlot
            {
                Slot = Active[index].Slot,
                Participant = replacement,
                FamiliarityBasisPoints = LineupResolver.FamiliarityOf(replacement, Active[index].Slot, rules),
                Condition = PlayerCondition.From(replacement.State),
                LiveRatingBasisPoints = rules.LiveRatingBaseBasisPoints,
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
    public bool RemoveParticipant(Guid participantId, int minute, EngineRulesV2 rules)
    {
        var index = Active.FindIndex(slot => slot.Participant.ParticipantId == participantId);

        if (index < 0)
        {
            return false;
        }

        FinalConditions[participantId] = Active[index].Condition.ConditionBasisPoints;
        LiveRatings[participantId] = Active[index].LiveRatingBasisPoints;
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
    /// <param name="rules">The rules in force, which say how far the best leader on the pitch changes the shift.</param>
    public void ShiftMorale(int delta, int maxDriftBasisPoints, EngineRulesV2 rules)
    {
        // The best leader on the pitch sharpens a lift and softens a blow (engine-v6), within a bound.
        var bestLeader = 0;

        foreach (var slot in Active)
        {
            bestLeader = Math.Max(bestLeader, slot.Participant.Attributes.ValueOf(MatchAttributeName.Leadership));
        }

        var leadership = int.Clamp(
            EngineRulesV2.Certain + ((bestLeader - rules.LeadershipReference) * rules.LeadershipMoraleStepBasisPoints),
            rules.MinLeadershipMoraleMultiplierBasisPoints,
            rules.MaxLeadershipMoraleMultiplierBasisPoints);

        delta = delta >= 0
            ? Probability.Apply(delta, leadership)
            : -Probability.Apply(-delta, (2 * EngineRulesV2.Certain) - leadership);

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
    /// than pick the same eleven every week. A side playing a man short pays more still: the tactical
    /// imbalance handler's coverage cost, so a red card is felt in the legs as well as on the scoreboard.
    /// </remarks>
    /// <param name="rules">The rules in force.</param>
    public void ApplyLoad(EngineRulesV2 rules)
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

        // Tactical imbalance (master plan Stage 2): covering a teammate's vacated zone costs legs.
        conditionLoss = Probability.Apply(conditionLoss, 10_000 + (ShorthandedCount * (rules.ShorthandedConditionLossMultiplierBasisPoints - 10_000)));

        var fatigueGain = rules.FatigueGainPerPossessionBasisPoints;

        for (var index = 0; index < Active.Count; index++)
        {
            var slot = Active[index];

            // A player's own Stamina sets how fast he tires (engine-v6); the goalkeeper's legs are not modelled.
            var playerLoss = slot.Slot.Family == MatchPositionFamily.Goalkeeper
                ? conditionLoss
                : Probability.Apply(
                    conditionLoss,
                    int.Clamp(
                        EngineRulesV2.Certain
                            - ((slot.Participant.Attributes.ValueOf(MatchAttributeName.Stamina) - rules.StaminaReference)
                                * rules.StaminaConditionLossStepBasisPoints),
                        rules.MinStaminaConditionLossMultiplierBasisPoints,
                        rules.MaxStaminaConditionLossMultiplierBasisPoints));

            Active[index] = slot with
            {
                Condition = slot.Condition
                    .WithConditionDelta(-playerLoss)
                    .WithFatigueDelta(fatigueGain)
                    .WithSharpnessDelta(rules.SharpnessGainPerPossessionBasisPoints),
            };
        }
    }

    /// <summary>Applies the half-time recovery to every player on the pitch.</summary>
    /// <param name="rules">The rules in force.</param>
    public void ApplyHalfTimeRecovery(EngineRulesV2 rules)
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

    /// <summary>
    /// Adjusts one player's live match rating by a signed amount (`engine-v3`).
    /// </summary>
    /// <remarks>
    /// Adjusts in place and ignores players who have left the pitch: a booking given to a player who has
    /// already been substituted belongs to nobody's rating. The clamp keeps a calamitous afternoon inside
    /// the scale the match viewer draws.
    /// </remarks>
    /// <param name="participantId">The player.</param>
    /// <param name="deltaBasisPoints">The change, which may be negative.</param>
    public void AdjustLiveRating(Guid participantId, int deltaBasisPoints)
    {
        for (var index = 0; index < Active.Count; index++)
        {
            if (Active[index].Participant.ParticipantId != participantId)
            {
                continue;
            }

            Active[index] = Active[index].WithLiveRatingDelta(deltaBasisPoints);

            return;
        }
    }

    /// <summary>Adjusts every player on the pitch's live rating by a signed amount.</summary>
    /// <param name="deltaBasisPoints">The change, which may be negative.</param>
    public void AdjustAllLiveRatings(int deltaBasisPoints)
    {
        for (var index = 0; index < Active.Count; index++)
        {
            Active[index] = Active[index].WithLiveRatingDelta(deltaBasisPoints);
        }
    }

    /// <summary>
    /// Records the condition and live rating of everybody still on the pitch at the final whistle.
    /// </summary>
    public void CaptureEndOfMatchStates()
    {
        foreach (var slot in Active)
        {
            FinalConditions[slot.Participant.ParticipantId] = slot.Condition.ConditionBasisPoints;
            LiveRatings[slot.Participant.ParticipantId] = slot.LiveRatingBasisPoints;
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
