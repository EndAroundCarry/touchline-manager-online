using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;
using TouchlineManager.MatchEngine.Ratings;
using TouchlineManager.MatchEngine.Spatial;

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
        state.RestartFromCentre = true;
        state.Emit(MatchSide.Home, EngineEventType.KickOff);
        state.CaptureLiveMetrics();
        RunHalf(state);
        state.EndHalf();
        state.Emit(MatchSide.Home, EngineEventType.HalfTime);

        state.Home.ApplyHalfTimeRecovery(state.Rules);
        state.Away.ApplyHalfTimeRecovery(state.Rules);

        state.BeginHalf(firstHalf: false);
        state.RestartFromCentre = true;
        state.Emit(MatchSide.Away, EngineEventType.SecondHalfStart);
        state.CaptureLiveMetrics();
        RunHalf(state);
        state.EndHalf();
        state.Emit(MatchSide.Home, EngineEventType.FullTime);
        state.CaptureLiveMetrics();
    }

    private static void RunHalf(MatchState state)
    {
        while (!state.HalfIsOver)
        {
            PlayPossession(state);

            // The planner's windows are minutes, and a possession can straddle two of them, so the window is
            // consumed for both sides at once rather than once per side.
            state.LastPlannerMinute = state.Minute;

            // The replay's condition bars and rating badges are sampled from the same run, one row per
            // player per minute the possession passed through (`engine-v3`, §9.5).
            state.CaptureLiveMetrics();
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

        // The possession is played along a real passage now: the ball starts where the last one left it — or
        // at a restart — and progresses into the attacking third through a handful of touches. The geometry
        // comes from a per-possession derived stream, so it is reproducible but can never move a play draw
        // (engine-v4).
        state.PossessionOrdinal++;

        var derived = PassagePlanner.CreateStream(state);
        var plan = PassagePlanner.Plan(state, possessionSide, derived);

        state.BeginPassage(possessionSide);
        RecordApproach(state, possessionSide, plan, derived);

        // The defending side's foul comes first: a foul ends the passage of play before it develops, which is
        // what makes it the defending side's event rather than a consequence of the attack. What the foul
        // gives — a penalty, or a direct free kick in range — is resolved here, where the ball's location is
        // known; the discipline flow recorded only the foul and its card.
        var foul = DisciplineSimulator.TryResolveFoul(state, MatchInputV1.OpponentOf(possessionSide));

        if (foul.FoulCommitted)
        {
            ApplyFoulLiveRatings(state, possessionSide, foul);

            if (state.Random.RollBasisPoints(rules.PenaltyFromFoulBasisPoints))
            {
                state.MoveBallAndRecord(plan.PenaltySpot, PassageWaypointKind.Shot, rules.ShotAltitude);
                ChanceSimulator.ResolvePenalty(state, possessionSide);
                InjurySimulator.TryResolveInjury(state);
                state.EndPassage();

                return;
            }

            var attackingX = AttackingX(state, possessionSide);

            if (attackingX >= rules.FreeKickShootingRangeX && state.Random.RollBasisPoints(rules.FreeKickAwardBasisPoints))
            {
                state.MoveBallAndRecord(plan.PressurePoint, PassageWaypointKind.Shot, rules.ShotAltitude);
                ResolveFreeKick(state, possessionSide, attackingX);
                InjurySimulator.TryResolveInjury(state);
                state.EndPassage();

                return;
            }

            InjurySimulator.TryResolveInjury(state);
            state.EndPassage();

            return;
        }

        // A loose ball opens only a share of passages: most possessions begin with the ball already under
        // control, and the contested 50/50 is the exception rather than the tax on every attack.
        if (state.Random.RollBasisPoints(rules.ScrambleOpeningBasisPoints) && ResolveScramble(state, possessionSide))
        {
            state.MoveBallAndRecord(plan.TurnoverPoint, PassageWaypointKind.Clearance, rules.ClearanceAltitude);
            InjurySimulator.TryResolveInjury(state);
            state.EndPassage();

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
            ResolveFailedProgression(state, possessionSide, attacker, plan);
            InjurySimulator.TryResolveInjury(state);
            state.EndPassage();

            return;
        }

        // The carrier takes the ball into the box, where the last defender engages.
        state.MoveBallAndRecord(plan.HeaderPoint, PassageWaypointKind.Pass);

        var creation = Probability.Differential(
            attacker.Ratings.Creation + (attacker.Ratings.Finishing / 2),
            defender.Ratings.DefensiveShape + (defender.Ratings.Goalkeeping / 2));

        // The 1v1 the carrier fights to reach the creation phase: a beat man makes the chance more likely,
        // a tackle shuts the passage down.
        var duelBonus = ResolveGroundDuel(state, possessionSide);

        var creationChance = Probability.Band(
            rules.BaseCreationBasisPoints
                + duelBonus
                + Probability.Swing(
                    creation,
                    rules.CreationSwingBasisPoints,
                    rules.RatingDifferentialReference),
            rules.MinCreationBasisPoints,
            rules.MaxCreationBasisPoints);

        creationChance = Probability.Apply(creationChance, GameStateModifier(state, possessionSide));

        if (!state.Random.RollBasisPoints(creationChance))
        {
            ResolveFailedCreation(state, possessionSide, plan);
            InjurySimulator.TryResolveInjury(state);
            state.EndPassage();

            return;
        }

        state.MoveBallAndRecord(plan.ShotPoint, PassageWaypointKind.Shot, rules.ShotAltitude);
        ChanceSimulator.ResolveOpenPlay(state, possessionSide, plan.Zone);
        InjurySimulator.TryResolveInjury(state);
        state.EndPassage();
    }

    /// <summary>
    /// Moves the ball along the possession's approach, from the start to the pressure point, recording the
    /// waypoints and the carrier's and passer's touches (`engine-v4`).
    /// </summary>
    private static void RecordApproach(
        MatchState state,
        MatchSide side,
        PlannedPassage plan,
        Pcg32 derived)
    {
        var points = plan.Approach;

        // The first waypoint is the possession's start, and the carrier who receives there.
        state.MoveBallAndRecord(points[0], PassageWaypointKind.Carry);

        var carrier = PickOutfield(state, side, derived, MatchAttributeName.Dribbling);

        if (carrier is Guid carrierId)
        {
            state.RecordTouch(carrierId, PassageAction.Carry);
        }

        for (var index = 1; index < points.Count; index++)
        {
            var last = index == points.Count - 1;
            var kind = last && plan.ApproachEndsInCross ? PassageWaypointKind.Cross : PassageWaypointKind.Pass;
            var altitude = kind == PassageWaypointKind.Cross ? state.Rules.CrossAltitude : 0;

            state.MoveBallAndRecord(points[index], kind, altitude);
        }

        // The pressure point is where the ball is received into the final beat of the build-up.
        var passer = PickOutfield(state, side, derived, MatchAttributeName.Passing);

        if (passer is Guid passerId)
        {
            state.RecordTouch(passerId, plan.ApproachEndsInCross ? PassageAction.Cross : PassageAction.Pass);
        }
    }

    /// <summary>Picks an on-pitch outfield player, weighted by an attribute, from the geometry stream.</summary>
    private static Guid? PickOutfield(
        MatchState state,
        MatchSide side,
        Pcg32 derived,
        MatchAttributeName attribute)
    {
        var pick = WeightedPick.From(
            state.SideOf(side).Outfield,
            slot => slot.Participant.Attributes.ValueOf(attribute),
            derived);

        return pick?.Participant.ParticipantId;
    }

    /// <summary>Gets how far up the pitch the ball is from the attacking side's own goal.</summary>
    private static int AttackingX(MatchState state, MatchSide possessionSide)
    {
        var x = state.Ball.GroundPoint.X;

        return possessionSide == MatchSide.Home ? x : SpatialPitch.PitchLength - x;
    }

    /// <summary>
    /// Resolves the loose-ball scramble that opens a passage: whether the possession side keeps the ball.
    /// </summary>
    /// <remarks>
    /// Returns <see langword="true"/> when the scramble is <em>lost</em>, which ends the possession with a
    /// turnover. The nearest players from each side contest it, chosen by the legs a scramble asks for
    /// (engine-v3).
    /// </remarks>
    private static bool ResolveScramble(MatchState state, MatchSide possessionSide)
    {
        var attacker = WeightedPick.From(
            state.SideOf(possessionSide).Outfield,
            slot => ScrambleWeight(slot.Participant),
            state.Random);

        var defender = WeightedPick.From(
            state.OpponentOf(possessionSide).Outfield,
            slot => ScrambleWeight(slot.Participant),
            state.Random);

        if (attacker is null || defender is null)
        {
            return false;
        }

        var kept = DuelResolver.ResolveScramble(
            attacker.Participant,
            defender.Participant,
            possessionSide == MatchSide.Home,
            state.Rules,
            state.Random);

        // Both players' ratings record the 50/50, won or lost.
        if (kept)
        {
            state.SideOf(possessionSide).AdjustLiveRating(attacker.Participant.ParticipantId, state.Rules.LiveRatingTackleBonusBasisPoints);
            state.OpponentOf(possessionSide).AdjustLiveRating(defender.Participant.ParticipantId, -state.Rules.LiveRatingTackleLostPenaltyBasisPoints);
        }
        else
        {
            state.SideOf(possessionSide).AdjustLiveRating(attacker.Participant.ParticipantId, -state.Rules.LiveRatingTackleLostPenaltyBasisPoints);
            state.OpponentOf(possessionSide).AdjustLiveRating(defender.Participant.ParticipantId, state.Rules.LiveRatingTackleBonusBasisPoints);
        }

        return !kept;
    }

    private static int ScrambleWeight(MatchParticipantV1 participant) =>
        participant.Attributes.ValueOf(MatchAttributeName.Pace)
        + participant.Attributes.ValueOf(MatchAttributeName.Acceleration)
        + participant.Attributes.ValueOf(MatchAttributeName.WorkRate);

    /// <summary>
    /// Resolves a direct free kick awarded in the attacking half: the award event, the strike, and what it
    /// produced (engine-v3).
    /// </summary>
    private static void ResolveFreeKick(MatchState state, MatchSide side, int attackingX)
    {
        var attacker = state.SideOf(side);
        var defender = state.OpponentOf(side);

        var taker = WeightedPick.From(
            attacker.Outfield,
            slot => slot.Participant.Attributes.ValueOf(MatchAttributeName.SetPieces)
                + slot.Participant.Attributes.ValueOf(MatchAttributeName.Finishing),
            state.Random);

        if (taker is null)
        {
            return;
        }

        state.RecordTouch(taker.Participant.ParticipantId, PassageAction.FreeKick);
        state.Emit(side, EngineEventType.FreeKickWon, taker.Participant.ParticipantId);

        var outcome = SetPieceDirector.ResolveDirectFreeKick(
            taker.Participant,
            defender.Goalkeeper?.Participant,
            attackingX,
            side == MatchSide.Home,
            state.Rules,
            state.Random);

        if (!outcome.Attempted)
        {
            // Crossed rather than struck: the passage ends with the ball delivered into the box.
            return;
        }

        state.Emit(
            side,
            EngineEventType.FreeKickShot,
            outcome.TakerId,
            outcome.GoalkeeperId,
            outcome.Zone,
            state.Rules.FreeKickGoalBasisPoints);

        if (outcome.IsGoal)
        {
            ScoreFromSetPiece(state, side, outcome.TakerId, EngineEventType.Goal);
            return;
        }

        // The strike missed: attribute it, saved, blocked, woodwork, or off target, like an open-play shot.
        if (outcome.WasSaved)
        {
            state.Emit(side, EngineEventType.ShotSaved, outcome.TakerId, outcome.GoalkeeperId, outcome.Zone, state.Rules.FreeKickGoalBasisPoints);

            if (outcome.GoalkeeperId is Guid keeper)
            {
                defender.AdjustLiveRating(keeper, state.Rules.LiveRatingSaveBonusBasisPoints);
            }
        }
        else if (outcome.HitWoodwork)
        {
            state.Emit(side, EngineEventType.Woodwork, outcome.TakerId, outcome.GoalkeeperId, outcome.Zone, state.Rules.FreeKickGoalBasisPoints);
        }
        else
        {
            state.Emit(side, EngineEventType.ShotOffTarget, outcome.TakerId, outcome.GoalkeeperId, outcome.Zone, state.Rules.FreeKickGoalBasisPoints);
        }

        attacker.AdjustLiveRating(outcome.TakerId, -state.Rules.LiveRatingShotMissPenaltyBasisPoints);
    }

    /// <summary>
    /// Scores a goal from a set piece and records everything the open-play scorer records, minus the assist
    /// a set piece does not model.
    /// </summary>
    private static void ScoreFromSetPiece(MatchState state, MatchSide side, Guid scorerId, EngineEventType type)
    {
        var runtime = state.SideOf(side);
        var goalkeeper = state.OpponentOf(side).Goalkeeper?.Participant.ParticipantId;

        runtime.Goals.TryGetValue(scorerId, out var goals);
        runtime.Goals[scorerId] = goals + 1;

        // A goal is a restart: the next possession begins at the centre spot (engine-v4).
        state.RestartFromCentre = true;

        state.AddGoalStoppage();

        runtime.ShiftMorale(state.Rules.MoraleGainPerGoalBasisPoints, state.Rules.MaxMoraleDriftBasisPoints);
        state
            .OpponentOf(side)
            .ShiftMorale(-state.Rules.MoraleLossPerConcededGoalBasisPoints, state.Rules.MaxMoraleDriftBasisPoints);

        runtime.AdjustLiveRating(scorerId, state.Rules.LiveRatingGoalBonusBasisPoints);

        if (goalkeeper is Guid keeper)
        {
            state.OpponentOf(side).AdjustLiveRating(keeper, -state.Rules.LiveRatingGoalConcededPenaltyBasisPoints);
        }

        state.Emit(side, type, scorerId, goalkeeper, ShotZone.Central, state.Rules.FreeKickGoalBasisPoints);
    }

    /// <summary>
    /// Records what the foul cost in live ratings: a foul is a duel lost badly, whether or not it drew a
    /// card — the card's own penalty, when there was one, was applied where the card was shown (engine-v3).
    /// </summary>
    private static void ApplyFoulLiveRatings(
        MatchState state,
        MatchSide possessionSide,
        DisciplineSimulator.FoulOutcome foul)
    {
        if (foul.FoulerId is Guid foulerId)
        {
            state.OpponentOf(possessionSide).AdjustLiveRating(
                foulerId,
                -state.Rules.LiveRatingTackleLostPenaltyBasisPoints);
        }
    }

    /// <summary>
    /// Resolves the 1v1 ground duel the carrier fights once the possession has progressed (engine-v3).
    /// </summary>
    /// <remarks>
    /// Winning the duel buys the creation that follows a beat man — the rules' dribble bonus — and losing
    /// it hands the initiative back, which is the same bonus taken away. The possession is not ended by a
    /// lost duel: the scramble already decides hard turnovers, and a tackled attack regrouping is the
    /// ordinary rhythm of a match.
    /// </remarks>
    /// <returns>The signed creation bonus the duel earned the attacking side, in basis points.</returns>
    private static int ResolveGroundDuel(MatchState state, MatchSide possessionSide)
    {
        var carrier = WeightedPick.From(
            state.SideOf(possessionSide).Outfield,
            slot => slot.Participant.Attributes.ValueOf(MatchAttributeName.Dribbling),
            state.Random);

        var tackler = WeightedPick.From(
            state.OpponentOf(possessionSide).Outfield,
            slot => slot.Participant.Attributes.ValueOf(MatchAttributeName.Tackling),
            state.Random);

        if (carrier is null || tackler is null)
        {
            return 0;
        }

        var duel = DuelResolver.ResolveGroundDuel(
            carrier.Participant,
            tackler.Participant,
            possessionSide == MatchSide.Home,
            state.OpponentOf(possessionSide).Instructions.Tackling,
            state.Rules,
            state.Random);

        // The carrier and the defender are the participants the duel actually picked, so the replay can name
        // them (engine-v4).
        state.RecordTouch(duel.AttackerId, PassageAction.Carry);
        state.RecordTouch(duel.DefenderId, PassageAction.Tackle);

        if (duel.AttackerWon)
        {
            state.SideOf(possessionSide).AdjustLiveRating(duel.AttackerId, state.Rules.LiveRatingTackleBonusBasisPoints);
            state.OpponentOf(possessionSide).AdjustLiveRating(duel.DefenderId, -state.Rules.LiveRatingTackleLostPenaltyBasisPoints);

            return state.Rules.DribbleCreationBonusBasisPoints;
        }

        state.SideOf(possessionSide).AdjustLiveRating(duel.AttackerId, -state.Rules.LiveRatingTackleLostPenaltyBasisPoints);
        state.OpponentOf(possessionSide).AdjustLiveRating(duel.DefenderId, state.Rules.LiveRatingTackleBonusBasisPoints);

        return -state.Rules.DribbleCreationBonusBasisPoints;
    }

    /// <summary>
    /// Resolves a possession that could not be progressed: either an offside or a plain turnover.
    /// </summary>
    /// <remarks>
    /// Fouls are not here, because the defending side's foul was already rolled before the progression
    /// attempt. The two failure modes left are the attacking side's own: caught offside, or simply losing the
    /// ball.
    /// </remarks>
    private static void ResolveFailedProgression(
        MatchState state,
        MatchSide side,
        SideRuntime attacker,
        PlannedPassage plan)
    {
        if (!state.Random.RollBasisPoints(state.Rules.OffsideShareOfTurnoverBasisPoints))
        {
            // A plain turnover: the defence clears the ball out towards the middle third (engine-v4).
            state.MoveBallAndRecord(plan.TurnoverPoint, PassageWaypointKind.Clearance, state.Rules.ClearanceAltitude);

            return;
        }

        state.MoveBallAndRecord(plan.OffsidePoint, PassageWaypointKind.Pass);

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
    private static void ResolveFailedCreation(MatchState state, MatchSide side, PlannedPassage plan)
    {
        if (!state.Random.RollBasisPoints(state.Rules.CornerShareOfFailedCreationBasisPoints))
        {
            // The attack broke down without a corner: the ball is cleared into the middle third (engine-v4).
            state.MoveBallAndRecord(plan.TurnoverPoint, PassageWaypointKind.Clearance, state.Rules.ClearanceAltitude);

            return;
        }

        // A corner is taken from the flag, so the Corner event carries the corner flag's coordinates
        // (engine-v4).
        state.MoveBallAndRecord(plan.CornerPoint, PassageWaypointKind.Cross, state.Rules.CrossAltitude);
        state.Emit(side, EngineEventType.Corner);

        if (state.Random.RollBasisPoints(state.Rules.CornerChanceBasisPoints))
        {
            state.MoveBallAndRecord(plan.HeaderPoint, PassageWaypointKind.Cross, state.Rules.HeaderAltitude);
            ChanceSimulator.ResolveCorner(state, side);
        }

        // The corner is cleared whether or not it produced a header, so the passage ends deeper.
        state.MoveBallAndRecord(plan.TurnoverPoint, PassageWaypointKind.Clearance, state.Rules.ClearanceAltitude);
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
            return EngineRulesV2.Certain;
        }

        var steps = Math.Abs(margin) - rules.GameStateMarginThresholdGoals + 1;

        if (margin > 0)
        {
            var reduction = Math.Min(
                rules.MaxGameStateModifierBasisPoints,
                rules.LeadingCreationStepBasisPoints * steps);

            return EngineRulesV2.Certain - reduction;
        }

        var increase = Math.Min(
            rules.MaxGameStateModifierBasisPoints,
            rules.TrailingCreationStepBasisPoints * steps);

        return EngineRulesV2.Certain + increase;
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
            _ => EngineRulesV2.Certain,
        };

        return Math.Max(rules.MinEffectivePossessionSeconds, Probability.Apply(seconds, multiplier));
    }
}
