using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Ratings;

namespace TouchlineManager.MatchEngine.Simulation;

/// <summary>
/// Resolves fouls, bookings, and sendings-off (master plan §8.4, `DIS-1`…`DIS-4`).
/// </summary>
/// <remarks>
/// <para>
/// The foul is rolled against the <em>defending</em> side, once per possession, independently of how the
/// possession otherwise goes. That independence is what makes the foul rate tunable: a foul is not a side
/// effect of a failed tackle, so moving the tackle model does not silently move the number of cards a season
/// produces.
/// </para>
/// <para>
/// The aggressive-tackling trade-off lives here rather than in the rating table. Choosing
/// <c>Aggressive</c> buys defensive pressure in <c>TacticalModifiers</c> and pays for it in fouls, cards, and
/// suspensions here — which is why a manager who sets it every week ends up with a squad that cannot field
/// its best eleven.
/// </para>
/// </remarks>
internal static class DisciplineSimulator
{
    /// <summary>What one possession's foul produced, for the caller that resolves what the foul gave.</summary>
    /// <param name="FoulCommitted">Whether a foul was committed.</param>
    /// <param name="FoulerId">The player who fouled, when there was one.</param>
    /// <param name="CardShown">Whether the foul drew a card: a booking, a second booking, or a red.</param>
    /// <param name="SecondYellow">Whether the card was a second booking rather than a first.</param>
    /// <param name="StraightRed">Whether the card was a straight red rather than a booking.</param>
    public readonly record struct FoulOutcome(
        bool FoulCommitted,
        Guid? FoulerId,
        bool CardShown,
        bool SecondYellow,
        bool StraightRed);

    /// <summary>
    /// Rolls the defending side's foul for one possession and resolves the card the foul drew.
    /// </summary>
    /// <remarks>
    /// What the foul <em>gives</em> the attacking side — a penalty, or a free kick in a promising position —
    /// is resolved by the possession flow that called this, because that flow knows where the ball was and
    /// who was attacking; the discipline flow owns only the foul and its card. The card's stoppage, event,
    /// and removal are still handled here, as they always were.
    /// </remarks>
    /// <param name="state">The match state.</param>
    /// <param name="defendingSide">The side not in possession.</param>
    /// <returns>Whether a foul happened, which ends the possession.</returns>
    public static FoulOutcome TryResolveFoul(MatchState state, MatchSide defendingSide)
    {
        var defender = state.SideOf(defendingSide);
        var rules = state.Rules;

        var foulChance = Probability.Apply(
            rules.BaseFoulBasisPoints,
            TacklingMultiplier(defender.Instructions.Tackling, rules));

        if (!state.Random.RollBasisPoints(foulChance))
        {
            return new FoulOutcome(FoulCommitted: false, null, CardShown: false, SecondYellow: false, StraightRed: false);
        }

        var fouler = WeightedPick.From(
            defender.Outfield,
            slot => slot.Participant.Attributes.ValueOf(MatchAttributeName.Aggression),
            state.Random);

        if (fouler is null)
        {
            return new FoulOutcome(FoulCommitted: true, null, CardShown: false, SecondYellow: false, StraightRed: false);
        }

        var foulerId = fouler.Participant.ParticipantId;

        state.Emit(defendingSide, EngineEventType.Foul, foulerId);

        var card = ApplyCard(state, defendingSide, defender, fouler, rules);

        return new FoulOutcome(
            FoulCommitted: true,
            foulerId,
            card.Shown,
            card.SecondYellow,
            card.StraightRed);
    }

    /// <summary>
    /// Decides whether a foul is booked, sent off, or let go, consuming exactly one draw either way.
    /// </summary>
    /// <remarks>
    /// One draw for the whole decision rather than one per possible card, so the random stream advances by the
    /// same amount whether or not a card came out. A stream that advances variably is still deterministic, but
    /// it makes every later decision depend on how many cards happened to fall, which makes a golden hash
    /// change for reasons nobody can see in the diff.
    /// </remarks>
    /// <summary>What the card decision showed.</summary>
    /// <param name="Shown">Whether any card came out.</param>
    /// <param name="SecondYellow">Whether it was a second booking.</param>
    /// <param name="StraightRed">Whether it was a straight red.</param>
    private readonly record struct CardResult(bool Shown, bool SecondYellow, bool StraightRed);

