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
/// <para>
/// Since `engine-v5` a possession is also recorded completely: what the play drew is decided first — the foul,
/// the scramble, the progression — and only then is the ball's path written down, so a possession that ends
/// early records an approach that ends early. The record never reads the play stream and never writes to the
/// match, so it cannot move a result.
/// </para>
/// </remarks>
internal static class PossessionSimulator
{



    /// <summary>What one possession is being played with, so the phases do not each take eight parameters.</summary>
    /// <param name="Side">The side in possession.</param>
    /// <param name="Defending">The side defending.</param>
    /// <param name="Restart">The restart the possession began with.</param>
    /// <param name="Plan">The possession's planned geometry.</param>
    /// <param name="Derived">The possession's geometry stream.</param>
    /// <param name="Counter">Whether the possession is a counter-attack on a ball regained from play (`engine-v11`).</param>
    /// <param name="Backfire">How many steps of caution the side has when it wins the ball back from a counter that failed, or zero (`engine-v11`).</param>
    private sealed record Possession(
        MatchSide Side,
        MatchSide Defending,
        PassageRestartKind Restart,
        PlannedPassage Plan,
        Pcg32 Derived,
        bool Counter,
        int Backfire);

    /// <summary>The contested loose ball that opens some possessions, and who contested it (`engine-v5`).</summary>
    /// <param name="Lost">Whether the side in possession lost it.</param>
    /// <param name="AttackerId">The player from the side in possession.</param>
    /// <param name="DefenderId">The player from the side defending.</param>
    private readonly record struct Scramble(bool Lost, Guid AttackerId, Guid DefenderId);

    /// <summary>Plays both halves and stoppage.</summary>
    /// <param name="state">The match state.</param>
    public static void Run(MatchState state)
    {
        state.BeginHalf(firstHalf: true);
        state.NextRestart = new PendingRestart(MatchSide.Home, PassageRestartKind.KickOff, SpatialPoint.Center);
        state.Emit(MatchSide.Home, EngineEventType.KickOff);
        state.CaptureLiveMetrics();
        RunHalf(state);
        state.EndHalf();
        state.Emit(MatchSide.Home, EngineEventType.HalfTime);

        state.Home.ApplyHalfTimeRecovery(state.Rules);
        state.Away.ApplyHalfTimeRecovery(state.Rules);

        // The sides change ends' kick-off: the away side takes the second half's, from the centre spot, and
        // whatever dead ball the first half ended on is forgotten (`MAT-12`).
        state.BeginHalf(firstHalf: false);
        state.MoveBall(SpatialPoint.Center);
        state.NextRestart = new PendingRestart(MatchSide.Away, PassageRestartKind.KickOff, SpatialPoint.Center);
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

        // A dead ball belongs to the side the rules give it to, and the possession it is taken in is the very
        // next one: it is read and cleared here, so nothing is left pending for a later possession (`MAT-12`).
        var restart = state.NextRestart;
        state.NextRestart = null;

        var possessionSide = restart?.Side ?? ChoosePossession(state);
        var attacker = state.SideOf(possessionSide);

        // The possession starts at the moment the clock is read, before it advances, so the possessions tile
        // each half rather than every one of them being zero seconds long (`engine-v5`).
        var startSeconds = state.ClockSeconds;
        var seconds = DrawPossessionSeconds(state, attacker);

        state.ClockSeconds += seconds;
        attacker.PossessionSeconds += seconds;

        // A possession's work is done by both sides, so both tire.
        state.Home.ApplyLoad(rules);
        state.Away.ApplyLoad(rules);

        // Ratings follow the players' legs, morale, and the scoreline, a minute at a time (engine-v6).
        state.RefreshRatings();

        SubstitutionPlanner.ConsiderBothSides(state);

        // The possession is played along a real passage now: the ball starts where the last one left it — or
        // at a restart — and progresses into the attacking third through a handful of touches. The geometry
        // comes from a per-possession derived stream, so it is reproducible but can never move a play draw
        // (engine-v4).
        state.PossessionOrdinal++;

        var derived = PassagePlanner.CreateStream(state);
        var plan = PassagePlanner.Plan(state, possessionSide, derived, restart);

        // A ball won back from play may become a counter-attack. The roll is drawn from a stream of its own, so it can
        // never move a play draw: what it changes is the thresholds of the possession it starts (`engine-v11`).
        var fromPlay = restart is null && state.LastPossessionSide == MatchInputV1.OpponentOf(possessionSide);

        // A side that sits deep and wins the ball back from a counter that failed has the other side stretched: it is
        // likelier to break in its turn, and what it creates is worth more.
        var backfire = fromPlay && state.FailedCounterBy == MatchInputV1.OpponentOf(possessionSide)
            ? Math.Max(0, -Posture(attacker.Instructions))
            : 0;

        var counter = fromPlay && RollCounter(state, attacker, backfire);

        var possession = new Possession(
            possessionSide,
            MatchInputV1.OpponentOf(possessionSide),
            restart?.Kind ?? PassageRestartKind.None,
            plan,
            derived,
            counter,
            backfire);

        state.BeginPassage(possessionSide, startSeconds, possession.Restart, counter);

        // The defending side's foul comes first: a foul ends the passage of play before it develops, which is
        // what makes it the defending side's event rather than a consequence of the attack. It is only rolled
        // here; it is put on the pitch once the possession knows where it happened (`engine-v5`).
        var foul = DisciplineSimulator.RollFoul(state, possession.Defending);

        if (foul.Committed)
        {
            PlayFoul(state, possession, foul);

            return;
        }

        PlayOpen(state, possession);
    }

