using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Ratings;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Simulation;

/// <summary>
/// Resolves a chance on goal into a goal, a save, a block, the woodwork, or a miss (master plan §8.4).
/// </summary>
/// <remarks>
/// <para>
/// The last step of a possession, and the only place a goal is ever produced — `MAT-4` forbids the
/// independent per-minute roll that would make the score a separate process from the play. Everything here
/// reads ratings that were computed from attributes, so a goal is attributable to a shooter, a goalkeeper,
/// and a position on the pitch rather than to a draw.
/// </para>
/// <para>
/// Order matters and is part of the engine version. The outcome is decided in two stages: first whether the
/// shot beats the goalkeeper, then — for a shot that does not — which way it failed. The second stage draws
/// only when it is reached, so a goal consumes one draw and a miss consumes two, and changing that ordering
/// changes every historical result.
/// </para>
/// </remarks>
internal static class ChanceSimulator
{
    /// <summary>Resolves a chance created from open play.</summary>
    /// <param name="state">The match state.</param>
    /// <param name="side">The side attacking.</param>
    /// <param name="zone">The shot zone the possession's passage was aimed at (`engine-v4`).</param>
    /// <param name="shotPoint">Where the ball is played to, and the shot is taken from.</param>
    /// <param name="strike">Where the strike finishes, for each outcome it could have (`engine-v5`).</param>
    public static void ResolveOpenPlay(MatchState state, MatchSide side, ShotZone zone, SpatialPoint shotPoint, StrikePlan strike)
    {
        var attacker = state.SideOf(side);
        var defender = state.OpponentOf(side);

        // The player who has the ball as the attack goes in is the likeliest to shoot (`engine-v10`).
        var shooter = ChooseShooter(state, attacker, MatchAttributeName.Finishing, state.Passing.OnBall);

        // The ball is played on to the shot point, or carried there when the shooter is the one who has it.
        state.MoveBallAndRecord(
            shotPoint,
            shooter is not null && shooter.Participant.ParticipantId == state.Passing.OnBall
                ? PassageWaypointKind.Carry
                : PassageWaypointKind.Pass);

        if (shooter is null)
        {
            return;
        }

        // The shooter is the participant the simulation actually picked, and the ball is already at the
        // passage's shot point (engine-v4).
        state.RecordTouch(shooter.Participant.ParticipantId, PassageAction.Shot);
        state.Passing.Shooter = shooter.Participant.ParticipantId;

        var goalChance = GoalChance(state, defender, shooter, zone, headed: false);

        Resolve(state, side, shooter, zone, goalChance, strike);
    }

    /// <summary>
    /// Resolves a shot the holder takes from distance, in place of the attack creating a chance (`engine-v10`).
    /// </summary>
    /// <remarks>
    /// He shoots from where the ball is, and it is not an assisted chance: no pass is counted for it and no assist is
    /// credited. The shot is an ordinary one in every way but how likely it is to score, which the rules scale down
    /// from the zone he is in and the keeper he faces.
    /// </remarks>
    /// <param name="state">The match state.</param>
    /// <param name="side">The side attacking.</param>
    /// <param name="shooterId">The player on the ball.</param>
    /// <param name="strike">Where the strike finishes, for each outcome it could have, from where he stands.</param>
    public static void ResolveLongShot(MatchState state, MatchSide side, Guid shooterId, StrikePlan strike)
    {
        var rules = state.Rules;
        var shooter = state.SideOf(side).Outfield.FirstOrDefault(slot => slot.Participant.ParticipantId == shooterId);

        if (shooter is null)
        {
            return;
        }

        var zone = PassagePlanner.ZoneAt(strike.Origin, side == MatchSide.Home, rules);

        state.RecordTouch(shooterId, PassageAction.Shot);
        state.Passing.Shooter = shooterId;
        state.Passing.LongShot = true;

        var goalChance = Probability.Apply(
            GoalChance(state, state.OpponentOf(side), shooter, zone, headed: false),
            rules.LongShotGoalMultiplierBasisPoints);

        Resolve(state, side, shooter, zone, goalChance, strike);
    }

