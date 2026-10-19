using System.Diagnostics;
using System.Globalization;
using System.Text;
using Tactical11Manager.Application.Match;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.MatchEngine;
using Tactical11Manager.MatchEngine.Configuration;
using Tactical11Manager.MatchEngine.Highlights;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Tick;

namespace Tactical11Manager.SimulationBenchmarks;

/// <summary>
/// The condition census (plan at-the-moment-the-quizzical-truffle, Milestone 0): how tired the players of the tick engine are, minute by
/// minute, and what the late minutes of a match look like before tiredness lowers any skill.
/// </summary>
/// <remarks>
/// <para>
/// The census is read from the live metrics the match centre draws (one end-of-minute condition a player on the pitch), so a figure here
/// is the figure a manager sees. The drop columns apply the curve (0% at 100% condition, 2% at 90%, 10% at 60%, 20% at 40%
/// and below, a straight line between the breakpoints; mental skills take half) to that condition. Nothing in the engine reads the curve yet:
/// the probe carries its own copy so the plan can see how often it would bite before it is built.
/// </para>
/// <para>
/// The home side is the one that plays the setup, against a default 4-4-2 of the same ability. The laboratory's fixtures start every player
/// at 100% condition (they started at 80% until the tired-skills plan's M0; TC_STARTS=8000,10000 plays both to show what the start is worth). The late-game columns
/// (passes completed, shots on target) are what the feature is meant to move; they are counted per 15 minutes of play.
/// </para>
/// </remarks>
internal static class TickConditionProbe
{
    private const int Half = TickMatchRecording.Entities / 2;

    /// <summary>The floor of each band, from the top: 90% and over, 80-90, 70-80, 60-70, 40-60, under 40.</summary>
    private static readonly int[] BandFloors = [9_000, 8_000, 7_000, 6_000, 4_000, 0];

    private static readonly string[] BandNames = ["90+", "80-90", "70-80", "60-70", "40-60", "<40"];

    /// <summary>The minutes the mean condition is read at.</summary>
    private static readonly int[] Checkpoints = [30, 60, 75, 85];

    private const int Windows = 6;

    /// <summary>The setups the home side plays, against a default 4-4-2 of the same ability.</summary>
    private static readonly (string Name, string Plain, string Spec)[] Setups =
    [
        ("default", "4-4-2 default", string.Empty),
        ("press", "4-4-2 high press", "press=2"),
        ("tempo", "4-4-2 high tempo", "tempo=2"),
        ("positive", "4-4-2 Positive", "ment=3"),
        ("attacking", "4-4-2 Attacking", "ment=4"),
        ("positive-all", "4-4-2 Positive with high press, high tempo and a high line", "ment=3,press=2,line=2,tempo=2"),
        ("attacking-all", "4-4-2 Attacking with high press, high tempo and a high line", "ment=4,press=2,line=2,tempo=2"),
    ];

