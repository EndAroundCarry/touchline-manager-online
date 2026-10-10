using System.Globalization;
using TouchlineManager.Application.Match;
using TouchlineManager.Domain.Squad;
using TouchlineManager.MatchEngine;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.SimulationBenchmarks;

/// <summary>
/// The tick motion probe (`tick-film-v1`, Milestone 0): puts a number on how the tick engine's players and ball move and how much of
/// a move its highlights show.
/// </summary>
/// <remarks>
/// <para>
/// Everything is read from the continuous recording the loop already writes, so the probe changes no play. Speeds are the distance
/// between two consecutive frames (a pitch unit is a centimetre and a frame a tenth of a second, so a unit a frame is 0.1 m/s). The
/// figures answer the five complaints in the plan: players that run and stop, a ball that crawls in and sits at a receiver's feet,
/// opponents that stand on top of each other, and highlights that start after the move did.
/// </para>
/// <para>
/// A pair of players "in a duel" is not known to the recording, so a pair is excused when either man has the ball or either stands
/// within three metres of it: that covers a presser against a carrier and a chaser on a loose ball.
/// </para>
/// </remarks>
internal static class TickMotionProbe
{
    private const int Entities = TickMatchRecording.Entities;
    private const int Half = Entities / 2;

    /// <summary>A player is standing when slower than this, in metres per second.</summary>
    private const double StandingSpeed = 0.3;

    /// <summary>A player has stopped when slower than this, in metres per second.</summary>
    private const double StoppedSpeed = 0.5;

    /// <summary>A player has set off when faster than this, in metres per second.</summary>
    private const double GoingSpeed = 2.0;

    /// <summary>How soon after a stop a set-off counts as stop-and-go, in frames (1.5 s).</summary>
    private const int StopGoFrames = 15;

    /// <summary>How many outfielders stopped at once make a synchronised stop.</summary>
    private const int SynchronisedStop = 12;

    /// <summary>The distance below which two opponents stand on each other, in pitch units (1.0 m).</summary>
    private const int OverlapUnits = 100;

    /// <summary>Both men of a pair are sitting together when each moves less than this a frame, in pitch units (1.5 m/s).</summary>
    private const int SittingSpeedUnits = 15;

    /// <summary>How near the ball a pair is excused as a duel, in pitch units (3 m).</summary>
    private const int DuelUnits = 300;

    /// <summary>The ball is slow when it rolls under this, in pitch units a frame (4 m/s).</summary>
    private const int SlowBallUnits = 40;

    /// <summary>How long a slow ball before a reception makes a slow tail, in frames (more than 1 s).</summary>
    private const int SlowTailFrames = 10;

    /// <summary>A step longer than this in one frame is a teleport (15 m), not running.</summary>
    private const int TeleportUnits = 1_500;

    /// <summary>A player who has not stopped, as a frame far enough back that no set-off is near it.</summary>
    private const int Never = -1_000_000;

    /// <summary>How many frames of recorded play stand for ninety minutes.</summary>
    private const int NinetyMinuteFrames = 54_000;

    private static readonly FormationPreset[] Shapes =
    [
        FormationPreset.FourFourTwo,
        FormationPreset.FourThreeThree,
        FormationPreset.FourTwoThreeOne,
        FormationPreset.FiveThreeTwo,
    ];

