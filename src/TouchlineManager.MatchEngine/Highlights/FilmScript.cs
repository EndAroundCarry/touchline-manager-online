using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Highlights;

/// <summary>A player leaving the pitch, or coming on, between two possessions (`replay-v4`).</summary>
/// <param name="BeforePossession">The index of the first possession played without them (or with the newcomer).</param>
/// <param name="Side">The side making the change.</param>
/// <param name="Off">The player leaving.</param>
/// <param name="On">The player coming on, or null when the side plays a player short.</param>
/// <param name="Event">The substitution event, when there is one to show as a hold; null for a dismissal or an injury.</param>
internal sealed record PersonnelChange(int BeforePossession, MatchSide Side, Guid Off, Guid? On, EngineEventV1? Event);

/// <summary>What the script is: every beat of the film, and where each possession sits among them (`replay-v4`).</summary>
/// <param name="Beats">The beats, in film order.</param>
/// <param name="Possessions">The possessions, each with the run of beats it owns.</param>
internal sealed record FilmScriptResult(List<FilmBeat> Beats, List<FilmPossession> Possessions);

/// <summary>
/// Turns each recorded possession into the beats the film plays (`replay-v4`).
/// </summary>
/// <remarks>
/// <para>
/// A possession is recorded as a ball path through a chain of stations — the waypoints — with the players the
/// engine named at some of them and the events positioned among them. The script reads that record and decides
/// what a viewer would see: who passes to whom, where a player drives the ball, whether a high ball is a cross
/// or a lofted pass, where a challenge is made, and where the play stops for a restart.
/// </para>
/// <para>
/// The engine's facts are never overridden. Where it named a player — the carrier, the passer, the duel pair, the
/// shooter, the keeper, the header pair, the taker, the fouler and the fouled, the player caught offside — that
/// player is at that beat. Where it did not, the receiver is the team-mate who can reach the reception point
/// soonest from the shape he is standing in, which is also where the motion will send him. Nothing here draws from
/// the play stream, and nothing here can move a result.
/// </para>
/// </remarks>
internal static class FilmScript
{
    /// <summary>The shortest a move may be to be worth showing, in metres.</summary>
    private const double MinMove = 0.35;

    /// <summary>How long a ground move has to be before the player drives the ball first, in metres.</summary>
    private const double CarryThreshold = 12.0;

    /// <summary>How long a ground pass can be before it is played in the air, in metres.</summary>
    private const double LoftedThreshold = 30.0;

    /// <summary>How far a challenge the carrier comes through pushes the ball on, in metres.</summary>
    private const double DuelPush = 2.5;

    /// <summary>The altitude at which a ball is headed.</summary>
    private const double HeaderContactZ = 30.0;

    /// <summary>Builds the script for a whole match.</summary>
    /// <param name="context">The film's context.</param>
    /// <param name="shape">The team shape, which receivers are chosen from.</param>
    /// <param name="possessions">The recorded possessions, in order.</param>
    /// <param name="rosters">Who is on the pitch for each possession.</param>
    /// <param name="changes">The substitutions and dismissals.</param>
    public static FilmScriptResult Build(
        FilmContext context,
        FilmShape shape,
        IReadOnlyList<MatchPassageV1> possessions,
        IReadOnlyList<FilmRoster> rosters,
        IReadOnlyList<PersonnelChange> changes)
    {
        var builder = new Builder(context, shape);
        var beats = new List<FilmBeat>(possessions.Count * 9);
        var placed = new List<FilmPossession>(possessions.Count);

        var ball = FilmSpace.Centre;
        int? previousPeriod = null;
        var previousScored = false;

        for (var index = 0; index < possessions.Count; index++)
        {
            var source = possessions[index];
            var roster = rosters[index];
            var first = beats.Count;
            string? cutKind = null;

            if (previousPeriod is int period && period != source.Period)
            {
                // The half-time card is its own hold, and the second half's kick-off is a cut.
                beats.Add(builder.HalfTime(ball, period));
                cutKind = "half_time";
            }
            else if (previousScored)
            {
                cutKind = "kick_off";
            }

            var afterCard = beats.Count;

            // After a cut the film is at the kick-off spot, so a change shown first is shown there.
            var heldAt = cutKind is not null && source.Waypoints.Count > 0
                ? FilmSpace.FromEngine(source.Waypoints[0].X, source.Waypoints[0].Y)
                : ball;

            foreach (var change in changes.Where(change => change.BeforePossession == index && change.Event is not null))
            {
                var hold = builder.Hold(HoldKind.Substitution, change.Side, heldAt, index, source.Period, null);

                hold.Events.Add(new BeatEvent(change.Event!.Sequence, AtStart: true));
                beats.Add(hold);
            }

            beats.AddRange(builder.Possession(index, source, roster, ball));

            if (beats.Count == afterCard)
            {
                // A possession that recorded nothing still has a place in the film.
                beats.Add(builder.Hold(HoldKind.FreeKick, source.Side, ball, index, source.Period, null));
            }

            if (cutKind is not null && source.Restart == PassageRestartKind.KickOff)
            {
                beats[afterCard].Cut = true;
                beats[afterCard].CutKind = cutKind;
            }

            previousPeriod = source.Period;
            previousScored = beats.Skip(first).Any(beat => beat.Hold == HoldKind.Goal);
            ball = beats[^1].To;
            placed.Add(new FilmPossession(index, source, first, beats.Count - 1));
        }

        return new FilmScriptResult(beats, placed);
    }

