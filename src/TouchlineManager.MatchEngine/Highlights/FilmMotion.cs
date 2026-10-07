using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Highlights;

/// <summary>Where one beat sits in the motion record (`replay-v4`).</summary>
/// <param name="FirstRecord">The index of the record the beat starts at.</param>
/// <param name="LastRecord">The index of the record the beat ends at.</param>
/// <param name="StartSeconds">When the beat starts, in seconds of real time since the first whistle.</param>
/// <param name="EndSeconds">When the beat ends.</param>
internal readonly record struct BeatSpan(int FirstRecord, int LastRecord, double StartSeconds, double EndSeconds)
{
    /// <summary>Gets how long the beat runs for, in seconds of real time.</summary>
    public double Seconds => EndSeconds - StartSeconds;
}

/// <summary>
/// The whole film's motion: every player and the ball, in real time, at a fine step (`replay-v4`).
/// </summary>
/// <remarks>
/// Positions are metres and times are seconds of real time. The film plays them back at the one pace the timing
/// solved, which is a division and nothing more — so the bounds the players were simulated under are the bounds the
/// viewer sees, multiplied by that pace.
/// </remarks>
internal sealed class FilmMotionResult
{
    private double[] _time = new double[4096];
    private float[] _playerX = new float[4096 * FilmRoster.Size];
    private float[] _playerY = new float[4096 * FilmRoster.Size];
    private float[] _ballX = new float[4096];
    private float[] _ballY = new float[4096];
    private float[] _ballZ = new float[4096];

    /// <summary>Gets how many records there are.</summary>
    public int Count { get; private set; }

    /// <summary>Gets where each beat sits in the record.</summary>
    public BeatSpan[] Spans { get; set; } = [];

    /// <summary>Gets or sets the real-time length of the whole film, holds included.</summary>
    public double TotalSeconds { get; set; }

    /// <summary>Gets or sets the real time that moves had to be lengthened by to meet their constraints.</summary>
    public double ExtensionSeconds { get; set; }

    /// <summary>Gets or sets the real-time length of the moves, holds excluded.</summary>
    public double MotionSeconds { get; set; }

    /// <summary>Gets the time of a record.</summary>
    /// <param name="record">The record.</param>
    public double TimeOf(int record) => _time[record];

    /// <summary>Gets a player's X at a record.</summary>
    public float PlayerX(int record, int entity) => _playerX[(record * FilmRoster.Size) + entity];

    /// <summary>Gets a player's Y at a record.</summary>
    public float PlayerY(int record, int entity) => _playerY[(record * FilmRoster.Size) + entity];

    /// <summary>Gets the ball's X at a record.</summary>
    public float BallX(int record) => _ballX[record];

    /// <summary>Gets the ball's Y at a record.</summary>
    public float BallY(int record) => _ballY[record];

    /// <summary>Gets the ball's altitude at a record.</summary>
    public float BallZ(int record) => _ballZ[record];

    /// <summary>Appends a record.</summary>
    internal int Add(double time, double[] px, double[] py, double ballX, double ballY, double ballZ)
    {
        if (Count == _time.Length)
        {
            Grow();
        }

        _time[Count] = time;

        var offset = Count * FilmRoster.Size;

        for (var entity = 0; entity < FilmRoster.Size; entity++)
        {
            _playerX[offset + entity] = (float)px[entity];
            _playerY[offset + entity] = (float)py[entity];
        }

        _ballX[Count] = (float)ballX;
        _ballY[Count] = (float)ballY;
        _ballZ[Count] = (float)ballZ;

        return Count++;
    }

    /// <summary>Drops every record from an index on, to retry a beat that needed longer.</summary>
    internal void Truncate(int count) => Count = count;

    private void Grow()
    {
        var size = _time.Length * 2;

        Array.Resize(ref _time, size);
        Array.Resize(ref _playerX, size * FilmRoster.Size);
        Array.Resize(ref _playerY, size * FilmRoster.Size);
        Array.Resize(ref _ballX, size);
        Array.Resize(ref _ballY, size);
        Array.Resize(ref _ballZ, size);
    }
}

/// <summary>
/// Moves the ball and the twenty-two players through the beats, at bounded speed (`replay-v4`).
/// </summary>
/// <remarks>
/// <para>
/// The ball follows its beat: a ground pass eases out, a lofted ball, a cross and a clearance arc, a strike goes
/// where its outcome sent it. A ball that is being driven is carried by the player driving it instead, so he is at
/// it by construction.
/// </para>
/// <para>
/// The players are simulated in real-time units at a fine step. Each steers towards one target at a time — a point
/// he has to be at when a beat ends, the ball if he is pressing it, a support position beside it, or his place in
/// the shape — limited by a top speed and by how quickly he can change speed. Whoever receives a pass the engine
/// did not name is chosen as the beat approaches, from where the players actually are: the team-mate who can reach
/// the ball soonest. When a player who has to be somewhere cannot be there in time, the beat is lengthened and
/// played again; no cap is ever exceeded to make up the time. The film then plays all of it back at the one pace the
/// timing solved.
/// </para>
/// </remarks>
internal sealed class FilmMotion
{
    /// <summary>How quickly the focus the block follows catches up with the ball, in seconds.</summary>
    private const double FocusSeconds = 3.0;

    /// <summary>How far ahead of a driving player the ball is kept, in metres.</summary>
    private const double CarryLead = 0.8;

    /// <summary>How near the ball a driver has to be for it to be his, in metres.</summary>
    private const double AttachDistance = 1.5;

    /// <summary>How long a ball takes to settle onto the player driving it, in seconds.</summary>
    private const double SettleSeconds = 0.4;

    /// <summary>How long before a deadline a pinned player aims to be there, in seconds.</summary>
    private const double Margin = 0.3;

    /// <summary>How far a goalkeeper can dive towards a strike, in metres.</summary>
    private const double DiveReach = 3.5;

    /// <summary>How far ahead, in seconds of real time, a player starts making for a place he will have to be.</summary>
    private const double Lookahead = 12.0;

    /// <summary>How near a place a player has to get to be there, in metres.</summary>
    private const double HardTolerance = 1.2;

    /// <summary>How near a driven ball's end a player has to be: he is moving, so it is looser.</summary>
    private const double SoftTolerance = 1.8;

    /// <summary>How far a restart's spot has to be from where the ball stopped for it to be put down rather than fetched, in metres (`replay-v9`).</summary>
    private const double PutDownDistance = 6.0;

    /// <summary>How far from his place on the line the keeper has to be before he moves to it, in metres (`replay-v9`).</summary>
    private const double KeeperSetOff = 1.5;

    /// <summary>How near the ball the keeper has to be when a strike he saves ends, in metres: he is not sent there ahead of time, so he has to arrive (`replay-v9`).</summary>
    private const double SaveTolerance = 0.7;

    /// <summary>How far from the flag the taker of a corner may still be when the ball is put down, in metres: what he runs while it is held (`replay-v8`).</summary>
    private const double CornerArrival = 14.0;

    /// <summary>How far from his place in the shape a player has to be before he sets off for it, in metres.</summary>
    private const double SetOffDistance = 8.0;

    /// <summary>How near his place in the shape a player has to get before he stops, in metres.</summary>
    private const double SettleDistance = 1.0;

    /// <summary>How much of the speed it could still stop from a pack running to its places keeps, as a share (`replay-v13`).</summary>
    private const double PackBraking = 0.9;

    /// <summary>How far from his place at a set piece a player has to be before he sets off for it, in metres (`replay-v6`).</summary>
    private const double CornerSetOff = 1.5;

    /// <summary>
    /// The roles that are kept from one beat to the next (`replay-v6`): a challenger, a second and a cover for the
    /// side without the ball; five places for its opponents' options, which a flank attack, a counter and a delivery
    /// fill differently; and six for the defenders who mark and hold zones when a cross comes in.
    /// </summary>
    private const int RoleSlots = 14;

    /// <summary>The first of the slots the defenders take to mark and hold zones in the box.</summary>
    private const int BoxDefenderSlot = 8;

    /// <summary>How many beats before a cross the box begins to fill, so that the runners are there when it arrives.</summary>
    private const int DeliveryLead = 3;

    /// <summary>How many beats after a cross, at the most, the box stays set while it is headed, cleared or shot at.</summary>
    private const int DeliveryAftermath = 3;