    /// <summary>Runs the probe.</summary>
    /// <param name="matches">How many matches a setup.</param>
    /// <param name="seed">The base seed.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="mode">Empty for the setups (TC_ONLY=a,b narrows them), "s6b" for the weaker-side guard cell S6b, "s6bsubs" for what substitutions are worth to its attackers, or "snap" to census the stored snapshots.</param>
    /// <param name="path">The snapshot export for "snap".</param>
    public static void Run(int matches, ulong seed, EngineRulesV2 rules, string mode, string path)
    {
        var directory = Path.Combine(".run-logs", "cond");

        Directory.CreateDirectory(directory);

        var watch = Stopwatch.StartNew();
        var text = new StringBuilder();

        if (mode.Equals("snap", StringComparison.OrdinalIgnoreCase))
        {
            text.Append(Snapshots(path, matches, rules));
        }
        else if (mode.Equals("s6bsubs", StringComparison.OrdinalIgnoreCase))
        {
            text.Append(SubstitutionStudy(matches, seed, rules));
        }
        else if (mode.Equals("s6b", StringComparison.OrdinalIgnoreCase))
        {
            // The weaker-side guard cell S6b of tickbalance: a 17-20 side (keeper 17, pace 16) at home to a 15-18 counter-attacking side
            // (keeper 15, pace 19), played with the all-out attacking setup and with the default.
            string[] counter = ["form=12,ment=1,press=0,line=0,pass=2,counter=1"];

            foreach (var (name, plain, homeSetup) in new[] { ("s6b-all-out", "17-20 all-out (Positive, high press, high tempo, high line)", "ment=3,press=2,line=2,tempo=2"), ("s6b-default", "17-20 default 4-4-2", string.Empty) })
            {
                var tallies = new Tally[matches];
                var homeSpec = $"gk=17,pace=16{(homeSetup.Length == 0 ? string.Empty : "," + homeSetup)}";
                var awaySpec = $"gk=15,pace=19,{counter[0]}";

                Parallel.For(0, matches, new ParallelOptions { MaxDegreeOfParallelism = 10 }, index =>
                {
                    var (homeForm, homeInstructions, homeKeeper, homePace) = TickShowcase.Spec(homeSpec);
                    var (awayForm, awayInstructions, awayKeeper, awayPace) = TickShowcase.Spec(awaySpec);
                    var (homeLow, homeHigh) = TickShowcase.Range("17-20");
                    var (awayLow, awayHigh) = TickShowcase.Range("15-18");
                    var built = LaboratoryFixtures.Build(seed + (ulong)index, homeLow, awayLow, homeInstructions, awayInstructions, rules);
                    var input = TickCalibration.Stand(
                        built with
                        {
                            Home = TickShowcase.Paced(TickShowcase.Keeper(TickShowcase.Spread(built.Home, homeLow, homeHigh), homeKeeper), homePace),
                            Away = TickShowcase.Paced(TickShowcase.Keeper(TickShowcase.Spread(built.Away, awayLow, awayHigh), awayKeeper), awayPace),
                        },
                        homeForm,
                        awayForm);

                    tallies[index] = Play(input, rules);
                });

                text.Append(Report($"{plain} at home to the 15-18 counter-attacking side, 100% start", name, Tally.Sum(tallies), ["home 17-20", "away 15-18 counter"]));
            }
        }
        else
        {
            var filter = (Environment.GetEnvironmentVariable("TC_ONLY") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var starts = (Environment.GetEnvironmentVariable("TC_STARTS") ?? "10000").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();

            foreach (var start in starts)
            {
                foreach (var (name, plain, spec) in Setups.Where(setup => filter.Length == 0 || filter.Contains(setup.Name, StringComparer.OrdinalIgnoreCase)))
                {
                    var tallies = new Tally[matches];

                    Parallel.For(0, matches, new ParallelOptions { MaxDegreeOfParallelism = 10 }, index =>
                    {
                        var matchSeed = seed + (ulong)index;
                        var input = Build(spec, start, matchSeed, rules);

                        tallies[index] = Play(input, rules);
                    });

                    text.Append(Report($"{plain}, every player starting at {start / 100}%", name, Tally.Sum(tallies), ["setup side", "default opponent"]));
                }
            }
        }

        watch.Stop();

        var report = text.ToString();

        Console.Write(report);
        File.WriteAllText(Path.Combine(directory, $"{(mode.Length == 0 ? "setups" : mode)}-{seed}.txt"), report);
        File.AppendAllText(
            Path.Combine(directory, "ledger.tsv"),
            string.Create(CultureInfo.InvariantCulture, $"{DateTime.UtcNow:yyyy-MM-dd HH:mm}\ttickcond\t{(mode.Length == 0 ? "setups" : mode)}\t{matches}\t{seed}\t{watch.Elapsed.TotalSeconds:F0}\n"));
        Console.WriteLine($"  run time this call {watch.Elapsed.TotalSeconds:F0} s; every call is appended to {Path.Combine(directory, "ledger.tsv")}");
    }

    /// <summary>
    /// What substitutions are worth to the all-out attacking side of S6b: no substitutions, the assistant's usual fatigue rule, and five
    /// substitutions that each take off the most tired player, at the five windows (46', 58', 68', 78', 84').
    /// </summary>
    /// <remarks>
    /// The counter-attacking side makes no substitutions in the first two arms, so only the home side's choice differs; in the reference arm
    /// both assistants are on (that is how S6b is played in tickbalance). "Five for the most tired" relaxes the assistant's two thresholds (replace
    /// the most tired player whatever his condition, whatever the gain) for that run only; the bench is the laboratory's (a keeper and central
    /// midfielders of the side's own ability).
    /// </remarks>
    private static string SubstitutionStudy(int matches, ulong seed, EngineRulesV2 rules)
    {
        var text = new StringBuilder();
        var everyWindow = rules with { ConditionSubstitutionThresholdBasisPoints = 10_000, MinimumConditionAdvantageBasisPoints = 0 };
        var arms = new (string Name, string Plain, EngineRulesV2 Rules, bool HomeAssistant, bool AwayAssistant)[]
        {
            ("assistant", "the assistant's usual fatigue substitutions on both sides (as tickbalance plays S6b)", rules, true, true),
            ("none", "no substitutions by the attackers (the counter side makes none either)", rules, false, false),
            ("five-tired", "five substitutions by the attackers, each for the most tired player on the pitch (the counter side makes none)", everyWindow, true, false),
        };

        text.Append(CultureInfo.InvariantCulture, $"== tickcond s6bsubs: the 17-20 all-out attacking side (Positive, high press, high tempo, high line) at home to the 15-18 counter-attacking side, {matches} matches an arm, 100% start ==\n");

        foreach (var (name, plain, armRules, homeAssistant, awayAssistant) in arms)
        {
            var homeWins = 0;
            var draws = 0;
            var awayWins = 0;
            var goals = new long[2];
            var substitutions = new long[2];
            var substitutionMinutes = 0L;
            var conditionSum = new double[2];
            var gate = new object();

            Parallel.For(0, matches, new ParallelOptions { MaxDegreeOfParallelism = 10 }, index =>
            {
                var (homeForm, homeInstructions, homeKeeper, homePace) = TickShowcase.Spec("gk=17,pace=16,ment=3,press=2,line=2,tempo=2");
                var (awayForm, awayInstructions, awayKeeper, awayPace) = TickShowcase.Spec("gk=15,pace=19,form=12,ment=1,press=0,line=0,pass=2,counter=1");
                var (homeLow, homeHigh) = TickShowcase.Range("17-20");
                var (awayLow, awayHigh) = TickShowcase.Range("15-18");
                var built = LaboratoryFixtures.Build(seed + (ulong)index, homeLow, awayLow, homeInstructions, awayInstructions, armRules);
                var home = TickShowcase.Paced(TickShowcase.Keeper(TickShowcase.Spread(built.Home, homeLow, homeHigh), homeKeeper), homePace);
                var away = TickShowcase.Paced(TickShowcase.Keeper(TickShowcase.Spread(built.Away, awayLow, awayHigh), awayKeeper), awayPace);
                var input = TickCalibration.Stand(
                    built with
                    {
                        Home = home with { MatchPlan = new MatchPlanV1 { AssistantFatigueSubstitutions = homeAssistant } },
                        Away = away with { MatchPlan = new MatchPlanV1 { AssistantFatigueSubstitutions = awayAssistant } },
                    },
                    homeForm,
                    awayForm);
                var live = new PlayerLiveMetricsRecorder();
                var result = MatchSimulator.Simulate(input, armRules, live, null, TickMatchEngine.Instance);
                var squads = new[] { input.Home.Squad.Select(player => player.ParticipantId).ToHashSet(), input.Away.Squad.Select(player => player.ParticipantId).ToHashSet() };
                var onPitch = new double[2];

                for (var side = 0; side < 2; side++)
                {
                    var late = live.Metrics.Where(metric => metric.Minute == 85 && squads[side].Contains(metric.ParticipantId)).ToArray();

                    onPitch[side] = late.Length == 0 ? 0 : late.Average(metric => metric.ConditionBasisPoints);
                }

                lock (gate)
                {
                    homeWins += result.HomeGoals > result.AwayGoals ? 1 : 0;
                    draws += result.HomeGoals == result.AwayGoals ? 1 : 0;
                    awayWins += result.HomeGoals < result.AwayGoals ? 1 : 0;
                    goals[0] += result.HomeGoals;
                    goals[1] += result.AwayGoals;
                    conditionSum[0] += onPitch[0];
                    conditionSum[1] += onPitch[1];

                    foreach (var matchEvent in result.Events.Where(e => e.Type == EngineEventType.Substitution))
                    {
                        substitutions[(int)matchEvent.Side]++;

                        if (matchEvent.Side == MatchSide.Home)
                        {
                            substitutionMinutes += matchEvent.Minute;
                        }
                    }
                }
            });

            var points = ((3.0 * homeWins) + draws) / matches;

            text.Append(CultureInfo.InvariantCulture, $"  {name,-10} {plain}\n");
            text.Append(CultureInfo.InvariantCulture, $"             attackers win/draw/lose {100.0 * homeWins / matches,3:F0}/{100.0 * draws / matches,3:F0}/{100.0 * awayWins / matches,3:F0} %   points {points:F2} (counter side {((3.0 * awayWins) + draws) / matches:F2})   goals {goals[0] / (double)matches:F2}-{goals[1] / (double)matches:F2}\n");
            text.Append(CultureInfo.InvariantCulture, $"             substitutions a match: attackers {substitutions[0] / (double)matches:F2} (at minute {(substitutions[0] == 0 ? 0 : substitutionMinutes / (double)substitutions[0]):F0} on average), counter side {substitutions[1] / (double)matches:F2}; mean condition of the players on the pitch at 85': attackers {conditionSum[0] / matches / 100.0:F1}%, counter side {conditionSum[1] / matches / 100.0:F1}%\n");
        }

        text.Append('\n');

        return text.ToString();
    }

    private static MatchInputV1 Build(string spec, int start, ulong seed, EngineRulesV2 rules)
    {
        var (shape, instructions, _, _) = TickShowcase.Spec(spec);
        var built = LaboratoryFixtures.Build(seed, 16, 16, instructions, new MatchInstructionsV1(), rules);

        if (start != 10_000)
        {
            built = built with { Home = Start(built.Home, start), Away = Start(built.Away, start) };
        }

        return TickCalibration.Stand(built, shape, FormationPreset.FourFourTwo);
    }

    private static MatchSideV1 Start(MatchSideV1 side, int start) =>
        side with { Squad = [.. side.Squad.Select(player => player with { State = PlayerMatchStateV1.Uniform(start) })] };

    private static Tally Play(MatchInputV1 input, EngineRulesV2 rules)
    {
        var live = new PlayerLiveMetricsRecorder();
        var recorder = new MatchPassageRecorder();
        var result = MatchSimulator.Simulate(input, rules, live, recorder, TickMatchEngine.Instance);

        return Read(input, result, live, recorder.Tick!);
    }

    /// <summary>The drop the plan's curve gives a technical, physical or goalkeeping skill at a condition, in basis points of the sheet value.</summary>
    /// <param name="condition">The condition, 0-10,000.</param>
    private static int Drop(int condition)
    {
        // The breakpoints of TickSkillTiredness (internal to the engine): condition and drop, linear between, flat below the last.
        int[] conditions = [10_000, 9_000, 8_000, 7_000, 6_000, 5_000, 4_000];
        int[] drops = [0, 200, 400, 700, 1_000, 1_400, 2_000];

        if (condition >= conditions[0])
        {
            return drops[0];
        }

        for (var index = 1; index < conditions.Length; index++)
        {
            if (condition >= conditions[index])
            {
                return drops[index - 1] + ((conditions[index - 1] - condition) * (drops[index] - drops[index - 1]) / (conditions[index - 1] - conditions[index]));
            }
        }

        return drops[^1];
    }

    private static int BandOf(int condition)
    {
        for (var band = 0; band < BandFloors.Length; band++)
        {
            if (condition >= BandFloors[band])
            {
                return band;
            }
        }

        return BandFloors.Length - 1;
    }

    private static int MinuteOf(int period, int clockSecond) => Math.Min((clockSecond / 60) + 1, period == 1 ? 45 : 90);

    private static int WindowOf(int minute) => Math.Clamp((minute - 1) / 15, 0, Windows - 1);

    private static Tally Read(MatchInputV1 input, MatchResultV1 result, PlayerLiveMetricsRecorder live, TickMatchRecording recording)
    {
        var tally = new Tally { Matches = 1 };
        var keeper = new bool[TickMatchRecording.Entities];
        var entityOf = new Dictionary<Guid, int>();

        foreach (var (side, sideInput) in new[] { (0, input.Home), (1, input.Away) })
        {
            foreach (var slot in sideInput.Slots)
            {
                var entity = (side * Half) + slot.SlotNumber - 1;

                keeper[entity] = slot.Family == MatchPositionFamily.Goalkeeper;
                entityOf[slot.ParticipantId] = entity;
            }
        }

        // A substitute takes over his team-mate's seat, so his minutes belong to the seat's side and kind.
        foreach (var stamp in recording.Rosters.Where(stamp => stamp.Occupant != Guid.Empty))
        {
            entityOf[stamp.Occupant] = stamp.Entity;
        }

        var starters = recording.Starters.Where(id => id != Guid.Empty).ToHashSet();
        var lowest = new Dictionary<Guid, int>();
        var seen = new HashSet<(Guid, int)>();

        // The live metrics keep the last capture of a minute; a second capture of one minute is dropped from the count if it ever appears.
        foreach (var metric in live.Metrics.Reverse())
        {
            if (!entityOf.TryGetValue(metric.ParticipantId, out var entity) || !seen.Add((metric.ParticipantId, metric.Minute)))
            {
                continue;
            }

            var side = entity / Half;
            var kind = keeper[entity] ? 1 : 0;
            var drop = Drop(metric.ConditionBasisPoints);

            tally.Bands[Tally.BandIndex(side, kind, BandOf(metric.ConditionBasisPoints))]++;

            var at = Array.IndexOf(Checkpoints, metric.Minute);

            if (at >= 0)
            {
                tally.AtCondition[Tally.AtIndex(side, kind, at)].Add(metric.ConditionBasisPoints);
                tally.AtDrop[Tally.AtIndex(side, kind, at)].Add(drop);
            }

            lowest[metric.ParticipantId] = lowest.TryGetValue(metric.ParticipantId, out var low) ? Math.Min(low, metric.ConditionBasisPoints) : metric.ConditionBasisPoints;
        }

        foreach (var (id, low) in lowest.Where(pair => starters.Contains(pair.Key)))
        {
            var entity = entityOf[id];
            var cell = ((entity / Half) * 2) + (keeper[entity] ? 1 : 0);

            tally.Starters[cell]++;
            tally.BelowSixty[cell] += low < 6_000 ? 1 : 0;
            tally.BelowForty[cell] += low < 4_000 ? 1 : 0;
        }

        // The late-game figures: passes tried and found, shots and shots on target, in 15-minute windows.
        foreach (var stamp in recording.Actions)
        {
            if (stamp.Entity < 0 || stamp.Frame >= recording.FrameCount)
            {
                continue;
            }

            var window = WindowOf(MinuteOf(recording.Period(stamp.Frame), recording.ClockSecond(stamp.Frame)));

            // A hoof upfield and a ball given back are tagged as passes but the engine does not count them as such.
            if (stamp.Action is PassageAction.Pass or PassageAction.Cross && stamp.Detail is not (TickActionDetail.Clear or TickActionDetail.Recycle))
            {
                tally.Attempts[Tally.WindowIndex(stamp.Entity / Half, window)]++;
            }
            else if (stamp.Action == PassageAction.Receive && stamp.Target >= 0 && stamp.Target / Half == stamp.Entity / Half)
            {
                tally.Completed[Tally.WindowIndex(stamp.Target / Half, window)]++;
            }
        }

        foreach (var matchEvent in result.Events)
        {
            var onTarget = matchEvent.IsGoal || matchEvent.Type == EngineEventType.ShotSaved;
            var shot = onTarget || matchEvent.Type is EngineEventType.ShotBlocked or EngineEventType.ShotOffTarget or EngineEventType.Woodwork or EngineEventType.PenaltyMissed;

            if (!shot)
            {
                continue;
            }

            var cell = Tally.WindowIndex((int)matchEvent.Side, WindowOf(matchEvent.Minute));

            tally.Shots[cell]++;
            tally.OnTarget[cell] += onTarget ? 1 : 0;
            tally.Goals[cell] += matchEvent.IsGoal ? 1 : 0;
        }

        // The engine's own statistics count the same passes and shots; a difference means this probe is reading the match wrongly.
        tally.Inconsistent = (tally.Shots.Sum() != result.Home.Shots + result.Away.Shots)
            || (tally.OnTarget.Sum() != result.Home.ShotsOnTarget + result.Away.ShotsOnTarget)
            || (tally.Attempts.Sum() != result.PlayerLines.Sum(line => line.PassesAttempted))
            || (tally.Completed.Sum() != result.PlayerLines.Sum(line => line.PassesCompleted)) ? 1 : 0;
        tally.PassGap = tally.Attempts.Sum() - result.PlayerLines.Sum(line => line.PassesAttempted);
        tally.CompletedGap = tally.Completed.Sum() - result.PlayerLines.Sum(line => line.PassesCompleted);
        tally.ShotGap = tally.Shots.Sum() - (result.Home.Shots + result.Away.Shots);
        tally.OnTargetGap = tally.OnTarget.Sum() - (result.Home.ShotsOnTarget + result.Away.ShotsOnTarget);

        return tally;
    }

    private static string Report(string title, string name, Tally tally, string[] sideNames)
    {
        var text = new StringBuilder();

        void Line(string line) => text.Append(line).Append('\n');

        Line($"== tickcond {name}: {title}; {tally.Matches} matches ==");
        Line($"  reading check: {tally.Inconsistent} of {tally.Matches} matches disagree with the result's own pass and shot counts (passes {tally.PassGap:+0;-0;0}, completed {tally.CompletedGap:+0;-0;0}, shots {tally.ShotGap:+0;-0;0}, on target {tally.OnTargetGap:+0;-0;0} summed over the matches; should be 0)");

        for (var side = 0; side < sideNames.Length; side++)
        {
            Line($"  -- {sideNames[side]} --");

            for (var kind = 0; kind < 2; kind++)
            {
                var kindName = kind == 0 ? "outfield" : "goalkeeper";
                var minutes = Enumerable.Range(0, BandNames.Length).Sum(band => tally.Bands[Tally.BandIndex(side, kind, band)]);

                if (minutes == 0)
                {
                    continue;
                }

                Line($"  {kindName,-10} share of player-minutes (%): {string.Join("  ", Enumerable.Range(0, BandNames.Length).Select(band => $"{BandNames[band]} {100.0 * tally.Bands[Tally.BandIndex(side, kind, band)] / minutes,5:F1}"))}   ({minutes} player-minutes)");

                var cells = new List<string>();

                for (var at = 0; at < Checkpoints.Length; at++)
                {
                    var conditions = tally.AtCondition[Tally.AtIndex(side, kind, at)];
                    var drops = tally.AtDrop[Tally.AtIndex(side, kind, at)];

                    if (conditions.Count == 0)
                    {
                        continue;
                    }

                    var sorted = conditions.Order().ToArray();

                    cells.Add($"{Checkpoints[at]}': {conditions.Average() / 100.0,4:F1}% (p10 {sorted[(int)Math.Round(0.1 * (sorted.Length - 1))] / 100.0,4:F1}%) drop {drops.Average() / 100.0,4:F1}% / mental {drops.Average() / 200.0,4:F1}%");
                }

                Line($"  {"",-10} mean condition, p10 and the mean skill drop it would give:  {string.Join("   ", cells)}");

                var cell = (side * 2) + kind;

                Line($"  {"",-10} starters who ever go under 60%: {tally.BelowSixty[cell]} of {tally.Starters[cell]} ({100.0 * tally.BelowSixty[cell] / Math.Max(1, tally.Starters[cell]):F1}%), under 40%: {tally.BelowForty[cell]} ({100.0 * tally.BelowForty[cell] / Math.Max(1, tally.Starters[cell]):F1}%)");
            }

            var lateRows = new StringBuilder();

            for (var window = 0; window < Windows; window++)
            {
                var cell = Tally.WindowIndex(side, window);
                var attempts = tally.Attempts[cell];
                var shots = tally.Shots[cell];

                lateRows.Append(CultureInfo.InvariantCulture, $"    {(window * 15) + 1,2}-{(window + 1) * 15,2}': passes {attempts / (double)tally.Matches,5:F1} a match, completed {100.0 * tally.Completed[cell] / Math.Max(1, attempts),5:F1}%   shots {shots / (double)tally.Matches,4:F2}, on target {100.0 * tally.OnTarget[cell] / Math.Max(1, shots),5:F1}%, goals {tally.Goals[cell] / (double)tally.Matches,4:F2}\n");
            }

            Line("  late game by 15 minutes (the stoppage time of each half counts in its last window):");
            text.Append(lateRows);
        }

        Line(string.Empty);

        return text.ToString();
    }

    /// <summary>The kick-off condition of the stored snapshots, and what playing them on the tick engine does to it.</summary>
    private static string Snapshots(string path, int limit, EngineRulesV2 rules)
    {
        var text = new StringBuilder();
        var inputs = new List<MatchInputV1>();

        foreach (var row in File.ReadLines(path).Take(limit))
        {
            var input = MatchSnapshotDocument.Read(row.Split('\t', 4)[3]).Input;

            inputs.Add(input with
            {
                EngineVersion = EngineVersions.EngineLabel,
                RuleSetVersion = EngineVersions.RuleSetLabel,
                FormulaConfigurationHash = EngineConfiguration.HashOf(rules),
            });
        }

        var starterBands = new int[2, BandNames.Length];
        var benchBands = new int[BandNames.Length];
        var starterConditions = new List<int>[2] { [], [] };

        foreach (var input in inputs)
        {
            foreach (var side in new[] { input.Home, input.Away })
            {
                var starting = side.Slots.Select(slot => slot.ParticipantId).ToHashSet();
                var keepers = side.Slots.Where(slot => slot.Family == MatchPositionFamily.Goalkeeper).Select(slot => slot.ParticipantId).ToHashSet();

                foreach (var player in side.Squad)
                {
                    var condition = player.State.ConditionBasisPoints;

                    if (!starting.Contains(player.ParticipantId))
                    {
                        benchBands[BandOf(condition)]++;

                        continue;
                    }

                    var kind = keepers.Contains(player.ParticipantId) ? 1 : 0;

                    starterBands[kind, BandOf(condition)]++;
                    starterConditions[kind].Add(condition);
                }
            }
        }

        text.Append(CultureInfo.InvariantCulture, $"== tickcond snap: {inputs.Count} stored snapshots, condition at kick-off ==\n");

        for (var kind = 0; kind < 2; kind++)
        {
            var total = Enumerable.Range(0, BandNames.Length).Sum(band => starterBands[kind, band]);
            var sorted = starterConditions[kind].Order().ToArray();

            if (total == 0)
            {
                continue;
            }

            text.Append(CultureInfo.InvariantCulture, $"  {(kind == 0 ? "outfield starters" : "goalkeepers"),-18} {string.Join("  ", Enumerable.Range(0, BandNames.Length).Select(band => $"{BandNames[band]} {100.0 * starterBands[kind, band] / total,5:F1}"))}   n {total}, mean {sorted.Average() / 100.0:F1}%, p10 {sorted[(int)Math.Round(0.1 * (sorted.Length - 1))] / 100.0:F1}%, median {sorted[sorted.Length / 2] / 100.0:F1}%, lowest {sorted[0] / 100.0:F1}%\n");
        }

        var benchTotal = benchBands.Sum();

        if (benchTotal > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"  {"bench",-18} {string.Join("  ", Enumerable.Range(0, BandNames.Length).Select(band => $"{BandNames[band]} {100.0 * benchBands[band] / benchTotal,5:F1}"))}   n {benchTotal}\n");
        }

        text.Append('\n');

        var tallies = new Tally[inputs.Count];

        Parallel.For(0, inputs.Count, new ParallelOptions { MaxDegreeOfParallelism = 10 }, index => tallies[index] = Play(inputs[index], rules));
        text.Append(Report("the stored snapshots played on the tick engine as they are", "snap", Tally.Sum(tallies), ["home side", "away side"]));

        return text.ToString();
    }