    /// <summary>Builds one possession at a time, with the scratch space it needs to choose receivers.</summary>
    private sealed class Builder
    {
        private readonly FilmContext _context;
        private readonly FilmShape _shape;
        private readonly Vec[] _scratch = new Vec[FilmRoster.Size];

        public Builder(FilmContext context, FilmShape shape)
        {
            _context = context;
            _shape = shape;
        }

        public FilmBeat HalfTime(Vec ball, int period) => Hold(HoldKind.HalfTime, MatchSide.Home, ball, -1, period, null);

        public FilmBeat Hold(
            HoldKind kind,
            MatchSide side,
            Vec at,
            int possession,
            int period,
            Guid? actor,
            FormationMode formation = FormationMode.Open) => new()
            {
                Kind = BeatKind.Hold,
                Hold = kind,
                Possession = possession,
                Period = period,
                Side = side,
                From = at,
                To = at,
                Actor = actor,
                Formation = formation,
                HoldFilmSeconds = HoldSeconds(kind, formation),
            };

        private double HoldSeconds(HoldKind kind, FormationMode formation)
        {
            var options = _context.Options;

            return kind switch
            {
                HoldKind.KickOff => options.KickOffHoldSeconds,
                HoldKind.GoalKick or HoldKind.KeeperBall => options.GoalKickHoldSeconds,
                HoldKind.FreeKick => formation == FormationMode.Open ? options.QuickFreeKickHoldSeconds : options.SetPieceHoldSeconds,
                HoldKind.Corner or HoldKind.Penalty => options.SetPieceHoldSeconds,
                HoldKind.Goal => options.GoalHoldSeconds,
                HoldKind.Card => options.CardHoldSeconds,
                HoldKind.Substitution => options.SubstitutionHoldSeconds,
                HoldKind.HalfTime => options.HalfTimeHoldSeconds,
                _ => options.QuickFreeKickHoldSeconds,
            };
        }

        /// <summary>Scripts one possession: the restart it began with, then its stations in order.</summary>
        public List<FilmBeat> Possession(int index, MatchPassageV1 source, FilmRoster roster, Vec ball)
        {
            var stations = Stations(source);
            var beats = new List<FilmBeat>(stations.Count * 3);

            if (stations.Count == 0)
            {
                return beats;
            }

            var state = new State(index, source, roster, stations, beats);

            StartOfPossession(state, ball);
            StationActions(state, 0);
            AttachEvents(state, 0);

            for (var j = 1; j < stations.Count; j++)
            {
                Arrive(state, j);
                StationActions(state, j);
                AttachEvents(state, j);
            }

            return beats;
        }

        /// <summary>Gets the stations of a possession: its waypoints, with the touches and events that fall at each.</summary>
        private List<Station> Stations(MatchPassageV1 source)
        {
            var stations = new List<Station>(source.Waypoints.Count);

            foreach (var waypoint in source.Waypoints)
            {
                stations.Add(new Station(waypoint, FilmSpace.FromEngine(waypoint.X, waypoint.Y)));
            }

            if (stations.Count == 0)
            {
                return stations;
            }

            foreach (var touch in source.Touches)
            {
                stations[StationAt(stations, touch.FractionBasisPoints)].Touches.Add(touch);
            }

            foreach (var recorded in source.Events)
            {
                if (_context.EventsBySequence.TryGetValue(recorded.Sequence, out var found))
                {
                    stations[StationAt(stations, recorded.FractionBasisPoints)].Events.Add(found);
                }
            }

            foreach (var station in stations)
            {
                station.Resolve();
            }

            return stations;
        }

        private static int StationAt(List<Station> stations, int fraction)
        {
            var at = 0;

            for (var index = 0; index < stations.Count; index++)
            {
                if (stations[index].Waypoint.FractionBasisPoints <= fraction)
                {
                    at = index;
                }
            }

            return at;
        }