    /// <summary>Runs the probe.</summary>
    /// <param name="matches">How many matches to play.</param>
    /// <param name="seed">The base seed.</param>
    /// <param name="rules">The rules in force.</param>
    public static void Run(int matches, ulong seed, EngineRulesV2 rules)
    {
        Console.WriteLine($"== Tick motion, {matches:N0} matches ==");

        var tally = new Tally();

        for (var index = 0; index < matches; index++)
        {
            var input = TickCalibration.Stand(
                LaboratoryFixtures.EvenlyMatched(seed + (ulong)index, rules),
                Shapes[index % Shapes.Length],
                Shapes[(index / Shapes.Length) % Shapes.Length]);
            var recorder = new MatchPassageRecorder();
            var result = MatchSimulator.Simulate(input, rules, new PlayerLiveMetricsRecorder(), recorder, TickMatchEngine.Instance);
            var recording = recorder.Tick;

            if (recording is null)
            {
                continue;
            }

            Space(recording, tally);
            Motion(recording, tally);
            Dips(recording, tally);
            Receptions(recording, tally);
            Overlap(recording, tally);
            tally.EnginePasses += result.PlayerLines.Sum(line => line.PassesAttempted);
            tally.EngineCompleted += result.PlayerLines.Sum(line => line.PassesCompleted);
            Film(recording, result, tally);

            foreach (var stamp in recording.Actions)
            {
                tally.Stamps[stamp.Action] = tally.Stamps.GetValueOrDefault(stamp.Action) + 1;

                if (stamp.Action.IsBlame())
                {
                    tally.Blame[stamp.Action] = tally.Blame.GetValueOrDefault(stamp.Action) + 1;
                }
            }

            tally.Matches++;
        }

        Report(tally);
    }

    // ---- Space ------------------------------------------------------------------------------------------------------------------------

    /// <summary>How much room a man has when he takes the ball and when he shoots: the distance to the nearest opponent, in metres.</summary>
    private static void Space(TickMatchRecording recording, Tally tally)
    {
        foreach (var stamp in recording.Actions)
        {
            if (stamp.Entity < 0 || stamp.Frame >= recording.FrameCount || recording.State(stamp.Frame) != TickPlayState.OpenPlay)
            {
                continue;
            }

            if (stamp.Action is not (PassageAction.Receive or PassageAction.Shot))
            {
                continue;
            }

            var side = stamp.Entity / Half;
            var x = recording.PlayerX(stamp.Frame, stamp.Entity);
            var y = recording.PlayerY(stamp.Frame, stamp.Entity);
            var nearest = double.MaxValue;
            var markers = 0;

            for (var other = (1 - side) * Half; other < ((1 - side) * Half) + Half; other++)
            {
                if (recording.PlayerX(stamp.Frame, other) < 0 || other % Half == 0)
                {
                    continue;
                }

                var dx = recording.PlayerX(stamp.Frame, other) - x;
                var dy = recording.PlayerY(stamp.Frame, other) - y;
                var distance = Math.Sqrt((dx * (double)dx) + (dy * (double)dy)) / 100.0;

                nearest = Math.Min(nearest, distance);
                markers += distance < 5.0 ? 1 : 0;
            }

            // Into the opponent's half, in the side's own point of view: home attacks towards high X.
            var attackingX = side == 0 ? x : 10_000 - x;
            var advanced = attackingX > 7_000;

            if (stamp.Action == PassageAction.Shot)
            {
                tally.ShotSpace.Add(nearest);
                tally.ShotMarkers.Add(markers);
            }
            else
            {
                tally.ReceiveSpace.Add(nearest);

                if (advanced)
                {
                    tally.FinalThirdReceiveSpace.Add(nearest);
                }
            }
        }
    }

    // ---- Movement ---------------------------------------------------------------------------------------------------------------------