    /// <summary>
    /// Plays a possession the defending side fouled in: a penalty, a free kick struck or crossed, or a quick
    /// free kick with no shot in it.
    /// </summary>
    /// <remarks>
    /// What the foul gives is decided before the ball's path is written down, because it decides where the
    /// foul was: a penalty is a foul in the box, and a free kick in range is a foul in the attacking third. The
    /// draws are taken in the order they always were — the foul, the fouler, the card, the penalty, the free
    /// kick award — so the result is the same whatever is recorded.
    /// </remarks>
    private static void PlayFoul(MatchState state, Possession possession, DisciplineSimulator.FoulRoll foul)
    {
        var rules = state.Rules;
        var plan = possession.Plan;
        var side = possession.Side;

        var penalty = state.Random.RollBasisPoints(rules.PenaltyFromFoulBasisPoints);
        var attackingX = AttackingX(plan.PressurePoint, side);

        var freeKick = !penalty
            && attackingX >= rules.FreeKickShootingRangeX
            && state.Random.RollBasisPoints(rules.FreeKickAwardBasisPoints);

        RecordApproach(
            state,
            possession,
            plan.Approach,
            plan.ApproachEndsInCross,
            passer: true,
            lost: false,
            scramble: null,
            OpeningCarrier(state, possession, scramble: null));

        if (penalty)
        {
            // A penalty is a foul in the box, so the attack is played in there before the defender brings the
            // attacker down.
            state.MoveBallAndRecord(plan.BoxFoulPoint, PassageWaypointKind.Pass);
            state.Passing.Completed(1);
        }

        AwardFromFoul(state, possession, foul, penalty, freeKick, fouledId: null);
    }

    /// <summary>
    /// Puts a foul on the pitch and resolves what it gave the attacking side: a penalty, a free kick struck or
    /// crossed, or a quick restart.
    /// </summary>
    /// <remarks>
    /// Shared by the foul rolled before a possession develops and the foul a ground duel decides (engine-v6).
    /// A duel names the player it fouled; the other foul draws him from the geometry stream, so naming him
    /// cannot move a play draw.
    /// </remarks>
    private static void AwardFromFoul(
        MatchState state,
        Possession possession,
        DisciplineSimulator.FoulRoll foul,
        bool penalty,
        bool freeKick,
        Guid? fouledId)
    {
        var rules = state.Rules;
        var plan = possession.Plan;
        var side = possession.Side;

        // The two players the foul is between are both at the ball when it is committed: the fouler the
        // engine named, and the player fouled — who, when the approach was played through, is the one who has just
        // been given the ball (`engine-v10`).
        var fouled = fouledId
            ?? state.Passing.ChainHolder
            ?? PickOutfield(state, side, possession.Derived, MatchAttributeName.Dribbling);

        if (foul.Fouler is { } fouler)
        {
            state.RecordTouch(fouler.Participant.ParticipantId, PassageAction.Tackle);
        }

        if (fouled is Guid carrierId)
        {
            state.RecordTouch(carrierId, PassageAction.Carry);
        }

        var outcome = DisciplineSimulator.ApplyFoul(state, possession.Defending, foul);

        ApplyFoulLiveRatings(state, side, outcome);

        if (penalty)
        {
            ChanceSimulator.ResolvePenalty(state, side, PassagePlanner.StrikeFrom(plan, plan.PenaltySpot, rules));
            Finish(state, PassageOutcome.Penalty);

            return;
        }

        if (freeKick)
        {
            Finish(state, ResolveFreeKick(state, possession));

            return;
        }

        // A foul with no shot in it is a free kick for the side that was fouled, taken where it was committed.
        state.RestartWithFreeKick(side, state.Ball.GroundPoint);
        Finish(state, PassageOutcome.Foul);
    }

    /// <summary>
    /// Plays a possession that ends in a foul the final-third ground duel decided (`engine-v6`).
    /// </summary>
    /// <remarks>
    /// The defender who lost the duel brought the carrier down at the edge of the box, so a penalty is far more
    /// likely than for a foul rolled before the move developed, and a free kick in range is certain to be in
    /// range. The draws are the same as ever: the penalty, the free kick award, then the card.
    /// </remarks>
    private static void PlayDuelFoul(MatchState state, Possession possession, Guid defenderId, Guid carrierId)
    {
        var rules = state.Rules;
        var plan = possession.Plan;
        var side = possession.Side;

        var penalty = state.Random.RollBasisPoints(rules.DuelFoulPenaltyBasisPoints);
        var attackingX = AttackingX(state.Ball.GroundPoint, side);

        var freeKick = !penalty
            && attackingX >= rules.FreeKickShootingRangeX
            && state.Random.RollBasisPoints(rules.FreeKickAwardBasisPoints);

        var foul = DisciplineSimulator.RollDuelFoul(state, possession.Defending, defenderId);

        if (penalty)
        {
            state.MoveBallAndRecord(plan.BoxFoulPoint, PassageWaypointKind.Pass);
        }

        AwardFromFoul(state, possession, foul, penalty, freeKick, carrierId);
    }