        // ---- The start of a possession ---------------------------------------------------------------------

        private void StartOfPossession(State state, Vec ball)
        {
            var source = state.Source;
            var side = source.Side;
            var first = state.Stations[0];
            var start = first.Point;

            // The first station's holder: the carrier the engine named, or the nearest player to the ball. A
            // restart is taken from a formation the film itself arranges, so the nearest player there is known now.
            var named = first.Holder ?? Nearest(state.Roster, side, start, start, [], includeKeeper: false);

            SetHolder(state, named, pending: false);

            switch (source.Restart)
            {
                case PassageRestartKind.None:
                    if (state.Index > 0 && ball.DistanceTo(start) > MinMove)
                    {
                        // The ball is where the last possession left it, and the engine says it started somewhere
                        // else: it travels there at a pace it could.
                        state.Add(Move(state, BeatKind.Pass, ball, start, named, ActorSource.Named, named, false, side));
                    }

                    break;

                case PassageRestartKind.KickOff:
                    AddRestart(state, HoldKind.KickOff, FormationMode.KickOff, ball, start, named);
                    break;

                case PassageRestartKind.GoalKick:
                case PassageRestartKind.KeeperBall:
                    {
                        // The goalkeeper takes it, and plays it to the player the engine named.
                        var keeper = KeeperOf(state.Roster, side) ?? named;

                        state.Preferred = named;
                        SetHolder(state, keeper, pending: false);
                        AddRestart(
                            state,
                            source.Restart == PassageRestartKind.GoalKick ? HoldKind.GoalKick : HoldKind.KeeperBall,
                            FormationMode.Open,
                            ball,
                            start,
                            keeper);
                        break;
                    }

                default:
                    AddRestart(state, HoldKind.FreeKick, FormationMode.Open, ball, start, named);
                    break;
            }
        }

        /// <summary>A dead-ball restart: the ball is put where it is taken from, and play is held.</summary>
        private void AddRestart(State state, HoldKind kind, FormationMode formation, Vec from, Vec spot, Guid? taker)
        {
            var side = state.Source.Side;

            // A kick-off is reached by a cut, or is where the film began; everything else is put back.
            if (kind != HoldKind.KickOff && from.DistanceTo(spot) > MinMove && state.Index > 0)
            {
                state.Add(Move(state, BeatKind.Placement, from, spot, taker, ActorSource.Named, taker, false, side));
            }

            state.Add(Hold(kind, side, spot, state.Index, state.Source.Period, taker, formation));
        }

        // ---- Arriving at a station -------------------------------------------------------------------------

        private void Arrive(State state, int j)
        {
            var from = state.Stations[j - 1];
            var to = state.Stations[j];

            switch (to.Kind)
            {
                case PassageWaypointKind.Restart:
                    ScriptPlacement(state, j, from, to);
                    break;

                case PassageWaypointKind.Shot:
                    ScriptStrike(state, from, to);
                    break;

                case PassageWaypointKind.Clearance:
                    ScriptClearance(state, from, to);
                    break;

                case PassageWaypointKind.Cross:
                    ScriptDelivery(state, from, to);
                    break;

                default:
                    ScriptPass(state, j, from, to);
                    break;
            }
        }

        /// <summary>A ground or lofted pass between two stations, driven first when the ground to cover is long.</summary>
        private void ScriptPass(State state, int j, Station from, Station to)
        {
            var (actor, actorSource) = ActorOf(state);
            var side = SideNow(state);
            var start = BallStart(state, from);
            var distance = start.DistanceTo(to.Point);

            if (distance < MinMove)
            {
                if (to.Holder is { } named)
                {
                    SetHolder(state, named, pending: false);
                }

                return;
            }

            if (IsCornerOut(state, j))
            {
                // The ball goes out of play: nobody receives it.
                state.Add(Move(state, BeatKind.LoftedPass, start, to.Point, actor, actorSource, null, false, side));
                SetHolder(state, null, pending: false);

                return;
            }

            // The engine's named player receives it; otherwise whoever can reach the ball soonest, chosen as it is played.
            var receiver = to.Holder ?? state.TakePreferred();
            var kind = distance >= LoftedThreshold ? BeatKind.LoftedPass : BeatKind.Pass;

            if (receiver is not null && receiver == actor)
            {
                state.Add(Move(state, BeatKind.Carry, start, to.Point, actor, actorSource, actor, false, side, receiverIsActor: true));
                SetHolder(state, actor, pending: false);

                return;
            }

            var driven = kind == BeatKind.Pass
                && distance >= CarryThreshold
                && !(j == 1 && state.Source.Restart is PassageRestartKind.GoalKick or PassageRestartKind.KeeperBall)
                && state.Source.Outcome != PassageOutcome.Offside;

            if (driven)
            {
                var drive = DriveLength(state, j, actor, distance);
                var split = start.Lerp(to.Point, drive / distance);

                state.Add(Move(state, BeatKind.Carry, start, split, actor, actorSource, actor, false, side, receiverIsActor: true));
                start = split;

                // The player who drove the ball plays it on.
                actorSource = actor is null ? ActorSource.PreviousReceiver : ActorSource.Named;
            }

            state.Add(Move(state, kind, start, to.Point, actor, actorSource, receiver, receiver is null, side));
            SetHolder(state, receiver, pending: receiver is null);
        }