    /// <summary>How near its goal the ball is, in metres, before the defenders send a second player to it.</summary>
    private const double FinalThird = 35.0;

    /// <summary>How near a team-mate a player walks before he turns aside, in metres.</summary>
    private const double PersonalSpace = 2.5;

    /// <summary>The furthest a player is turned aside from where he is going, in metres.</summary>
    private const double MaxElbow = 2.0;

    /// <summary>How near a role's place a team-mate's place in the shape may be, in metres.</summary>
    private const double RoleClearance = 3.5;

    /// <summary>
    /// How far from the ball, in metres, a player who is not on it keeps (`replay-v6`): only the players the beat is
    /// about, the challenger and whoever is due on the ball may be nearer.
    /// </summary>
    private const double ClearZone = 6.5;

    /// <summary>How long before he is due on the ball, in seconds, a player kept clear of it may come in.</summary>
    private const double ComeInSeconds = 2.5;

    /// <summary>How many seconds of play ahead the places the ball is going to are kept clear, so that players have time to leave them.</summary>
    private const double ClearHorizon = 6.0;

    /// <summary>How far behind the ball the player covering the challenger stands, in metres.</summary>
    private const double CoverDistance = 7.0;

    /// <summary>How far from the middle of the pitch the ball is, in metres, before it is a flank attack.</summary>
    private const double FlankLane = 16.0;

    /// <summary>How far up the pitch a flank attack is before the support runs and the box is set, in metres from the goal line.</summary>
    private const double WingAttackDepth = 50.0;

    /// <summary>How far from the goal line the edge of the box is, in metres.</summary>
    private const double EdgeOfBox = 20.0;

    /// <summary>How far the play can move from a presser or supporter before somebody else takes the role, in metres.</summary>
    private const double RoleRadius = 16.0;

    /// <summary>How far the shape can run from the place a player is walking to before he turns for the new one, in metres.</summary>
    private const double RelatchDistance = 10.0;

    /// <summary>How many beats ahead the player who will receive the ball is chosen, so that he has time to get there.</summary>
    private const int Foresight = 4;

    /// <summary>How often a beat that needs longer is tried again.</summary>
    private const int MaxRetries = 6;

    /// <summary>How fast a goalkeeper may change speed in a dive, in metres per second per second.</summary>
    private const double DiveAcceleration = 20.0;

    private readonly FilmContext _context;
    private readonly FilmShape _shape;
    private readonly IReadOnlyList<FilmRoster> _rosters;
    private readonly IReadOnlyList<MatchPassageV1> _passages;
    private readonly HighlightOptionsV1 _options;

    private readonly double[] _px = new double[FilmRoster.Size];
    private readonly double[] _py = new double[FilmRoster.Size];
    private readonly double[] _vx = new double[FilmRoster.Size];
    private readonly double[] _vy = new double[FilmRoster.Size];
    private readonly bool[] _walking = new bool[FilmRoster.Size];
    private readonly bool[] _savedWalking = new bool[FilmRoster.Size];
    private readonly Vec[] _destinations = new Vec[FilmRoster.Size];
    private readonly Vec[] _savedDestinations = new Vec[FilmRoster.Size];

    // Who is pressing and who is supporting, kept from one beat to the next while the play stays near them.
    private readonly int[] _roles = new int[RoleSlots];
    private readonly int[] _savedRoles = new int[RoleSlots];
    private readonly Spec[] _specs = new Spec[RoleSlots];
    private readonly Vec[] _targets = new Vec[FilmRoster.Size];
    private readonly Task[] _tasks = new Task[FilmRoster.Size];
    private readonly bool[] _clear = new bool[FilmRoster.Size];
    private readonly List<Vec> _path = [];
    private readonly List<Pin> _pins = [];
    private readonly List<Pin> _endPins = [];

    private readonly double[] _savedPx = new double[FilmRoster.Size];
    private readonly double[] _savedPy = new double[FilmRoster.Size];
    private readonly double[] _savedVx = new double[FilmRoster.Size];
    private readonly double[] _savedVy = new double[FilmRoster.Size];

    private Vec _ball;
    private double _ballZ;
    private Vec _focus;
    private Vec _savedBall;
    private double _savedBallZ;
    private Vec _savedFocus;
    private Vec _setPieceAnchor = FilmSpace.Centre;
    private MatchSide _setPieceSide = MatchSide.Home;
    private int _setPieceTaker = -1;
    private Vec _celebration = FilmSpace.Centre;
    private FilmRoster _roster;

    /// <summary>Initializes the motion.</summary>
    /// <param name="context">The film's context.</param>
    /// <param name="shape">Where the players want to be.</param>
    /// <param name="rosters">Who is on the pitch for each possession.</param>
    /// <param name="passages">The recorded possessions, which say which of them were counter-attacks.</param>
    public FilmMotion(FilmContext context, FilmShape shape, IReadOnlyList<FilmRoster> rosters, IReadOnlyList<MatchPassageV1> passages)
    {
        _context = context;
        _shape = shape;
        _rosters = rosters;
        _passages = passages;
        _options = context.Options;
        _roster = context.Starters;
    }

    /// <summary>Plays the beats through, at the given pace.</summary>
    /// <param name="beats">The beats, with their natural lengths.</param>
    /// <param name="pace">The one pace the film is played at.</param>
    /// <param name="holdScale">The factor the holds are scaled by.</param>
    public FilmMotionResult Run(IReadOnlyList<FilmBeat> beats, double pace, double holdScale)
    {
        var result = new FilmMotionResult();
        var spans = new BeatSpan[beats.Count];
        var time = 0.0;
        var extension = 0.0;
        var motion = 0.0;

        if (beats.Count == 0)
        {
            return result;
        }

        ForgetChoices(beats);
        MarkCounters(beats);

        _roster = RosterOf(beats[0]);
        Reset(beats, 0);

        for (var index = 0; index < beats.Count; index++)
        {
            var beat = beats[index];

            _roster = RosterOf(beat);

            var cut = beat.Cut && index > 0;

            if (cut)
            {
                Reset(beats, index);
            }

            ResolveChoices(beats, index);

            // A set piece is arranged from the moment it is known to be coming: where it is taken, and by whom.
            if (beat.Formation is FormationMode.Corner or FormationMode.FreeKickShot or FormationMode.FreeKickCross or FormationMode.Penalty or FormationMode.GoalKick
                && SetPieceHold(beats, index) is { } hold)
            {
                _setPieceAnchor = hold.To;
                _setPieceSide = hold.Side;
                _setPieceTaker = hold.Actor is Guid taker ? _roster.EntityOf(taker) : -1;
            }

            // The ball is where it is, whatever the plan said: the move is as long as the ground it has to cover.
            var scripted = beat.NaturalSeconds;
            var planned = beat.IsHold
                ? beat.HoldFilmSeconds * holdScale * pace
                : FilmTiming.NaturalSeconds(_context, beat, _ball.DistanceTo(beat.To));

            var firstRecord = result.Count == 0 || cut ? result.Add(time, _px, _py, _ball.X, _ball.Y, _ballZ) : result.Count - 1;
            var duration = Simulate(beats, index, planned, time, result);

            spans[index] = new BeatSpan(firstRecord, result.Count - 1, time, time + duration);
            time += duration;

            if (beat.IsHold)
            {
                continue;
            }

            motion += duration;
            extension += duration - scripted;
        }

        result.Spans = spans;
        result.TotalSeconds = time;
        result.MotionSeconds = motion;
        result.ExtensionSeconds = extension;

        return result;
    }

    private FilmRoster RosterOf(FilmBeat beat) => beat.Possession >= 0 ? _rosters[beat.Possession] : _roster;

    /// <summary>Gets the hold a set-piece beat is the lead-up to, or is: the beats that wait for the ball to be put down belong to it.</summary>
    private static FilmBeat? SetPieceHold(IReadOnlyList<FilmBeat> beats, int index)
    {
        var beat = beats[index];

        for (var next = index; next < beats.Count && next <= index + 4; next++)
        {
            var other = beats[next];

            if (other.Possession != beat.Possession || (next > index && other.Cut))
            {
                break;
            }

            if (other.IsHold && other.Formation == beat.Formation)
            {
                return other;
            }
        }

        return null;
    }

    // ---- Cuts and set-ups ------------------------------------------------------------------------------------

