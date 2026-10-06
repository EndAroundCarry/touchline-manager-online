using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Highlights;

/// <summary>
/// Turns a simulated match and its recorded possessions into one constant-pace film and a highlights reel over
/// the same data (`replay-v4`, master plan §9.2–§9.3, ADR-0006, ADR-0054).
/// </summary>
/// <remarks>
/// <para>
/// A possession in `engine-v5` is a real passage of play with real start and end times: the ball starts where the
/// last one left it, moves through a chain of touches, and ends at an outcome-appropriate point. The director
/// scripts each one into beats — passes between players, carries, a cross only from a wide position into the box,
/// a strike that reaches the goal or the keeper — gives every move its natural length, and solves the one pace the
/// whole film is played at. Nothing is warped: a quiet spell is shorter in the film only because there is less
/// ball movement in it, and the quietest play is condensed first when a busy match would otherwise push the pace
/// past its band.
/// </para>
/// <para>
/// The players are simulated at bounded speed and acceleration around the shape the side's real instructions
/// give, with the players the engine named, and the receiver of every pass, held to the ball. The film is cut
/// into passages of about ten seconds with continuous tracks, so a boundary is only a place the data is chunked,
/// and an explicit cut is the only place anything jumps. The reel is a server-side playlist of chance clips over
/// the same film, each reaching back about ten match-minutes into the same half.
/// </para>
/// </remarks>
public static class ReplayDirector
{
    /// <summary>The version label of this presentation.</summary>
    public const string Version = "replay-v5";

    /// <summary>The most times the film is played again to let its pace settle.</summary>
    private const int MaxSettlingRuns = 4;

    /// <summary>Builds the match's presentation from the passages recorded while it was simulated.</summary>
    /// <param name="input">The frozen snapshot, which supplies the eleven and their positions.</param>
    /// <param name="result">The simulated result.</param>
    /// <param name="passages">
    /// The recorded possessions, in order. The film is built from them; a caller that recorded nothing gets an
    /// empty presentation rather than an invented one.
    /// </param>
    /// <param name="options">How much is worth showing, and the film's pacing.</param>
    /// <param name="liveMetrics">The minute-by-minute condition and rating curve, or null.</param>
    public static MatchPresentationV1 Build(
        MatchInputV1 input,
        MatchResultV1 result,
        IReadOnlyList<MatchPassageV1> passages,
        HighlightOptionsV1? options = null,
        IReadOnlyList<PlayerLiveMetricV1>? liveMetrics = null) =>
        BuildFilm(input, result, passages, options, liveMetrics, diagnose: false).Presentation;

    /// <summary>
    /// Builds the presentation and measures the film it describes: its pace, how much was condensed, how fast the
    /// ball moves, and whether anything jumps. For tests and the replay benchmark.
    /// </summary>
    /// <param name="input">The frozen snapshot.</param>
    /// <param name="result">The simulated result.</param>
    /// <param name="passages">The recorded possessions, in order.</param>
    /// <param name="options">How much is worth showing, and the film's pacing.</param>
    /// <param name="liveMetrics">The minute-by-minute condition and rating curve, or null.</param>
    public static FilmBuild Analyse(
        MatchInputV1 input,
        MatchResultV1 result,
        IReadOnlyList<MatchPassageV1> passages,
        HighlightOptionsV1? options = null,
        IReadOnlyList<PlayerLiveMetricV1>? liveMetrics = null) =>
        BuildFilm(input, result, passages, options, liveMetrics, diagnose: true);

    /// <summary>
    /// Scripts a match into beats without playing them (tests only): what the film is going to show, before it is
    /// timed or moved.
    /// </summary>
    /// <param name="input">The frozen snapshot.</param>
    /// <param name="result">The simulated result.</param>
    /// <param name="passages">The recorded possessions, in order.</param>
    internal static FilmScriptResult ScriptOf(MatchInputV1 input, MatchResultV1 result, IReadOnlyList<MatchPassageV1> passages)
    {
        var context = new FilmContext(input, result, EngineRulesV2.Default, new HighlightOptionsV1());
        var changes = PersonnelChanges(context, passages);
        var rosters = RostersFor(context, passages.Count, changes);

        var script = FilmScript.Build(context, new FilmShape(context), passages, rosters, changes);

        FilmTiming.Assign(context, script.Beats);

        return script;
    }

