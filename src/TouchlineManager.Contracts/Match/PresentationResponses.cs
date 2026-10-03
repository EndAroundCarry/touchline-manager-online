namespace TouchlineManager.Contracts.Match;

/// <summary>One named fact a commentary line was built from (§8.6).</summary>
/// <param name="Name">The parameter's name, which a translation keys off.</param>
/// <param name="Value">The parameter's value.</param>
public sealed record CommentaryParameterResponse(string Name, string Value);

/// <summary>
/// One line of commentary: the template it came from, the facts, and the current English text (§8.6).
/// </summary>
/// <remarks>
/// The template key and parameters are the durable part and the text is a rendering of them, so the same
/// match can be narrated in another language later without re-simulating it. Every parameter is a fact a
/// manager can already see — a name, a club, a minute — and never a hidden attribute (`MAT-11`).
/// </remarks>
/// <param name="Sequence">The sequence number of the event this narrates.</param>
/// <param name="Minute">The match minute.</param>
/// <param name="StoppageMinute">The stoppage minute, or zero in regulation.</param>
/// <param name="Side">Which side the event belongs to: <c>home</c> or <c>away</c>.</param>
/// <param name="TemplateKey">The stable template key.</param>
/// <param name="VariantKey">Which variant of the template was used, so repeated lines can be told apart.</param>
/// <param name="Parameters">The facts the line was built from.</param>
/// <param name="Text">The current English rendering.</param>
public sealed record CommentaryLineResponse(
    int Sequence,
    int Minute,
    int StoppageMinute,
    string Side,
    string TemplateKey,
    string VariantKey,
    IReadOnlyList<CommentaryParameterResponse> Parameters,
    string Text);

/// <summary>
/// One line of commentary pinned to a moment inside a highlight (`replay-v2`).
/// </summary>
/// <remarks>
/// The full match log narrates by the minute; a ten-to-twenty-five-second passage needs better resolution
/// than that, so the offset is milliseconds from the passage's first frame. The keys and parameters are the
/// durable part and the text is a rendering of them, exactly as they are for the match log.
/// </remarks>
/// <param name="TimeMilliseconds">The offset from the passage's first frame, in milliseconds.</param>
/// <param name="TemplateKey">The stable template key.</param>
/// <param name="VariantKey">Which variant of the template was used.</param>
/// <param name="Parameters">The facts the line was built from.</param>
/// <param name="Text">The current English rendering.</param>
public sealed record HighlightCommentaryResponse(
    int TimeMilliseconds,
    string TemplateKey,
    string VariantKey,
    IReadOnlyList<CommentaryParameterResponse> Parameters,
    string Text);

/// <summary>
/// One segment of the film playback clock: a passage (`replay-v3`, `replay-v4`).
/// </summary>
/// <param name="Kind">What the segment is: <c>passage</c>.</param>
/// <param name="SourceEventSequence">The passage's principal event sequence, or zero when it has none.</param>
/// <param name="StartMilliseconds">When the segment starts on the playback clock.</param>
/// <param name="DurationMilliseconds">How long the segment runs for.</param>
public sealed record PlaybackSegmentResponse(
    string Kind,
    int SourceEventSequence,
    int StartMilliseconds,
    int DurationMilliseconds);

/// <summary>
/// One point of a passage's match clock: where the match clock stands at one moment of the film (`replay-v4`).
/// </summary>
/// <remarks>
/// Match seconds are the half's own clock, so they restart at 45:00 in the second half, and the passage's
/// <c>Period</c> says which half they belong to. Between two points the clock runs linearly.
/// </remarks>
/// <param name="TimeMilliseconds">Milliseconds from the start of the passage.</param>
/// <param name="MatchSecond">The match second on the half's own clock.</param>
public sealed record ClockKeyframeResponse(int TimeMilliseconds, int MatchSecond);

/// <summary>
/// A cut in the film: a moment at which the players and the ball are put somewhere new (`replay-v4`).
/// </summary>
/// <remarks>
/// The film moves at bounded speed everywhere except at a cut, which is played as a short crossfade under an
/// overlay. A client never interpolates across one.
/// </remarks>
/// <param name="TimeMilliseconds">Milliseconds from the start of the passage at which the film jumps.</param>
/// <param name="DurationMilliseconds">How long the crossfade lasts.</param>
/// <param name="Kind">Why the film cuts: <c>kick_off</c> or <c>half_time</c>.</param>
public sealed record PassageCutResponse(int TimeMilliseconds, int DurationMilliseconds, string Kind);