    /// <summary>The counts of one or many matches; every array is indexed by the helpers below.</summary>
    private sealed class Tally
    {
        /// <summary>Gets how many matches are in the tally.</summary>
        public int Matches { get; set; }

        public int Inconsistent { get; set; }

        public int PassGap { get; set; }

        public int CompletedGap { get; set; }

        public int ShotGap { get; set; }

        public int OnTargetGap { get; set; }

        /// <summary>Gets the player-minutes by side, kind (outfield, goalkeeper) and band.</summary>
        public long[] Bands { get; } = new long[2 * 2 * BandNames.Length];

        /// <summary>Gets the end-of-minute conditions at each checkpoint by side and kind.</summary>
        public List<int>[] AtCondition { get; } = [.. Enumerable.Range(0, 2 * 2 * Checkpoints.Length).Select(_ => new List<int>())];

        /// <summary>Gets the skill drop, in basis points, those conditions would give.</summary>
        public List<int>[] AtDrop { get; } = [.. Enumerable.Range(0, 2 * 2 * Checkpoints.Length).Select(_ => new List<int>())];

        /// <summary>Gets the starters by side and kind.</summary>
        public int[] Starters { get; } = new int[4];

        public int[] BelowSixty { get; } = new int[4];

        public int[] BelowForty { get; } = new int[4];

