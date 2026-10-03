using TouchlineManager.Application.Abstractions.Match;
using TouchlineManager.Contracts.Match;
using TouchlineManager.Domain.Competition;
using TouchlineManager.MatchEngine.Commentary;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.Application.Match;

/// <summary>
/// Maps a stored match to its transport projections (master plan §9.5).
/// </summary>
/// <remarks>
/// <para>
/// One place, so the summary and the replay describe the same match the same way. The summary reads the
/// stored statistics document rather than re-deriving it, because the document is the published result and
/// a read should not be able to disagree with what was published; the replay re-derives its commentary and
/// highlights from the frozen snapshot, because those are pure functions of it and storing them would be a
/// second copy that could drift (`MAT-8`).
/// </para>
/// <para>
/// Nothing here is player-facing data that is not already public: no seed, no hashes, no shot quality
/// (`MAT-11`, §10.9).
/// </para>
/// </remarks>
public static class MatchMapping
{
    /// <summary>Projects a stored match to its summary.</summary>
    /// <param name="snapshot">The stored match.</param>
    /// <param name="serverTime">When the response was produced.</param>
    public static MatchResponse ToResponse(this MatchReadSnapshot snapshot, DateTimeOffset serverTime)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var statistics = MatchStatisticsDocument.Read(snapshot.StatisticsJson);