    private static FilmBuild BuildFilm(
        MatchInputV1 input,
        MatchResultV1 result,
        IReadOnlyList<MatchPassageV1> passages,
        HighlightOptionsV1? options,
        IReadOnlyList<PlayerLiveMetricV1>? liveMetrics,
        bool diagnose)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(passages);

        var settings = options ?? new HighlightOptionsV1();
        var rules = EngineRulesV2.Default;
        var homeLineup = MatchLineupBuilder.Build(input, result, MatchSide.Home);
        var awayLineup = MatchLineupBuilder.Build(input, result, MatchSide.Away);

        if (passages.Count == 0)
        {
            return new FilmBuild(
                new MatchPresentationV1
                {
                    PresentationVersion = Version,
                    EngineVersion = result.EngineVersion,
                    HomeGoals = result.HomeGoals,
                    AwayGoals = result.AwayGoals,
                    Passages = [],
                    Reel = [],
                    Playback = [],
                    HomeLineup = homeLineup,
                    AwayLineup = awayLineup,
                    LiveMetrics = liveMetrics,
                },
                null);
        }

        var context = new FilmContext(input, result, rules, settings);
        var changes = PersonnelChanges(context, passages);
        var rosters = RostersFor(context, passages.Count, changes);
        var shape = new FilmShape(context);

        var script = FilmScript.Build(context, shape, passages, rosters, changes);

        FilmTiming.Assign(context, script.Beats);

        var target = FilmTiming.TargetSeconds(context, passages);
        var allowance = 0.15;
        var condensed = FilmTiming.Condense(context, ref script, target, allowance);

        var pace = FilmTiming.Solve(context, script.Beats, target, allowance, condensed);
        var holds = script.Beats.Where(beat => beat.IsHold).Sum(beat => beat.HoldFilmSeconds);
        var natural = script.Beats.Where(beat => !beat.IsHold).Sum(beat => beat.NaturalSeconds);
        FilmMotionResult motion;
        var used = pace;

        for (var run = 0; ; run++)
        {
            used = pace;
            motion = new FilmMotion(context, shape, rosters, passages).Run(script.Beats, used.Pace, used.HoldScale);

            var solved = FilmTiming.SolveFor(settings, motion.MotionSeconds, holds, target, condensed);
            var measured = natural <= 0 ? 0.0 : Math.Max(0.0, (motion.MotionSeconds / natural) - 1.0);

            // The quiet play is condensed again when the lengthened moves have pushed the pace out of its band.
            if (solved.Pace > settings.CondensePaceMilli / 1_000.0 && run < MaxSettlingRuns - 1)
            {
                var more = FilmTiming.Condense(context, ref script, target, measured);

                if (more > 0)
                {
                    condensed += more;
                    natural = script.Beats.Where(beat => !beat.IsHold).Sum(beat => beat.NaturalSeconds);
                    pace = FilmTiming.Solve(context, script.Beats, target, measured, condensed);

                    continue;
                }
            }

            var settled = Math.Abs(solved.Pace - used.Pace) <= 0.004 * used.Pace
                && Math.Abs(solved.HoldScale - used.HoldScale) <= 0.01;

            if (settled || run >= MaxSettlingRuns - 1)
            {
                pace = solved;

                break;
            }

            pace = solved;
        }

        // The film never runs past its ceiling: if the holds were paid for at a pace that has since moved, the
        // pace is raised until it fits.
        var ceilingSeconds = settings.MaxFilmMilliseconds / 1_000.0 - 0.05;

        for (var guard = 0; guard < 3; guard++)
        {
            var length = (motion.MotionSeconds / used.Pace) + used.HoldSeconds;

            if (length <= ceilingSeconds)
            {
                break;
            }

            var raised = used.Pace * (length / ceilingSeconds) * 1.002;

            used = used with { Pace = raised };
            motion = new FilmMotion(context, shape, rosters, passages).Run(script.Beats, used.Pace, used.HoldScale);
        }

