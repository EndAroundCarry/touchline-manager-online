using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;

namespace TouchlineManager.MatchEngine.Simulation;

/// <summary>
/// The passes one possession made, as its phases add them up (`engine-v7`).
/// </summary>
/// <remarks>
/// The simulation plays a possession as a handful of phases rather than as individual passes, so there is
/// no pass for a player to be credited with at the moment it happens. What it does decide is how far the ball
/// got and whether the attack broke down, and the passes follow from that: every leg of the ball's approach
/// was a pass, and the leg a failed phase ended on is the one that did not find its man. The phases record
/// that here as they resolve; <see cref="PassTally.Settle"/> hands the legs to players when the possession
/// ends.
/// </remarks>
/// <param name="Side">The side in possession.</param>
internal sealed class PossessionPassing(MatchSide Side)
{
    /// <summary>Gets the side whose players made the passes.</summary>
    public MatchSide Side { get; } = Side;

    /// <summary>Gets how many passes the possession attempted.</summary>
    public int Legs { get; private set; }

    /// <summary>Gets whether the last of them was lost.</summary>
    public bool FinalLegFailed { get; private set; }

    /// <summary>Gets whether the last of them was the ball that created a shot, which a goal turns into an assist.</summary>
    public bool FinalLegCreatedShot { get; private set; }

    /// <summary>Gets the player credited with the assist when the possession ended in a goal.</summary>
    public Guid? AssistedBy { get; set; }

    /// <summary>
    /// Gets the players who played the approach's passes, in order, when the approach was played through chosen
    /// receivers (`engine-v10`); empty when it was not.
    /// </summary>
    public IReadOnlyList<Guid> ChainPassers { get; private set; } = [];

    /// <summary>
    /// Gets the player who has the ball when the approach ends (`engine-v10`): the one who plays on whatever the
    /// phases after it add, so the ball that creates a shot and the one that is stopped are his.
    /// </summary>
    public Guid? ChainHolder { get; private set; }

    /// <summary>Records who played the approach's passes and who has the ball at the end of it.</summary>
    /// <param name="chain">The receivers the approach was played through.</param>
    public void Chained(ReceiverChain chain)
    {
        ArgumentNullException.ThrowIfNull(chain);

        ChainPassers = chain.Passers;
        ChainHolder = chain.Holder;
    }

    /// <summary>
    /// Gets who played one of the possession's passes, counting from 1, when the approach was played through
    /// chosen receivers: the passer of the leg while it was in the approach, and the player on the ball at the end
    /// of it for every pass after.
    /// </summary>
    /// <param name="leg">The pass, 1 for the first.</param>
    /// <returns>The passer, or null when the approach had no chain.</returns>
    public Guid? PasserOf(int leg) =>
        ChainHolder is null ? null : leg <= ChainPassers.Count ? ChainPassers[leg - 1] : ChainHolder;

    /// <summary>
    /// Gets the player who set a shot up for a scorer, when the approach had a chain: the one on the ball at the end
    /// of it, or the one who passed to him when he is the scorer.
    /// </summary>
    /// <param name="scorerId">The player who shot, who cannot assist his own goal.</param>
    /// <returns>The creator, or null when there is nobody else to credit.</returns>
    public Guid? CreatorFor(Guid scorerId)
    {
        if (ChainHolder is not Guid holder)
        {
            return null;
        }

        if (holder != scorerId)
        {
            return holder;
        }

        return ChainPassers.Count > 0 && ChainPassers[^1] != scorerId ? ChainPassers[^1] : null;
    }

    /// <summary>Adds passes that found their man.</summary>
    /// <param name="legs">How many.</param>
    public void Completed(int legs) => Legs += Math.Max(0, legs);

    /// <summary>
    /// Ends the ball's approach on a pass that was lost: the last leg played, or the first when none was.
    /// </summary>
    public void LostApproach()
    {
        Legs = Math.Max(1, Legs);
        FinalLegFailed = true;
    }

    /// <summary>Adds the pass that broke the defence and set a shot up.</summary>
    public void CreatedShot()
    {
        Legs++;
        FinalLegCreatedShot = true;
    }

