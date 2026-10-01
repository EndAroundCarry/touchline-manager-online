using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Highlights;

/// <summary>One entity in a highlight: a player or the ball.</summary>
/// <remarks>
/// Identity and anchor position only. Where an entity *goes* is a track, so a stationary player costs two
/// keyframes rather than a frame per animation tick — which is the whole reason the presentation is semantic
/// rather than a video (ADR-0006, master plan §9.1).
/// </remarks>
public sealed record HighlightEntityV1
{
    /// <summary>Gets the stable entity identifier, which tracks refer to.</summary>
    public required string EntityId { get; init; }

    /// <summary>Gets whether this entity is the ball rather than a player.</summary>
    public required bool IsBall { get; init; }

    /// <summary>Gets which side the entity belongs to, absent for the ball.</summary>
    public MatchSide? Side { get; init; }

    /// <summary>Gets the participant's identity, absent for the ball.</summary>
    public Guid? ParticipantId { get; init; }

    /// <summary>Gets the shirt number, zero for the ball.</summary>
    public int ShirtNumber { get; init; }

    /// <summary>Gets the position family the player occupies, absent for the ball.</summary>
    public MatchPositionFamily? Family { get; init; }

    /// <summary>Gets the entity's resting position across the pitch, 0…10_000.</summary>
    public required int X { get; init; }

    /// <summary>Gets the entity's resting position down the pitch, 0…10_000.</summary>
    public required int Y { get; init; }

    /// <summary>Gets the display name of the player, absent for the ball.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the position of the player (e.g. GK, DC, MC, ST), absent for the ball.</summary>
    public string? Position { get; init; }
}

/// <summary>One position at one moment, normalized to the pitch.</summary>
/// <param name="TimeMilliseconds">Milliseconds from the start of the highlight.</param>
/// <param name="X">Position across the pitch, 0…10,000.</param>
/// <param name="Y">Position down the pitch, 0…10,000.</param>
/// <param name="Z">Altitude / ball height 0…100 (0 = on pitch, 100 = aerial).</param>
/// <param name="Speed">Movement speed normalized in units/sec.</param>
/// <param name="Action">Optional action or duel tag (e.g., tackle, pass, shot, save, header).</param>
public sealed record HighlightKeyframeV1(
    int TimeMilliseconds,
    int X,
    int Y,
    int Z = 0,
    int Speed = 0,
    string? Action = null);

/// <summary>One entity's movement through a highlight, as keyframes the client interpolates between.</summary>
/// <param name="EntityId">The entity.</param>
/// <param name="Keyframes">Its positions, ordered by time.</param>
public sealed record HighlightTrackV1(string EntityId, IReadOnlyList<HighlightKeyframeV1> Keyframes);

/// <summary>
/// The recycling passage between two highlights, keyed to the highlight it leads into (`replay-v2`).
/// </summary>
/// <remarks>
/// A condensed replay that cuts from one chance to the next teleports the ball back to the halfway line
/// between highlights, and a viewer reads that as a fake match. A bridge carries the entities' movement
/// between one highlight's ending and the next's beginning: the ball travelling back, the teams shifting
/// with it. It is optional — a bridge that does not fit the budget is dropped and the cut remains — and
/// the client plays it at speed, which is what condenses ninety minutes into the plan's five to ten minutes.
/// </remarks>
/// <param name="AfterEventSequence">The highlight this bridge leads into.</param>
/// <param name="DurationMilliseconds">How long the bridge runs for.</param>
/// <param name="Tracks">One track per entity, ordered by entity identifier.</param>
public sealed record BridgeV1(
    int AfterEventSequence,
    int DurationMilliseconds,
    IReadOnlyList<HighlightTrackV1> Tracks)
{
    /// <summary>
    /// Gets an estimate of the serialized payload, on the same accounting the highlights use.
    /// </summary>
    public int EstimatedPayloadBytes => (DurationMilliseconds / 1_000) + (Tracks.Sum(track => track.Keyframes.Count) * 24) + 32;
}

