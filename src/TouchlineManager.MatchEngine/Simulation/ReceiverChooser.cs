using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;
using TouchlineManager.MatchEngine.Ratings;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Simulation;

/// <summary>
/// One leg of a possession's approach: who had the ball, who was given it, and where it went (`engine-v10`).
/// </summary>
/// <param name="Passer">The player on the ball at the start of the leg.</param>
/// <param name="Receiver">The player the ball was played to, or null when nobody was and the holder kept it.</param>
/// <param name="To">Where the ball went: the planned point, moved partway towards the receiver unless it is the last.</param>
/// <param name="Openness">How open the receiver was where he took it, 0…10,000; nothing for a leg the holder kept.</param>
/// <param name="Score">The score the holder gave the receiver he chose, 0…10,000; nothing for a leg the holder kept.</param>
internal readonly record struct ReceiverLeg(Guid Passer, Guid? Receiver, SpatialPoint To, int Openness = 0, int Score = 0)
{
    /// <summary>Gets whether the leg is a pass, as opposed to the holder carrying the ball on.</summary>
    public bool IsPass => Receiver is not null;
}

/// <summary>
/// The players a possession's approach passed through, in order (`engine-v10`).
/// </summary>
/// <param name="Legs">One entry for each leg of the approach, from the start.</param>
/// <param name="Carrier">The player who had the ball at the start.</param>
/// <param name="Holder">The player who has it at the end of the legs, who is the carrier when no leg was a pass.</param>
internal sealed record ReceiverChain(IReadOnlyList<ReceiverLeg> Legs, Guid Carrier, Guid Holder)
{
    /// <summary>Gets the players who played each pass of the chain, in order.</summary>
    public IReadOnlyList<Guid> Passers { get; } = [.. Legs.Where(leg => leg.IsPass).Select(leg => leg.Passer)];

    /// <summary>
    /// Reads how good the chain was, for the chances it moves: how open its most marked receiver was, how open the
    /// player it ends with was, and how well its holders chose (`engine-v10`).
    /// </summary>
    /// <remarks>
    /// A chain nobody played a pass in says nothing, so it reads as the average the rules centre on. The weakest
    /// receiver is read over the legs of the approach, which is what the attack has to get through to progress; the
    /// last receiver and the choices are read over every leg, the one into the final third included.
    /// </remarks>
    /// <param name="rules">The rules in force.</param>
    /// <param name="approachLegs">How many legs, from the start, are the approach.</param>
    public ChainQuality Quality(EngineRulesV2 rules, int approachLegs = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var passes = Legs.Where(leg => leg.IsPass).ToList();
        var approach = Legs.Take(approachLegs).Where(leg => leg.IsPass).ToList();

        return new ChainQuality(
            approach.Count == 0 ? rules.ChainWeakestOpennessReference : approach.Min(leg => leg.Openness),
            passes.Count == 0 ? rules.ChainFinalOpennessReference : passes[^1].Openness,
            passes.Count == 0 ? rules.ChainChoiceReference : passes.Sum(leg => leg.Score) / passes.Count);
    }
}

/// <summary>
/// How good a receiver chain was, on the 0…10,000 scale of the scores it was built from (`engine-v10`).
/// </summary>
/// <param name="Weakest">How open the most marked of its receivers was.</param>
/// <param name="Final">How open the player it ends with was.</param>
/// <param name="Choice">The mean score of the receivers its holders chose.</param>
internal readonly record struct ChainQuality(int Weakest, int Final, int Choice)
{
    /// <summary>Gets what the quality moves the chance the attack progresses by, in basis points.</summary>
    /// <param name="rules">The rules in force.</param>
    public int ProgressNudge(EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return Probability.Swing(
            Weakest - rules.ChainWeakestOpennessReference,
            rules.ChainProgressSwingBasisPoints,
            EngineRulesV2.Certain);
    }

    /// <summary>Gets what the quality moves the chance the attack creates a shot by, in basis points.</summary>
    /// <param name="rules">The rules in force.</param>
    public int CreationNudge(EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return Probability.Swing(
                Final - rules.ChainFinalOpennessReference,
                rules.ChainCreationOpennessSwingBasisPoints,
                EngineRulesV2.Certain)
            + Probability.Swing(
                Choice - rules.ChainChoiceReference,
                rules.ChainCreationChoiceSwingBasisPoints,
                EngineRulesV2.Certain);
    }
}

