using Tactical11Manager.MatchEngine.Configuration;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Ratings;

namespace Tactical11Manager.MatchEngine.Shootout;

/// <summary>
/// One side's players who are on the pitch when a drawn match ends, ready to take penalties.
/// </summary>
/// <param name="ClubId">The club.</param>
/// <param name="OnPitch">Everyone still on the pitch at full time, goalkeeper included.</param>
public sealed record ShootoutSide(Guid ClubId, IReadOnlyList<ActiveSlot> OnPitch)
{
    /// <summary>
    /// Reads who was on the pitch at the final whistle from a played match's input and player lines.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On the pitch at full time means started or came on, and was not substituted off, sent off, or hurt. An injured
    /// player with no substitute left is not substituted off in the lines (the side simply finishes a player short),
    /// so the injury flag counts as off: the engine takes the player out of play at that moment and never puts them back.
    /// </para>
    /// <para>
    /// The players carry the condition they had at the final whistle and the fatigue, morale and sharpness the match
    /// was locked with. A penalty is not positional, so everyone is read at full familiarity; a starter keeps the slot
    /// he played and a substitute stands in a slot made from his own position.
    /// </para>
    /// </remarks>
    /// <param name="side">The side as the match was locked.</param>
    /// <param name="lines">Every participant's line from the stored statistics, both clubs' or one's.</param>
    public static ShootoutSide FromFullTime(MatchSideV1 side, IReadOnlyList<MatchPlayerLineV1> lines)
    {
        ArgumentNullException.ThrowIfNull(side);
        ArgumentNullException.ThrowIfNull(lines);

        var ownLines = lines
            .Where(line => line.ClubId == side.ClubId)
            .ToDictionary(line => line.ParticipantId);

        var slotOf = side.Slots.ToDictionary(slot => slot.ParticipantId);

        var onPitch = side.Squad
            .Where(participant => ownLines.TryGetValue(participant.ParticipantId, out var line) && IsOnPitch(line))
            .OrderBy(participant => participant.ParticipantId)
            .Select(participant => Stand(participant, slotOf, ownLines[participant.ParticipantId]))
            .ToList();

        if (onPitch.Count == 0)
        {
            // Nobody left is not a state a match can end in. Fall back to the eleven who started rather than
            // leave a decided draw with nobody to kick, so a damaged line set still gives a shoot-out.
            onPitch =
            [
                .. side.Starters()
                    .OrderBy(participant => participant.ParticipantId)
                    .Select(participant => Stand(participant, slotOf, line: null)),
            ];
        }

        return new ShootoutSide(side.ClubId, onPitch);
    }

    private static bool IsOnPitch(MatchPlayerLineV1 line) =>
        (line.Started || line.SubbedInMinute is not null)
        && line.SubbedOutMinute is null
        && !line.SentOff
        && !line.IsInjured;

    private static ActiveSlot Stand(
        MatchParticipantV1 participant,
        Dictionary<Guid, MatchSlotV1> slotOf,
        MatchPlayerLineV1? line)
    {
        var slot = slotOf.TryGetValue(participant.ParticipantId, out var own)
            ? own
            : SlotFor(participant);

        var state = participant.State;

        return new ActiveSlot
        {
            Slot = slot,
            Participant = participant,
            FamiliarityBasisPoints = EngineRulesV2.Certain,
            Condition = new PlayerCondition(
                line?.FinalConditionBasisPoints ?? state.ConditionBasisPoints,
                state.FatigueBasisPoints,
                state.MoraleBasisPoints,
                state.SharpnessBasisPoints),
        };
    }

    private static MatchSlotV1 SlotFor(MatchParticipantV1 participant)
    {
        var role = participant.Position switch
        {
            MatchPosition.Goalkeeper => MatchRole.Goalkeeper,
            MatchPosition.CentreBack => MatchRole.CentreBack,
            MatchPosition.RightBack or MatchPosition.LeftBack => MatchRole.FullBack,
            MatchPosition.DefensiveMidfielder => MatchRole.DefensiveMidfielder,
            MatchPosition.CentralMidfielder => MatchRole.CentralMidfielder,
            MatchPosition.AttackingMidfielder => MatchRole.AttackingMidfielder,
            MatchPosition.RightMidfielder or MatchPosition.LeftMidfielder => MatchRole.WideMidfielder,
            MatchPosition.RightWinger or MatchPosition.LeftWinger => MatchRole.Winger,
            _ => MatchRole.Striker,
        };

        return new MatchSlotV1
        {
            SlotNumber = 0,
            Family = role.FamilyOf(),
            Role = role,
            X = EngineRulesV2.SlotCoordinateScale / 2,
            Y = EngineRulesV2.SlotCoordinateScale / 2,
            ParticipantId = participant.ParticipantId,
        };
    }
}
