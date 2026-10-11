using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using TouchlineManager.Application.Match;
using TouchlineManager.MatchEngine;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.SimulationBenchmarks;

/// <summary>
/// Goal origin probe: for every goal, how did the scoring side's possession begin, and what did the goalkeepers' kicks cost. Reads the
/// continuous recording only, so it changes no play.
/// </summary>
internal static class GoalOriginProbe
{
    private const int Half = TickMatchRecording.Entities / 2;

    private static readonly object Gate = new();

    /// <summary>Runs the probe over a tab-separated export of stored snapshots.</summary>
    /// <param name="path">The export.</param>
    /// <param name="limit">The most matches to play.</param>
    /// <param name="rules">The rules in force.</param>
    public static void Run(string path, int limit, EngineRulesV2 rules)
    {
        var inputs = new List<MatchInputV1>();

        foreach (var row in File.ReadLines(path).Take(limit))
        {
            var parts = row.Split('\t', 4);
            var input = MatchSnapshotDocument.Read(parts[3]).Input;

            inputs.Add(input with
            {
                EngineVersion = EngineVersions.EngineLabel,
                RuleSetVersion = EngineVersions.RuleSetLabel,
                FormulaConfigurationHash = EngineConfiguration.HashOf(rules),
            });
        }

        var tally = new Tally();

        Parallel.For(0, inputs.Count, new ParallelOptions { MaxDegreeOfParallelism = 10 }, index =>
        {
            var recorder = new MatchPassageRecorder();
            var result = MatchSimulator.Simulate(inputs[index], rules, new PlayerLiveMetricsRecorder(), recorder, TickMatchEngine.Instance);

            if (recorder.Tick is { } recording)
            {
                Analyse(index, inputs[index], result, recording, tally);
            }
        });

        Report(tally, inputs.Count);
    }

