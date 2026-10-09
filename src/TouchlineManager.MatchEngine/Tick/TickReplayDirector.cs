using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Simulation;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>
/// Assembles a tick match's film into the presentation the viewer reads (`tick-engine-v1`, Milestone 9).
/// </summary>
/// <remarks>
/// <para>
/// The tick engine's passages are already the presentation's own: the synthesizer slices the continuous
/// recording into windows of real play, and each window carries its entities, clock, cuts and compressed
/// keyframe tracks. All that is left is the presentation around them — the playback schedule that runs one
/// segment per passage back to back from zero, the lineups, the live curve, and the reel over the same film —
/// which is what this assembly adds. Nothing is reconstructed and nothing is invented: a client reads the same
/// <see cref="MatchPresentationV1"/> shape the possession film produces, so the 2D viewer needs no change
/// (ADR-0006, plan §1.3.1).
/// </para>
/// <para>
/// The reel is the same playlist the possession film carries, located on this film's clock: every goal and the
/// best of the other chances, each clip reaching back over its lead-in into the same half and the whole
/// selection capped and merged by <see cref="ReelBuilder"/> (`replay-v3`, `replay-v4`).
/// </para>
/// </remarks>
internal static class TickReplayDirector
{
    /// <summary>Builds the presentation from a finished match's film.</summary>
    /// <param name="input">The frozen snapshot.</param>
    /// <param name="result">The simulated result.</param>
    /// <param name="film">The film the synthesizer sliced from the recording.</param>
    /// <param name="recording">The continuous trace, for where each event sits on the tick clock.</param>
    /// <param name="state">The match state, for the events the reel selects.</param>
    /// <param name="options">How much is worth showing and how far a clip reaches back.</param>
    /// <param name="liveMetrics">The minute-by-minute condition and rating curve, or null.</param>
    public static MatchPresentationV1 Build(
        MatchInputV1 input,
        MatchResultV1 result,
        TickReplayFilm film,
        TickMatchRecording recording,
        MatchState state,
        HighlightOptionsV1 options,
        IReadOnlyList<PlayerLiveMetricV1>? liveMetrics)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(film);
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(options);

        var passages = new List<PassageV1>(film.Passages.Count);

        foreach (var slice in film.Passages)
        {
            passages.Add(slice.Passage);
        }

        return new MatchPresentationV1
        {
            PresentationVersion = ReplayDirector.Version,
            EngineVersion = result.EngineVersion,
            HomeGoals = result.HomeGoals,
            AwayGoals = result.AwayGoals,
            Passages = passages,
            Reel = ReelBuilder.Build(Candidates(state, film, recording, options), options),
            Playback = Schedule(passages),
            HomeLineup = MatchLineupBuilder.Build(input, result, MatchSide.Home),
            AwayLineup = MatchLineupBuilder.Build(input, result, MatchSide.Away),
            LiveMetrics = liveMetrics,
            PaceMilli = film.PaceMilli,
        };
    }

    /// <summary>Builds the playback schedule: one segment per passage, each starting where the last ended.</summary>
    /// <param name="passages">The passages, in film order.</param>
    private static List<PlaybackSegmentV1> Schedule(List<PassageV1> passages)
    {
        var schedule = new List<PlaybackSegmentV1>(passages.Count);
        var start = 0;

        foreach (var passage in passages)
        {
            schedule.Add(new PlaybackSegmentV1(
                "passage",
                passage.SourceEventSequence,
                start,
                passage.DurationMilliseconds));

            start += passage.DurationMilliseconds;
        }

        return schedule;
    }

    /// <summary>Locates every chance the reel might carry on the film clock.</summary>
    /// <remarks>
    /// A chance sits in the passage its event was emitted in; its clip's own window starts at that passage's
    /// first frame, so a clip can be merged with its neighbours without cutting a passage in half, and a goal's
    /// clip runs to the end of its passage, because the celebration is the moment.
    /// </remarks>
    /// <param name="state">The match state, for the events.</param>
    /// <param name="film">The film, for the passage windows.</param>
    /// <param name="recording">The recording, for where each event sits on the tick clock.</param>
    /// <param name="options">How much is worth showing and how far a clip reaches back.</param>
    private static List<ReelCandidateV1> Candidates(
        MatchState state,
        TickReplayFilm film,
        TickMatchRecording recording,
        HighlightOptionsV1 options)
    {
        var starts = new int[film.Passages.Count];
        var start = 0;

        for (var index = 0; index < film.Passages.Count; index++)
        {
            starts[index] = start;
            start += film.Passages[index].Passage.DurationMilliseconds;
        }

        var tickOfSequence = new Dictionary<int, int>(recording.Events.Count);

        foreach (var stamp in recording.Events)
        {
            tickOfSequence[stamp.Sequence] = stamp.Tick;
        }

        var secondHalfStart = 0;

        for (var index = 0; index < film.Passages.Count; index++)
        {
            if (film.Passages[index].Passage.Period == 2)
            {
                secondHalfStart = starts[index];

                break;
            }
        }

        var filmMsPerTick = Math.Max(1, TickSpatialUnits.TickDeltaMs / Math.Max(1, options.FilmMatchSecondsPerFilmSecond));
        var leadIn = Math.Min(
            options.MaxReelLeadInMilliseconds,
            Math.Max(
                options.MinReelLeadInMilliseconds,
                options.ReelLeadInMatchSeconds * 1_000 / Math.Max(1, options.FilmMatchSecondsPerFilmSecond)));
        var total = film.TotalMilliseconds;
        var candidates = new List<ReelCandidateV1>();

        for (var index = 0; index < film.Passages.Count; index++)
        {
            var slice = film.Passages[index];
            var passageStart = starts[index];
            var passageEnd = passageStart + slice.Passage.DurationMilliseconds;

            foreach (var sequence in slice.Passage.EventSequences)
            {
                var matchEvent = state.Events[sequence - 1];

                if (!ReplayDirector.IsWorthShowing(matchEvent, options) || !tickOfSequence.TryGetValue(sequence, out var tick))
                {
                    continue;
                }

                var eventMs = passageStart + ((tick - slice.FirstTick) * filmMsPerTick);
                var end = Math.Min(eventMs + options.ReelReactionMilliseconds, total);

                if (matchEvent.IsGoal)
                {
                    end = Math.Max(end, passageEnd);
                }

                candidates.Add(new ReelCandidateV1(
                    sequence,
                    FilmLabels.OutcomeCode(matchEvent.Type),
                    matchEvent.Minute,
                    matchEvent.StoppageMinute,
                    passageStart,
                    Math.Min(end, total),
                    matchEvent.QualityBasisPoints ?? 0,
                    matchEvent.IsGoal,
                    leadIn,
                    slice.Passage.Period == 2 ? secondHalfStart : 0));
            }
        }

        return candidates;
    }
}
