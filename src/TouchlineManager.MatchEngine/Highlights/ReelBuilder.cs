using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Highlights;

/// <summary>
/// One chance the reel might carry, with the film window it sits in (`replay-v3`, `replay-v4`).
/// </summary>
/// <remarks>
/// The director hands the reel builder the chances it found, already located on the film clock, together with how
/// far back the film reaches over the lead-in measured in match time; the builder decides which clips the reel
/// keeps and how far back each one actually reaches.
/// </remarks>
/// <param name="SourceEventSequence">The sequence of the chance event.</param>
/// <param name="OutcomeCode">The chance's outcome as a stable code.</param>
/// <param name="Minute">The match minute.</param>
/// <param name="StoppageMinute">The stoppage minute, or zero in regulation.</param>
/// <param name="FilmStart">Where the possession the chance belongs to starts on the film clock.</param>
/// <param name="FilmEnd">Where the chance ends on the film clock, after its outcome and the reaction to it.</param>
/// <param name="QualityBasisPoints">How good the chance was, in basis points.</param>
/// <param name="IsGoal">Whether the chance was a goal, which every reel keeps.</param>
/// <param name="LeadFilmMilliseconds">
/// How much film lies between <paramref name="FilmStart"/> and the moment, ten match-minutes earlier in the same
/// half, that the lead-in reaches back to (`replay-v4`).
/// </param>
/// <param name="PeriodFilmStart">Where the chance's half starts on the film clock, which a lead-in never crosses.</param>
internal sealed record ReelCandidateV1(
    int SourceEventSequence,
    string OutcomeCode,
    int Minute,
    int StoppageMinute,
    int FilmStart,
    int FilmEnd,
    int QualityBasisPoints,
    bool IsGoal,
    int LeadFilmMilliseconds = 0,
    int PeriodFilmStart = 0);

/// <summary>
/// Builds the highlights reel: a deterministic playlist of chance clips over the film (`replay-v3`, `replay-v4`).
/// </summary>
/// <remarks>
/// <para>
/// The reel is a view of the same film, not a second presentation. Each clip is a window of the film that
/// leads into a selected chance, so a viewer sees the move that produced it rather than a cut to the strike.
/// Goals are always kept — a manager who scores and cannot watch it has been given a worse product than one
/// whose best save was omitted — and the rest of the room goes to the best chances by their goal probability.
/// </para>
/// <para>
/// The lead-in reaches back over about ten match-minutes, measured on the match clock and kept inside the same
/// half, and is then clamped to a band of film time; clips that overlap are merged so two chances a few seconds
/// apart share one continuous stretch rather than replaying the same build-up twice. When the reel would run too
/// long the lead-ins are shortened to their floor first, and only then are the lowest-quality non-goal clips
/// dropped; goals survive both.
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

        var clips = Clips(kept, candidate => LeadInMilliseconds(candidate, options));

        if (Total(clips) > options.MaxReelMilliseconds)
        {
            clips = Clips(kept, _ => options.MinReelLeadInMilliseconds);
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
    private static List<Clip> Clips(IReadOnlyList<ReelCandidateV1> kept, Func<ReelCandidateV1, int> leadIn)
    {
        var clips = new List<Clip>();

        foreach (var candidate in kept.OrderBy(candidate => candidate.FilmStart))
        {
            // A lead-in stays inside the half the chance was made in.
            var start = Math.Max(candidate.PeriodFilmStart, candidate.FilmStart - leadIn(candidate));

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

    /// <summary>The lead-in a clip reaches back over: about ten match-minutes, clamped to a band of film time.</summary>
    private static int LeadInMilliseconds(ReelCandidateV1 candidate, HighlightOptionsV1 options) =>
        int.Clamp(candidate.LeadFilmMilliseconds, options.MinReelLeadInMilliseconds, options.MaxReelLeadInMilliseconds);

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