    private static void Analyse(int match, MatchInputV1 input, MatchResultV1 result, TickMatchRecording recording, Tally tally)
    {
        var families = new MatchPositionFamily[TickMatchRecording.Entities];

        foreach (var (side, sideInput) in new[] { (0, input.Home), (1, input.Away) })
        {
            foreach (var slot in sideInput.Slots)
            {
                families[(side * Half) + slot.SlotNumber - 1] = slot.Family;
            }
        }

        var dead = new int[recording.FrameCount + 1];

        for (var frame = 0; frame < recording.FrameCount; frame++)
        {
            dead[frame + 1] = dead[frame] + (recording.State(frame) == TickPlayState.OpenPlay ? 0 : 1);
        }

        var touches = recording.Actions
            .Where(stamp => stamp.Entity >= 0 && stamp.Frame < recording.FrameCount
                && stamp.Action is not (PassageAction.Run or PassageAction.Celebrate) && !stamp.Action.IsBlame())
            .ToList();

        // ---- Spells: unbroken runs of one side's touches ----
        var spells = new List<Spell>();
        Spell? current = null;

        for (var index = 0; index < touches.Count; index++)
        {
            var touch = touches[index];
            var side = touch.Entity / Half;
            var broken = current is null
                || current.Side != side
                || dead[touch.Frame] - dead[touches[index - 1].Frame + 1] > 0;

            if (broken)
            {
                if (current is not null)
                {
                    current.EndTouch = index - 1;
                    current.NextTouch = index;
                    current.Broken = current.Side != side && dead[touch.Frame] - dead[touches[index - 1].Frame + 1] <= 0 ? "turnover" : "dead";
                    spells.Add(current);
                }

                current = new Spell { Side = side, FirstTouch = index, StartFrame = touch.Frame, FirstAction = touch.Action };

                if (index > 0 && dead[touch.Frame] - dead[touches[index - 1].Frame + 1] <= 0)
                {
                    var previous = touches[index - 1];

                    current.PreviousAction = previous.Action;
                    current.PreviousByKeeper = families[previous.Entity] == MatchPositionFamily.Goalkeeper;
                    current.PreviousFrame = previous.Frame;
                    current.PreviousEntity = previous.Entity;
                }
                else
                {
                    current.RestartKind = RestartAt(recording, touch.Frame);
                }
            }

            current!.LastFrame = touch.Frame;

            var x = side == 0 ? recording.BallX(touch.Frame) : 10_000 - recording.BallX(touch.Frame);

            current.MaxX = Math.Max(current.MaxX, x);

            if (touch.Action is PassageAction.Pass or PassageAction.Cross)
            {
                current.Passes++;
            }

            if (touch.Action is PassageAction.Shot or PassageAction.Penalty or PassageAction.FreeKick)
            {
                current.Shot = true;
                current.StrikeAction = touch.Action;
                current.StrikeEntity = touch.Entity;
                current.StrikeFrame = touch.Frame;
            }
        }

        if (current is not null)
        {
            current.EndTouch = touches.Count - 1;
            current.NextTouch = -1;
            current.Broken = "dead";
            spells.Add(current);
        }

        var frameOf = recording.Events.ToDictionary(stamp => stamp.Sequence, stamp => Math.Min(stamp.Frame, recording.FrameCount - 1));
        var goalSpells = new HashSet<Spell>();

        lock (Gate)
        {
            foreach (var sideInput in new[] { input.Home, input.Away })
            {
                var style = sideInput.Instructions.Passing.ToString();

                tally.StyleSides[style] = tally.StyleSides.GetValueOrDefault(style) + 1;
            }
        }

        foreach (var matchEvent in result.Events)
        {
            if (matchEvent.Type is not (EngineEventType.Goal or EngineEventType.PenaltyGoal) || !frameOf.TryGetValue(matchEvent.Sequence, out var goalFrame))
            {
                continue;
            }

            var side = (int)matchEvent.Side;
            var spell = spells.LastOrDefault(candidate => candidate.Side == side && candidate.Shot && candidate.StrikeFrame <= goalFrame && goalFrame - candidate.StrikeFrame <= 120);

            lock (Gate)
            {
                tally.Goals++;

                if (spell is null)
                {
                    tally.GoalKinds["unattributed (own goal / no strike found)"] = tally.GoalKinds.GetValueOrDefault("unattributed (own goal / no strike found)") + 1;

                    continue;
                }

                goalSpells.Add(spell);
                spell.Goal = true;

                var label = Label(spell, families);

                tally.GoalKinds[label] = tally.GoalKinds.GetValueOrDefault(label) + 1;
                tally.GoalPasses[Bucket(spell.Passes)] = tally.GoalPasses.GetValueOrDefault(Bucket(spell.Passes)) + 1;

                if (spell.PreviousByKeeper && spell.PreviousAction is PassageAction.Pass or PassageAction.Cross)
                {
                    tally.KeeperGifts.Add(Describe(match, input, recording, families, spell, touches, goalFrame));

                    var concedingStyle = (spell.PreviousEntity / Half == 0 ? input.Home : input.Away).Instructions.Passing.ToString();

                    tally.StyleGifts[concedingStyle] = tally.StyleGifts.GetValueOrDefault(concedingStyle) + 1;
                }
            }
        }

        // ---- Shots: where from and how it ended, by the kind of move ----
        var shotEvents = result.Events
            .Where(e => e.Type is EngineEventType.Goal or EngineEventType.ShotSaved or EngineEventType.ShotBlocked or EngineEventType.ShotOffTarget or EngineEventType.Woodwork && frameOf.ContainsKey(e.Sequence))
            .Select(e => (Event: e, Frame: frameOf[e.Sequence]))
            .ToList();

        lock (Gate)
        {
            foreach (var spell in spells.Where(s => s.Shot && s.StrikeAction == PassageAction.Shot))
            {
                var outcome = shotEvents.FirstOrDefault(s => (int)s.Event.Side == spell.Side && s.Frame >= spell.StrikeFrame && s.Frame - spell.StrikeFrame <= 100);
                var ownX = spell.Side == 0 ? recording.BallX(spell.StrikeFrame) : 10_000 - recording.BallX(spell.StrikeFrame);
                var ownY = spell.Side == 0 ? recording.BallY(spell.StrikeFrame) : 7_000 - recording.BallY(spell.StrikeFrame);
                var distance = (long)Math.Sqrt(((10_000L - ownX) * (10_000L - ownX)) + ((3_500L - ownY) * (3_500L - ownY)));
                var kind = spell.RestartKind is not null ? "restart" : (spell.Passes == 0 ? "turnover, 0 passes" : (spell.Passes <= 2 ? "turnover, 1-2 passes" : "built up, 3+ passes"));
                var row = tally.ShotQuality.GetValueOrDefault(kind);
                var type = outcome.Event?.Type;

                tally.ShotQuality[kind] = row with
                {
                    Shots = row.Shots + 1,
                    Goals = row.Goals + (type == EngineEventType.Goal ? 1 : 0),
                    OnTarget = row.OnTarget + (type is EngineEventType.Goal or EngineEventType.ShotSaved ? 1 : 0),
                    Blocked = row.Blocked + (type == EngineEventType.ShotBlocked ? 1 : 0),
                    DistanceSum = row.DistanceSum + distance,
                };

                if (spell.RestartKind is null)
                {
                    var band = ShotBandOf(distance);
                    var bandRow = tally.ShotBands.GetValueOrDefault(band);

                    tally.ShotBands[band] = bandRow with
                    {
                        Shots = bandRow.Shots + 1,
                        Goals = bandRow.Goals + (type == EngineEventType.Goal ? 1 : 0),
                        OnTarget = bandRow.OnTarget + (type is EngineEventType.Goal or EngineEventType.ShotSaved ? 1 : 0),
                        Blocked = bandRow.Blocked + (type == EngineEventType.ShotBlocked ? 1 : 0),
                        DistanceSum = bandRow.DistanceSum + distance,
                    };
                }
            }

            // Why a corner is given: the last man to touch the ball before it (a defender, or a keeper turning it round the post).
            foreach (var stoppage in recording.Stoppages.Where(s => s.State == TickPlayState.CornerPending))
            {
                var last = touches.LastOrDefault(t => t.Frame <= stoppage.Frame);

                if (last.Entity < 0 && last.Frame == 0)
                {
                    continue;
                }

                var key = $"{last.Action} by {families[last.Entity]}";

                tally.CornerCauses[key] = tally.CornerCauses.GetValueOrDefault(key) + 1;
            }

            // Why the ball is in the goal-kick spot so often: the last man to touch it before each goal kick.
            foreach (var stoppage in recording.Stoppages.Where(s => s.State == TickPlayState.GoalKickPending))
            {
                var last = touches.LastOrDefault(t => t.Frame <= stoppage.Frame);

                if (last.Entity < 0 && last.Frame == 0)
                {
                    continue;
                }

                var key = $"{last.Action} by {families[last.Entity]}";

                tally.GoalKickCauses[key] = tally.GoalKickCauses.GetValueOrDefault(key) + 1;
            }
        }

        // ---- Spell and shot statistics ----
        lock (Gate)
        {
            foreach (var spell in spells)
            {
                var restart = spell.RestartKind is not null;
                var key = restart ? "restart " + spell.RestartKind : "turnover";
                var bucket = Bucket(spell.Passes);
                var row = tally.Spells.GetValueOrDefault(key);

                tally.Spells[key] = row with
                {
                    Count = row.Count + 1,
                    FinalThird = row.FinalThird + (spell.MaxX >= 6_667 ? 1 : 0),
                    Shots = row.Shots + (spell.Shot ? 1 : 0),
                    Goals = row.Goals + (spell.Goal ? 1 : 0),
                };

                var passRow = tally.SpellsByPasses.GetValueOrDefault(bucket);

                tally.SpellsByPasses[bucket] = passRow with
                {
                    Count = passRow.Count + 1,
                    FinalThird = passRow.FinalThird + (spell.MaxX >= 6_667 ? 1 : 0),
                    Shots = passRow.Shots + (spell.Shot ? 1 : 0),
                    Goals = passRow.Goals + (spell.Goal ? 1 : 0),
                };

                if (spell.Shot && spell.StrikeAction == PassageAction.Shot)
                {
                    tally.OpenShotPasses[bucket] = tally.OpenShotPasses.GetValueOrDefault(bucket) + 1;
                }

                // How it ended.
                var end = spell.Broken;

                if (spell.Broken == "turnover" && spell.NextTouch >= 0)
                {
                    end = "turnover: " + touches[spell.NextTouch].Action;
                }
                else if (spell.Broken == "dead")
                {
                    end = "dead: " + StoppageAfter(recording, spell.LastFrame);
                }

                var endRow = tally.Ends.GetValueOrDefault(end);

                tally.Ends[end] = endRow with { Count = endRow.Count + 1, FinalThird = endRow.FinalThird + (spell.MaxX >= 6_667 ? 1 : 0) };
            }

            // ---- Passes by passer family and the keeper's kicks ----
            for (var index = 0; index < touches.Count; index++)
            {
                var kick = touches[index];

                if (kick.Action is not (PassageAction.Pass or PassageAction.Cross))
                {
                    continue;
                }

                var side = kick.Entity / Half;
                var family = families[kick.Entity];
                var keeper = family == MatchPositionFamily.Goalkeeper;
                var source = index >= 0 && kick.Frame > 0 && recording.State(kick.Frame - 1) != TickPlayState.OpenPlay
                    ? "restart " + RestartAt(recording, kick.Frame)
                    : "open play";
                var outcome = "out of play";
                var turnover = false;
                var spellOfNext = (Spell?)null;

                if (index + 1 < touches.Count && dead[touches[index + 1].Frame] - dead[kick.Frame + 1] <= 0)
                {
                    var next = touches[index + 1];

                    if (next.Entity / Half == side)
                    {
                        outcome = next.Entity == kick.Entity ? "own touch" : "completed";
                    }
                    else
                    {
                        outcome = "turnover";
                        turnover = true;
                        spellOfNext = spells.FirstOrDefault(candidate => candidate.FirstTouch == index + 1);
                    }
                }

                var ownX = side == 0 ? recording.BallX(kick.Frame) : 10_000 - recording.BallX(kick.Frame);
                var zone = ownX < 3_333 ? "own third" : (ownX < 6_667 ? "middle" : "final third");
                var groupKey = (keeper ? "keeper " + source : family.ToString()) + " / " + zone;
                var group = tally.PassGroups.GetValueOrDefault(groupKey);

                tally.PassGroups[groupKey] = group with
                {
                    Passes = group.Passes + 1,
                    Completed = group.Completed + (outcome == "completed" ? 1 : 0),
                    Turnovers = group.Turnovers + (turnover ? 1 : 0),
                    TurnoverShots = group.TurnoverShots + (spellOfNext is { Shot: true } ? 1 : 0),
                    TurnoverGoals = group.TurnoverGoals + (spellOfNext is { Goal: true } ? 1 : 0),
                };

                if (keeper)
                {
                    var length = outcome == "out of play"
                        ? -1
                        : Distance(recording, kick.Frame, touches[index + 1].Frame);
                    var lengthBand = length < 0 ? "n/a" : (length < 2_500 ? "short (<25 m)" : (length < 4_500 ? "medium (25-45 m)" : "long (45 m+)"));
                    var detailKey = source + " / " + lengthBand;
                    var detail = tally.KeeperDetail.GetValueOrDefault(detailKey);

                    tally.KeeperDetail[detailKey] = detail with
                    {
                        Passes = detail.Passes + 1,
                        Completed = detail.Completed + (outcome == "completed" ? 1 : 0),
                        Turnovers = detail.Turnovers + (turnover ? 1 : 0),
                        TurnoverShots = detail.TurnoverShots + (spellOfNext is { Shot: true } ? 1 : 0),
                        TurnoverGoals = detail.TurnoverGoals + (spellOfNext is { Goal: true } ? 1 : 0),
                    };
                }
            }
        }
    }