/// <summary>One position at one moment, normalized to the pitch.</summary>
/// <param name="TimeMilliseconds">Milliseconds from the start of the highlight.</param>
/// <param name="X">Position across the pitch, 0…10,000.</param>
/// <param name="Y">Position down the pitch, 0…10,000.</param>
/// <param name="Z">Altitude / ball height 0…100 (0 = on pitch, 100 = maximum aerial height).</param>
/// <param name="Speed">Movement speed normalized in units/sec.</param>
/// <param name="Action">Optional action or duel tag (e.g., tackle, pass, shot, save, header).</param>
public sealed record HighlightKeyframeResponse(
    int TimeMilliseconds,
    int X,
    int Y,
    int Z = 0,
    int Speed = 0,
    string? Action = null);

/// <summary>One entity's movement through a highlight, as keyframes the client interpolates between.</summary>
/// <param name="EntityId">The entity the track belongs to.</param>
/// <param name="Keyframes">Its positions, ordered by time.</param>
public sealed record HighlightTrackResponse(string EntityId, IReadOnlyList<HighlightKeyframeResponse> Keyframes);

/// <summary>One entity in a passage: a player or the ball.</summary>
/// <remarks>
/// Identity and anchor position only. Where the entity goes is a track, so a stationary player costs two
/// keyframes rather than a frame per animation tick (master plan §9.1, §9.3).
/// </remarks>
/// <param name="EntityId">The stable entity identifier the tracks refer to.</param>
/// <param name="IsBall">Whether this entity is the ball rather than a player.</param>
/// <param name="Side">The side the entity belongs to, absent for the ball.</param>
/// <param name="ParticipantId">The participant's identity, absent for the ball.</param>
/// <param name="ShirtNumber">The shirt number, zero for the ball.</param>
/// <param name="Family">The position family the player occupies: <c>goalkeeper</c>, <c>defence</c>, <c>midfield</c>, or <c>attack</c>.</param>
/// <param name="X">The entity's resting position across the pitch, 0…10,000.</param>
/// <param name="Y">The entity's resting position down the pitch, 0…10,000.</param>
/// <param name="Name">Display name of the player, absent for the ball.</param>
/// <param name="Position">Abbreviated position of the player (e.g. GK, DC, MC, ST), absent for the ball.</param>
public sealed record HighlightEntityResponse(
    string EntityId,
    bool IsBall,
    string? Side,
    Guid? ParticipantId,
    int ShirtNumber,
    string? Family,
    int X,
    int Y,
    string? Name = null,
    string? Position = null);

/// <summary>One player's full match participation line for the match center lineups.</summary>
public sealed record MatchLineupPlayerResponse(
    Guid ParticipantId,
    Guid PlayerId,
    int ShirtNumber,
    string Name,
    string Position,
    string Family,
    bool IsStarter,
    int SlotNumber,
    int KickoffCondition,
    int FinalCondition,
    int FinalRating,
    int Goals,
    int Assists,
    int YellowCards,
    bool SentOff,
    int? SubbedOutMinute,
    int? SubbedInMinute,
    bool IsInjured);

/// <summary>One team's lineup and tactical setup for the match center.</summary>
public sealed record MatchLineupResponse(
    string ClubName,
    string ShortName,
    string PrimaryColour,
    string SecondaryColour,
    string Formation,
    IReadOnlyList<MatchLineupPlayerResponse> Starters,
    IReadOnlyList<MatchLineupPlayerResponse> Bench);

/// <summary>A player's live condition and rating at a specific minute in the match.</summary>
public sealed record PlayerLiveMetricResponse(
    Guid ParticipantId,
    int Minute,
    int ConditionBasisPoints,
    int RatingBasisPoints);

