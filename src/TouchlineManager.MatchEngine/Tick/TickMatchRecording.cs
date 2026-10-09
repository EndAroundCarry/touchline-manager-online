using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>What is special about one recorded frame.</summary>
[Flags]
internal enum TickFrameFlags : byte
{
    /// <summary>Nothing: every body and the ball moved at ordinary speed since the frame before.</summary>
    None = 0,

    /// <summary>The ball was put down somewhere new (a restart's spot, the centre spot after a goal), not moved there.</summary>
    BallPlaced = 1,

    /// <summary>The players were put in their places (the kick-off after the interval), not walked there.</summary>
    PlayersPlaced = 2,
}

/// <summary>A player's action at a frame: a tag a film keyframe carries so a renderer and a commentary line can synchronize to it.</summary>
/// <param name="Frame">The frame the action happened at.</param>
/// <param name="Entity">The slot entity (0..10 home, 11..21 away) that acted, or -1 for the ball.</param>
/// <param name="Action">What was done.</param>
internal readonly record struct TickActionStamp(int Frame, int Entity, PassageAction Action);

/// <summary>An event's place in the recording: the frame it was emitted at.</summary>
/// <param name="Frame">The frame.</param>
/// <param name="Sequence">The event's sequence number.</param>
internal readonly record struct TickEventStamp(int Frame, int Sequence);

/// <summary>A change of who stands in a slot: a substitute coming on, or a player leaving for good.</summary>
/// <param name="Frame">The first frame the new occupant is on the pitch for.</param>
/// <param name="Entity">The slot entity (0..10 home, 11..21 away).</param>
/// <param name="Occupant">The new occupant, or <see cref="Guid.Empty"/> when the slot is left empty.</param>
internal readonly record struct TickRosterStamp(int Frame, int Entity, Guid Occupant);

/// <summary>The moment play stopped for a restart, or the match went into a break.</summary>
/// <param name="Frame">The first frame of the stoppage.</param>
/// <param name="State">What the match is doing from here.</param>
/// <param name="Kind">Which restart is being set up, when <paramref name="State"/> is one of the pending restarts.</param>
/// <param name="TakerIsHome">Whether the home side takes the restart.</param>
internal readonly record struct TickStoppageStamp(int Frame, TickPlayState State, TickRestartKind Kind, bool TakerIsHome);

/// <summary>
/// The continuous 10 Hz trace of a tick match: where the 22 slots and the ball were at every tick, and the facts the film is cut
/// and labelled from (`tick-engine-v1`, Milestone 8).
/// </summary>
/// <remarks>
/// <para>
/// This is the whole point of the tick engine for the replay: nothing is reconstructed. The loop writes one frame per simulated tick
/// and the synthesizer reads windows of them, so a film is a view of what the simulation did rather than a second, invented motion.
/// Frames are stored as parallel 16-bit arrays in pitch units (a unit is about a centimetre), 22 × 2 + 3 values a frame, which is
/// about 5.5 MB for a ninety-minute match. A recording exists only when a replay is being derived (the caller supplies a
/// <see cref="MatchPassageRecorder"/>); the loop that fills it reads no draw and changes no state, so the result is identical with
/// and without it.
/// </para>
/// <para>
/// A slot entity is a side's slot number, so a substitution changes who occupies the entity (a <see cref="TickRosterStamp"/>) and
/// not the entity itself, which is how the presentation's <c>H1</c>…<c>A11</c> identifiers have always worked. An entity with no
/// occupant is recorded at X = -1.
/// </para>
/// </remarks>
internal sealed class TickMatchRecording
{
    /// <summary>The slot entities in a match: eleven a side.</summary>
    public const int Entities = 22;

    /// <summary>The X recorded for a slot nobody stands in.</summary>
    public const short Absent = -1;

    private const int FirstCapacity = 8_192;
    private const int Stride = Entities * 2;

    private short[] _players = new short[FirstCapacity * Stride];
    private short[] _ball = new short[FirstCapacity * 3];
    private int[] _clock = new int[FirstCapacity];
    private byte[] _period = new byte[FirstCapacity];
    private byte[] _state = new byte[FirstCapacity];
    private sbyte[] _controller = new sbyte[FirstCapacity];
    private byte[] _flags = new byte[FirstCapacity];
    private int _capacity = FirstCapacity;

    /// <summary>Gets how many frames have been recorded.</summary>
    public int FrameCount { get; private set; }

    /// <summary>Gets the actions players took, in frame order.</summary>
    public List<TickActionStamp> Actions { get; } = [];

    /// <summary>Gets the frame each event was emitted at, in sequence order.</summary>
    public List<TickEventStamp> Events { get; } = [];

    /// <summary>Gets the changes of who stands in a slot, in frame order.</summary>
    public List<TickRosterStamp> Rosters { get; } = [];

    /// <summary>Gets the moments play stopped, in frame order.</summary>
    public List<TickStoppageStamp> Stoppages { get; } = [];

    /// <summary>Gets who stood in each slot at kick-off.</summary>
    public Guid[] Starters { get; } = new Guid[Entities];

    /// <summary>Gets the frame the second half starts at, or -1 while it has not.</summary>
    public int SecondHalfFrame { get; private set; } = -1;