    /// <summary>
    /// Plays a possession that was not fouled in: the scramble, the progression, creation, and the chance.
    /// </summary>
    private static void PlayOpen(MatchState state, Possession possession)
    {
        var rules = state.Rules;
        var plan = possession.Plan;
        var side = possession.Side;
        var attacker = state.SideOf(side);
        var defender = state.OpponentOf(side);

        // A loose ball opens only a share of passages: most possessions begin with the ball already under
        // control, and the contested 50/50 is the exception rather than the tax on every attack.
        Scramble? scramble = null;

        if (state.Random.RollBasisPoints(rules.ScrambleOpeningBasisPoints))
        {
            scramble = ResolveScramble(state, side, plan.Approach[0]);
        }

        if (scramble is { Lost: true } lostScramble)
        {
            // The ball is lost close to where it began: the approach is cut short, the defender has it, and
            // the defence clears.
            RecordApproach(
                state,
                possession,
                PassagePlanner.CutApproach(plan, plan.ScrambleCutBasisPoints),
                endsInCross: false,
                passer: false,
                lost: true,
                scramble: null,
                OpeningCarrier(state, possession, scramble: null));

            state.RecordTouch(lostScramble.AttackerId, PassageAction.Run);
            state.RecordTouch(lostScramble.DefenderId, PassageAction.Interception);

            ClearBall(state, plan);
            Finish(state, PassageOutcome.ScrambleLost);

            return;
        }

        // The approach is played through before the progression is rolled, because how it was played is part of what
        // the roll weighs: a ball into a crowd is likelier to be lost than one to a free man (`engine-v10`). The
        // chain draws from a stream of its own, so playing it early moves no play draw.
        var carrier = OpeningCarrier(state, possession, scramble);

        // It runs on one pass further than the approach does: the ball played into the final third, which only a
        // progressing attack plays. The approach's legs come out the same with or without it.
        var approachLegs = plan.Approach.Count - 1;

        var chain = carrier is Guid first
            ? ReceiverChooser.Choose(state, side, [.. plan.Approach, plan.EntryPoint], first, lost: false, unpulledFrom: approachLegs, canShoot: true)
            : null;

        var quality = chain?.Quality(rules, approachLegs);

        var control = Probability.Differential(attacker.Ratings.BuildUp, defender.Ratings.DefensivePressure);

        // A counter-attack finds the opponent out of shape or well placed, by how it has set up (`engine-v11`).
        var (counterProgress, counterCreation) = CounterEdge(state, possession);

        var progressChance = Probability.Band(
            rules.BaseProgressBasisPoints
                + Probability.Swing(
                    control,
                    rules.ProgressControlSwingBasisPoints,
                    rules.RatingDifferentialReference)
                + (quality?.ProgressNudge(rules) ?? 0)
                + counterProgress,
            rules.MinProgressBasisPoints,
            rules.MaxProgressBasisPoints);

        if (!state.Random.RollBasisPoints(progressChance))
        {
            // The attack breaks down part of the way up the pitch, not after it has arrived. Its chain is played again
            // as far as it got, with the pass that was lost known.
            RecordApproach(
                state,
                possession,
                PassagePlanner.CutApproach(plan, plan.ProgressionCutBasisPoints),
                endsInCross: false,
                passer: true,
                lost: true,
                scramble,
                carrier);

            // The ball was lost on the last pass of the approach: the one an interception or an offside
            // flag ended (`engine-v7`).
            state.Passing.LostApproach();

            Finish(state, ResolveFailedProgression(state, possession, attacker));

            return;
        }

        RecordApproach(state, possession, plan.Approach, plan.ApproachEndsInCross, passer: true, lost: false, scramble, carrier, chain);

        if (chain is { EndsInShot: true })
        {
            // The holder took it on himself from where he stood, and the shot is the whole of the chance: there is no
            // ball into the final third, no duel and no creation roll (`engine-v10`).
            ChanceSimulator.ResolveLongShot(
                state,
                side,
                chain.Holder,
                PassagePlanner.StrikeFrom(plan, state.Ball.GroundPoint, rules));
            Finish(state, PassageOutcome.OpenPlayShot);

            return;
        }

        // The ball is played into the final third, where the last defender engages. When the approach was played
        // through, the holder chooses who gets it there, and that is the carrier the duel is fought with (`engine-v10`).
        RecordEntry(state, plan, chain);

        var creation = Probability.Differential(
            attacker.Ratings.Creation + (attacker.Ratings.Finishing / 2),
            defender.Ratings.DefensiveShape + (defender.Ratings.Goalkeeping / 2));

        // The 1v1 the carrier fights to reach the creation phase: a beat man makes the chance more likely,
        // a tackle shuts the passage down. The carrier is the player the ball was played in to (`engine-v10`).
        var duel = ResolveGroundDuel(state, side, state.Passing.OnBall);

        if (duel.FoulerId is Guid foulerId)
        {
            // The defender brought him down: the move ends in a free kick, a penalty, or a card.
            PlayDuelFoul(state, possession, foulerId, duel.FouledId!.Value);

            return;
        }

        var creationChance = Probability.Band(
            rules.BaseCreationBasisPoints
                + duel.CreationBonus
                + counterCreation
                + (possession.Backfire * rules.CounterBackfireCreationBasisPoints)
                + Probability.Swing(
                    creation,
                    rules.CreationSwingBasisPoints,
                    rules.RatingDifferentialReference)
                + (quality?.CreationNudge(rules) ?? 0),
            rules.MinCreationBasisPoints,
            rules.MaxCreationBasisPoints);

        creationChance = Probability.Apply(creationChance, GameStateModifier(state, side));

        // A side that asked for a lane takes more or fewer shots, to pay for where it shoots from (`engine-v9`).
        creationChance = Probability.Apply(
            creationChance,
            PassagePlanner.ChanceVolume(state.SideOf(side).Instructions.PassFocus, rules));

        // A cross is a chance more readily than a ground ball, and the header that follows decides how many are
        // (`engine-v10`).
        if (plan.ApproachEndsInCross)
        {
            creationChance = Probability.Band(
                Probability.Apply(creationChance, rules.CrossCreationMultiplierBasisPoints),
                0,
                EngineRulesV2.Certain);
        }

        if (!state.Random.RollBasisPoints(creationChance))
        {
            Finish(state, ResolveFailedCreation(state, possession));

            return;
        }

        if (plan.ApproachEndsInCross && state.Passing.OnBall is Guid crosser)
        {
            // A move that ended in a cross ends in a header, which the defence can win (`engine-v10`).
            Finish(state, ResolveCrossedChance(state, possession, crosser));

            return;
        }

        // The attack breaks through: the ball is played on to the shot point and struck from there.
        state.Passing.CreatedShot();
        ChanceSimulator.ResolveOpenPlay(state, side, plan.Zone, plan.ShotPoint, PassagePlanner.StrikeFrom(plan, plan.ShotPoint, rules));
        Finish(state, PassageOutcome.OpenPlayShot);
    }

    /// <summary>The stride that separates the counter-attack streams from the geometry streams.</summary>
    private const ulong CounterStreamStride = 1_000_033UL;

    /// <summary>Rolls whether a ball regained from play becomes a counter-attack, on a stream that moves no play draw (`engine-v11`).</summary>
    private static bool RollCounter(MatchState state, SideRuntime attacker, int backfire)
    {
        var rules = state.Rules;

        var chance = (attacker.Instructions.CounterAttack
            ? rules.CounterStartWithInstructionBasisPoints
            : rules.CounterStartBasisPoints)
            + (backfire * rules.CounterBackfireStartBasisPoints);

        var stream = new Pcg32(unchecked((state.Input.Seed * CounterStreamStride) + (ulong)state.PossessionOrdinal));

        return stream.RollBasisPoints(chance);
    }

    /// <summary>
    /// Gets how a counter-attack's chance of progressing and of creating moves, in basis points: up against an
    /// opponent that has committed forward and down against one that sits deep (`engine-v11`).
    /// </summary>
    /// <remarks>
    /// The opponent's posture is its mentality and its defensive line, each a step either side of the middle; the
    /// edge is the rules' base plus a step per point of posture. A deep side is well placed to cut a long ball out, so
    /// against it the counter is worth less than an ordinary attack and the instruction backfires a little.
    /// </remarks>
    private static (int Progress, int Creation) CounterEdge(MatchState state, Possession possession)
    {
        if (!possession.Counter)
        {
            return (0, 0);
        }

        var defender = state.SideOf(possession.Defending);
        var (progress, creation) = CounterEdge(state.Rules, defender.Instructions);
        var (recoveryProgress, recoveryCreation) = CounterRecovery(state.Rules, defender.Outfield);

        return (progress + recoveryProgress, creation + recoveryCreation);
    }