        /// <summary>A delivery that is a cross when it is wide and ends in the box, and a lofted ball otherwise.</summary>
        private void ScriptDelivery(State state, Station from, Station to)
        {
            var (actor, actorSource) = ActorOf(state);
            var side = SideNow(state);
            var start = BallStart(state, from);
            var setPiece = from.Kind == PassageWaypointKind.Restart
                && state.Source.Outcome is PassageOutcome.CornerCleared or PassageOutcome.CornerHeaded
                    or PassageOutcome.FreeKickCrossed or PassageOutcome.FreeKickStruck;
            var cross = setPiece ? IsInBox(side, to.Point) : IsWideCross(side, start, to.Point);

            // A header at the end of it is won by the pair the engine named; otherwise somebody gets on the end of it.
            var receiver = to.HasHeader ? null : to.Holder;
            var pending = !to.HasHeader && receiver is null;

            var beat = Move(state, cross ? BeatKind.Cross : BeatKind.LoftedPass, start, to.Point, actor, actorSource, receiver, pending, side);

            beat.ZTo = to.HasHeader ? HeaderContactZ : 0;
            beat.ZArc = cross ? 55 : Math.Min(50, 12 + (0.9 * start.DistanceTo(to.Point)));
            state.Add(beat);

            SetHolder(state, to.HasHeader ? to.Holder : receiver, pending: pending);
        }

        /// <summary>The strike at goal, and what it did: the net, the keeper, a miss, the frame, a block.</summary>
        private void ScriptStrike(State state, Station from, Station to)
        {
            var (actor, actorSource) = ActorOf(state);
            var side = SideNow(state);
            var defending = MatchInputV1.OpponentOf(side);
            var keeper = KeeperOf(state.Roster, defending);
            var result = StrikeOf(to);

            var beat = Move(state, BeatKind.Shot, BallStart(state, from), to.Point, actor, actorSource, result == StrikeResult.Saved ? keeper : null, false, side);

            beat.ZFrom = from.HasHeader ? HeaderContactZ : 0;
            beat.ZTo = to.Waypoint.Z;
            beat.Opponent = keeper;
            beat.Strike = result;
            beat.Headed = from.HasHeader;
            state.Add(beat);

            SetHolder(state, null, pending: false);

            if (result == StrikeResult.Saved && keeper is Guid saver)
            {
                state.Add(new FilmBeat
                {
                    Kind = BeatKind.Save,
                    Possession = state.Index,
                    Period = state.Source.Period,
                    Side = defending,
                    From = to.Point,
                    To = to.Point,
                    ZFrom = to.Waypoint.Z,
                    ZTo = 0,
                    Actor = saver,
                    Receiver = saver,
                });

                SetHolder(state, saver, pending: false);
            }
            else if (result == StrikeResult.Goal)
            {
                state.Add(Hold(HoldKind.Goal, side, to.Point, state.Index, state.Source.Period, actor, FormationMode.Celebration));
            }
        }

        /// <summary>A clearance, or the rebound off the frame: the defence's ball, played away.</summary>
        private static void ScriptClearance(State state, Station from, Station to)
        {
            var attacking = state.Source.Side;
            var defending = MatchInputV1.OpponentOf(attacking);
            var start = BallStart(state, from);
            var woodwork = StrikeOf(from) == StrikeResult.Woodwork;

            // A rebound off the frame is nobody's ball; a clearance belongs to the defender who won it, and when the
            // engine did not name one, to the nearest defender when the ball gets there.
            Guid? clearer = null;
            var source = ActorSource.Named;

            if (!woodwork)
            {
                clearer = from.HasHeader ? from.Holder : from.Interceptor ?? from.Opponent;

                if (clearer is null)
                {
                    source = ActorSource.NearestToBall;
                }
            }

            var beat = Move(state, BeatKind.Clearance, start, to.Point, clearer, source, null, false, woodwork ? attacking : defending);

            beat.ZFrom = woodwork ? from.Waypoint.Z : (from.HasHeader ? HeaderContactZ : 0);
            beat.ZTo = 0;
            beat.ZArc = woodwork ? 12 : 38;
            state.Add(beat);

            SetHolder(state, null, pending: false);
        }

