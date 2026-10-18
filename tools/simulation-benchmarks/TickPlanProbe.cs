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
/// The match-plan probe (plan i-want-you-to-curious-globe, Milestone 0): what a match looks like at the minutes a rule would fire.
/// </summary>
/// <remarks>
/// <para>
/// Every scenario is a named setup played from the same seeds, so a scenario can later be played again with a rule attached and the two
/// runs compared match by match. The engine is deterministic and draws nothing in the plan runner, so the pair is identical until the rule
/// fires and every difference after that is the rule's doing. Today no rule exists: this probe is the baseline. It writes one row a match
/// (<c>.run-logs/plans/&lt;scenario&gt;-&lt;seed&gt;.tsv</c>) so a later run can be joined to it on the match index.
/// </para>
/// <para>
/// The home side is the one a rule will belong to, and every figure that has a sign (the lead, the points) is the home side's. A match
/// minute is the engine's own: the first half's stoppage time is minute 45 and the second half's is minute 90, so "after 60" is the
/// events and actions in minutes 61 onward. The condition and the live rating are the end-of-minute figures the match centre draws.
/// </para>
/// </remarks>
internal static class TickPlanProbe
{
    private const int Entities = TickMatchRecording.Entities;
    private const int Half = Entities / 2;

    /// <summary>The minutes the score is read at: half-time and the minutes a late rule would be written for.</summary>
    private static readonly int[] Checkpoints = [45, 60, 70, 75, 80];

    /// <summary>The minutes of the trigger grid, the ones a "from minute N" rule is likely to use.</summary>
    private static readonly int[] GridMinutes = [60, 70, 75, 80];

    /// <summary>The minutes the legs are read at: where a fresh forward would meet tired defenders.</summary>
    private static readonly int[] ConditionMinutes = [60, 80];

    /// <summary>The minute the live rating is read at, and the minute output is split before and after.</summary>
    private const int SplitMinute = 60;

    private static readonly MatchPositionFamily[] Families = [MatchPositionFamily.Defence, MatchPositionFamily.Midfield, MatchPositionFamily.Attack];

    private static readonly PassageAction[] Kinds = [PassageAction.Shot, PassageAction.Cross, PassageAction.Tackle, PassageAction.Pass];

    private static readonly string[] KindNames = ["shots", "crosses", "tackles", "passes"];

    private static readonly string[] StateNames = ["lead 2+", "lead 1", "level", "trail 1", "trail 2+"];

    private static readonly double[] Percentiles = [0.05, 0.10, 0.25, 0.50, 0.75, 0.90, 0.95];

    /// <summary>The named setups. The home side is the one a rule will belong to.</summary>
    private static readonly Scenario[] Scenarios =
    [
        new("equal-442", "equal 16 against 16, 4-4-2 default both", 16, 16, 16, 16, FormationPreset.FourFourTwo, string.Empty, FormationPreset.FourFourTwo, string.Empty, true),
        new("equal-433-v-4231", "equal 16 against 16, 4-3-3 against 4-2-3-1", 16, 16, 16, 16, FormationPreset.FourThreeThree, string.Empty, FormationPreset.FourTwoThreeOne, string.Empty, true),
        new("weaker", "weaker side 16 against 18-19, 4-4-2 default both", 16, 16, 18, 19, FormationPreset.FourFourTwo, string.Empty, FormationPreset.FourFourTwo, string.Empty, true),
        new("stronger", "stronger side 18-19 against 16, 4-4-2 default both", 18, 19, 16, 16, FormationPreset.FourFourTwo, string.Empty, FormationPreset.FourFourTwo, string.Empty, true),
        new("equal-press", "equal 16 against 16, 4-4-2 both, the opponent on high press and high tempo", 16, 16, 16, 16, FormationPreset.FourFourTwo, string.Empty, FormationPreset.FourFourTwo, "press=2,tempo=2", true),
        new("equal-442-v-352", "equal 16 against 16, 4-4-2 against 3-5-2", 16, 16, 16, 16, FormationPreset.FourFourTwo, string.Empty, FormationPreset.ThreeFiveTwo, string.Empty, false),
        new("equal-tackle", "equal 16 against 16, 4-4-2 both, aggressive tackling on both sides", 16, 16, 16, 16, FormationPreset.FourFourTwo, "tackle=2", FormationPreset.FourFourTwo, "tackle=2", false),
    ];