/// <summary>
/// Chooses who the player on the ball plays it to, for each leg of a possession's approach (`engine-v10`).
/// </summary>
/// <remarks>
/// <para>
/// The ball's path is still drawn first, and the pass focus still steers it. What this adds is a person at each
/// end of every leg. The holder looks round: a teammate is seen with a chance that rises with his Vision and falls
/// with the distance. Of those he sees, only the ones who could get to the ball (reach) and whom the depth rule
/// allows may be given it. They are scored for how open they are, how far the pass takes the attack, how easily they
/// get there and how well their lane fits the side's pass focus, and one is drawn, favouring the best score the more
/// Decisions the holder has. The planned touch is then pulled part of the way towards him. When nobody is both seen
/// and eligible the holder keeps the ball.
/// </para>
/// <para>
/// The draws come from a stream derived from the match seed and the possession's ordinal, with a fixed number
/// for each leg: one for every outfield player, then one for the choice. It is never the play stream, so choosing a
/// receiver cannot move a play draw. The last touch of an approach is never pulled, so the pressure point, the point
/// a ball is lost at, and everything the outcome reads off them stay where the plan put them.
/// </para>
/// </remarks>
internal static class ReceiverChooser
{

    /// <summary>A stride that keeps each possession's receiver stream distinct from the seed and from the other streams.</summary>
    private const ulong StreamStride = 1_000_037UL;

    /// <summary>The weight of a seen and eligible receiver whose score is nothing, so even he can be chosen.</summary>
    private const int BaseWeight = 100;

    /// <summary>The ceiling a lost pass's weight is measured against, one above an attribute's maximum, in hundredths.</summary>
    private const int FailureCeiling = (MatchAttributeNames.Max + 2) * EffectiveSkill.Scale;

    private const int LowestHundredths = MatchAttributeNames.Min * EffectiveSkill.Scale;
    private const int HighestHundredths = MatchAttributeNames.Max * EffectiveSkill.Scale;

    /// <summary>Creates the per-possession receiver stream for the possession being played.</summary>
    /// <param name="state">The match state.</param>
    public static Pcg32 CreateStream(MatchState state) =>
        new(unchecked((state.Input.Seed * StreamStride) + (ulong)state.PossessionOrdinal));