        var assembler = new FilmAssembler(context, script, motion, rosters, used.Pace);
        var plans = assembler.Plan();
        var playback = Schedule(plans);
        var reel = ReelBuilder.Build(Candidates(context, script, assembler, plans, settings), settings);

        var built = new List<PassageV1>(plans.Count);
        var rung = 0;

        var (homeColour, awayColour) = FilmLabels.Colours(input);

        for (; rung < settings.PlayerTolerances.Count; rung++)
        {
            var tolerance = settings.PlayerTolerances[rung];
            var interval = settings.PlayerSampleIntervals[Math.Min(rung, settings.PlayerSampleIntervals.Count - 1)];

            built = [.. plans.Select(plan => ToPassage(plan, assembler.Tracks(plan, interval, tolerance), homeColour, awayColour))];

            if (Estimate(built, reel, playback, homeLineup, awayLineup, liveMetrics) <= settings.PayloadBudgetBytes)
            {
                break;
            }
        }

        var presentation = new MatchPresentationV1
        {
            PresentationVersion = Version,
            EngineVersion = result.EngineVersion,
            HomeGoals = result.HomeGoals,
            AwayGoals = result.AwayGoals,
            Passages = built,
            Reel = reel,
            Playback = playback,
            HomeLineup = homeLineup,
            AwayLineup = awayLineup,
            LiveMetrics = liveMetrics,
            PaceMilli = (int)Math.Round(used.Pace * 1_000.0),
        };

        var diagnostics = diagnose
            ? FilmMeasure.Measure(context, script, motion, rosters, used, target, assembler.TotalMs, Math.Min(rung, settings.PlayerTolerances.Count - 1))
            : null;

