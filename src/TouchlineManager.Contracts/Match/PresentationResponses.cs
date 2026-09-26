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

/// <summary>One position at one moment, normalized to the pitch.</summary>
/// <param name="TimeMilliseconds">Milliseconds from the start of the highlight.</param>
/// <param name="X">Position across the pitch, 0…10,000.</param>
/// <param name="Y">Position down the pitch, 0…10,000.</param>
public sealed record HighlightKeyframeResponse(int TimeMilliseconds, int X, int Y);

/// <summary>One entity's movement through a highlight, as keyframes the client interpolates between.</summary>
/// <param name="EntityId">The entity the track belongs to.</param>
/// <param name="Keyframes">Its positions, ordered by time.</param>
public sealed record HighlightTrackResponse(string EntityId, IReadOnlyList<HighlightKeyframeResponse> Keyframes);

/// <summary>One entity in a highlight: a player or the ball.</summary>
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
public sealed record HighlightEntityResponse(
    string EntityId,
    bool IsBall,
    string? Side,
    Guid? ParticipantId,
    int ShirtNumber,
    string? Family,
    int X,
    int Y);

/// <summary>
/// One immutable, replayable highlight (master plan §9.3).
/// </summary>
/// <remarks>
/// Carries no frames and no video: the client interpolates between keyframes at its own refresh rate, so
/// the payload is a few kilobytes instead of a few megabytes and a replay is identical on a 60 Hz and a
/// 144 Hz display. The narration is what makes the Canvas accessible, and the colours come from safe
/// generated palettes rather than from any real club's identity (`WORLD-3`).
/// </remarks>
/// <param name="SourceEventSequence">The sequence number of the event this presents.</param>
/// <param name="Minute">The match minute.</param>
/// <param name="StoppageMinute">The stoppage minute, or zero in regulation.</param>
/// <param name="DurationMilliseconds">How long the highlight runs for.</param>
/// <param name="OutcomeCode">The outcome as a stable code: <c>goal</c>, <c>saved</c>, and so on.</param>
/// <param name="Narration">The narration, so the Canvas is not the only way to follow it.</param>
/// <param name="HomeColour">The home side's colour.</param>
/// <param name="AwayColour">The away side's colour.</param>
/// <param name="Entities">Every entity, including the ball.</param>
/// <param name="Tracks">One track per entity, ordered by entity identifier.</param>
public sealed record HighlightResponse(
    int SourceEventSequence,
    int Minute,
    int StoppageMinute,
    int DurationMilliseconds,
    string OutcomeCode,
    string Narration,
    string HomeColour,
    string AwayColour,
    IReadOnlyList<HighlightEntityResponse> Entities,
    IReadOnlyList<HighlightTrackResponse> Tracks);

/// <summary>
/// A played match's whole replay: its commentary timeline and its highlights, in event order (§9.5).
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
/// <param name="Highlights">The highlights, in event order.</param>
/// <param name="EstimatedPayloadBytes">The estimated serialized size, for the payload budget (§9.3).</param>
public sealed record MatchPresentationResponse(
    Guid MatchId,
    string PresentationVersion,
    string EngineVersion,
    int HomeGoals,
    int AwayGoals,
    IReadOnlyList<CommentaryLineResponse> Commentary,
    IReadOnlyList<HighlightResponse> Highlights,
    int EstimatedPayloadBytes);