    /// <summary>Puts everybody at their kick-off places: the start of the film, a kick-off after a goal, the second half.</summary>
    private void Reset(IReadOnlyList<FilmBeat> beats, int index)
    {
        var kickOff = FindKickOff(beats, index);
        var side = kickOff?.Side ?? beats[index].Side;
        var spot = kickOff?.To ?? FilmSpace.Centre;
        var state = new ShapeState(FormationMode.KickOff, side, spot);

        _shape.Fill(_roster, state, spot, _targets);

        for (var entity = 0; entity < FilmRoster.Size; entity++)
        {
            if (!_roster.IsOccupied(entity))
            {
                continue;
            }

            _px[entity] = _targets[entity].X;
            _py[entity] = _targets[entity].Y;
            _vx[entity] = 0;
            _vy[entity] = 0;
            _walking[entity] = false;
        }

        Array.Fill(_roles, -1);

        // The taker stands at the ball.
        var taker = kickOff?.Actor is Guid actor ? _roster.EntityOf(actor) : -1;

        if (taker >= 0)
        {
            _px[taker] = spot.X - (FilmSpace.Direction(side) * 0.6);
            _py[taker] = spot.Y;
        }

        _ball = kickOff is not null ? spot : beats[index].From;
        _ballZ = 0;
        _focus = _ball;
    }

    private static FilmBeat? FindKickOff(IReadOnlyList<FilmBeat> beats, int index)
    {
        for (var next = index; next < beats.Count && next < index + 4; next++)
        {
            if (beats[next].Hold == HoldKind.KickOff)
            {
                return beats[next];
            }
        }

        return null;
    }

    // ---- Who plays and who receives -------------------------------------------------------------------------

    /// <summary>Marks the beats of the possessions the engine played as counter-attacks (`replay-v6`).</summary>
    private void MarkCounters(IReadOnlyList<FilmBeat> beats)
    {
        foreach (var beat in beats)
        {
            beat.Counter = beat.Possession >= 0 && beat.Possession < _passages.Count && _passages[beat.Possession].Counter;
        }
    }

    /// <summary>Forgets who the motion chose last time, so that every run starts from the script.</summary>
    private static void ForgetChoices(IReadOnlyList<FilmBeat> beats)
    {
        foreach (var beat in beats)
        {
            if (beat.ActorSource != ActorSource.Named)
            {
                beat.Actor = null;
            }

            if (beat.ReceiverPending || beat.ReceiverIsActor)
            {
                beat.Receiver = null;
            }

            if (beat.Contested)
            {
                beat.Receiver = null;
                beat.Opponent = null;
            }
        }
    }

    /// <summary>Chooses the players the script left open for this beat and the next, from where everybody is now.</summary>
    private void ResolveChoices(IReadOnlyList<FilmBeat> beats, int index)
    {
        for (var k = index; k < beats.Count && k <= index + Foresight; k++)
        {
            var beat = beats[k];

            if (k > index && beat.Cut)
            {
                break;
            }

            ResolveActor(beats, k);
            ResolveReceiver(beats, k);
        }
    }

    private void ResolveActor(IReadOnlyList<FilmBeat> beats, int k)
    {
        var beat = beats[k];

        if (beat.Actor is null && beat.ActorSource == ActorSource.PreviousReceiver && k > 0)
        {
            beat.Actor = beats[k - 1].Receiver;
        }

        if (beat.Actor is null && beat.ActorSource != ActorSource.Named)
        {
            var near = k == 0 || beats[k - 1].IsHold ? beat.From : beats[k - 1].To;

            beat.Actor = Nearest(beat.Side, near, beat, k, beats);
        }

        if (beat.ReceiverIsActor)
        {
            beat.Receiver = beat.Actor;
        }
    }

    private void ResolveReceiver(IReadOnlyList<FilmBeat> beats, int k)
    {
        var beat = beats[k];

        // A held ball is won by the defender who can reach it soonest, off the man who has it (`replay-v11`).
        if (beat.Contested)
        {
            if (beat.Receiver is null)
            {
                beat.Receiver = Nearest(MatchInputV1.OpponentOf(beat.Side), beat.To, beat, k, beats);
                beat.Opponent = beat.Actor;
            }

            return;
        }

        if (!beat.ReceiverPending || beat.Receiver is not null)
        {
            return;
        }

        beat.Receiver = Nearest(beat.Side, beat.To, beat, k, beats) ?? beat.Actor;
    }

    /// <summary>
    /// Gets the player of a side who can reach a point soonest from where he is, keeping clear of the players the
    /// engine has already given a part in the next few beats.
    /// </summary>
    private Guid? Nearest(MatchSide side, Vec point, FilmBeat beat, int k, IReadOnlyList<FilmBeat> beats)
    {
        var best = -1;
        var bestTime = double.MaxValue;

        for (var slot = 1; slot <= 11; slot++)
        {
            var entity = FilmRoster.Index(side, slot);

            if (!_roster.IsOccupied(entity) || _context.Slots[entity].Family == MatchPositionFamily.Goalkeeper)
            {
                continue;
            }

            var occupant = _roster.Occupants[entity];

            if (occupant == beat.Actor || IsEngaged(occupant, beats, k))
            {
                continue;
            }

            var seconds = new Vec(_px[entity], _py[entity]).DistanceTo(point) / _options.SprintMetresPerSecond;

            if (seconds < bestTime - 1e-9)
            {
                bestTime = seconds;
                best = entity;
            }
        }

        return best < 0 ? null : _roster.Occupants[best];
    }

    /// <summary>Whether a player already has a named part in the beats around this one.</summary>
    private static bool IsEngaged(Guid participant, IReadOnlyList<FilmBeat> beats, int k)
    {
        for (var m = Math.Max(0, k - 1); m <= Math.Min(beats.Count - 1, k + 2); m++)
        {
            var other = beats[m];

            if (m != k && (other.Actor == participant || other.Receiver == participant))
            {
                return true;
            }

            if (other.Opponent == participant)
            {
                return true;
            }
        }

        return false;
    }

    // ---- One beat ---------------------------------------------------------------------------------------------

    private double Simulate(IReadOnlyList<FilmBeat> beats, int index, double planned, double start, FilmMotionResult result)
    {
        var beat = beats[index];
        var firstUnsaved = result.Count;

        Save();

        var duration = planned;

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            Restore();
            result.Truncate(firstUnsaved);
            PlanTasks(beats, index, duration);

            Steps(beat, duration, start, result);

            if (beat.IsHold || attempt == MaxRetries)
            {
                break;
            }

            var shortfall = Shortfall(beat);

            if (shortfall <= 0)
            {
                break;
            }

            duration += shortfall;
        }

