using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>
/// One entity of a tick recording: one of the twenty-two players or the ball (`tick-engine-v1`, Milestone 8).
/// </summary>
/// <remarks>
/// The entity is the identity the replay synthesizer builds the film's entities from: a stable index, the side,
/// the participant the player is, and the facts a viewer needs to draw and label the dot. Entity 0…10 are the
/// home side, 11…21 the away side, and <see cref="TickMatchRecording.BallEntityIndex"/> is the ball.
/// </remarks>
/// <param name="Index">The entity's index in every frame and touch of the recording.</param>
/// <param name="IsBall">Whether the entity is the ball rather than a player.</param>
/// <param name="Side">The side the entity belongs to; the home side for the ball.</param>
/// <param name="ParticipantId">The player's identity, or empty for the ball.</param>
/// <param name="ShirtNumber">The shirt number, zero for the ball.</param>
/// <param name="Family">The position family the player occupies; the goalkeeper family for the ball.</param>
/// <param name="DisplayName">The name commentary uses, empty for the ball.</param>
internal sealed record TickRecordingEntity(
    int Index,
    bool IsBall,
    MatchSide Side,
    Guid ParticipantId,
    int ShirtNumber,
    MatchPositionFamily Family,
    string DisplayName);

/// <summary>
/// Where one engine event sits on the tick clock: the event and the tick it was emitted on (`tick-engine-v1`,
/// Milestone 8).
/// </summary>
/// <remarks>
/// The event itself lives on the match state's own log, in sequence order; the stamp is the tick, because the
/// event's minute and second are too coarse to place a commentary line beside a keyframe. The pair is what the
/// replay synthesizer merges the film clock and the match log with.
/// </remarks>
/// <param name="Tick">The tick the event was emitted on, 0-based.</param>
/// <param name="Sequence">The event's sequence number on the match log.</param>
internal readonly record struct TickEventStamp(int Tick, int Sequence);

/// <summary>
/// A dead ball being set up, on the tick clock (`tick-engine-v1`, Milestone 8).
/// </summary>
/// <remarks>
/// One stamp per setup picture: the tick the hold began, the phase, whose restart it is, and where the ball is
/// placed. The replay synthesizer reads the stamps to cut the film at kick-offs and half-time and to slice the
/// continuous match into passages around the dead-ball phases.
/// </remarks>
/// <param name="Tick">The tick the setup hold began on, 0-based.</param>
/// <param name="Phase">The phase that is being set up: a pending restart, a celebration, or half-time.</param>
/// <param name="Side">The side that takes the restart.</param>
/// <param name="BallX">Where the ball is placed, in pitch units.</param>
/// <param name="BallY">Where the ball is placed, in pitch units.</param>
internal readonly record struct TickRestartStamp(int Tick, TickMatchPhase Phase, MatchSide Side, int BallX, int BallY);

/// <summary>
/// One player's touch of the ball, on the tick clock (`tick-engine-v1`, Milestone 8).
/// </summary>
/// <remarks>
/// The action vocabulary is <see cref="PassageAction"/>'s, which is the replay's own: a keyframe a renderer styles
/// and a commentary line synchronizes against. The touch is where the semantic action tags on the film's keyframes
/// come from — everything else in a track is interpolation between them.
/// </remarks>
/// <param name="Tick">The tick the touch happened on, 0-based.</param>
/// <param name="EntityIndex">The entity that touched the ball.</param>
/// <param name="Action">What the player did with it.</param>
internal readonly record struct TickTouch(int Tick, int EntityIndex, PassageAction Action);

/// <summary>
/// The continuous 10 Hz trace of one match: every entity's position every tick, and the moments that are facts
/// (`tick-engine-v1`, Milestone 8).
/// </summary>
/// <remarks>
/// <para>
/// The trace the tick engine produces for the replay synthesizer: 90-odd minutes of positions at 10 samples a
/// second, plus the events, dead balls and touches that give those positions meaning. Positions are the engine's
/// own coordinates (X 0…10,000, Y 0…7,000, Z 0…100) as they stood at the end of each tick, held flat in one array
/// per axis so a whole match is three allocations rather than one per frame.
/// </para>
/// <para>
/// The recording is a by-product of the simulation in the sense the passage recorder is: the loop writes it when
/// one is attached, the match result does not depend on it, and a match run with and without one is identical
/// tick for tick. It is not part of the canonical output and never appears in a hash.
/// </para>
/// </remarks>
internal sealed class TickMatchRecording
{
    /// <summary>The number of entities every frame carries: eleven a side and the ball.</summary>
    public const int EntityCount = 23;