    private static void Motion(TickMatchRecording recording, Tally tally)
    {
        var lastStop = new int[Entities];
        var distance = 0.0;
        var counted = 0;

        Array.Fill(lastStop, Never);

        for (var frame = 1; frame < recording.FrameCount; frame++)
        {
            var now = recording.State(frame);
            var open = now == TickPlayState.OpenPlay && recording.State(frame - 1) == TickPlayState.OpenPlay;
            var placed = recording.Flags(frame) != TickFrameFlags.None;

            if (now != TickPlayState.HalfTime && !placed)
            {
                counted++;
            }

            var stoppedNow = 0;

            for (var entity = 0; entity < Entities; entity++)
            {
                if (entity % Half == 0 || recording.PlayerX(frame, entity) < 0 || recording.PlayerX(frame - 1, entity) < 0)
                {
                    continue;
                }

                var step = Step(recording, frame, entity);

                if (step > TeleportUnits || placed)
                {
                    lastStop[entity] = Never;

                    continue;
                }

                if (now != TickPlayState.HalfTime)
                {
                    distance += step / 100.0;
                }

                if (!open)
                {
                    lastStop[entity] = Never;

                    continue;
                }

                var speed = step / 10.0;

                tally.PlayerFrames++;

                if (speed < StandingSpeed)
                {
                    tally.StandingFrames++;
                }

                if (speed < StoppedSpeed)
                {
                    stoppedNow++;
                    lastStop[entity] = frame;
                }
                else if (speed > GoingSpeed && frame - lastStop[entity] <= StopGoFrames)
                {
                    tally.StopGo++;
                    lastStop[entity] = Never;
                }
            }

            if (open)
            {
                tally.OpenFrames++;

                if (stoppedNow >= SynchronisedStop)
                {
                    tally.SynchronisedFrames++;
                }
            }
        }

        if (counted > 0)
        {
            // Twenty outfielders; scaled from the frames recorded to ninety minutes of them.
            tally.KilometresPer90.Add(distance / 20.0 / 1_000.0 * NinetyMinuteFrames / counted);
        }
    }

    /// <summary>
    /// Counts the dips in a man's speed: one frame under 35% of the speed he keeps over the three either side, which he then recovers
    /// within four frames. It is what the viewer shows as a stutter, read from the engine's own motion.
    /// </summary>
    private static void Dips(TickMatchRecording recording, Tally tally)
    {
        var speeds = new int[recording.FrameCount];

        for (var entity = 0; entity < Entities; entity++)
        {
            if (entity % Half == 0)
            {
                continue;
            }

            for (var frame = 0; frame < speeds.Length; frame++)
            {
                var valid = frame > 0
                    && recording.State(frame) == TickPlayState.OpenPlay
                    && recording.State(frame - 1) == TickPlayState.OpenPlay
                    && recording.Flags(frame) == TickFrameFlags.None
                    && recording.PlayerX(frame, entity) >= 0
                    && recording.PlayerX(frame - 1, entity) >= 0;
                var step = valid ? Step(recording, frame, entity) : -1;

                speeds[frame] = step > TeleportUnits ? -1 : step;
            }

            for (var frame = 4; frame < speeds.Length - 5; frame++)
            {
                if (speeds[frame] < 0)
                {
                    continue;
                }

                var sum = 0;
                var valid = true;

                for (var near = -3; near <= 4 && valid; near++)
                {
                    if (speeds[frame + near] < 0)
                    {
                        valid = false;
                    }
                    else if (near is >= -3 and <= 3 && near != 0)
                    {
                        sum += speeds[frame + near];
                    }
                }

                if (!valid)
                {
                    continue;
                }

                var keeps = sum / 6;
                var recovers = Math.Max(Math.Max(speeds[frame + 1], speeds[frame + 2]), Math.Max(speeds[frame + 3], speeds[frame + 4]));

                if (keeps >= 15 && speeds[frame] * 100 < keeps * 35 && recovers * 100 >= keeps * 70)
                {
                    tally.Dips++;
                    frame += 4;
                }
            }
        }
    }

    private static int Step(TickMatchRecording recording, int frame, int entity)
    {
        var dx = recording.PlayerX(frame, entity) - recording.PlayerX(frame - 1, entity);
        var dy = recording.PlayerY(frame, entity) - recording.PlayerY(frame - 1, entity);

        return (int)Math.Sqrt((dx * dx) + (dy * (long)dy));
    }

    private static int BallStep(TickMatchRecording recording, int frame)
    {
        var dx = recording.BallX(frame) - recording.BallX(frame - 1);
        var dy = recording.BallY(frame) - recording.BallY(frame - 1);

        return (int)Math.Sqrt((dx * dx) + (dy * (long)dy));
    }