        return new FilmBuild(presentation, diagnostics);
    }

    // ---- Personnel --------------------------------------------------------------------------------------------

    /// <summary>
    /// Finds the substitutions and the players who leave the pitch, and the possession each is effective before.
    /// </summary>
    /// <remarks>
    /// A substitution is stamped with the minute the next possession ends in, but made before it begins. It is
    /// placed in the window of possessions the event sequence leaves it, and in the first one whose end falls in
    /// the minute it was stamped in. Placing a change early is harmless and placing it late would show a player
    /// who has not come on yet, so where the window is ambiguous the first possession of it is taken.
    /// </remarks>
    private static List<PersonnelChange> PersonnelChanges(FilmContext context, IReadOnlyList<MatchPassageV1> possessions)
    {
        var rules = context.Rules;
        var changes = new List<PersonnelChange>();
        var lowest = new int[possessions.Count];
        var highest = new int[possessions.Count];

        for (var index = 0; index < possessions.Count; index++)
        {
            lowest[index] = possessions[index].Events.Count == 0 ? int.MaxValue : possessions[index].Events.Min(recorded => recorded.Sequence);
            highest[index] = possessions[index].Events.Count == 0 ? int.MinValue : possessions[index].Events.Max(recorded => recorded.Sequence);
        }

        var substituted = context.Result.Events
            .Where(matchEvent => matchEvent.Type == EngineEventType.Substitution)
            .Select(matchEvent => matchEvent.ParticipantId)
            .ToHashSet();

        foreach (var matchEvent in context.Result.Events.OrderBy(matchEvent => matchEvent.Sequence))
        {
            switch (matchEvent.Type)
            {
                case EngineEventType.Substitution when matchEvent.ParticipantId is Guid off:
                    {
                        var before = SubstitutionPossession(matchEvent, possessions, lowest, highest, rules);

                        changes.Add(new PersonnelChange(before, matchEvent.Side, off, matchEvent.SecondaryParticipantId, matchEvent));
                        break;
                    }

                case EngineEventType.RedCard or EngineEventType.SecondYellowCard when matchEvent.ParticipantId is Guid sent:
                    changes.Add(new PersonnelChange(PossessionAfter(matchEvent, possessions) + 0, matchEvent.Side, sent, null, null));
                    break;

                case EngineEventType.Injury when matchEvent.ParticipantId is Guid hurt && !substituted.Contains(hurt):
                    // An injured player nobody can replace still cannot continue: the side finishes a player short.
                    changes.Add(new PersonnelChange(PossessionAfter(matchEvent, possessions), matchEvent.Side, hurt, null, null));
                    break;

                default:
                    break;
            }
        }

        return [.. changes.Where(change => change.BeforePossession < possessions.Count)];
    }

    /// <summary>Gets the index of the possession after the one an event was recorded in.</summary>
    private static int PossessionAfter(EngineEventV1 matchEvent, IReadOnlyList<MatchPassageV1> possessions)
    {
        for (var index = 0; index < possessions.Count; index++)
        {
            if (possessions[index].Events.Any(recorded => recorded.Sequence == matchEvent.Sequence))
            {
                return index + 1;
            }
        }

        // Not in any possession: the event was emitted between two, and the player leaves before the next one.
        for (var index = 0; index < possessions.Count; index++)
        {
            if (possessions[index].Events.Any(recorded => recorded.Sequence > matchEvent.Sequence))
            {
                return index;
            }
        }

        return possessions.Count;
    }

    private static int SubstitutionPossession(
        EngineEventV1 matchEvent,
        IReadOnlyList<MatchPassageV1> possessions,
        int[] lowest,
        int[] highest,
        EngineRulesV2 rules)
    {
        var lo = -1;
        var hi = possessions.Count;

        for (var index = 0; index < possessions.Count; index++)
        {
            // A possession with no event says nothing about where in the sequence a change was made.
            if (possessions[index].Events.Count == 0)
            {
                continue;
            }

            if (highest[index] < matchEvent.Sequence)
            {
                lo = index;
            }

            if (lowest[index] > matchEvent.Sequence && hi == possessions.Count)
            {
                hi = index;
            }
        }

        var period = matchEvent.Minute > rules.HalfTimeMinute ? 2 : 1;
        var regulation = period == 1 ? rules.HalfTimeMinute : rules.RegulationMinutes;
        var floor = (matchEvent.StoppageMinute > 0 ? regulation + matchEvent.StoppageMinute - 1 : matchEvent.Minute - 1) * rules.SecondsPerMinute;
        var first = Math.Min(possessions.Count - 1, lo + 1);

        for (var index = lo + 1; index <= Math.Min(hi, possessions.Count - 1); index++)
        {
            var possession = possessions[index];

            if (possession.Period == period
                && possession.EndClockSeconds >= floor
                && possession.EndClockSeconds < floor + rules.SecondsPerMinute)
            {
                return index;
            }
        }

        return Math.Max(0, first);
    }

    /// <summary>Gets who is on the pitch for each possession, sharing one roster between possessions with no change.</summary>
    private static FilmRoster[] RostersFor(FilmContext context, int count, List<PersonnelChange> changes)
    {
        var rosters = new FilmRoster[count];
        var roster = context.Starters;

        for (var index = 0; index < count; index++)
        {
            foreach (var change in changes.Where(change => change.BeforePossession == index).OrderBy(change => change.Event?.Sequence ?? int.MaxValue))
            {
                roster = roster.With(change.Off, change.On);
            }

            rosters[index] = roster;
        }

        return rosters;
    }

    // ---- The schedule, the reel, and the payload ----------------------------------------------------------------

    private static List<PlaybackSegmentV1> Schedule(List<FilmPassagePlan> plans) =>
    [
        .. plans.Select(plan => new PlaybackSegmentV1("passage", plan.SourceEventSequence, plan.StartMs, plan.DurationMs)),
    ];

    /// <summary>The chance candidates the reel is built from, located on the film clock.</summary>
    private static List<ReelCandidateV1> Candidates(
        FilmContext context,
        FilmScriptResult script,
        FilmAssembler assembler,
        List<FilmPassagePlan> plans,
        HighlightOptionsV1 settings)
    {
        var candidates = new List<ReelCandidateV1>();
        var rules = context.Rules;
        var secondHalfStart = 0;

        for (var index = 0; index < script.Beats.Count; index++)
        {
            if (script.Beats[index].Hold == HoldKind.HalfTime)
            {
                secondHalfStart = assembler.BeatEndMs(index);
            }
        }

        foreach (var possession in script.Possessions)
        {
            var owned = Enumerable.Range(possession.FirstBeat, possession.LastBeat - possession.FirstBeat + 1)
                .Where(index => script.Beats[index].Possession == possession.Index)
                .ToList();

            foreach (var index in owned)
            {
                foreach (var recorded in script.Beats[index].Events)
                {
                    if (!context.EventsBySequence.TryGetValue(recorded.Sequence, out var matchEvent) || !IsWorthShowing(matchEvent, settings))
                    {
                        continue;
                    }

                    var period = possession.Source.Period;
                    var periodStartMs = period == 1 ? 0 : secondHalfStart;
                    var periodStartSecond = period == 1 ? 0 : rules.HalfTimeMinute * rules.SecondsPerMinute;
                    var filmStart = assembler.BeatStartMs(owned[0]);
                    var eventMs = assembler.EventMs(index, recorded);
                    var end = eventMs + settings.ReelReactionMilliseconds;

                    // A goal runs on through its celebration, whether the event is on the strike or on the hold after it.
                    if (script.Beats[index].Hold == HoldKind.Goal)
                    {
                        end = Math.Max(end, assembler.BeatEndMs(index));
                    }
                    else if (index + 1 < script.Beats.Count && script.Beats[index + 1].Hold == HoldKind.Goal)
                    {
                        end = Math.Max(end, assembler.BeatEndMs(index + 1));
                    }

                    var leadSecond = Math.Max(periodStartSecond, possession.Source.EndClockSeconds - settings.ReelLeadInMatchSeconds);
                    var leadStartMs = assembler.Clock.FilmAt(period, leadSecond);

                    candidates.Add(new ReelCandidateV1(
                        recorded.Sequence,
                        FilmLabels.OutcomeCode(matchEvent.Type),
                        matchEvent.Minute,
                        matchEvent.StoppageMinute,
                        filmStart,
                        Math.Min(end, assembler.TotalMs),
                        matchEvent.QualityBasisPoints ?? 0,
                        matchEvent.IsGoal,
                        Math.Max(0, filmStart - Math.Max(leadStartMs, periodStartMs)),
                        periodStartMs));
                }
            }
        }

        _ = plans;

        return candidates;
    }

    /// <summary>
    /// Whether an event is worth a reel clip, before any cap is applied.
    /// </summary>
    /// <remarks>
    /// Every goal and penalty, then shots whose goal probability cleared the threshold, and the direct free
    /// kicks the spatial play model struck at goal. A penalty award or a free-kick award is not selected on its
    /// own: the goal or the miss that follows it a moment later is the thing worth watching.
    /// </remarks>
    private static bool IsWorthShowing(EngineEventV1 matchEvent, HighlightOptionsV1 options) => matchEvent.Type switch
    {
        EngineEventType.Goal or EngineEventType.PenaltyGoal or EngineEventType.PenaltyMissed => true,
        EngineEventType.FreeKickShot when options.IncludeFreeKicks => true,
        EngineEventType.Woodwork => options.IncludeWoodwork,
        EngineEventType.ShotSaved or EngineEventType.ShotBlocked or EngineEventType.ShotOffTarget =>
            (matchEvent.QualityBasisPoints ?? 0) >= options.MinQualityForShotBasisPoints,
        _ => false,
    };

    private static PassageV1 ToPassage(
        FilmPassagePlan plan,
        List<HighlightTrackV1> tracks,
        string homeColour,
        string awayColour) => new()
        {
            PresentationVersion = Version,
            SourceEventSequence = plan.SourceEventSequence,
            Minute = plan.Minute,
            StoppageMinute = plan.StoppageMinute,
            Period = plan.Period,
            StartMatchSecond = plan.StartMatchSecond,
            EndMatchSecond = plan.EndMatchSecond,
            DurationMilliseconds = plan.DurationMs,
            OutcomeCode = plan.OutcomeCode,
            Narration = plan.Narration,
            HomeColour = homeColour,
            AwayColour = awayColour,
            EventSequences = plan.EventSequences,
            Entities = plan.Entities,
            Tracks = tracks,
            Commentary = plan.Commentary,
            Clock = plan.Clock,
            Cuts = plan.Cuts,
        };

    private static int Estimate(
        IReadOnlyList<PassageV1> passages,
        IReadOnlyList<ReelClipV1> reel,
        List<PlaybackSegmentV1> playback,
        MatchLineupV1 homeLineup,
        MatchLineupV1 awayLineup,
        IReadOnlyList<PlayerLiveMetricV1>? liveMetrics) =>
        passages.Sum(passage => passage.EstimatedPayloadBytes)
        + reel.Sum(clip => clip.EstimatedPayloadBytes)
        + (playback.Count * MatchPresentationV1.ScheduleSegmentBytes)
        + homeLineup.EstimatedPayloadBytes
        + awayLineup.EstimatedPayloadBytes
        + ((liveMetrics?.Count ?? 0) * MatchPresentationV1.LiveMetricBytes);
}

