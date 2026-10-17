using System.Text.Json;
using Tactical11Manager.Application.Match;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.MatchEngine;
using Tactical11Manager.MatchEngine.Commentary;
using Tactical11Manager.MatchEngine.Configuration;
using Tactical11Manager.MatchEngine.Highlights;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Tick;
using System.Globalization;

namespace Tactical11Manager.SimulationBenchmarks;

/// <summary>
/// Plays one match between two uniform sides of chosen ability on the tick engine, prints what it came to and writes its film.
/// </summary>
/// <remarks>
/// <c>tickshow [homeAbility] [awayAbility] [seed] --dump file</c>: the ability is the value every attribute of every player in the side
/// takes, so a side of 17 has core skills of 17 and a side of 6 has core skills of 6.
/// </remarks>
internal static class TickShowcase
{
    /// <summary>How long after a regain a shot still counts as the break: twelve seconds at ten frames a second.</summary>
    private const int BreakFrames = 120;

    /// <summary>Where the weaker side's own third starts: it defends the goal at X = 10 000, so a third is 3 333 units.</summary>
    private const int OwnThirdFromX = 6_667;

    /// <summary>How long the weaker side must keep a regain to count as kept: ten seconds.</summary>
    private const int KeptFrames = 100;

    /// <summary>How soon after a regain a loss counts as quick: five seconds.</summary>
    private const int QuickFrames = 50;

    /// <summary>The longest flight a pass is followed for to see where it ends: six seconds.</summary>
    private const int FlightFrames = 60;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Plays the match.</summary>
    /// <param name="homeAbility">The home side's uniform attribute value.</param>
    /// <param name="awayAbility">The away side's uniform attribute value.</param>
    /// <param name="seed">The match seed.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="dump">Where to write the film, or null.</param>
    /// <param name="homeSpec">The home side's setup in the <c>tickexp</c> spec language (<c>ment=1,counter=1,...</c>), or empty for the default 4-4-2.</param>
    /// <param name="awaySpec">The away side's setup, likewise.</param>
    public static void Run(string homeAbility, string awayAbility, ulong seed, EngineRulesV2 rules, string? dump, string homeSpec = "", string awaySpec = "")
    {
        var (homeLow, homeHigh) = Range(homeAbility);
        var (awayLow, awayHigh) = Range(awayAbility);
        var (homeForm, homeInstructions, homeKeeper, homePace) = Spec(homeSpec);
        var (awayForm, awayInstructions, awayKeeper, awayPace) = Spec(awaySpec);
        var built = LaboratoryFixtures.Build(seed, homeLow, awayLow, homeInstructions, awayInstructions, rules);
        var input = TickCalibration.Stand(
            built with { Home = Paced(Keeper(Spread(built.Home, homeLow, homeHigh), homeKeeper), homePace), Away = Paced(Keeper(Spread(built.Away, awayLow, awayHigh), awayKeeper), awayPace) },
            homeForm,
            awayForm);
        var liveMetrics = new PlayerLiveMetricsRecorder();
        var recorder = new MatchPassageRecorder();
        var result = MatchSimulator.Simulate(input, rules, liveMetrics, recorder, TickMatchEngine.Instance);
        var presentation = ReplayDirector.Build(input, result, recorder, liveMetrics: liveMetrics.Metrics);

        Console.WriteLine($"== Tick showcase: home {homeAbility} v away {awayAbility}, seed {seed} ==");
        Console.WriteLine($"  attribute average {Mean(input.Home.Squad.Count, homeLow, homeHigh):F1} - {Mean(input.Away.Squad.Count, awayLow, awayHigh):F1}");
        Console.WriteLine($"  score            {result.HomeGoals} - {result.AwayGoals}");
        Console.WriteLine($"  shots            {result.Home.Shots} - {result.Away.Shots}");
        Console.WriteLine($"  on target        {result.Home.ShotsOnTarget} - {result.Away.ShotsOnTarget}");
        Console.WriteLine($"  corners          {result.Home.Corners} - {result.Away.Corners}");
        Console.WriteLine($"  fouls            {result.Home.Fouls} - {result.Away.Fouls}");
        Console.WriteLine($"  yellow cards     {result.Home.YellowCards} - {result.Away.YellowCards}");
        Console.WriteLine($"  film             {presentation.TotalPlaybackMilliseconds / 1_000.0:F0} s, {presentation.Passages.Count} passages");

        if (dump is not null)
        {
            var response = presentation.ToResponse(Guid.Empty, CommentaryTokenBuilder.Build(input, result));
            var serialized = JsonSerializer.SerializeToUtf8Bytes(response, Json);

            File.WriteAllBytes(dump, serialized);
            Console.WriteLine($"  dumped to {dump} ({serialized.Length / 1024.0:F0} KB)");
        }
    }

    /// <summary>Plays many matches between the two sides and writes one tab-separated row each: seed, goals, shots and shots on target, home then away.</summary>
    /// <param name="homeAbility">The home side's ability, a number or a range.</param>
    /// <param name="awayAbility">The away side's ability, a number or a range.</param>
    /// <param name="matches">How many matches.</param>
    /// <param name="seed">The base seed.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="path">The file to write.</param>
    public static void Many(string homeAbility, string awayAbility, int matches, ulong seed, EngineRulesV2 rules, string path)
    {
        var (homeLow, homeHigh) = Range(homeAbility);
        var (awayLow, awayHigh) = Range(awayAbility);
        var rows = new string[matches];

        Parallel.For(0, matches, index =>
        {
            var built = LaboratoryFixtures.Build(seed + (ulong)index, homeLow, awayLow, new(), new(), rules);
            var input = TickCalibration.Stand(
                built with { Home = Spread(built.Home, homeLow, homeHigh), Away = Spread(built.Away, awayLow, awayHigh) },
                FormationPreset.FourFourTwo,
                FormationPreset.FourFourTwo);
            var result = MatchSimulator.Simulate(input, rules, null, null, TickMatchEngine.Instance);

            rows[index] = string.Join(
                '	',
                seed + (ulong)index,
                result.HomeGoals,
                result.AwayGoals,
                result.Home.Shots,
                result.Away.Shots,
                result.Home.ShotsOnTarget,
                result.Away.ShotsOnTarget);
        });

        File.WriteAllLines(path, rows);
        Console.WriteLine($"  wrote {matches} matches to {path}");
    }

    /// <summary>
    /// Plays many matches between two configured sides and prints one summary: the results, the finishing, and where the shots came from.
    /// </summary>
    /// <remarks>
    /// A side spec is a comma-separated list of <c>key=value</c>: form (a <see cref="FormationPreset"/> number), ment, tempo, pass, width, focus, press,
    /// line, tackle, waste (the enum numbers), counter (0 or 1), gk (the keeper's ability) and pace (Pace and Acceleration of every outfield player). Anything left out is the default.
    /// </remarks>
    /// <param name="label">What the row is called.</param>
    /// <param name="homeAbility">The home side's ability, a number or a range.</param>
    /// <param name="awayAbility">The away side's ability, a number or a range.</param>
    /// <param name="homeSpec">The home side's formation and instructions.</param>
    /// <param name="awaySpec">The away side's formation and instructions.</param>
    /// <param name="matches">How many matches.</param>
    /// <param name="seed">The base seed.</param>
    /// <param name="rules">The rules in force.</param>
    public static void Experiment(string label, string homeAbility, string awayAbility, string homeSpec, string awaySpec, int matches, ulong seed, EngineRulesV2 rules)
    {
        var cell = Measure(homeAbility, awayAbility, homeSpec, awaySpec, matches, seed, rules);

        Console.WriteLine(
            $"{label,-34} n={matches} W/D/L {100.0 * cell.Wins / matches,3:F0}/{100.0 * cell.Draws / matches,3:F0}/{100.0 * cell.Losses / matches,3:F0}  pts {cell.HomePoints:F2}  goals {cell.Goals[0]:F2}-{cell.Goals[1]:F2}  shots {cell.Shots[0]:F1}-{cell.Shots[1]:F1}  sot {cell.OnTarget[0]:F1}-{cell.OnTarget[1]:F1}  goals/sot {100.0 * cell.GoalsPerOnTarget[0]:F0}%-{100.0 * cell.GoalsPerOnTarget[1]:F0}%  fouls {cell.Fouls[0]:F1}-{cell.Fouls[1]:F1}");

        if (Environment.GetEnvironmentVariable("TICK_SHOTS") is not { Length: > 0 })
        {
            return;
        }

        foreach (var side in new[] { 0, 1 })
        {
            var tally = cell.PerShot[side];

            Console.WriteLine($"    {(side == 0 ? "home" : "away")} open-play shots {tally.Shots} ({tally.Shots / (double)matches:F1}/match), mean distance {tally.DistanceSum / Math.Max(1, tally.Shots) / 100.0:F1} m, breaks {tally.Breaks / (double)matches:F2}/match of {tally.Regains / (double)matches:F1} regains");

            for (var band = 0; band < ShotTally.Bands.Length; band++)
            {
                var n = tally.BandShots[band];

                Console.WriteLine(
                    $"      {ShotTally.Bands[band],-8} shots {n,5} ({100.0 * n / Math.Max(1, tally.Shots),4:F0}%)  goal {Share(tally.BandGoals[band], n)}  saved {Share(tally.BandSaved[band], n)}  blocked {Share(tally.BandBlocked[band], n)}  off {Share(tally.BandOff[band], n)}  goals/on-target {Share(tally.BandGoals[band], tally.BandGoals[band] + tally.BandSaved[band])}");
            }
        }
    }