    /// <summary>
    /// Chooses the receiver for each leg of a path the side in possession plays, from its first point to its last.
    /// </summary>
    /// <param name="state">The match state.</param>
    /// <param name="side">The side in possession.</param>
    /// <param name="points">The planned path: the start, the touches between, and where it ends.</param>
    /// <param name="firstHolder">The player on the ball at the start.</param>
    /// <param name="lost">Whether the ball was lost on the last leg, which makes its passer likelier a poor one.</param>
    /// <param name="unpulledFrom">
    /// The index of the first point that is never moved towards its receiver; the last point when it is not given.
    /// A path that goes on past the point the ball can be lost at holds the rest of it still too.
    /// </param>
    /// <returns>The chain, or null when the first holder is not an outfield player on the pitch.</returns>
    public static ReceiverChain? Choose(
        MatchState state,
        MatchSide side,
        IReadOnlyList<SpatialPoint> points,
        Guid firstHolder,
        bool lost,
        int? unpulledFrom = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(points);

        var rules = state.Rules;
        var attacking = state.SideOf(side);
        var defending = state.OpponentOf(side);
        var isHome = side == MatchSide.Home;

        var holder = attacking.Outfield.FirstOrDefault(slot => slot.Participant.ParticipantId == firstHolder);

        if (holder is null || points.Count == 0)
        {
            return null;
        }

        var stream = CreateStream(state);
        var legs = new List<ReceiverLeg>(Math.Max(0, points.Count - 1));
        var from = points[0];
        var defendersAtBall = OffBallModel.Place(defending.Active, !isHome, hasPossession: false, from, defending.Instructions, rules);

        for (var index = 1; index < points.Count; index++)
        {
            var target = points[index];
            var last = index >= (unpulledFrom ?? points.Count - 1);

            var teammates = OffBallModel.Place(attacking.Active, isHome, hasPossession: true, target, attacking.Instructions, rules);
            var defenders = OffBallModel.Place(defending.Active, !isHome, hasPossession: false, target, defending.Instructions, rules);

            var pressed = OffBallModel.IsUnderPressure(from, defendersAtBall, rules);

            var chosen = ChooseOne(
                new Leg(holder, from, target, last, pressed, isHome, attacking.Instructions.PassFocus, lost && index == points.Count - 2),
                teammates,
                defenders,
                rules,
                stream);

            var arrival = chosen is { } receiver ? LandingPoint(target, receiver.Player, last, isHome, rules) : target;

            legs.Add(new ReceiverLeg(
                holder.Participant.ParticipantId,
                chosen?.Player.Player.Participant.ParticipantId,
                arrival,
                chosen?.Openness ?? 0,
                chosen?.Score ?? 0));

            holder = chosen?.Player.Player ?? holder;
            from = arrival;
            defendersAtBall = defenders;
        }

        return new ReceiverChain(legs, firstHolder, holder.Participant.ParticipantId);
    }

    /// <summary>Gets the chance, in basis points, that a holder sees a teammate who is a distance away.</summary>
    /// <param name="holder">The player on the ball.</param>
    /// <param name="distance">How far away the teammate stands, in pitch units.</param>
    /// <param name="rules">The rules in force.</param>
    public static int SeeChance(ActiveSlot holder, int distance, EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(holder);
        ArgumentNullException.ThrowIfNull(rules);

        var sight = Interpolate(
            EffectiveSkill.Hundredths(holder, MatchAttributeName.Vision, rules),
            rules.ReceiverSeeLowestBasisPoints,
            rules.ReceiverSeeHighestBasisPoints);

        var far = Math.Min(Math.Max(0, distance), rules.ReceiverSeeFullDistance);
        var lost = (long)rules.ReceiverSeeDistancePenaltyBasisPoints * far / rules.ReceiverSeeFullDistance;

        return (int)(sight * (EngineRulesV2.Certain - lost) / EngineRulesV2.Certain);
    }

    /// <summary>Gets how sharply a holder favours the best-placed teammate: his Decisions as a gain on the squared score.</summary>
    /// <param name="holder">The player on the ball.</param>
    /// <param name="rules">The rules in force.</param>
    public static int ChoiceGain(ActiveSlot holder, EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(holder);
        ArgumentNullException.ThrowIfNull(rules);

        return Interpolate(
            EffectiveSkill.Hundredths(holder, MatchAttributeName.Decisions, rules),
            rules.ReceiverChoiceGainLowest,
            rules.ReceiverChoiceGainHighest);
    }

    /// <summary>Gets the selection weight a score earns for a holder of a given gain: nearly flat at a low gain, steep at a high one.</summary>
    /// <param name="score">The receiver's score, 0…10,000.</param>
    /// <param name="gain">The holder's gain.</param>
    public static int ChoiceWeight(int score, int gain)
    {
        var bounded = Math.Clamp(score, 0, EngineRulesV2.Certain);
        var squared = (long)bounded * bounded / EngineRulesV2.Certain;

        return BaseWeight + (int)(squared * gain / 100);
    }

