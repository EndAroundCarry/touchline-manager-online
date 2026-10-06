using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Model;

/// <summary>
/// How the ball travelled between two waypoints of a passage (`engine-v4`).
/// </summary>
/// <remarks>
/// The kind is what lets the replay tell a ground pass from a cross or a strike: a pass has no altitude, a
/// cross and a header arc above the pitch, and a shot is a quick flat delivery. It is a display fact rather
/// than an outcome, so it never appears in the canonical output hash.
/// </remarks>
public enum PassageWaypointKind
{
    /// <summary>The carrier ran with the ball.</summary>
    Carry = 0,

    /// <summary>The ball was passed along the ground.</summary>
    Pass = 1,

    /// <summary>The ball was crossed into the box.</summary>
    Cross = 2,

    /// <summary>The ball was struck at goal.</summary>
    Shot = 3,

    /// <summary>The ball was cleared out of danger.</summary>
    Clearance = 4,

    /// <summary>
    /// The ball was placed for a dead-ball restart: a kick-off, a goal kick, a free kick, a penalty, or a
    /// corner (`engine-v5`).
    /// </summary>
    /// <remarks>A placement is not a movement of the ball, so a replay holds the play and sets the ball down.</remarks>
    Restart = 5,
}

/// <summary>
/// How a possession began, when it began with a dead ball (`engine-v5`).
/// </summary>
/// <remarks>
/// A restart belongs to somebody: the rules decide which side takes it and from where, instead of the next
/// possession being drawn afresh (`MAT-12`). The kind is a display fact — it never appears in the canonical
/// output hash — and is what lets the replay hold the play and place the ball.
/// </remarks>
public enum PassageRestartKind
{
    /// <summary>The possession began from play: the ball was loose or already under control.</summary>
    None = 0,

    /// <summary>A kick-off from the centre spot: the start of a half, or after a goal.</summary>
    KickOff = 1,

    /// <summary>A goal kick, after a shot went out of play.</summary>
    GoalKick = 2,

    /// <summary>The goalkeeper's ball, after a save or a missed penalty the keeper stopped.</summary>
    KeeperBall = 3,

    /// <summary>A free kick for a foul with no shot, or for an offside, taken from where it was given.</summary>
    FreeKick = 4,
}

/// <summary>
/// How a possession ended (`engine-v5`).
/// </summary>
/// <remarks>
/// What the replay reads to know what kind of passage it is turning into a film: the events say what happened
/// to a shot, and the outcome says what the possession was. Like every recorder field it is a display fact and
/// never appears in the canonical output hash.
/// </remarks>
public enum PassageOutcome
{
    /// <summary>The contested loose ball that opened the possession was lost.</summary>
    ScrambleLost = 0,

    /// <summary>The attack could not be progressed and the ball was turned over.</summary>
    ProgressionFailed = 1,

    /// <summary>The attack was stopped by an offside.</summary>
    Offside = 2,

    /// <summary>The attack reached the final third but created nothing, and the ball was cleared.</summary>
    CreationFailed = 3,

    /// <summary>A foul by the defending side stopped play and the free kick was taken quickly, with no shot.</summary>
    Foul = 4,

    /// <summary>A foul in the box gave a penalty.</summary>
    Penalty = 5,

    /// <summary>A foul gave a direct free kick that was struck at goal.</summary>
    FreeKickStruck = 6,

    /// <summary>A foul gave a free kick that was delivered into the box instead of struck.</summary>
    FreeKickCrossed = 7,

    /// <summary>A corner that came to nothing: the delivery was cleared, or the header was lost.</summary>
    CornerCleared = 8,

    /// <summary>A corner that produced a header at goal.</summary>
    CornerHeaded = 9,

    /// <summary>An attack that created a shot from open play, whatever the shot became.</summary>
    OpenPlayShot = 10,
}

/// <summary>
/// What a player did to the ball at a touch (`engine-v4`).
/// </summary>
/// <remarks>
/// The replay's keyframe action vocabulary. Free text on a keyframe would let two builds disagree about how
/// an action is spelled; an enum with <see cref="PassageVocabulary.Code(PassageAction)"/> fixes the spelling
/// once. The values are the display vocabulary only — nothing here is stored on a match result.
/// </remarks>
public enum PassageAction
{
    /// <summary>An off-the-ball run.</summary>
    Run = 0,

    /// <summary>Carried the ball.</summary>
    Carry = 1,

    /// <summary>Played a pass.</summary>
    Pass = 2,

    /// <summary>Received a pass.</summary>
    Receive = 3,

    /// <summary>Delivered a cross.</summary>
    Cross = 4,

    /// <summary>Won a header.</summary>
    Header = 5,

    /// <summary>Made a tackle.</summary>
    Tackle = 6,

