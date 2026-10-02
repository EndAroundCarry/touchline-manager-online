using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Highlights;

/// <summary>
/// One chance the reel might carry, with the film window it sits in (`replay-v3`).
/// </summary>
/// <remarks>
/// The director hands the reel builder the chance passages it found, already located on the film clock; the
/// builder decides which clips the reel keeps and how far back each one reaches.
/// </remarks>
/// <param name="SourceEventSequence">The sequence of the chance event.</param>
/// <param name="OutcomeCode">The chance's outcome as a stable code.</param>
/// <param name="Minute">The match minute.</param>
/// <param name="StoppageMinute">The stoppage minute, or zero in regulation.</param>
/// <param name="FilmStart">Where the chance's passage starts on the film clock.</param>
/// <param name="FilmEnd">Where the chance's passage ends on the film clock.</param>
/// <param name="QualityBasisPoints">How good the chance was, in basis points.</param>
/// <param name="IsGoal">Whether the chance was a goal, which every reel keeps.</param>
internal sealed record ReelCandidateV1(
    int SourceEventSequence,
    string OutcomeCode,
    int Minute,
    int StoppageMinute,
    int FilmStart,
    int FilmEnd,
    int QualityBasisPoints,
    bool IsGoal);

/// <summary>
/// Builds the highlights reel: a deterministic playlist of chance clips over the film (`replay-v3`).
/// </summary>
/// <remarks>
/// <para>
/// The reel is a view of the same film, not a second presentation. Each clip is a window of the film that
/// leads into a selected chance, so a viewer sees the move that produced it rather than a cut to the strike.
/// Goals are always kept — a manager who scores and cannot watch it has been given a worse product than one
/// whose best save was omitted — and the rest of the room goes to the best chances by their goal probability.
/// </para>
/// <para>
/// The lead-in reaches back over about ten match-minutes of film, clamped to a band, and clips that overlap
/// are merged so two chances a few seconds apart share one continuous stretch rather than replaying the same
/// build-up twice. When the reel would run too long the lead-ins are shortened to their floor first, and only
/// then are the lowest-quality non-goal clips dropped; goals survive both.
/// </para>
/// </remarks>
internal static class ReelBuilder
{
    /// <summary>Builds the reel from the chance candidates the director found.</summary>
    /// <param name="candidates">The chances, in film order.</param>
    /// <param name="options">How much is worth showing.</param>
    public static IReadOnlyList<ReelClipV1> Build(
        IReadOnlyList<ReelCandidateV1> candidates,
        HighlightOptionsV1 options)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(options);

        if (candidates.Count == 0)
        {
            return [];
        }

        var kept = Select(candidates, options);
        var leadIn = LeadInMilliseconds(options);

        var clips = Clips(kept, leadIn);

        if (Total(clips) > options.MaxReelMilliseconds)
        {
            clips = Clips(kept, options.MinReelLeadInMilliseconds);
        }

        while (Total(clips) > options.MaxReelMilliseconds)
        {
            var droppable = clips
                .Where(clip => !clip.IsGoal)
                .OrderBy(clip => clip.QualityBasisPoints)
                .ThenByDescending(clip => clip.SourceEventSequence)
                .FirstOrDefault();

            if (droppable is null)
            {
                // Only goals are left; a nine-goal match is the one reel allowed to run long.
                break;
            }

            clips.Remove(droppable);
        }

        return [.. clips.Select(clip => new ReelClipV1(
            clip.SourceEventSequence,
            clip.OutcomeCode,
            clip.Minute,
            clip.StoppageMinute,
            clip.StartMilliseconds,
            clip.EndMilliseconds))];
    }

    /// <summary>Applies the count cap, keeping every goal and the best of the rest, in film order.</summary>
    private static List<ReelCandidateV1> Select(
        IReadOnlyList<ReelCandidateV1> candidates,
        HighlightOptionsV1 options)
    {
        var goals = candidates.Where(candidate => candidate.IsGoal).ToList();

        if (candidates.Count <= options.MaxReelClips && goals.Count <= options.MaxReelClips)
        {
            return [.. candidates.OrderBy(candidate => candidate.FilmStart)];
        }

        var others = candidates
            .Where(candidate => !candidate.IsGoal)
            .OrderByDescending(candidate => candidate.QualityBasisPoints)
            .ThenBy(candidate => candidate.SourceEventSequence)
            .Take(Math.Max(0, options.MaxReelClips - goals.Count));

        return [.. goals.Concat(others).OrderBy(candidate => candidate.FilmStart)];
    }

    /// <summary>Wraps each kept chance in a clip window, merging the ones that overlap.</summary>
    private static List<Clip> Clips(IReadOnlyList<ReelCandidateV1> kept, int leadInMilliseconds)
    {
        var clips = new List<Clip>();

        foreach (var candidate in kept.OrderBy(candidate => candidate.FilmStart))
        {
            var start = Math.Max(0, candidate.FilmStart - leadInMilliseconds);

            if (clips.Count > 0 && start <= clips[^1].EndMilliseconds)
            {
                // Overlapping clips are one continuous stretch; the merge keeps the higher quality of the two
                // so a dropped-clip decision never throws away the better chance.
                var last = clips[^1];

                clips[^1] = last with
                {
                    EndMilliseconds = Math.Max(last.EndMilliseconds, candidate.FilmEnd),
                    QualityBasisPoints = Math.Max(last.QualityBasisPoints, candidate.QualityBasisPoints),
                    IsGoal = last.IsGoal || candidate.IsGoal,
                };

                continue;
            }

            clips.Add(new Clip(
                candidate.SourceEventSequence,
                candidate.OutcomeCode,
                candidate.Minute,
                candidate.StoppageMinute,
                start,
                candidate.FilmEnd,
                candidate.QualityBasisPoints,
                candidate.IsGoal));
        }

        return clips;
    }

    /// <summary>The lead-in a clip reaches back over: about ten match-minutes of film, clamped to the band.</summary>
    private static int LeadInMilliseconds(HighlightOptionsV1 options)
    {
        // The film runs at roughly nine match-seconds a film-second, so ten match-minutes of lead-in is about
        // sixty-five film-seconds; the band stops a very short clip or an uncomfortably long one.
        var scaled = options.ReelLeadInMatchSeconds * 1_000 / Math.Max(1, options.FilmMatchSecondsPerFilmSecond);

        return int.Clamp(scaled, options.MinReelLeadInMilliseconds, options.MaxReelLeadInMilliseconds);
    }

    private static int Total(List<Clip> clips) => clips.Sum(clip => clip.EndMilliseconds - clip.StartMilliseconds);

    /// <summary>A merged clip window, before it is projected to the transport shape.</summary>
    private sealed record Clip(
        int SourceEventSequence,
        string OutcomeCode,
        int Minute,
        int StoppageMinute,
        int StartMilliseconds,
        int EndMilliseconds,
        int QualityBasisPoints,
        bool IsGoal);
}