    // ---- Receptions -------------------------------------------------------------------------------------------------------------------

    private static bool IsKick(PassageAction action) => action is PassageAction.Pass or PassageAction.Cross;

    private static bool IsRelease(PassageAction action) =>
        action is PassageAction.Pass or PassageAction.Cross or PassageAction.Shot or PassageAction.FreeKick or PassageAction.Penalty;

    /// <summary>Whether an action ends a ball's flight, or a man's time with it.</summary>
    private static bool EndsFlight(PassageAction action) =>
        action is PassageAction.Receive or PassageAction.Interception or PassageAction.Save or PassageAction.Pass
            or PassageAction.Cross or PassageAction.Shot or PassageAction.FreeKick or PassageAction.Penalty;

    private static bool EndsHold(PassageAction action) => EndsFlight(action) || action == PassageAction.Tackle;

    private static void Receptions(TickMatchRecording recording, Tally tally)
    {
        var actions = recording.Actions;

        for (var index = 0; index < actions.Count; index++)
        {
            var kick = actions[index];

            if (!IsKick(kick.Action) || kick.Frame >= recording.FrameCount || recording.State(kick.Frame) != TickPlayState.OpenPlay)
            {
                continue;
            }

            tally.Passes++;

            // The next thing that ends the ball's flight: a reception by the passer's side is a completed pass.
            TickActionStamp? next = null;

            for (var after = index + 1; after < actions.Count; after++)
            {
                if (EndsFlight(actions[after].Action))
                {
                    next = actions[after];

                    break;
                }
            }

            if (next is not { Action: PassageAction.Receive } reception
                || reception.Entity / Half != kick.Entity / Half
                || reception.Entity == kick.Entity
                || reception.Frame >= recording.FrameCount)
            {
                continue;
            }

            var arrival = reception.Frame;
            var flight = arrival - kick.Frame;

            tally.Completed++;
            tally.FlightFrames.Add(flight);

            if (flight >= 3)
            {
                tally.ArrivalSpeeds.Add(BallStep(recording, arrival - 1) / 10.0);

                var bx = recording.BallX(arrival - 1) - recording.BallX(arrival - 2);
                var by = recording.BallY(arrival - 1) - recording.BallY(arrival - 2);
                var length = Math.Sqrt((bx * (double)bx) + (by * (double)by));

                if (length > 0)
                {
                    var rx = recording.PlayerX(arrival - 1, reception.Entity) - recording.PlayerX(arrival - 2, reception.Entity);
                    var ry = recording.PlayerY(arrival - 1, reception.Entity) - recording.PlayerY(arrival - 2, reception.Entity);

                    // Toward the ball is against the way it travels.
                    tally.TowardBall.Add(-((rx * bx) + (ry * by)) / length / 10.0);
                }

                var slow = 0;

                for (var frame = arrival - 1; frame > kick.Frame && BallStep(recording, frame) < SlowBallUnits; frame--)
                {
                    slow++;
                }

                tally.Receptions++;

                if (slow > SlowTailFrames)
                {
                    tally.SlowTails++;
                }
            }

            // How long he keeps it: to his next release, unless something else ends his time with the ball first.
            for (var after = index + 1; after < actions.Count; after++)
            {
                var stamp = actions[after];

                if (stamp.Frame <= arrival && stamp.Action != PassageAction.Receive)
                {
                    continue;
                }

                if (stamp.Frame == arrival && stamp.Action == PassageAction.Receive && stamp.Entity == reception.Entity)
                {
                    continue;
                }

                if (!EndsHold(stamp.Action))
                {
                    continue;
                }

                if (stamp.Entity == reception.Entity && IsRelease(stamp.Action))
                {
                    tally.HoldFrames.Add(stamp.Frame - arrival);
                }

                break;
            }
        }
    }

    // ---- Overlap ----------------------------------------------------------------------------------------------------------------------