    /// <summary>Intercepted the ball.</summary>
    Interception = 7,

    /// <summary>Took a shot.</summary>
    Shot = 8,

    /// <summary>Took a penalty.</summary>
    Penalty = 9,

    /// <summary>Took a direct free kick.</summary>
    FreeKick = 10,

    /// <summary>Made a save.</summary>
    Save = 11,

    /// <summary>Dived for the ball.</summary>
    Dive = 12,

    /// <summary>Celebrated a goal.</summary>
    Celebrate = 13,
}

/// <summary>Stable codes for the passage vocabulary, so a replay and a renderer agree on a spelling.</summary>
public static class PassageVocabulary
{
    /// <summary>Converts a waypoint kind to its stable code.</summary>
    /// <param name="kind">The kind.</param>
    public static string Code(this PassageWaypointKind kind) => kind switch
    {
        PassageWaypointKind.Carry => "carry",
        PassageWaypointKind.Pass => "pass",
        PassageWaypointKind.Cross => "cross",
        PassageWaypointKind.Shot => "shot",
        PassageWaypointKind.Clearance => "clearance",
        PassageWaypointKind.Restart => "restart",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown waypoint kind."),
    };

    /// <summary>Converts an action to its stable code.</summary>
    /// <param name="action">The action.</param>
    public static string Code(this PassageAction action) => action switch
    {
        PassageAction.Run => "run",
        PassageAction.Carry => "carry",
        PassageAction.Pass => "pass",
        PassageAction.Receive => "receive",
        PassageAction.Cross => "cross",
        PassageAction.Header => "header",
        PassageAction.Tackle => "tackle",
        PassageAction.Interception => "interception",
        PassageAction.Shot => "shot",
        PassageAction.Penalty => "penalty",
        PassageAction.FreeKick => "free_kick",
        PassageAction.Save => "save",
        PassageAction.Dive => "dive",
        PassageAction.Celebrate => "celebrate",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown action."),
    };
}

/// <summary>
/// One point the ball passed through during a possession (`engine-v4`).
/// </summary>
/// <remarks>
/// The waypoint is placed on the normalized pitch, with altitude zero for a ground touch and lifted for a
/// cross or a strike. The fraction is the point's position inside the possession as a whole, so a replay can
/// map the passage's match seconds onto playback milliseconds without knowing how the possession was drawn.
/// </remarks>
/// <param name="FractionBasisPoints">Where in the possession the touch happened, 0…10,000.</param>
/// <param name="X">The position across the pitch, 0…10,000.</param>
/// <param name="Y">The position down the pitch, 0…7,000.</param>
/// <param name="Z">The ball's altitude, 0…100.</param>
/// <param name="Kind">How the ball travelled to this point.</param>
public readonly record struct PassageWaypointV1(
    int FractionBasisPoints,
    int X,
    int Y,
    int Z,
    PassageWaypointKind Kind);

/// <summary>
/// One player's touch of the ball during a possession (`engine-v4`).
/// </summary>
/// <remarks>
/// The participant is the player the simulation actually picked for the action — the carrier, the passer, the
/// shooter, the keeper, the header winner — so the replay can name who did what rather than inventing a
/// participant. The point is where the ball was at the touch, and the action is the vocabulary code a
/// renderer dispatches on.
/// </remarks>
/// <param name="FractionBasisPoints">Where in the possession the touch happened, 0…10,000.</param>
/// <param name="ParticipantId">The player who touched the ball.</param>
/// <param name="Action">What they did with it.</param>
/// <param name="X">The position across the pitch, 0…10,000.</param>
/// <param name="Y">The position down the pitch, 0…7,000.</param>
/// <param name="Z">The ball's altitude at the touch, 0…100.</param>
public readonly record struct PassageTouchV1(
    int FractionBasisPoints,
    Guid ParticipantId,
    PassageAction Action,
    int X,
    int Y,
    int Z);

/// <summary>
/// Where an event sits among the facts of a possession (`engine-v5`).
/// </summary>
/// <remarks>
/// An event is positioned by the item it follows: the same fraction as the waypoint or touch recorded last
/// when the event was emitted. A reader that merges waypoints, touches and events by fraction therefore puts
/// an event after the item it shares a fraction with.
/// </remarks>
/// <param name="Sequence">The event's sequence number.</param>
/// <param name="FractionBasisPoints">Where in the possession the event happened, 0…10,000.</param>
public readonly record struct PassageEventV1(int Sequence, int FractionBasisPoints);