    /// <summary>
    /// Resolves a penalty, which is its own event pair: the award, then the outcome.
    /// </summary>
    /// <remarks>
    /// The ball is placed on the spot, the taker steps up, and the kick travels to the target its outcome
    /// decides (`engine-v5`). A goal is a kick-off for the other side; a miss, saved or put wide, is the
    /// defending side's restart from its goal area.
    /// </remarks>
    /// <param name="state">The match state.</param>
    /// <param name="side">The side awarded the penalty.</param>
    /// <param name="strike">Where the kick finishes, for each outcome it could have.</param>
    public static void ResolvePenalty(MatchState state, MatchSide side, StrikePlan strike)
    {
        var attacker = state.SideOf(side);
        var defender = state.OpponentOf(side);
        var taker = PenaltyTaker(attacker);

        // A penalty is a placement and then a strike: the ball is set down on the spot, not played to it.
        state.MoveBallAndRecord(strike.Origin, PassageWaypointKind.Restart);

        if (taker is not null)
        {
            state.RecordTouch(taker.Participant.ParticipantId, PassageAction.Penalty);
        }

        state.Emit(side, EngineEventType.PenaltyAwarded, taker?.Participant.ParticipantId, zone: ShotZone.Central);

        if (taker is null)
        {
            return;
        }

        var goalkeeper = defender.Goalkeeper?.Participant.ParticipantId;

        // The taker's finishing and composure against the keeper's reflexes (engine-v6); before it was a flat 76%.
        var penaltyChance = SetPieceDirector.PenaltyGoalChance(taker, defender.Goalkeeper, state.Rules);

        if (state.Random.RollBasisPoints(penaltyChance))
        {
            Score(state, side, taker);

            state.MoveBallAndRecord(strike.GoalTarget, PassageWaypointKind.Shot, strike.GoalAltitude);
            state.Emit(
                side,
                EngineEventType.PenaltyGoal,
                taker.Participant.ParticipantId,
                goalkeeper,
                ShotZone.Central,
                penaltyChance,
                at: strike.Origin);

            // The conversion and the beaten keeper, on the live scale (engine-v3).
            state.SideOf(side).AdjustLiveRating(taker.Participant.ParticipantId, state.Rules.LiveRatingGoalBonusBasisPoints);

            if (goalkeeper is Guid beatenKeeper)
            {
                state.OpponentOf(side).AdjustLiveRating(beatenKeeper, -state.Rules.LiveRatingGoalConcededPenaltyBasisPoints);
            }
        }
        else
        {
            // The engine records only that it was missed; whether the keeper is shown stopping it or the taker
            // putting it wide is the geometry stream's choice, and cannot move a result.
            var saved = strike.PenaltySaved && goalkeeper is not null;

            if (saved)
            {
                state.MoveBallAndRecord(strike.SaveTarget, PassageWaypointKind.Shot, strike.SaveAltitude);
                state.RecordTouch(goalkeeper!.Value, PassageAction.Save);
            }
            else
            {
                state.MoveBallAndRecord(strike.MissTarget, PassageWaypointKind.Shot, strike.MissAltitude);
            }

            state.Emit(
                side,
                EngineEventType.PenaltyMissed,
                taker.Participant.ParticipantId,
                goalkeeper,
                ShotZone.Central,
                penaltyChance,
                at: strike.Origin);

            // A missed penalty is a shot that did not test anybody worth naming (engine-v3).
            state.SideOf(side).AdjustLiveRating(taker.Participant.ParticipantId, -state.Rules.LiveRatingShotMissPenaltyBasisPoints);

            // Whether it was saved or put wide, the defending side restarts from its own goal area.
            state.RestartFromGoalArea(defender.Which, saved ? PassageRestartKind.KeeperBall : PassageRestartKind.GoalKick);
        }
    }

    /// <summary>
    /// Chooses who takes a corner: the side's outfield players, weighted by their set pieces and crossing
    /// (`engine-v6`).
    /// </summary>
    /// <param name="state">The match state.</param>
    /// <param name="side">The side taking the corner.</param>
    /// <returns>The taker, or null when the side has nobody to take it.</returns>
    public static ActiveSlot? ChooseCornerTaker(MatchState state, MatchSide side) =>
        WeightedPick.From(
            state.SideOf(side).Outfield,
            slot => DeliveryOf(slot, state.Rules),
            state.Random);

