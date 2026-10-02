using TouchlineManager.MatchEngine.Commentary;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Highlights;

/// <summary>One entity in a passage: a player or the ball.</summary>
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
/// <param name="TimeMilliseconds">Milliseconds from the start of the passage.</param>
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

/// <summary>One entity's movement through a passage, as keyframes the client interpolates between.</summary>
/// <param name="EntityId">The entity.</param>
/// <param name="Keyframes">Its positions, ordered by time.</param>
public sealed record HighlightTrackV1(string EntityId, IReadOnlyList<HighlightKeyframeV1> Keyframes);

/// <summary>
/// One segment of the film playback clock: a passage (`replay-v3`).
/// </summary>
/// <remarks>
/// The presentation is a sequence of film passages rather than a continuous recording, so a client needs to
/// know where each one sits on its own playback clock. The schedule is that answer: passages run back to back
/// from zero, one segment per passage, and the last segment's end is the film's total length. It is what lets
/// the clock, the commentary feed, and the animation all agree on when something happened without any of them
/// owning the arithmetic.
/// </remarks>
/// <param name="Kind">What the segment is: <c>passage</c>.</param>
/// <param name="SourceEventSequence">The passage's principal event sequence, or zero when it has none.</param>
/// <param name="StartMilliseconds">When the segment starts on the playback clock.</param>
/// <param name="DurationMilliseconds">How long the segment runs for.</param>
public sealed record PlaybackSegmentV1(
    string Kind,
    int SourceEventSequence,
    int StartMilliseconds,
    int DurationMilliseconds);

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

    /// <summary>
    /// Gets an estimate of the serialized payload this line contributes.
    /// </summary>
    /// <remarks>
    /// On the same accounting the passages use: an estimate rather than a serialization, because the
    /// engine has no serializer and should not acquire one, and because the match center's budget is
    /// enforced by the sum rather than by measuring one field.
    /// </remarks>
    public int EstimatedPayloadBytes => 96 + (Name.Length * 2) + (Position.Length * 2);
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

    /// <summary>Gets an estimate of the serialized payload this lineup contributes.</summary>
    public int EstimatedPayloadBytes =>
        128
        + (ClubName.Length * 2)
        + (ShortName.Length * 2)
        + (Formation.Length * 2)
        + Starters.Sum(player => player.EstimatedPayloadBytes)
        + Bench.Sum(player => player.EstimatedPayloadBytes);
}

/// <summary>
/// One film passage: an immutable, replayable slice of a continuous match (`replay-v3`).
/// </summary>
/// <remarks>
/// Carries no raw frames and no video: the client interpolates between keyframes at its own refresh rate, so
/// the payload is a few kilobytes instead of a few megabytes and a replay is identical on a 60 Hz and a
/// 144 Hz display. A passage covers a window of the real match clock, carries the current eleven and the
/// ball, and is played in the film for a length the director's time warp chose. The narration is derived
/// from its events, and the colours come from safe generated palettes rather than from any real club's
/// identity (`WORLD-3`).
/// </remarks>
public sealed record PassageV1
{
    /// <summary>Gets the presentation version, so a client can refuse a shape it does not know.</summary>
    public required string PresentationVersion { get; init; }

    /// <summary>Gets the sequence of the passage's principal event, or zero when it produced none.</summary>
    public required int SourceEventSequence { get; init; }

    /// <summary>Gets the match minute the passage begins in.</summary>
    public required int Minute { get; init; }

    /// <summary>Gets the stoppage minute, or zero in regulation.</summary>
    public required int StoppageMinute { get; init; }

    /// <summary>Gets the match second the passage begins at.</summary>
    public required int StartMatchSecond { get; init; }

    /// <summary>Gets the match second the passage ends at.</summary>
    public required int EndMatchSecond { get; init; }

    /// <summary>Gets how long the passage runs for in the film, in milliseconds.</summary>
    public required int DurationMilliseconds { get; init; }

    /// <summary>Gets the passage's outcome as a stable code a client keys its styling off.</summary>
    public required string OutcomeCode { get; init; }

    /// <summary>Gets the narration for accessibility, so the Canvas is not the only way to follow it.</summary>
    public required string Narration { get; init; }

    /// <summary>Gets the home side's colour.</summary>
    public required string HomeColour { get; init; }

    /// <summary>Gets the away side's colour.</summary>
    public required string AwayColour { get; init; }

    /// <summary>Gets the sequences of the events this passage produced, in order.</summary>
    public required IReadOnlyList<int> EventSequences { get; init; }