    private static void Overlap(TickMatchRecording recording, Tally tally)
    {
        for (var frame = 0; frame < recording.FrameCount; frame++)
        {
            if (recording.State(frame) != TickPlayState.OpenPlay)
            {
                continue;
            }

            var controller = recording.Controller(frame);
            var ballX = recording.BallX(frame);
            var ballY = recording.BallY(frame);
            var any = false;
            var outsideDuels = false;
            var sitting = false;

            for (var home = 0; home < Half; home++)
            {
                var hx = recording.PlayerX(frame, home);

                if (hx < 0)
                {
                    continue;
                }

                var hy = recording.PlayerY(frame, home);

                for (var away = Half; away < Entities; away++)
                {
                    var ax = recording.PlayerX(frame, away);

                    if (ax < 0)
                    {
                        continue;
                    }

                    var dx = hx - ax;
                    var dy = hy - recording.PlayerY(frame, away);

                    if ((dx * dx) + (dy * dy) >= OverlapUnits * OverlapUnits)
                    {
                        continue;
                    }

                    any = true;

                    var excused = home == controller
                        || away == controller
                        || NearBall(hx, hy, ballX, ballY)
                        || NearBall(ax, recording.PlayerY(frame, away), ballX, ballY);

                    if (!excused)
                    {
                        outsideDuels = true;
                        tally.OverlapPairs++;

                        if (frame > 0 && Step(recording, frame, home) < SittingSpeedUnits && Step(recording, frame, away) < SittingSpeedUnits)
                        {
                            sitting = true;
                        }
                    }
                }
            }

            tally.OverlapAnyFrames += any ? 1 : 0;
            tally.OverlapFrames += outsideDuels ? 1 : 0;
            tally.SittingFrames += sitting ? 1 : 0;
            tally.OpenFramesForOverlap++;
        }
    }

    private static bool NearBall(int x, int y, int ballX, int ballY)
    {
        var dx = x - ballX;
        var dy = y - ballY;

        return (dx * dx) + (dy * dy) < DuelUnits * DuelUnits;
    }

    // ---- Film -------------------------------------------------------------------------------------------------------------------------

    private static void Film(TickMatchRecording recording, MatchResultV1 result, Tally tally)
    {
        var windows = TickFilmSelector.Select(recording, result.Events, new HighlightOptionsV1());
        var content = TickFilmSelector.Select(recording, result.Events, new HighlightOptionsV1 { TickMinFilmMilliseconds = 0 });

        tally.FilmSeconds.Add(windows.Sum(window => window.Length) / 20.0);
        tally.ContentSeconds.Add(content.Sum(window => window.Length) / 20.0);
        tally.FilmFrames += windows.Sum(window => window.Length);

        foreach (var stamp in recording.Actions)
        {
            if (stamp.Action.IsBlame() && windows.Any(window => window.Start <= stamp.Frame && stamp.Frame <= window.End))
            {
                tally.BlameInFilm[stamp.Action] = tally.BlameInFilm.GetValueOrDefault(stamp.Action) + 1;
            }
        }

        var possession = PossessionOf(recording);
        var frameOf = recording.Events.ToDictionary(stamp => stamp.Sequence, stamp => Math.Min(stamp.Frame, recording.FrameCount - 1));

        foreach (var matchEvent in result.Events)
        {
            var isGoal = matchEvent.Type == EngineEventType.Goal;
            var isShot = matchEvent.Type is EngineEventType.ShotSaved or EngineEventType.ShotOffTarget or EngineEventType.ShotBlocked
                or EngineEventType.Woodwork;

            if ((!isGoal && !isShot) || !frameOf.TryGetValue(matchEvent.Sequence, out var frame))
            {
                continue;
            }

            var side = (int)matchEvent.Side;
            var strike = StrikeOf(recording, side, frame);
            var start = RegainOf(recording, possession, side, strike);
            var lead = (strike - start) / 10.0;

            var window = windows.FirstOrDefault(candidate => candidate.Start <= frame && frame <= candidate.End);

            if (window.Length == 0)
            {
                continue;
            }

            var slack = (start - window.Start) / 10.0;

            (isGoal ? tally.GoalLeads : tally.ShotLeads).Add(lead);
            (isGoal ? tally.GoalSlacks : tally.ShotSlacks).Add(slack);
        }
    }