/// <summary>A presentation, and what was measured of the film it describes (`replay-v4`).</summary>
/// <param name="Presentation">The presentation.</param>
/// <param name="Diagnostics">The measurements, when they were asked for.</param>
public sealed record FilmBuild(MatchPresentationV1 Presentation, FilmDiagnostics? Diagnostics);

/// <summary>How fast the ball moves in one kind of beat, in metres per second of film (`replay-v4`).</summary>
/// <param name="Median">The median speed.</param>
/// <param name="Percentile95">The ninety-fifth percentile.</param>
public sealed record BallSpeedSummary(double Median, double Percentile95);

/// <summary>
/// What a film is, measured rather than described (`replay-v4`): its pace, how much was condensed, whether the ball
/// jumps, and how fast everything moves. Used by the tests and the replay benchmark; never sent to a client.
/// </summary>
public sealed record FilmDiagnostics
{
    /// <summary>Gets the one pace the film is played at.</summary>
    public required double Pace { get; init; }

    /// <summary>Gets the factor the holds were scaled by.</summary>
    public required double HoldScale { get; init; }

    /// <summary>Gets the film length aimed at, in milliseconds.</summary>
    public required int TargetMilliseconds { get; init; }

    /// <summary>Gets the film length, in milliseconds.</summary>
    public required int FilmMilliseconds { get; init; }