    /// <summary>Gets every entity, ordered by identifier.</summary>
    public required IReadOnlyList<HighlightEntityV1> Entities { get; init; }

    /// <summary>Gets every track, ordered by entity identifier.</summary>
    public required IReadOnlyList<HighlightTrackV1> Tracks { get; init; }

    /// <summary>
    /// Gets the passage's synchronized commentary, ordered by its offset in the film passage.
    /// </summary>
    /// <remarks>
    /// The narration says what happened; these tokens say when, in milliseconds from the passage's first
    /// frame, so the commentary feed can append line by line with the action rather than a minute at a time.
    /// The keys and parameters are the durable part, exactly as they are for the full match log.
    /// </remarks>
    public IReadOnlyList<HighlightCommentaryV1> Commentary { get; init; } = [];

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
            var commentary = Commentary.Sum(line => line.EstimatedPayloadBytes);

            return (Entities.Count * 48) + (keyframes * 24) + (Narration.Length * 2) + commentary + 128;
        }
    }
}

/// <summary>
/// One clip of the highlights reel: a window of the film to watch, around a chance (`replay-v3`).
/// </summary>
/// <remarks>
/// The reel is a server-side playlist over the same film: each clip names the chance it is built around and
/// the film window that leads into it, so the client can play the selected chances with a genuine lead-in
/// without a second presentation. Clip windows may overlap and are merged by the client, so two chances a few
/// seconds apart share one continuous stretch rather than replaying the same build-up twice.
/// </remarks>
/// <param name="SourceEventSequence">The sequence of the event the clip is built around.</param>
/// <param name="OutcomeCode">The outcome as a stable code: <c>goal</c>, <c>saved</c>, and so on.</param>
/// <param name="Minute">The match minute of the chance.</param>
/// <param name="StoppageMinute">The stoppage minute of the chance, or zero in regulation.</param>
/// <param name="StartMilliseconds">Where the clip starts on the film clock.</param>
/// <param name="EndMilliseconds">Where the clip ends on the film clock.</param>
public sealed record ReelClipV1(
    int SourceEventSequence,
    string OutcomeCode,
    int Minute,
    int StoppageMinute,
    int StartMilliseconds,
    int EndMilliseconds)
{
    /// <summary>Gets how long the clip runs for, in milliseconds.</summary>
    public int DurationMilliseconds => EndMilliseconds - StartMilliseconds;

    /// <summary>Gets an estimate of the serialized payload this clip contributes.</summary>
    public int EstimatedPayloadBytes => 32 + (OutcomeCode.Length * 2);
}

/// <summary>The whole presentation of a match: one continuous film, and a reel over the same data.</summary>
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

    /// <summary>Gets the film passages, in match order.</summary>
    public required IReadOnlyList<PassageV1> Passages { get; init; }

    /// <summary>Gets the highlights reel: the selected chance clips, in order.</summary>
    public IReadOnlyList<ReelClipV1> Reel { get; init; } = [];

    /// <summary>Gets the home side's lineup and player stats.</summary>
    public MatchLineupV1? HomeLineup { get; init; }

    /// <summary>Gets the away side's lineup and player stats.</summary>
    public MatchLineupV1? AwayLineup { get; init; }

    /// <summary>Gets the live minute-by-minute condition and ratings for all players.</summary>
    public IReadOnlyList<PlayerLiveMetricV1>? LiveMetrics { get; init; }

    /// <summary>
    /// Gets the film playback schedule, in the order the segments play (`replay-v3`).
    /// </summary>
    /// <remarks>
    /// Empty only when there is nothing to watch. One segment per passage, contiguous — each starts where the
    /// previous ended — so the client plays one list rather than deriving the offsets itself.
    /// </remarks>
    public IReadOnlyList<PlaybackSegmentV1> Playback { get; init; } = [];

    /// <summary>Gets how long the film runs for, in milliseconds.</summary>
    public int TotalPlaybackMilliseconds =>
        Playback.Count > 0 ? Playback[^1].StartMilliseconds + Playback[^1].DurationMilliseconds : 0;

    /// <summary>
    /// Gets the estimated total payload: the passages, the reel, the playback schedule, both lineups, and the
    /// live metric curve (`match_presentation_payload_budget_kb`, ADR-0006).
    /// </summary>
    /// <remarks>
    /// Everything the replay ships is counted, because a budget that ignored the lineups and the metrics
    /// would be measuring the film while the client downloaded three times as much.
    /// </remarks>
    public int EstimatedPayloadBytes =>
        Passages.Sum(passage => passage.EstimatedPayloadBytes)
        + Reel.Sum(clip => clip.EstimatedPayloadBytes)
        + (Playback.Count * ScheduleSegmentBytes)
        + (HomeLineup?.EstimatedPayloadBytes ?? 0)
        + (AwayLineup?.EstimatedPayloadBytes ?? 0)
        + ((LiveMetrics?.Count ?? 0) * LiveMetricBytes);

    /// <summary>Gets the estimated bytes one captured metric contributes, on the same accounting.</summary>
    public const int LiveMetricBytes = 64;

    /// <summary>Gets the estimated bytes one playback segment contributes, on the same accounting.</summary>
    public const int ScheduleSegmentBytes = 48;
}