    /// <summary>Gets what a counter-attack gains or loses against an opponent set up as given (`engine-v11`).</summary>
    /// <param name="rules">The rules in force.</param>
    /// <param name="opponent">The instructions of the side being countered.</param>
    internal static (int Progress, int Creation) CounterEdge(EngineRulesV2 rules, MatchInstructionsV1 opponent)
    {
        var posture = Posture(opponent);

        return (
            rules.CounterProgressBasisPoints + (posture * rules.CounterProgressPerPostureBasisPoints),
            rules.CounterCreationBasisPoints + (posture * rules.CounterCreationPerPostureBasisPoints));
    }

    /// <summary>How far a side has committed forward: its mentality and its defensive line, each a step either side of the middle.</summary>
    private static int Posture(MatchInstructionsV1 instructions) =>
        ((int)instructions.Mentality - (int)MatchMentality.Balanced)
        + ((int)instructions.DefensiveLine - (int)MatchDefensiveLine.Normal);

    /// <summary>
    /// Gets how much the pace and acceleration of the defenders and midfielders of the side being countered cut a counter
    /// down, in basis points (negative) or leave it room (positive) (`engine-v11`). A quick back line is in position
    /// before the ball arrives or catches the runner from behind and tackles; a slow one is not.
    /// </summary>
    /// <param name="rules">The rules in force.</param>
    /// <param name="outfield">The side's outfield players on the pitch.</param>
    internal static (int Progress, int Creation) CounterRecovery(EngineRulesV2 rules, IReadOnlyList<ActiveSlot> outfield)
    {
        long sum = 0;
        var count = 0;

        foreach (var slot in outfield)
        {
            if (slot.Slot.Family is not (MatchPositionFamily.Defence or MatchPositionFamily.Midfield))
            {
                continue;
            }

            sum += (EffectiveSkill.Hundredths(slot, MatchAttributeName.Pace, rules)
                + EffectiveSkill.Hundredths(slot, MatchAttributeName.Acceleration, rules)) / 2;
            count++;
        }

        if (count == 0)
        {
            return (0, 0);
        }

        // Hundredths of an attribute point above or below the reference.
        var surplus = (sum / count) - ((long)rules.CounterRecoveryReference * EffectiveSkill.Scale);

        return (
            (int)Math.Clamp(-surplus * rules.CounterRecoveryProgressStepBasisPoints / EffectiveSkill.Scale, -rules.CounterRecoveryMaxProgressBasisPoints, rules.CounterRecoveryMaxProgressBasisPoints),
            (int)Math.Clamp(-surplus * rules.CounterRecoveryCreationStepBasisPoints / EffectiveSkill.Scale, -rules.CounterRecoveryMaxCreationBasisPoints, rules.CounterRecoveryMaxCreationBasisPoints));
    }

    /// <summary>Plays the ball into the final third: to the receiver the holder chose, or carried there when he chose nobody.</summary>
    private static void RecordEntry(MatchState state, PlannedPassage plan, ReceiverChain? chain)
    {
        if (chain is null)
        {
            state.MoveBallAndRecord(plan.EntryPoint, PassageWaypointKind.Pass);

            return;
        }

        var leg = chain.Legs[plan.Approach.Count - 1];

        if (leg.Receiver is not Guid receiver)
        {
            state.MoveBallAndRecord(plan.EntryPoint, PassageWaypointKind.Carry);
            state.RecordTouch(leg.Passer, PassageAction.Carry);

            return;
        }

        state.RecordTouch(leg.Passer, PassageAction.Pass);
        state.MoveBallAndRecord(plan.EntryPoint, PassageWaypointKind.Pass);
        state.RecordTouch(receiver, PassageAction.Receive);
        state.Passing.Entered(leg.Passer, receiver);
    }

    /// <summary>
    /// Plays the end of a move that was crossed: the ball is delivered into the box and contested in the air
    /// (`engine-v10`).
    /// </summary>
    /// <remarks>
    /// The player on the ball puts it into the box, the best-placed of the others goes up for it against the
    /// defender marking, and when the attacker wins it the header is the shot. When the defence wins it the attack
    /// created nothing: the cross is cleared.
    /// </remarks>
    private static PassageOutcome ResolveCrossedChance(MatchState state, Possession possession, Guid crosser)
    {
        var rules = state.Rules;
        var plan = possession.Plan;

        state.RecordTouch(crosser, PassageAction.Cross);
        state.MoveBallAndRecord(plan.HeaderPoint, PassageWaypointKind.Cross, rules.HeaderAltitude);

        if (ChanceSimulator.ResolveCross(
                state,
                possession.Side,
                plan.Zone,
                PassagePlanner.StrikeFrom(plan, plan.HeaderPoint, rules),
                crosser))
        {
            return PassageOutcome.OpenPlayShot;
        }

        // The defence won the ball in the air: the cross was the pass that was stopped, and it is cleared.
        state.Passing.LostCreation();
        ClearBall(state, plan);

        return PassageOutcome.CreationFailed;
    }

    /// <summary>
    /// Ends a possession: the injury roll, the ball placed where play continues, and the passage handed on.
    /// </summary>
    /// <remarks>
    /// A dead ball is taken from its spot, so when a restart is pending the ball is placed there; a loose ball
    /// — a block, a rebound, a clearance — is already where play continues. Moving the ball here, and not where
    /// the outcome was recorded, keeps the strike's last waypoint where the strike ended.
    /// </remarks>
    private static void Finish(MatchState state, PassageOutcome outcome)
    {
        // The possession's passes are credited before an injury can take a passer off the pitch (`engine-v7`).
        PassTally.Settle(state);

        InjurySimulator.TryResolveInjury(state);

        if (state.NextRestart is { } restart)
        {
            state.MoveBall(restart.Spot);
        }

        state.EndPassage(outcome);
    }