        /// <summary>A station that is a dead-ball placement: a penalty spot, a corner flag, a free-kick spot.</summary>
        private void ScriptPlacement(State state, int j, Station from, Station to)
        {
            var source = state.Source;
            var side = source.Side;
            var corner = source.Outcome is PassageOutcome.CornerCleared or PassageOutcome.CornerHeaded;
            var taker = to.Holder ?? (corner ? CornerTaker(state, side) : (state.HolderPending ? Nearest(state.Roster, side, to.Point, to.Point, [], includeKeeper: false) : state.Holder));
            var start = BallStart(state, from);

            var kind = source.Outcome switch
            {
                PassageOutcome.Penalty => HoldKind.Penalty,
                PassageOutcome.CornerCleared or PassageOutcome.CornerHeaded => HoldKind.Corner,
                _ => HoldKind.FreeKick,
            };

            var formation = kind switch
            {
                HoldKind.Penalty => FormationMode.Penalty,
                HoldKind.Corner => FormationMode.Corner,
                _ => FormationMode.FreeKickShot,
            };

            state.SetPiece = formation;

            if (start.DistanceTo(to.Point) > MinMove)
            {
                state.Add(Move(state, BeatKind.Placement, start, to.Point, taker, ActorSource.Named, taker, false, side));
            }

            var hold = Hold(kind, side, to.Point, state.Index, source.Period, taker, formation);

            // The set piece's own events — the corner, the award — happen as it is set down.
            foreach (var matchEvent in to.Events)
            {
                hold.Events.Add(new BeatEvent(matchEvent.Sequence, AtStart: true));
            }

            state.Add(hold);
            SetHolder(state, taker, pending: false);
            state.Attached.Add(j);
        }

        // ---- Challenges and aerial duels -------------------------------------------------------------------

        /// <summary>What happens at a station once the ball is there: a header, a challenge, a foul.</summary>
        private void StationActions(State state, int j)
        {
            var station = state.Stations[j];

            if (station.HasHeader)
            {
                state.Add(HeaderBeat(state, station));
                SetHolder(state, station.Holder, pending: false);

                return;
            }

            if (station.IsChallenge)
            {
                var next = j + 1 < state.Stations.Count ? state.Stations[j + 1] : null;
                var duel = DuelBeat(state, station, next);

                state.Add(duel);
                SetHolder(state, duel.Receiver, pending: false);
            }
        }

        private FilmBeat DuelBeat(State state, Station station, Station? next)
        {
            var source = state.Source;
            var foul = station.Foul;
            var lost = station.Interceptor is not null;

            // The player on the ball, and the one contesting it. In a foul the fouled player is on the ball; in a
            // loss, the attacker is the one who loses it and the defender who comes away with it.
            var carrier = station.Holder;
            var challenger = lost ? station.Interceptor : station.Opponent;
            var start = state.Last is { } last && last.To.DistanceTo(station.Point) < 3.5 ? last.To : station.Point;
            var end = start;

            if (!foul && !lost && next is not null)
            {
                // A challenge the carrier comes through pushes the ball on towards the next station.
                var toward = (next.Point - start).Unit();

                end = FilmSpace.Clamp(start + (toward * Math.Min(DuelPush, start.DistanceTo(next.Point))));
            }

            return new FilmBeat
            {
                Kind = BeatKind.Duel,
                Possession = state.Index,
                Period = source.Period,
                Side = SideOfPlayer(carrier, state),
                From = start,
                To = end,
                Actor = carrier,
                Receiver = lost ? challenger : carrier,
                Opponent = lost ? carrier : challenger,
                Foul = foul,
            };
        }

        private FilmBeat HeaderBeat(State state, Station station)
        {
            var winner = station.Holder;

            return new FilmBeat
            {
                Kind = BeatKind.Header,
                Possession = state.Index,
                Period = state.Source.Period,
                Side = SideOfPlayer(winner, state),
                From = station.Point,
                To = station.Point,
                ZFrom = HeaderContactZ,
                ZTo = HeaderContactZ,
                Actor = winner,
                Receiver = winner,
                Opponent = station.Opponent,
            };
        }

        // ---- Events ----------------------------------------------------------------------------------------