        return new MatchResponse(
            snapshot.MatchId,
            snapshot.FixtureId,
            snapshot.DivisionId,
            snapshot.DivisionName,
            snapshot.TierNumber,
            snapshot.CountryId,
            snapshot.CountryCode,
            snapshot.CountryName,
            snapshot.SeasonNumber,
            snapshot.SeasonLabel,
            snapshot.RoundNumber,
            snapshot.KickoffAt,
            // Only a published match is readable at all, so the state is not in question — but it is
            // reported so a client does not have to assume it.
            FixtureStatus.Published.ToCode(),
            new MatchTeamResponse(
                snapshot.Home.Id,
                snapshot.Home.Name,
                snapshot.Home.ShortName,
                snapshot.HomeGoals,
                ToResponse(statistics.Home)),
            new MatchTeamResponse(
                snapshot.Away.Id,
                snapshot.Away.Name,
                snapshot.Away.ShortName,
                snapshot.AwayGoals,
                ToResponse(statistics.Away)),
            snapshot.EngineVersion,
            ReplayDirector.Version,
            serverTime);
    }

    /// <summary>Projects a match's replay: its commentary timeline and its highlights.</summary>
    /// <param name="snapshot">The stored match.</param>
    /// <param name="presentation">The re-derived presentation.</param>
    /// <param name="commentary">The re-derived commentary, in event order.</param>
    public static MatchPresentationResponse ToResponse(
        this MatchReadSnapshot snapshot,
        MatchPresentationV1 presentation,
        IReadOnlyList<CommentaryToken> commentary)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return presentation.ToResponse(snapshot.MatchId, commentary);
    }

    /// <summary>Projects a presentation and its commentary to the replay the API returns.</summary>
    /// <param name="presentation">The presentation.</param>
    /// <param name="matchId">The match it presents.</param>
    /// <param name="commentary">The commentary, in event order.</param>
    public static MatchPresentationResponse ToResponse(
        this MatchPresentationV1 presentation,
        Guid matchId,
        IReadOnlyList<CommentaryToken> commentary)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(commentary);

        return new MatchPresentationResponse(
            matchId,
            presentation.PresentationVersion,
            presentation.EngineVersion,
            presentation.HomeGoals,
            presentation.AwayGoals,
            [.. commentary.Select(ToResponse)],
            [.. presentation.Passages.Select(ToResponse)],
            [.. presentation.Reel.Select(ToResponse)],
            presentation.EstimatedPayloadBytes,
            presentation.HomeLineup is { } homeLineup ? ToResponse(homeLineup) : null,
            presentation.AwayLineup is { } awayLineup ? ToResponse(awayLineup) : null,
            presentation.LiveMetrics is { } metrics ? [.. metrics.Select(ToResponse)] : null,
            presentation.Playback.Count > 0 ? [.. presentation.Playback.Select(ToResponse)] : null,
            presentation.TotalPlaybackMilliseconds,
            presentation.PaceMilli);
    }

    /// <summary>Projects one playback segment.</summary>
    /// <param name="segment">The segment.</param>
    public static PlaybackSegmentResponse ToResponse(this PlaybackSegmentV1 segment)
    {
        ArgumentNullException.ThrowIfNull(segment);

        return new PlaybackSegmentResponse(
            segment.Kind,
            segment.SourceEventSequence,
            segment.StartMilliseconds,
            segment.DurationMilliseconds);
    }

    /// <summary>Projects one reel clip.</summary>
    /// <param name="clip">The clip.</param>
    public static ReelClipResponse ToResponse(this ReelClipV1 clip)
    {
        ArgumentNullException.ThrowIfNull(clip);

        return new ReelClipResponse(
            clip.SourceEventSequence,
            clip.OutcomeCode,
            clip.Minute,
            clip.StoppageMinute,
            clip.StartMilliseconds,
            clip.EndMilliseconds);
    }

    /// <summary>Projects one commentary line.</summary>
    /// <param name="token">The token.</param>
    public static CommentaryLineResponse ToResponse(this CommentaryToken token)
    {
        ArgumentNullException.ThrowIfNull(token);

        return new CommentaryLineResponse(
            token.EventSequence,
            token.Minute,
            token.StoppageMinute,
            MatchVocabulary.Code(token.Side),
            token.TemplateKey,
            token.VariantKey,
            [.. token.Parameters.Select(parameter => new CommentaryParameterResponse(parameter.Name, parameter.Value))],
            token.Text);
    }

    /// <summary>Projects one film passage.</summary>
    /// <param name="passage">The passage.</param>
    public static PassageResponse ToResponse(this PassageV1 passage)
    {
        ArgumentNullException.ThrowIfNull(passage);

        return new PassageResponse(
            passage.SourceEventSequence,
            passage.Minute,
            passage.StoppageMinute,
            passage.StartMatchSecond,
            passage.EndMatchSecond,
            passage.DurationMilliseconds,
            passage.OutcomeCode,
            passage.Narration,
            passage.HomeColour,
            passage.AwayColour,
            passage.EventSequences,
            [.. passage.Entities.Select(ToResponse)],
            [.. passage.Tracks.Select(ToResponse)],
            [.. passage.Commentary.Select(ToResponse)],
            passage.Period,
            [.. passage.Clock.Select(clock => new ClockKeyframeResponse(clock.TimeMilliseconds, clock.MatchSecond))],
            [.. passage.Cuts.Select(cut => new PassageCutResponse(cut.TimeMilliseconds, cut.DurationMilliseconds, cut.Kind))]);
    }

    /// <summary>Projects one synchronized passage commentary line.</summary>
    /// <param name="line">The line.</param>
    public static HighlightCommentaryResponse ToResponse(this HighlightCommentaryV1 line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return new HighlightCommentaryResponse(
            line.TimeMilliseconds,
            line.TemplateKey,
            line.VariantKey,
            [.. line.Parameters.Select(parameter => new CommentaryParameterResponse(parameter.Name, parameter.Value))],
            line.Text);
    }

    /// <summary>Projects one highlight entity.</summary>
    /// <param name="entity">The entity.</param>
    public static HighlightEntityResponse ToResponse(this HighlightEntityV1 entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return new HighlightEntityResponse(
            entity.EntityId,
            entity.IsBall,
            entity.Side is { } side ? MatchVocabulary.Code(side) : null,
            entity.ParticipantId,
            entity.ShirtNumber,
            Code(entity.Family),
            entity.X,
            entity.Y,
            entity.Name,
            entity.Position);
    }

    /// <summary>Projects one entity's track.</summary>
    /// <param name="track">The track.</param>
    public static HighlightTrackResponse ToResponse(this HighlightTrackV1 track)
    {
        ArgumentNullException.ThrowIfNull(track);

        return new HighlightTrackResponse(
            track.EntityId,
            [.. track.Keyframes.Select(ToResponse)]);
    }

    /// <summary>Projects one keyframe.</summary>
    /// <param name="keyframe">The keyframe.</param>
    public static HighlightKeyframeResponse ToResponse(this HighlightKeyframeV1 keyframe) =>
        new(keyframe.TimeMilliseconds, keyframe.X, keyframe.Y, keyframe.Z, keyframe.Speed, keyframe.Action);

    /// <summary>Projects one team's lineup.</summary>
    public static MatchLineupResponse ToResponse(this MatchLineupV1 lineup) =>
        new(
            lineup.ClubName,
            lineup.ShortName,
            lineup.PrimaryColour,
            lineup.SecondaryColour,
            lineup.Formation,
            [.. lineup.Starters.Select(ToResponse)],
            [.. lineup.Bench.Select(ToResponse)]);

    /// <summary>Projects one player's lineup line.</summary>
    public static MatchLineupPlayerResponse ToResponse(this MatchLineupPlayerV1 player) =>
        new(
            player.ParticipantId,
            player.PlayerId,
            player.ShirtNumber,
            player.Name,
            player.Position,
            Code(player.Family) ?? "unknown",
            player.IsStarter,
            player.SlotNumber,
            player.KickoffCondition,
            player.FinalCondition,
            player.FinalRating,
            player.Goals,
            player.Assists,
            player.YellowCards,
            player.SentOff,
            player.SubbedOutMinute,
            player.SubbedInMinute,
            player.IsInjured);

    /// <summary>Projects one live metric snapshot.</summary>
    public static PlayerLiveMetricResponse ToResponse(this PlayerLiveMetricV1 metric) =>
        new(
            metric.ParticipantId,
            metric.Minute,
            metric.ConditionBasisPoints,
            metric.RatingBasisPoints);

    /// <summary>Projects one side's statistics.</summary>
    /// <param name="statistics">The engine's statistics.</param>
    public static MatchStatisticsResponse ToResponse(this MatchStatisticsV1 statistics)
    {
        ArgumentNullException.ThrowIfNull(statistics);

        return new MatchStatisticsResponse(
            statistics.PossessionBasisPoints,
            statistics.Goals,
            statistics.Shots,
            statistics.ShotsOnTarget,
            statistics.ShotsOffTarget,
            statistics.ShotsBlocked,
            statistics.WoodworkHits,
            statistics.Saves,
            statistics.Corners,
            statistics.Offsides,
            statistics.Fouls,
            statistics.YellowCards,
            statistics.RedCards,
            statistics.PenaltiesAwarded,
            statistics.PenaltiesScored,
            statistics.Injuries,
            statistics.Substitutions);
    }

    /// <summary>
    /// Names a position family for a renderer's styling.
    /// </summary>
    /// <remarks>
    /// The engine deliberately has no wire code for a family — it is not part of any snapshot hash — so the
    /// code is the application layer's, and it is the enum's own name in lower case rather than a second
    /// vocabulary to keep in step.
    /// </remarks>
    private static string? Code(MatchPositionFamily? family) => family?.ToString().ToLowerInvariant();
}