    /// <summary>Gets how much of the film is holds, in milliseconds.</summary>
    public required int HoldMilliseconds { get; init; }

    /// <summary>Gets how many possessions had their ground moves merged to fit.</summary>
    public required int CondensedPossessions { get; init; }

    /// <summary>Gets how many possessions the film has.</summary>
    public required int Possessions { get; init; }

    /// <summary>Gets how much longer than their natural length the moves were played, as a share.</summary>
    public required double ExtensionShare { get; init; }

    /// <summary>Gets the number of cuts.</summary>
    public required int Cuts { get; init; }

    /// <summary>Gets how many steps the ball moved further than any ball can, outside the cuts.</summary>
    public required int Teleports { get; init; }

    /// <summary>Gets the share of the film outside the holds in which the ball is still.</summary>
    public required double StillBallShare { get; init; }

    /// <summary>Gets the fastest an outfield player moves, in metres per second of film.</summary>
    public required double MaxPlayerFilmSpeed { get; init; }

    /// <summary>Gets the fastest a goalkeeper moves, in metres per second of film.</summary>
    public required double MaxKeeperFilmSpeed { get; init; }

    /// <summary>Gets the ball's speed in each kind of beat, in metres per second of film.</summary>
    public required IReadOnlyDictionary<string, BallSpeedSummary> BallSpeeds { get; init; }

    /// <summary>Gets which rung of the payload ladder the film was compressed at.</summary>
    public required int PayloadRung { get; init; }