    /// <summary>
    /// Gets how good a teammate is to play the ball to, 0…10,000: how open he is, how far it takes the attack, how
    /// easily he gets there and how well his lane fits the side's pass focus, by the rules' weights.
    /// </summary>
    /// <param name="openness">His openness where he would take it.</param>
    /// <param name="progress">The progress score of the pass.</param>
    /// <param name="reach">How easily he gets to the planned point.</param>
    /// <param name="lane">How well his lane fits the pass focus.</param>
    /// <param name="rules">The rules in force.</param>
    public static int Score(int openness, int progress, int reach, int lane, EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var weights = rules.ReceiverOpennessWeight + rules.ReceiverProgressWeight + rules.ReceiverReachWeight + rules.ReceiverLaneWeight;
        var sum = ((long)openness * rules.ReceiverOpennessWeight)
            + ((long)progress * rules.ReceiverProgressWeight)
            + ((long)reach * rules.ReceiverReachWeight)
            + ((long)lane * rules.ReceiverLaneWeight);

        return (int)(sum / Math.Max(1, weights));
    }

    /// <summary>
    /// Weighs a receiver by what he will do with the ball once he has it: the better his Passing, the likelier the
    /// possession goes through him, and when the possession is known to have broken down on his pass, the likelier it
    /// is that he was a poor passer.
    /// </summary>
    /// <remarks>
    /// The outcome of the possession was decided before the chain was written, so the chain is the story of how it
    /// came about: of everyone who might have had the ball, the player whose pass was lost is likelier a poor passer
    /// and the ones whose passes found their man are likelier good ones. It is the same link between a player's Passing
    /// and his completion rate that the credit made before there was a chain, and it moves nothing the match depends on.
    /// </remarks>
    /// <param name="weight">The weight the receiver's score earned.</param>
    /// <param name="receiver">The receiver.</param>
    /// <param name="lostNext">Whether his pass is the one the possession was lost on.</param>
    /// <param name="rules">The rules in force.</param>
    public static int HandsWeight(int weight, ActiveSlot receiver, bool lostNext, EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(receiver);
        ArgumentNullException.ThrowIfNull(rules);

        var passing = EffectiveSkill.Hundredths(receiver, MatchAttributeName.Passing, rules);
        var hands = lostNext ? FailureCeiling - passing : passing;

        return Math.Max(1, (int)((long)weight * Math.Max(1, hands) / EffectiveSkill.Scale));
    }

    /// <summary>
    /// Gets where a pass lands when it is played to a receiver, from the point it was planned to: moved partway
    /// towards him, but never out of the lane it was planned in, so the shares of the ball in each lane that the pass
    /// focus is calibrated to stay as they were.
    /// </summary>
    /// <param name="planned">The planned point.</param>
    /// <param name="receiver">The receiver and where he stands.</param>
    /// <param name="last">Whether it is the last point of the path, which does not move.</param>
    /// <param name="isHome">Whether the side attacks towards the high end of the pitch.</param>
    /// <param name="rules">The rules in force.</param>
    public static SpatialPoint LandingPoint(SpatialPoint planned, OffBallPlayer receiver, bool last, bool isHome, EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        if (last)
        {
            return planned;
        }

        var x = planned.X + (int)((long)(receiver.Spot.X - planned.X) * rules.ReceiverPullBasisPoints / EngineRulesV2.Certain);
        var y = planned.Y + (int)((long)(receiver.Spot.Y - planned.Y) * rules.ReceiverPullBasisPoints / EngineRulesV2.Certain);

        // The lane is read on the side's own scale, where left is the low end.
        var plannedY = PassagePlanner.AttackingY(planned.Y, isHome);
        var pulledY = PassagePlanner.AttackingY(y, isHome);

        var (laneMin, laneMax) = plannedY < rules.PassLeftLaneMaxYBasisPoints
            ? (0, rules.PassLeftLaneMaxYBasisPoints - 1)
            : plannedY >= rules.PassRightLaneMinYBasisPoints
                ? (rules.PassRightLaneMinYBasisPoints, SpatialPitch.PitchWidth)
                : (rules.PassLeftLaneMaxYBasisPoints, rules.PassRightLaneMinYBasisPoints - 1);

        return new SpatialPoint(x, PassagePlanner.FromAttack(0, Math.Clamp(pulledY, laneMin, laneMax), isHome).Y).Clamp();
    }