/// <summary>One player's full match participation line for the match center lineups.</summary>
public sealed record MatchLineupPlayerV1
{
    public required Guid ParticipantId { get; init; }
    public required Guid PlayerId { get; init; }
    public required int ShirtNumber { get; init; }
    public required string Name { get; init; }
    public required string Position { get; init; }
    public required MatchPositionFamily Family { get; init; }
    public required bool IsStarter { get; init; }
    public required int SlotNumber { get; init; }
    public required int KickoffCondition { get; init; }
    public required int FinalCondition { get; init; }
    public required int FinalRating { get; init; }
    public required int Goals { get; init; }
    public required int Assists { get; init; }
    public required int YellowCards { get; init; }
    public required bool SentOff { get; init; }
    public int? SubbedOutMinute { get; init; }
    public int? SubbedInMinute { get; init; }
    public bool IsInjured { get; init; }
}

/// <summary>One team's lineup and tactical setup for the match center.</summary>
public sealed record MatchLineupV1
{
    public required string ClubName { get; init; }
    public required string ShortName { get; init; }
    public required string PrimaryColour { get; init; }
    public required string SecondaryColour { get; init; }
    public required string Formation { get; init; }
    public required IReadOnlyList<MatchLineupPlayerV1> Starters { get; init; }
    public required IReadOnlyList<MatchLineupPlayerV1> Bench { get; init; }
}

/// <summary>A player's live condition and rating at a specific minute in the match.</summary>
public sealed record PlayerLiveMetricV1
{
    public required Guid ParticipantId { get; init; }
    public required int Minute { get; init; }
    public required int ConditionBasisPoints { get; init; }
    public required int RatingBasisPoints { get; init; }
}

/// <summary>
/// One immutable, replayable highlight (master plan §9.3, `replay-v2`).
/// </summary>
/// <remarks>
/// Carries no raw frames and no video: the client interpolates between keyframes at its own refresh rate, so
/// the payload is a few kilobytes instead of a few megabytes and a replay is identical on a 60 Hz and a
/// 144 Hz display. The narration is derived from the event, and the colours come from safe generated
/// palettes rather than from any real club's identity (`WORLD-3`). Since `replay-v2` a highlight is a whole
/// passage of play — ten to twenty-five seconds — whose entities move as the play model moved them, and the
/// ball's keyframes carry the altitude the flight model gave them.
/// </remarks>
public sealed record HighlightPresentationV1
{
    /// <summary>Gets the presentation version, so a client can refuse a shape it does not know.</summary>
    public required string PresentationVersion { get; init; }

    /// <summary>Gets the sequence number of the event this presents.</summary>
    public required int SourceEventSequence { get; init; }

    /// <summary>Gets the match minute.</summary>
    public required int Minute { get; init; }

    /// <summary>Gets the stoppage minute, or zero in regulation.</summary>
    public required int StoppageMinute { get; init; }

    /// <summary>Gets how long the highlight runs for, in milliseconds.</summary>
    public required int DurationMilliseconds { get; init; }

    /// <summary>Gets the outcome as a stable code, which a client keys its styling off.</summary>
    public required string OutcomeCode { get; init; }

    /// <summary>Gets the narration for accessibility, so the Canvas is not the only way to follow it.</summary>
    public required string Narration { get; init; }

    /// <summary>Gets the home side's colour.</summary>
    public required string HomeColour { get; init; }

    /// <summary>Gets the away side's colour.</summary>
    public required string AwayColour { get; init; }

    /// <summary>Gets every entity, ordered by identifier.</summary>
    public required IReadOnlyList<HighlightEntityV1> Entities { get; init; }

    /// <summary>Gets every track, ordered by entity identifier.</summary>
    public required IReadOnlyList<HighlightTrackV1> Tracks { get; init; }

    /// <summary>
    /// Gets an estimate of the serialized payload, for the instrumentation the payload budget requires.
    /// </summary>
    /// <remarks>
    /// An estimate rather than a serialization, because the engine has no serializer and should not acquire
    /// one. It counts the numbers a keyframe carries and the fields an entity does, which is enough to
    /// enforce a budget and to notice a presentation that has become pathological.
    /// </remarks>
    public int EstimatedPayloadBytes
    {
        get
        {
            var keyframes = Tracks.Sum(track => track.Keyframes.Count);

            return (Entities.Count * 48) + (keyframes * 24) + (Narration.Length * 2) + 128;
        }
    }
}