/// <summary>
/// One film passage: an immutable, replayable slice of a continuous match (`replay-v3`, `replay-v4`).
/// </summary>
/// <remarks>
/// Carries no frames and no video: the client interpolates between keyframes at its own refresh rate, so
/// the payload is a few kilobytes instead of a few megabytes and a replay is identical on a 60 Hz and a
/// 144 Hz display. The narration is what makes the Canvas accessible, and the colours come from safe
/// generated palettes rather than from any real club's identity (`WORLD-3`).
/// </remarks>
/// <param name="SourceEventSequence">The sequence of the passage's principal event, or zero when it has none.</param>
/// <param name="Minute">The match minute the passage begins in.</param>
/// <param name="StoppageMinute">The stoppage minute, or zero in regulation.</param>
/// <param name="StartMatchSecond">The match second the passage begins at.</param>
/// <param name="EndMatchSecond">The match second the passage ends at.</param>
/// <param name="DurationMilliseconds">How long the passage runs for in the film.</param>
/// <param name="OutcomeCode">The outcome as a stable code: <c>goal</c>, <c>saved</c>, <c>play</c>, and so on.</param>
/// <param name="Narration">The narration, so the Canvas is not the only way to follow it.</param>
/// <param name="HomeColour">The home side's colour.</param>
/// <param name="AwayColour">The away side's colour.</param>
/// <param name="EventSequences">The sequences of the events the passage produced, in order.</param>
/// <param name="Entities">Every entity, including the ball.</param>
/// <param name="Tracks">One track per entity, ordered by entity identifier.</param>
/// <param name="Commentary">The passage's synchronized commentary, ordered by offset.</param>
/// <param name="Period">The half the passage is played in: 1 or 2 (`replay-v4`).</param>
/// <param name="Clock">How the match clock runs through the passage, on the half's own clock (`replay-v4`).</param>
/// <param name="Cuts">The cuts inside the passage (`replay-v4`).</param>
public sealed record PassageResponse(
    int SourceEventSequence,
    int Minute,
    int StoppageMinute,
    int StartMatchSecond,
    int EndMatchSecond,
    int DurationMilliseconds,
    string OutcomeCode,
    string Narration,
    string HomeColour,
    string AwayColour,
    IReadOnlyList<int> EventSequences,
    IReadOnlyList<HighlightEntityResponse> Entities,
    IReadOnlyList<HighlightTrackResponse> Tracks,
    IReadOnlyList<HighlightCommentaryResponse>? Commentary = null,
    int Period = 1,
    IReadOnlyList<ClockKeyframeResponse>? Clock = null,
    IReadOnlyList<PassageCutResponse>? Cuts = null);

/// <summary>
/// One clip of the highlights reel: a window of the film to watch, around a chance (`replay-v3`).
/// </summary>
/// <param name="SourceEventSequence">The sequence of the event the clip is built around.</param>
/// <param name="OutcomeCode">The outcome as a stable code: <c>goal</c>, <c>saved</c>, and so on.</param>
/// <param name="Minute">The match minute of the chance.</param>
/// <param name="StoppageMinute">The stoppage minute of the chance, or zero in regulation.</param>
/// <param name="StartMilliseconds">Where the clip starts on the film clock.</param>
/// <param name="EndMilliseconds">Where the clip ends on the film clock.</param>
public sealed record ReelClipResponse(
    int SourceEventSequence,
    string OutcomeCode,
    int Minute,
    int StoppageMinute,
    int StartMilliseconds,
    int EndMilliseconds);

/// <summary>
/// A played match's whole replay: one continuous film, a highlights reel, and the commentary log (§9.5).
/// </summary>
/// <remarks>
/// Immutable once published — the events it is built from are history and the engine is deterministic —
/// which is what lets it carry a strong entity tag and be cached by the service worker. It carries no
/// server instant, unlike the mutable reads: a cached response with a server time on it would be a stale
/// clock dressed as a fresh one (`TIME-5`).
/// </remarks>
/// <param name="MatchId">The match identity.</param>
/// <param name="PresentationVersion">The version of this presentation shape, so a client can refuse one it does not know.</param>
/// <param name="EngineVersion">The engine version that produced the match.</param>
/// <param name="HomeGoals">The home side's goals.</param>
/// <param name="AwayGoals">The away side's goals.</param>
/// <param name="Commentary">One line per narrated event, in event order.</param>
/// <param name="Passages">The film passages, in match order.</param>
/// <param name="Reel">The highlights reel: the selected chance clips, in order (`replay-v3`).</param>
/// <param name="EstimatedPayloadBytes">The estimated serialized size, for the payload budget (§9.3).</param>
/// <param name="HomeLineup">The home side's complete lineup and player performance.</param>
/// <param name="AwayLineup">The away side's complete lineup and player performance.</param>
/// <param name="LiveMetrics">Minute-by-minute condition and ratings for all players.</param>
/// <param name="Playback">The film playback schedule, in the order the segments play (`replay-v3`).</param>
/// <param name="TotalPlaybackMilliseconds">How long the film runs for, in milliseconds.</param>
/// <param name="PaceMilli">
/// The one pace the whole film is played at, in thousandths of real time: 2,200 is 2.2 times the speed it would be
/// run at (`replay-v4`).
/// </param>
public sealed record MatchPresentationResponse(
    Guid MatchId,
    string PresentationVersion,
    string EngineVersion,
    int HomeGoals,
    int AwayGoals,
    IReadOnlyList<CommentaryLineResponse> Commentary,
    IReadOnlyList<PassageResponse> Passages,
    IReadOnlyList<ReelClipResponse> Reel,
    int EstimatedPayloadBytes,
    MatchLineupResponse? HomeLineup = null,
    MatchLineupResponse? AwayLineup = null,
    IReadOnlyList<PlayerLiveMetricResponse>? LiveMetrics = null,
    IReadOnlyList<PlaybackSegmentResponse>? Playback = null,
    int TotalPlaybackMilliseconds = 0,
    int PaceMilli = 0);