    /// <summary>The index of the ball among the entities.</summary>
    public const int BallEntityIndex = 22;

    private readonly int[] _x;
    private readonly int[] _y;
    private readonly int[] _z;
    private readonly int[] _second;
    private readonly int[] _period;

    /// <summary>Initializes a recording from its parts.</summary>
    /// <param name="entities">The entities, in index order.</param>
    /// <param name="tickCount">How many ticks were recorded.</param>
    /// <param name="x">The flat X positions, <c>[tick * entityCount + entity]</c>.</param>
    /// <param name="y">The flat Y positions.</param>
    /// <param name="z">The flat Z positions.</param>
    /// <param name="second">The match clock second at each tick.</param>
    /// <param name="period">The half at each tick: 1 or 2.</param>
    /// <param name="events">The event stamps.</param>
    /// <param name="restarts">The dead-ball stamps.</param>
    /// <param name="touches">The touches.</param>
    internal TickMatchRecording(
        IReadOnlyList<TickRecordingEntity> entities,
        int tickCount,
        int[] x,
        int[] y,
        int[] z,
        int[] second,
        int[] period,
        IReadOnlyList<TickEventStamp> events,
        IReadOnlyList<TickRestartStamp> restarts,
        IReadOnlyList<TickTouch> touches)
    {
        Entities = entities;
        TickCount = tickCount;
        _x = x;
        _y = y;
        _z = z;
        _second = second;
        _period = period;
        Events = events;
        Restarts = restarts;
        Touches = touches;
    }

    /// <summary>Gets the entities, in index order.</summary>
    public IReadOnlyList<TickRecordingEntity> Entities { get; }

    /// <summary>Gets how many ticks were recorded.</summary>
    public int TickCount { get; }

    /// <summary>Gets the event stamps, in the order the events were emitted.</summary>
    public IReadOnlyList<TickEventStamp> Events { get; }

    /// <summary>Gets the dead-ball setup stamps, in order.</summary>
    public IReadOnlyList<TickRestartStamp> Restarts { get; }

    /// <summary>Gets the touches, in the order they happened.</summary>
    public IReadOnlyList<TickTouch> Touches { get; }

    /// <summary>Gets an entity's X position at a tick, in pitch units.</summary>
    /// <param name="tick">The tick, 0-based.</param>
    /// <param name="entity">The entity's index.</param>
    public int XAt(int tick, int entity) => _x[(tick * EntityCount) + entity];

    /// <summary>Gets an entity's Y position at a tick, in pitch units.</summary>
    /// <param name="tick">The tick, 0-based.</param>
    /// <param name="entity">The entity's index.</param>
    public int YAt(int tick, int entity) => _y[(tick * EntityCount) + entity];

    /// <summary>Gets an entity's Z position (height) at a tick.</summary>
    /// <param name="tick">The tick, 0-based.</param>
    /// <param name="entity">The entity's index.</param>
    public int ZAt(int tick, int entity) => _z[(tick * EntityCount) + entity];

    /// <summary>Gets the match clock second at a tick. The second is the half's own clock, which restarts at 45:00.</summary>
    /// <param name="tick">The tick, 0-based.</param>
    public int SecondAt(int tick) => _second[tick];

    /// <summary>Gets the half at a tick: 1 or 2.</summary>
    /// <param name="tick">The tick, 0-based.</param>
    public int PeriodAt(int tick) => _period[tick];
}

/// <summary>
/// Builds a <see cref="TickMatchRecording"/> while a match runs (`tick-engine-v1`, Milestone 8).
/// </summary>
/// <remarks>
/// The tick loop appends one frame per tick and stamps the facts as they happen; the finished recording is
/// immutable. Buffers grow by doubling, so a match of any length costs a handful of allocations rather than one
/// per tick. The recorder reads nothing the simulation mutates and changes no behaviour, so attaching one cannot
/// move a result.
/// </remarks>
internal sealed class TickMatchRecorder
{
    private const int InitialTicks = 16_384;

