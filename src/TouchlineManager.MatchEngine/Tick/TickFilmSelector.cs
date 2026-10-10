using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>A stretch of the recording the film shows: the frames from <see cref="Start"/> to <see cref="End"/>, both inclusive.</summary>
/// <param name="Start">The first frame.</param>
/// <param name="End">The last frame.</param>
internal readonly record struct TickFilmWindow(int Start, int End)
{
    /// <summary>Gets how many frames the window covers.</summary>
    public int Length => End - Start + 1;
}

/// <summary>
/// Chooses which stretches of a tick match the film shows (`tick-engine-v1`, Milestone 8).
/// </summary>
/// <remarks>
/// <para>
/// The recording is a ninety-minute match and the film is about eighteen minutes played at twice real speed, so about a third of the
/// match is shown, and the question is which third. The answer follows what a viewer came for. Both kick-offs and the final whistle
/// are always shown. Every goal is shown as the whole move that made it, from the moment its side won the ball
/// (<see cref="TickMoveFinder"/>), and the celebration after it; so is every penalty awarded and every good chance. What room is left
/// goes to the best chances by their goal probability, then to corners and free kicks in range, then to offsides (a booking is never chosen for itself), and
/// last, if the film is still short of its length, to the spells in which a side camped in the other's final third.
/// </para>
/// <para>
/// Stretches that overlap or lie within ten seconds of each other are one stretch, a stretch never crosses the interval, and the
/// chosen total never exceeds <see cref="HighlightOptionsV1.TickMaxFilmMilliseconds"/>. When it would, the moves behind the chances are
/// cut back to fifteen seconds first, and only then are whole stretches dropped; a goal and a red card are never dropped. The choice is
/// a pure function of the recording and the event log, so a replay is the same on every machine.
/// </para>
/// </remarks>
internal static class TickFilmSelector
{
    /// <summary>The play shown before a chance whose move is not looked for, and the least shown before one whose move is, in frames (15 s).</summary>
    public const int ShotLead = 150;

    /// <summary>The least play shown before a goal, in frames (18 s): a move shorter than that is shown with the play before it, never less than the film once showed.</summary>
    public const int GoalLead = 180;

    /// <summary>The play after a goal that is shown, in frames: the celebration (7 s).</summary>
    public const int GoalAfter = 70;

    /// <summary>The frames from each half's first that are always shown: the kick-off and the first passes (16 s).</summary>
    public const int KickOffFrames = 160;

    /// <summary>The frames before the final whistle that are always shown (10 s).</summary>
    public const int FinalFrames = 100;

    /// <summary>Stretches closer than this (10 s) are joined, so two moves a few seconds apart are one run of play and not two cuts.</summary>
    public const int JoinGap = 100;

    /// <summary>The longest a filler stretch is, in frames (40 s of play, 20 s of film): fewer, longer stretches mean fewer cuts.</summary>
    public const int FillerFrames = 400;

    /// <summary>
    /// The frames the selection aims over the film's floor (20 s of film): the film loses a frame at every cut, and the stretches the
    /// passages cannot make into one lose a few more, so a film aimed at exactly the floor ends a few seconds under it.
    /// </summary>
    public const int FloorMargin = 400;

    /// <summary>
    /// One thing worth showing: where it is, how much play around it, and how much it is worth. <c>ShortBefore</c> is the lead the
    /// moment is cut back to when the film is over its ceiling; it is <c>Before</c> for a moment that is never cut back.
    /// </summary>
    private readonly record struct Moment(int Frame, int Before, int After, int Rank, int Weight, int ShortBefore)
    {
        public Moment(int frame, int before, int after, int rank, int weight)
            : this(frame, before, after, rank, weight, before)
        {
        }
    }