    private static string Label(Spell spell, MatchPositionFamily[] families)
    {
        if (spell.StrikeAction == PassageAction.Penalty)
        {
            return "penalty";
        }

        if (spell.StrikeAction == PassageAction.FreeKick)
        {
            return "direct free kick";
        }

        var passes = Bucket(spell.Passes);

        if (spell.RestartKind is { } restart)
        {
            return $"from restart: {restart} ({passes} passes)";
        }

        var previous = spell.PreviousAction;
        var who = spell.PreviousByKeeper ? "opp KEEPER" : "opp outfielder";

        if (previous is PassageAction.Pass or PassageAction.Cross)
        {
            return $"turnover: pass by {who} reached us, then {passes} passes (first touch {spell.FirstAction})";
        }

        return $"turnover: after {who} {previous} (first touch {spell.FirstAction}), then {passes} passes";
    }

    private static string Bucket(int passes) => passes switch
    {
        0 => "0",
        1 => "1",
        2 => "2",
        3 or 4 => "3-4",
        5 or 6 or 7 => "5-7",
        _ => "8+",
    };

    private static TickRestartKind? RestartAt(TickMatchRecording recording, int frame)
    {
        TickRestartKind? kind = null;

        foreach (var stamp in recording.Stoppages)
        {
            if (stamp.Frame > frame)
            {
                break;
            }

            kind = stamp.Kind;
        }

        return kind;
    }