        return duration;
    }

    private void Save()
    {
        Array.Copy(_px, _savedPx, FilmRoster.Size);
        Array.Copy(_py, _savedPy, FilmRoster.Size);
        Array.Copy(_vx, _savedVx, FilmRoster.Size);
        Array.Copy(_vy, _savedVy, FilmRoster.Size);
        Array.Copy(_walking, _savedWalking, FilmRoster.Size);
        Array.Copy(_destinations, _savedDestinations, FilmRoster.Size);
        Array.Copy(_roles, _savedRoles, RoleSlots);

        _savedBall = _ball;
        _savedBallZ = _ballZ;
        _savedFocus = _focus;
    }

    private void Restore()
    {
        Array.Copy(_savedPx, _px, FilmRoster.Size);
        Array.Copy(_savedPy, _py, FilmRoster.Size);
        Array.Copy(_savedVx, _vx, FilmRoster.Size);
        Array.Copy(_savedVy, _vy, FilmRoster.Size);
        Array.Copy(_savedWalking, _walking, FilmRoster.Size);
        Array.Copy(_savedDestinations, _destinations, FilmRoster.Size);
        Array.Copy(_savedRoles, _roles, RoleSlots);

        _ball = _savedBall;
        _ballZ = _savedBallZ;
        _focus = _savedFocus;
    }

    /// <summary>
    /// Moves the place a player holds in the shape off the places his team-mates are making for, so that a player sent
    /// to the ball is not run into by the one who was standing where he is going.
    /// </summary>
    /// <summary>Gets how far a player is turned aside from his target by a team-mate who is in the way (`replay-v6`).</summary>
    private Vec Elbow(int entity, Vec position)
    {
        var side = FilmRoster.SideOf(entity);
        var push = Vec.Zero;

        for (var slot = 1; slot <= 11; slot++)
        {
            var mate = FilmRoster.Index(side, slot);

            if (mate == entity || !_roster.IsOccupied(mate) || _context.Slots[mate].Family == MatchPositionFamily.Goalkeeper)
            {
                continue;
            }

            var away = position - new Vec(_px[mate], _py[mate]);
            var distance = away.Length;

            if (distance >= PersonalSpace)
            {
                continue;
            }

            push += (distance < 1e-6 ? new Vec(0, mate > entity ? -1 : 1) : away * (1.0 / distance)) * (PersonalSpace - distance);
        }

        return push.Length > MaxElbow ? push.Unit() * MaxElbow : push;
    }

    private Vec ClearOfRoles(int entity, Vec wanted)
    {
        var side = FilmRoster.SideOf(entity);

        for (var slot = 0; slot < RoleSlots; slot++)
        {
            if (_roles[slot] < 0 || _specs[slot].Side != side)
            {
                continue;
            }

            var away = wanted - _specs[slot].Point;
            var distance = away.Length;

            if (distance >= RoleClearance)
            {
                continue;
            }

            var direction = distance < 1e-6 ? new Vec(0, entity % 2 == 0 ? 1 : -1) : away * (1.0 / distance);

            wanted = FilmSpace.Clamp(_specs[slot].Point + (direction * RoleClearance), 1.5);
        }

        return wanted;
    }

    /// <summary>How much longer the beat has to be for everyone who has to be somewhere to be there, or zero.</summary>
    private double Shortfall(FilmBeat beat)
    {
        var worst = 0.0;

        foreach (var pin in _endPins)
        {
            var entity = _roster.EntityOf(pin.Participant);

            if (entity < 0 || pin.Soft)
            {
                continue;
            }

            var distance = new Vec(_px[entity], _py[entity]).DistanceTo(pin.Point);

            if (distance <= pin.Tolerance)
            {
                continue;
            }

            var speed = IsKeeperDive(beat, entity) ? _options.DiveMetresPerSecond : _options.SprintMetresPerSecond;

            worst = Math.Max(worst, ((distance - (pin.Tolerance * 0.5)) / speed) + 0.25);
        }

        return worst;
    }

    // ---- What everyone is doing in a beat ------------------------------------------------------------------------

    private void PlanTasks(IReadOnlyList<FilmBeat> beats, int index, double duration)
    {
        var beat = beats[index];

        for (var entity = 0; entity < FilmRoster.Size; entity++)
        {
            _tasks[entity] = default;
        }

        _endPins.Clear();
        CollectPins(beats, index, _endPins);

        foreach (var pin in _endPins)
        {
            Assign(pin, duration);
        }

        // Whoever has to be somewhere in the beats to come starts making their way as soon as they should.
        var elapsed = duration;

        for (var k = index + 1; k < beats.Count && elapsed < Lookahead && !beats[k].Cut; k++)
        {
            elapsed += beats[k].IsHold ? beats[k].HoldFilmSeconds * 2.0 : beats[k].NaturalSeconds;

            _pins.Clear();
            CollectPins(beats, k, _pins);

            foreach (var pin in _pins)
            {
                // The keeper stays on his line until the ball is dead: he does not set off for the goal kick, or for
                // where the shot will arrive, while the ball is still on its way (`replay-v9`).
                if (IsKeeperKeptOnLine(pin, beats, index, k))
                {
                    continue;
                }

                Assign(pin, elapsed);
            }
        }

        AssignRoles(beats, index);
        AssignDive(beat);
        MarkKeptClear(beats, index);

    }

    /// <summary>
    /// Marks who keeps clear of the ball for a beat in open play (`replay-v6`): everybody but the keepers, the players
    /// the beat is about (the one who plays it, who receives it, who is beaten or tackled), whoever has to be on the ball
    /// by the end of it, and the one challenger. A delivery into the box and a set piece fill the area around the ball
    /// on purpose, so they are left alone.
    /// </summary>
    private void MarkKeptClear(IReadOnlyList<FilmBeat> beats, int index)
    {
        Array.Clear(_clear);
        _path.Clear();

        var beat = beats[index];

        if (beat.IsHold
            || beat.Formation != FormationMode.Open
            || beat.Kind is not (BeatKind.Carry or BeatKind.Pass or BeatKind.LoftedPass or BeatKind.Duel)
            || CrossOf(beats, index) >= 0)
        {
            return;
        }

        for (var entity = 0; entity < FilmRoster.Size; entity++)
        {
            var challenger = _tasks[entity].Kind == TaskKind.Press && (int)_tasks[entity].Value == 0;

            _clear[entity] = _roster.IsOccupied(entity)
                && _context.Slots[entity].Family != MatchPositionFamily.Goalkeeper
                && !challenger;
        }

        foreach (var involved in new Guid?[] { beat.Actor, beat.Receiver, beat.Opponent })
        {
            if (involved is Guid id && _roster.EntityOf(id) is var found and >= 0)
            {
                _clear[found] = false;
            }
        }

        foreach (var pin in _endPins)
        {
            var due = _roster.EntityOf(pin.Participant);

            if (due >= 0)
            {
                _clear[due] = false;
            }
        }

        // Where the ball is going over the next few seconds of open play, and so where nobody should be standing.
        var elapsed = 0.0;

        for (var k = index; k < beats.Count && elapsed < ClearHorizon; k++)
        {
            var ahead = beats[k];

            if (k > index && (ahead.Cut || ahead.IsHold || ahead.Formation != FormationMode.Open || ahead.Possession < 0))
            {
                break;
            }

            _path.Add(ahead.To);
            elapsed += ahead.NaturalSeconds;
        }
    }

    /// <summary>Moves a place off the ball, and off the place it is going to, to the edge of the zone a player keeps clear of it.</summary>
    private Vec KeepClear(FilmBeat beat, Vec target, Vec position)
    {
        // Pushed off one place it can land in another's zone, so it is settled in a few passes.
        for (var pass = 0; pass < 3; pass++)
        {
            var moved = false;

            for (var point = -1; point < _path.Count; point++)
            {
                var around = point < 0 ? _ball : _path[point];
                var away = target - around;

                if (away.Length >= ClearZone)
                {
                    continue;
                }

                var outward = away.Length > 1e-6 ? away : position - around;
                var direction = outward.Length > 1e-6 ? outward.Unit() : new Vec(0, position.Y >= FilmSpace.Width / 2 ? 1 : -1);

                target = FilmSpace.Clamp(around + (direction * ClearZone), 1.5);
                moved = true;
            }

            if (!moved)
            {
                break;
            }
        }

        return target;
    }

    private void Assign(Pin pin, double deadline)
    {
        var entity = _roster.EntityOf(pin.Participant);

        if (entity >= 0 && _tasks[entity].Kind == TaskKind.None)
        {
            _tasks[entity] = new Task(pin.Soft ? TaskKind.Carry : TaskKind.Pin, pin.Point, deadline);
        }
    }

    /// <summary>
    /// Lists who has to be where when a beat ends: the player who receives the ball, whoever plays the next beat at
    /// the place the ball will be, and whoever contests it.
    /// </summary>
    private void CollectPins(IReadOnlyList<FilmBeat> beats, int k, List<Pin> pins)
    {
        var beat = beats[k];

        if (beat.Receiver is Guid receiver)
        {
            var carrier = beat.Kind is BeatKind.Carry or BeatKind.Duel && beat.Receiver == beat.Actor;

            // The taker of a corner is on his way to the flag, and has the hold to arrive in: the ball is put down once
            // he is within the distance he can still cover (`replay-v8`).
            var tolerance = IsCornerPlacement(beat) ? CornerArrival : carrier ? SoftTolerance : beat.Kind is BeatKind.Shot or BeatKind.Save ? SaveTolerance : HardTolerance;

            pins.Add(new Pin(receiver, CarryPoint(beat, carrier), tolerance, carrier));
        }

        if (k + 1 >= beats.Count || beats[k + 1].Cut)
        {
            return;
        }

        var next = beats[k + 1];

        if (next.Possession < 0 || next.Hold is HoldKind.Goal or HoldKind.Card or HoldKind.Substitution or HoldKind.HalfTime)
        {
            return;
        }

        // A ball that is put down somewhere else is not fetched from where it stopped: a goal kick is taken from the
        // six-yard box and a corner from the flag, so the keeper does not run to meet a shot that goes wide (`replay-v9`).
        if (next.Actor is Guid actor && actor != beat.Receiver && !IsPutDownElsewhere(next))
        {
            pins.Add(new Pin(actor, beat.To, HardTolerance));
        }

        if (next.Kind is BeatKind.Duel or BeatKind.Header && next.Opponent is Guid opponent && opponent != beat.Receiver)
        {
            var side = _context.SideOf(opponent) ?? MatchSide.Home;

            pins.Add(new Pin(opponent, beat.To + ((FilmSpace.OwnGoal(side) - beat.To).Unit() * 0.9), 1.5));
        }
    }

    /// <summary>
    /// Whether a pin for a beat still to come is a goalkeeper's for a strike, a save, a dead-ball restart, or a later
    /// possession (`replay-v9`): his place for those is not taken up ahead of time, only when the beat itself is reached.
    /// </summary>
    private bool IsKeeperKeptOnLine(Pin pin, IReadOnlyList<FilmBeat> beats, int index, int k)
    {
        var entity = _roster.EntityOf(pin.Participant);

        if (entity < 0 || _context.Slots[entity].Family != MatchPositionFamily.Goalkeeper)
        {
            return false;
        }

        static bool Stands(FilmBeat beat) => beat.IsHold || beat.Kind is BeatKind.Shot or BeatKind.Save or BeatKind.Placement;

        // Nor for anything that is the next possession's: the ball is not dead yet.
        return beats[k].Possession != beats[index].Possession || Stands(beats[k]) || (k + 1 < beats.Count && Stands(beats[k + 1]));
    }

    /// <summary>Whether a beat puts the ball down at a corner flag (`replay-v8`).</summary>
    private static bool IsCornerPlacement(FilmBeat beat) =>
        beat.Kind == BeatKind.Placement && beat.Formation == FormationMode.Corner;

    /// <summary>Whether a beat puts the ball down at a spot well away from where it stopped, for a restart (`replay-v9`).</summary>
    private static bool IsPutDownElsewhere(FilmBeat beat) =>
        beat.Kind == BeatKind.Placement && (beat.Formation is FormationMode.Corner or FormationMode.GoalKick || beat.Distance > PutDownDistance);

    /// <summary>Where a pinned player stands: at the point, or just behind the ball if he is the one driving it.</summary>
    private static Vec CarryPoint(FilmBeat beat, bool carrier)
    {
        if (carrier && beat.Distance > 0.5)
        {
            return beat.To - ((beat.To - beat.From).Unit() * CarryLead);
        }

        return beat.To;
    }

    // ---- Roles around the ball (`replay-v6`) ----------------------------------------------------------------------

    /// <summary>
    /// Picks who closes the ball down, who covers him, and where the team-mates of the player on the ball offer
    /// themselves, for a beat in open play.
    /// </summary>
    /// <remarks>
    /// One defender goes to the ball and a second only where the play is dangerous; the rest hold the shape. The
    /// side with the ball offers options ten metres and more away, which is what leaves the player on it alone, and
    /// changes them for a flank attack and for a counter.
    /// </remarks>
    private void AssignRoles(IReadOnlyList<FilmBeat> beats, int index)
    {
        var beat = beats[index];
        var cross = CrossOf(beats, index);

        // A set piece arranges everybody itself: nobody presses the flag or offers himself to the taker.
        if (beat.IsHold
            || beat.Formation != FormationMode.Open
            || (cross < 0 && beat.Kind is BeatKind.Shot or BeatKind.Cross or BeatKind.Clearance or BeatKind.Header or BeatKind.Save))
        {
            return;
        }

        var attacking = beat.Side;
        var defending = MatchInputV1.OpponentOf(attacking);
        var focus = beat.To;

        if (cross >= 0)
        {
            BuildDeliverySpecs(beat, beats[cross]);
        }
        else
        {
            var counter = CounterTuning.For(_context.InstructionsOf(attacking));

            BuildSpecs(beat, IsTransition(beats, index, counter) ? counter : null, attacking, defending, focus);
        }

        // Each takes a place for the whole beat — a press closes down the point the ball is going to, and an option
        // stands off it — so that a player runs straight there instead of chasing a ball that moves faster than he
        // does; and keeps the role while the play stays near him, so that two players do not swap every pass.
        for (var slot = 0; slot < RoleSlots; slot++)
        {
            var spec = _specs[slot];
            var holder = _roles[slot];

            var keep = spec.Wants != RoleWants.None
                && holder >= 0
                && _roster.IsOccupied(holder)
                && FilmRoster.SideOf(holder) == spec.Side
                && _tasks[holder].Kind == TaskKind.None
                && Eligible(holder, spec, focus, relaxed: true)
                && new Vec(_px[holder], _py[holder]).DistanceTo(spec.Point) <= RoleRadius;

            if (!keep)
            {
                _roles[slot] = -1;
            }
        }

        for (var slot = 0; slot < RoleSlots; slot++)
        {
            var spec = _specs[slot];

            if (spec.Wants == RoleWants.None || _roles[slot] >= 0)
            {
                continue;
            }

            var found = Choose(spec, focus, relaxed: false);

            _roles[slot] = found >= 0 || !spec.Relax ? found : Choose(spec, focus, relaxed: true);
        }

        for (var slot = 0; slot < RoleSlots; slot++)
        {
            if (_roles[slot] >= 0)
            {
                _tasks[_roles[slot]] = new Task(_specs[slot].Run ? TaskKind.Press : TaskKind.Support, _specs[slot].Point, slot);
            }
        }
    }

    /// <summary>
    /// Gets the index of the cross, in open play, that a beat is the run-up to, the flight of, or the finish after, or -1
    /// (`replay-v6`). Crosses from a corner or a free kick are set pieces, which arrange their own box.
    /// </summary>
    internal static int CrossOf(IReadOnlyList<FilmBeat> beats, int index)
    {
        var beat = beats[index];

        if (beat.IsHold || beat.Formation != FormationMode.Open)
        {
            return -1;
        }

        if (beat.Kind == BeatKind.Cross)
        {
            return index;
        }

        if (beat.Kind is BeatKind.Carry or BeatKind.Pass or BeatKind.LoftedPass or BeatKind.Duel)
        {
            for (var next = index + 1; next < beats.Count && next <= index + DeliveryLead; next++)
            {
                var other = beats[next];

                if (other.Cut || other.IsHold || other.Possession != beat.Possession || other.Formation != FormationMode.Open)
                {
                    break;
                }

                if (other.Kind == BeatKind.Cross)
                {
                    return next;
                }

                if (other.Kind is BeatKind.Shot or BeatKind.Clearance or BeatKind.Header or BeatKind.Save)
                {
                    break;
                }
            }

            return -1;
        }

        for (var back = index - 1; back >= 0 && back >= index - DeliveryAftermath; back--)
        {
            var other = beats[back];

            if (beats[back + 1].Cut || other.IsHold || other.Possession != beat.Possession)
            {
                break;
            }

            if (other.Kind == BeatKind.Cross)
            {
                return other.Formation == FormationMode.Open ? back : -1;
            }

            if (other.Kind is not (BeatKind.Header or BeatKind.Shot or BeatKind.Clearance or BeatKind.Save))
            {
                break;
            }
        }

        return -1;
    }

    /// <summary>
    /// Writes the roles for a cross coming in: runners at the near post, the far post and the penalty spot and a
    /// cutback player at the edge of the box, each with a defender goal-side of him, two defenders holding zones, the
    /// far-side wide player left high, and — while the ball is still being worked down the flank — one challenger.
    /// </summary>
    private void BuildDeliverySpecs(FilmBeat beat, FilmBeat cross)
    {
        Array.Fill(_specs, default);

        var attacking = cross.Side;
        var defending = MatchInputV1.OpponentOf(attacking);
        var ballSide = cross.From.Y >= FilmSpace.Width / 2 ? 1.0 : -1.0;
        var box = FilmShape.SetBox(attacking, ballSide);

        if (beat.Kind is BeatKind.Carry or BeatKind.Pass or BeatKind.LoftedPass or BeatKind.Duel)
        {
            var toGoal = (FilmSpace.OwnGoal(defending) - beat.To).Unit();

            _specs[0] = new Spec(defending, RoleWants.Any, FilmSpace.Clamp(beat.To + (toGoal * 1.5), 1.5), true, true, true);
        }

        _specs[3] = new Spec(attacking, RoleWants.FrontLine, box.NearPost, false, false, true);
        _specs[4] = new Spec(attacking, RoleWants.Any, box.FarPost, false, false, true);
        _specs[5] = new Spec(attacking, RoleWants.Any, box.PenaltySpot, false, false, true);
        _specs[6] = new Spec(attacking, RoleWants.Any, box.Cutback, false, false, true);
        _specs[7] = new Spec(attacking, RoleWants.Any, Place(attacking, FilmSpace.Length - 24.0, (FilmSpace.Width / 2) - (ballSide * 22.0)), false, false, true);

        Vec[] defenders = [box.NearMark, box.SpotMark, box.FarMark, box.SixYardZone, box.BoxZone, box.CutbackMark];

        for (var index = 0; index < defenders.Length; index++)
        {
            _specs[BoxDefenderSlot + index] = new Spec(defending, RoleWants.Any, defenders[index], true, true, true);
        }
    }

    /// <summary>Writes the roles the beat wants and where each stands.</summary>
    private void BuildSpecs(FilmBeat beat, CounterTuning? counter, MatchSide attacking, MatchSide defending, Vec focus)
    {
        Array.Fill(_specs, default);

        var d = FilmSpace.Direction(attacking);
        var ballDepth = FilmSpace.Attacking(focus, attacking);
        var ownDepth = FilmSpace.Attacking(focus, defending);
        var finalThird = ownDepth < FinalThird;
        var toGoal = (FilmSpace.OwnGoal(defending) - focus).Unit();

        // The defenders: nobody chases a counter from the halfway line; they drop, and the shape closes them up.
        var challenge = beat.Kind != BeatKind.Placement && (counter is not { DefendersDrop: true } || finalThird || beat.Kind == BeatKind.Duel);
        var second = challenge
            && (finalThird || _context.InstructionsOf(defending).Pressing == MatchPressing.HighPress);

        // In a duel the defender the beat names is the one on the ball: he is the challenger, and nobody else is sent to it.
        var named = beat.Kind == BeatKind.Duel && beat.Opponent is Guid opponent && _context.SideOf(opponent) == defending;

        if (challenge)
        {
            if (!named)
            {
                _specs[0] = new Spec(defending, RoleWants.Any, FilmSpace.Clamp(focus + (toGoal * 1.5), 1.5), true, true, true);
            }

            _specs[2] = new Spec(defending, RoleWants.SameLine, FilmSpace.Clamp(focus + (toGoal * CoverDistance), 1.5), true, true);
        }

        if (second)
        {
            // The second cuts the ball's way in from the side the pitch is wider on.
            var inside = focus.Y > FilmSpace.Width / 2 ? -1.0 : 1.0;

            _specs[1] = new Spec(defending, RoleWants.Any, FilmSpace.Clamp(focus + (toGoal * 5.2) + new Vec(0, inside * 2.5), 1.5), true, true, true);
        }

        // The side with the ball.
        var lateral = focus.Y - (FilmSpace.Width / 2);
        var flank = Math.Abs(lateral) >= FlankLane;
        var outward = lateral >= 0 ? 1.0 : -1.0;
        var open = -outward;

        // The options: a wide one, a forward one and a way back, each a long pass from the ball.
        _specs[3] = new Spec(attacking, RoleWants.Any, FilmSpace.Clamp(focus + new Vec(d * 3.0, open * 13.0), 1.5), false, false);
        _specs[4] = new Spec(attacking, RoleWants.Any, FilmSpace.Clamp(focus + new Vec(d * 14.0, -open * 4.0), 1.5), false, false);
        _specs[5] = new Spec(attacking, RoleWants.Any, FilmSpace.Clamp(focus + new Vec(-d * 9.0, -open * 8.0), 1.5), false, false);

        if (counter is not null)
        {
            // Forwards hold high as outlets; the midfield runs the lanes behind them; the other side recovers. A small
            // counter keeps one of each and the usual options for the rest.
            var outletDepth = Math.Min(FilmSpace.Length - 18.0, ballDepth + counter.OutletAhead);

            _specs[4] = new Spec(attacking, RoleWants.FrontLine, Place(attacking, outletDepth, (FilmSpace.Width / 2) + (open * 14.0)), false, false);
            _specs[3] = new Spec(attacking, RoleWants.Any, FilmSpace.Clamp(focus + new Vec(d * counter.RunnerAhead, open * 12.0), 1.5), false, false);

            if (counter.Outlets >= 2)
            {
                _specs[7] = new Spec(attacking, RoleWants.FrontLine, Place(attacking, outletDepth, (FilmSpace.Width / 2) - (open * 14.0)), false, false);
            }

            if (counter.Runners >= 2)
            {
                _specs[5] = new Spec(attacking, RoleWants.Any, FilmSpace.Clamp(focus + new Vec(d * (counter.RunnerAhead * 0.7), -open * 14.0), 1.5), false, false);
            }

            return;
        }

        var wingAttack = flank && ballDepth >= WingAttackDepth && beat.Kind is BeatKind.Carry or BeatKind.Pass or BeatKind.LoftedPass or BeatKind.Duel;

        if (wingAttack)
        {
            // The ball-side partner overlaps, the striker pins the back line, a midfielder trails at the edge of the
            // box for the cutback, and the far-side wide player stays high.
            _specs[3] = new Spec(attacking, RoleWants.Any, FilmSpace.Clamp(focus + new Vec(d * 10.0, outward * 4.0), 1.5), false, false);
            _specs[4] = new Spec(attacking, RoleWants.FrontLine, Place(attacking, PinDepth(defending), (FilmSpace.Width / 2) + (open * 4.0)), false, false);
            _specs[5] = new Spec(attacking, RoleWants.Any, FilmSpace.Clamp(focus + new Vec(-d * 9.0, open * 8.0), 1.5), false, false);
            _specs[6] = new Spec(attacking, RoleWants.Any, Place(attacking, Math.Min(FilmSpace.Length - EdgeOfBox, ballDepth - 6.0), (FilmSpace.Width / 2) + (outward * 4.0)), false, false);
            _specs[7] = new Spec(attacking, RoleWants.Any, Place(attacking, Math.Min(FilmSpace.Length - 14.0, Math.Max(ballDepth + 4.0, 60.0)), (FilmSpace.Width / 2) + (open * 22.0)), false, false);

            return;
        }
    }

    /// <summary>How deep from his own goal the back line of a side stands now, which is where an attacker can stand and be onside.</summary>
    private double PinDepth(MatchSide defending)
    {
        var attacking = MatchInputV1.OpponentOf(defending);
        var deepest = FilmSpace.Length / 2;

        for (var slot = 1; slot <= 11; slot++)
        {
            var entity = FilmRoster.Index(defending, slot);

            if (!_roster.IsOccupied(entity) || _context.Slots[entity].Family == MatchPositionFamily.Goalkeeper)
            {
                continue;
            }

            deepest = Math.Min(deepest, FilmSpace.Attacking(new Vec(_px[entity], _py[entity]), defending));
        }

        // Metres from the attackers' own goal: the whole pitch less the line, and a step off it.
        return Math.Min(FilmSpace.Length - deepest - 0.8, FilmSpace.Length - 6.0);
    }

    /// <summary>Gets the point a side's player stands at: so far from its own goal line, and so far from the touchline.</summary>
    private static Vec Place(MatchSide side, double depth, double across) =>
        FilmSpace.Clamp(new Vec(side == MatchSide.Home ? depth : FilmSpace.Length - depth, across), 1.5);

    /// <summary>
    /// Whether the beat is part of the first moves of a counter-attack the engine played, while the ball is still on its
    /// way up the pitch.
    /// </summary>
    internal static bool IsTransition(IReadOnlyList<FilmBeat> beats, int index, CounterTuning counter)
    {
        var beat = beats[index];

        if (!beat.Counter || beat.Kind is not (BeatKind.Pass or BeatKind.LoftedPass or BeatKind.Carry or BeatKind.Duel))
        {
            return false;
        }

        var first = index;

        while (first > 0 && !beats[first].Cut && beats[first - 1].Possession == beat.Possession)
        {
            first--;
        }

        return index - first <= counter.BreakBeats && FilmSpace.Attacking(beat.To, beat.Side) <= FilmSpace.Length - 20.0;
    }

    /// <summary>Gets the free outfield player of a side who best fills a role, or -1.</summary>
    private int Choose(Spec spec, Vec focus, bool relaxed)
    {
        var best = -1;
        var bestDistance = double.MaxValue;

        for (var slot = 1; slot <= 11; slot++)
        {
            var entity = FilmRoster.Index(spec.Side, slot);

            if (!_roster.IsOccupied(entity)
                || _tasks[entity].Kind != TaskKind.None
                || _context.Slots[entity].Family == MatchPositionFamily.Goalkeeper
                || Array.IndexOf(_roles, entity) >= 0
                || !Eligible(entity, spec, focus, relaxed))
            {
                continue;
            }

            var distance = new Vec(_px[entity], _py[entity]).DistanceTo(spec.Point);

            if (distance < bestDistance - 1e-9)
            {
                bestDistance = distance;
                best = entity;
            }
        }

        return best;
    }

    /// <summary>
    /// Whether a player can take a role: a back-line player stays in his line unless the ball is in his own third (or,
    /// for the side defending, half), the forwards' roles go to the front line, and the cover comes from the
    /// challenger's line.
    /// </summary>
    private bool Eligible(int entity, Spec spec, Vec focus, bool relaxed)
    {
        var inPossession = !spec.Defending;
        var count = _shape.LineCountOf(spec.Side, inPossession);
        var line = _shape.LineOf(entity, inPossession);

        if (count >= 2)
        {
            var ownDepth = FilmSpace.Attacking(focus, spec.Side);
            var leaves = spec.Defending ? ownDepth < FilmSpace.Length / 2 : ownDepth < FinalThird;

            if (line == 0 && !leaves && (!relaxed || !spec.Defending))
            {
                return false;
            }

            if (spec.Wants == RoleWants.FrontLine && line != count - 1)
            {
                return false;
            }
        }

        if (spec.Wants == RoleWants.SameLine && !relaxed && _roles[0] >= 0)
        {
            return line == _shape.LineOf(_roles[0], inPossession);
        }

        return true;
    }

    /// <summary>A goalkeeper facing a strike dives at it: to the save if he makes one, towards it if he does not.</summary>
    private void AssignDive(FilmBeat beat)
    {
        if (beat.Kind != BeatKind.Shot || beat.Strike is StrikeResult.None or StrikeResult.Blocked or StrikeResult.OffTarget)
        {
            return;
        }

        if (beat.Opponent is not Guid keeperId)
        {
            return;
        }

        var keeper = _roster.EntityOf(keeperId);

        if (keeper < 0 || _tasks[keeper].Kind == TaskKind.Pin)
        {
            return;
        }

        var position = new Vec(_px[keeper], _py[keeper]);
        var toward = beat.To - position;
        var reach = Math.Min(DiveReach, toward.Length);

        _tasks[keeper] = new Task(TaskKind.Dive, position + (toward.Unit() * reach), 0);
    }

    private bool IsKeeperDive(FilmBeat beat, int entity) =>
        beat.Kind == BeatKind.Shot
        && beat.Strike != StrikeResult.None
        && _context.Slots[entity].Family == MatchPositionFamily.Goalkeeper;

    // ---- Stepping ---------------------------------------------------------------------------------------------

    private void Steps(FilmBeat beat, double duration, double start, FilmMotionResult result)
    {
        var steps = Math.Max(1, (int)Math.Ceiling((duration / _options.StepSeconds) - 1e-9));
        var dt = duration / steps;
        var blend = 1.0 - Math.Exp(-dt / FocusSeconds);

        var state = StateFor(beat);
        var ballStart = _ball;
        var zStart = _ballZ;
        var carrier = CarrierOf(beat);
        var carryDirection = (beat.To - ballStart).Unit();

        // A driven ball is the driver's once he is at it. If he is not yet — he was late to it — the ball waits where
        // it is for him, rather than being dragged across the pitch to him.
        var attached = carrier >= 0 && new Vec(_px[carrier], _py[carrier]).DistanceTo(ballStart) <= AttachDistance;
        var carryOffset = attached ? ballStart - new Vec(_px[carrier], _py[carrier]) : Vec.Zero;
        var attachedAt = 0.0;

        for (var step = 1; step <= steps; step++)
        {
            var tau = step * dt;

            // A set piece's pack stands in its places and drifts while the hold lasts, and is in them at the strike (`replay-v13`).
            var current = beat.IsHold && state.Mode is FormationMode.Corner or FormationMode.FreeKickShot or FormationMode.FreeKickCross
                ? state with { Waited = tau / duration }
                : state;

            _shape.Fill(_roster, current, _focus, _targets);

            for (var entity = 0; entity < FilmRoster.Size; entity++)
            {
                if (_roster.IsOccupied(entity))
                {
                    Steer(beat, entity, tau, dt);
                }
            }

            if (carrier >= 0)
            {
                var driver = new Vec(_px[carrier], _py[carrier]);

                if (!attached && driver.DistanceTo(_ball) <= AttachDistance)
                {
                    attached = true;
                    attachedAt = tau;
                    carryOffset = _ball - driver;
                }

                if (attached)
                {
                    var settle = Smooth(Math.Min(1.0, (tau - attachedAt) / SettleSeconds));
                    var offset = carryOffset.Lerp(carryDirection * CarryLead, settle);

                    _ball = driver + offset;
                }

                _ballZ = 0;
            }
            else
            {
                var u = tau / duration;

                _ball = BallAt(beat, ballStart, u);
                _ballZ = AltitudeAt(beat, zStart, u);
            }

            _focus = _focus.Lerp(_ball, blend);

            result.Add(start + tau, _px, _py, _ball.X, _ball.Y, _ballZ);
        }
    }

    private ShapeState StateFor(FilmBeat beat)
    {
        switch (beat.Formation)
        {
            case FormationMode.Celebration:
                if (beat.IsHold)
                {
                    _celebration = CelebrationSpot(beat);
                }

                return new ShapeState(
                    FormationMode.Celebration,
                    beat.Side,
                    _celebration,
                    beat.Actor is Guid scorer ? _roster.EntityOf(scorer) : -1);

            case FormationMode.Corner:
                // The side is the one taking it, whoever plays the beat: the defence puts the ball behind, and may win the header.
                // The runners wait in their places for the hold, and are in them for the delivery and what comes of it.
                return new ShapeState(
                    FormationMode.Corner,
                    _setPieceSide,
                    _setPieceAnchor,
                    Taker: _setPieceTaker,
                    Seed: beat.Possession);

            case FormationMode.FreeKickCross:
                // Like a corner: the pack waits in its places for the hold, and is in them for the delivery.
                return new ShapeState(
                    FormationMode.FreeKickCross,
                    _setPieceSide,
                    _setPieceAnchor,
                    Taker: _setPieceTaker,
                    Seed: beat.Possession);

            case FormationMode.FreeKickShot:
            case FormationMode.Penalty:
            case FormationMode.GoalKick:
                return new ShapeState(beat.Formation, _setPieceSide, _setPieceAnchor, Taker: _setPieceTaker, Seed: beat.Possession);

            case FormationMode.KickOff:
                return new ShapeState(FormationMode.KickOff, beat.Side, beat.To);

            default:
                // A free kick taken quickly leaves the shape as it is, with the nearest defenders stepping off the ball.
                return IsQuickFreeKick(beat)
                    ? new ShapeState(FormationMode.FreeKickQuick, beat.Side, beat.To, Taker: beat.Actor is Guid quick ? _roster.EntityOf(quick) : -1)
                    : new ShapeState(FormationMode.Open, beat.Side, beat.To);
        }
    }

    private static bool IsQuickFreeKick(FilmBeat beat) =>
        beat.IsHold && beat.Hold == HoldKind.FreeKick && beat.Formation == FormationMode.Open;

    /// <summary>Whether a beat is played with a set piece's pack taking up its places (`replay-v13`).</summary>
    private static bool IsPack(FilmBeat beat) =>
        beat.Formation is FormationMode.Corner or FormationMode.FreeKickShot or FormationMode.FreeKickCross;

    /// <summary>Whether a beat is played with the players taking up a set piece: they have a place to be, and run to it.</summary>
    private static bool IsSetPiece(FilmBeat beat) =>
        beat.Formation is FormationMode.Corner or FormationMode.FreeKickShot or FormationMode.FreeKickCross or FormationMode.Penalty or FormationMode.GoalKick
        || IsQuickFreeKick(beat);

    /// <summary>Gets where the scorer runs to: a few metres off the goal line, towards the nearer touchline.</summary>
    private static Vec CelebrationSpot(FilmBeat beat)
    {
        var towardsTop = beat.To.Y < FilmSpace.Width / 2;
        var along = beat.Side == MatchSide.Home ? FilmSpace.Length - 9.0 : 9.0;

        return new Vec(along, towardsTop ? 6.0 : FilmSpace.Width - 6.0);
    }

    /// <summary>Gets the entity driving the ball for the beat, or -1 when the ball follows a path of its own.</summary>
    private int CarrierOf(FilmBeat beat)
    {
        if (beat.Kind is not (BeatKind.Carry or BeatKind.Duel) || beat.Receiver != beat.Actor)
        {
            return -1;
        }

        return beat.Receiver is Guid id ? _roster.EntityOf(id) : -1;
    }

    private void Steer(FilmBeat beat, int entity, double tau, double dt)
    {
        var task = _tasks[entity];
        var position = new Vec(_px[entity], _py[entity]);
        var velocity = new Vec(_vx[entity], _vy[entity]);

        Vec target;
        var cap = _options.ShapeMetresPerSecond;
        var accel = _options.AccelerationMetresPerSecondSquared;
        var stopping = false;
        var deadline = double.NaN;

        switch (task.Kind)
        {
            case TaskKind.Pin:
                target = task.Point;
                cap = _options.SprintMetresPerSecond;
                deadline = task.Value - tau;
                stopping = true;
                break;

            case TaskKind.Carry:
                target = task.Point;
                cap = _options.SprintMetresPerSecond;
                deadline = task.Value - tau;
                break;

            case TaskKind.Press:
                target = task.Point;
                cap = _options.SprintMetresPerSecond;
                break;

            case TaskKind.Support:
                target = task.Point;
                cap = _options.ShapeMetresPerSecond * 1.15;
                break;

            case TaskKind.Dive:
                target = task.Point;
                cap = _options.DiveMetresPerSecond;
                accel = DiveAcceleration;
                break;

            default:
                {
                    // A player holds his place until the shape has moved far enough from him to be worth the walk.
                    // He then walks to where it is, in a straight line, and stops: runs and rests, not a drift after
                    // every small shift of the block. It is also what a viewer expects of a player off the ball.
                    var wanted = ClearOfRoles(entity, _targets[entity]);
                    var here = new Vec(_px[entity], _py[entity]);

                    // At a set piece everybody has a place to be, and goes to it; the runners run.
                    var setPiece = IsSetPiece(beat);
                    var setOff = setPiece ? CornerSetOff : SetOffDistance;
                    var relatch = setPiece ? CornerSetOff : RelatchDistance;

                    // The keeper follows the ball across his goal in small steps, and is back on his line when it comes:
                    // he does not wait for the shape to be eight metres from him (`replay-v9`).
                    if (_context.Slots[entity].Family == MatchPositionFamily.Goalkeeper && !setPiece)
                    {
                        setOff = KeeperSetOff;
                        relatch = KeeperSetOff;
                    }

                    if (setPiece)
                    {
                        // The hold is short, and the players may have a long way to go.
                        cap = _options.SprintMetresPerSecond;
                    }

                    if (_walking[entity])
                    {
                        // The destination is kept unless the shape has run off somewhere else altogether.
                        if (_destinations[entity].DistanceTo(wanted) > relatch)
                        {
                            _destinations[entity] = wanted;
                        }

                        _walking[entity] = here.DistanceTo(_destinations[entity]) > SettleDistance;
                    }
                    else if (here.DistanceTo(wanted) > setOff)
                    {
                        _walking[entity] = true;
                        _destinations[entity] = wanted;
                    }

                    target = _walking[entity] ? _destinations[entity] : here;
                    break;
                }
        }

        // Whoever the beat is not about stays off the ball: he comes in only when he is due on it.
        if (_clear[entity] && task.Kind != TaskKind.Dive && !(task.Kind == TaskKind.Pin && deadline <= ComeInSeconds))
        {
            target = KeepClear(beat, target, position);
        }

        // A keeper going for a save dives, too.
        if (task.Kind == TaskKind.Pin && IsKeeperDive(beat, entity))
        {
            cap = _options.DiveMetresPerSecond;
            accel = DiveAcceleration;
        }

        if (task.Kind is TaskKind.None or TaskKind.Press or TaskKind.Support)
        {
            target += Elbow(entity, position);
        }

        var toTarget = target - position;
        var distance = toTarget.Length;
        double speed;

        if (!double.IsNaN(deadline))
        {
            // Arrive in time: the speed that covers what is left in what is left, no faster than he can run.
            var needed = distance / Math.Max(deadline - Margin, 0.1);

            speed = Math.Min(cap, needed);

            if (stopping)
            {
                // And be able to stop there: never faster than he could brake from.
                speed = Math.Min(speed, Math.Sqrt(2.0 * accel * Math.Max(0.0, distance - 0.1)));
            }
        }
        else
        {
            // Close in smoothly on a target with no deadline, rather than overshooting it. A pack taking up a set piece is in a
            // hurry and runs until it has to brake, which is later than the smooth close-in would have it (`replay-v13`).
            speed = Math.Min(cap, IsPack(beat) && task.Kind == TaskKind.None ? Math.Sqrt(2.0 * accel * distance) * PackBraking : distance * 1.5);
        }

        var desired = toTarget.Unit() * speed;
        var change = desired - velocity;
        var limit = accel * dt;
        var length = change.Length;

        if (length > limit)
        {
            change *= limit / length;
        }

        velocity += change;

        // Whatever the steering asked, nobody ever exceeds the cap.
        var top = velocity.Length;

        if (top > cap)
        {
            velocity *= cap / top;
        }

        _vx[entity] = velocity.X;
        _vy[entity] = velocity.Y;

        var moved = position + (velocity * dt);

        _px[entity] = moved.X;
        _py[entity] = moved.Y;
    }

    // ---- The ball ---------------------------------------------------------------------------------------------

    private static Vec BallAt(FilmBeat beat, Vec start, double u)
    {
        if (beat.IsHold || beat.Kind is BeatKind.Header or BeatKind.Save)
        {
            return start;
        }

        var progress = beat.Kind == BeatKind.Pass ? u * (1.4 - (0.4 * u)) : u;

        return start.Lerp(beat.To, progress);
    }

    private static double AltitudeAt(FilmBeat beat, double start, double u)
    {
        var end = beat.IsHold ? start : beat.ZTo;

        return Math.Max(0.0, start + ((end - start) * u) + (4.0 * beat.ZArc * u * (1.0 - u)));
    }

    private static double Smooth(double t) => t * t * (3.0 - (2.0 * t));

    /// <summary>Who a role takes, among the players who are free.</summary>
    private enum RoleWants
    {
        /// <summary>The role is not wanted for the beat.</summary>
        None = 0,

        /// <summary>Whoever is nearest the place.</summary>
        Any = 1,

        /// <summary>Whoever is nearest, from the line the challenger stands in.</summary>
        SameLine = 2,

        /// <summary>Whoever is nearest, from the front line.</summary>
        FrontLine = 3,
    }

    /// <summary>One role for a beat.</summary>
    /// <param name="Side">The side whose player takes the role.</param>
    /// <param name="Wants">Who takes it.</param>
    /// <param name="Point">Where he stands.</param>
    /// <param name="Relax">Whether the role goes to somebody from the wrong line rather than to nobody.</param>
    /// <param name="Defending">Whether the side is the one without the ball.</param>
    /// <param name="Run">Whether he sprints there rather than jogs.</param>
    private readonly record struct Spec(MatchSide Side, RoleWants Wants, Vec Point, bool Relax, bool Defending, bool Run = false);

    private enum TaskKind
    {
        None = 0,
        Pin = 1,
        Carry = 2,
        Press = 3,
        Support = 4,
        Dive = 5,
    }

    /// <summary>What one player is doing for the beat.</summary>
    /// <param name="Kind">The kind of task; none means the shape.</param>
    /// <param name="Point">Where he is going.</param>
    /// <param name="Value">A deadline in seconds for a pin, or an index for a press or support role.</param>
    private readonly record struct Task(TaskKind Kind, Vec Point, double Value);
}