        /// <summary>Attaches the events recorded at a station to the beat that has just ended there.</summary>
        private void AttachEvents(State state, int j)
        {
            if (!state.Attached.Add(j))
            {
                return;
            }

            var station = state.Stations[j];

            if (station.Events.Count == 0 || state.Last is not { } target)
            {
                return;
            }

            foreach (var matchEvent in station.Events)
            {
                // A hold, and the keeper's save, begin as the ball arrives: the line is read as they do.
                target.Events.Add(new BeatEvent(matchEvent.Sequence, AtStart: target.IsHold || target.Kind == BeatKind.Save));
            }

            if (station.Events.Any(matchEvent => matchEvent.Type is EngineEventType.YellowCard or EngineEventType.RedCard or EngineEventType.SecondYellowCard))
            {
                state.Add(Hold(HoldKind.Card, target.Side, target.To, state.Index, state.Source.Period, station.Opponent));
            }
        }

        // ---- Small pieces ----------------------------------------------------------------------------------

        private static FilmBeat Move(
            State state,
            BeatKind kind,
            Vec from,
            Vec to,
            Guid? actor,
            ActorSource actorSource,
            Guid? receiver,
            bool receiverPending,
            MatchSide side,
            bool receiverIsActor = false) => new()
            {
                Kind = kind,
                Possession = state.Index,
                Period = state.Source.Period,
                Side = side,
                From = from,
                To = to,
                Actor = actor,
                ActorSource = actor is null ? actorSource : ActorSource.Named,
                Receiver = receiver,
                ReceiverPending = receiverPending,
                ReceiverIsActor = receiverIsActor,
                ZArc = kind == BeatKind.LoftedPass ? Math.Min(50, 12 + (0.9 * from.DistanceTo(to))) : 0,
            };

        /// <summary>Gets who plays the next beat: the player on the ball, or whoever the motion picks to receive it.</summary>
        private static (Guid? Actor, ActorSource Source) ActorOf(State state) =>
            state.HolderPending ? (null, state.PendingSource) : (state.Holder, ActorSource.Named);

        private static void SetHolder(State state, Guid? holder, bool pending)
        {
            state.Holder = holder;
            state.HolderPending = pending;
            state.PendingSource = ActorSource.PreviousReceiver;
        }

        /// <summary>Gets the side of the player on the ball, or of the last beat while it is not yet known.</summary>
        private MatchSide SideNow(State state) =>
            !state.HolderPending && state.Holder is Guid id && _context.SideOf(id) is { } side
                ? side
                : state.Last?.Side ?? state.Source.Side;

        private MatchSide SideOfPlayer(Guid? player, State state) =>
            player is Guid id && _context.SideOf(id) is { } side ? side : state.Source.Side;

        private static Vec BallStart(State state, Station from) =>
            state.Last is { } last && last.To.DistanceTo(from.Point) < 3.5 ? last.To : from.Point;

        private Guid? KeeperOf(FilmRoster roster, MatchSide side)
        {
            for (var slot = 1; slot <= 11; slot++)
            {
                var entity = FilmRoster.Index(side, slot);

                if (roster.IsOccupied(entity) && _context.Slots[entity].Family == MatchPositionFamily.Goalkeeper)
                {
                    return roster.Occupants[entity];
                }
            }

            return null;
        }

        /// <summary>Gets the team-mate who can reach a point soonest from where the shape has him.</summary>
        private Guid? Nearest(FilmRoster roster, MatchSide side, Vec point, Vec focus, Guid?[] excluding, bool includeKeeper)
        {
            _shape.FillOpen(roster, side, focus, _scratch);

            var best = -1;
            var bestDistance = double.MaxValue;

            for (var slot = 1; slot <= 11; slot++)
            {
                var entity = FilmRoster.Index(side, slot);

                if (!roster.IsOccupied(entity))
                {
                    continue;
                }

                if (!includeKeeper && _context.Slots[entity].Family == MatchPositionFamily.Goalkeeper)
                {
                    continue;
                }

                if (excluding.Contains(roster.Occupants[entity]))
                {
                    continue;
                }

                var distance = _scratch[entity].DistanceTo(point);

                if (distance < bestDistance - 1e-9)
                {
                    bestDistance = distance;
                    best = entity;
                }
            }

            return best < 0 ? null : roster.Occupants[best];
        }

        private Guid? CornerTaker(State state, MatchSide side)
        {
            Guid? best = null;
            var bestScore = -1;

            for (var slot = 1; slot <= 11; slot++)
            {
                var entity = FilmRoster.Index(side, slot);

                if (!state.Roster.IsOccupied(entity) || _context.Slots[entity].Family == MatchPositionFamily.Goalkeeper)
                {
                    continue;
                }

                var participant = state.Roster.Occupants[entity];
                var score = _context.AttributeOf(participant, MatchAttributeName.SetPieces)
                    + _context.AttributeOf(participant, MatchAttributeName.Crossing);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = participant;
                }
            }