    private static string StoppageAfter(TickMatchRecording recording, int frame)
    {
        foreach (var stamp in recording.Stoppages)
        {
            if (stamp.Frame >= frame)
            {
                return stamp.State.ToString();
            }
        }

        return "end";
    }

    private static readonly string[] ShotBandLabels = ["< 6 m", "6-12 m", "12-18 m", "18-24 m", "24 m +"];

    /// <summary>Gets the distance band of a shot, in pitch units from the middle of the goal.</summary>
    private static int ShotBandOf(long distance) => (int)Math.Min(ShotBandLabels.Length - 1, distance / 600);

    private static int Distance(TickMatchRecording recording, int from, int to)
    {
        var dx = (long)recording.BallX(to) - recording.BallX(from);
        var dy = (long)recording.BallY(to) - recording.BallY(from);

        return (int)Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>Describes a goal that came from the goalkeeper's kick, with where everybody stood when he kicked.</summary>
    private static string Describe(int match, MatchInputV1 input, TickMatchRecording recording, MatchPositionFamily[] families, Spell spell, List<TickActionStamp> touches, int goalFrame)
    {
        var builder = new StringBuilder();
        var kickFrame = spell.PreviousFrame;
        var keeper = spell.PreviousEntity;
        var keeperSide = keeper / Half;
        var flip = keeperSide == 0;
        var source = kickFrame > 0 && recording.State(kickFrame - 1) != TickPlayState.OpenPlay ? "restart " + RestartAt(recording, kickFrame) : "open play";

        int OwnX(int entity, int frame) => keeperSide == 0 ? recording.PlayerX(frame, entity) : 10_000 - recording.PlayerX(frame, entity);
        int OwnY(int entity, int frame) => keeperSide == 0 ? recording.PlayerY(frame, entity) : 7_000 - recording.PlayerY(frame, entity);

        _ = flip;

        var clock = recording.ClockSecond(kickFrame);

        builder.Append(CultureInfo.InvariantCulture, $"  match {match} half {recording.Period(kickFrame)} {clock / 60}:{clock % 60:00} [{source}] keeper at ({OwnX(keeper, kickFrame)},{OwnY(keeper, kickFrame)}) ");

        var ballX = keeperSide == 0 ? recording.BallX(kickFrame) : 10_000 - recording.BallX(kickFrame);
        var ballY = keeperSide == 0 ? recording.BallY(kickFrame) : 7_000 - recording.BallY(kickFrame);

        builder.Append(CultureInfo.InvariantCulture, $"ball ({ballX},{ballY}); ");

        var first = touches[spell.FirstTouch];
        var arriveX = keeperSide == 0 ? recording.BallX(first.Frame) : 10_000 - recording.BallX(first.Frame);
        var arriveY = keeperSide == 0 ? recording.BallY(first.Frame) : 7_000 - recording.BallY(first.Frame);
        var airborne = Enumerable.Range(kickFrame, Math.Max(1, first.Frame - kickFrame + 1)).Max(recording.BallZ);

        builder.Append(CultureInfo.InvariantCulture, $"taken by {families[first.Entity]} ({first.Action}) at ({arriveX},{arriveY}) after {(first.Frame - kickFrame) / 10.0:F1}s peak z {airborne}; ");
        builder.Append(CultureInfo.InvariantCulture, $"goal {(goalFrame - spell.StartFrame) / 10.0:F1}s later after {spell.Passes} passes; ");

        var mates = new StringBuilder();
        var opps = new StringBuilder();

        for (var entity = 0; entity < TickMatchRecording.Entities; entity++)
        {
            if (recording.PlayerX(kickFrame, entity) < 0 || entity == keeper)
            {
                continue;
            }

            var text = $"{families[entity].ToString()[0]}({OwnX(entity, kickFrame) / 100},{OwnY(entity, kickFrame) / 100}) ";

            if (entity / Half == keeperSide)
            {
                mates.Append(text);
            }
            else
            {
                opps.Append(text);
            }
        }

        builder.Append("mates [").Append(mates).Append("] opps [").Append(opps).Append(']');

        return builder.ToString();
    }

    private static string Share(long part, long whole) =>
        whole == 0 ? "   -  " : (100.0 * part / whole).ToString("F1", CultureInfo.InvariantCulture).PadLeft(5) + "%";

    private static void Report(Tally tally, int matches)
    {
        Console.WriteLine($"== Goal origins, {matches} stored matches, {tally.Goals} goals ({tally.Goals / (double)matches:F2} a match) ==");
        Console.WriteLine();
        Console.WriteLine("-- how each goal's move began --");

        foreach (var (kind, count) in tally.GoalKinds.OrderByDescending(pair => pair.Value))
        {
            Console.WriteLine($"  {count,4}  {Share(count, tally.Goals)}  {kind}");
        }

        Console.WriteLine();
        Console.WriteLine("-- passes by the scoring side in the move that ended in a goal --");

        foreach (var (bucket, count) in tally.GoalPasses.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {bucket,4} passes  {count,4}  {Share(count, tally.Goals)}");
        }

        Console.WriteLine();
        Console.WriteLine("-- possession spells (a run of one side's touches) by how they began --");

        foreach (var (key, row) in tally.Spells.OrderByDescending(pair => pair.Value.Count))
        {
            Console.WriteLine($"  {key,-22} {row.Count,7}  ({row.Count / (double)matches,6:F1}/match)  reach final third {Share(row.FinalThird, row.Count)}  shot {Share(row.Shots, row.Count)}  goal {Share(row.Goals, row.Count)}");
        }

        Console.WriteLine();
        Console.WriteLine("-- spells by the passes in them --");

        foreach (var (bucket, row) in tally.SpellsByPasses.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {bucket,4} passes  {row.Count,7}  ({row.Count / (double)matches,6:F1}/match)  final third {Share(row.FinalThird, row.Count)}  shot {Share(row.Shots, row.Count)}  goal {Share(row.Goals, row.Count)}");
        }

        Console.WriteLine();
        Console.WriteLine("-- open-play shots by passes in the move --");

        var shots = tally.OpenShotPasses.Values.Sum();

        foreach (var (bucket, count) in tally.OpenShotPasses.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {bucket,4} passes  {count,6}  {Share(count, shots)}");
        }

        Console.WriteLine();
        Console.WriteLine("-- how spells ended (count, share of those that reached the final third) --");

        foreach (var (key, row) in tally.Ends.OrderByDescending(pair => pair.Value.Count).Take(18))
        {
            Console.WriteLine($"  {key,-34} {row.Count,7}  ({row.Count / (double)matches,6:F1}/match)  of which reached final third {row.FinalThird,6}");
        }

        Console.WriteLine();
        Console.WriteLine("-- passes by passer and zone: completion, turnovers, and what the turnover led to --");

        foreach (var (key, row) in tally.PassGroups.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {key,-40} {row.Passes,7}  completed {Share(row.Completed, row.Passes)}  turnover {Share(row.Turnovers, row.Passes)}  -> shot {Share(row.TurnoverShots, row.Turnovers)}  goals {row.TurnoverGoals,3} ({1000.0 * row.TurnoverGoals / Math.Max(1, row.Passes):F2} per 1000 passes)");
        }

        Console.WriteLine();
        Console.WriteLine("-- the goalkeeper's kicks by where they came from and how far the ball went --");

        foreach (var (key, row) in tally.KeeperDetail.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {key,-40} {row.Passes,7} ({row.Passes / (double)matches,5:F1}/match)  completed {Share(row.Completed, row.Passes)}  turnover {Share(row.Turnovers, row.Passes)}  -> shot {Share(row.TurnoverShots, row.Turnovers)}  goals {row.TurnoverGoals,3}");
        }

        Console.WriteLine();
        Console.WriteLine("-- open-play shots by the kind of move: count, goals, on target, blocked, mean distance (m) --");

        foreach (var (kind, row) in tally.ShotQuality.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {kind,-24} {row.Shots,6} ({row.Shots / (double)matches,5:F1}/match)  goals {Share(row.Goals, row.Shots)}  on target {Share(row.OnTarget, row.Shots)}  blocked {Share(row.Blocked, row.Shots)}  mean distance {row.DistanceSum / Math.Max(1, row.Shots) / 100.0:F1} m");
        }

        Console.WriteLine();
        Console.WriteLine("-- open-play shots by distance from goal: count, goals, on target, blocked --");

        foreach (var (band, row) in tally.ShotBands.OrderBy(pair => pair.Key))
        {
            Console.WriteLine($"  {ShotBandLabels[band],-10} {row.Shots,6} ({row.Shots / (double)matches,5:F1}/match)  goals {Share(row.Goals, row.Shots)}  on target {Share(row.OnTarget, row.Shots)}  blocked {Share(row.Blocked, row.Shots)}");
        }

        Console.WriteLine();
        Console.WriteLine($"-- what put the ball out for a goal kick (total {tally.GoalKickCauses.Values.Sum()}, {tally.GoalKickCauses.Values.Sum() / (double)matches:F1}/match) --");

        foreach (var (key, count) in tally.GoalKickCauses.OrderByDescending(pair => pair.Value).Take(12))
        {
            Console.WriteLine($"  {count,6}  {Share(count, tally.GoalKickCauses.Values.Sum())}  {key}");
        }

        Console.WriteLine();
        Console.WriteLine($"-- what put the ball behind for a corner (total {tally.CornerCauses.Values.Sum()}, {tally.CornerCauses.Values.Sum() / (double)matches:F1}/match) --");

        foreach (var (key, count) in tally.CornerCauses.OrderByDescending(pair => pair.Value).Take(8))
        {
            Console.WriteLine($"  {count,6}  {Share(count, tally.CornerCauses.Values.Sum())}  {key}");
        }

        Console.WriteLine();
        Console.WriteLine("-- goals conceded straight off the side's own goalkeeper's kick, by that side's passing style --");

        foreach (var (style, sides) in tally.StyleSides.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var gifts = tally.StyleGifts.GetValueOrDefault(style);

            Console.WriteLine($"  {style,-16} {sides,4} side-matches  {gifts,4} goals conceded this way  ({gifts / (double)sides:F2} a side-match)");
        }

        Console.WriteLine();
        Console.WriteLine($"-- goals that came straight off a goalkeeper's kick: {tally.KeeperGifts.Count} of {tally.Goals} --");

        foreach (var text in tally.KeeperGifts.Take(14))
        {
            Console.WriteLine(text);
        }
    }

    private sealed class Spell
    {
        public int Side { get; init; }

        public int FirstTouch { get; init; }

        public int StartFrame { get; init; }

        public PassageAction FirstAction { get; init; }

        public TickRestartKind? RestartKind { get; set; }

        public PassageAction PreviousAction { get; set; }

        public bool PreviousByKeeper { get; set; }

        public int PreviousFrame { get; set; }

        public int PreviousEntity { get; set; }

        public int LastFrame { get; set; }

        public int EndTouch { get; set; }

        public int NextTouch { get; set; } = -1;

        public string Broken { get; set; } = string.Empty;

        public int Passes { get; set; }

        public int MaxX { get; set; }

        public bool Shot { get; set; }

        public bool Goal { get; set; }

        public PassageAction StrikeAction { get; set; }

        public int StrikeEntity { get; set; }

        public int StrikeFrame { get; set; }
    }

    private readonly record struct SpellRow(int Count, int FinalThird, int Shots, int Goals);

    private readonly record struct EndRow(int Count, int FinalThird);

    private readonly record struct ShotRow(int Shots, int Goals, int OnTarget, int Blocked, long DistanceSum);

    private readonly record struct PassRow(int Passes, int Completed, int Turnovers, int TurnoverShots, int TurnoverGoals);

    private sealed class Tally
    {
        public int Goals { get; set; }

        public Dictionary<string, int> GoalKinds { get; } = [];

        public Dictionary<string, int> GoalPasses { get; } = [];

        public Dictionary<string, SpellRow> Spells { get; } = [];

        public Dictionary<string, SpellRow> SpellsByPasses { get; } = [];

        public Dictionary<string, int> OpenShotPasses { get; } = [];

        public Dictionary<string, EndRow> Ends { get; } = [];

        public Dictionary<string, ShotRow> ShotQuality { get; } = [];

        public Dictionary<int, ShotRow> ShotBands { get; } = [];

        public Dictionary<string, int> GoalKickCauses { get; } = [];

        public Dictionary<string, int> CornerCauses { get; } = [];

        public Dictionary<string, PassRow> PassGroups { get; } = [];

        public Dictionary<string, PassRow> KeeperDetail { get; } = [];

        public ConcurrentBag<string> KeeperGifts { get; } = [];

        public Dictionary<string, int> StyleGifts { get; } = [];

        public Dictionary<string, int> StyleSides { get; } = [];
    }
}