        public int[] Attempts { get; } = new int[2 * Windows];

        public int[] Completed { get; } = new int[2 * Windows];

        public int[] Shots { get; } = new int[2 * Windows];

        public int[] OnTarget { get; } = new int[2 * Windows];

        public int[] Goals { get; } = new int[2 * Windows];

        public static int BandIndex(int side, int kind, int band) => (((side * 2) + kind) * BandNames.Length) + band;

        public static int AtIndex(int side, int kind, int at) => (((side * 2) + kind) * Checkpoints.Length) + at;

        public static int WindowIndex(int side, int window) => (side * Windows) + window;

        public static Tally Sum(Tally[] tallies)
        {
            var sum = new Tally();

            foreach (var tally in tallies)
            {
                sum.Matches += tally.Matches;
                sum.Inconsistent += tally.Inconsistent;
                sum.PassGap += tally.PassGap;
                sum.CompletedGap += tally.CompletedGap;
                sum.ShotGap += tally.ShotGap;
                sum.OnTargetGap += tally.OnTargetGap;
                Add(sum.Bands, tally.Bands);
                Add(sum.Starters, tally.Starters);
                Add(sum.BelowSixty, tally.BelowSixty);
                Add(sum.BelowForty, tally.BelowForty);
                Add(sum.Attempts, tally.Attempts);
                Add(sum.Completed, tally.Completed);
                Add(sum.Shots, tally.Shots);
                Add(sum.OnTarget, tally.OnTarget);
                Add(sum.Goals, tally.Goals);

                for (var cell = 0; cell < sum.AtCondition.Length; cell++)
                {
                    sum.AtCondition[cell].AddRange(tally.AtCondition[cell]);
                    sum.AtDrop[cell].AddRange(tally.AtDrop[cell]);
                }
            }

            return sum;
        }

        private static void Add(long[] total, long[] part)
        {
            for (var index = 0; index < total.Length; index++)
            {
                total[index] += part[index];
            }
        }

        private static void Add(int[] total, int[] part)
        {
            for (var index = 0; index < total.Length; index++)
            {
                total[index] += part[index];
            }
        }
    }
}