            return best;
        }

        /// <summary>How far the player drives the ball before passing it, from a stable hash weighted by Dribbling.</summary>
        private double DriveLength(State state, int j, Guid? carrier, double distance)
        {
            // A player the motion has yet to pick is taken to be a middling dribbler.
            var dribbling = carrier is Guid id ? _context.AttributeOf(id, MatchAttributeName.Dribbling) : 10;
            var skill = 0.4 + (0.6 * (dribbling - 1) / 19.0);
            var unit = FilmHash.Unit(state.Index, j, 7);

            return double.Clamp(3.0 + (7.0 * unit * skill), 3.0, Math.Max(3.0, distance - 4.0));
        }

        private static bool IsCornerOut(State state, int stationIndex) =>
            state.Source.Outcome is PassageOutcome.CornerCleared or PassageOutcome.CornerHeaded
            && stationIndex + 1 < state.Stations.Count
            && state.Stations[stationIndex + 1].Kind == PassageWaypointKind.Restart;

        /// <summary>Whether a ball is crossed: from the wide part of the final third, into the box.</summary>
        private static bool IsWideCross(MatchSide side, Vec from, Vec to)
        {
            var fromAlong = FilmSpace.Attacking(from, side);
            var wide = Math.Abs(from.Y - (FilmSpace.Width / 2)) >= 16.0;

            return wide && fromAlong >= FilmSpace.Length * 2.0 / 3.0 && IsInBox(side, to);
        }

        /// <summary>Whether a point is in the penalty box a side attacks.</summary>
        private static bool IsInBox(MatchSide side, Vec point) =>
            FilmSpace.Attacking(point, side) >= FilmSpace.Length - 16.5 && Math.Abs(point.Y - (FilmSpace.Width / 2)) <= 20.2;

