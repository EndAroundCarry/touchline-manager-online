using System.Text.Json;
using TouchlineManager.Application.Match;
using TouchlineManager.Domain.Squad;
using TouchlineManager.MatchEngine;
using TouchlineManager.MatchEngine.Commentary;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Tick;
using System.Globalization;

namespace TouchlineManager.SimulationBenchmarks;

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

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Plays the match.</summary>
    /// <param name="homeAbility">The home side's uniform attribute value.</param>
    /// <param name="awayAbility">The away side's uniform attribute value.</param>
    /// <param name="seed">The match seed.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="dump">Where to write the film, or null.</param>
    public static void Run(string homeAbility, string awayAbility, ulong seed, EngineRulesV2 rules, string? dump)
    {
        var (homeLow, homeHigh) = Range(homeAbility);
        var (awayLow, awayHigh) = Range(awayAbility);
        var built = LaboratoryFixtures.Build(seed, homeLow, awayLow, new(), new(), rules);
        var input = TickCalibration.Stand(
            built with { Home = Spread(built.Home, homeLow, homeHigh), Away = Spread(built.Away, awayLow, awayHigh) },
            FormationPreset.FourFourTwo,
            FormationPreset.FourFourTwo);
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
    /// line, tackle, waste (the enum numbers) and counter (0 or 1). Anything left out is the default.
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
    public static void Balance(int matches, ulong seed, bool quick, EngineRulesV2 rules)
    {
        var setups = new Dictionary<string, string>
        {
            ["DEF"] = string.Empty,
            ["POS"] = "ment=3,press=2,line=2,tempo=2",
            ["ATT"] = "ment=4,press=2,line=2,tempo=2",
            ["COUNTER"] = "form=12,ment=1,press=0,line=0,pass=2,counter=1",
            ["BUS"] = "form=12,ment=0,press=0,line=0,counter=1,waste=1",
        };
        var scenarios = new List<(string Name, string Home, string Away, int HomeKeeper, int AwayKeeper, string[] HomeSetups, string[] AwaySetups)>
        {
            ("S1", "17-20", "15-18", 17, 15, ["DEF", "POS"], ["DEF", "POS", "COUNTER", "BUS"]),
            ("S2", "17-20", "13-15", 17, 13, ["DEF", "POS"], ["DEF", "POS", "COUNTER", "BUS"]),
            ("S3", "17-20", "6", 17, 6, ["DEF"], ["DEF", "COUNTER", "BUS"]),
            ("S4", "16", "16", 16, 16, [.. setups.Keys], [.. setups.Keys]),
        };

        Console.WriteLine($"== tickbalance: {matches} matches a cell, seed {seed}{(quick ? ", quick subset" : string.Empty)} ==");
        Console.WriteLine("   home v away (setup)                    W/D/L %  pts home-away  goals       shots       sot         <16 m shots  goals/sot  breaks/match (regains)");

        foreach (var (name, home, away, homeKeeper, awayKeeper, homeSetups, awaySetups) in scenarios)
        {
            if (quick && name is "S2" or "S3")
            {
                continue;
            }

            foreach (var homeSetup in homeSetups)
            {
                foreach (var awaySetup in awaySetups)
                {
                    if (quick && name == "S4" && homeSetup != "COUNTER" && awaySetup != "COUNTER")
                    {
                        continue;
                    }

                    var cell = Measure(home, away, $"gk={homeKeeper}{Join(setups[homeSetup])}", $"gk={awayKeeper}{Join(setups[awaySetup])}", matches, seed, rules);
                    var insideSixteen = cell.PerShot.Select(tally => (tally.BandShots[0] + tally.BandShots[1] + tally.BandShots[2]) / (double)matches).ToArray();
                    var breaks = cell.PerShot.Select(tally => (Breaks: tally.Breaks / (double)matches, Regains: tally.Regains / (double)matches)).ToArray();

                    Console.WriteLine(
                        $"{name} {home,5} {homeSetup,-7} v {away,5} {awaySetup,-7} {100.0 * cell.Wins / matches,3:F0}/{100.0 * cell.Draws / matches,3:F0}/{100.0 * cell.Losses / matches,3:F0}  {cell.HomePoints:F2}-{cell.AwayPoints:F2}  {cell.Goals[0]:F2}-{cell.Goals[1]:F2}  {cell.Shots[0]:F1}-{cell.Shots[1]:F1}  {cell.OnTarget[0]:F1}-{cell.OnTarget[1]:F1}  {insideSixteen[0]:F1}-{insideSixteen[1]:F1}  {100.0 * cell.GoalsPerOnTarget[0]:F0}%-{100.0 * cell.GoalsPerOnTarget[1]:F0}%  {breaks[0].Breaks:F2} ({breaks[0].Regains:F1}) - {breaks[1].Breaks:F2} ({breaks[1].Regains:F1})");
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
        ShotTally[] PerShot)
    {
        public double HomePoints => ((3.0 * Wins) + Draws) / (Wins + Draws + Losses);

        public double AwayPoints => ((3.0 * Losses) + Draws) / (Wins + Draws + Losses);
    }

    private static Cell Measure(string homeAbility, string awayAbility, string homeSpec, string awaySpec, int matches, ulong seed, EngineRulesV2 rules)
    {
        var (homeLow, homeHigh) = Range(homeAbility);
        var (awayLow, awayHigh) = Range(awayAbility);
        var (homeForm, homeInstructions, homeKeeper) = Spec(homeSpec);
        var (awayForm, awayInstructions, awayKeeper) = Spec(awaySpec);
        var goals = new int[matches, 2];
        var shots = new int[matches, 2];
        var onTarget = new int[matches, 2];
        var fouls = new int[matches, 2];
        var perShot = new ShotTally[] { new(), new() };
        var gate = new object();

        Parallel.For(0, matches, index =>
        {
            var built = LaboratoryFixtures.Build(seed + (ulong)index, homeLow, awayLow, homeInstructions, awayInstructions, rules);
            var input = TickCalibration.Stand(
                built with { Home = Keeper(Spread(built.Home, homeLow, homeHigh), homeKeeper), Away = Keeper(Spread(built.Away, awayLow, awayHigh), awayKeeper) },
                homeForm,
                awayForm);
            var recorder = new MatchPassageRecorder();
            var result = MatchSimulator.Simulate(input, rules, null, recorder, TickMatchEngine.Instance);

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
                lock (gate)
                {
                    Tally(result, recording, perShot);
                }
            }
        });

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
            perShot);
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

    private static (FormationPreset Formation, MatchInstructionsV1 Instructions, int? Keeper) Spec(string spec)
    {
        int? keeper = null;
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

        return (formation, instructions, keeper);
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
