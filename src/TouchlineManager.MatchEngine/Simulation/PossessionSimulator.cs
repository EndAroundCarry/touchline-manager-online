using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Simulation;

/// <summary>
/// Plays a match: a sequence of possessions across two halves (master plan §8.4, `MAT-3`).
/// </summary>
/// <remarks>
/// <para>
/// A possession is the unit of play, and a goal can only come out of one — never out of an independent
/// per-minute roll (`MAT-4`). Within a possession the phases run in a fixed order: the defending side's foul,
/// then the attempt to progress out of build-up, then creation, then the chance itself. Each phase consumes
/// its draw whether or not it is reached, and the order is part of the engine version.
/// </para>
/// <para>
/// The draw order is the contract. Every method here takes draws in a documented sequence, and no
/// collection is ever iterated unordered: the candidates for a foul, a shot, and an injury all come from the
/// slot-ordered list of players on the pitch. A refactor that changed any of that would change every
/// historical replay, which is why the golden hash tests exist (ADR-0004).
/// </para>
/// </remarks>
internal static class PossessionSimulator
{
    /// <summary>Plays both halves and stoppage.</summary>
    /// <param name="state">The match state.</param>
    public static void Run(MatchState state)
    {
        state.BeginHalf(firstHalf: true);
        state.Emit(MatchSide.Home, EngineEventType.KickOff);
        RunHalf(state);
        state.EndHalf();
        state.Emit(MatchSide.Home, EngineEventType.HalfTime);

        state.Home.ApplyHalfTimeRecovery(state.Rules);
        state.Away.ApplyHalfTimeRecovery(state.Rules);

        state.BeginHalf(firstHalf: false);
        state.Emit(MatchSide.Away, EngineEventType.SecondHalfStart);
        RunHalf(state);
        state.EndHalf();
        state.Emit(MatchSide.Home, EngineEventType.FullTime);
    }

    private static void RunHalf(MatchState state)
    {
        while (!state.HalfIsOver)
        {
            PlayPossession(state);

            // The planner's windows are minutes, and a possession can straddle two of them, so the window is
            // consumed for both sides at once rather than once per side.
            state.LastPlannerMinute = state.Minute;
        }
    }

    private static void PlayPossession(MatchState state)
    {
        var rules = state.Rules;
        var possessionSide = ChoosePossession(state);
        var attacker = state.SideOf(possessionSide);
        var defender = state.OpponentOf(possessionSide);

        var seconds = DrawPossessionSeconds(state, attacker);

        state.ClockSeconds += seconds;
        attacker.PossessionSeconds += seconds;

        // A possession's work is done by both sides, so both tire.
        state.Home.ApplyLoad(rules);
        state.Away.ApplyLoad(rules);

        SubstitutionPlanner.ConsiderBothSides(state);

        // The defending side's foul comes first: a foul ends the passage of play before it develops, which is
        // what makes it the defending side's event rather than a consequence of the attack.
        if (DisciplineSimulator.TryResolveFoul(state, MatchInputV1.OpponentOf(possessionSide)))
        {
            InjurySimulator.TryResolveInjury(state);

            return;
        }

        var control = Probability.Differential(attacker.Ratings.BuildUp, defender.Ratings.DefensivePressure);

        var progressChance = Probability.Band(
            rules.BaseProgressBasisPoints
                + Probability.Swing(
                    control,
                    rules.ProgressControlSwingBasisPoints,
                    rules.RatingDifferentialReference),
            rules.MinProgressBasisPoints,
            rules.MaxProgressBasisPoints);

        if (!state.Random.RollBasisPoints(progressChance))
        {
            ResolveFailedProgression(state, possessionSide, attacker);
            InjurySimulator.TryResolveInjury(state);

            return;
        }

        var creation = Probability.Differential(
            attacker.Ratings.Creation + (attacker.Ratings.Finishing / 2),
            defender.Ratings.DefensiveShape + (defender.Ratings.Goalkeeping / 2));

        var creationChance = Probability.Band(
            rules.BaseCreationBasisPoints
                + Probability.Swing(
                    creation,
                    rules.CreationSwingBasisPoints,
                    rules.RatingDifferentialReference),
            rules.MinCreationBasisPoints,
            rules.MaxCreationBasisPoints);

        creationChance = Probability.Apply(creationChance, GameStateModifier(state, possessionSide));

        if (!state.Random.RollBasisPoints(creationChance))
        {
            ResolveFailedCreation(state, possessionSide);
            InjurySimulator.TryResolveInjury(state);

            return;
        }

        ChanceSimulator.ResolveOpenPlay(state, possessionSide);
        InjurySimulator.TryResolveInjury(state);
    }