    /// <summary>
    /// Plays the fixed table of scenarios the tick engine's tactical balance is judged by and prints one row per cell: the results, the
    /// shots and where they came from, and how often a ball won in a side's own half became a shot within twelve seconds (a break).
    /// </summary>
    /// <remarks>
    /// Every scenario fixes both goalkeepers so the keeper cliff never enters. Four scenarios: S1 a side of 17-20 at home to 15-18, S2 17-20
    /// to 13-15, S3 17-20 to 6, and S4 two sides of 16 in the full matrix of setups. Setups: DEF (the default), POS, ATT, COUNTER and BUS.
    /// </remarks>
    /// <param name="matches">How many matches a cell.</param>
    /// <param name="seed">The base seed.</param>
    /// <param name="quick">Whether to play only S1 and the S4 cells with COUNTER, for iterating.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="only">The names (or prefixes, such as S6) of the scenarios to play; empty plays S1 to S4.</param>
    public static void Balance(int matches, ulong seed, bool quick, EngineRulesV2 rules, string[] only)
    {
        var setups = new Dictionary<string, string>
        {
            ["DEF"] = string.Empty,
            ["POS"] = "ment=3,press=2,line=2,tempo=2",
            ["ATT"] = "ment=4,press=2,line=2,tempo=2",
            ["COUNTER"] = "form=12,ment=1,press=0,line=0,pass=2,counter=1",
            ["BUS"] = "form=12,ment=0,press=0,line=0,counter=1,waste=1",
            ["BUS0"] = "form=12,ment=0,press=0,line=0,counter=1,waste=0",
            ["BUS2"] = "form=12,ment=0,press=0,line=0,counter=1,waste=2",
            ["DEFW2"] = "waste=2",
            ["DEFW1"] = "waste=1",
            ["DEEP"] = "form=12,ment=0,press=0,line=0",
            ["COUNTER0"] = "form=12,ment=1,press=0,line=0,pass=2,counter=0",
        };
        string[] matrix = ["DEF", "POS", "ATT", "COUNTER", "BUS"];
        string[] deepOrCounter = ["COUNTER", "COUNTER0", "DEEP"];
        var scenarios = new List<(string Name, string Home, string Away, int HomeKeeper, int AwayKeeper, int HomePace, int AwayPace, string[] HomeSetups, string[] AwaySetups)>
        {
            ("S1", "17-20", "15-18", 17, 15, 0, 0, ["DEF", "POS"], ["DEF", "POS", "COUNTER", "COUNTER0", "DEEP", "BUS"]),
            ("S2", "17-20", "13-15", 17, 13, 0, 0, ["DEF", "POS"], ["DEF", "POS", "COUNTER", "COUNTER0", "DEEP", "BUS"]),
            ("S3", "17-20", "6", 17, 6, 0, 0, ["DEF"], ["DEF", "COUNTER", "BUS"]),
            ("S4", "16", "16", 16, 16, 0, 0, matrix, matrix),
            ("S5", "16", "16", 16, 16, 0, 0, ["DEF", "POS", "ATT", "DEEP"], ["COUNTER", "COUNTER0"]),
            ("S7", "17-20", "15-18", 17, 15, 0, 0, ["DEF", "POS"], ["BUS0", "BUS", "BUS2"]),
            ("S7h", "17-20", "15-18", 17, 15, 0, 0, ["DEF", "DEFW1", "DEFW2"], ["DEF"]),
            ("S7e", "16", "16", 16, 16, 0, 0, ["DEF"], ["DEF", "DEFW2"]),
            ("S6n", "17-20", "15-18", 17, 15, 0, 0, ["DEF", "POS"], deepOrCounter),
            ("S6a", "17-20", "15-18", 17, 15, 20, 16, ["DEF", "POS"], deepOrCounter),
            ("S6b", "17-20", "15-18", 17, 15, 16, 19, ["DEF", "POS"], deepOrCounter),
            ("S6c", "14", "14", 14, 14, 15, 13, ["DEF", "POS"], deepOrCounter),
        };
        var awayOnly = (Environment.GetEnvironmentVariable("TB_AWAY") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);
        var homeOnly = (Environment.GetEnvironmentVariable("TB_HOME") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);
        var chosen = only.Length > 0
            ? scenarios.Where(scenario => only.Any(name => scenario.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase))).ToList()
            : [.. scenarios.Where(scenario => scenario.Name is "S1" or "S2" or "S3" or "S4")];

        Console.WriteLine($"== tickbalance: {matches} matches a cell, seed {seed}{(quick ? ", quick subset" : string.Empty)} ==");
        Console.WriteLine("   home v away (setup)                    W/D/L %  pts home-away  goals       shots       sot         <16 m shots  goals/sot  breaks/match (regains)");

        foreach (var (name, home, away, homeKeeper, awayKeeper, homePace, awayPace, homeSetups, awaySetups) in chosen)
        {
            if (quick && name is "S2" or "S3")
            {
                continue;
            }

            foreach (var homeSetup in homeSetups.Where(setup => homeOnly.Length == 0 || homeOnly.Contains(setup)))
            {
                foreach (var awaySetup in awaySetups)
                {
                    if (awayOnly.Length > 0 && !awayOnly.Contains(awaySetup))
                    {
                        continue;
                    }

                    if (quick && name == "S4" && homeSetup != "COUNTER" && awaySetup != "COUNTER")
                    {
                        continue;
                    }

                    var homeSpec = $"gk={homeKeeper}{(homePace > 0 ? $",pace={homePace}" : string.Empty)}{Join(setups[homeSetup])}";
                    var awaySpec = $"gk={awayKeeper}{(awayPace > 0 ? $",pace={awayPace}" : string.Empty)}{Join(setups[awaySetup])}";
                    var cell = Measure(home, away, homeSpec, awaySpec, matches, seed, rules);
                    var insideSixteen = cell.PerShot.Select(tally => (tally.BandShots[0] + tally.BandShots[1] + tally.BandShots[2]) / (double)matches).ToArray();
                    var breaks = cell.PerShot.Select(tally => (Breaks: tally.Breaks / (double)matches, Regains: tally.Regains / (double)matches)).ToArray();

                    Console.WriteLine(
                        $"{name} {home,5} {homeSetup,-7} v {away,5} {awaySetup,-7} {100.0 * cell.Wins / matches,3:F0}/{100.0 * cell.Draws / matches,3:F0}/{100.0 * cell.Losses / matches,3:F0}  {cell.HomePoints:F2}-{cell.AwayPoints:F2}  {cell.Goals[0]:F2}-{cell.Goals[1]:F2}  {cell.Shots[0]:F1}-{cell.Shots[1]:F1}  {cell.OnTarget[0]:F1}-{cell.OnTarget[1]:F1}  {insideSixteen[0]:F1}-{insideSixteen[1]:F1}  {100.0 * cell.GoalsPerOnTarget[0]:F0}%-{100.0 * cell.GoalsPerOnTarget[1]:F0}%  {breaks[0].Breaks:F2} ({breaks[0].Regains:F1}) - {breaks[1].Breaks:F2} ({breaks[1].Regains:F1})  cond {cell.Condition[0]:F0}-{cell.Condition[1]:F0}  live {cell.LiveSeconds:F0} s  led70 {cell.Led70[0]:F0}/{cell.Held[0]:F0}-{cell.Led70[1]:F0}/{cell.Held[1]:F0}");
                }
            }
        }

