using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Tactical11Manager.Application.Match;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.MatchEngine;
using Tactical11Manager.MatchEngine.Configuration;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Tick;

namespace Tactical11Manager.SimulationBenchmarks;

/// <summary>
/// The impact study (plan i-want-you-to-curious-globe, Milestone 7): what a match rule does, measured by playing the same seeds with and
/// without it.
/// </summary>
/// <remarks>
/// <para>
/// Every cell is a base pairing (two lab sides) and a plan for the home side. The baseline of a base is played once and kept on disk; a cell plays
/// the same seeds with the plan. The engine is deterministic and the plan runner draws nothing, so the two runs are identical until the rule fires
/// and every difference after that is the rule's doing. Effects are therefore read only over the matches in which the rule fired, as a paired
/// difference with its standard error. Because the matches are identical up to the change, a difference in a match total (goals, shots, cards, a
/// player's output) is exactly the difference after the change.
/// </para>
/// <para>
/// The bench is a real club's (keeper, centre back, full back, central midfielder, attacking midfielder, winger, striker), so a rule that brings on a
/// forward or a defender has one to bring. Run with <c>TP_BENCH=real</c>. <c>TP_CELLS</c> narrows the cells to a comma list of id prefixes.
/// </para>
/// </remarks>
internal static class TickPlanStudy
{
    private const int Entities = TickMatchRecording.Entities;


    /// <summary>The bases: a scenario of the plan probe, with a side that carries the AI's own match plan or not.</summary>
    private static readonly Dictionary<string, Pairing> Bases = new()
    {
        ["E"] = new("E", "equal 16 against 16, 4-4-2 both", "equal-442", FormationPreset.FourFourTwo, FormationPreset.FourFourTwo, false),
        ["T"] = new("T", "equal 16 against 16, 4-3-3 against 4-2-3-1", "equal-433-v-4231", FormationPreset.FourThreeThree, FormationPreset.FourTwoThreeOne, false),
        ["S"] = new("S", "stronger side 18-19 against 16, 4-4-2 both", "stronger", FormationPreset.FourFourTwo, FormationPreset.FourFourTwo, false),
        ["W"] = new("W", "weaker side 16 against 18-19, 4-4-2 both", "weaker", FormationPreset.FourFourTwo, FormationPreset.FourFourTwo, false),
        ["P"] = new("P", "equal 16 against 16, 4-4-2 both, the opponent on high press and high tempo", "equal-press", FormationPreset.FourFourTwo, FormationPreset.FourFourTwo, false),
        ["K"] = new("K", "equal 16 against 16, 4-4-2 both, aggressive tackling on both sides", "equal-tackle", FormationPreset.FourFourTwo, FormationPreset.FourFourTwo, false),
        ["X"] = new("X", "equal 16 against 16, 4-4-2 against 3-5-2", "equal-442-v-352", FormationPreset.FourFourTwo, FormationPreset.ThreeFiveTwo, false),
        ["EA"] = new("EA", "equal 16 against 16, 4-4-2 both, the opponent plays the AI's default plan", "equal-442", FormationPreset.FourFourTwo, FormationPreset.FourFourTwo, true),
        ["WA"] = new("WA", "weaker side 16 against 18-19, 4-4-2 both, the opponent plays the AI's default plan", "weaker", FormationPreset.FourFourTwo, FormationPreset.FourFourTwo, true),
    };

    private static readonly Cell[] Cells = BuildCells();