    /// <summary>
    /// Gets how much better than the baseline a corner taker's delivery is, in hundredths of an attribute point.
    /// </summary>
    /// <param name="taker">The corner taker.</param>
    /// <param name="rules">The rules in force.</param>
    public static int CornerDeliveryEdge(ActiveSlot taker, EngineRulesV2 rules) =>
        DeliveryOf(taker, rules) - (rules.CornerDeliveryBaseline * EffectiveSkill.Scale);

    private static int DeliveryOf(ActiveSlot slot, EngineRulesV2 rules) =>
        (EffectiveSkill.Hundredths(slot, MatchAttributeName.SetPieces, rules)
            + EffectiveSkill.Hundredths(slot, MatchAttributeName.Crossing, rules)) / 2;

    /// <summary>
    /// Resolves a header from a corner, contested with the defender marking it (engine-v3).
    /// </summary>
    /// <remarks>
    /// The corner becomes a chance only when the attacker wins the aerial duel; a lost header ends the
    /// passage with the defence heading clear. Both players' live ratings record the contest, and both players
    /// are recorded at the ball: the one who won it with a header, the other as having gone for it (`engine-v5`).
    /// </remarks>
    /// <param name="state">The match state.</param>
    /// <param name="side">The side taking the corner.</param>
    /// <param name="strike">Where the header finishes, for each outcome it could have.</param>
    /// <param name="deliveryEdge">How much better than the baseline the taker's delivery was, in hundredths of an attribute point.</param>
    /// <returns>Whether the attacker won the header and it became a chance on goal.</returns>
    public static bool ResolveCorner(MatchState state, MatchSide side, StrikePlan strike, int deliveryEdge)
    {
        var attacker = state.SideOf(side);
        var defender = state.OpponentOf(side);
        var headerer = ChooseShooter(state, attacker, MatchAttributeName.Heading);
        var marker = ChooseMarker(state, defender);

        if (headerer is null || marker is null)
        {
            return false;
        }

        // A good delivery puts the ball where the header can be won: the taker's edge is added to the
        // attacker's aerial score (engine-v6).
        if (!ContestHeader(state, side, headerer, marker, deliveryEdge * state.Rules.CornerDeliveryAerialWeight))
        {
            return false;
        }

        var goalChance = GoalChance(state, defender, headerer, ShotZone.Central, headed: true);

        Resolve(state, side, headerer, ShotZone.Central, goalChance, strike);

        return true;
    }

