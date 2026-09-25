using System.Diagnostics;
using System.Globalization;
using TouchlineManager.MatchEngine;
using TouchlineManager.MatchEngine.Commentary;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.SimulationBenchmarks;

// The simulation laboratory (master plan §8.5, §16 Stage 5).
//
// A single match, a distribution over many matches, and a timing run. It is deliberately a console tool
// rather than a test: the numbers that tune the engine want a hundred thousand matches and a printed table,
// and a test suite that took twenty minutes would stop being run.
//
// Usage: dotnet run --project tools/simulation-benchmarks -- [single|distributions|bench|all] [count] [seed]

var mode = args.Length > 0 ? args[0] : "all";
var count = args.Length > 1 && int.TryParse(args[1], CultureInfo.InvariantCulture, out var parsed) ? parsed : 20_000;
var seed = args.Length > 2 && ulong.TryParse(args[2], CultureInfo.InvariantCulture, out var parsedSeed)
    ? parsedSeed
    : 20_260_925UL;

var rules = EngineRulesV1.Default;
rules.Validate();

Console.WriteLine($"Engine {EngineVersions.EngineLabel}, rules {EngineVersions.RuleSetLabel}");
Console.WriteLine($"Rules hash  {EngineConfiguration.HashOf(rules)}");
Console.WriteLine();

if (mode is "single" or "all")
{
    SingleMatch(seed);
}

if (mode is "distributions" or "all")
{
    Distributions(count, seed);
}

if (mode is "bench" or "all")
{
    Bench(Math.Min(count, 5_000), seed);
}

return 0;

void SingleMatch(ulong matchSeed)
{
    var input = LaboratoryFixtures.EvenlyMatched(matchSeed);
    var result = MatchSimulator.Simulate(input, rules);

    Console.WriteLine("== One match ==");
    Console.WriteLine($"  {input.Home.ClubName} {result.HomeGoals} - {result.AwayGoals} {input.Away.ClubName}");
    Console.WriteLine($"  minutes played   {result.TotalMinutesPlayed}");
    Console.WriteLine($"  possession       {result.Home.PossessionBasisPoints / 100.0:F1}% / {result.Away.PossessionBasisPoints / 100.0:F1}%");
    Console.WriteLine($"  shots (on/off)   {result.Home.Shots} ({result.Home.ShotsOnTarget}/{result.Home.ShotsOffTarget}) - {result.Away.Shots} ({result.Away.ShotsOnTarget}/{result.Away.ShotsOffTarget})");
    Console.WriteLine($"  fouls, cards     {result.Home.Fouls}/{result.Home.YellowCards}/{result.Home.RedCards} - {result.Away.Fouls}/{result.Away.YellowCards}/{result.Away.RedCards}");
    Console.WriteLine($"  corners, offside {result.Home.Corners}/{result.Home.Offsides} - {result.Away.Corners}/{result.Away.Offsides}");
    Console.WriteLine($"  injuries         {result.Home.Injuries} - {result.Away.Injuries}");
    Console.WriteLine($"  substitutions    {result.Home.Substitutions} - {result.Away.Substitutions}");
    Console.WriteLine($"  events           {result.Events.Count}");
    Console.WriteLine($"  input hash       {result.InputHash}");
    Console.WriteLine($"  output hash      {result.OutputHash}");

    var commentary = CommentaryTokenBuilder.Build(input, result);
    var presentation = HighlightDirector.Build(input, result);

    Console.WriteLine($"  commentary lines {commentary.Count}");
    Console.WriteLine($"  highlights       {presentation.Highlights.Count}, ~{presentation.EstimatedPayloadBytes / 1024.0:F1} KB");
    Console.WriteLine();
}