    /// <summary>Gets how many moves had a player drive the ball.</summary>
    public required int CarryBeats { get; init; }

    /// <summary>Gets how many of those ended with the driver within a metre and a half of the ball.</summary>
    public required int CarriesWithBall { get; init; }

    /// <summary>Gets how many passes, crosses and lofted balls were received by a player on the pitch.</summary>
    public required int Receptions { get; init; }

    /// <summary>Gets how many of those were received with the receiver within a metre and a half of the ball.</summary>
    public required int ReceiversAtBall { get; init; }

    /// <summary>Gets the furthest a receiver was from the ball as it arrived, in metres.</summary>
    public required double WorstReceiverGap { get; init; }

    /// <summary>Gets how many saves were made.</summary>
    public required int Saves { get; init; }

    /// <summary>Gets the furthest the goalkeeper was from the ball as he saved it, in metres.</summary>
    public required double WorstKeeperGap { get; init; }

    /// <summary>Gets how many strikes ended in a goal.</summary>
    public required int GoalStrikes { get; init; }

    /// <summary>Gets how many of those ended inside the goal mouth, on the line.</summary>
    public required int GoalsInNet { get; init; }

    /// <summary>Gets how the players stand around the ball in open play (`replay-v6`).</summary>
    public required ShapeMetrics Shape { get; init; }
}

/// <summary>
/// How the twenty-two stand around the ball in open play, sampled at every step of the simulated record with the dead
/// balls left out (`replay-v6`). Distances are metres. Used by the tests and the replay benchmark; never sent to a client.
/// </summary>
public sealed record ShapeMetrics
{
    /// <summary>Gets how many steps were sampled.</summary>
    public required int Samples { get; init; }

    /// <summary>Gets the median number of players within five metres of the ball.</summary>
    public required double NearBallP50 { get; init; }

    /// <summary>Gets the ninety-fifth percentile of the number of players within five metres of the ball.</summary>
    public required double NearBallP95 { get; init; }

    /// <summary>Gets the fifth percentile of an outfield player's distance to his nearest team-mate.</summary>
    public required double NeighbourSpacingP5 { get; init; }

    /// <summary>Gets the mean depth, front to back, of the outfield players of the side with the ball.</summary>
    public required double InPossessionDepth { get; init; }

    /// <summary>Gets the mean width of the outfield players of the side with the ball.</summary>
    public required double InPossessionWidth { get; init; }

    /// <summary>Gets the mean depth of the outfield players of the side without the ball.</summary>
    public required double OutOfPossessionDepth { get; init; }

    /// <summary>Gets the mean width of the outfield players of the side without the ball.</summary>
    public required double OutOfPossessionWidth { get; init; }

    /// <summary>Gets the mean distance from its own goal line of the back line of the side without the ball.</summary>
    public required double BackLineDepth { get; init; }

    /// <summary>Gets the mean distance from its own goal line of the front line of the side without the ball.</summary>
    public required double FrontLineDepth { get; init; }

    /// <summary>Gets how many steps had the ball in the own third of the side without it.</summary>
    public required int DeepSamples { get; init; }

    /// <summary>Gets, in those steps, the mean number of its outfield players within thirty metres of its own goal line.</summary>
    public required double DeepBlockOutfield { get; init; }

    /// <summary>Gets, in those steps, the mean distance of its back line from its own goal line.</summary>
    public required double DeepBlockBackLine { get; init; }

    /// <summary>Gets how many steps had the ball inside an eighteen-yard box.</summary>
    public required int BoxSamples { get; init; }

    /// <summary>Gets, in those steps, the median number of outfield players inside that box.</summary>
    public required double InBoxP50 { get; init; }

    /// <summary>Gets, in those steps, the ninety-fifth percentile of the number of outfield players inside that box.</summary>
    public required double InBoxP95 { get; init; }

    /// <summary>Gets, in those steps, the ninety-fifth percentile of the number of outfield players inside its six-yard box.</summary>
    public required double SixYardP95 { get; init; }
}