    /// <summary>
    /// Resolves the header at the end of an open-play cross, contested with the defender marking it (`engine-v10`).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The best-placed of the crosser's team-mates goes up for it, weighted by Heading, Positioning and how near the
    /// formation puts him to the ball, against the defender who can get to it. The better the crosser's Crossing, the likelier it is won, and a
    /// cross is played to where the attackers are, so the attacker starts ahead (<see cref="EngineRulesV2.CrossHeaderAttackerBonus"/>).
    /// When he wins it the header is the shot, from the box and with the zone the possession's chance was planned
    /// for, and the ball that set it up is the cross; when he loses, nothing is created and the caller gives the
    /// stopped ball its ending.
    /// </para>
    /// </remarks>
    /// <param name="state">The match state.</param>
    /// <param name="side">The side crossing.</param>
    /// <param name="zone">The shot zone the possession's chance was planned for.</param>
    /// <param name="strike">Where the header finishes, for each outcome it could have.</param>
    /// <param name="crosserId">The player who delivered the ball, who cannot be the one who heads it.</param>
    /// <returns>Whether the attacker won the header and it became a chance on goal.</returns>
    public static bool ResolveCross(MatchState state, MatchSide side, ShotZone zone, StrikePlan strike, Guid crosserId)
    {
        var rules = state.Rules;
        var attacker = state.SideOf(side);
        var defender = state.OpponentOf(side);
        var crosser = attacker.Active.FirstOrDefault(slot => slot.Participant.ParticipantId == crosserId);

        var delivery = crosser is null
            ? 0
            : EffectiveSkill.Hundredths(crosser, MatchAttributeName.Crossing, rules)
                - (rules.CrossHeaderDeliveryBaseline * EffectiveSkill.Scale);

        // Both go up from where they stand when the ball is in the box: the attacker the formation puts nearest it
        // with the best Heading, against the defender who can get to it.
        var isHome = side == MatchSide.Home;
        var attackers = OffBallModel.Place(attacker.Active, isHome, hasPossession: true, strike.Origin, attacker.Instructions, rules);
        var defenders = OffBallModel.Place(defender.Active, !isHome, hasPossession: false, strike.Origin, defender.Instructions, rules);

        OffBallPlayer? headerer = OffBallModel.Pick(
            [.. attackers.Where(placed => placed.Player.Slot.Family != MatchPositionFamily.Goalkeeper
                && placed.Player.Participant.ParticipantId != crosserId)],
            placed => OffBallModel.ReachWeight(
                placed,
                PositioningEdge.Apply(
                    EffectiveSkill.Hundredths(placed.Player, MatchAttributeName.Heading, rules),
                    PositioningEdge.Of(placed.Player, rules)),
                strike.Origin,
                isHome,
                rules),
            state.Random);

        OffBallPlayer? marker = OffBallModel.Pick(
            [.. defenders.Where(placed => placed.Player.Slot.Family != MatchPositionFamily.Goalkeeper)],
            placed => OffBallModel.ReachWeight(
                placed,
                PositioningEdge.Apply(
                    EffectiveSkill.Hundredths(placed.Player, MatchAttributeName.Heading, rules),
                    PositioningEdge.OfDefender(placed.Player, rules)),
                strike.Origin,
                attackingFor: null,
                rules),
            state.Random);

        if (headerer is not { } up || marker is not { } against)
        {
            return false;
        }

        var bonus = rules.CrossHeaderAttackerBonus + (delivery * rules.CrossHeaderDeliveryAerialWeight);

        if (!ContestHeader(state, side, up.Player, against.Player, bonus))
        {
            return false;
        }

        // The cross is the ball that set the header up, and the header is the shot it created.
        state.Passing.CreatedShot();
        state.Passing.Shooter = up.Player.Participant.ParticipantId;

        Resolve(state, side, up.Player, zone, GoalChance(state, defender, up.Player, zone, headed: true), strike);

        return true;
    }

    /// <summary>
    /// Chooses the defender who goes up against a header: the one with the best Heading, as his Marking and
    /// Positioning let him get to it (`engine-v10`).
    /// </summary>
    private static ActiveSlot? ChooseMarker(MatchState state, SideRuntime defender) =>
        WeightedPick.From(
            defender.Outfield,
            slot => PositioningEdge.Apply(
                EffectiveSkill.Hundredths(slot, MatchAttributeName.Heading, state.Rules),
                PositioningEdge.OfDefender(slot, state.Rules)),
            state.Random);

    /// <summary>
    /// Fights the aerial duel for a header, records its live ratings and who went up for it, and says who won.
    /// </summary>
    /// <remarks>
    /// Both players' live ratings record the contest, and both are recorded at the ball: the one who won it with a
    /// header, the other as having gone for it (`engine-v5`).
    /// </remarks>
    private static bool ContestHeader(MatchState state, MatchSide side, ActiveSlot headerer, ActiveSlot marker, int attackerBonus)
    {
        var attacker = state.SideOf(side);
        var defender = state.OpponentOf(side);

        var aerial = DuelResolver.ResolveAerialDuel(
            DuelContender.Of(attacker, headerer),
            DuelContender.Of(defender, marker),
            side == MatchSide.Home,
            attackerBonus,
            state.Rules,
            state.Random);

        attacker.AdjustLiveRating(
            aerial.AttackerId,
            aerial.AttackerWon ? state.Rules.LiveRatingAerialBonusBasisPoints : -state.Rules.LiveRatingAerialLostPenaltyBasisPoints);
        defender.AdjustLiveRating(
            aerial.DefenderId,
            aerial.AttackerWon ? -state.Rules.LiveRatingAerialLostPenaltyBasisPoints : state.Rules.LiveRatingAerialBonusBasisPoints);

        // Both jumpers are named by the simulation, so both are at the ball: whoever won it headed it.
        var winner = aerial.AttackerWon ? headerer : marker;
        var loser = aerial.AttackerWon ? marker : headerer;

        state.RecordTouch(winner.Participant.ParticipantId, PassageAction.Header);
        state.RecordTouch(loser.Participant.ParticipantId, PassageAction.Run);

        return aerial.AttackerWon;
    }

