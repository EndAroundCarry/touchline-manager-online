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
    /// <summary>
    /// Rolls the defending side's foul for one possession and resolves everything that follows from it.
    /// </summary>
    /// <param name="state">The match state.</param>
    /// <param name="defendingSide">The side not in possession.</param>
    /// <returns>Whether a foul happened, which ends the possession.</returns>
    public static bool TryResolveFoul(MatchState state, MatchSide defendingSide)
    {
        var defender = state.SideOf(defendingSide);
        var rules = state.Rules;

        var foulChance = Probability.Apply(
            rules.BaseFoulBasisPoints,
            TacklingMultiplier(defender.Instructions.Tackling, rules));

        if (!state.Random.RollBasisPoints(foulChance))
        {
            return false;
        }

        var fouler = WeightedPick.From(
            defender.Outfield,
            slot => slot.Participant.Attributes.ValueOf(MatchAttributeName.Aggression),
            state.Random);

        if (fouler is null)
        {
            return true;
        }

        var foulerId = fouler.Participant.ParticipantId;

        state.Emit(defendingSide, EngineEventType.Foul, foulerId);

        // A foul in the box is resolved as a penalty and the possession ends there, rather than the attack
        // continuing: the award is the end of the passage of play.
        if (state.Random.RollBasisPoints(rules.PenaltyFromFoulBasisPoints))
        {
            ChanceSimulator.ResolvePenalty(state, MatchInputV1.OpponentOf(defendingSide));

            return true;
        }

        ApplyCard(state, defendingSide, defender, fouler, rules);

        return true;
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
    private static void ApplyCard(
        MatchState state,
        MatchSide defendingSide,
        SideRuntime defender,
        ActiveSlot fouler,
        EngineRulesV1 rules)
    {
        var booking = Probability.Apply(
            rules.YellowCardPerFoulBasisPoints,
            TacklingCardMultiplier(defender.Instructions.Tackling, rules));

        var roll = state.Random.NextBasisPoints();
        var straightRed = rules.StraightRedPerFoulBasisPoints;

        if (roll < straightRed)
        {
            SendOff(state, defendingSide, defender, fouler, rules, secondBooking: false);

            return;
        }

        if (roll >= straightRed + booking)
        {
            return;
        }

        var foulerId = fouler.Participant.ParticipantId;
        var second = defender.Book(foulerId);

        state.AddCardStoppage();

        if (second)
        {
            SendOff(state, defendingSide, defender, fouler, rules, secondBooking: true);
        }
        else
        {
            state.Emit(defendingSide, EngineEventType.YellowCard, foulerId);
        }
    }

    private static void SendOff(
        MatchState state,
        MatchSide side,
        SideRuntime defender,
        ActiveSlot player,
        EngineRulesV1 rules,
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

        // A sent-off player is not replaced — no substitution can bring one on — so the side plays a player
        // short for the rest of the match. That is the whole point of the sanction.
        defender.RemoveParticipant(participantId, state.Minute, rules);
    }

    /// <summary>Gets how a tackling style scales the foul rate.</summary>
    /// <param name="style">The tackling style.</param>
    /// <param name="rules">The rules in force.</param>
    private static int TacklingMultiplier(MatchTacklingStyle style, EngineRulesV1 rules) => style switch
    {
        MatchTacklingStyle.Aggressive => rules.AggressiveTacklingFoulMultiplierBasisPoints,
        MatchTacklingStyle.StayOnFeet => rules.StayOnFeetFoulMultiplierBasisPoints,
        _ => EngineRulesV1.Certain,
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
    private static int TacklingCardMultiplier(MatchTacklingStyle style, EngineRulesV1 rules) => style switch
    {
        MatchTacklingStyle.Aggressive => rules.AggressiveTacklingCardMultiplierBasisPoints,
        _ => EngineRulesV1.Certain,
    };
}