    /// <summary>Starts a frame. Every slot is absent and the ball at the centre until set.</summary>
    /// <param name="clockSecond">The match second on the half's own clock.</param>
    /// <param name="period">The half, 1 or 2.</param>
    /// <param name="state">What the match is doing.</param>
    /// <param name="controller">The slot entity with the ball at his feet, or -1.</param>
    /// <param name="flags">What is special about the frame.</param>
    public void BeginFrame(int clockSecond, int period, TickPlayState state, int controller, TickFrameFlags flags)
    {
        if (FrameCount == _capacity)
        {
            Grow();
        }

        var frame = FrameCount++;

        _clock[frame] = clockSecond;
        _period[frame] = (byte)period;
        _state[frame] = (byte)state;
        _controller[frame] = (sbyte)controller;
        _flags[frame] = (byte)flags;

        if (period == 2 && SecondHalfFrame < 0)
        {
            SecondHalfFrame = frame;
        }

        Array.Fill(_players, Absent, frame * Stride, Stride);
    }

    /// <summary>Sets where a slot entity stands in the frame being recorded.</summary>
    /// <param name="entity">The slot entity.</param>
    /// <param name="x">The X, in pitch units.</param>
    /// <param name="y">The Y, in pitch units.</param>
    public void SetPlayer(int entity, int x, int y)
    {
        var at = ((FrameCount - 1) * Stride) + (entity * 2);

        _players[at] = (short)x;
        _players[at + 1] = (short)y;
    }

    /// <summary>Sets where the ball is in the frame being recorded.</summary>
    /// <param name="x">The X, in pitch units.</param>
    /// <param name="y">The Y, in pitch units.</param>
    /// <param name="z">The height, in Z units.</param>
    public void SetBall(int x, int y, int z)
    {
        var at = (FrameCount - 1) * 3;

        _ball[at] = (short)x;
        _ball[at + 1] = (short)y;
        _ball[at + 2] = (short)z;
    }

    /// <summary>Tags an action on the frame about to be recorded (the tick being simulated is recorded when it ends).</summary>
    /// <param name="entity">The slot entity that acted, or -1 for the ball.</param>
    /// <param name="action">What was done.</param>
    public void AddAction(int entity, PassageAction action) => Actions.Add(new TickActionStamp(FrameCount, entity, action));

    /// <summary>Notes that an event was emitted during the tick about to be recorded.</summary>
    /// <param name="sequence">The event's sequence number.</param>
    public void AddEvent(int sequence) => Events.Add(new TickEventStamp(FrameCount, sequence));

    /// <summary>Notes a change of occupant at the frame about to be recorded.</summary>
    /// <param name="entity">The slot entity.</param>
    /// <param name="occupant">The new occupant, or <see cref="Guid.Empty"/>.</param>
    public void AddRoster(int entity, Guid occupant) => Rosters.Add(new TickRosterStamp(FrameCount, entity, occupant));

    /// <summary>Notes that play stopped at the frame about to be recorded.</summary>
    /// <param name="state">What the match is doing from here.</param>
    /// <param name="kind">Which restart is set up.</param>
    /// <param name="takerIsHome">Whether the home side takes it.</param>
    public void AddStoppage(TickPlayState state, TickRestartKind kind, bool takerIsHome) =>
        Stoppages.Add(new TickStoppageStamp(FrameCount, state, kind, takerIsHome));

    /// <summary>Gets a slot entity's X in a frame, in pitch units, or <see cref="Absent"/>.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="entity">The slot entity.</param>
    public int PlayerX(int frame, int entity) => _players[(frame * Stride) + (entity * 2)];

    /// <summary>Gets a slot entity's Y in a frame, in pitch units.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="entity">The slot entity.</param>
    public int PlayerY(int frame, int entity) => _players[(frame * Stride) + (entity * 2) + 1];

    /// <summary>Gets the ball's X in a frame, in pitch units.</summary>
    /// <param name="frame">The frame.</param>
    public int BallX(int frame) => _ball[frame * 3];

    /// <summary>Gets the ball's Y in a frame, in pitch units.</summary>
    /// <param name="frame">The frame.</param>
    public int BallY(int frame) => _ball[(frame * 3) + 1];

    /// <summary>Gets the ball's height in a frame, in Z units.</summary>
    /// <param name="frame">The frame.</param>
    public int BallZ(int frame) => _ball[(frame * 3) + 2];

    /// <summary>Gets a frame's match second on its half's own clock.</summary>
    /// <param name="frame">The frame.</param>
    public int ClockSecond(int frame) => _clock[frame];

    /// <summary>Gets a frame's half, 1 or 2.</summary>
    /// <param name="frame">The frame.</param>
    public int Period(int frame) => _period[frame];

    /// <summary>Gets what the match was doing in a frame.</summary>
    /// <param name="frame">The frame.</param>
    public TickPlayState State(int frame) => (TickPlayState)_state[frame];

    /// <summary>Gets the slot entity with the ball at his feet in a frame, or -1.</summary>
    /// <param name="frame">The frame.</param>
    public int Controller(int frame) => _controller[frame];

    /// <summary>Gets what is special about a frame.</summary>
    /// <param name="frame">The frame.</param>
    public TickFrameFlags Flags(int frame) => (TickFrameFlags)_flags[frame];

    private void Grow()
    {
        _capacity *= 2;

        Array.Resize(ref _players, _capacity * Stride);
        Array.Resize(ref _ball, _capacity * 3);
        Array.Resize(ref _clock, _capacity);
        Array.Resize(ref _period, _capacity);
        Array.Resize(ref _state, _capacity);
        Array.Resize(ref _controller, _capacity);
        Array.Resize(ref _flags, _capacity);
    }
}