    private static void Resolve(
        MatchState state,
        MatchSide side,
        ActiveSlot shooter,
        ShotZone zone,
        int goalChance,
        StrikePlan strike)
    {
        var opponent = state.OpponentOf(side);
        var goalkeeper = opponent.Goalkeeper?.Participant.ParticipantId;
        var shooterId = shooter.Participant.ParticipantId;

        // The strike travels to where its outcome puts it, and the event follows the ball there: it is
        // positioned after the waypoint that ends the strike, and stamped where the strike was taken from.
        if (state.Random.RollBasisPoints(goalChance))
        {
            Score(state, side, shooter);

            state.MoveBallAndRecord(strike.GoalTarget, PassageWaypointKind.Shot, strike.GoalAltitude);
            var goal = state.Emit(side, EngineEventType.Goal, shooterId, goalkeeper, zone, qualityBasisPoints: goalChance, at: strike.Origin);

            // The assister is the last thing a goal decides, and it is decided from a stream of its own so
            // that crediting one cannot move a single draw of the play (see AssistPlanner).
            AssistPlanner.Credit(state, side, shooterId, goal.Sequence);

            // The scorer and the beaten keeper, on the live scale the match viewer showed (engine-v3).
            state.SideOf(side).AdjustLiveRating(shooterId, state.Rules.LiveRatingGoalBonusBasisPoints);

            if (goalkeeper is Guid beatenKeeper)
            {
                state.OpponentOf(side).AdjustLiveRating(beatenKeeper, -state.Rules.LiveRatingGoalConcededPenaltyBasisPoints);
            }

            return;
        }

        // Not a goal. The woodwork and a block are decided before the goalkeeper is asked, because a shot
        // that hits the post never reaches them and a shot that is blocked never gets there either.
        var roll = state.Random.NextBasisPoints();
        var woodwork = state.Rules.WoodworkShareBasisPoints;

        if (roll < woodwork)
        {
            state.MoveBallAndRecord(strike.WoodworkTarget, PassageWaypointKind.Shot, strike.WoodworkAltitude);
            state.Emit(side, EngineEventType.Woodwork, shooterId, goalkeeper, zone, qualityBasisPoints: goalChance, at: strike.Origin);

            // The frame sends it back into the box, where it is loose: nobody owns a rebound, so the next
            // possession is contested.
            state.MoveBallAndRecord(strike.ReboundPoint, PassageWaypointKind.Clearance, strike.GoalAltitude);

            return;
        }

        if (roll < woodwork + state.Rules.BlockedShareBasisPoints)
        {
            state.MoveBallAndRecord(strike.BlockPoint, PassageWaypointKind.Shot);
            state.Emit(side, EngineEventType.ShotBlocked, shooterId, goalkeeper, zone, qualityBasisPoints: goalChance, at: strike.Origin);

            // A block is a loose ball where it was stopped, and the next possession is contested.
            return;
        }

        var quality = Probability.Differential(
            EffectiveSkill.Hundredths(shooter, MatchAttributeName.Finishing, state.Rules),
            KeeperQuality(opponent, state.Rules));

        var saveChance = Probability.Band(
            state.Rules.BaseSaveBasisPoints
                + Probability.Swing(
                    quality,
                    state.Rules.ShotQualitySwingBasisPoints,
                    state.Rules.ShotContestReference * EffectiveSkill.Scale),
            state.Rules.MinSaveBasisPoints,
            state.Rules.MaxSaveBasisPoints);

        if (state.Random.RollBasisPoints(saveChance))
        {
            // The goalkeeper gets to it near the line, and the save is where he is, not where it was struck from.
            state.MoveBallAndRecord(strike.SaveTarget, PassageWaypointKind.Shot, strike.SaveAltitude);

            if (goalkeeper is Guid savingKeeper)
            {
                state.RecordTouch(savingKeeper, PassageAction.Save);
            }

            state.Emit(side, EngineEventType.ShotSaved, shooterId, goalkeeper, zone, qualityBasisPoints: goalChance, at: strike.Origin);

            // An effort that tested the goalkeeper is worth something; a miss is not (engine-v3).
            state.SideOf(side).AdjustLiveRating(shooterId, state.Rules.LiveRatingShotBonusBasisPoints);

            if (goalkeeper is Guid ratedKeeper)
            {
                state.OpponentOf(side).AdjustLiveRating(ratedKeeper, state.Rules.LiveRatingSaveBonusBasisPoints);
            }

            // A keeper who stops it holds it, and the defence restarts from its goal area (engine-v5).
            state.RestartFromGoalArea(opponent.Which, PassageRestartKind.KeeperBall);
        }
        else
        {
            state.MoveBallAndRecord(strike.MissTarget, PassageWaypointKind.Shot, strike.MissAltitude);
            state.Emit(side, EngineEventType.ShotOffTarget, shooterId, goalkeeper, zone, qualityBasisPoints: goalChance, at: strike.Origin);

            state.SideOf(side).AdjustLiveRating(shooterId, -state.Rules.LiveRatingShotMissPenaltyBasisPoints);

            // A miss goes out for a goal kick (engine-v5).
            state.RestartFromGoalArea(opponent.Which, PassageRestartKind.GoalKick);
        }
    }