/// <summary>The whole presentation of a match: its highlights, in event order.</summary>
public sealed record MatchPresentationV1
{
    /// <summary>Gets the presentation version.</summary>
    public required string PresentationVersion { get; init; }

    /// <summary>Gets the engine version that produced the match.</summary>
    public required string EngineVersion { get; init; }

    /// <summary>Gets the home side's goals.</summary>
    public required int HomeGoals { get; init; }

    /// <summary>Gets the away side's goals.</summary>
    public required int AwayGoals { get; init; }

    /// <summary>Gets the highlights, in event order.</summary>
    public required IReadOnlyList<HighlightPresentationV1> Highlights { get; init; }

    /// <summary>Gets the bridges between consecutive highlights, keyed to the highlight each leads into.</summary>
    public IReadOnlyList<BridgeV1> Bridges { get; init; } = [];

    /// <summary>Gets the home side's lineup and player stats.</summary>
    public MatchLineupV1? HomeLineup { get; init; }

    /// <summary>Gets the away side's lineup and player stats.</summary>
    public MatchLineupV1? AwayLineup { get; init; }

    /// <summary>Gets the live minute-by-minute condition and ratings for all players.</summary>
    public IReadOnlyList<PlayerLiveMetricV1>? LiveMetrics { get; init; }

    /// <summary>Gets the estimated total payload, bridges included.</summary>
    public int EstimatedPayloadBytes =>
        Highlights.Sum(highlight => highlight.EstimatedPayloadBytes)
        + Bridges.Sum(bridge => bridge.EstimatedPayloadBytes);
}

/// <summary>
/// How much is worth showing, and how much may be sent (`replay-v2`).
/// </summary>
/// <remarks>
/// The caps are policy rather than formulas, but they are versioned with the presentation so a stored
/// payload can always be explained. They exist because "show every shot" produces a payload nobody wants to
/// download on a phone (`match_presentation_payload_budget_kb`). Stage 3 widens a highlight from a single
/// moment to a whole passage of play, so the duration band and the bridge budget are part of the options.
/// </remarks>
public sealed record HighlightOptionsV1
{
    /// <summary>Gets the least goal probability that makes a shot worth replaying, in basis points.</summary>
    public int MinQualityForShotBasisPoints { get; init; } = 1_600;

    /// <summary>Gets whether shots that hit the woodwork are worth showing.</summary>
    public bool IncludeWoodwork { get; init; } = true;

    /// <summary>Gets whether direct free kicks are worth showing, at any quality (`replay-v2`).</summary>
    public bool IncludeFreeKicks { get; init; } = true;

    /// <summary>Gets the most highlights one match may carry, goals excepted.</summary>
    public int MaxHighlights { get; init; } = 24;

    /// <summary>Gets the estimated payload budget for one match, in bytes (750 KB, ADR-0006).</summary>
    public int PayloadBudgetBytes { get; init; } = 750 * 1024;

    /// <summary>Gets the shortest a highlight runs for, in milliseconds.</summary>
    /// <remarks>
    /// A passage of play, not a moment: the plan's Stage 3 asks highlights to represent ten to twenty-five
    /// seconds of football.
    /// </remarks>
    public int MinDurationMilliseconds { get; init; } = 10_000;

    /// <summary>Gets the longest a highlight runs for, in milliseconds.</summary>
    public int MaxDurationMilliseconds { get; init; } = 25_000;

    /// <summary>Gets the most an inter-highlight bridge may add to the payload, in bytes.</summary>
    /// <remarks>
    /// Bridges are the recycling passages between chances; they are worth having only while they fit the
    /// same budget the highlights were sized against.
    /// </remarks>
    public int BridgeBudgetBytes { get; init; } = 160 * 1024;

    /// <summary>Gets the shortest a bridge runs for, in milliseconds.</summary>
    public int MinBridgeDurationMilliseconds { get; init; } = 4_000;

    /// <summary>Gets the longest a bridge runs for, in milliseconds.</summary>
    public int MaxBridgeDurationMilliseconds { get; init; } = 9_000;
}