        static string Join(string spec) => spec.Length == 0 ? string.Empty : "," + spec;
    }

    /// <summary>One cell of an experiment: the per-match averages and the per-shot tallies of the many matches played.</summary>
    private sealed record Cell(
        int Wins,
        int Draws,
        int Losses,
        double[] Goals,
        double[] Shots,
        double[] OnTarget,
        double[] Fouls,
        double[] GoalsPerOnTarget,
        ShotTally[] PerShot,
        double[] Condition,
        double LiveSeconds,
        int[] Led70,
        int[] Held)
    {
        public double HomePoints => ((3.0 * Wins) + Draws) / (Wins + Draws + Losses);

        public double AwayPoints => ((3.0 * Losses) + Draws) / (Wins + Draws + Losses);
    }

    private static Cell Measure(string homeAbility, string awayAbility, string homeSpec, string awaySpec, int matches, ulong seed, EngineRulesV2 rules)
    {
        var (homeLow, homeHigh) = Range(homeAbility);
        var (awayLow, awayHigh) = Range(awayAbility);
        var (homeForm, homeInstructions, homeKeeper, homePace) = Spec(homeSpec);
        var (awayForm, awayInstructions, awayKeeper, awayPace) = Spec(awaySpec);
        var goals = new int[matches, 2];
        var shots = new int[matches, 2];
        var onTarget = new int[matches, 2];
        var fouls = new int[matches, 2];
        var perShot = new ShotTally[] { new(), new() };
        var condition = new double[matches, 2];
        var live = new double[matches];
        var led70 = new int[matches, 2];
        var held = new int[matches, 2];
        var gate = new object();

        Parallel.For(0, matches, index =>
        {
            var built = LaboratoryFixtures.Build(seed + (ulong)index, homeLow, awayLow, homeInstructions, awayInstructions, rules);
            var input = TickCalibration.Stand(
                built with { Home = Paced(Keeper(Spread(built.Home, homeLow, homeHigh), homeKeeper), homePace), Away = Paced(Keeper(Spread(built.Away, awayLow, awayHigh), awayKeeper), awayPace) },
                homeForm,
                awayForm);
            var recorder = new MatchPassageRecorder();
            var liveMetrics = new PlayerLiveMetricsRecorder();
            var result = MatchSimulator.Simulate(input, rules, liveMetrics, recorder, TickMatchEngine.Instance);

            foreach (var side in Enumerable.Range(0, 2))
            {
                var starters = (side == 0 ? input.Home : input.Away).Slots.Where(slot => slot.Family != MatchPositionFamily.Goalkeeper).Select(slot => slot.ParticipantId).ToHashSet();
                var last = new Dictionary<Guid, (int Minute, int Condition)>();

                foreach (var metric in liveMetrics.Metrics.Where(metric => starters.Contains(metric.ParticipantId) && metric.Minute <= 85))
                {
                    if (!last.TryGetValue(metric.ParticipantId, out var seen) || metric.Minute >= seen.Minute)
                    {
                        last[metric.ParticipantId] = (metric.Minute, metric.ConditionBasisPoints);
                    }
                }

                condition[index, side] = last.Count == 0 ? 0 : last.Values.Average(entry => entry.Condition);
            }

            var early = new int[2];

            foreach (var goal in result.Events.Where(e => e.IsGoal && e.Minute <= 70))
            {
                early[(int)goal.Side]++;
            }

            foreach (var side in Enumerable.Range(0, 2))
            {
                var ahead = early[side] > early[1 - side];
                var mine = side == 0 ? result.HomeGoals : result.AwayGoals;
                var theirs = side == 0 ? result.AwayGoals : result.HomeGoals;

                led70[index, side] = ahead ? 1 : 0;
                held[index, side] = ahead && mine > theirs ? 1 : 0;
            }

            goals[index, 0] = result.HomeGoals;
            goals[index, 1] = result.AwayGoals;
            shots[index, 0] = result.Home.Shots;
            shots[index, 1] = result.Away.Shots;
            onTarget[index, 0] = result.Home.ShotsOnTarget;
            onTarget[index, 1] = result.Away.ShotsOnTarget;
            fouls[index, 0] = result.Home.Fouls;
            fouls[index, 1] = result.Away.Fouls;

            if (recorder.Tick is { } recording)
            {
                live[index] = Enumerable.Range(0, recording.FrameCount).Count(frame => recording.State(frame) == TickPlayState.OpenPlay) / 10.0;

                lock (gate)
                {
                    Tally(result, recording, perShot);
                }
            }
        });

        double[] Averages2(double[,] table) => [.. Enumerable.Range(0, 2).Select(side => Enumerable.Range(0, matches).Average(index => table[index, side]))];

        double[] Averages(int[,] table) => [.. Enumerable.Range(0, 2).Select(side => Enumerable.Range(0, matches).Average(index => table[index, side]))];

        var wins = Enumerable.Range(0, matches).Count(index => goals[index, 0] > goals[index, 1]);
        var draws = Enumerable.Range(0, matches).Count(index => goals[index, 0] == goals[index, 1]);
        var totalGoals = Enumerable.Range(0, 2).Select(side => Enumerable.Range(0, matches).Sum(index => goals[index, side])).ToArray();
        var totalOnTarget = Enumerable.Range(0, 2).Select(side => Enumerable.Range(0, matches).Sum(index => onTarget[index, side])).ToArray();

        return new Cell(
            wins,
            draws,
            matches - wins - draws,
            Averages(goals),
            Averages(shots),
            Averages(onTarget),
            Averages(fouls),
            [.. Enumerable.Range(0, 2).Select(side => totalGoals[side] / (double)Math.Max(1, totalOnTarget[side]))],
            perShot,
            Averages2(condition),
            live.Average(),
            [.. Enumerable.Range(0, 2).Select(side => Enumerable.Range(0, matches).Sum(index => led70[index, side]))],
            [.. Enumerable.Range(0, 2).Select(side => Enumerable.Range(0, matches).Sum(index => held[index, side]))]);
    }

    /// <summary>The numbers of one `tickwhy` cell: sums over the matches, divided by the match count when printed.</summary>
    private sealed class WhyTally
    {
        public int Wins;
        public int Draws;
        public int Losses;
        public double GoalsStrong;
        public double GoalsWeak;
        public double ShotsStrong;
        public double ShotsWeak;
        public double PossessionFramesWeak;
        public double OpenFrames;
        public double StrongRegains;
        public double StrongRegainsInOwnHalf;
        public double StrongTransitionShots;
        public double StrongTransitionGoals;
        public double StrongTransitionShotsOwnHalf;
        public double BehindOwnHalf;
        public double BehindOppHalf;
        public double LineDepthOwnHalf;
        public double LineDepthOppHalf;
        public double WeakRegains;
        public double WeakRegainsHigh;
        public double WeakTransitionShots;
        public double WeakTransitionGoals;
        public double OwnThirdRegains;
        public double OwnThirdKept;
        public double OwnThirdLostQuick;
        public double OwnThirdLostSlow;
        public double OwnThirdStopped;
        public double OwnThirdNearest;
        public double OwnThirdLostStrongShots;
        public double[] OwnThirdLostHow { get; } = new double[4];
        public double[] OwnThirdLostWhere { get; } = new double[3];
        public double[] OwnThirdLostKick { get; } = new double[6];
        public double[] OwnThirdKickWeakThird { get; } = new double[6];
        public double[] OwnThirdKickShots { get; } = new double[6];
        public double[] OwnThirdFirst { get; } = new double[7];
        public double[] OwnThirdFirstLost { get; } = new double[7];
        public double[] ExposedRegains { get; } = new double[11];
        public double[] ExposedShots { get; } = new double[11];
        public double[] WeakGoalsByPeriod { get; } = new double[6];
        public double[] StrongGoalsByPeriod { get; } = new double[6];
        public double[] WeakShotsByPeriod { get; } = new double[6];
        public double[] StrongShotsByPeriod { get; } = new double[6];
    }

    /// <summary>
    /// `tickwhy`: why does a weaker side that attacks all-out take more points than one that sits deep? Plays an 18-19 side at home to a
    /// 15-16 side in four setups and reads from the recording what happens when the weaker side loses the ball (how many of its players are
    /// behind the ball, how deep its line is, what the stronger side makes of it) and when it wins it (a high regain, a shot from it).
    /// </summary>
    /// <param name="matches">How many matches a cell.</param>
    /// <param name="seed">The base seed.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="weak">The weaker side's ability, a number or a range.</param>
    public static void Why(int matches, ulong seed, EngineRulesV2 rules, string weak = "15-16")
    {
        var (weakLow, weakHigh) = Range(weak);

        (string Name, string Spec)[] setups =
        [
            ("DEF", string.Empty),
            ("POS", "ment=3,press=2,line=2,tempo=2"),
            ("ATT", "ment=4,press=2,line=2,tempo=2"),
            ("COUNTER", "ment=1,press=0,line=0,pass=2,counter=1"),
            ("DEEP", "ment=0,press=0,line=0"),
            ("DEF532", "form=5"),
            ("DEEP541", "form=12,ment=0,press=0,line=0"),
            ("CTR541", "form=12,ment=1,press=0,line=0,pass=2,counter=1"),
            ("CTR532", "form=5,ment=1,press=0,line=0,pass=2,counter=1"),
            ("DEF541", "form=12"),
            ("CAU532", "form=5,ment=1"),
            ("CAU541", "form=12,ment=1"),
        ];
        var whyOnly = (Environment.GetEnvironmentVariable("TW_ONLY") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);

        setups = [.. setups.Where(setup => whyOnly.Length == 0 || whyOnly.Contains(setup.Name))];

        // The attack edge (plan zesty-chipmunk): the weaker side's attack group, and the stronger side's Defence family, get one value on their core skills.
        int? awayForward = int.TryParse(Environment.GetEnvironmentVariable("TW_AWAY_FWD"), CultureInfo.InvariantCulture, out var forwardValue) ? forwardValue : null;
        int? homeDefence = int.TryParse(Environment.GetEnvironmentVariable("TW_HOME_DEF"), CultureInfo.InvariantCulture, out var defenceValue) ? defenceValue : null;

        Console.WriteLine($"== tickwhy: 18-19 at home to {weak} (gk 18 / {weakLow}), the weaker side setup varies, {matches} matches a cell, seed {seed}, weaker attack group {(awayForward is null ? "as spread" : awayForward)}, stronger defence {(homeDefence is null ? "as spread" : homeDefence)} ==");

        foreach (var (name, setup) in setups)
        {
            var tally = new WhyTally();
            var gate = new object();
            var (homeForm, homeInstructions, homeKeeper, homePace) = Spec("gk=18");
            var (awayForm, awayInstructions, awayKeeper, awayPace) = Spec($"gk={weakLow}" + (setup.Length == 0 ? string.Empty : "," + setup));

            Parallel.For(0, matches, index =>
            {
                var built = LaboratoryFixtures.Build(seed + (ulong)index, 18, weakLow, homeInstructions, awayInstructions, rules);
                var input = TickCalibration.Stand(
                    built with { Home = Paced(Keeper(Spread(built.Home, 18, 19), homeKeeper), homePace), Away = Paced(Keeper(Spread(built.Away, weakLow, weakHigh), awayKeeper), awayPace) },
                    homeForm,
                    awayForm);

                // Applied after the formation is stood, because the groups are read from the slots' family and depth.
                if (awayForward is { } forwardSkill)
                {
                    input = input with { Away = WithCore(input.Away, AttackGroup(input.Away), FlankForwardCore, forwardSkill) };
                }

                if (homeDefence is { } defenceSkill)
                {
                    var defenders = input.Home.Slots.Where(slot => slot.Family == MatchPositionFamily.Defence).Select(slot => slot.ParticipantId).ToList();

                    input = input with { Home = WithCore(input.Home, defenders, DefenderCore, defenceSkill) };
                }

                var recorder = new MatchPassageRecorder();
                var result = MatchSimulator.Simulate(input, rules, null, recorder, TickMatchEngine.Instance);

                if (recorder.Tick is { } recording)
                {
                    lock (gate)
                    {
                        WhyRead(result, recording, tally);
                    }
                }
            });

            var n = (double)matches;
            string Per(double value) => $"{value / n,6:F2}";
            var strongRegainsOpp = tally.StrongRegains - tally.StrongRegainsInOwnHalf;

            Console.WriteLine();
            Console.WriteLine($"-- weaker side {name}: strong W/D/L {100.0 * tally.Wins / n:F0}/{100.0 * tally.Draws / n:F0}/{100.0 * tally.Losses / n:F0}%  weaker points {((3.0 * tally.Losses) + tally.Draws) / n:F2}  goals {tally.GoalsStrong / n:F2}-{tally.GoalsWeak / n:F2}  shots {tally.ShotsStrong / n:F1}-{tally.ShotsWeak / n:F1}");
            var weakShare = tally.PossessionFramesWeak / tally.OpenFrames;
            Console.WriteLine($"   possession of the weaker side {100.0 * weakShare:F0}%; shots per 100 s of possession: stronger {100.0 * tally.ShotsStrong / n / (5_400.0 * (1 - weakShare)):F2}, weaker {100.0 * tally.ShotsWeak / n / (5_400.0 * weakShare):F2}");
            Console.WriteLine("   stronger side regains by weaker players behind the ball (0..10), a match / share followed by its shot within 8 s: " + string.Join("  ", Enumerable.Range(0, 11).Where(k => tally.ExposedRegains[k] > 0).Select(k => $"{k}: {tally.ExposedRegains[k] / n:F1}/{100.0 * tally.ExposedShots[k] / tally.ExposedRegains[k]:F0}%")));
            Console.WriteLine($"   weaker side loses the ball (stronger side regains) {Per(tally.StrongRegains)} a match: {Per(tally.StrongRegainsInOwnHalf)} in the stronger own half, {Per(strongRegainsOpp)} in the weaker half");
            Console.WriteLine($"     weaker players behind the ball at those regains: {tally.BehindOwnHalf / Math.Max(1, tally.StrongRegainsInOwnHalf):F1} (ball in the stronger half), {tally.BehindOppHalf / Math.Max(1, strongRegainsOpp):F1} (ball in the weaker half)");
            Console.WriteLine($"     weaker back four, mean depth from its own goal (m): {tally.LineDepthOwnHalf / Math.Max(1, tally.StrongRegainsInOwnHalf):F1} / {tally.LineDepthOppHalf / Math.Max(1, strongRegainsOpp):F1}");
            Console.WriteLine($"   stronger side shots within 12 s of a regain {Per(tally.StrongTransitionShots)} a match ({100.0 * tally.StrongTransitionShots / Math.Max(1, tally.StrongRegains):F1}% of regains; {Per(tally.StrongTransitionShotsOwnHalf)} of them from its own-half regains), goals from them {Per(tally.StrongTransitionGoals)}");
            Console.WriteLine($"   weaker side regains {Per(tally.WeakRegains)} a match, {Per(tally.WeakRegainsHigh)} in the stronger half; shots within 12 s of a regain {Per(tally.WeakTransitionShots)} of its {tally.ShotsWeak / n:F1}, goals from them {Per(tally.WeakTransitionGoals)}");
            string Share(double part, double whole) => $"{100.0 * part / Math.Max(1, whole):F0}%";
            var lostQuick = Math.Max(1, tally.OwnThirdLostQuick);
            var firstNames = new[] { "pass <20 m", "pass 20-40 m", "pass 40 m+", "shot", "none", "clear", "recycle" };

            Console.WriteLine($"   weaker side regains in its own third {Per(tally.OwnThirdRegains)} a match: kept 10 s+ {Share(tally.OwnThirdKept, tally.OwnThirdRegains)}, lost within 5 s {Share(tally.OwnThirdLostQuick, tally.OwnThirdRegains)}, lost 5-10 s {Share(tally.OwnThirdLostSlow, tally.OwnThirdRegains)}, play stopped {Share(tally.OwnThirdStopped, tally.OwnThirdRegains)}; nearest outfield teammate at the regain {tally.OwnThirdNearest / Math.Max(1, tally.OwnThirdRegains):F1} m");
            Console.WriteLine($"     lost within 5 s, how: tackled {Share(tally.OwnThirdLostHow[0], lostQuick)}, pass intercepted {Share(tally.OwnThirdLostHow[1], lostQuick)}, loose ball/other {Share(tally.OwnThirdLostHow[2], lostQuick)}, header {Share(tally.OwnThirdLostHow[3], lostQuick)}; won by the stronger side in: weaker third {Share(tally.OwnThirdLostWhere[0], lostQuick)}, middle {Share(tally.OwnThirdLostWhere[1], lostQuick)}, stronger third {Share(tally.OwnThirdLostWhere[2], lostQuick)}; a stronger shot within 12 s of the loss {Share(tally.OwnThirdLostStrongShots, lostQuick)} ({Per(tally.OwnThirdLostStrongShots)} a match)");
            Console.WriteLine($"     lost within 5 s, what the weaker side did last: kicked it <20 m {Share(tally.OwnThirdLostKick[0], lostQuick)}, 20-40 m {Share(tally.OwnThirdLostKick[1], lostQuick)}, 40 m+ {Share(tally.OwnThirdLostKick[2], lostQuick)}, clearance {Share(tally.OwnThirdLostKick[4], lostQuick)}, recycle {Share(tally.OwnThirdLostKick[5], lostQuick)}; carrier lost it with no kick {Share(tally.OwnThirdLostKick[3], lostQuick)}");
            var kickNames = new[] { "pass <20 m", "pass 20-40 m", "pass 40 m+", "no kick", "clear", "recycle" };

            Console.WriteLine("     by the last kick, a match / won in the weaker third / followed by a stronger shot within 12 s: " + string.Join("  ", Enumerable.Range(0, 6).Select(k => $"{kickNames[k]} {Per(tally.OwnThirdLostKick[k])}/{Share(tally.OwnThirdKickWeakThird[k], tally.OwnThirdLostKick[k])}/{Share(tally.OwnThirdKickShots[k], tally.OwnThirdLostKick[k])}")));
            Console.WriteLine("     first action (share of regains / share of that action lost within 5 s): " + string.Join("  ", Enumerable.Range(0, 7).Select(k => $"{firstNames[k]} {Share(tally.OwnThirdFirst[k], tally.OwnThirdRegains)}/{Share(tally.OwnThirdFirstLost[k], tally.OwnThirdFirst[k])}")));
            Console.WriteLine("   by quarter-hour (weaker goals/stronger goals, weaker shots/stronger shots): " + string.Join("  ", Enumerable.Range(0, 6).Select(period => $"{tally.WeakGoalsByPeriod[period] / n:F2}/{tally.StrongGoalsByPeriod[period] / n:F2} {tally.WeakShotsByPeriod[period] / n:F1}/{tally.StrongShotsByPeriod[period] / n:F1}")));
        }
    }

    private static void WhyRead(MatchResultV1 result, TickMatchRecording recording, WhyTally tally)
    {
        const int half = TickMatchRecording.Entities / 2;
        var frameOf = recording.Events.ToDictionary(stamp => stamp.Sequence, stamp => Math.Min(stamp.Frame, recording.FrameCount - 1));
        var goalFrames = new List<int>[] { [], [] };
        var shotFrames = new List<int>[] { [], [] };

        foreach (var goal in result.Events.Where(e => e.IsGoal && frameOf.ContainsKey(e.Sequence)))
        {
            goalFrames[(int)goal.Side].Add(frameOf[goal.Sequence]);
        }

        foreach (var stamp in recording.Actions.Where(stamp => stamp.Entity >= 0 && stamp.Frame < recording.FrameCount && stamp.Action == PassageAction.Shot))
        {
            shotFrames[stamp.Entity / half].Add(stamp.Frame);

            var period = Math.Min(5, recording.ClockSecond(stamp.Frame) / 900);

            if (stamp.Entity / half == 0)
            {
                tally.StrongShotsByPeriod[period]++;
            }
            else
            {
                tally.WeakShotsByPeriod[period]++;
            }
        }

        foreach (var goal in result.Events.Where(e => e.IsGoal))
        {
            var period = Math.Min(5, Math.Max(0, goal.Minute - 1) / 15);

            if ((int)goal.Side == 0)
            {
                tally.StrongGoalsByPeriod[period]++;
            }
            else
            {
                tally.WeakGoalsByPeriod[period]++;
            }
        }

        tally.GoalsStrong += result.HomeGoals;
        tally.GoalsWeak += result.AwayGoals;
        tally.ShotsStrong += result.Home.Shots;
        tally.ShotsWeak += result.Away.Shots;

        if (result.HomeGoals > result.AwayGoals)
        {
            tally.Wins++;
        }
        else if (result.HomeGoals == result.AwayGoals)
        {
            tally.Draws++;
        }
        else
        {
            tally.Losses++;
        }

        var lastSide = -1;
        var interrupted = false;
        var depths = new int[half - 1];
        var firstAction = new int[recording.FrameCount + 1];

        for (int frame = 0, at = 0; frame <= recording.FrameCount; frame++)
        {
            while (at < recording.Actions.Count && recording.Actions[at].Frame < frame)
            {
                at++;
            }

            firstAction[frame] = at;
        }

        for (var frame = 0; frame < recording.FrameCount; frame++)
        {
            if (recording.State(frame) != TickPlayState.OpenPlay)
            {
                interrupted = true;
                continue;
            }

            var controller = recording.Controller(frame);

            if (controller < 0)
            {
                continue;
            }

            var side = controller / half;

            tally.OpenFrames++;

            if (side == 1)
            {
                tally.PossessionFramesWeak++;
            }

            if (side != lastSide && lastSide >= 0 && !interrupted && controller % half != 0)
            {
                var ballX = recording.BallX(frame);
                var shotSoon = shotFrames[side].Any(shot => shot >= frame && shot - frame <= BreakFrames);
                var goalSoon = shotSoon && goalFrames[side].Any(goal => goal >= frame && goal - frame <= BreakFrames + 100);

                if (side == 0)
                {
                    // The stronger side wins the ball: the weaker side (entities 12 to 21) defends the goal at X = 10 000.
                    var ownHalf = ballX < 5_000;
                    var behind = 0;

                    for (var seat = 1; seat < half; seat++)
                    {
                        var x = recording.PlayerX(frame, half + seat);

                        if (x > ballX)
                        {
                            behind++;
                        }

                        depths[seat - 1] = 10_000 - x;
                    }

                    Array.Sort(depths);
                    var backFour = (depths[0] + depths[1] + depths[2] + depths[3]) / 4.0 * 105.0 / 10_000.0;

                    tally.StrongRegains++;
                    tally.ExposedRegains[behind]++;

                    if (shotFrames[0].Any(shot => shot >= frame && shot - frame <= 80))
                    {
                        tally.ExposedShots[behind]++;
                    }

                    if (ownHalf)
                    {
                        tally.StrongRegainsInOwnHalf++;
                        tally.BehindOwnHalf += behind;
                        tally.LineDepthOwnHalf += backFour;
                    }
                    else
                    {
                        tally.BehindOppHalf += behind;
                        tally.LineDepthOppHalf += backFour;
                    }

                    if (shotSoon)
                    {
                        tally.StrongTransitionShots++;
                        tally.StrongTransitionShotsOwnHalf += ownHalf ? 1 : 0;
                    }

                    if (goalSoon)
                    {
                        tally.StrongTransitionGoals++;
                    }
                }
                else
                {
                    tally.WeakRegains++;

                    if (ballX < 5_000)
                    {
                        tally.WeakRegainsHigh++;
                    }

                    if (ballX >= OwnThirdFromX)
                    {
                        WhyOwnThirdRegain(recording, frame, controller, firstAction, shotFrames[0], tally);
                    }

                    if (shotSoon)
                    {
                        tally.WeakTransitionShots++;
                    }

                    if (goalSoon)
                    {
                        tally.WeakTransitionGoals++;
                    }
                }
            }

            lastSide = side;
            interrupted = false;
        }
    }

    /// <summary>
    /// Follows one regain by the weaker side in its own third (M5 of plan zesty-chipmunk): how long it keeps the ball, how it loses it and
    /// where, what its first action is and how far its nearest outfield teammate stands. A clearance and a back-pass are both stamped
    /// <see cref="PassageAction.Pass"/> by the engine, so the "clear" and "recycle" counts read zero unless the engine is built with the
    /// diagnostic tags in `.run-logs/v2-m5-knobs.patch` (Header for a clearance, Dive for a back-pass); without them both land under the pass lengths.
    /// </summary>
    private static void WhyOwnThirdRegain(TickMatchRecording recording, int frame, int controller, int[] firstAction, List<int> strongShots, WhyTally tally)
    {
        const int half = TickMatchRecording.Entities / 2;
        var last = Math.Min(recording.FrameCount - 1, frame + KeptFrames);
        var lostAt = -1;
        var stopped = false;

        for (var at = frame + 1; at <= last && lostAt < 0 && !stopped; at++)
        {
            if (recording.State(at) != TickPlayState.OpenPlay)
            {
                stopped = true;
            }
            else if (recording.Controller(at) >= 0 && recording.Controller(at) / half == 0)
            {
                lostAt = at;
            }
        }

        // The first thing the regaining side does with the ball, before it loses it or play stops.
        var until = lostAt >= 0 ? lostAt : last;
        var first = 4;

        for (var index = firstAction[frame]; index < recording.Actions.Count && recording.Actions[index].Frame <= until; index++)
        {
            var stamp = recording.Actions[index];

            if (stamp.Entity / half != 1 || stamp.Action is not (PassageAction.Pass or PassageAction.Cross or PassageAction.Shot or PassageAction.Header or PassageAction.Dive))
            {
                continue;
            }

            if (stamp.Action == PassageAction.Shot)
            {
                first = 3;
            }
            else if (stamp.Action is PassageAction.Header or PassageAction.Dive)
            {
                first = stamp.Action == PassageAction.Header ? 5 : 6;
            }
            else
            {
                var end = stamp.Frame + 1;

                while (end < stamp.Frame + FlightFrames && end < recording.FrameCount - 1 && recording.Controller(end) < 0)
                {
                    end++;
                }

                var dx = (double)(recording.BallX(end) - recording.BallX(stamp.Frame));
                var dy = (double)(recording.BallY(end) - recording.BallY(stamp.Frame));
                var length = Math.Sqrt((dx * dx) + (dy * dy)) * 105.0 / 10_000.0;

                first = length < 20 ? 0 : length < 40 ? 1 : 2;
            }

            break;
        }

        tally.OwnThirdRegains++;
        tally.OwnThirdFirst[first]++;

        var nearest = double.MaxValue;
        var carrierX = recording.PlayerX(frame, controller);
        var carrierY = recording.PlayerY(frame, controller);

        for (var mate = (controller / half * half) + 1; mate < (controller / half * half) + half; mate++)
        {
            if (mate == controller || recording.PlayerX(frame, mate) < 0)
            {
                continue;
            }

            var dx = (double)(recording.PlayerX(frame, mate) - carrierX);
            var dy = (double)(recording.PlayerY(frame, mate) - carrierY);

            nearest = Math.Min(nearest, Math.Sqrt((dx * dx) + (dy * dy)));
        }

        tally.OwnThirdNearest += nearest * 105.0 / 10_000.0;

        if (lostAt < 0)
        {
            if (stopped)
            {
                tally.OwnThirdStopped++;
            }
            else
            {
                tally.OwnThirdKept++;
            }

            return;
        }

        tally.OwnThirdFirstLost[first] += lostAt - frame <= QuickFrames ? 1 : 0;

        if (lostAt - frame > QuickFrames)
        {
            tally.OwnThirdLostSlow++;

            return;
        }

        tally.OwnThirdLostQuick++;

        // How the stronger side got it: its action stamped on the tick it took the ball.
        var how = 2;

        for (var index = firstAction[Math.Max(0, lostAt - 2)]; index < recording.Actions.Count && recording.Actions[index].Frame <= lostAt + 1; index++)
        {
            var stamp = recording.Actions[index];

            if (stamp.Entity / half != 0)
            {
                continue;
            }

            if (stamp.Action == PassageAction.Tackle)
            {
                how = 0;

                break;
            }

            if (stamp.Action == PassageAction.Interception)
            {
                how = 1;
            }
            else if (stamp.Action == PassageAction.Header && how == 2)
            {
                how = 3;
            }
        }

        tally.OwnThirdLostHow[how]++;

        // Was the ball kicked away, or did the carrier lose it? The last frame the weaker side held it, and a kick stamped from there on.
        var heldTo = lostAt - 1;

        while (heldTo > frame && (recording.Controller(heldTo) < 0 || recording.Controller(heldTo) / half != 1))
        {
            heldTo--;
        }

        var kick = 3;

        for (var index = firstAction[heldTo]; index < recording.Actions.Count && recording.Actions[index].Frame <= lostAt; index++)
        {
            var stamp = recording.Actions[index];

            if (stamp.Entity / half != 1 || stamp.Action is not (PassageAction.Pass or PassageAction.Cross or PassageAction.Header or PassageAction.Dive))
            {
                continue;
            }

            if (stamp.Action is PassageAction.Header or PassageAction.Dive)
            {
                kick = stamp.Action == PassageAction.Header ? 4 : 5;

                continue;
            }

            var end = stamp.Frame + 1;

            while (end < lostAt && recording.Controller(end) < 0)
            {
                end++;
            }

            var dx = (double)(recording.BallX(end) - recording.BallX(stamp.Frame));
            var dy = (double)(recording.BallY(end) - recording.BallY(stamp.Frame));
            var length = Math.Sqrt((dx * dx) + (dy * dy)) * 105.0 / 10_000.0;

            kick = length < 20 ? 0 : length < 40 ? 1 : 2;
        }

        tally.OwnThirdLostKick[kick]++;

        var ballX = recording.BallX(lostAt);

        tally.OwnThirdLostWhere[ballX >= OwnThirdFromX ? 0 : ballX >= 3_333 ? 1 : 2]++;
        tally.OwnThirdKickWeakThird[kick] += ballX >= OwnThirdFromX ? 1 : 0;

        if (strongShots.Any(shot => shot >= lostAt && shot - lostAt <= BreakFrames))
        {
            tally.OwnThirdLostStrongShots++;
            tally.OwnThirdKickShots[kick]++;
        }
    }

    private static readonly MatchAttributeName[] FlankDefenderCore =
    [
        MatchAttributeName.Tackling,
        MatchAttributeName.Marking,
        MatchAttributeName.Heading,
        MatchAttributeName.Positioning,
        MatchAttributeName.Anticipation,
        MatchAttributeName.Pace,
        MatchAttributeName.Acceleration,
    ];

    private static readonly MatchAttributeName[] FlankForwardCore =
    [
        MatchAttributeName.Finishing,
        MatchAttributeName.Dribbling,
        MatchAttributeName.FirstTouch,
        MatchAttributeName.Technique,
        MatchAttributeName.Composure,
        MatchAttributeName.Pace,
        MatchAttributeName.Acceleration,
    ];

    private static readonly MatchAttributeName[] DefenderCore =
    [
        MatchAttributeName.Tackling,
        MatchAttributeName.Marking,
        MatchAttributeName.Positioning,
        MatchAttributeName.Pace,
        MatchAttributeName.Acceleration,
        MatchAttributeName.Strength,
    ];

    /// <summary>Sets some attributes of one occupant of a side to a value and leaves the rest of his attributes as they are.</summary>
    private static MatchSideV1 WithCore(MatchSideV1 side, Guid participant, MatchAttributeName[] names, int value) =>
        WithCore(side, [participant], names, value);

    /// <summary>Sets some attributes of several occupants of a side to a value and leaves the rest of their attributes as they are.</summary>
    private static MatchSideV1 WithCore(MatchSideV1 side, IReadOnlyCollection<Guid> participants, MatchAttributeName[] names, int value) =>
        side with
        {
            Squad =
            [
                .. side.Squad.Select(player => !participants.Contains(player.ParticipantId)
                    ? player
                    : player with
                    {
                        Attributes = PlayerAttributesV1.From([.. player.Attributes.Values.Select((current, index) => names.Contains((MatchAttributeName)index) ? value : current)]),
                    }),
            ],
        };

    /// <summary>
    /// The weaker side's attack group: its Attack-family outfield players and its two most advanced midfielders (by shape depth). Read from the
    /// side's slots, so it must be called after the formation has been stood.
    /// </summary>
    private static List<Guid> AttackGroup(MatchSideV1 side) =>
    [
        .. side.Slots.Where(slot => slot.Family == MatchPositionFamily.Attack).Select(slot => slot.ParticipantId),
        .. side.Slots.Where(slot => slot.Family == MatchPositionFamily.Midfield).OrderByDescending(slot => slot.X).Take(2).Select(slot => slot.ParticipantId),
    ];

    /// <summary>
    /// `tickflank`: an 18-19 side at home to a 15-16 side. On each of three lanes the home defender there may be weak (core skills 15), the away
    /// forward who plays him may be a star (core skills 17, two above the defender), and the away pass focus is aimed at the lane. Each mentality
    /// of the away side is played against the control (nobody changed), the star forward alone, the weak defender alone and both, so the effect of
    /// each change can be read apart. Prints one machine-readable line a cell.
    /// </summary>
    /// <param name="matches">How many matches a cell.</param>
    /// <param name="seed">The base seed (the same matches are played in every cell).</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="weak">The weaker side's ability, a number or a range.</param>
    public static void Flank(int matches, ulong seed, EngineRulesV2 rules, string weak = "15-16")
    {
        var (weakLow, weakHigh) = Range(weak);

        (string Name, MatchMentality Value)[] mentalities =
        [
            ("ATTK", MatchMentality.Attacking),
            ("BALANCE", MatchMentality.Balanced),
            ("DEF", MatchMentality.Defensive),
        ];
        (string Name, int DefenderSlot)[] lanes = [("Left", 5), ("Centre", 4), ("Right", 2)];
        // Each cell: the lane, whether the home defender there is weak, whether the away forward facing him is a star, and whether the pass focus is aimed at the lane.
        var cells = new List<(string Lane, bool WeakDefender, bool StarForward, bool Focus)> { (string.Empty, false, false, false) };

        foreach (var (lane, _) in lanes)
        {
            cells.Add((lane, false, true, true));
            cells.Add((lane, true, false, true));
            cells.Add((lane, true, true, true));
        }

        Console.WriteLine($"== tickflank: 18-19 at home to {weak}, {matches} matches a cell, seed {seed} ==");
        Console.WriteLine("CELL\tmentality\tlane\tskills\tfocus\tn\tweakWins\tdraws\tweakLosses\tgoalsStrong\tgoalsWeak\tshotsStrong\tshotsWeak\tfwdShots\tfwdGoals\tfinalThirdLowY%\tfinalThirdMidY%\tfinalThirdHighY%\tdefY\tfwdY\tfocusUsed");

        foreach (var (mentalityName, mentality) in mentalities)
        {
            foreach (var (lane, weakDefender, starForward, focus) in cells)
            {
                var tracked = lane.Length > 0;
                var defenderSlotNumber = lane.Length == 0 ? 0 : lanes.Single(entry => entry.Name == lane).DefenderSlot;
                var results = new (int HomeGoals, int AwayGoals, int HomeShots, int AwayShots, int FwdShots, int FwdGoals, int ThirdLow, int ThirdMid, int ThirdHigh, double DefY, double FwdY)[matches];
                var focusUsed = MatchPassFocus.Balanced;

                Parallel.For(0, matches, index =>
                {
                    var homeInstructions = new MatchInstructionsV1();
                    var awayInstructions = new MatchInstructionsV1 { Mentality = mentality };
                    var built = LaboratoryFixtures.Build(seed + (ulong)index, 18, weakLow, homeInstructions, awayInstructions, rules);
                    var input = TickCalibration.Stand(
                        built with { Home = Keeper(Spread(built.Home, 18, 19), 18), Away = Keeper(Spread(built.Away, weakLow, weakHigh), weakLow) },
                        FormationPreset.FourFourTwo,
                        FormationPreset.FourFourTwo);
                    var defender = Guid.Empty;
                    var forward = Guid.Empty;
                    var defenderY = 0;

                    if (tracked)
                    {
                        var defenderSlot = input.Home.Slots.Single(slot => slot.SlotNumber == defenderSlotNumber);
                        var forwardSlot = input.Away.Slots
                            .Where(slot => slot.Family == MatchPositionFamily.Attack)
                            .OrderBy(slot => Math.Abs((10_000 - slot.Y) - defenderSlot.Y))
                            .First();

                        defender = defenderSlot.ParticipantId;
                        forward = forwardSlot.ParticipantId;
                        defenderY = defenderSlot.Y;
                        input = input with
                        {
                            Home = weakDefender ? WithCore(input.Home, defender, FlankDefenderCore, 15) : input.Home,
                            Away = starForward ? WithCore(input.Away, forward, FlankForwardCore, 17) : input.Away,
                        };

                        if (focus)
                        {
                            var chosen = forwardSlot.Y > 6_500 ? MatchPassFocus.CentreAndRight : (forwardSlot.Y < 3_500 ? MatchPassFocus.CentreAndLeft : MatchPassFocus.Centre);

                            focusUsed = chosen;
                            input = input with { Away = input.Away with { Instructions = input.Away.Instructions with { PassFocus = chosen } } };
                        }
                    }

                    var recorder = new MatchPassageRecorder();
                    var result = MatchSimulator.Simulate(input, rules, null, recorder, TickMatchEngine.Instance);
                    var fwdShots = 0;
                    var thirds = new int[3];
                    double defY = 0;
                    double fwdY = 0;

                    if (recorder.Tick is { } recording)
                    {
                        const int half = TickMatchRecording.Entities / 2;
                        var defenderEntity = tracked ? Array.IndexOf(recording.Starters, defender) : -1;
                        var forwardEntity = tracked ? Array.IndexOf(recording.Starters, forward) : -1;
                        var samples = 0;

                        foreach (var stamp in recording.Actions.Where(stamp => stamp.Entity == forwardEntity && stamp.Frame < recording.FrameCount && stamp.Action == PassageAction.Shot))
                        {
                            fwdShots++;
                        }

                        for (var frame = 0; frame < recording.FrameCount; frame += 5)
                        {
                            if (recording.State(frame) != TickPlayState.OpenPlay)
                            {
                                continue;
                            }

                            // The weaker side (away, entities 11 to 21) in possession with the ball in the final third it attacks (X below a third).
                            if (recording.Controller(frame) >= half && recording.BallX(frame) < 3_333)
                            {
                                var ballY = recording.BallY(frame) / 0.7;

                                thirds[ballY < 3_333 ? 0 : (ballY < 6_667 ? 1 : 2)]++;
                            }

                            if (tracked)
                            {
                                defY += recording.PlayerY(frame, defenderEntity) / 0.7;
                                fwdY += recording.PlayerY(frame, forwardEntity) / 0.7;
                                samples++;
                            }
                        }

                        defY /= Math.Max(1, samples);
                        fwdY /= Math.Max(1, samples);
                    }

                    var fwdGoals = tracked ? result.Events.Count(e => e.IsGoal && e.ParticipantId == forward) : 0;

                    results[index] = (result.HomeGoals, result.AwayGoals, result.Home.Shots, result.Away.Shots, fwdShots, fwdGoals, thirds[0], thirds[1], thirds[2], defY, fwdY);
                });

                var weakWins = results.Count(entry => entry.AwayGoals > entry.HomeGoals);
                var draws = results.Count(entry => entry.AwayGoals == entry.HomeGoals);
                var thirdTotal = Math.Max(1, results.Sum(entry => entry.ThirdLow + entry.ThirdMid + entry.ThirdHigh));

                Console.WriteLine(
                    string.Join(
                        '\t',
                        "CELL",
                        mentalityName,
                        lane.Length == 0 ? "-" : lane,
                        (weakDefender ? "D" : string.Empty) + (starForward ? "F" : string.Empty) + (tracked ? string.Empty : "-"),
                        focus ? "1" : "0",
                        matches.ToString(CultureInfo.InvariantCulture),
                        weakWins.ToString(CultureInfo.InvariantCulture),
                        draws.ToString(CultureInfo.InvariantCulture),
                        (matches - weakWins - draws).ToString(CultureInfo.InvariantCulture),
                        results.Average(entry => entry.HomeGoals).ToString("F2", CultureInfo.InvariantCulture),
                        results.Average(entry => entry.AwayGoals).ToString("F2", CultureInfo.InvariantCulture),
                        results.Average(entry => entry.HomeShots).ToString("F1", CultureInfo.InvariantCulture),
                        results.Average(entry => entry.AwayShots).ToString("F1", CultureInfo.InvariantCulture),
                        results.Average(entry => entry.FwdShots).ToString("F2", CultureInfo.InvariantCulture),
                        results.Average(entry => entry.FwdGoals).ToString("F2", CultureInfo.InvariantCulture),
                        (100.0 * results.Sum(entry => entry.ThirdLow) / thirdTotal).ToString("F0", CultureInfo.InvariantCulture),
                        (100.0 * results.Sum(entry => entry.ThirdMid) / thirdTotal).ToString("F0", CultureInfo.InvariantCulture),
                        (100.0 * results.Sum(entry => entry.ThirdHigh) / thirdTotal).ToString("F0", CultureInfo.InvariantCulture),
                        tracked ? results.Average(entry => entry.DefY).ToString("F0", CultureInfo.InvariantCulture) : "-",
                        tracked ? results.Average(entry => entry.FwdY).ToString("F0", CultureInfo.InvariantCulture) : "-",
                        focusUsed));
            }
        }
    }

    private static string Share(int part, int whole) => $"{100.0 * part / Math.Max(1, whole),3:F0}%";

    private static void Tally(MatchResultV1 result, TickMatchRecording recording, ShotTally[] sides)
    {
        var half = TickMatchRecording.Entities / 2;
        var frameOf = recording.Events.ToDictionary(stamp => stamp.Sequence, stamp => Math.Min(stamp.Frame, recording.FrameCount - 1));
        var outcomes = result.Events
            .Where(e => e.Type is EngineEventType.Goal or EngineEventType.ShotSaved or EngineEventType.ShotBlocked or EngineEventType.ShotOffTarget or EngineEventType.Woodwork && frameOf.ContainsKey(e.Sequence))
            .Select(e => (Event: e, Frame: frameOf[e.Sequence]))
            .ToList();

        CountBreaks(recording, sides);

        foreach (var stamp in recording.Actions.Where(stamp => stamp.Entity >= 0 && stamp.Frame < recording.FrameCount && stamp.Action == PassageAction.Shot))
        {
            var side = stamp.Entity / half;
            var before = Math.Max(0, stamp.Frame - 1);
            var ownX = side == 0 ? recording.BallX(before) : 10_000 - recording.BallX(before);
            var ownY = side == 0 ? recording.BallY(before) : 7_000 - recording.BallY(before);
            var distance = (long)Math.Sqrt(((10_000L - ownX) * (10_000L - ownX)) + ((3_500L - ownY) * (3_500L - ownY)));
            var band = distance < 600 ? 0 : (distance < 1_200 ? 1 : (distance < 1_600 ? 2 : (distance < 2_500 ? 3 : 4)));
            var outcome = outcomes.FirstOrDefault(entry => (int)entry.Event.Side == side && entry.Frame >= stamp.Frame && entry.Frame - stamp.Frame <= 100);
            var tally = sides[side];

            tally.Shots++;
            tally.DistanceSum += distance;
            tally.BandShots[band]++;

            switch (outcome.Event?.Type)
            {
                case EngineEventType.Goal:
                    tally.BandGoals[band]++;
                    break;
                case EngineEventType.ShotSaved:
                    tally.BandSaved[band]++;
                    break;
                case EngineEventType.ShotBlocked:
                    tally.BandBlocked[band]++;
                    break;
                default:
                    tally.BandOff[band]++;
                    break;
            }
        }
    }

    /// <summary>
    /// Counts the balls a side won in open play in its own half (the other side had it last, with no stoppage between) and how many of them
    /// became a shot by the side that won it within <see cref="BreakFrames"/>. A keeper picking the ball up is not a regain.
    /// </summary>
    private static void CountBreaks(TickMatchRecording recording, ShotTally[] sides)
    {
        const int half = TickMatchRecording.Entities / 2;
        var shotFrames = new List<int>[] { [], [] };

        foreach (var stamp in recording.Actions.Where(stamp => stamp.Entity >= 0 && stamp.Action == PassageAction.Shot))
        {
            shotFrames[stamp.Entity / half].Add(stamp.Frame);
        }

        var lastSide = -1;
        var interrupted = false;

        for (var frame = 0; frame < recording.FrameCount; frame++)
        {
            if (recording.State(frame) != TickPlayState.OpenPlay)
            {
                interrupted = true;
                continue;
            }

            var controller = recording.Controller(frame);

            if (controller < 0)
            {
                continue;
            }

            var side = controller / half;

            if (side != lastSide && lastSide >= 0 && !interrupted && controller % half != 0)
            {
                var ownX = side == 0 ? recording.BallX(frame) : 10_000 - recording.BallX(frame);

                if (ownX < 5_000)
                {
                    sides[side].Regains++;

                    if (shotFrames[side].Any(shot => shot >= frame && shot - frame <= BreakFrames))
                    {
                        sides[side].Breaks++;
                    }
                }
            }

            lastSide = side;
            interrupted = false;
        }
    }

    private static MatchSideV1 Keeper(MatchSideV1 side, int? ability) =>
        ability is null
            ? side
            : side with
            {
                Squad = [.. side.Squad.Select(player => player.IsGoalkeeper ? player with { Attributes = PlayerAttributesV1.Uniform(ability.Value) } : player)],
            };

    /// <summary>Sets Pace and Acceleration of every outfield player to a value and leaves his other attributes as they are.</summary>
    private static MatchSideV1 Paced(MatchSideV1 side, int? pace) =>
        pace is null
            ? side
            : side with
            {
                Squad =
                [
                    .. side.Squad.Select(player => player.IsGoalkeeper
                        ? player
                        : player with
                        {
                            Attributes = PlayerAttributesV1.From(
                            [
                                .. player.Attributes.Values.Select((value, index) => index is (int)MatchAttributeName.Pace or (int)MatchAttributeName.Acceleration ? pace.Value : value),
                            ]),
                        }),
                ],
            };

    internal static (FormationPreset Formation, MatchInstructionsV1 Instructions, int? Keeper, int? Pace) Spec(string spec)
    {
        int? keeper = null;
        int? pace = null;
        var formation = FormationPreset.FourFourTwo;
        var instructions = new MatchInstructionsV1();

        foreach (var pair in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split('=', 2);
            var value = int.Parse(parts[1], CultureInfo.InvariantCulture);

            switch (parts[0])
            {
                case "gk":
                    keeper = value;
                    break;
                case "pace":
                    pace = value;
                    break;
                case "form":
                    formation = (FormationPreset)value;
                    break;
                case "ment":
                    instructions = instructions with { Mentality = (MatchMentality)value };
                    break;
                case "tempo":
                    instructions = instructions with { Tempo = (MatchTempo)value };
                    break;
                case "pass":
                    instructions = instructions with { Passing = (MatchPassingStyle)value };
                    break;
                case "width":
                    instructions = instructions with { Width = (MatchWidth)value };
                    break;
                case "focus":
                    instructions = instructions with { PassFocus = (MatchPassFocus)value };
                    break;
                case "press":
                    instructions = instructions with { Pressing = (MatchPressing)value };
                    break;
                case "line":
                    instructions = instructions with { DefensiveLine = (MatchDefensiveLine)value };
                    break;
                case "tackle":
                    instructions = instructions with { Tackling = (MatchTacklingStyle)value };
                    break;
                case "waste":
                    instructions = instructions with { TimeWasting = (MatchTimeWasting)value };
                    break;
                case "counter":
                    instructions = instructions with { CounterAttack = value != 0 };
                    break;
                default:
                    throw new ArgumentException($"No side setting named {parts[0]}.");
            }
        }

        return (formation, instructions, keeper, pace);
    }

    private sealed class ShotTally
    {
        public static readonly string[] Bands = ["<6 m", "6-12 m", "12-16 m", "16-25 m", "25 m+"];

        public int Shots { get; set; }

        public long DistanceSum { get; set; }

        public int Regains { get; set; }

        public int Breaks { get; set; }

        public int[] BandShots { get; } = new int[5];

        public int[] BandGoals { get; } = new int[5];

        public int[] BandSaved { get; } = new int[5];

        public int[] BandBlocked { get; } = new int[5];

        public int[] BandOff { get; } = new int[5];
    }

    private static double Mean(int players, int low, int high) =>
        Enumerable.Range(0, players).Average(index => low + ((index * 5) % (high - low + 1)));

    private static (int Low, int High) Range(string text)
    {
        var parts = text.Split('-');
        var low = int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);

        return (low, parts.Length > 1 ? int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : low);
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
}