    /// <summary>
    /// Resolves a possession that could not be progressed: either an offside or a plain turnover.
    /// </summary>
    /// <remarks>
    /// Fouls are not here, because the defending side's foul was already rolled before the progression
    /// attempt. The two failure modes left are the attacking side's own: caught offside, or simply losing the
    /// ball.
    /// </remarks>
    private static void ResolveFailedProgression(MatchState state, MatchSide side, SideRuntime attacker)
    {
        if (!state.Random.RollBasisPoints(state.Rules.OffsideShareOfTurnoverBasisPoints))
        {
            return;
        }

        var caught = WeightedPick.From(
            attacker.Outfield,
            slot => slot.Participant.Attributes.ValueOf(MatchAttributeName.Pace),
            state.Random);

        if (caught is not null)
        {
            state.Emit(side, EngineEventType.Offside, caught.Participant.ParticipantId);
        }
    }

    /// <summary>Resolves a possession that created nothing: a corner, or a plain turnover.</summary>
    private static void ResolveFailedCreation(MatchState state, MatchSide side)
    {
        if (!state.Random.RollBasisPoints(state.Rules.CornerShareOfFailedCreationBasisPoints))
        {
            return;
        }

        state.Emit(side, EngineEventType.Corner);

        if (state.Random.RollBasisPoints(state.Rules.CornerChanceBasisPoints))
        {
            ChanceSimulator.ResolveCorner(state, side);
        }
    }

    /// <summary>
    /// Chooses which side gets the next possession, from the two sides' control of the ball.
    /// </summary>
    /// <remarks>
    /// A side's control is what it can do with the ball against what the opponent can do about it, and the
    /// share is bounded so that neither side is ever shut out of the match however lopsided the ratings are.
    /// A twenty-minute spell without the ball is a thing that happens; never touching it is not.
    /// </remarks>
    private static MatchSide ChoosePossession(MatchState state)
    {
        var rules = state.Rules;

        var homeControl = Probability.Differential(
            state.Home.Ratings.BuildUp,
            state.Away.Ratings.DefensivePressure);

        var awayControl = Probability.Differential(
            state.Away.Ratings.BuildUp,
            state.Home.Ratings.DefensivePressure);

        var homeShare = Probability.Band(
            rules.BasePossessionBasisPoints
                + Probability.Swing(
                    homeControl - awayControl,
                    rules.PossessionControlSwingBasisPoints,
                    rules.RatingDifferentialReference)
                + rules.PossessionHomeBonusBasisPoints,
            rules.MinPossessionBasisPoints,
            rules.MaxPossessionBasisPoints);

        return state.Random.RollBasisPoints(homeShare) ? MatchSide.Home : MatchSide.Away;
    }

    /// <summary>
    /// How the current scoreline changes a side's appetite for creating chances.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bounded, and derived from the score rather than accumulated, so it consumes no random draw and cannot
    /// drift. A side that is two goals up creates less; a side two goals down creates more. The effect stops
    /// at a fixed cap, which keeps a rout a rout: the point is that a settled game finishes 3-1 rather than
    /// 6-1, not that a comeback becomes likely.
    /// </para>
    /// <para>
    /// This is the one place the scoreline feeds back into the simulation, and it exists because the
    /// alternative is worse: without it, each goal is an independent event and the model produces the Poisson
    /// tail — twice as many seven-goal matches as football actually has. Master plan §8.4 permits momentum
    /// "only if explicitly bounded", and this is that bound.
    /// </para>
    /// </remarks>
    private static int GameStateModifier(MatchState state, MatchSide side)
    {
        var rules = state.Rules;
        var margin = state.GoalsOf(side) - state.GoalsOf(MatchInputV1.OpponentOf(side));

        if (Math.Abs(margin) < rules.GameStateMarginThresholdGoals)
        {
            return EngineRulesV1.Certain;
        }

        var steps = Math.Abs(margin) - rules.GameStateMarginThresholdGoals + 1;

        if (margin > 0)
        {
            var reduction = Math.Min(
                rules.MaxGameStateModifierBasisPoints,
                rules.LeadingCreationStepBasisPoints * steps);

            return EngineRulesV1.Certain - reduction;
        }

        var increase = Math.Min(
            rules.MaxGameStateModifierBasisPoints,
            rules.TrailingCreationStepBasisPoints * steps);

        return EngineRulesV1.Certain + increase;
    }

    /// <summary>
    /// Gets how long this possession will take, which is where tempo shows up as more or less football.
    /// </summary>
    /// <remarks>
    /// One draw, resolved once per possession and then used for both the clock and the possession share.
    /// Deriving the seconds twice would advance the random stream twice and silently change every subsequent
    /// decision in the match, which is the kind of defect a golden hash catches and a code review does not.
    /// The result is floored so a possession always advances the clock and the half cannot fail to end.
    /// </remarks>
    private static int DrawPossessionSeconds(MatchState state, SideRuntime attacker)
    {
        var rules = state.Rules;

        var seconds = state.Random.NextRange(rules.PossessionSecondsMin, rules.PossessionSecondsMax);

        var multiplier = attacker.Instructions.Tempo switch
        {
            MatchTempo.High => rules.HighTempoPossessionSecondsMultiplierBasisPoints,
            MatchTempo.Low => rules.LowTempoPossessionSecondsMultiplierBasisPoints,
            _ => EngineRulesV1.Certain,
        };

        return Math.Max(rules.MinEffectivePossessionSeconds, Probability.Apply(seconds, multiplier));
    }
}