    /// <summary>
    /// Writes down the possession's approach: the ball's waypoints from the start, and who had it at each of them
    /// (`engine-v4`, `engine-v10`).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Since `engine-v10` a possession that is played through (<paramref name="passer"/>) has a person at each end
    /// of every leg. The carrier has the ball at the start; at each touch the holder either plays it to a receiver
    /// the chooser picked, which is a pass, or keeps it, which is a carry. The touches are written in the order they
    /// happen: the holder plays, the ball arrives, the receiver takes it. Every touch but the last is moved
    /// partway towards its receiver, and the last is where the plan put it.
    /// </para>
    /// <para>
    /// When the ball was lost on the last leg (<paramref name="lost"/>) nobody takes it there: the receiver the pass
    /// was meant for is only the one who ran onto it.
    /// </para>
    /// </remarks>
    /// <param name="state">The match state.</param>
    /// <param name="possession">The possession.</param>
    /// <param name="points">The path the ball took: the whole approach, or the part of it that was played.</param>
    /// <param name="endsInCross">Whether the path ends in a cross, which only the whole approach can.</param>
    /// <param name="passer">Whether the path was played through by passes, and so has a chain of receivers.</param>
    /// <param name="lost">Whether the ball was lost at the end of the path.</param>
    /// <param name="scramble">The scramble the side in possession won at the start, if there was one.</param>
    /// <param name="carrier">The player who has the ball at the start.</param>
    /// <param name="chain">The chain already played for this whole path, or null to play it here.</param>
    private static void RecordApproach(
        MatchState state,
        Possession possession,
        IReadOnlyList<SpatialPoint> points,
        bool endsInCross,
        bool passer,
        bool lost,
        Scramble? scramble,
        Guid? carrier,
        ReceiverChain? chain = null)
    {
        var side = possession.Side;

        // The first waypoint is the possession's start: a dead ball set down, or the loose ball picked up
        // where it lay — and the carrier who receives there.
        state.MoveBallAndRecord(
            points[0],
            possession.Restart == PassageRestartKind.None ? PassageWaypointKind.Carry : PassageWaypointKind.Restart);

        if (scramble is { } won)
        {
            // The contested loose ball is where the possession begins, and the side that won it carries on
            // from there: both contestants are at the ball.
            state.RecordTouch(won.AttackerId, PassageAction.Carry);
            state.RecordTouch(won.DefenderId, PassageAction.Run);
        }
        else if (carrier is Guid carrierId)
        {
            state.RecordTouch(carrierId, PassageAction.Carry);
        }

        chain ??= passer && carrier is Guid first
            ? ReceiverChooser.Choose(state, side, points, first, lost)
            : null;

        if (chain is null)
        {
            RecordUnnamed(state, points, endsInCross, passer);

            return;
        }

        for (var index = 1; index < points.Count; index++)
        {
            var leg = chain.Legs[index - 1];
            var last = index == points.Count - 1;
            var cross = last && endsInCross && leg.IsPass;

            if (leg.IsPass)
            {
                state.RecordTouch(leg.Passer, cross ? PassageAction.Cross : PassageAction.Pass);
            }

            state.MoveBallAndRecord(
                leg.To,
                !leg.IsPass ? PassageWaypointKind.Carry : cross ? PassageWaypointKind.Cross : PassageWaypointKind.Pass,
                cross ? state.Rules.CrossAltitude : 0);

            if (leg.Receiver is not Guid receiver)
            {
                state.RecordTouch(leg.Passer, PassageAction.Carry);
            }
            else
            {
                state.RecordTouch(receiver, lost && last ? PassageAction.Run : PassageAction.Receive);
            }
        }

        // Every leg that was a pass was one, unless the ball was lost before anybody played it on.
        state.Passing.Chained(chain, points.Count - 1);
        state.Passing.Completed(state.Passing.ChainPassers.Count);
    }

    /// <summary>
    /// Gets the player who has the ball when the possession begins: the one who won the scramble, or else one drawn
    /// from the geometry stream, weighted by Dribbling and by how near the formation puts him to the ball.
    /// </summary>
    /// <remarks>
    /// The draw is taken whether or not a scramble was won, so the geometry stream is the same either way. A player
    /// the formation keeps far from where the possession starts is a fraction as likely to be the one who picks the
    /// ball up there, so a centre half is not the man who has it in the other team's half (`engine-v10`).
    /// </remarks>
    private static Guid? OpeningCarrier(MatchState state, Possession possession, Scramble? scramble)
    {
        var rules = state.Rules;
        var attacking = state.SideOf(possession.Side);
        var start = possession.Plan.Approach[0];

        var placed = OffBallModel.Place(
            attacking.Active,
            possession.Side == MatchSide.Home,
            hasPossession: true,
            start,
            attacking.Instructions,
            rules);

        var drawn = OffBallModel.Pick(
            [.. placed.Where(candidate => candidate.Player.Slot.Family != MatchPositionFamily.Goalkeeper)],
            candidate => OffBallModel.ReachWeight(
                candidate,
                EffectiveSkill.Hundredths(candidate.Player, MatchAttributeName.Dribbling, rules),
                start,
                possession.Side == MatchSide.Home,
                rules),
            possession.Derived)?.Player.Participant.ParticipantId;

        return scramble?.AttackerId ?? drawn;
    }

