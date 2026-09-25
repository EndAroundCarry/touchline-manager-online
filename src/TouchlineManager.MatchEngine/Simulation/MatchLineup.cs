using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Simulation;

/// <summary>
/// One slot as the match reads it: the frozen slot, who occupies it, and how at home that player is there.
/// </summary>
/// <remarks>
/// Familiarity is resolved once, when the lineup is read, rather than recomputed wherever a rating is
/// asked for. It is a single definition of `INS-10`'s out-of-position penalty, so the rating calculator
/// and the cohesion component cannot disagree about who is makeshift.
/// </remarks>
public sealed record ResolvedSlot
{
    /// <summary>Gets the frozen slot.</summary>
    public required MatchSlotV1 Slot { get; init; }

    /// <summary>Gets the player occupying it at kickoff.</summary>
    public required MatchParticipantV1 Participant { get; init; }

    /// <summary>Gets how well the player suits the slot, as a basis-point multiplier (`INS-10`).</summary>
    public required int FamiliarityBasisPoints { get; init; }
}

/// <summary>
/// A side's starting lineup, resolved from the frozen squad and slots.
/// </summary>
/// <remarks>
/// The lineup is the bridge between the input snapshot's two descriptions of a side — a squad and eleven
/// slots — and everything the simulation does with them. Who is on the pitch changes during the match, so
/// this records only the eleven that started; the running occupancy lives in the match state, and a
/// substitution or a sending-off changes that rather than this.
/// </remarks>
public sealed class MatchLineup
{
    /// <summary>Gets which end the side plays.</summary>
    public required MatchSide Which { get; init; }

    /// <summary>Gets the club's identity.</summary>
    public required Guid ClubId { get; init; }

    /// <summary>Gets the club's name.</summary>
    public required string ClubName { get; init; }

    /// <summary>Gets the instructions the side takes into the match.</summary>
    public required MatchInstructionsV1 Instructions { get; init; }

    /// <summary>Gets the eleven slots, in slot-number order.</summary>
    public required IReadOnlyList<ResolvedSlot> Slots { get; init; }

    /// <summary>Gets the substitutes, in the order the snapshot named them.</summary>
    public required IReadOnlyList<MatchParticipantV1> Bench { get; init; }
}

/// <summary>
/// Resolves a frozen side into a starting lineup and its role familiarity.
/// </summary>
public static class LineupResolver
{
    /// <summary>Resolves the side's eleven slots, occupant by occupant.</summary>
    /// <param name="side">The frozen side.</param>
    /// <param name="which">Which end the side plays.</param>
    /// <param name="rules">The rules in force, which supply the familiarity penalties.</param>
    /// <returns>The resolved lineup.</returns>
    public static MatchLineup Resolve(MatchSideV1 side, MatchSide which, EngineRulesV1 rules)
    {
        ArgumentNullException.ThrowIfNull(side);
        ArgumentNullException.ThrowIfNull(rules);

        var byId = side.Squad.ToDictionary(participant => participant.ParticipantId);

        var starters = side.Slots
            .OrderBy(slot => slot.SlotNumber)
            .Select(slot => new ResolvedSlot
            {
                Slot = slot,
                Participant = byId[slot.ParticipantId],
                FamiliarityBasisPoints = FamiliarityOf(byId[slot.ParticipantId], slot, rules),
            })
            .ToList();

        var starting = starters.Select(resolved => resolved.Participant.ParticipantId).ToHashSet();

        var bench = side.Squad
            .Where(participant => !starting.Contains(participant.ParticipantId))
            .ToList();

        return new MatchLineup
        {
            Which = which,
            ClubId = side.ClubId,
            ClubName = side.ClubName,
            Instructions = side.Instructions,
            Slots = starters,
            Bench = bench,
        };
    }

    /// <summary>
    /// Grades how well a player suits a slot, as a basis-point multiplier (`INS-10`).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three grades, and the middle one is the interesting one. A player whose own position is one the role
    /// naturally asks for is fully effective. A player who is in the right band but the wrong job — a
    /// centre back at full back, a winger at striker — takes the unfamiliar-role penalty. A player
    /// deployed outside their band entirely takes the out-of-position penalty, unless one of their
    /// secondary positions covers it, which is what secondary positions are for.
    /// </para>
    /// <para>
    /// Out of position is a penalty and never a refusal: `INS-10` makes a makeshift side a legitimate
    /// choice that the engine prices, which is why the tactics validator accepts it and this function
    /// merely discounts it.
    /// </para>
    /// </remarks>
    /// <param name="participant">The player.</param>
    /// <param name="slot">The slot they occupy.</param>
    /// <param name="rules">The rules in force.</param>
    public static int FamiliarityOf(
        MatchParticipantV1 participant,
        MatchSlotV1 slot,
        EngineRulesV1 rules)
    {
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(rules);

        if (participant.Position.FamilyOf() == slot.Family)
        {
            return NaturalPositionsOf(slot.Role).Contains(participant.Position)
                ? EngineRulesV1.Certain
                : rules.UnfamiliarRolePenaltyBasisPoints;
        }

        if (participant.SecondaryPositions.Any(position => position.FamilyOf() == slot.Family))
        {
            return rules.SecondaryPositionPenaltyBasisPoints;
        }

        return rules.OutOfPositionPenaltyBasisPoints;
    }

    /// <summary>
    /// Gets the positions a role naturally asks for, which is what separates "in role" from "out of
    /// position within the same band".
    /// </summary>
    /// <param name="role">The role.</param>
    public static IReadOnlyList<MatchPosition> NaturalPositionsOf(MatchRole role) => role switch
    {
        MatchRole.Goalkeeper => [MatchPosition.Goalkeeper],
        MatchRole.CentreBack => [MatchPosition.CentreBack],
        MatchRole.FullBack => [MatchPosition.RightBack, MatchPosition.LeftBack],
        MatchRole.WingBack =>
            [MatchPosition.RightBack, MatchPosition.LeftBack, MatchPosition.RightWinger, MatchPosition.LeftWinger],
        MatchRole.DefensiveMidfielder => [MatchPosition.DefensiveMidfielder, MatchPosition.CentralMidfielder],
        MatchRole.CentralMidfielder =>
            [MatchPosition.CentralMidfielder, MatchPosition.DefensiveMidfielder, MatchPosition.AttackingMidfielder],
        MatchRole.AttackingMidfielder => [MatchPosition.AttackingMidfielder, MatchPosition.CentralMidfielder],
        MatchRole.Winger =>
            [MatchPosition.RightWinger, MatchPosition.LeftWinger, MatchPosition.AttackingMidfielder],
        MatchRole.Striker => [MatchPosition.Striker, MatchPosition.AttackingMidfielder],
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role."),
    };
}