    /// <summary>Runs the probe.</summary>
    /// <param name="matches">How many matches a scenario.</param>
    /// <param name="seed">The base seed.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="only">A scenario name (or its start), or empty or "all" for the baseline set (TP_ONLY narrows the set to a comma list of names), or "smoke" for the match-plan smoke test.</param>
    public static void Run(int matches, ulong seed, EngineRulesV2 rules, string only)
    {
        if (only.Equals("study", StringComparison.OrdinalIgnoreCase))
        {
            TickPlanStudy.Run(matches, seed, rules);

            return;
        }

        if (only.Equals("smoke", StringComparison.OrdinalIgnoreCase))
        {
            TickPlanSmoke.Run(matches, seed, rules);

            return;
        }

        var directory = Path.Combine(".run-logs", "plans");

        Directory.CreateDirectory(directory);

        var all = only.Length == 0 || only.Equals("all", StringComparison.OrdinalIgnoreCase);
        var filter = (Environment.GetEnvironmentVariable("TP_ONLY") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var selected = Scenarios
            .Where(scenario => all
                ? scenario.Baseline && (filter.Length == 0 || filter.Contains(scenario.Name, StringComparer.OrdinalIgnoreCase))
                : scenario.Name.StartsWith(only, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (selected.Length == 0)
        {
            Console.WriteLine($"No scenario matches '{only}'. Known: {string.Join(", ", Scenarios.Select(scenario => scenario.Name))}.");

            return;
        }

        var totalSeconds = 0.0;
        var pooled = new Pool();

        foreach (var scenario in selected)
        {
            var watch = Stopwatch.StartNew();
            var reads = new MatchRead[matches];

            Parallel.For(0, matches, new ParallelOptions { MaxDegreeOfParallelism = 10 }, index =>
            {
                var input = Build(scenario, seed + (ulong)index, rules);
                var live = new PlayerLiveMetricsRecorder();
                var recorder = new MatchPassageRecorder();
                var result = MatchSimulator.Simulate(input, rules, live, recorder, TickMatchEngine.Instance);

                reads[index] = recorder.Tick is { } recording
                    ? Read(index, seed + (ulong)index, input, result, live, recording)
                    : new MatchRead { Index = index, Seed = seed + (ulong)index, Missing = true };
            });

            watch.Stop();
            totalSeconds += watch.Elapsed.TotalSeconds;

            var report = Report(scenario, reads.Where(read => !read.Missing).ToArray(), seed, watch.Elapsed.TotalSeconds, pooled);

            Console.Write(report);
            File.WriteAllText(Path.Combine(directory, $"{scenario.Name}-{seed}{TickAiPlan.Tag}.txt"), report);
            File.WriteAllText(Path.Combine(directory, $"{scenario.Name}-{seed}{TickAiPlan.Tag}.tsv"), Tsv(reads.Where(read => !read.Missing).ToArray()));
            File.AppendAllText(
                Path.Combine(directory, "ledger.tsv"),
                string.Create(CultureInfo.InvariantCulture, $"{DateTime.UtcNow:yyyy-MM-dd HH:mm}\ttickplans\t{scenario.Name}{TickAiPlan.Tag}\t{matches}\t{seed}\t{watch.Elapsed.TotalSeconds:F0}\n"));
        }

        if (selected.Length > 1)
        {
            Console.Write(pooled.Report());
        }

        Console.WriteLine($"  run time this call {totalSeconds:F0} s over {selected.Length} scenario(s); every call is appended to {Path.Combine(directory, "ledger.tsv")}");
    }

    /// <summary>Builds a match of a named scenario, for the probes that attach a match plan to it.</summary>
    /// <param name="name">The scenario's name.</param>
    /// <param name="seed">The match seed.</param>
    /// <param name="rules">The rules in force.</param>
    internal static MatchInputV1 BuildNamed(string name, ulong seed, EngineRulesV2 rules) =>
        Build(Scenarios.Single(scenario => scenario.Name.Equals(name, StringComparison.OrdinalIgnoreCase)), seed, rules);

    private static MatchInputV1 Build(Scenario scenario, ulong seed, EngineRulesV2 rules)
    {
        var built = LaboratoryFixtures.Build(
            seed,
            scenario.HomeLow,
            scenario.AwayLow,
            TickShowcase.Spec(scenario.HomeSet).Instructions,
            TickShowcase.Spec(scenario.AwaySet).Instructions,
            rules);

        built = built with
        {
            Home = scenario.HomeHigh > scenario.HomeLow ? Spread(built.Home, scenario.HomeLow, scenario.HomeHigh) : built.Home,
            Away = scenario.AwayHigh > scenario.AwayLow ? Spread(built.Away, scenario.AwayLow, scenario.AwayHigh) : built.Away,
        };

        return TickAiPlan.Apply(TickCalibration.Stand(built, scenario.HomeShape, scenario.AwayShape), seed, scenario.HomeShape, scenario.AwayShape);
    }

    /// <summary>Gives each player a uniform ability from the range, cycling through it so the side's average sits mid-range.</summary>
    private static MatchSideV1 Spread(MatchSideV1 side, int low, int high) =>
        side with
        {
            Squad =
            [
                .. side.Squad.Select((player, index) => player with
                {
                    Attributes = PlayerAttributesV1.Uniform(low + ((index * 5) % (high - low + 1))),
                }),
            ],
        };

    /// <summary>The match minute of a frame: the first half's stoppage is minute 45, the second half's is minute 90.</summary>
    private static int MinuteOf(int period, int clockSecond)
    {
        var minute = (clockSecond / 60) + 1;

        return Math.Min(minute, period == 1 ? 45 : 90);
    }

    private static MatchRead Read(int index, ulong seed, MatchInputV1 input, MatchResultV1 result, PlayerLiveMetricsRecorder live, TickMatchRecording recording)
    {
        var read = new MatchRead { Index = index, Seed = seed, HomeGoals = result.HomeGoals, AwayGoals = result.AwayGoals, HomeShots = result.Home.Shots, AwayShots = result.Away.Shots };
        var familyOf = new MatchPositionFamily[Entities];
        var entityOf = new Dictionary<Guid, int>();

        foreach (var (side, sideInput) in new[] { (0, input.Home), (1, input.Away) })
        {
            foreach (var slot in sideInput.Slots)
            {
                var entity = (side * Half) + slot.SlotNumber - 1;

                familyOf[entity] = slot.Family;
                entityOf[slot.ParticipantId] = entity;
            }
        }

        // A substitute takes over his team-mate's seat, so his figures belong to the seat's family.
        foreach (var stamp in recording.Rosters.Where(stamp => stamp.Occupant != Guid.Empty))
        {
            entityOf[stamp.Occupant] = stamp.Entity;
        }

        var homeGoalMinutes = new List<int>();
        var awayGoalMinutes = new List<int>();
        var secondYellows = new int[2];

        foreach (var matchEvent in result.Events)
        {
            var side = (int)matchEvent.Side;

            if (matchEvent.IsGoal)
            {
                (side == 0 ? homeGoalMinutes : awayGoalMinutes).Add(matchEvent.Minute);
            }
            else if (matchEvent.Type == EngineEventType.YellowCard)
            {
                read.Yellows[side]++;
            }
            else if (matchEvent.Type is EngineEventType.RedCard or EngineEventType.SecondYellowCard)
            {
                read.SendOffs[side]++;

                if (matchEvent.Type == EngineEventType.SecondYellowCard)
                {
                    secondYellows[side]++;
                }

                if (read.FirstSendOffMinute == 0 || matchEvent.Minute < read.FirstSendOffMinute)
                {
                    read.FirstSendOffMinute = matchEvent.Minute;
                    read.FirstSendOffSide = side;
                }
            }
            else if (matchEvent.Type == EngineEventType.TacticalChange && matchEvent.RuleOrdinal is { } changed)
            {
                read.Fired[side] |= 1 << changed;
            }
            else if (matchEvent.Type == EngineEventType.Substitution)
            {
                read.Substitutions[side]++;

                if (matchEvent.SubstitutionReason == MatchSubstitutionReason.Tactical && matchEvent.RuleOrdinal is { } substituted)
                {
                    read.Fired[side] |= 1 << substituted;
                    read.TacticalSubstitutions[side]++;
                }

                if (matchEvent.SubstitutionReason == MatchSubstitutionReason.Fatigue)
                {
                    read.FatigueSubstitutions[side]++;
                    read.FatigueSubstitutionMinutes += matchEvent.Minute;
                }
            }
        }

        // The engine's own statistics count the same events; a difference means this probe is reading the match wrongly.
        read.Inconsistent = homeGoalMinutes.Count != result.HomeGoals
            || awayGoalMinutes.Count != result.AwayGoals
            || read.SendOffs[0] != result.Home.RedCards
            || read.SendOffs[1] != result.Away.RedCards
            || read.Yellows[0] + secondYellows[0] != result.Home.YellowCards
            || read.Yellows[1] + secondYellows[1] != result.Away.YellowCards;

        for (var checkpoint = 0; checkpoint < Checkpoints.Length; checkpoint++)
        {
            var minute = Checkpoints[checkpoint];

            read.Lead[checkpoint] = homeGoalMinutes.Count(goal => goal <= minute) - awayGoalMinutes.Count(goal => goal <= minute);
            read.HomeAfter[checkpoint] = homeGoalMinutes.Count(goal => goal > minute);
            read.AwayAfter[checkpoint] = awayGoalMinutes.Count(goal => goal > minute);
        }

        // Output by side, family and window (before and after the split minute), from the action stamps.
        var framesBefore = 0;

        for (var frame = 0; frame < recording.FrameCount; frame++)
        {
            if (MinuteOf(recording.Period(frame), recording.ClockSecond(frame)) <= SplitMinute)
            {
                framesBefore++;
            }
        }

        var share = recording.FrameCount == 0 ? 0 : (double)framesBefore / recording.FrameCount;

        read.MinutesBefore = result.TotalMinutesPlayed * share;
        read.MinutesAfter = result.TotalMinutesPlayed * (1 - share);

        foreach (var stamp in recording.Actions)
        {
            if (stamp.Entity < 0 || stamp.Frame >= recording.FrameCount)
            {
                continue;
            }

            var kind = Array.IndexOf(Kinds, stamp.Action);
            var family = Array.IndexOf(Families, familyOf[stamp.Entity]);

            if (kind < 0 || family < 0)
            {
                continue;
            }

            var window = MinuteOf(recording.Period(stamp.Frame), recording.ClockSecond(stamp.Frame)) <= SplitMinute ? 0 : 1;

            read.Output[OutputIndex(stamp.Entity / Half, family, window, kind)]++;
        }

        // The legs and the live rating at the end of the minutes a rule would look at.
        var minimum = new int[2, Families.Length];

        foreach (var side in new[] { 0, 1 })
        {
            foreach (var family in Enumerable.Range(0, Families.Length))
            {
                minimum[side, family] = int.MaxValue;
            }
        }

        var conditionSum = new long[ConditionMinutes.Length, 2, Families.Length];
        var conditionCount = new int[ConditionMinutes.Length, 2, Families.Length];

        foreach (var metric in live.Metrics)
        {
            var at = Array.IndexOf(ConditionMinutes, metric.Minute);

            if (!entityOf.TryGetValue(metric.ParticipantId, out var entity) || (at < 0 && metric.Minute != SplitMinute))
            {
                continue;
            }

            var family = Array.IndexOf(Families, familyOf[entity]);

            if (family < 0)
            {
                continue;
            }

            var side = entity / Half;

            if (at >= 0)
            {
                conditionSum[at, side, family] += metric.ConditionBasisPoints;
                conditionCount[at, side, family]++;
            }

            if (metric.Minute == SplitMinute)
            {
                read.Ratings.Add((side, family, metric.RatingBasisPoints));
                minimum[side, family] = Math.Min(minimum[side, family], metric.RatingBasisPoints);
            }
        }

        for (var at = 0; at < ConditionMinutes.Length; at++)
        {
            for (var side = 0; side < 2; side++)
            {
                for (var family = 0; family < Families.Length; family++)
                {
                    read.Condition[ConditionIndex(at, side, family)] = conditionCount[at, side, family] == 0 ? double.NaN : (double)conditionSum[at, side, family] / conditionCount[at, side, family];
                }
            }
        }

        for (var side = 0; side < 2; side++)
        {
            for (var family = 0; family < Families.Length; family++)
            {
                read.LowestRating[(side * Families.Length) + family] = minimum[side, family] == int.MaxValue ? -1 : minimum[side, family];
            }
        }

        return read;
    }

    private static int OutputIndex(int side, int family, int window, int kind) =>
        (((((side * Families.Length) + family) * 2) + window) * Kinds.Length) + kind;

    private static int ConditionIndex(int at, int side, int family) =>
        (((at * 2) + side) * Families.Length) + family;

    private static int State(int lead) => lead >= 2 ? 0 : (lead == 1 ? 1 : (lead == 0 ? 2 : (lead == -1 ? 3 : 4)));

    private static string Tsv(MatchRead[] reads)
    {
        var tsv = new StringBuilder("index\tseed\thome_goals\taway_goals\thome_shots\taway_shots");

        foreach (var minute in Checkpoints)
        {
            tsv.Append(CultureInfo.InvariantCulture, $"\tlead_{minute}");
        }

        foreach (var minute in Checkpoints)
        {
            tsv.Append(CultureInfo.InvariantCulture, $"\thome_goals_after_{minute}\taway_goals_after_{minute}");
        }

        tsv.Append("\thome_yellows\taway_yellows\thome_sendoffs\taway_sendoffs\tfirst_sendoff_minute\tfirst_sendoff_side\thome_subs\taway_subs");

        foreach (var side in new[] { "home", "away" })
        {
            foreach (var kind in KindNames)
            {
                tsv.Append(CultureInfo.InvariantCulture, $"\t{side}_{kind}_after_{SplitMinute}");
            }
        }

        foreach (var minute in ConditionMinutes)
        {
            foreach (var side in new[] { "home", "away" })
            {
                foreach (var family in Families)
                {
                    tsv.Append(CultureInfo.InvariantCulture, $"\t{side}_{family.ToString().ToLowerInvariant()}_condition_{minute}");
                }
            }
        }

        tsv.Append('\n');

        foreach (var read in reads)
        {
            tsv.Append(CultureInfo.InvariantCulture, $"{read.Index}\t{read.Seed}\t{read.HomeGoals}\t{read.AwayGoals}\t{read.HomeShots}\t{read.AwayShots}");

            foreach (var lead in read.Lead)
            {
                tsv.Append(CultureInfo.InvariantCulture, $"\t{lead}");
            }

            for (var checkpoint = 0; checkpoint < Checkpoints.Length; checkpoint++)
            {
                tsv.Append(CultureInfo.InvariantCulture, $"\t{read.HomeAfter[checkpoint]}\t{read.AwayAfter[checkpoint]}");
            }

            tsv.Append(CultureInfo.InvariantCulture, $"\t{read.Yellows[0]}\t{read.Yellows[1]}\t{read.SendOffs[0]}\t{read.SendOffs[1]}\t{read.FirstSendOffMinute}\t{read.FirstSendOffSide}\t{read.Substitutions[0]}\t{read.Substitutions[1]}");

            for (var side = 0; side < 2; side++)
            {
                for (var kind = 0; kind < Kinds.Length; kind++)
                {
                    var after = 0;

                    for (var family = 0; family < Families.Length; family++)
                    {
                        after += read.Output[OutputIndex(side, family, 1, kind)];
                    }

                    tsv.Append(CultureInfo.InvariantCulture, $"\t{after}");
                }
            }

            foreach (var value in read.Condition)
            {
                tsv.Append(CultureInfo.InvariantCulture, $"\t{(double.IsNaN(value) ? string.Empty : value.ToString("F0", CultureInfo.InvariantCulture))}");
            }

            tsv.Append('\n');
        }

        return tsv.ToString();
    }

    private static string Report(Scenario scenario, MatchRead[] reads, ulong seed, double seconds, Pool pooled)
    {
        var n = reads.Length;
        var text = new StringBuilder();

        void Line(string line) => text.Append(line).Append('\n');

        string Percent(double part, double whole) => whole == 0 ? "    -" : $"{100.0 * part / whole,5:F1}";

        var wins = reads.Count(read => read.HomeGoals > read.AwayGoals);
        var draws = reads.Count(read => read.HomeGoals == read.AwayGoals);
        var losses = n - wins - draws;
        var goals = reads.Sum(read => read.HomeGoals + read.AwayGoals);

        Line($"== tickplans {scenario.Name}{TickAiPlan.Tag}: {scenario.Plain}; {n} matches, seed {seed}, {seconds:F0} s ({n / Math.Max(seconds, 0.001):F1} matches/s) ==");
        Line($"  reading check: {reads.Count(read => read.Inconsistent)} of {n} matches disagree with the result's own goals, cards and sendings-off (must be 0)");
        Line($"  goals {(double)goals / n:F2} ({reads.Average(read => read.HomeGoals):F2}-{reads.Average(read => read.AwayGoals):F2}), shots {reads.Average(read => read.HomeShots + read.AwayShots):F1} ({reads.Average(read => read.HomeShots):F1}-{reads.Average(read => read.AwayShots):F1}); home W/D/L {Percent(wins, n)}/{Percent(draws, n)}/{Percent(losses, n)} %, home points {((3.0 * wins) + draws) / n:F2}");

        // How often each trigger state occurs, and where those matches end up.
        Line("  trigger states of the home side (a rule's score condition) and what became of them:");
        Line($"    {"state",-14} {"n",4} {"share%",6}   {"win%",5} {"draw%",5} {"lose%",5}  {"points",6}   goals for/against after that minute");

        foreach (var minute in GridMinutes)
        {
            var checkpoint = Array.IndexOf(Checkpoints, minute);

            for (var state = 0; state < StateNames.Length; state++)
            {
                var inState = reads.Where(read => State(read.Lead[checkpoint]) == state).ToArray();

                if (inState.Length == 0)
                {
                    continue;
                }

                var stateWins = inState.Count(read => read.HomeGoals > read.AwayGoals);
                var stateDraws = inState.Count(read => read.HomeGoals == read.AwayGoals);
                var stateLosses = inState.Length - stateWins - stateDraws;

                Line($"    {StateNames[state] + " at " + minute,-14} {inState.Length,4} {Percent(inState.Length, n),6}   {Percent(stateWins, inState.Length)} {Percent(stateDraws, inState.Length)} {Percent(stateLosses, inState.Length)}  {((3.0 * stateWins) + stateDraws) / inState.Length,6:F2}   {inState.Average(read => read.HomeAfter[checkpoint]):F2} / {inState.Average(read => read.AwayAfter[checkpoint]):F2}");
            }
        }

        // The late goals.
        var share60 = Percent(reads.Sum(read => read.HomeAfter[1] + read.AwayAfter[1]), goals);
        var share75 = Percent(reads.Sum(read => read.HomeAfter[3] + read.AwayAfter[3]), goals);

        Line($"  goals after 60': {reads.Average(read => read.HomeAfter[1]):F2} home, {reads.Average(read => read.AwayAfter[1]):F2} away, {share60} % of all goals; after 75': {reads.Average(read => read.HomeAfter[3]):F2} home, {reads.Average(read => read.AwayAfter[3]):F2} away, {share75} % of all goals");

        // Cards and substitutions.
        var sendOffMatches = reads.Count(read => read.SendOffs[0] + read.SendOffs[1] > 0);

        Line($"  bookings: a match {reads.Average(read => read.Yellows[0] + read.Yellows[1]):F2} (home {reads.Average(read => read.Yellows[0]):F2}, away {reads.Average(read => read.Yellows[1]):F2}); matches with a booking {Percent(reads.Count(read => read.Yellows[0] + read.Yellows[1] > 0), n)} %, home booked {Percent(reads.Count(read => read.Yellows[0] > 0), n)} %");
        Line($"  sendings-off: matches with one {Percent(sendOffMatches, n)} % (home player {Percent(reads.Count(read => read.SendOffs[0] > 0), n)} %, away player {Percent(reads.Count(read => read.SendOffs[1] > 0), n)} %), before 60': {Percent(reads.Count(read => read.FirstSendOffMinute is > 0 and <= SplitMinute), n)} %");

        var fatigueSubstitutions = reads.Sum(read => read.FatigueSubstitutions[0] + read.FatigueSubstitutions[1]);

        if (TickAiPlan.Sides.Length > 0)
        {
            var names = AiMatchPlanPolicy.Decide(Guid.Empty, FormationPreset.FourFourTwo, TeamInstructionSet.Neutral).Instructions.Select(instruction => instruction.Name).ToArray();

            Line($"  AI match plan on the {TickAiPlan.Sides} side(s), {(TickAiPlan.RealBench ? "a real club's bench" : "the lab bench (keeper and central midfielders)")}; share of matches in which each rule visibly did something (home / away), tactical substitutions a side:");

            for (var rule = 0; rule < names.Length; rule++)
            {
                Line($"    {names[rule],-26} {Percent(reads.Count(read => (read.Fired[0] & (1 << (rule + 1))) != 0), n)} / {Percent(reads.Count(read => (read.Fired[1] & (1 << (rule + 1))) != 0), n)} %");
            }

            Line($"    tactical substitutions a match: home {reads.Average(read => read.TacticalSubstitutions[0]):F2}, away {reads.Average(read => read.TacticalSubstitutions[1]):F2}");
        }

        Line($"  substitutions a side per match: home {reads.Average(read => read.Substitutions[0]):F2}, away {reads.Average(read => read.Substitutions[1]):F2}; fatigue ones {(double)fatigueSubstitutions / n / 2:F2} a side, at minute {(fatigueSubstitutions == 0 ? 0 : (double)reads.Sum(read => read.FatigueSubstitutionMinutes) / fatigueSubstitutions):F0} on average");

        // Output per man per 90 minutes of play, before and after the split minute.
        Line($"  output per man per 90 minutes, before the {SplitMinute}th minute -> after it (a family's seats; substitutes count in their seat):");
        Line($"    {"",-16} {string.Join(" ", KindNames.Select(name => $"{name,16}"))}");

        for (var side = 0; side < 2; side++)
        {
            for (var family = 0; family < Families.Length; family++)
            {
                var seats = Seats(scenario, side, family);
                var cells = new List<string>();

                for (var kind = 0; kind < Kinds.Length; kind++)
                {
                    var before = reads.Sum(read => read.Output[OutputIndex(side, family, 0, kind)]) / (reads.Sum(read => read.MinutesBefore) * seats) * 90.0;
                    var after = reads.Sum(read => read.Output[OutputIndex(side, family, 1, kind)]) / (reads.Sum(read => read.MinutesAfter) * seats) * 90.0;

                    cells.Add($"{before,6:F2} -> {after,6:F2}");
                }

                Line($"    {(side == 0 ? "home " : "away ") + Families[family].ToString().ToLowerInvariant(),-16} {string.Join("   ", cells)}");
            }
        }

        // The legs.
        Line("  condition (% of full) at the end of the minute, mean of a family and its 10th percentile over matches:");

        for (var at = 0; at < ConditionMinutes.Length; at++)
        {
            var cells = new List<string>();

            for (var side = 0; side < 2; side++)
            {
                for (var family = 0; family < Families.Length; family++)
                {
                    var values = reads.Select(read => read.Condition[ConditionIndex(at, side, family)]).Where(value => !double.IsNaN(value)).Order().ToArray();

                    cells.Add(values.Length == 0 ? "   -" : $"{(side == 0 ? "H" : "A")}{Families[family].ToString()[0]} {values.Average() / 100.0,5:F1} ({values[(int)Math.Round(0.1 * (values.Length - 1))] / 100.0:F1})");
                }
            }

            Line($"    at {ConditionMinutes[at]}':  {string.Join("   ", cells)}");
        }

        // The live rating, which the "playing badly" trigger will read.
        Line($"  live rating at {SplitMinute}' (out of 10), outfield players of both sides; percentiles {string.Join(" ", Percentiles.Select(p => $"p{p * 100:F0}"))}:");

        var allRatings = reads.SelectMany(read => read.Ratings).ToArray();

        foreach (var (name, picked) in Families.Select((family, index) => (Families[index].ToString().ToLowerInvariant(), allRatings.Where(rating => rating.Family == index).Select(rating => rating.Rating).ToArray())).Append(("all outfield", allRatings.Select(rating => rating.Rating).ToArray())))
        {
            Line($"    {name,-14} n {picked.Length,5}: {PercentileText(picked, 1_000.0)}");
        }

        Line("  lowest-rated man of a family at that minute (one value a side per match):");

        for (var family = 0; family < Families.Length; family++)
        {
            var lowest = reads.SelectMany(read => new[] { read.LowestRating[family], read.LowestRating[Families.Length + family] }).Where(value => value >= 0).ToArray();

            Line($"    {Families[family].ToString().ToLowerInvariant(),-14} n {lowest.Length,5}: {PercentileText(lowest, 1_000.0)}");
        }

        Line(string.Empty);
        pooled.Add(reads);

        return text.ToString();
    }

    private static int Seats(Scenario scenario, int side, int family)
    {
        var layout = FormationLayouts.DefaultSlots(side == 0 ? scenario.HomeShape : scenario.AwayShape);

        return layout.Count(slot => EngineVocabulary.Family(slot.PositionFamily) == Families[family]);
    }

    private static string PercentileText(int[] values, double scale)
    {
        if (values.Length == 0)
        {
            return "-";
        }

        var sorted = values.Order().ToArray();

        return string.Join(" ", Percentiles.Select(p => $"{sorted[(int)Math.Round(p * (sorted.Length - 1))] / scale,5:F2}"));
    }

    /// <summary>Every scenario's ratings, so the "playing badly" thresholds can be set on all the bases at once.</summary>
    private sealed class Pool
    {
        private readonly List<(int Side, int Family, int Rating)> _ratings = [];

        public void Add(MatchRead[] reads) => _ratings.AddRange(reads.SelectMany(read => read.Ratings));

        public string Report()
        {
            var text = new StringBuilder("== all the scenarios of this call together ==\n");

            text.Append(CultureInfo.InvariantCulture, $"  live rating at {SplitMinute}' (out of 10), outfield players; percentiles {string.Join(" ", Percentiles.Select(p => $"p{p * 100:F0}"))}:\n");

            for (var family = 0; family < Families.Length; family++)
            {
                var picked = _ratings.Where(rating => rating.Family == family).Select(rating => rating.Rating).ToArray();

                text.Append(CultureInfo.InvariantCulture, $"    {Families[family].ToString().ToLowerInvariant(),-14} n {picked.Length,6}: {PercentileText(picked, 1_000.0)}\n");
            }

            text.Append(CultureInfo.InvariantCulture, $"    {"all outfield",-14} n {_ratings.Count,6}: {PercentileText([.. _ratings.Select(rating => rating.Rating)], 1_000.0)}\n\n");

            return text.ToString();
        }
    }

    private sealed record Scenario(
        string Name,
        string Plain,
        int HomeLow,
        int HomeHigh,
        int AwayLow,
        int AwayHigh,
        FormationPreset HomeShape,
        string HomeSet,
        FormationPreset AwayShape,
        string AwaySet,
        bool Baseline);

    /// <summary>What one match came to at the minutes a rule would look at.</summary>
    private sealed class MatchRead
    {
        public int Index { get; init; }

        public ulong Seed { get; init; }

        public bool Missing { get; init; }

        /// <summary>Gets whether the events counted here disagree with the result's own statistics.</summary>
        public bool Inconsistent { get; set; }

        public int HomeGoals { get; init; }

        public int AwayGoals { get; init; }

        public int HomeShots { get; init; }

        public int AwayShots { get; init; }

        /// <summary>Gets the home lead at each of <see cref="Checkpoints"/>.</summary>
        public int[] Lead { get; } = new int[Checkpoints.Length];

        /// <summary>Gets the goals the home side scored after each checkpoint.</summary>
        public int[] HomeAfter { get; } = new int[Checkpoints.Length];

        /// <summary>Gets the goals the away side scored after each checkpoint.</summary>
        public int[] AwayAfter { get; } = new int[Checkpoints.Length];

        public int[] Yellows { get; } = new int[2];

        public int[] SendOffs { get; } = new int[2];

        public int FirstSendOffMinute { get; set; }

        public int FirstSendOffSide { get; set; }

        public int[] Substitutions { get; } = new int[2];

        public int[] FatigueSubstitutions { get; } = new int[2];

        /// <summary>Gets the rules (by ordinal, as a bit each) that visibly did something for each side: a tactical change or a tactical substitution.</summary>
        public int[] Fired { get; } = new int[2];

        public int[] TacticalSubstitutions { get; } = new int[2];

        public int FatigueSubstitutionMinutes { get; set; }

        public double MinutesBefore { get; set; }

        public double MinutesAfter { get; set; }

        /// <summary>Gets the counts by side, family, window and kind of action.</summary>
        public int[] Output { get; } = new int[2 * 3 * 2 * 4];

        /// <summary>Gets the mean condition by minute, side and family, or NaN where nobody of that family was on.</summary>
        public double[] Condition { get; } = new double[2 * 2 * 3];

        /// <summary>Gets each outfield player's live rating at the split minute.</summary>
        public List<(int Side, int Family, int Rating)> Ratings { get; } = [];

        /// <summary>Gets the lowest live rating of each side's families at the split minute, or -1.</summary>
        public int[] LowestRating { get; } = new int[2 * 3];
    }
}