    private static void Score(MatchState state, MatchSide side, ActiveSlot scorer)
    {
        var runtime = state.SideOf(side);
        var scorerId = scorer.Participant.ParticipantId;

        runtime.Goals.TryGetValue(scorerId, out var goals);
        runtime.Goals[scorerId] = goals + 1;

        // A goal is a restart: the side that conceded kicks off from the centre spot (engine-v5).
        state.RestartAfterGoal(side);

        state.AddGoalStoppage();

        // Scoring lifts the scorers and drops the conceders, bounded so a rout cannot empty a side's morale.
        runtime.ShiftMorale(state.Rules.MoraleGainPerGoalBasisPoints, state.Rules.MaxMoraleDriftBasisPoints, state.Rules);
        state
            .OpponentOf(side)
            .ShiftMorale(-state.Rules.MoraleLossPerConcededGoalBasisPoints, state.Rules.MaxMoraleDriftBasisPoints, state.Rules);
    }

    /// <summary>
    /// Computes the goal probability of one shot from the shooter, the goalkeeper, and the position.
    /// </summary>
    private static int GoalChance(
        MatchState state,
        SideRuntime defender,
        ActiveSlot shooter,
        ShotZone zone,
        bool headed)
    {
        ArgumentNullException.ThrowIfNull(defender);

        var rules = state.Rules;

        var zoneMultiplier = zone switch
        {
            ShotZone.Central => rules.CentralZoneMultiplierBasisPoints,
            ShotZone.InsideLeft or ShotZone.InsideRight => rules.InsideZoneMultiplierBasisPoints,
            ShotZone.WideLeft or ShotZone.WideRight => rules.WideZoneMultiplierBasisPoints,
            _ => rules.InsideZoneMultiplierBasisPoints,
        };

        var baseChance = Probability.Apply(rules.BaseShotGoalBasisPoints, zoneMultiplier);

        var contest = Probability.Differential(
            EffectiveSkill.Hundredths(shooter, headed ? MatchAttributeName.Heading : MatchAttributeName.Finishing, rules),
            KeeperQuality(defender, rules));

        // Finishing counts on every shot, a header included: a header decides how the ball is struck, and finishing
        // whether it goes in. It is read against a reference skill, so a side of average finishers is untouched
        // (`engine-v10`).
        var finishing = Probability.Swing(
            EffectiveSkill.Hundredths(shooter, MatchAttributeName.Finishing, rules) - (rules.FinishingGoalReference * EffectiveSkill.Scale),
            rules.FinishingGoalSwingBasisPoints,
            rules.ShotContestReference * EffectiveSkill.Scale);

        return Probability.Band(
            baseChance
                + Probability.Swing(contest, rules.ShotQualitySwingBasisPoints, rules.ShotContestReference * EffectiveSkill.Scale)
                + finishing,
            rules.MinShotGoalBasisPoints,
            rules.MaxShotGoalBasisPoints);
    }

