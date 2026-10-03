using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;

namespace TouchlineManager.MatchEngine.Simulation;

/// <summary>
/// Credits the player who set a goal up (`engine-v2`).
/// </summary>
/// <remarks>
/// <para>
/// An assist is nowhere in the event stream: a goal event names the scorer and the goalkeeper it beat and
/// nothing else, so the player who made the chance has to be chosen at the moment the goal is scored. This
/// is that choice.
/// </para>
/// <para>
/// It draws from a stream derived from the match seed and the goal's own sequence number, never from the
/// play stream. That is deliberate, and it is what keeps this version's <em>play</em> identical to the
/// previous one: a draw taken from the play stream would shift every decision after it and move the
/// scoreline distributions the engine was calibrated against. Crediting an assist changes a player line and
/// the output hash, and nothing else about the match.
/// </para>
/// <para>
/// The candidate is drawn, weighted by the attributes that make a creator — vision, passing, technique,
/// crossing, and dribbling — over the outfield players on the pitch in slot order, excluding the scorer. A
/// goalkeeper is never credited, and a teammate who has gone off is not a candidate because they are no
/// longer in the active list.
/// </para>
/// </remarks>
internal static class AssistPlanner
{
    /// <summary>A stride that keeps each goal's assist stream distinct from the seed and from the others'.</summary>
    private const ulong StreamStride = 1_000_003UL;

    /// <summary>Credits an assist for a goal, when the scoring side had another player on the pitch.</summary>
    /// <param name="state">The match state.</param>
    /// <param name="side">The side that scored.</param>
    /// <param name="scorerId">The scorer, who cannot assist their own goal.</param>
    /// <param name="sequence">The goal event's sequence number.</param>
    public static void Credit(MatchState state, MatchSide side, Guid scorerId, int sequence)
    {
        ArgumentNullException.ThrowIfNull(state);

        var runtime = state.SideOf(side);

        var candidates = runtime.Outfield
            .Where(slot => slot.Participant.ParticipantId != scorerId)
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        var stream = new Pcg32(unchecked((state.Input.Seed * StreamStride) + (ulong)sequence));
        var assister = WeightedPick.From(candidates, slot => CreationWeight(slot.Participant.Attributes), stream);

        if (assister is null)
        {
            return;
        }

        var id = assister.Participant.ParticipantId;

        runtime.Assists.TryGetValue(id, out var assists);
        runtime.Assists[id] = assists + 1;

        // The ball that set the goal up is a completed pass: the possession's own creating pass when it had
        // one, which the tally credits to this player when the possession ends, and otherwise the delivery
        // that no phase of the approach counts, a corner's (`engine-v6`).
        if (state.Passing.FinalLegCreatedShot)
        {
            state.Passing.AssistedBy = id;
        }
        else
        {
            PassTally.RecordDelivery(runtime, id);
        }

        // The assist the crowd saw is also on the live scale, the moment the goal is credited (engine-v3).
        runtime.AdjustLiveRating(id, state.Rules.LiveRatingAssistBonusBasisPoints);
    }

    /// <summary>The weight that makes a player likely to be the one who set the goal up.</summary>
    /// <param name="attributes">The player's attributes.</param>
    public static int CreationWeight(PlayerAttributesV1 attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        return (attributes.ValueOf(MatchAttributeName.Vision) * 2)
            + (attributes.ValueOf(MatchAttributeName.Passing) * 2)
            + attributes.ValueOf(MatchAttributeName.Technique)
            + attributes.ValueOf(MatchAttributeName.Crossing)
            + attributes.ValueOf(MatchAttributeName.Dribbling);
    }
}