void Distributions(int matches, ulong baseSeed)
{
    Console.WriteLine($"== {matches:N0} matches ==");

    long homeGoals = 0;
    long awayGoals = 0;
    long homeWins = 0;
    long draws = 0;
    long awayWins = 0;
    long homeShots = 0;
    long awayShots = 0;
    long homePossession = 0;
    long fouls = 0;
    long yellows = 0;
    long reds = 0;
    long injuries = 0;
    long penalties = 0;
    long substitutions = 0;
    var maxGoals = 0;
    var maxShots = 0;
    var goalTotals = new long[16];

    for (var index = 0; index < matches; index++)
    {
        var result = MatchSimulator.Simulate(
            LaboratoryFixtures.EvenlyMatched(baseSeed + (ulong)index),
            rules);

        homeGoals += result.HomeGoals;
        awayGoals += result.AwayGoals;
        homeShots += result.Home.Shots;
        awayShots += result.Away.Shots;
        homePossession += result.Home.PossessionBasisPoints;
        fouls += result.Home.Fouls + result.Away.Fouls;
        yellows += result.Home.YellowCards + result.Away.YellowCards;
        reds += result.Home.RedCards + result.Away.RedCards;
        injuries += result.Home.Injuries + result.Away.Injuries;
        penalties += result.Home.PenaltiesAwarded + result.Away.PenaltiesAwarded;
        substitutions += result.Home.Substitutions + result.Away.Substitutions;

        homeWins += result.HomeGoals > result.AwayGoals ? 1 : 0;
        draws += result.HomeGoals == result.AwayGoals ? 1 : 0;
        awayWins += result.HomeGoals < result.AwayGoals ? 1 : 0;

        maxGoals = Math.Max(maxGoals, result.HomeGoals + result.AwayGoals);
        maxShots = Math.Max(maxShots, result.Home.Shots + result.Away.Shots);

        var total = result.HomeGoals + result.AwayGoals;
        goalTotals[Math.Min(total, goalTotals.Length - 1)]++;
    }

    Print("goals per match", (double)(homeGoals + awayGoals) / matches, "2.5 – 3.0");
    Print("home goals", (double)homeGoals / matches, "1.3 – 1.9");
    Print("away goals", (double)awayGoals / matches, "1.0 – 1.5");
    Print("home win %", 100.0 * homeWins / matches, "40 – 50");
    Print("draw %", 100.0 * draws / matches, "20 – 30");
    Print("away win %", 100.0 * awayWins / matches, "25 – 35");
    Print("shots per match", (double)(homeShots + awayShots) / matches, "20 – 32");
    Print("home possession %", (double)homePossession / matches / 100.0, "50 – 54");
    Print("fouls per match", (double)fouls / matches, "18 – 26");
    Print("yellows per match", (double)yellows / matches, "3.0 – 5.0");
    Print("reds per match", (double)reds / matches, "0.10 – 0.35");
    Print("injuries per match", (double)injuries / matches, "0.20 – 0.60");
    Print("penalties per match", (double)penalties / matches, "0.15 – 0.40");
    Print("substitutions per match", (double)substitutions / matches, "4.0 – 10.0");
    Console.WriteLine($"  {"max goals in a match",-28} {maxGoals,10}");
    Console.WriteLine($"  {"max shots in a match",-28} {maxShots,10}");

    // The tail is what a scoreline distribution is actually judged on: a mean can sit perfectly while one
    // match in a thousand finishes 9-3, and that is the match a manager screenshots.
    var running = 0L;
    var percentile = 0;

    for (var goals = 0; goals < goalTotals.Length && percentile == 0; goals++)
    {
        running += goalTotals[goals];

        if (running >= matches * 0.99)
        {
            percentile = goals;
        }
    }

    var highScoring = goalTotals.Skip(7).Sum();

    Console.WriteLine($"  {"p99 total goals",-28} {percentile,10}   target 6 – 8");
    Console.WriteLine($"  {"matches with 7+ goals",-28} {100.0 * highScoring / matches,10:F3}%  target < 3.0%");
    Console.WriteLine();
}

void Bench(int matches, ulong baseSeed)
{
    Console.WriteLine($"== timing, {matches:N0} matches ==");

    var inputs = new MatchInputV1[matches];

    for (var index = 0; index < matches; index++)
    {
        inputs[index] = LaboratoryFixtures.EvenlyMatched(baseSeed + (ulong)index);
    }

    // Warm up, so the first run's JIT cost is not reported as a percentile.
    for (var index = 0; index < Math.Min(200, matches); index++)
    {
        _ = MatchSimulator.Simulate(inputs[index], rules);
    }

    var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    var samples = new double[matches];

    for (var index = 0; index < matches; index++)
    {
        var started = Stopwatch.GetTimestamp();
        _ = MatchSimulator.Simulate(inputs[index], rules);
        samples[index] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

    Array.Sort(samples);

    Console.WriteLine($"  p50   {Percentile(samples, 0.50):F3} ms");
    Console.WriteLine($"  p95   {Percentile(samples, 0.95):F3} ms   (budget: 100 ms)");
    Console.WriteLine($"  p99   {Percentile(samples, 0.99):F3} ms");
    Console.WriteLine($"  mean  {samples.Average():F3} ms");
    Console.WriteLine($"  allocated {allocated / matches / 1024.0:F1} KB per match");
    Console.WriteLine($"  throughput {matches / (samples.Sum() / 1_000.0):N0} matches/sec single-threaded");
    Console.WriteLine($"  hardware   {Environment.MachineName}, {Environment.ProcessorCount} logical cores, "
        + $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
    Console.WriteLine();
}

static double Percentile(double[] sorted, double fraction)
{
    var index = (int)Math.Clamp(Math.Round((sorted.Length - 1) * fraction), 0, sorted.Length - 1);

    return sorted[index];
}

static void Print(string label, double value, string target) =>
    Console.WriteLine($"  {label,-28} {value,10:F3}   target {target}");