    /// <summary>
    /// Reads the defending side's goalkeeping as a single attribute-scale value.
    /// </summary>
    /// <remarks>
    /// The Goalkeeping unit rating divided back down to the attribute scale, in hundredths of an attribute point
    /// like <see cref="EffectiveSkill.Hundredths"/>, so it can be compared against a shooter's own skill. One measure for every shot is deliberate: the alternative — reading specific
    /// goalkeeper attributes per shot type — would let a headed chance and a placed shot disagree about how
    /// good the same goalkeeper is.
    /// <para>
    /// A side whose goalkeeper has been sent off has no player in the Goalkeeping unit, so this returns the
    /// floor and every shot against them is close to a formality. That is the correct shape: `MAT-6` has no
    /// mechanism for naming a new goalkeeper mid-match, so a side that loses theirs is in trouble.
    /// </para>
    /// </remarks>
    private static int KeeperQuality(SideRuntime defender, EngineRulesV2 rules) =>
        defender.Ratings.Goalkeeping * EffectiveSkill.Scale / rules.AttributeRatingFactor;

    /// <summary>
    /// Chooses the shooter, weighted by the attribute the chance asks for.
    /// </summary>
    /// <remarks>
    /// The weight is deliberately the attribute itself rather than a flat draw over the eleven: a side's
    /// best finisher takes more of its shots, which is what makes the Finishing rating mean something. A
    /// goalkeeper is excluded — a goalkeeper taking a shot from open play is not a thing this engine models
    /// — and an attribute of 1 is floored at 1 by the picker so nobody is impossible. Since `engine-v10` the
    /// weight is the skill times the player's Positioning edge: the best finisher takes the shot, and the
    /// one who finds the space takes it more often than one who stands where the defenders are. A player the
    /// move was played through to (<paramref name="favoured"/>) has his weight multiplied again, because he has the
    /// ball, and one who put it there (<paramref name="excluded"/>) is not a candidate.
    /// </remarks>
    private static ActiveSlot? ChooseShooter(
        MatchState state,
        SideRuntime side,
        MatchAttributeName attribute,
        Guid? favoured = null,
        Guid? excluded = null)
    {
        var candidates = side.Outfield
            .Where(slot => slot.Participant.ParticipantId != excluded)
            .ToList();

        return WeightedPick.From(
            candidates,
            slot =>
            {
                var weight = PositioningEdge.Apply(
                    EffectiveSkill.Hundredths(slot, attribute, state.Rules),
                    PositioningEdge.Of(slot, state.Rules));

                return slot.Participant.ParticipantId == favoured
                    ? Probability.Apply(weight, state.Rules.ShooterChainBonusBasisPoints)
                    : weight;
            },
            state.Random);
    }

    /// <summary>
    /// Chooses the penalty taker: the best finisher on the pitch, ties broken by identity.
    /// </summary>
    /// <remarks>
    /// A designated taker rather than a draw, because a penalty shootout's taker is a decision and not a
    /// chance event, and because a stable choice is reproducible without consuming a draw.
    /// </remarks>
    private static ActiveSlot? PenaltyTaker(SideRuntime side) =>
        side.Outfield.Count == 0
            ? null
            : side.Outfield
                .OrderByDescending(slot => slot.Participant.Attributes.ValueOf(MatchAttributeName.Finishing))
                .ThenBy(slot => slot.Participant.ParticipantId)
                .First();
}