    /// <summary>Picks the stretches the film shows.</summary>
    /// <param name="recording">The continuous trace.</param>
    /// <param name="events">The match's events.</param>
    /// <param name="options">The film's length and pace.</param>
    /// <returns>The stretches, in frame order, none overlapping and none crossing the interval.</returns>
    public static IReadOnlyList<TickFilmWindow> Select(
        TickMatchRecording recording,
        IReadOnlyList<EngineEventV1> events,
        HighlightOptionsV1 options)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(options);

        if (recording.FrameCount == 0)
        {
            return [];
        }

        var msPerFrame = MillisecondsPerFrame(options);
        var minFrames = (options.TickMinFilmMilliseconds / msPerFrame) + FloorMargin;
        var maxFrames = (options.TickMaxFilmMilliseconds / msPerFrame) - HalfTimeFilmFrames(options);
        var targetFrames = Math.Min(maxFrames, Math.Max(minFrames, (minFrames + maxFrames) / 2));
        var last = recording.FrameCount - 1;
        var secondHalf = recording.SecondHalfFrame;

        var chosen = new List<TickFilmWindow>();

        // The kick-offs and the final whistle: the film opens on one and closes on the other.
        Add(chosen, new TickFilmWindow(0, Math.Min(last, KickOffFrames)), recording);

        if (secondHalf > 0)
        {
            Add(chosen, new TickFilmWindow(secondHalf, Math.Min(last, secondHalf + KickOffFrames)), recording);
        }

        Add(chosen, new TickFilmWindow(Math.Max(0, last - FinalFrames), last), recording);

        var frameOf = new Dictionary<int, int>(recording.Events.Count);

        foreach (var stamp in recording.Events)
        {
            frameOf[stamp.Sequence] = Math.Min(stamp.Frame, last);
        }

        var moments = MomentsOf(recording, events, frameOf, options);
        var kept = new List<Moment>();
        var fixedCount = chosen.Count;

        foreach (var moment in moments.OrderBy(moment => moment.Rank).ThenByDescending(moment => moment.Weight).ThenBy(moment => moment.Frame))
        {
            var window = WindowOf(moment, moment.Before, last);

            // A goal and a red card are always shown; the rest wait their turn for the room.
            if (moment.Rank > 1 && Total(chosen, recording) + Added(chosen, window, recording) > targetFrames)
            {
                continue;
            }

            Add(chosen, window, recording);
            kept.Add(moment);
        }

        var fillFrom = chosen.Count;

        if (Total(chosen, recording) < minFrames)
        {
            Fill(chosen, recording, minFrames, last);
        }

        if (Total(chosen, recording) > maxFrames)
        {
            // Over the ceiling: the moves behind the chances are cut back first, and every stretch stays.
            var fillers = chosen.Skip(fillFrom).ToList();

            chosen = [.. chosen.Take(fixedCount)];

            foreach (var moment in kept)
            {
                Add(chosen, WindowOf(moment, moment.ShortBefore, last), recording);
            }

            chosen.AddRange(fillers);
        }

        Trim(chosen, recording, maxFrames, moments);