        private static StrikeResult StrikeOf(Station station)
        {
            var saved = station.Touches.Any(touch => touch.Action == PassageAction.Save);

            foreach (var matchEvent in station.Events)
            {
                switch (matchEvent.Type)
                {
                    case EngineEventType.Goal or EngineEventType.PenaltyGoal:
                        return StrikeResult.Goal;
                    case EngineEventType.ShotSaved:
                        return StrikeResult.Saved;
                    case EngineEventType.Woodwork:
                        return StrikeResult.Woodwork;
                    case EngineEventType.ShotBlocked:
                        return StrikeResult.Blocked;
                    case EngineEventType.ShotOffTarget:
                        return StrikeResult.OffTarget;
                    case EngineEventType.PenaltyMissed:
                        return saved ? StrikeResult.Saved : StrikeResult.OffTarget;
                    default:
                        break;
                }
            }

            return StrikeResult.None;
        }
    }

    /// <summary>The state a possession's script is built with.</summary>
    private sealed class State
    {
        private readonly List<FilmBeat> _beats;

        public State(int index, MatchPassageV1 source, FilmRoster roster, List<Station> stations, List<FilmBeat> beats)
        {
            Index = index;
            Source = source;
            Roster = roster;
            Stations = stations;
            _beats = beats;
        }

        public int Index { get; }

        public MatchPassageV1 Source { get; }

        public FilmRoster Roster { get; }

        public List<Station> Stations { get; }

        /// <summary>Gets or sets the player on the ball, as the engine named them.</summary>
        public Guid? Holder { get; set; }

        /// <summary>Gets or sets whether the player on the ball is whoever the motion picks to receive it.</summary>
        public bool HolderPending { get; set; }

        /// <summary>Gets or sets where the next beat's actor comes from while the holder is pending.</summary>
        public ActorSource PendingSource { get; set; } = ActorSource.PreviousReceiver;

        /// <summary>Gets or sets the player the engine named at the start who is the first to receive a keeper's ball.</summary>
        public Guid? Preferred { get; set; }

        /// <summary>Gets or sets how the players stand until the set piece has been taken.</summary>
        public FormationMode SetPiece { get; set; }

        /// <summary>Gets the stations whose events have already been attached.</summary>
        public HashSet<int> Attached { get; } = [];

        /// <summary>Gets the last beat added to this possession.</summary>
        public FilmBeat? Last => _beats.Count == 0 ? null : _beats[^1];

        /// <summary>Takes the named player waiting to receive the first ball, once.</summary>
        public Guid? TakePreferred()
        {
            var preferred = Preferred;

            Preferred = null;

            return preferred;
        }

        public void Add(FilmBeat beat)
        {
            if (SetPiece != FormationMode.Open && beat.Formation == FormationMode.Open)
            {
                beat.Formation = SetPiece;
            }

            // A set piece's arrangement lasts until its delivery has been won, or its strike taken.
            if (beat.Kind is BeatKind.Shot or BeatKind.Header or BeatKind.Clearance
                || (beat.Kind == BeatKind.Cross && SetPiece == FormationMode.FreeKickShot))
            {
                SetPiece = FormationMode.Open;
            }

            _beats.Add(beat);
        }
    }

    /// <summary>One waypoint of a possession, with the touches and events recorded at it.</summary>
    private sealed class Station
    {
        public Station(PassageWaypointV1 waypoint, Vec point)
        {
            Waypoint = waypoint;
            Point = point;
        }

        public PassageWaypointV1 Waypoint { get; }

        public PassageWaypointKind Kind => Waypoint.Kind;

        public Vec Point { get; }

        public List<PassageTouchV1> Touches { get; } = [];

        public List<EngineEventV1> Events { get; } = [];

        /// <summary>Gets the player on the ball at the station, as the engine named them.</summary>
        public Guid? Holder { get; private set; }

        /// <summary>Gets the player against them: the tackler, the marker, the fouler, the player who contested it.</summary>
        public Guid? Opponent { get; private set; }

        /// <summary>Gets the defender who won the ball at the station.</summary>
        public Guid? Interceptor { get; private set; }

        /// <summary>Gets whether a header was won at the station.</summary>
        public bool HasHeader { get; private set; }

        /// <summary>Gets whether the station is a foul.</summary>
        public bool Foul { get; private set; }

        /// <summary>Gets whether the station is a challenge or a foul.</summary>
        public bool IsChallenge { get; private set; }

        public void Resolve()
        {
            Guid? lastCarry = null;
            var runs = new List<Guid>();
            var hasCarry = false;

            foreach (var touch in Touches)
            {
                switch (touch.Action)
                {
                    case PassageAction.Carry:
                        hasCarry = true;
                        lastCarry = touch.ParticipantId;
                        Holder ??= touch.ParticipantId;
                        break;

                    case PassageAction.Pass or PassageAction.Cross or PassageAction.Shot
                        or PassageAction.Penalty or PassageAction.FreeKick or PassageAction.Save
                        or PassageAction.Receive:
                        Holder = touch.ParticipantId;
                        break;

                    case PassageAction.Header:
                        Holder = touch.ParticipantId;
                        HasHeader = true;
                        break;

                    case PassageAction.Tackle:
                        Opponent = touch.ParticipantId;
                        break;

                    case PassageAction.Interception:
                        Interceptor = touch.ParticipantId;
                        break;

                    case PassageAction.Run:
                        runs.Add(touch.ParticipantId);
                        break;

                    default:
                        break;
                }
            }

            Foul = Events.Any(matchEvent => matchEvent.Type == EngineEventType.Foul);

            if (HasHeader)
            {
                // The loser is recorded after the winner; both are at the ball.
                Opponent = runs.Count > 0 ? runs[^1] : null;
            }
            else if (Interceptor is not null)
            {
                // A ball lost: the defender who came away with it, and the attacker who was on it.
                Opponent = Interceptor;
                Holder ??= runs.Count > 0 ? runs[0] : null;
            }
            else if (Foul)
            {
                // The fouled player is on the ball and the fouler has brought him down.
                Holder = lastCarry ?? Holder;
            }
            else if (hasCarry && runs.Count > 0 && Opponent is null)
            {
                // A contested ball the attacker came through.
                Opponent = runs[0];
            }
            else if (Holder is null && runs.Count > 0)
            {
                // A player running onto a ball: the one caught offside.
                Holder = runs[0];
            }

            IsChallenge = !HasHeader && (Foul || Interceptor is not null || (Opponent is not null && Holder is not null));
        }
    }
}

/// <summary>A small stable hash, so the film's choices are the same every time it is built (`replay-v4`).</summary>
internal static class FilmHash
{
    /// <summary>Hashes some numbers and a string to a stable 32-bit value.</summary>
    /// <param name="seed">The string to mix in.</param>
    public static uint Of(string seed)
    {
        var hash = 2166136261u;

        foreach (var character in seed)
        {
            hash ^= character;
            hash *= 16777619u;
        }

        return hash;
    }

    /// <summary>Hashes three numbers to a number in [0, 1).</summary>
    /// <param name="a">The first number.</param>
    /// <param name="b">The second number.</param>
    /// <param name="c">The third number.</param>
    public static double Unit(int a, int b, int c)
    {
        var hash = Of($"{a}:{b}:{c}");

        return (hash % 100_000) / 100_000.0;
    }
}