    private readonly List<TickRecordingEntity> _entities = [];
    private readonly List<TickEventStamp> _events = [];
    private readonly List<TickRestartStamp> _restarts = [];
    private readonly List<TickTouch> _touches = [];

    private int[] _x = new int[InitialTicks * TickMatchRecording.EntityCount];
    private int[] _y = new int[InitialTicks * TickMatchRecording.EntityCount];
    private int[] _z = new int[InitialTicks * TickMatchRecording.EntityCount];
    private int[] _second = new int[InitialTicks];
    private int[] _period = new int[InitialTicks];
    private int _tickCount;

    /// <summary>Gets how many frames have been recorded so far.</summary>
    public int TickCount => _tickCount;

    /// <summary>Registers one entity. Called for all twenty-three before the first frame, in index order.</summary>
    /// <param name="entity">The entity.</param>
    public void AddEntity(TickRecordingEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        _entities.Add(entity);
    }

    /// <summary>Appends one tick's frame: every entity's position as it stood at the end of the tick.</summary>
    /// <param name="x">The X positions, one per entity.</param>
    /// <param name="y">The Y positions, one per entity.</param>
    /// <param name="z">The Z positions, one per entity.</param>
    /// <param name="second">The match clock second at the tick.</param>
    /// <param name="period">The half at the tick: 1 or 2.</param>
    public void AddFrame(ReadOnlySpan<int> x, ReadOnlySpan<int> y, ReadOnlySpan<int> z, int second, int period)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(x.Length, TickMatchRecording.EntityCount);
        ArgumentOutOfRangeException.ThrowIfNotEqual(y.Length, TickMatchRecording.EntityCount);
        ArgumentOutOfRangeException.ThrowIfNotEqual(z.Length, TickMatchRecording.EntityCount);

        if ((_tickCount + 1) * TickMatchRecording.EntityCount > _x.Length)
        {
            var grown = _x.Length * 2;

            Array.Resize(ref _x, grown);
            Array.Resize(ref _y, grown);
            Array.Resize(ref _z, grown);
            Array.Resize(ref _second, _second.Length * 2);
            Array.Resize(ref _period, _period.Length * 2);
        }

        var start = _tickCount * TickMatchRecording.EntityCount;

        x.CopyTo(_x.AsSpan(start));
        y.CopyTo(_y.AsSpan(start));
        z.CopyTo(_z.AsSpan(start));
        _second[_tickCount] = second;
        _period[_tickCount] = period;
        _tickCount++;
    }

    /// <summary>Stamps an engine event with the tick it was emitted on.</summary>
    /// <param name="tick">The tick, 0-based.</param>
    /// <param name="sequence">The event's sequence number.</param>
    public void AddEvent(int tick, int sequence) => _events.Add(new TickEventStamp(tick, sequence));

    /// <summary>Stamps a dead-ball setup with the tick its hold began on.</summary>
    /// <param name="tick">The tick, 0-based.</param>
    /// <param name="phase">The phase being set up.</param>
    /// <param name="side">The side that takes the restart.</param>
    /// <param name="ballX">Where the ball is placed, in pitch units.</param>
    /// <param name="ballY">Where the ball is placed, in pitch units.</param>
    public void AddRestart(int tick, TickMatchPhase phase, MatchSide side, int ballX, int ballY) =>
        _restarts.Add(new TickRestartStamp(tick, phase, side, ballX, ballY));

    /// <summary>Records a player's touch of the ball.</summary>
    /// <param name="tick">The tick, 0-based.</param>
    /// <param name="entityIndex">The entity that touched it.</param>
    /// <param name="action">What the player did with it.</param>
    public void AddTouch(int tick, int entityIndex, PassageAction action) =>
        _touches.Add(new TickTouch(tick, entityIndex, action));

    /// <summary>Builds the immutable recording.</summary>
    public TickMatchRecording Build() =>
        new(_entities, _tickCount, _x, _y, _z, _second, _period, _events, _restarts, _touches);
}