    /// <summary>The frame a side struck the shot an event is about: the event is emitted when the keeper or the net has dealt with it.</summary>
    private static int StrikeOf(TickMatchRecording recording, int side, int frame)
    {
        for (var index = recording.Actions.Count - 1; index >= 0; index--)
        {
            var stamp = recording.Actions[index];

            if (stamp.Frame > frame)
            {
                continue;
            }

            if (frame - stamp.Frame > 80)
            {
                break;
            }

            // A goal is stamped a second time when the ball crosses the line, in the celebration; the strike is the kick in open play.
            if (stamp.Entity >= 0
                && stamp.Entity / Half == side
                && stamp.Action is PassageAction.Shot or PassageAction.FreeKick or PassageAction.Penalty
                && recording.State(Math.Min(stamp.Frame, recording.FrameCount - 1)) == TickPlayState.OpenPlay)
            {
                return stamp.Frame;
            }
        }

        // No kick in open play (a goal stamped only as it crossed the line): the play up to the stoppage is the move.
        while (frame > 0 && recording.State(frame) != TickPlayState.OpenPlay)
        {
            frame--;
        }

        return frame;
    }

    /// <summary>
    /// Which side has the ball in every frame: the side of the man who controls it, and while it is loose the side of whoever acted
    /// last, so a pass in flight is still its passer's.
    /// </summary>
    private static int[] PossessionOf(TickMatchRecording recording)
    {
        var sides = new int[recording.FrameCount];
        var next = 0;
        var last = -1;

        for (var frame = 0; frame < sides.Length; frame++)
        {
            for (; next < recording.Actions.Count && recording.Actions[next].Frame <= frame; next++)
            {
                var stamp = recording.Actions[next];

                if (stamp.Entity >= 0 && stamp.Action is not (PassageAction.Run or PassageAction.Celebrate) && !stamp.Action.IsBlame())
                {
                    last = stamp.Entity / Half;
                }
            }

            var controller = recording.Controller(frame);

            sides[frame] = controller >= 0 ? controller / Half : last;
        }

        return sides;
    }

    /// <summary>
    /// The first frame of the unbroken spell in which a side had the ball up to a frame: walks back while the side has it, tolerating
    /// an opponent's touch of five frames, and stops at a stoppage.
    /// </summary>
    /// <remarks>This is the rule the film's move finder is to apply (Milestone 2), measured here before it exists.</remarks>
    private static int RegainOf(TickMatchRecording recording, int[] possession, int side, int from)
    {
        var start = from;
        var opponent = 0;

        for (var frame = from; frame >= 0; frame--)
        {
            if (recording.State(frame) != TickPlayState.OpenPlay)
            {
                break;
            }

            if (possession[frame] == side)
            {
                start = frame;
                opponent = 0;
            }
            else if (++opponent > 5)
            {
                break;
            }
        }

        return start;
    }

    // ---- Report -----------------------------------------------------------------------------------------------------------------------

    private static string Share(long part, long whole) =>
        (100.0 * part / Math.Max(1, whole)).ToString("F1", CultureInfo.InvariantCulture) + "%";

    private static double Percentile(List<double> values, double share)
    {
        if (values.Count == 0)
        {
            return double.NaN;
        }

        var sorted = values.OrderBy(value => value).ToArray();

        return sorted[Math.Min(sorted.Length - 1, (int)(share * sorted.Length))];
    }

    private static string Spread(List<double> values) =>
        values.Count == 0
            ? "n/a"
            : $"p10 {Percentile(values, 0.10):F1}   p50 {Percentile(values, 0.50):F1}   p90 {Percentile(values, 0.90):F1}   mean {values.Average():F1}";