    /// <summary>Runs the study.</summary>
    /// <param name="matches">How many matches a cell.</param>
    /// <param name="seed">The base seed.</param>
    /// <param name="rules">The rules in force.</param>
    public static void Run(int matches, ulong seed, EngineRulesV2 rules)
    {
        var directory = Path.Combine(".run-logs", "plans", "study");

        Directory.CreateDirectory(directory);

        var filter = (Environment.GetEnvironmentVariable("TP_CELLS") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var selected = Cells.Where(cell => filter.Length == 0 || filter.Any(prefix => $"{cell.Id}-{cell.Base}".StartsWith(prefix, StringComparison.OrdinalIgnoreCase))).ToArray();
        var baselines = new Dictionary<string, Rec[]>();
        var summary = Path.Combine(directory, $"cells-{seed}.tsv");
        var total = 0.0;

        if (!File.Exists(summary))
        {
            File.WriteAllText(summary, Header());
        }

        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine($"== tickplans study: {selected.Length} cell(s), {matches} matches each, seed {seed} ==");

        foreach (var cell in selected)
        {
            var watch = Stopwatch.StartNew();
            var basis = Bases[cell.Base];
            var spent = 0.0;

            if (!baselines.TryGetValue(basis.Key, out var without))
            {
                without = Baseline(basis, matches, seed, rules, directory, out spent);
                baselines[basis.Key] = without;
            }

            var with = Play(basis, cell, matches, seed, rules, without);

            watch.Stop();
            spent += watch.Elapsed.TotalSeconds;
            total += spent;

            var line = Analyse(cell, basis, without, with, seed, matches, spent, out var text);

            Console.Write(text);
            File.AppendAllText(summary, line);
            File.WriteAllText(Path.Combine(directory, $"{cell.Id}-{cell.Base}-{seed}.txt"), text);
            File.AppendAllText(
                Path.Combine(".run-logs", "plans", "ledger.tsv"),
                string.Create(CultureInfo.InvariantCulture, $"{DateTime.UtcNow:yyyy-MM-dd HH:mm}\tstudy\t{cell.Id}\t{matches}\t{seed}\t{spent:F0}\n"));
        }

        Console.WriteLine($"  run time this call {total:F0} s over {selected.Length} cell(s)");
    }

    private static Rec[] Baseline(Pairing basis, int matches, ulong seed, EngineRulesV2 rules, string directory, out double seconds)
    {
        var path = Path.Combine(directory, $"base-{basis.Key}-{seed}-{matches}.json");

        if (File.Exists(path))
        {
            seconds = 0;

            return JsonSerializer.Deserialize<Rec[]>(File.ReadAllText(path))!;
        }

        var watch = Stopwatch.StartNew();
        var recs = Play(basis, null, matches, seed, rules, null);

        watch.Stop();
        seconds = watch.Elapsed.TotalSeconds;
        File.WriteAllText(path, JsonSerializer.Serialize(recs));
        File.AppendAllText(
            Path.Combine(".run-logs", "plans", "ledger.tsv"),
            string.Create(CultureInfo.InvariantCulture, $"{DateTime.UtcNow:yyyy-MM-dd HH:mm}\tstudy\tbaseline-{basis.Key}\t{matches}\t{seed}\t{seconds:F0}\n"));

        return recs;
    }

    private static Rec[] Play(Pairing basis, Cell? cell, int matches, ulong seed, EngineRulesV2 rules, Rec[]? baseline)
    {
        var recs = new Rec[matches];

        Parallel.For(0, matches, new ParallelOptions { MaxDegreeOfParallelism = 10 }, index =>
        {
            // A rule that cannot fire in this match, judged from the baseline's own history (identical until the rule fires), is not played.
            if (cell?.Gate is { } gate && baseline is not null && !gate(baseline[index]))
            {
                recs[index] = new Rec();

                return;
            }

            var matchSeed = seed + (ulong)index;
            var input = TickPlanProbe.BuildNamed(basis.Scenario, matchSeed, rules);

            if (basis.AwayAi)
            {
                input = input with { Away = input.Away with { MatchPlan = TickAiPlan.PlanFor(matchSeed, 1, basis.AwayShape) } };
            }

            if (cell is not null)
            {
                input = cell.Modify(input, matchSeed, basis);

                var plan = cell.Rules(input.Home);

                if (plan.Count > 0)
                {
                    input = input with { Home = input.Home with { MatchPlan = new MatchPlanV1 { Rules = plan } } };
                }
            }

            var live = cell is { Live: true } ? new PlayerLiveMetricsRecorder() : null;
            var result = MatchSimulator.Simulate(input, rules, live, null, TickMatchEngine.Instance);

            recs[index] = Read(input, result, live);
        });

        return recs;
    }

    private static Rec Read(MatchInputV1 input, MatchResultV1 result, PlayerLiveMetricsRecorder? live)
    {
        var rec = new Rec
        {
            HomeGoals = result.HomeGoals,
            AwayGoals = result.AwayGoals,
            HomeShots = result.Home.Shots,
            AwayShots = result.Away.Shots,
            HomeYellows = result.Home.YellowCards,
            AwayYellows = result.Away.YellowCards,
            HomeReds = result.Home.RedCards,
            AwayReds = result.Away.RedCards,
        };

        var lines = new Dictionary<Guid, int[]>();

        foreach (var line in result.PlayerLines)
        {
            if (line.Side == MatchSide.Home)
            {
                lines[line.ParticipantId] =
                [
                    line.MinutesPlayed,
                    line.Goals,
                    line.Assists,
                    0,
                    line.DribblesAttempted,
                    line.DribblesCompleted,
                    line.PassesAttempted,
                    line.TacklesAttempted,
                    line.CrossesAttempted,
                ];
                rec.HomeCrosses += line.CrossesAttempted;
            }
            else
            {
                rec.AwayCrosses += line.CrossesAttempted;
            }
        }

        EngineEventV1? first = null;
        EngineEventV1? tacticalSubstitution = null;

        foreach (var matchEvent in result.Events)
        {
            if (matchEvent.Side == MatchSide.Home
                && (matchEvent.Type == EngineEventType.TacticalChange
                    || (matchEvent.Type == EngineEventType.Substitution && matchEvent.SubstitutionReason == MatchSubstitutionReason.Tactical)))
            {
                first ??= matchEvent;

                if (matchEvent.Type == EngineEventType.Substitution)
                {
                    tacticalSubstitution ??= matchEvent;
                }
            }

            if (matchEvent.Side == MatchSide.Home
                && matchEvent.ParticipantId is { } shooter
                && matchEvent.Type is EngineEventType.Goal or EngineEventType.ShotSaved or EngineEventType.ShotBlocked or EngineEventType.ShotOffTarget or EngineEventType.Woodwork
                && lines.TryGetValue(shooter, out var line))
            {
                line[3]++;
            }
        }

        rec.Lines = lines;

        foreach (var goal in result.Events.Where(matchEvent => matchEvent.IsGoal))
        {
            (goal.Side == MatchSide.Home ? rec.HomeGoalMinutes : rec.AwayGoalMinutes).Add(goal.Minute);
        }

        if (first is null)
        {
            return rec;
        }

        rec.FireSeq = first.Sequence;
        rec.FireMinute = first.Minute;

        foreach (var matchEvent in result.Events.Where(matchEvent => matchEvent.Sequence < first.Sequence && matchEvent.IsGoal))
        {
            if (matchEvent.Side == MatchSide.Home)
            {
                rec.HomeGoalsBefore++;
            }
            else
            {
                rec.AwayGoalsBefore++;
            }
        }

        if (tacticalSubstitution is { ParticipantId: { } off, SecondaryParticipantId: { } on })
        {
            rec.Off = off;
            rec.On = on;
        }

        if (live is not null)
        {
            var awayDefenders = input.Away.Slots.Where(slot => slot.Family == MatchPositionFamily.Defence).Select(slot => slot.ParticipantId).ToHashSet();
            var defenders = live.Metrics.Where(metric => metric.Minute == rec.FireMinute && awayDefenders.Contains(metric.ParticipantId)).Select(metric => metric.ConditionBasisPoints).ToArray();

            rec.OffCondition = live.Metrics.Where(metric => metric.ParticipantId == rec.Off && metric.Minute == rec.FireMinute).Select(metric => metric.ConditionBasisPoints).DefaultIfEmpty(-1).First();
            rec.OnCondition = live.Metrics.Where(metric => metric.ParticipantId == rec.On && metric.Minute > rec.FireMinute).OrderBy(metric => metric.Minute).Select(metric => metric.ConditionBasisPoints).DefaultIfEmpty(-1).First();
            rec.OpponentDefenceCondition = defenders.Length == 0 ? -1 : (int)defenders.Average();
        }

        return rec;
    }

    private static string Header() =>
        "cell\tfamily\tbase\tseed\tmatches\tn_fired\tfire_minute\t"
        + "pts_with\tpts_without\td_pts\tse_pts\twin_with\twin_without\tdraw_with\tdraw_without\tlose_with\tlose_without\t"
        + "d_win\tse_win\td_draw\td_lose\tse_lose\td_worse\tse_worse\td_better\tse_better\t"
        + "gf_after_with\tgf_after_without\tga_after_with\tga_after_without\td_gf\tse_gf\td_ga\tse_ga\t"
        + "d_shots_for\tse_shots_for\td_shots_against\td_crosses_for\td_yellow_own\td_red_own\td_red_opp\t"
        + "sub_n\tsub_shots90\tstarter_shots90\tsub_goals90\tstarter_goals90\tsub_drib90\tstarter_drib90\tsub_pass90\tstarter_pass90\tsub_min\tstarter_min\t"
        + "off_cond\ton_cond\topp_def_cond\tspent_s\tplain\n";

    private static string Analyse(Cell cell, Pairing basis, Rec[] without, Rec[] with, ulong seed, int matches, double spent, out string text)
    {
        var fired = Enumerable.Range(0, matches).Where(index => with[index].FireSeq > 0).ToArray();
        var n = fired.Length;
        var report = new StringBuilder();

        void Line(string line) => report.Append(line).Append('\n');

        double Points(Rec rec) => rec.HomeGoals > rec.AwayGoals ? 3.0 : (rec.HomeGoals == rec.AwayGoals ? 1.0 : 0.0);

        (double Mean, double Se) Paired(Func<int, double> difference)
        {
            if (n == 0)
            {
                return (0, 0);
            }

            var values = fired.Select(difference).ToArray();
            var mean = values.Average();
            var variance = n > 1 ? values.Sum(value => (value - mean) * (value - mean)) / (n - 1) : 0;

            return (mean, Math.Sqrt(variance / n));
        }

        (double Mean, double Se) Share(Func<Rec, bool> test) => Paired(index => (test(with[index]) ? 1.0 : 0.0) - (test(without[index]) ? 1.0 : 0.0));

        double Rate(Func<Rec, bool> test, Rec[] side) => n == 0 ? 0 : 100.0 * fired.Count(index => test(side[index])) / n;

        var points = Paired(index => Points(with[index]) - Points(without[index]));
        var gf = Paired(index => with[index].HomeGoals - without[index].HomeGoals);
        var ga = Paired(index => with[index].AwayGoals - without[index].AwayGoals);
        var shotsFor = Paired(index => with[index].HomeShots - without[index].HomeShots);
        var shotsAgainst = Paired(index => with[index].AwayShots - without[index].AwayShots);
        var crosses = Paired(index => with[index].HomeCrosses - without[index].HomeCrosses);
        var yellowOwn = Paired(index => with[index].HomeYellows - without[index].HomeYellows);
        var redOwn = Paired(index => with[index].HomeReds - without[index].HomeReds);
        var redOpponent = Paired(index => with[index].AwayReds - without[index].AwayReds);
        var meanMinute = n == 0 ? 0 : fired.Average(index => with[index].FireMinute);

        bool Won(Rec rec) => rec.HomeGoals > rec.AwayGoals;
        bool Drew(Rec rec) => rec.HomeGoals == rec.AwayGoals;
        bool Lost(Rec rec) => rec.HomeGoals < rec.AwayGoals;

        // "Worse" and "better" are against the margin at the moment the rule fired: the lead that was given up, or the deficit that grew.
        bool Worse(Rec rec) => rec.HomeGoals - rec.AwayGoals < rec.HomeGoalsBefore - rec.AwayGoalsBefore;
        bool Better(Rec rec) => rec.HomeGoals - rec.AwayGoals > rec.HomeGoalsBefore - rec.AwayGoalsBefore;

        var win = Share(Won);
        var draw = Share(Drew);
        var lose = Share(Lost);
        var worse = Paired(index => (Worse(with[index]) ? 1.0 : 0.0) - (WorseBase(without[index], with[index]) ? 1.0 : 0.0));
        var better = Paired(index => (Better(with[index]) ? 1.0 : 0.0) - (BetterBase(without[index], with[index]) ? 1.0 : 0.0));

        double GoalsAfter(Rec rec, bool home) => home ? rec.HomeGoals - rec.HomeGoalsBefore : rec.AwayGoals - rec.AwayGoalsBefore;

        double GoalsAfterBase(Rec baseline, Rec played, bool home) => home ? baseline.HomeGoals - played.HomeGoalsBefore : baseline.AwayGoals - played.AwayGoalsBefore;

        var gfWith = n == 0 ? 0 : fired.Average(index => GoalsAfter(with[index], true));
        var gfWithout = n == 0 ? 0 : fired.Average(index => GoalsAfterBase(without[index], with[index], true));
        var gaWith = n == 0 ? 0 : fired.Average(index => GoalsAfter(with[index], false));
        var gaWithout = n == 0 ? 0 : fired.Average(index => GoalsAfterBase(without[index], with[index], false));

        // The substitute against the man he replaced, over the same minutes: the starter's output in those minutes is his match total without the
        // rule less his total with it (identical before the change).
        var swapped = fired.Where(index => with[index].On != Guid.Empty && with[index].Lines.ContainsKey(with[index].On) && without[index].Lines.ContainsKey(with[index].Off)).ToArray();
        var subMinutes = swapped.Sum(index => (double)with[index].Lines[with[index].On][0]);
        var starterMinutes = swapped.Sum(index => (double)(without[index].Lines[with[index].Off][0] - with[index].Lines[with[index].Off][0]));

        double SubPer90(int field) => subMinutes == 0 ? 0 : 90.0 * swapped.Sum(index => with[index].Lines[with[index].On][field]) / subMinutes;

        double StarterPer90(int field) => starterMinutes <= 0 ? 0 : 90.0 * swapped.Sum(index => without[index].Lines[with[index].Off][field] - with[index].Lines[with[index].Off][field]) / starterMinutes;

        double Condition(Func<Rec, int> read) => fired.Select(index => read(with[index])).Where(value => value >= 0).DefaultIfEmpty(-100).Average() / 100.0;

        string Delta((double Mean, double Se) value, string format = "+0.00;-0.00;0.00") => $"{value.Mean.ToString(format, CultureInfo.InvariantCulture)} ±{1.96 * value.Se:F2}";

        string DeltaPercent((double Mean, double Se) value) => $"{(100.0 * value.Mean).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture)} ±{196.0 * value.Se:F1}";

        Line($"== {cell.Id}: {cell.Plain}; on {basis.Plain}; seed {seed}, {spent:F0} s ==");
        Line($"   fired in {n} of {matches} matches, on average at minute {meanMinute:F0}");

        if (n > 0)
        {
            Line($"   home points   with {fired.Average(index => Points(with[index])):F2}   without {fired.Average(index => Points(without[index])):F2}   change {Delta(points)}");
            Line($"   win / draw / lose %   with {Rate(Won, with):F0}/{Rate(Drew, with):F0}/{Rate(Lost, with):F0}   without {Rate(Won, without):F0}/{Rate(Drew, without):F0}/{Rate(Lost, without):F0}   change win {DeltaPercent(win)}, draw {DeltaPercent(draw)}, lose {DeltaPercent(lose)}");
            Line($"   margin against the one at the change: worse {DeltaPercent(worse)} points of matches, better {DeltaPercent(better)}");
            Line($"   goals after the change   for {gfWith:F2} (without {gfWithout:F2}, change {Delta(gf)})   against {gaWith:F2} (without {gaWithout:F2}, change {Delta(ga)})");
            Line($"   shots for {Delta(shotsFor)}, against {Delta(shotsAgainst)}; crosses for {Delta(crosses)}; own yellows {Delta(yellowOwn)}; own reds {Delta(redOwn)}, opponent reds {Delta(redOpponent)}");
        }

        if (swapped.Length > 0)
        {
            Line($"   the substitute ({swapped.Length} swaps, {subMinutes / swapped.Length:F0} min each) against the starter over the same minutes ({starterMinutes / swapped.Length:F0} min each), per 90: shots {SubPer90(3):F2} / {StarterPer90(3):F2}, goals {SubPer90(1):F3} / {StarterPer90(1):F3}, dribbles {SubPer90(4):F2} / {StarterPer90(4):F2}, passes {SubPer90(6):F1} / {StarterPer90(6):F1}");
        }

        if (cell.Live && n > 0)
        {
            Line($"   legs at the change: starter {Condition(rec => rec.OffCondition):F1} %, substitute {Condition(rec => rec.OnCondition):F1} % (first minute on), the opponent's defenders {Condition(rec => rec.OpponentDefenceCondition):F1} %");
        }

        Line(string.Empty);
        text = report.ToString();

        string F(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

        string Both((double Mean, double Se) value) => $"{F(value.Mean)}\t{F(value.Se)}";

        return string.Join(
            '\t',
            cell.Id,
            cell.Family,
            basis.Key,
            seed,
            matches,
            n,
            F(meanMinute),
            F(n == 0 ? 0 : fired.Average(index => Points(with[index]))),
            F(n == 0 ? 0 : fired.Average(index => Points(without[index]))),
            Both(points),
            F(Rate(Won, with)),
            F(Rate(Won, without)),
            F(Rate(Drew, with)),
            F(Rate(Drew, without)),
            F(Rate(Lost, with)),
            F(Rate(Lost, without)),
            Both(win),
            F(draw.Mean),
            F(lose.Mean),
            F(lose.Se),
            Both(worse),
            Both(better),
            F(gfWith),
            F(gfWithout),
            F(gaWith),
            F(gaWithout),
            Both(gf),
            Both(ga),
            Both(shotsFor),
            F(shotsAgainst.Mean),
            F(crosses.Mean),
            F(yellowOwn.Mean),
            F(redOwn.Mean),
            F(redOpponent.Mean),
            swapped.Length,
            F(SubPer90(3)),
            F(StarterPer90(3)),
            F(SubPer90(1)),
            F(StarterPer90(1)),
            F(SubPer90(4)),
            F(StarterPer90(4)),
            F(SubPer90(6)),
            F(StarterPer90(6)),
            F(swapped.Length == 0 ? 0 : subMinutes / swapped.Length),
            F(swapped.Length == 0 ? 0 : starterMinutes / swapped.Length),
            cell.Live && n > 0 ? F(Condition(rec => rec.OffCondition)) : string.Empty,
            cell.Live && n > 0 ? F(Condition(rec => rec.OnCondition)) : string.Empty,
            cell.Live && n > 0 ? F(Condition(rec => rec.OpponentDefenceCondition)) : string.Empty,
            F(spent),
            cell.Plain) + "\n";
    }

    // The baseline match has no tactical event, so the margin at the change is the one the played match recorded.
    private static bool WorseBase(Rec baseline, Rec played) => baseline.HomeGoals - baseline.AwayGoals < played.HomeGoalsBefore - played.AwayGoalsBefore;

    private static bool BetterBase(Rec baseline, Rec played) => baseline.HomeGoals - baseline.AwayGoals > played.HomeGoalsBefore - played.AwayGoalsBefore;

    // ---------------------------------------------------------------- the rules

    private static Cell[] BuildCells()
    {
        var cells = new List<Cell>();

        // A. Protect a lead.
        foreach (var key in new[] { "E", "T", "S" })
        {
            cells.Add(Protect("A1", key, "one step more defensive, from 70' leading by 2+", 70, 2, OneStepDefensive));
            cells.Add(Protect("A2", key, "Defensive, low block and deep line, from 70' leading by 2+", 70, 2, DefensiveLowBlock));
            cells.Add(Protect("A3", key, "switch to 5-4-1 Cautious, from 70' leading by 2+", 70, 2, FiveFourOneCautious));
            cells.Add(Protect("A4", key, "5-4-1 Defensive, low block and time-wasting, from 70' leading by 2+", 70, 2, FiveFourOneDefensive));
            cells.Add(Protect("A5", key, "5-4-1 and a fresh defender for the most tired forward, from 70' leading by 2+", 70, 2, FiveFourOneFreshDefender));
        }

        foreach (var key in new[] { "E", "S" })
        {
            cells.Add(Protect("A6a", key, "one step more defensive, from 80' leading by 1+", 80, 1, OneStepDefensive));
            cells.Add(Protect("A6b", key, "Defensive, low block and deep line, from 80' leading by 1+", 80, 1, DefensiveLowBlock));
            cells.Add(Protect("A6c", key, "5-4-1 Defensive, low block and time-wasting, from 80' leading by 1+", 80, 1, FiveFourOneDefensive));
        }

        cells.Add(Protect("A7a", "E", "Defensive, low block and deep line, from 60' leading by 2+", 60, 2, DefensiveLowBlock));
        cells.Add(Protect("A7b", "E", "Defensive, low block and deep line, from 80' leading by 2+", 80, 2, DefensiveLowBlock));

        // B. Chase the game.
        foreach (var key in new[] { "E", "W" })
        {
            cells.Add(Chase("B1", key, "Attacking only, from 60' losing by 1+", 60, AttackingOnly));
            cells.Add(Chase("B2", key, "4-3-3, Attacking and high press, from 60' losing by 1+", 60, FourThreeThreeChase));
            cells.Add(Chase("B3", key, "4-3-3, Attacking, high press and two fresh forwards for tired midfielders, from 60' losing by 1+", 60, FourThreeThreeChaseTwoForwards));
            cells.Add(Chase("B4", key, "all-out (3-4-3, Attacking, high press, high tempo, high line), from 85' losing by 1+", 85, AllOut));
            cells.Add(Chase("B5", key, "4-3-3, Attacking and high press, from 75' losing by 1+", 75, FourThreeThreeChase));
        }

        cells.Add(Chase("B2", "T", "4-3-3, Attacking and high press, from 60' losing by 1+", 60, FourThreeThreeChase));
        cells.Add(Chase("B3", "T", "4-3-3, Attacking, high press and two fresh forwards for tired midfielders, from 60' losing by 1+", 60, FourThreeThreeChaseTwoForwards));

        // C. Fresh legs.
        cells.Add(Fresh("C1", "P", "a fresh striker for the most tired forward at 60'", 60, FreshStriker, live: true));
        cells.Add(Fresh("C2", "P", "a fresh striker for the most tired forward at 70'", 70, FreshStriker, live: true));
        cells.Add(Fresh("C3", "E", "a fresh striker for the most tired forward at 60'", 60, FreshStriker, live: true));
        cells.Add(Fresh("C4", "P", "control: a fresh striker for the most tired forward at 30'", 30, FreshStriker, live: true));
        cells.Add(Fresh("C5", "P", "a fresh fast winger (pace 18) for the most tired midfielder at 60'", 60, FreshWinger, live: true, modify: FastWinger));
        cells.Add(Fresh("C6", "E", "a fresh fast winger (pace 18) for the most tired midfielder at 60'", 60, FreshWinger, live: true, modify: FastWinger));

        // D. A shape that exploits an area.
        foreach (var key in new[] { "X", "E" })
        {
            cells.Add(Shape("D1", key, "4-4-2 to 4-3-3 and wings focus at 60'", 60, FormationPreset.FourThreeThree, new MatchInstructionChangesV1 { PassFocus = MatchPassFocus.Wings }));
            cells.Add(Shape("D2", key, "4-4-2 to 4-2-3-1 and centre focus at 60'", 60, FormationPreset.FourTwoThreeOne, new MatchInstructionChangesV1 { PassFocus = MatchPassFocus.Centre }));
            cells.Add(Shape("D3", key, "4-4-2 to 3-4-3 at 60'", 60, FormationPreset.ThreeFourThree, null));
        }

        cells.Add(Shape("D4a", "X", "4-4-2 to 4-3-3 and wings focus at the first stoppage", 1, FormationPreset.FourThreeThree, new MatchInstructionChangesV1 { PassFocus = MatchPassFocus.Wings }));
        cells.Add(Shape("D4b", "X", "4-4-2 to 4-3-3 and wings focus at 30'", 30, FormationPreset.FourThreeThree, new MatchInstructionChangesV1 { PassFocus = MatchPassFocus.Wings }));

        // E. Bookings.
        cells.Add(Cell.Of("E1", "E", "K", "a booked defender comes off for a fresh one, after 45'", home => [Rule(1, from: 45, evt: MatchRuleEvent.OwnBooked, family: MatchPositionFamily.Defence, subs: [SubjectFor(MatchPositionFamily.Defence)])], gate: rec => rec.HomeYellows > 0));
        cells.Add(Cell.Of("E2", "E", "K", "on a defender's booking the side stays on its feet, after 45'", home => [Rule(1, from: 45, evt: MatchRuleEvent.OwnBooked, family: MatchPositionFamily.Defence, changes: new MatchInstructionChangesV1 { Tackling = MatchTacklingStyle.StayOnFeet })], gate: rec => rec.HomeYellows > 0));
        cells.Add(Cell.Of("E3", "E", "E", "a booked defender comes off for a fresh one, after 45'", home => [Rule(1, from: 45, evt: MatchRuleEvent.OwnBooked, family: MatchPositionFamily.Defence, subs: [SubjectFor(MatchPositionFamily.Defence)])], gate: rec => rec.HomeYellows > 0));

        // F. Red cards.
        foreach (var key in new[] { "K", "E" })
        {
            cells.Add(Cell.Of("F1", "F", key, "own player sent off: 4-5-1 and Cautious", home => [Rule(1, evt: MatchRuleEvent.OwnSentOff, shape: FormationPreset.FourFiveOne, changes: new MatchInstructionChangesV1 { Mentality = MatchMentality.Cautious })], gate: rec => rec.HomeReds > 0));
            cells.Add(Cell.Of("F2", "F", key, "own player sent off: keep the shape, Defensive", home => [Rule(1, evt: MatchRuleEvent.OwnSentOff, changes: new MatchInstructionChangesV1 { Mentality = MatchMentality.Defensive })], gate: rec => rec.HomeReds > 0));
            cells.Add(Cell.Of("F3", "F", key, "opponent player sent off: Attacking and wide", home => [Rule(1, evt: MatchRuleEvent.OpponentSentOff, changes: new MatchInstructionChangesV1 { Mentality = MatchMentality.Attacking, Width = MatchWidth.Wide })], gate: rec => rec.AwayReds > 0));
            cells.Add(Cell.Of("F4", "F", key, "opponent player sent off: 4-3-3 and high press", home => [Rule(1, evt: MatchRuleEvent.OpponentSentOff, shape: FormationPreset.FourThreeThree, changes: new MatchInstructionChangesV1 { Pressing = MatchPressing.HighPress })], gate: rec => rec.AwayReds > 0));
        }

        // G. A player playing badly.
        foreach (var key in new[] { "E", "W" })
        {
            cells.Add(Cell.Of("G1", "G", key, "the lowest rated forward comes off for a fresh forward, from 60'", home => [Rule(1, from: 60, evt: MatchRuleEvent.PlayingBadly, family: MatchPositionFamily.Attack, subs: [SubjectFor(MatchPositionFamily.Attack)])]));
            cells.Add(Cell.Of("G2", "G", key, "a midfielder rated below 5.7 comes off for a fresh midfielder, from 60'", home => [Rule(1, from: 60, evt: MatchRuleEvent.PlayingBadly, family: MatchPositionFamily.Midfield, threshold: 5_700, subs: [SubjectFor(MatchPositionFamily.Midfield)])]));
        }

        // H. The AI's default plans, and a human plan against one.
        cells.Add(Cell.Of("H1", "H", "E", "both sides play the AI's default plan, against neither having one", _ => [], bothAi: true));
        cells.Add(Cell.Of("H1", "H", "T", "both sides play the AI's default plan, against neither having one", _ => [], bothAi: true));
        cells.Add(Cell.Of("H2", "H", "EA", "a human plan (4-3-3 chase from 60', 5-4-1 Cautious protect from 70') against the AI's default plan", SmartPlan));
        cells.Add(Cell.Of("H2", "H", "WA", "a human plan (4-3-3 chase from 60', 5-4-1 Cautious protect from 70') against the AI's default plan", SmartPlan));

        return [.. cells];
    }

    private static Cell Protect(string id, string key, string plain, int from, int margin, Func<MatchRuleV1> rule) =>
        Cell.Of(id, id[..1], key, plain, _ => [rule() with { Ordinal = 1, FromMinute = from, Score = MatchRuleScore.Leading, ScoreMargin = margin }], gate: rec => LeadReaches(rec, from, margin, 1));

    private static Cell Chase(string id, string key, string plain, int from, Func<MatchRuleV1> rule) =>
        Cell.Of(id, id[..1], key, plain, _ => [rule() with { Ordinal = 1, FromMinute = from, Score = MatchRuleScore.Trailing, ScoreMargin = 1 }], gate: rec => LeadReaches(rec, from, 1, -1));

    /// <summary>
    /// Whether the home side's lead (sign 1) or deficit (sign -1) reaches the margin at some point from two minutes before the rule's minute on. It
    /// errs towards yes (goals in one minute are taken in the order most favourable to the test), because a match it wrongly lets through simply
    /// does not fire, while one it wrongly kept out would be missed.
    /// </summary>
    private static bool LeadReaches(Rec baseline, int from, int margin, int sign)
    {
        var cutoff = Math.Max(0, from - 2);
        var lead = baseline.HomeGoalMinutes.Count(minute => minute < cutoff) - baseline.AwayGoalMinutes.Count(minute => minute < cutoff);

        if (sign * lead >= margin)
        {
            return true;
        }

        foreach (var minute in baseline.HomeGoalMinutes.Concat(baseline.AwayGoalMinutes).Where(minute => minute >= cutoff).Distinct().Order())
        {
            var home = baseline.HomeGoalMinutes.Count(goal => goal == minute);
            var away = baseline.AwayGoalMinutes.Count(goal => goal == minute);

            lead += sign > 0 ? home : -away;

            if (sign * lead >= margin)
            {
                return true;
            }

            lead += sign > 0 ? -away : home;
        }

        return false;
    }

    private static Cell Fresh(string id, string key, string plain, int from, Func<MatchSideV1, MatchRuleSubstitutionV1> sub, bool live, Func<MatchInputV1, MatchInputV1>? modify = null) =>
        new(id, id[..1], key, plain, side => [new MatchRuleV1 { Ordinal = 1, FromMinute = from, Substitutions = [sub(side)] }], (input, _, _) => modify is null ? input : modify(input), live);

    private static Cell Shape(string id, string key, string plain, int from, FormationPreset preset, MatchInstructionChangesV1? changes) =>
        Cell.Of(id, id[..1], key, plain, _ => [Rule(1, from: from, shape: preset, changes: changes)]);

    private static MatchRuleV1 OneStepDefensive() =>
        new() { Ordinal = 1, Instructions = new MatchInstructionChangesV1 { MentalitySteps = -1 }, FormationCode = null };

    private static MatchRuleV1 DefensiveLowBlock() =>
        new() { Ordinal = 1, Instructions = new MatchInstructionChangesV1 { Mentality = MatchMentality.Defensive, Pressing = MatchPressing.LowBlock, DefensiveLine = MatchDefensiveLine.Deep } };

    private static MatchRuleV1 FiveFourOneCautious() =>
        new() { Ordinal = 1, FormationCode = "5-4-1", Slots = TickPlanSmoke.SlotsOf(FormationPreset.FiveFourOne), Instructions = new MatchInstructionChangesV1 { Mentality = MatchMentality.Cautious } };

    private static MatchRuleV1 FiveFourOneDefensive() =>
        new()
        {
            Ordinal = 1,
            FormationCode = "5-4-1",
            Slots = TickPlanSmoke.SlotsOf(FormationPreset.FiveFourOne),
            Instructions = new MatchInstructionChangesV1 { Mentality = MatchMentality.Defensive, Pressing = MatchPressing.LowBlock, DefensiveLine = MatchDefensiveLine.Deep, TimeWasting = MatchTimeWasting.On },
        };

    private static MatchRuleV1 FiveFourOneFreshDefender() =>
        new()
        {
            Ordinal = 1,
            FormationCode = "5-4-1",
            Slots = TickPlanSmoke.SlotsOf(FormationPreset.FiveFourOne),
            Substitutions = [new MatchRuleSubstitutionV1 { Off = MatchRuleOff.MostTiredInFamily, OffFamily = MatchPositionFamily.Attack, On = MatchRuleOn.BestFitInFamily, OnFamily = MatchPositionFamily.Defence }],
        };

    private static MatchRuleV1 AttackingOnly() =>
        new() { Ordinal = 1, Instructions = new MatchInstructionChangesV1 { Mentality = MatchMentality.Attacking } };

    private static MatchRuleV1 FourThreeThreeChase() =>
        new()
        {
            Ordinal = 1,
            FormationCode = "4-3-3",
            Slots = TickPlanSmoke.SlotsOf(FormationPreset.FourThreeThree),
            Instructions = new MatchInstructionChangesV1 { Mentality = MatchMentality.Attacking, Pressing = MatchPressing.HighPress },
        };

    private static MatchRuleV1 FourThreeThreeChaseTwoForwards() =>
        FourThreeThreeChase() with
        {
            Substitutions =
            [
                new MatchRuleSubstitutionV1 { Off = MatchRuleOff.MostTiredInFamily, OffFamily = MatchPositionFamily.Midfield, On = MatchRuleOn.BestFitInFamily, OnFamily = MatchPositionFamily.Attack },
                new MatchRuleSubstitutionV1 { Off = MatchRuleOff.MostTiredInFamily, OffFamily = MatchPositionFamily.Midfield, On = MatchRuleOn.BestFitInFamily, OnFamily = MatchPositionFamily.Attack },
            ],
        };

    private static MatchRuleV1 AllOut() =>
        new()
        {
            Ordinal = 1,
            FormationCode = "3-4-3",
            Slots = TickPlanSmoke.SlotsOf(FormationPreset.ThreeFourThree),
            Instructions = new MatchInstructionChangesV1
            {
                Mentality = MatchMentality.Attacking,
                Pressing = MatchPressing.HighPress,
                Tempo = MatchTempo.High,
                DefensiveLine = MatchDefensiveLine.High,
            },
        };

    private static MatchRuleV1 Rule(
        int ordinal,
        int from = 0,
        MatchRuleEvent evt = MatchRuleEvent.None,
        MatchPositionFamily? family = null,
        int? threshold = null,
        FormationPreset? shape = null,
        MatchInstructionChangesV1? changes = null,
        MatchRuleSubstitutionV1[]? subs = null) =>
        new()
        {
            Ordinal = ordinal,
            FromMinute = from,
            Event = evt,
            EventFamily = family,
            EventThreshold = threshold,
            FormationCode = shape is null ? null : Code(shape.Value),
            Slots = shape is null ? null : TickPlanSmoke.SlotsOf(shape.Value),
            Instructions = changes,
            Substitutions = subs ?? [],
        };

    private static string Code(FormationPreset preset) => preset switch
    {
        FormationPreset.FourFourTwo => "4-4-2",
        FormationPreset.FourThreeThree => "4-3-3",
        FormationPreset.FourTwoThreeOne => "4-2-3-1",
        FormationPreset.ThreeFiveTwo => "3-5-2",
        FormationPreset.FourFiveOne => "4-5-1",
        FormationPreset.ThreeFourThree => "3-4-3",
        FormationPreset.FiveFourOne => "5-4-1",
        _ => preset.ToString(),
    };

    private static MatchRuleSubstitutionV1 SubjectFor(MatchPositionFamily family) =>
        new() { Off = MatchRuleOff.Subject, On = MatchRuleOn.BestFitInFamily, OnFamily = family };

    private static MatchRuleSubstitutionV1 FreshStriker(MatchSideV1 side) =>
        new() { Off = MatchRuleOff.MostTiredInFamily, OffFamily = MatchPositionFamily.Attack, On = MatchRuleOn.Player, OnParticipantId = Bench(side, MatchPosition.Striker) };

    private static MatchRuleSubstitutionV1 FreshWinger(MatchSideV1 side) =>
        new() { Off = MatchRuleOff.MostTiredInFamily, OffFamily = MatchPositionFamily.Midfield, On = MatchRuleOn.Player, OnParticipantId = Bench(side, MatchPosition.RightWinger) };

    private static Guid Bench(MatchSideV1 side, MatchPosition position)
    {
        var starters = side.Slots.Select(slot => slot.ParticipantId).ToHashSet();

        return side.Squad.First(participant => !starters.Contains(participant.ParticipantId) && participant.Position == position).ParticipantId;
    }

    /// <summary>Gives the bench winger pace and acceleration 18, the rest of him as the lab makes him.</summary>
    private static MatchInputV1 FastWinger(MatchInputV1 input)
    {
        var winger = Bench(input.Home, MatchPosition.RightWinger);

        return input with
        {
            Home = input.Home with
            {
                Squad =
                [
                    .. input.Home.Squad.Select(participant =>
                    {
                        if (participant.ParticipantId != winger)
                        {
                            return participant;
                        }

                        var values = participant.Attributes.Values.ToArray();

                        values[(int)MatchAttributeName.Pace] = 18;
                        values[(int)MatchAttributeName.Acceleration] = 18;

                        return participant with { Attributes = PlayerAttributesV1.From(values) };
                    }),
                ],
            },
        };
    }

    private static IReadOnlyList<MatchRuleV1> SmartPlan(MatchSideV1 side) =>
    [
        FourThreeThreeChase() with { Ordinal = 1, FromMinute = 60, Score = MatchRuleScore.Trailing, ScoreMargin = 1 },
        FiveFourOneCautious() with { Ordinal = 2, FromMinute = 70, Score = MatchRuleScore.Leading, ScoreMargin = 2 },
    ];

    private sealed record Pairing(string Key, string Plain, string Scenario, FormationPreset HomeShape, FormationPreset AwayShape, bool AwayAi);

    private sealed record Cell(
        string Id,
        string Family,
        string Base,
        string Plain,
        Func<MatchSideV1, IReadOnlyList<MatchRuleV1>> Rules,
        Func<MatchInputV1, ulong, Pairing, MatchInputV1> Modify,
        bool Live = false,
        Func<Rec, bool>? Gate = null)
    {
        public static Cell Of(string id, string family, string key, string plain, Func<MatchSideV1, IReadOnlyList<MatchRuleV1>> rules, bool bothAi = false, Func<Rec, bool>? gate = null) =>
            new(
                id,
                family,
                key,
                plain,
                rules,
                (input, seed, basis) => bothAi
                    ? input with
                    {
                        Home = input.Home with { MatchPlan = TickAiPlan.PlanFor(seed, 0, basis.HomeShape) },
                        Away = input.Away with { MatchPlan = TickAiPlan.PlanFor(seed, 1, basis.AwayShape) },
                    }
                    : input,
                Gate: gate);
    }

    /// <summary>What one match came to, kept so a baseline can be read again without playing it.</summary>
    public sealed class Rec
    {
        public int HomeGoals { get; set; }

        public int AwayGoals { get; set; }

        public int HomeShots { get; set; }

        public int AwayShots { get; set; }

        public int HomeCrosses { get; set; }

        public int AwayCrosses { get; set; }

        public int HomeYellows { get; set; }

        public int AwayYellows { get; set; }

        public int HomeReds { get; set; }

        public int AwayReds { get; set; }

        /// <summary>Gets or sets the sequence of the first event of the home side's rule, or 0 when none fired.</summary>
        public int FireSeq { get; set; }

        public int FireMinute { get; set; }

        public List<int> HomeGoalMinutes { get; set; } = [];

        public List<int> AwayGoalMinutes { get; set; } = [];

        public int HomeGoalsBefore { get; set; }

        public int AwayGoalsBefore { get; set; }

        public Guid Off { get; set; }

        public Guid On { get; set; }

        public int OffCondition { get; set; } = -1;

        public int OnCondition { get; set; } = -1;

        public int OpponentDefenceCondition { get; set; } = -1;

        /// <summary>Gets or sets each home player's minutes, goals, assists, shots, dribbles attempted and completed, passes, tackles and crosses.</summary>
        public Dictionary<Guid, int[]> Lines { get; set; } = [];
    }
}