/// <summary>
/// How much is worth showing, and how much may be sent (`replay-v3`).
/// </summary>
/// <remarks>
/// The caps are policy rather than formulas, but they are versioned with the presentation so a stored
/// payload can always be explained. They exist because "show every shot" produces a payload nobody wants to
/// download on a phone (`match_presentation_payload_budget_kb`). The film's pacing constants live here too,
/// because the time warp is a presentation decision rather than a simulation one.
/// </remarks>
public sealed record HighlightOptionsV1
{
    /// <summary>Gets the least goal probability that makes a shot worth a reel clip, in basis points.</summary>
    public int MinQualityForShotBasisPoints { get; init; } = 700;

    /// <summary>Gets whether shots that hit the woodwork are worth a reel clip.</summary>
    public bool IncludeWoodwork { get; init; } = true;

    /// <summary>Gets whether direct free kicks are worth a reel clip, at any quality.</summary>
    public bool IncludeFreeKicks { get; init; } = true;

    /// <summary>Gets the most clips one reel may carry, goals excepted.</summary>
    public int MaxReelClips { get; init; } = 12;

    /// <summary>Gets the estimated payload budget for one match, in bytes (750 KB, ADR-0006).</summary>
    public int PayloadBudgetBytes { get; init; } = 750 * 1024;

    /// <summary>Gets the divisor that maps match seconds onto film milliseconds (nine to one).</summary>
    public int FilmMatchSecondsPerFilmSecond { get; init; } = 9;

    /// <summary>Gets the shortest the film may run for, in milliseconds (9:30).</summary>
    public int MinFilmMilliseconds { get; init; } = (9 * 60 * 1_000) + 30_000;

    /// <summary>Gets the longest the film may run for, in milliseconds (11:00, a hard ceiling).</summary>
    public int MaxFilmMilliseconds { get; init; } = 11 * 60 * 1_000;

    /// <summary>Gets the shortest any one passage may run for in the film (`replay-v3`).</summary>
    public int MinPassageMilliseconds { get; init; } = 1_200;

    /// <summary>Gets the film length one passage targets, which sets how many passages the film carries.</summary>
    public int TargetPassageMilliseconds { get; init; } = 9_000;

    /// <summary>Gets the most passages the film may carry, so per-passage overhead fits the budget.</summary>
    public int MaxPassages { get; init; } = 75;

    /// <summary>Gets the match seconds of film a reel clip's lead-in reaches back over (about ten minutes).</summary>
    public int ReelLeadInMatchSeconds { get; init; } = 600;

    /// <summary>Gets the shortest a reel clip's lead-in may be, in film milliseconds.</summary>
    public int MinReelLeadInMilliseconds { get; init; } = 25_000;

    /// <summary>Gets the longest a reel clip's lead-in may be, in film milliseconds.</summary>
    public int MaxReelLeadInMilliseconds { get; init; } = 70_000;

    /// <summary>Gets the longest the reel may run for, in milliseconds (12:00).</summary>
    public int MaxReelMilliseconds { get; init; } = 12 * 60 * 1_000;

    /// <summary>
    /// Gets the keyframe tolerances the adaptive ladder tries, in normalized position units.
    /// </summary>
    /// <remarks>
    /// The film is close to the payload budget, so the director retries compression at widening tolerances
    /// until it fits. Deterministic: the same match always lands on the same rung of the ladder.
    /// </remarks>
    public IReadOnlyList<int> TrackTolerances { get; init; } = [32, 40, 48];

    /// <summary>Gets the sampling intervals the adaptive ladder tries, in milliseconds, paired by index.</summary>
    public IReadOnlyList<int> TrackSampleIntervals { get; init; } = [400, 600, 800];
}