    /// <summary>The leg being played, so the choice does not take eight parameters.</summary>
    private sealed record Leg(
        ActiveSlot Holder,
        SpatialPoint From,
        SpatialPoint Target,
        bool Last,
        bool Pressed,
        bool IsHome,
        MatchPassFocus Focus,
        bool NextIsLost);

    /// <summary>A teammate who is seen and eligible, with his selection weight, his openness and his score.</summary>
    private readonly record struct Option(OffBallPlayer Player, int Weight, int Openness, int Score);

    private static Option? ChooseOne(
        Leg leg,
        IReadOnlyList<OffBallPlayer> teammates,
        IReadOnlyList<OffBallPlayer> defenders,
        EngineRulesV2 rules,
        Pcg32 stream)
    {
        var gain = ChoiceGain(leg.Holder, rules);
        var options = new List<Option>(teammates.Count);
        long total = 0;

        foreach (var teammate in teammates)
        {
            if (teammate.Player.Slot.Family == MatchPositionFamily.Goalkeeper)
            {
                continue;
            }

            // One draw for every outfield player, the holder's own included, so the stream's shape never depends on
            // who is seen.
            var sight = stream.NextBasisPoints();

            if (teammate.Player.Participant.ParticipantId == leg.Holder.Participant.ParticipantId)
            {
                continue;
            }

            var distance = SpatialMath.Distance(leg.From, teammate.Spot);

            if (sight >= SeeChance(leg.Holder, distance, rules))
            {
                continue;
            }

            // He runs to where he takes the ball, which is nearer than the point it was planned to when it is pulled
            // towards him, so that is the distance his reach is measured over.
            var arrival = LandingPoint(leg.Target, teammate, leg.Last, leg.IsHome, rules);

            if (!OffBallModel.IsWithinReach(teammate, arrival, rules))
            {
                continue;
            }

            if (!OffBallModel.PassesDepthRule(teammate.Player.Slot.Family, leg.From, arrival, leg.IsHome, leg.Pressed, rules))
            {
                continue;
            }

            var openness = OffBallModel.Openness(arrival, teammate.Player, defenders, rules);

            var score = Score(
                openness,
                OffBallModel.ProgressScore(leg.From, arrival, leg.IsHome, rules),
                OffBallModel.ReachScore(teammate, arrival, rules),
                PassagePlanner.LaneFit(teammate.Spot, leg.IsHome, leg.Focus, rules),
                rules);

            var weight = HandsWeight(ChoiceWeight(score, gain), teammate.Player, leg.NextIsLost, rules);

            options.Add(new Option(teammate, weight, openness, score));
            total += weight;
        }

        // The choice is drawn whether or not anybody is there to choose, for the same reason.
        var draw = stream.NextInt((int)Math.Clamp(total, 1, int.MaxValue));

        long running = 0;

        foreach (var option in options)
        {
            running += option.Weight;

            if (draw < running)
            {
                return option;
            }
        }

        return null;
    }

    /// <summary>Places an effective skill, in hundredths, on a line from a low value at the lowest skill to a high one at the highest.</summary>
    private static int Interpolate(int hundredths, int low, int high)
    {
        var bounded = int.Clamp(hundredths, LowestHundredths, HighestHundredths) - LowestHundredths;

        return low + (int)((long)(high - low) * bounded / (HighestHundredths - LowestHundredths));
    }
}