    private static CardResult ApplyCard(
        MatchState state,
        MatchSide defendingSide,
        SideRuntime defender,
        ActiveSlot fouler,
        EngineRulesV2 rules)
    {
        var booking = Probability.Apply(
            rules.YellowCardPerFoulBasisPoints,
            TacklingCardMultiplier(defender.Instructions.Tackling, rules));

        var roll = state.Random.NextBasisPoints();
        var straightRed = rules.StraightRedPerFoulBasisPoints;

        if (roll < straightRed)
        {
            SendOff(state, defendingSide, defender, fouler, rules, secondBooking: false);

            return new CardResult(Shown: true, SecondYellow: false, StraightRed: true);
        }

        if (roll >= straightRed + booking)
        {
            return new CardResult(Shown: false, SecondYellow: false, StraightRed: false);
        }
        var foulerId = fouler.Participant.ParticipantId;

        var second = defender.Book(foulerId);

        state.AddCardStoppage();

        if (second)
        {
            SendOff(state, defendingSide, defender, fouler, rules, secondBooking: true);

            return new CardResult(Shown: true, SecondYellow: true, StraightRed: false);
        }

        state.Emit(defendingSide, EngineEventType.YellowCard, foulerId);

        // A booking is what the crowd saw, so the live rating shows it too (engine-v3).
        defender.AdjustLiveRating(foulerId, -rules.LiveRatingYellowPenaltyBasisPoints);

        return new CardResult(Shown: true, SecondYellow: false, StraightRed: false);
    }

    private static void SendOff(
        MatchState state,
        MatchSide side,
        SideRuntime defender,
        ActiveSlot player,
        EngineRulesV2 rules,
        bool secondBooking)
    {
        var participantId = player.Participant.ParticipantId;

        if (!defender.SentOff.Add(participantId))
        {
            return;
        }

        state.Emit(
            side,
            secondBooking ? EngineEventType.SecondYellowCard : EngineEventType.RedCard,
            participantId);

        state.AddCardStoppage();

        // A sending-off is what the crowd saw, so the live rating records it before the player leaves the
        // pitch (engine-v3).
        defender.AdjustLiveRating(participantId, -rules.LiveRatingRedPenaltyBasisPoints);

        // A sent-off player is not replaced — no substitution can bring one on — so the side plays a player
        // short for the rest of the match. That is the whole point of the sanction.
        defender.RemoveParticipant(participantId, state.Minute, rules);
    }

    /// <summary>Gets how a tackling style scales the foul rate.</summary>
    /// <param name="style">The tackling style.</param>
    /// <param name="rules">The rules in force.</param>
    private static int TacklingMultiplier(MatchTacklingStyle style, EngineRulesV2 rules) => style switch
    {
        MatchTacklingStyle.Aggressive => rules.AggressiveTacklingFoulMultiplierBasisPoints,
        MatchTacklingStyle.StayOnFeet => rules.StayOnFeetFoulMultiplierBasisPoints,
        _ => EngineRulesV2.Certain,
    };

    /// <summary>
    /// Gets how a tackling style scales the chance that a foul is booked.
    /// </summary>
    /// <remarks>
    /// Separate from the foul multiplier because the two effects are separate: committing more fouls is not the
    /// same as committing worse ones. Staying on your feet lowers how often you foul and leaves the booking
    /// chance of the fouls you do commit alone, whereas going in aggressively raises both — which is what makes
    /// the aggressive setting a genuine disciplinary risk rather than merely a busier one.
    /// </remarks>
    /// <param name="style">The tackling style.</param>
    /// <param name="rules">The rules in force.</param>
    private static int TacklingCardMultiplier(MatchTacklingStyle style, EngineRulesV2 rules) => style switch
    {
        MatchTacklingStyle.Aggressive => rules.AggressiveTacklingCardMultiplierBasisPoints,
        _ => EngineRulesV2.Certain,
    };
}