    /// <summary>
    /// Writes down an approach nobody is named in but its carrier, and a passer drawn without reference to where
    /// anyone stands: a possession that was not played through, or one with nobody on the pitch to play it.
    /// </summary>
    private static void RecordUnnamed(MatchState state, IReadOnlyList<SpatialPoint> points, bool endsInCross, bool passer)
    {
        for (var index = 1; index < points.Count; index++)
        {
            var last = index == points.Count - 1;
            var kind = last && endsInCross ? PassageWaypointKind.Cross : PassageWaypointKind.Pass;
            var altitude = kind == PassageWaypointKind.Cross ? state.Rules.CrossAltitude : 0;

            state.MoveBallAndRecord(points[index], kind, altitude);
        }

        if (passer)
        {
            state.Passing.Completed(points.Count - 1);
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

    /// <summary>Gets how far up the pitch a point is from the attacking side's own goal.</summary>
    private static int AttackingX(SpatialPoint point, MatchSide possessionSide) =>
        possessionSide == MatchSide.Home ? point.X : SpatialPitch.PitchLength - point.X;

    /// <summary>
    /// Clears a ball the defence has won back towards the middle third, when it is far enough from there for a
    /// kick to be worth showing (`engine-v5`).
    /// </summary>
    private static void ClearBall(MatchState state, PlannedPassage plan)
    {
        var from = state.Ball.GroundPoint;
        var target = PassagePlanner.ClearanceTarget(plan, from, state.Rules);

        if (target != from)
        {
            state.MoveBallAndRecord(target, PassageWaypointKind.Clearance, state.Rules.ClearanceAltitude);
        }
    }

    /// <summary>
    /// Resolves the loose-ball scramble that opens a passage: whether the possession side keeps the ball.
    /// </summary>
    /// <remarks>
    /// The nearest players from each side contest it, chosen by the legs a scramble asks for (engine-v3) and, since
    /// `engine-v10`, by how near the formation puts them to the ball. When either side has nobody to contest it there
    /// is no scramble, which is the same as the side in possession keeping the ball.
    /// </remarks>
    /// <returns>The scramble and who contested it, or null when there was nobody to contest it.</returns>
    private static Scramble? ResolveScramble(MatchState state, MatchSide possessionSide, SpatialPoint ball)
    {
        var rules = state.Rules;
        var home = possessionSide == MatchSide.Home;
        var attacking = state.SideOf(possessionSide);
        var defending = state.OpponentOf(possessionSide);

        var attackers = OffBallModel.Place(attacking.Active, home, hasPossession: true, ball, attacking.Instructions, rules);
        var defenders = OffBallModel.Place(defending.Active, !home, hasPossession: false, ball, defending.Instructions, rules);

        var attackerPick = OffBallModel.Pick(
            [.. attackers.Where(placed => placed.Player.Slot.Family != MatchPositionFamily.Goalkeeper)],
            placed => OffBallModel.ReachWeight(placed, ScrambleWeight(placed.Player, rules), ball, home, rules),
            state.Random);

        var defenderPick = OffBallModel.Pick(
            [.. defenders.Where(placed => placed.Player.Slot.Family != MatchPositionFamily.Goalkeeper)],
            placed => OffBallModel.ReachWeight(placed, ScrambleWeight(placed.Player, rules), ball, attackingFor: null, rules),
            state.Random);

        if (attackerPick is not { } attackerPlaced || defenderPick is not { } defenderPlaced)
        {
            return null;
        }

        var attacker = attackerPlaced.Player;
        var defender = defenderPlaced.Player;

        var kept = DuelResolver.ResolveScramble(
            DuelContender.Of(state.SideOf(possessionSide), attacker),
            DuelContender.Of(state.OpponentOf(possessionSide), defender),
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

        return new Scramble(!kept, attacker.Participant.ParticipantId, defender.Participant.ParticipantId);
    }

    private static int ScrambleWeight(ActiveSlot slot, EngineRulesV2 rules) =>
        EffectiveSkill.Hundredths(slot, MatchAttributeName.Pace, rules)
        + EffectiveSkill.Hundredths(slot, MatchAttributeName.Acceleration, rules)
        + EffectiveSkill.Hundredths(slot, MatchAttributeName.WorkRate, rules);

    /// <summary>
    /// Resolves a direct free kick awarded in the attacking half: the placement, the award event, the strike,
    /// and what it produced (engine-v3, completed in engine-v5).
    /// </summary>
    /// <returns>How the possession ended: the free kick struck, or crossed rather than struck.</returns>
    private static PassageOutcome ResolveFreeKick(MatchState state, Possession possession)
    {
        var rules = state.Rules;
        var side = possession.Side;
        var attacker = state.SideOf(side);
        var defender = state.OpponentOf(side);

        var taker = WeightedPick.From(
            attacker.Outfield,
            slot => EffectiveSkill.Hundredths(slot, MatchAttributeName.SetPieces, rules)
                + EffectiveSkill.Hundredths(slot, MatchAttributeName.Finishing, rules),
            state.Random);

        var spot = state.Ball.GroundPoint;

        if (taker is null)
        {
            // Nobody to take it, so it is a quick free kick like any other foul.
            state.RestartWithFreeKick(side, spot);

            return PassageOutcome.Foul;
        }

        // The ball is set down where the foul was committed, and the taker stands over it.
        state.MoveBallAndRecord(spot, PassageWaypointKind.Restart);
        state.RecordTouch(taker.Participant.ParticipantId, PassageAction.FreeKick);
        state.Emit(side, EngineEventType.FreeKickWon, taker.Participant.ParticipantId);

        var outcome = SetPieceDirector.ResolveDirectFreeKick(
            taker,
            defender.Goalkeeper,
            AttackingX(spot, side),
            rules,
            state.Random);

        if (!outcome.Attempted)
        {
            // Crossed rather than struck: the passage ends with the ball delivered into the box.
            state.MoveBallAndRecord(possession.Plan.HeaderPoint, PassageWaypointKind.Cross, rules.HeaderAltitude);

            return PassageOutcome.FreeKickCrossed;
        }

        state.Emit(
            side,
            EngineEventType.FreeKickShot,
            outcome.TakerId,
            outcome.GoalkeeperId,
            outcome.Zone,
            rules.FreeKickGoalBasisPoints);

        var strike = PassagePlanner.StrikeFrom(possession.Plan, spot, rules);

        if (outcome.IsGoal)
        {
            state.MoveBallAndRecord(strike.GoalTarget, PassageWaypointKind.Shot, strike.GoalAltitude);
            ScoreFromSetPiece(state, side, outcome.TakerId, EngineEventType.Goal, spot);

            return PassageOutcome.FreeKickStruck;
        }

        // The strike missed: attribute it, saved, blocked, woodwork, or off target, like an open-play shot.
        if (outcome.WasSaved)
        {
            state.MoveBallAndRecord(strike.SaveTarget, PassageWaypointKind.Shot, strike.SaveAltitude);

            if (outcome.GoalkeeperId is Guid keeper)
            {
                state.RecordTouch(keeper, PassageAction.Save);
            }

            state.Emit(side, EngineEventType.ShotSaved, outcome.TakerId, outcome.GoalkeeperId, outcome.Zone, rules.FreeKickGoalBasisPoints, at: spot);

            if (outcome.GoalkeeperId is Guid ratedKeeper)
            {
                defender.AdjustLiveRating(ratedKeeper, rules.LiveRatingSaveBonusBasisPoints);
            }

            state.RestartFromGoalArea(possession.Defending, PassageRestartKind.KeeperBall);
        }
        else if (outcome.HitWoodwork)
        {
            state.MoveBallAndRecord(strike.WoodworkTarget, PassageWaypointKind.Shot, strike.WoodworkAltitude);
            state.Emit(side, EngineEventType.Woodwork, outcome.TakerId, outcome.GoalkeeperId, outcome.Zone, rules.FreeKickGoalBasisPoints, at: spot);

            // The frame sends it back into play, where it is loose.
            state.MoveBallAndRecord(strike.ReboundPoint, PassageWaypointKind.Clearance, strike.GoalAltitude);
        }
        else
        {
            state.MoveBallAndRecord(strike.MissTarget, PassageWaypointKind.Shot, strike.MissAltitude);
            state.Emit(side, EngineEventType.ShotOffTarget, outcome.TakerId, outcome.GoalkeeperId, outcome.Zone, rules.FreeKickGoalBasisPoints, at: spot);

            state.RestartFromGoalArea(possession.Defending, PassageRestartKind.GoalKick);
        }

        attacker.AdjustLiveRating(outcome.TakerId, -rules.LiveRatingShotMissPenaltyBasisPoints);

        return PassageOutcome.FreeKickStruck;
    }

    /// <summary>
    /// Scores a goal from a set piece and records everything the open-play scorer records, minus the assist
    /// a set piece does not model.
    /// </summary>
    private static void ScoreFromSetPiece(MatchState state, MatchSide side, Guid scorerId, EngineEventType type, SpatialPoint at)
    {
        var runtime = state.SideOf(side);
        var goalkeeper = state.OpponentOf(side).Goalkeeper?.Participant.ParticipantId;

        runtime.Goals.TryGetValue(scorerId, out var goals);
        runtime.Goals[scorerId] = goals + 1;

        // A goal is a restart: the side that conceded kicks off from the centre spot (engine-v5).
        state.RestartAfterGoal(side);

        state.AddGoalStoppage();

        runtime.ShiftMorale(state.Rules.MoraleGainPerGoalBasisPoints, state.Rules.MaxMoraleDriftBasisPoints, state.Rules);
        state
            .OpponentOf(side)
            .ShiftMorale(-state.Rules.MoraleLossPerConcededGoalBasisPoints, state.Rules.MaxMoraleDriftBasisPoints, state.Rules);

        runtime.AdjustLiveRating(scorerId, state.Rules.LiveRatingGoalBonusBasisPoints);

        if (goalkeeper is Guid keeper)
        {
            state.OpponentOf(side).AdjustLiveRating(keeper, -state.Rules.LiveRatingGoalConcededPenaltyBasisPoints);
        }

        state.Emit(side, type, scorerId, goalkeeper, ShotZone.Central, state.Rules.FreeKickGoalBasisPoints, at: at);
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

    /// <summary>What the final-third ground duel decided: the creation bonus, and a foul if the defender gave one.</summary>
    /// <param name="CreationBonus">The signed creation bonus the duel earned the attacking side, in basis points.</param>
    /// <param name="FoulerId">The defender who fouled the carrier, or null when there was no foul.</param>
    /// <param name="FouledId">The carrier who was fouled, or null when there was no foul.</param>
    private readonly record struct GroundDuelResult(int CreationBonus, Guid? FoulerId, Guid? FouledId);

    /// <summary>
    /// Resolves the 1v1 ground duel the carrier fights once the possession has progressed (engine-v3).
    /// </summary>
    /// <remarks>
    /// Winning the duel buys the creation that follows a beat man — the rules' dribble bonus — and losing
    /// it hands the initiative back, which is the same bonus taken away. A lost duel does not end the
    /// possession unless the defender fouled (engine-v6): the scramble already decides hard turnovers, and a
    /// tackled attack regrouping is the ordinary rhythm of a match. The two players are drawn by band: the
    /// carrier mostly from midfield and attack, the defender mostly from defence and midfield, so a striker is
    /// rarely the tackler (engine-v6).
    /// </remarks>
    private static GroundDuelResult ResolveGroundDuel(MatchState state, MatchSide possessionSide, Guid? holderId)
    {
        var rules = state.Rules;
        var attackers = state.SideOf(possessionSide);
        var defenders = state.OpponentOf(possessionSide);

        var drawn = WeightedPick.From(
            attackers.Outfield,
            slot => EffectiveSkill.Hundredths(slot, MatchAttributeName.Dribbling, rules)
                * BandWeight(slot, rules.DuelCarrierDefenceWeight, rules.DuelCarrierMidfieldWeight, rules.DuelCarrierAttackWeight),
            state.Random);

        // The player who has the ball fights the duel, when the approach was played through to him; the draw is
        // taken either way, so the stream after it is the same (`engine-v10`).
        var carrier = attackers.Outfield.FirstOrDefault(slot => slot.Participant.ParticipantId == holderId) ?? drawn;

        var tackler = WeightedPick.From(
            defenders.Outfield,
            slot => EffectiveSkill.Hundredths(slot, MatchAttributeName.Tackling, rules)
                * BandWeight(slot, rules.DuelTacklerDefenceWeight, rules.DuelTacklerMidfieldWeight, rules.DuelTacklerAttackWeight),
            state.Random);

        if (carrier is null || tackler is null)
        {
            return new GroundDuelResult(0, null, null);
        }

        var duel = DuelResolver.ResolveGroundDuel(
            DuelContender.Of(attackers, carrier),
            DuelContender.Of(defenders, tackler),
            possessionSide == MatchSide.Home,
            defenders.Instructions.Tackling,
            rules,
            state.Random);

        // The carrier and the defender are the participants the duel actually picked, so the replay can name
        // them (engine-v4). A foul records them itself, where it was committed.
        if (!duel.WasFoul)
        {
            state.RecordTouch(duel.AttackerId, PassageAction.Carry);
            state.RecordTouch(duel.DefenderId, PassageAction.Tackle);
        }

        // The take-on is a dribble attempted by the carrier, and a dribble completed when he beat the man. A
        // duel the defender ended with a foul is neither: the carrier was brought down, not dispossessed
        // (`engine-v7`).
        if (!duel.WasFoul)
        {
            PassTally.RecordDribble(attackers, duel.AttackerId, duel.AttackerWon);
        }

        if (duel.AttackerWon)
        {
            attackers.AdjustLiveRating(duel.AttackerId, rules.LiveRatingTackleBonusBasisPoints);
            defenders.AdjustLiveRating(duel.DefenderId, -rules.LiveRatingTackleLostPenaltyBasisPoints);

            return new GroundDuelResult(rules.DribbleCreationBonusBasisPoints, null, null);
        }

        attackers.AdjustLiveRating(duel.AttackerId, -rules.LiveRatingTackleLostPenaltyBasisPoints);
        defenders.AdjustLiveRating(duel.DefenderId, rules.LiveRatingTackleBonusBasisPoints);

        return new GroundDuelResult(
            -rules.DribbleCreationBonusBasisPoints,
            duel.WasFoul ? duel.DefenderId : null,
            duel.WasFoul ? duel.AttackerId : null);
    }

    /// <summary>Gets the weight of a player's band in a duel's draw: how likely his job is to put him in it.</summary>
    private static int BandWeight(ActiveSlot slot, int defence, int midfield, int attack) => slot.Slot.Family switch
    {
        MatchPositionFamily.Defence => defence,
        MatchPositionFamily.Midfield => midfield,
        MatchPositionFamily.Attack => attack,
        _ => 1,
    };

    /// <summary>
    /// Resolves a possession that could not be progressed: either an offside or a plain turnover.
    /// </summary>
    /// <remarks>
    /// Fouls are not here, because the defending side's foul was already rolled before the progression
    /// attempt. The two failure modes left are the attacking side's own: caught offside, or simply losing the
    /// ball. Either way the approach was cut short, so the ball is lost where it had got to (`engine-v5`).
    /// </remarks>
    private static PassageOutcome ResolveFailedProgression(MatchState state, Possession possession, SideRuntime attacker)
    {
        var plan = possession.Plan;

        if (!state.Random.RollBasisPoints(state.Rules.OffsideShareOfTurnoverBasisPoints))
        {
            // A plain turnover: the defence wins the ball and clears it out towards the middle third.
            ClearBall(state, plan);

            return PassageOutcome.ProgressionFailed;
        }

        // The last pass was played through to a player who was offside.
        var offsidePoint = PassagePlanner.OffsideTarget(plan, state.Ball.GroundPoint, state.Rules);

        state.MoveBallAndRecord(offsidePoint, PassageWaypointKind.Pass);

        var caught = WeightedPick.From(
            attacker.Outfield,
            slot => slot.Participant.Attributes.ValueOf(MatchAttributeName.Pace),
            state.Random);

        if (caught is not null)
        {
            state.RecordTouch(caught.Participant.ParticipantId, PassageAction.Run);
            state.Emit(possession.Side, EngineEventType.Offside, caught.Participant.ParticipantId);

            // The free kick is the defending side's, from where the offside was given.
            state.RestartWithFreeKick(possession.Defending, offsidePoint);
        }

        return PassageOutcome.Offside;
    }

    /// <summary>Resolves a possession that created nothing: a corner, or a plain turnover.</summary>
    private static PassageOutcome ResolveFailedCreation(MatchState state, Possession possession)
    {
        var rules = state.Rules;
        var plan = possession.Plan;

        // The ball that would have broken the defence was stopped, whether it went behind for a corner or was
        // cleared (`engine-v7`).
        state.Passing.LostCreation();

        if (!state.Random.RollBasisPoints(rules.CornerShareOfFailedCreationBasisPoints))
        {
            // The attack broke down without a corner: the ball is cleared into the middle third (engine-v4).
            ClearBall(state, plan);

            return PassageOutcome.CreationFailed;
        }

        // A corner: the ball goes out over the goal line, is set down at the flag, and is delivered into the
        // box whatever comes of it (engine-v5).
        state.MoveBallAndRecord(plan.CornerOutPoint, PassageWaypointKind.Pass);
        state.MoveBallAndRecord(plan.CornerPoint, PassageWaypointKind.Restart);

        // Somebody takes it (engine-v6): the better his set pieces and crossing, the likelier the delivery is
        // headed at goal and the likelier the header is won.
        var taker = ChanceSimulator.ChooseCornerTaker(state, possession.Side);
        var deliveryEdge = 0;

        if (taker is not null)
        {
            state.RecordTouch(taker.Participant.ParticipantId, PassageAction.Cross);
            deliveryEdge = ChanceSimulator.CornerDeliveryEdge(taker, rules);
        }

        state.Emit(possession.Side, EngineEventType.Corner);
        state.MoveBallAndRecord(plan.HeaderPoint, PassageWaypointKind.Cross, rules.HeaderAltitude);

        var cornerChance = Probability.Band(
            rules.CornerChanceBasisPoints
                + (deliveryEdge * rules.CornerDeliveryChanceStepBasisPoints / EffectiveSkill.Scale),
            rules.CornerChanceMinBasisPoints,
            rules.CornerChanceMaxBasisPoints);

        if (state.Random.RollBasisPoints(cornerChance)
            && ChanceSimulator.ResolveCorner(state, possession.Side, PassagePlanner.StrikeFrom(plan, plan.HeaderPoint, rules), deliveryEdge))
        {
            // The header was won and struck at goal, so what happens next is the shot's outcome: a goal, a
            // save, a miss, a block, or the woodwork. There is no clearance after any of them.
            return PassageOutcome.CornerHeaded;
        }

        // Nobody got a header on it, or the defence won it: the corner is cleared.
        ClearBall(state, plan);

        return PassageOutcome.CornerCleared;
    }

    /// <summary>
    /// Chooses which side gets the next possession, from the two sides' control of the ball.
    /// </summary>
    /// <remarks>
    /// A side's control is what it can do with the ball against what the opponent can do about it, and the
    /// share is bounded so that neither side is ever shut out of the match however lopsided the ratings are.
    /// A twenty-minute spell without the ball is a thing that happens; never touching it is not. It is only
    /// asked of a possession that begins from play: a dead ball already has its side (`engine-v5`).
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

        // A side that is wasting time keeps the ball longer, so there is less football in the match (engine-v6).
        if (attacker.WastingTime)
        {
            multiplier = Probability.Apply(multiplier, rules.TimeWastingPossessionSecondsMultiplierBasisPoints);
        }

        return Math.Max(rules.MinEffectivePossessionSeconds, Probability.Apply(seconds, multiplier));
    }
}