        return Merge(chosen, recording);
    }

    private static TickFilmWindow WindowOf(Moment moment, int before, int last) =>
        new(Math.Max(0, moment.Frame - before), Math.Min(last, moment.Frame + moment.After));

    /// <summary>Gets the film time one frame lasts, in milliseconds.</summary>
    /// <param name="options">The film's pace.</param>
    public static int MillisecondsPerFrame(HighlightOptionsV1 options) =>
        Math.Max(1, TickSpatialUnits.TickDeltaMs * 1_000 / Math.Max(1, options.TickFilmPaceMilli));

    private static int HalfTimeFilmFrames(HighlightOptionsV1 options) =>
        (int)(options.HalfTimeHoldSeconds * 1_000 / MillisecondsPerFrame(options));

    private static List<Moment> MomentsOf(
        TickMatchRecording recording,
        IReadOnlyList<EngineEventV1> events,
        Dictionary<int, int> frameOf,
        HighlightOptionsV1 options)
    {
        var moments = new List<Moment>();

        foreach (var matchEvent in events)
        {
            if (!frameOf.TryGetValue(matchEvent.Sequence, out var frame))
            {
                continue;
            }

            var quality = matchEvent.QualityBasisPoints ?? 500;

            switch (matchEvent.Type)
            {
                case EngineEventType.Goal:
                case EngineEventType.PenaltyGoal:
                    moments.Add(new Moment(frame, MoveLead(recording, matchEvent, frame, GoalLead), GoalAfter, 0, 10_000));
                    break;

                case EngineEventType.RedCard:
                case EngineEventType.SecondYellowCard:
                    moments.Add(new Moment(frame, 60, 50, 1, 9_000));
                    break;

                case EngineEventType.PenaltyAwarded:
                    moments.Add(new Moment(frame, MoveLead(recording, matchEvent, frame, TickMoveFinder.MinLead), 140, 1, 9_500));
                    break;

                case EngineEventType.PenaltyMissed:
                    moments.Add(new Moment(frame, 40, 60, 1, 9_000));
                    break;

                case EngineEventType.Woodwork:
                case EngineEventType.ShotSaved:
                case EngineEventType.ShotBlocked:
                case EngineEventType.ShotOffTarget:
                case EngineEventType.FreeKickShot:
                    {
                        // A good chance is shown as the move that made it; a poor one, and a dead-ball strike, with the fifteen seconds before.
                        var whole = matchEvent.Type != EngineEventType.FreeKickShot && quality >= options.MinQualityForShotBasisPoints;
                        var before = whole ? MoveLead(recording, matchEvent, frame, ShotLead) : ShotLead;

                        moments.Add(new Moment(
                            frame,
                            before,
                            40,
                            2,
                            quality + (matchEvent.Type == EngineEventType.Woodwork ? 1_000 : 0),
                            ShotLead));
                    }

                    break;

                case EngineEventType.Corner:
                    moments.Add(new Moment(frame, 20, 130, 3, 600));
                    break;

                case EngineEventType.FreeKickWon:
                    moments.Add(new Moment(frame, 20, 120, 3, 700));
                    break;

                case EngineEventType.Offside:
                    moments.Add(new Moment(frame, 50, 20, 4, 100));
                    break;

                default:
                    break;
            }
        }

        return moments;
    }

    /// <summary>Gets how many frames before an event its move began, so the film opens on the side winning the ball.</summary>
    private static int MoveLead(TickMatchRecording recording, EngineEventV1 matchEvent, int frame, int minLead) =>
        frame - TickMoveFinder.StartOf(recording, frame, (int)matchEvent.Side, minLead);

    // ---- Windows ---------------------------------------------------------------------------------------------------------------------

    /// <summary>Adds a window, clipped to the half it starts in so that no stretch crosses the interval.</summary>
    private static void Add(List<TickFilmWindow> chosen, TickFilmWindow window, TickMatchRecording recording)
    {
        var clipped = Clip(window, recording);

        if (clipped.Length > 0)
        {
            chosen.Add(clipped);
        }
    }

    private static TickFilmWindow Clip(TickFilmWindow window, TickMatchRecording recording)
    {
        var secondHalf = recording.SecondHalfFrame;

        if (secondHalf <= 0)
        {
            return window with { Start = Math.Max(0, window.Start), End = Math.Min(recording.FrameCount - 1, window.End) };
        }

        // The window belongs to the half its centre is in.
        var centre = (window.Start + window.End) / 2;
        var from = centre >= secondHalf ? secondHalf : 0;
        var to = centre >= secondHalf ? recording.FrameCount - 1 : secondHalf - 1;

        return new TickFilmWindow(Math.Max(from, window.Start), Math.Min(to, window.End));
    }

    private static int Total(List<TickFilmWindow> chosen, TickMatchRecording recording) =>
        Merge(chosen, recording).Sum(window => window.Length);

    /// <summary>Gets how many frames a window would add to the stretches already chosen.</summary>
    private static int Added(List<TickFilmWindow> chosen, TickFilmWindow window, TickMatchRecording recording)
    {
        var before = Total(chosen, recording);
        var with = new List<TickFilmWindow>(chosen);

        Add(with, window, recording);

        return Total(with, recording) - before;
    }

    /// <summary>Joins stretches that overlap or lie within <see cref="JoinGap"/> of each other.</summary>
    private static List<TickFilmWindow> Merge(List<TickFilmWindow> windows, TickMatchRecording recording)
    {
        var ordered = windows.OrderBy(window => window.Start).ThenBy(window => window.End).ToList();
        var merged = new List<TickFilmWindow>();
        var secondHalf = recording.SecondHalfFrame;

        foreach (var window in ordered)
        {
            if (merged.Count > 0)
            {
                var previous = merged[^1];
                var sameHalf = secondHalf <= 0 || (previous.End < secondHalf) == (window.Start < secondHalf);

                if (sameHalf && window.Start - previous.End <= JoinGap)
                {
                    merged[^1] = previous with { End = Math.Max(previous.End, window.End) };

                    continue;
                }
            }

            merged.Add(window);
        }

        return merged;
    }

    /// <summary>
    /// Tops a short film up with the spells in which one end of the pitch saw the most of the ball: long stretches first, so there are few
    /// cuts, then shorter ones into the gaps the long ones left, if the film is still short.
    /// </summary>
    private static void Fill(List<TickFilmWindow> chosen, TickMatchRecording recording, int minFrames, int last)
    {
        for (var size = FillerFrames; size >= FillerFrames / 4 && Total(chosen, recording) < minFrames; size /= 2)
        {
            FillWith(chosen, recording, minFrames, last, size);
        }
    }

    private static void FillWith(List<TickFilmWindow> chosen, TickMatchRecording recording, int minFrames, int last, int size)
    {
        var scores = new List<(int Start, int Score)>();

        for (var start = 0; start + size <= last; start += size / 2)
        {
            var score = 0;

            for (var frame = start; frame < start + size; frame += 5)
            {
                var x = recording.BallX(frame);

                if (x < 2_500 || x > 7_500)
                {
                    score++;
                }
            }

            scores.Add((start, score));
        }

        foreach (var (start, _) in scores.OrderByDescending(entry => entry.Score).ThenBy(entry => entry.Start))
        {
            if (Total(chosen, recording) >= minFrames)
            {
                return;
            }

            var window = new TickFilmWindow(start, start + size - 1);

            if (Merge(chosen, recording).Any(existing => existing.Start <= window.End && window.Start <= existing.End))
            {
                continue;
            }

            Add(chosen, window, recording);
        }
    }

    /// <summary>Drops the least valuable stretches until the film fits its ceiling; goals and red cards are never dropped.</summary>
    private static void Trim(List<TickFilmWindow> chosen, TickMatchRecording recording, int maxFrames, List<Moment> moments)
    {
        while (Total(chosen, recording) > maxFrames)
        {
            var merged = Merge(chosen, recording);
            var worst = -1;
            var worstValue = int.MaxValue;

            for (var index = 0; index < merged.Count; index++)
            {
                var window = merged[index];
                var value = 0;
                var protectedWindow = window.Start == 0 || window.Start == recording.SecondHalfFrame || window.End == recording.FrameCount - 1;

                foreach (var moment in moments)
                {
                    if (moment.Frame < window.Start || moment.Frame > window.End)
                    {
                        continue;
                    }

                    value += moment.Rank <= 1 ? int.MaxValue / 4 : moment.Weight;
                }

                if (!protectedWindow && value < worstValue)
                {
                    worstValue = value;
                    worst = index;
                }
            }

            if (worst < 0 || worstValue >= int.MaxValue / 4)
            {
                return;
            }

            var dropped = merged[worst];

            chosen.RemoveAll(window => window.Start >= dropped.Start && window.End <= dropped.End);
        }
    }
}