    /// <summary>Adds the final pass that the defence stopped, so the attack created nothing.</summary>
    public void LostCreation()
    {
        Legs++;
        FinalLegFailed = true;
    }
}

/// <summary>
/// Credits a possession's passes to the players who made them (`engine-v7`).
/// </summary>
/// <remarks>
/// <para>
/// Passes are credited to the players of the approach's receiver chain (`engine-v10`), and where there is none,
/// drawn from a stream derived from the match seed and the possession's ordinal — never from the
/// play stream — for the reason <see cref="AssistPlanner"/> is: a draw taken from the play stream would shift
/// every decision after it and move the scoreline distributions the engine was calibrated against. Counting
/// a pass changes a player line and the output hash, and nothing else about the match.
/// </para>
/// <para>
/// Who passed is weighted by passing, so the better passer has the ball more; who lost the ball is weighted
/// the other way, so the better passer loses it less. Over a season that is what a pass completion rate
/// measures. The ball that created a goal belongs to whoever the assist went to, so the two statistics tell
/// the same story.
/// </para>
/// </remarks>
internal static class PassTally
{
    /// <summary>A stride that keeps each possession's pass stream distinct from the seed and from the other streams.</summary>
    private const ulong StreamStride = 1_000_033UL;

    /// <summary>The ceiling a pass's weight is measured against, one above an attribute's maximum.</summary>
    private const int FailureCeiling = MatchAttributeNames.Max + 2;

    /// <summary>Hands the possession's passes to the players of the side in possession.</summary>
    /// <param name="state">The match state.</param>
    public static void Settle(MatchState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var passing = state.Passing;

        if (passing.Legs == 0)
        {
            return;
        }

        var runtime = state.SideOf(passing.Side);
        var candidates = runtime.Outfield;

        if (candidates.Count == 0)
        {
            return;
        }

        var stream = new Pcg32(unchecked((state.Input.Seed * StreamStride) + (ulong)state.PossessionOrdinal));

        for (var leg = 1; leg <= passing.Legs; leg++)
        {
            var last = leg == passing.Legs;
            var failed = last && passing.FinalLegFailed;

            Guid? passer;

            if (last && passing.FinalLegCreatedShot && passing.AssistedBy is Guid assister)
            {
                // The ball that set a goal up is the assist, so it is the assister's pass.
                passer = assister;
            }
            else if (passing.PasserOf(leg) is Guid played)
            {
                // The approach was played through named receivers, so the pass is its passer's (`engine-v10`).
                passer = played;
            }
            else
            {
                passer = WeightedPick.From(
                    candidates,
                    slot => failed
                        ? FailureCeiling - slot.Participant.Attributes.ValueOf(MatchAttributeName.Passing)
                        : slot.Participant.Attributes.ValueOf(MatchAttributeName.Passing),
                    stream)?.Participant.ParticipantId;
            }

            if (passer is not Guid id)
            {
                continue;
            }

            Increment(runtime.PassesAttempted, id);

            if (!failed)
            {
                Increment(runtime.PassesCompleted, id);
            }
        }
    }

    /// <summary>Records a take-on: a carrier's 1v1 duel, won or lost.</summary>
    /// <param name="runtime">The carrier's side.</param>
    /// <param name="carrierId">The carrier.</param>
    /// <param name="won">Whether the carrier beat the defender.</param>
    public static void RecordDribble(SideRuntime runtime, Guid carrierId, bool won)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        Increment(runtime.DribblesAttempted, carrierId);

        if (won)
        {
            Increment(runtime.DribblesCompleted, carrierId);
        }
    }

    /// <summary>Records a completed delivery that no phase of the approach counted: a corner that set a goal up.</summary>
    /// <param name="runtime">The passer's side.</param>
    /// <param name="passerId">The passer.</param>
    public static void RecordDelivery(SideRuntime runtime, Guid passerId)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        Increment(runtime.PassesAttempted, passerId);
        Increment(runtime.PassesCompleted, passerId);
    }

    private static void Increment(Dictionary<Guid, int> counts, Guid id)
    {
        counts.TryGetValue(id, out var count);
        counts[id] = count + 1;
    }
}