/// <summary>
/// One possession as the replay reads it: where the ball started and ended, how it got there, and which
/// events it produced (`engine-v4`, completed in `engine-v5`).
/// </summary>
/// <remarks>
/// <para>
/// This is the side-channel record that lets the replay director build a continuous film. The result still
/// carries only the events and player lines; a passage is a by-product of the same run, captured by an
/// optional sink, so it can never change the outcome and never appears in the output hash.
/// </para>
/// <para>
/// A possession that produced no event still has a passage, because the film needs the ball to move through
/// it: the away side's midfield recycling from kick-off is part of the match even when nothing happened.
/// </para>
/// <para>
/// Since `engine-v5` the record is complete: a possession's start and end are real moments of the match clock
/// (the possessions tile each half), and the period, the way it ended, the restart it began with, and the
/// position of each event among the ball's waypoints and the touches are all recorded. Match seconds restart
/// at the second half, so <see cref="Period"/> is what orders two possessions across half-time.
/// </para>
/// </remarks>
public sealed record MatchPassageV1
{
    /// <summary>Gets the 1-based ordinal of the possession within the match.</summary>
    public required int Ordinal { get; init; }

    /// <summary>Gets the side in possession.</summary>
    public required MatchSide Side { get; init; }

    /// <summary>Gets the half the possession was played in: 1 or 2 (`engine-v5`).</summary>
    public required int Period { get; init; }

    /// <summary>Gets the match second the possession started at, read before the clock advanced.</summary>
    public required int StartClockSeconds { get; init; }

    /// <summary>Gets the match second the possession ended at, which is always after it started.</summary>
    public required int EndClockSeconds { get; init; }

    /// <summary>Gets how the possession ended (`engine-v5`).</summary>
    public required PassageOutcome Outcome { get; init; }

    /// <summary>Gets the dead-ball restart the possession began with, or none when it began from play (`engine-v5`).</summary>
    public required PassageRestartKind Restart { get; init; }

    /// <summary>
    /// Gets whether the possession was a counter-attack: the ball was won back from play and the side broke on it
    /// (`engine-v11`).
    /// </summary>
    public bool Counter { get; init; }

    /// <summary>
    /// Gets the events the possession produced, in order, each positioned among the waypoints and touches;
    /// empty when it produced none (`engine-v5`).
    /// </summary>
    public required IReadOnlyList<PassageEventV1> Events { get; init; }

    /// <summary>Gets the event sequences the possession produced, in order; empty when it produced none.</summary>
    public IReadOnlyList<int> EventSequences => [.. Events.Select(matchEvent => matchEvent.Sequence)];

    /// <summary>Gets the ball's waypoints, in order, beginning at the possession's start point.</summary>
    public required IReadOnlyList<PassageWaypointV1> Waypoints { get; init; }

    /// <summary>Gets the players' touches, in order.</summary>
    public required IReadOnlyList<PassageTouchV1> Touches { get; init; }

    /// <summary>Gets where the possession started, or the pitch centre when it captured no waypoint.</summary>
    public SpatialPoint StartPoint => Waypoints.Count == 0 ? SpatialPoint.Center : new SpatialPoint(Waypoints[0].X, Waypoints[0].Y);

    /// <summary>Gets where the possession ended, or the pitch centre when it captured no waypoint.</summary>
    public SpatialPoint EndPoint =>
        Waypoints.Count == 0 ? SpatialPoint.Center : new SpatialPoint(Waypoints[^1].X, Waypoints[^1].Y);

    /// <summary>Gets where up the attacking side's own scale the possession ended.</summary>
    public int AttackingEndX =>
        Side == MatchSide.Home ? EndPoint.X : SpatialPitch.PitchLength - EndPoint.X;
}

/// <summary>
/// Captures the ball's path and players' touches for each possession (`engine-v4`).
/// </summary>
/// <remarks>
/// <para>
/// A recorder rather than a field on the result, deliberately and for the same reason
/// <see cref="PlayerLiveMetricsRecorder"/> is one: the result is the engine's hashed output, and a
/// replay-only fact on it would either change the hash of every match ever played or sit outside the hash and
/// go unaudited. Handing the simulation a sink leaves the result byte for byte what it was — capturing
/// consumes no draw and changes no state — while a replay read re-simulates the same frozen input and
/// recovers the same film.
/// </para>
/// <para>
/// The engine appends; the caller owns the instance and reads <see cref="Passages"/> when the match is over.
/// Each replay read gets its own recorder, so two viewers cannot share one match's capture.
/// </para>
/// </remarks>
public sealed class MatchPassageRecorder
{
    private readonly List<MatchPassageV1> _passages = [];

    /// <summary>Gets every captured possession, in the order they were played.</summary>
    public IReadOnlyList<MatchPassageV1> Passages => _passages;

    /// <summary>Gets how many possessions have been captured.</summary>
    internal int Count => _passages.Count;

    /// <summary>Appends one completed possession.</summary>
    /// <param name="passage">The passage.</param>
    internal void Add(MatchPassageV1 passage) => _passages.Add(passage);
}
