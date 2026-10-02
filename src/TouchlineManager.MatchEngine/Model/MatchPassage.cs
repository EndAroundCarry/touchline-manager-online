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
/// One possession as the replay reads it: where the ball started and ended, how it got there, and which
/// events it produced (`engine-v4`).
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
/// </remarks>
public sealed record MatchPassageV1
{
    /// <summary>Gets the 1-based ordinal of the possession within the match.</summary>
    public required int Ordinal { get; init; }

    /// <summary>Gets the side in possession.</summary>
    public required MatchSide Side { get; init; }

    /// <summary>Gets the match second the possession started at.</summary>
    public required int StartClockSeconds { get; init; }

    /// <summary>Gets the match second the possession ended at.</summary>
    public required int EndClockSeconds { get; init; }

    /// <summary>Gets the event sequences the possession produced, in order; empty when it produced none.</summary>
    public required IReadOnlyList<int> EventSequences { get; init; }

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