    private static void Report(Tally tally)
    {
        var playerMinutes = tally.PlayerFrames / 10.0 / 60.0;

        Console.WriteLine("  -- movement (open play, outfield players)");
        Console.WriteLine($"  {"standing share",-34} {Share(tally.StandingFrames, tally.PlayerFrames),8}   speed under {StandingSpeed:F1} m/s");
        Console.WriteLine($"  {"speed dips per player-minute",-34} {tally.Dips / Math.Max(1, playerMinutes),8:F2}   a frame under 35% of the speed either side, then back to 70%");
        Console.WriteLine($"  {"stop-go per player-minute",-34} {tally.StopGo / Math.Max(1, playerMinutes),8:F2}   under {StoppedSpeed:F1}, then over {GoingSpeed:F0} m/s within 1.5 s");
        Console.WriteLine($"  {"synchronised stops",-34} {Share(tally.SynchronisedFrames, tally.OpenFrames),8}   frames with {SynchronisedStop}+ outfielders under {StoppedSpeed:F1} m/s");
        Console.WriteLine($"  {"distance per player per 90 (km)",-34} {tally.KilometresPer90.DefaultIfEmpty(0).Average(),8:F2}   aim 9.5 - 11.5");

        Console.WriteLine("  -- receiving");
        Console.WriteLine($"  {"pass-stamped kicks per match",-34} {tally.Passes / Math.Max(1.0, tally.Matches),8:F0}   completed {Share(tally.Completed, tally.Passes)}");
        Console.WriteLine($"  {"the engine's own count",-34} {tally.EnginePasses / Math.Max(1.0, tally.Matches),8:F0}   completed {Share(tally.EngineCompleted, tally.EnginePasses)}");
        Console.WriteLine($"  {"ball speed at reception (m/s)",-34} {Spread(tally.ArrivalSpeeds)}");
        Console.WriteLine($"  {"receiver toward ball (m/s)",-34} {Spread(tally.TowardBall)}   moving toward it in {Share(tally.TowardBall.Count(value => value > 0), tally.TowardBall.Count)}");
        Console.WriteLine($"  {"kick to reception (frames)",-34} {Spread(tally.FlightFrames.ConvertAll(frames => (double)frames))}");
        Console.WriteLine($"  {"hold, reception to release (frames)",-34} {Spread(tally.HoldFrames.ConvertAll(frames => (double)frames))}");
        Console.WriteLine($"  {"one-touch plays",-34} {Share(tally.HoldFrames.Count(frames => frames <= 2), tally.HoldFrames.Count),8}   held 2 frames or fewer");
        Console.WriteLine($"  {"slow tail before reception",-34} {Share(tally.SlowTails, tally.Receptions),8}   ball under 4 m/s for over 1 s");

        Console.WriteLine("  -- room (metres to the nearest opponent)");
        Console.WriteLine($"  {"on receiving",-34} {Spread(tally.ReceiveSpace)}");
        Console.WriteLine($"  {"on receiving, final third",-34} {Spread(tally.FinalThirdReceiveSpace)}   {tally.FinalThirdReceiveSpace.Count / Math.Max(1.0, tally.Matches):F0} a match");
        Console.WriteLine($"  {"on shooting",-34} {Spread(tally.ShotSpace)}   markers within 5 m {tally.ShotMarkers.DefaultIfEmpty(0).Average():F2}");

        Console.WriteLine("  -- overlap (open play)");
        Console.WriteLine($"  {"opponents under 1.0 m, outside duels",-34} {Share(tally.OverlapFrames, tally.OpenFramesForOverlap),8}   of frames, {tally.OverlapPairs / Math.Max(1.0, tally.Matches):F0} pair-frames a match");
        Console.WriteLine($"  {"  of which both slower than 1.5 m/s",-34} {Share(tally.SittingFrames, tally.OpenFramesForOverlap),8}   of frames (sitting on each other)");
        Console.WriteLine($"  {"opponents under 1.0 m, anywhere",-34} {Share(tally.OverlapAnyFrames, tally.OpenFramesForOverlap),8}   of frames");

        Console.WriteLine("  -- every stamp a match");
        Console.WriteLine("  " + string.Join("   ", tally.Stamps.OrderByDescending(pair => pair.Value).Select(pair => $"{pair.Key.Code()} {pair.Value / Math.Max(1.0, tally.Matches):F0}")));

        Console.WriteLine("  -- who erred (stamps a match)");

        foreach (var action in new[] { PassageAction.Dispossessed, PassageAction.Misplaced, PassageAction.Beaten, PassageAction.Bypassed })
        {
            Console.WriteLine(
                $"  {action.Code(),-34} {tally.Blame.GetValueOrDefault(action) / Math.Max(1.0, tally.Matches),8:F1}"
                + $"   {tally.BlameInFilm.GetValueOrDefault(action) / Math.Max(1.0, tally.Matches),5:F1} in the film, {tally.BlameInFilm.GetValueOrDefault(action) / Math.Max(1.0, tally.FilmFrames / 1_200.0),5:F2} a film minute");
        }

        Console.WriteLine();
        Console.WriteLine("  -- film windows (match seconds; negative slack means the regain is cut off)");
        Console.WriteLine($"  {"film, in play (s of film)",-34} {Spread(tally.FilmSeconds)}");
        Console.WriteLine($"  {"without the filler (s of film)",-34} {Spread(tally.ContentSeconds)}");
        FilmLine("goals", tally.GoalLeads, tally.GoalSlacks);
        FilmLine("shots", tally.ShotLeads, tally.ShotSlacks);
        Console.WriteLine();
    }

    private static void FilmLine(string label, List<double> leads, List<double> slacks) =>
        Console.WriteLine(
            $"  {label,-8} {leads.Count / 1.0,5:F0} in the film   move {Spread(leads)}\n"
            + $"  {string.Empty,-8} {string.Empty,5}               slack {Spread(slacks)}   cut off {Share(slacks.Count(value => value < 0), slacks.Count)}");

    private sealed class Tally
    {
        public int Matches { get; set; }

        public List<double> ReceiveSpace { get; } = [];

        public List<double> FinalThirdReceiveSpace { get; } = [];

        public List<double> ShotSpace { get; } = [];

        public List<int> ShotMarkers { get; } = [];

        public Dictionary<PassageAction, long> Stamps { get; } = [];

        public Dictionary<PassageAction, long> Blame { get; } = [];

        public Dictionary<PassageAction, long> BlameInFilm { get; } = [];

        public long FilmFrames { get; set; }

        public long PlayerFrames { get; set; }

        public long StandingFrames { get; set; }

        public long StopGo { get; set; }

        public long Dips { get; set; }

        public long OpenFrames { get; set; }

        public long SynchronisedFrames { get; set; }

        public List<double> KilometresPer90 { get; } = [];

        public long EnginePasses { get; set; }

        public long EngineCompleted { get; set; }

        public long Passes { get; set; }

        public long Completed { get; set; }

        public long Receptions { get; set; }

        public long SlowTails { get; set; }

        public List<double> ArrivalSpeeds { get; } = [];

        public List<double> TowardBall { get; } = [];

        public List<int> FlightFrames { get; } = [];

        public List<int> HoldFrames { get; } = [];

        public long OpenFramesForOverlap { get; set; }

        public long OverlapFrames { get; set; }

        public long OverlapAnyFrames { get; set; }

        public long SittingFrames { get; set; }

        public long OverlapPairs { get; set; }

        public List<double> FilmSeconds { get; } = [];

        public List<double> ContentSeconds { get; } = [];

        public List<double> GoalLeads { get; } = [];

        public List<double> GoalSlacks { get; } = [];

        public List<double> ShotLeads { get; } = [];

        public List<double> ShotSlacks { get; } = [];
    }
}
