using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using TouchlineManager.Application.Match;
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
// Usage: dotnet run --project tools/simulation-benchmarks -- [single|distributions|replay|calibration|tactics|bench|offball|all] [count] [seed] [--dump file]
//
// `offball` is not part of `all`: it reads who receives the ball and what Positioning does at the finish (engine-v10).
//
// `--dump file` writes the first replayed match's presentation, as the API returns it, to a file: the fluidity
// harness in apps/web/.preview plays it back in a browser (replay-v7).

var dumpAt = Array.IndexOf(args, "--dump");
var dumpPath = dumpAt >= 0 && dumpAt + 1 < args.Length ? args[dumpAt + 1] : null;
var positional = args.Where((_, position) => dumpAt < 0 || (position != dumpAt && position != dumpAt + 1)).ToArray();

var mode = positional.Length > 0 ? positional[0] : "all";
var count = positional.Length > 1 && int.TryParse(positional[1], CultureInfo.InvariantCulture, out var parsed)
    ? parsed
    : mode switch
    {
        // The Stage 7 calibration is defined as ten thousand fixtures; the tactical invariants need fewer
        // matches because each one is a within-match comparison rather than a distribution.
        "calibration" => 10_000,
        "tactics" => 2_000,
        _ => 20_000,
    };
var seed = positional.Length > 2 && ulong.TryParse(positional[2], CultureInfo.InvariantCulture, out var parsedSeed)
    ? parsedSeed
    : 20_260_925UL;

var rules = EngineRulesV2.Default;

// RULES="LongShotMinX=7500,SoloDribbleUtilityBasisPoints=5500" tries constants without rebuilding: a tuning aid, never
// the rules a match is played with in the game.
if (Environment.GetEnvironmentVariable("RULES") is { Length: > 0 } overrides)
{
    rules = rules with { };

    foreach (var pair in overrides.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var parts = pair.Split('=', 2);
        var property = typeof(EngineRulesV2).GetProperty(parts[0])
            ?? throw new ArgumentException($"No engine rule named {parts[0]}.");

        property.SetValue(rules, int.Parse(parts[1], CultureInfo.InvariantCulture));
    }
}

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

if (mode is "calibration" or "all")
{
    // The full mode runs the defined ten-thousand-fixture calibration; the explicit mode honours whatever
    // count it is given, so a tuning session can sample more or fewer.
    Calibration(mode == "all" ? 10_000 : count, seed);
}

if (mode is "tactics" or "all")
{
    Tactics(mode == "all" ? 2_000 : count, seed);
}

if (mode is "replay" or "all")
{
    Replay(Math.Min(count, 10_000), seed, dumpPath);
}

if (mode is "bench" or "all")
{
    Bench(Math.Min(count, 5_000), seed);
}

if (mode is "offball")
{
    OffBallProbe.Run(count, seed, rules);
}

return 0;

void SingleMatch(ulong matchSeed)
{
    var input = LaboratoryFixtures.EvenlyMatched(matchSeed, rules);
    var liveMetrics = new PlayerLiveMetricsRecorder();
    var passages = new MatchPassageRecorder();
    var result = MatchSimulator.Simulate(input, rules, liveMetrics, passages);

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
    var presentation = ReplayDirector.Build(input, result, passages.Passages, liveMetrics: liveMetrics.Metrics);

    Console.WriteLine($"  commentary lines {commentary.Count}");
    Console.WriteLine($"  live metrics     {liveMetrics.Metrics.Count}");
    Console.WriteLine($"  passages         {presentation.Passages.Count}"
        + $", pace {presentation.PaceMilli / 1_000.0:F2}x"
        + $", film {presentation.TotalPlaybackMilliseconds / 60_000.0:F1} min"
        + $", reel {presentation.Reel.Sum(clip => clip.DurationMilliseconds) / 60_000.0:F1} min"
        + $", ~{presentation.EstimatedPayloadBytes / 1024.0:F1} KB");
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
            LaboratoryFixtures.EvenlyMatched(baseSeed + (ulong)index, rules),
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

void Replay(int matches, ulong baseSeed, string? dump)
{
    Console.WriteLine($"== {ReplayDirector.Version} constant-pace film and reel, {matches:N0} matches ==");

    var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var passageCounts = new double[matches];
    var paces = new double[matches];
    var filmDurations = new double[matches];
    var reelDurations = new double[matches];
    var estimates = new double[matches];
    var bytes = new double[matches];
    var condensedShare = new double[matches];
    var stillShare = new double[matches];
    var motionMinutes = new double[matches];
    var holdMinutes = new double[matches];
    var extensionShare = new double[matches];
    var playerSpeedRatio = new double[matches];
    var keeperSpeedRatio = new double[matches];
    var shapes = new List<ShapeMetrics>(matches);
    var ballByKind = new Dictionary<string, (List<double> Median, List<double> P95)>(StringComparer.Ordinal);
    var inWindow = 0;
    var overCeiling = 0;
    var inBand = 0;
    var condensedMatches = 0;
    var teleports = 0L;
    var cuts = 0L;
    var rungs = new int[4];
    var options = new HighlightOptionsV1();
    var elapsed = Stopwatch.StartNew();

    for (var index = 0; index < matches; index++)
    {
        // The film is judged on the board's own formation, not the laboratory's calibration shape.
        var input = LaboratoryFixtures.OnTheBoard(LaboratoryFixtures.EvenlyMatched(baseSeed + (ulong)index, rules));
        var liveMetrics = new PlayerLiveMetricsRecorder();
        var passages = new MatchPassageRecorder();
        var result = MatchSimulator.Simulate(input, rules, liveMetrics, passages);
        var built = ReplayDirector.Analyse(input, result, passages.Passages, options, liveMetrics.Metrics);
        var presentation = built.Presentation;
        var film = built.Diagnostics!;

        var response = presentation.ToResponse(Guid.Empty, CommentaryTokenBuilder.Build(input, result));
        var serialized = JsonSerializer.SerializeToUtf8Bytes(response, json);

        if (index == 0 && dump is not null)
        {
            File.WriteAllBytes(dump, serialized);
            Console.WriteLine($"  dumped match {baseSeed} to {dump} ({serialized.Length / 1024.0:F0} KB)");
        }

        passageCounts[index] = presentation.Passages.Count;
        paces[index] = film.Pace;
        filmDurations[index] = presentation.TotalPlaybackMilliseconds;
        reelDurations[index] = presentation.Reel.Sum(clip => clip.DurationMilliseconds);
        estimates[index] = presentation.EstimatedPayloadBytes;
        bytes[index] = serialized.Length;
        condensedShare[index] = film.Possessions == 0 ? 0 : (double)film.CondensedPossessions / film.Possessions;
        stillShare[index] = film.StillBallShare;
        motionMinutes[index] = film.Pace * (film.FilmMilliseconds - film.HoldMilliseconds) / 60_000.0;
        holdMinutes[index] = film.HoldMilliseconds / 60_000.0;
        extensionShare[index] = film.ExtensionShare;
        playerSpeedRatio[index] = film.MaxPlayerFilmSpeed / (options.SprintMetresPerSecond * film.Pace);
        keeperSpeedRatio[index] = film.MaxKeeperFilmSpeed / (options.DiveMetresPerSecond * film.Pace);
        teleports += film.Teleports;
        cuts += film.Cuts;
        shapes.Add(film.Shape);
        rungs[Math.Min(film.PayloadRung, rungs.Length - 1)]++;

        if (film.CondensedPossessions > 0)
        {
            condensedMatches++;
        }

        if (film.Pace >= options.MinPaceMilli / 1_000.0 - 0.005 && film.Pace <= options.MaxPaceMilli / 1_000.0 + 0.005)
        {
            inBand++;
        }

        foreach (var (kind, summary) in film.BallSpeeds)
        {
            if (!ballByKind.TryGetValue(kind, out var lists))
            {
                lists = ([], []);
                ballByKind[kind] = lists;
            }

            lists.Median.Add(summary.Median);
            lists.P95.Add(summary.Percentile95);
        }

        if (presentation.TotalPlaybackMilliseconds is >= (9 * 60 * 1000) and <= (11 * 60 * 1000))
        {
            inWindow++;
        }

        if (presentation.TotalPlaybackMilliseconds > 11 * 60 * 1000)
        {
            overCeiling++;
        }
    }

    foreach (var series in new[] { passageCounts, paces, filmDurations, reelDurations, estimates, bytes, condensedShare, stillShare, motionMinutes, holdMinutes, extensionShare, playerSpeedRatio, keeperSpeedRatio })
    {
        Array.Sort(series);
    }

    Console.WriteLine($"  {"passages per match",-30} {Percentile(passageCounts, 0.50),7} p50   {Percentile(passageCounts, 0.05),7} p05   {Percentile(passageCounts, 0.95),7} p95   max {passageCounts[^1]}");
    Console.WriteLine($"  {"pace p05/p50/p95",-30} {Percentile(paces, 0.05),7:F2}x      {Percentile(paces, 0.50),7:F2}x      {Percentile(paces, 0.95),7:F2}x      max {paces[^1]:F2}x   inside {options.MinPaceMilli / 1000.0:F1}-{options.MaxPaceMilli / 1000.0:F1}x {100.0 * inBand / matches:F1}%");
    Console.WriteLine($"  {"film minutes p05/p50/p95",-30} {Percentile(filmDurations, 0.05) / 60_000.0,7:F2}       {Percentile(filmDurations, 0.50) / 60_000.0,7:F2}       {Percentile(filmDurations, 0.95) / 60_000.0,7:F2}       min {filmDurations[0] / 60_000.0:F2}  max {filmDurations[^1] / 60_000.0:F2}");
    Console.WriteLine($"  {"reel minutes p05/p50/p95",-30} {Percentile(reelDurations, 0.05) / 60_000.0,7:F2}       {Percentile(reelDurations, 0.50) / 60_000.0,7:F2}       {Percentile(reelDurations, 0.95) / 60_000.0,7:F2}       max {reelDurations[^1] / 60_000.0:F2}");
    Console.WriteLine($"  {"inside 9:00-11:00",-30} {100.0 * inWindow / matches,7:F1}%   over 11 min {100.0 * overCeiling / matches:F2}%");
    Console.WriteLine($"  {"real-time motion / holds, min",-30} motion p50 {Percentile(motionMinutes, 0.50),5:F1} (p05 {Percentile(motionMinutes, 0.05):F1}, p95 {Percentile(motionMinutes, 0.95):F1})   holds p50 {Percentile(holdMinutes, 0.50),4:F2} (p95 {Percentile(holdMinutes, 0.95):F2})");
    Console.WriteLine($"  {"condensed possessions",-30} {100.0 * condensedMatches / matches,7:F1}% of matches   share of possessions p50 {100 * Percentile(condensedShare, 0.50):F1}% p95 {100 * Percentile(condensedShare, 0.95):F1}%");
    Console.WriteLine($"  {"moves lengthened for constraints",-30} p50 {100 * Percentile(extensionShare, 0.50):F1}%   p95 {100 * Percentile(extensionShare, 0.95):F1}%");
    Console.WriteLine($"  {"teleports (outside cuts)",-30} {teleports,7}   cuts per match {(double)cuts / matches:F1}");
    Console.WriteLine($"  {"ball still outside holds",-30} p50 {100 * Percentile(stillShare, 0.50):F1}%   p95 {100 * Percentile(stillShare, 0.95):F1}%   target <= 5%");
    Console.WriteLine($"  {"player speed / (sprint x pace)",-30} max {playerSpeedRatio[^1]:F3}   keeper / (dive x pace) max {keeperSpeedRatio[^1]:F3}   (1.000 is the cap)");
    Console.WriteLine($"  {"payload estimate KB p50/p95",-30} {Percentile(estimates, 0.50) / 1024.0,7:F1}       {Percentile(estimates, 0.95) / 1024.0,7:F1}       max {estimates[^1] / 1024.0:F1}   budget {options.PayloadBudgetBytes / 1024}");
    Console.WriteLine($"  {"payload JSON KB p50/p95",-30} {Percentile(bytes, 0.50) / 1024.0,7:F1}       {Percentile(bytes, 0.95) / 1024.0,7:F1}       max {bytes[^1] / 1024.0:F1}   (System.Text.Json, the API's own shape)");
    Console.WriteLine($"  {"payload ladder rungs",-30} {string.Join("  ", rungs.Select((rung, position) => $"{position}: {rung}"))}");

    // Median over matches of each match's own measure; the film's shape, in open play, with the dead balls left out.
    double Of(Func<ShapeMetrics, double> pick) => Percentile(shapes.Select(pick).Order().ToArray(), 0.50);

    Console.WriteLine("  shape in open play (median over matches):");
    Console.WriteLine($"    {"players within 5 m of ball",-34} p50 {Of(s => s.NearBallP50),5:F1}   p95 {Of(s => s.NearBallP95),5:F1}   target <= 4 at p95");
    Console.WriteLine($"    {"steps with 5+ within 5 m of ball",-34} {Of(s => s.CrowdedShare) * 100,5:F1}%   target < 2%");
    Console.WriteLine($"    {"steps with 4+ within 3 m of ball",-34} {Of(s => s.ClusteredShare) * 100,5:F1}%   target < 1%");
    Console.WriteLine($"    {"nearest team-mate, p5",-34} {Of(s => s.NeighbourSpacingP5),5:F1} m   target >= 2");
    Console.WriteLine($"    {"block depth / width, with ball",-34} {Of(s => s.InPossessionDepth),5:F1} / {Of(s => s.InPossessionWidth),5:F1} m");
    Console.WriteLine($"    {"block depth / width, without ball",-34} {Of(s => s.OutOfPossessionDepth),5:F1} / {Of(s => s.OutOfPossessionWidth),5:F1} m   target depth 20-30, width 45-55");
    Console.WriteLine($"    {"back line / front line from goal",-34} {Of(s => s.BackLineDepth),5:F1} / {Of(s => s.FrontLineDepth),5:F1} m   (side without the ball)");
    Console.WriteLine($"    {"ball in own third: deep / back line",-34} {Of(s => s.DeepBlockOutfield),5:F1} players within 30 m, back line {Of(s => s.DeepBlockBackLine),5:F1} m   target 8-9, ~18");
    Console.WriteLine($"    {"ball in a box: players in box",-34} p50 {Of(s => s.InBoxP50),5:F1}   p95 {Of(s => s.InBoxP95),5:F1}   six-yard p95 {Of(s => s.SixYardP95),5:F1}   target six-yard <= 7");
    Console.WriteLine($"    {"cross arrives: in box att / def",-34} {Of(s => s.DeliveryAttackersP50),5:F1} / {Of(s => s.DeliveryDefendersP50),5:F1}   six-yard p95 {Of(s => s.DeliverySixYardP95),5:F1}   ({Of(s => s.Deliveries),4:F0} crosses per match)   target 3-5 / 4-6, six-yard <= 7");
    Console.WriteLine($"    {"corner delivered: in box att / def",-34} {Of(s => s.CornerAttackersP50),5:F1} / {Of(s => s.CornerDefendersP50),5:F1}   six-yard p95 {Of(s => s.CornerSixYardP95),5:F1}   held back {Of(s => s.CornerGuardsP50),4:F1}   ({Of(s => s.Corners),4:F0} corners per match)   target 4 / 6-7, six-yard <= 7, held back 2-3");
    Console.WriteLine($"    {"free kick about to be taken",-34} inside 9.15 m p95 {Of(s => s.FreeKickIntrudersP95),4:F1}   wall p50 {Of(s => s.FreeKickWallP50),4:F1}   ({Of(s => s.FreeKicks),4:F1} per match)   target 0, wall 2-5");
    Console.WriteLine($"    {"penalty about to be taken",-34} inside arc or box p95 {Of(s => s.PenaltyIntrudersP95),4:F1}   ({Of(s => s.Penalties),4:F1} per match)   target 0");
    Console.WriteLine("  ball speed by beat, metres per second of film (median over matches of p50 / p95):");

    foreach (var (kind, lists) in ballByKind.OrderBy(pair => pair.Key, StringComparer.Ordinal))
    {
        lists.Median.Sort();
        lists.P95.Sort();

        Console.WriteLine($"    {kind,-12} {lists.Median[lists.Median.Count / 2],6:F1} / {lists.P95[lists.P95.Count / 2],6:F1}");
    }

    Console.WriteLine($"  {elapsed.Elapsed.TotalSeconds / matches * 1000:F0} ms per match (simulate, film, serialize)");
    Console.WriteLine();
}

void Bench(int matches, ulong baseSeed)
{
    Console.WriteLine($"== timing, {matches:N0} matches ==");

    var inputs = new MatchInputV1[matches];

    for (var index = 0; index < matches; index++)
    {
        inputs[index] = LaboratoryFixtures.EvenlyMatched(baseSeed + (ulong)index, rules);
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

void Calibration(int fixtures, ulong baseSeed)
{
    Console.WriteLine($"== calibration, {fixtures:N0} fixtures ==");

    // Home advantage is a multiplier on the home side's ratings, so the counterfactual rules set it to
    // neutral (10_000). A snapshot must carry the hash of the rules it is simulated under, so the neutral
    // fixture is built against the neutral rules rather than the shipped ones.
    var neutralRules = EngineRulesV2.Default with { HomeAdvantageBasisPoints = EngineRulesV2.Certain };

    long homeGoals = 0;
    long awayGoals = 0;
    long homeWins = 0;
    long draws = 0;
    long awayWins = 0;
    long neutralHomeWins = 0;
    long neutralAwayWins = 0;
    for (var index = 0; index < fixtures; index++)
    {
        var matchSeed = baseSeed + (ulong)index;

        var even = MatchSimulator.Simulate(LaboratoryFixtures.EvenlyMatched(matchSeed, rules), rules);
        var neutral = MatchSimulator.Simulate(
            LaboratoryFixtures.EvenlyMatched(matchSeed, neutralRules), neutralRules);

        homeGoals += even.HomeGoals;
        awayGoals += even.AwayGoals;
        homeWins += even.HomeGoals > even.AwayGoals ? 1 : 0;
        draws += even.HomeGoals == even.AwayGoals ? 1 : 0;
        awayWins += even.HomeGoals < even.AwayGoals ? 1 : 0;

        neutralHomeWins += neutral.HomeGoals > neutral.AwayGoals ? 1 : 0;
        neutralAwayWins += neutral.HomeGoals < neutral.AwayGoals ? 1 : 0;
    }

    var homeWinShare = 100.0 * homeWins / fixtures;
    var neutralHomeWinShare = 100.0 * neutralHomeWins / fixtures;

    Print("goals per match", (double)(homeGoals + awayGoals) / fixtures, "2.50 – 3.00");
    Print("home win %", homeWinShare, "40 – 50");
    Print("draw %", 100.0 * draws / fixtures, "20 – 30");
    Print("away win %", 100.0 * awayWins / fixtures, "25 – 35");
    Console.WriteLine(
        $"  {"neutral home/away win %",-28} {neutralHomeWinShare,10:F3} / {100.0 * neutralAwayWins / fixtures:F3}   (home advantage removed)");
    Verdict("home advantage, points", homeWinShare - neutralHomeWinShare, 2.0, 5.5, "about +4 (Stage 7)");

    // The underdog figure is measured across a sweep of ability gaps rather than at one arbitrary mismatch:
    // the plan states "about 15%" without naming the gap, and a 16-versus-10 side is a different question
    // from a 14-versus-12 one. Each gap is played both ways — the better side at home and away — so home
    // advantage does not decide which side counts as the underdog.
    var sweepFixtures = Math.Min(fixtures, 2_000);
    double? underdogAtThree = null;

    Console.WriteLine($"  underdog win %, by ability gap ({sweepFixtures:N0} fixtures each):");

    // A gap of eight would ask for an attribute of twenty-one, so the sweep stops at the top of the scale.
    for (var gap = 1; gap <= 7; gap++)
    {
        long weakWins = 0;
        long strongWins = 0;
        long mismatchDraws = 0;

        for (var index = 0; index < sweepFixtures; index++)
        {
            var matchSeed = baseSeed + (ulong)index;

            var strongAtHome = MatchSimulator.Simulate(
                LaboratoryFixtures.Build(matchSeed, 13 + gap, 13, new MatchInstructionsV1(), new MatchInstructionsV1()),
                rules);
            var strongAway = MatchSimulator.Simulate(
                LaboratoryFixtures.Build(matchSeed, 13, 13 + gap, new MatchInstructionsV1(), new MatchInstructionsV1()),
                rules);

            weakWins += strongAtHome.AwayGoals > strongAtHome.HomeGoals ? 1 : 0;
            weakWins += strongAway.HomeGoals > strongAway.AwayGoals ? 1 : 0;
            strongWins += strongAtHome.HomeGoals > strongAtHome.AwayGoals ? 1 : 0;
            strongWins += strongAway.AwayGoals > strongAway.HomeGoals ? 1 : 0;
            mismatchDraws += strongAtHome.HomeGoals == strongAtHome.AwayGoals ? 1 : 0;
            mismatchDraws += strongAway.HomeGoals == strongAway.AwayGoals ? 1 : 0;
        }

        var weakShare = 100.0 * weakWins / (2.0 * sweepFixtures);

        if (gap == 3)
        {
            underdogAtThree = weakShare;
        }

        Console.WriteLine($"    gap +{gap}: weak {weakShare,6:F2}%   strong {100.0 * strongWins / (2.0 * sweepFixtures),6:F2}%   level {100.0 * mismatchDraws / (2.0 * sweepFixtures),6:F2}%");
    }

    // Three attribute points is a clear tier of difference — a good side against a poor one — and it is
    // where the plan's "about 15%" underdog sits on this engine's curve.
    Verdict("underdog win % (gap +3)", underdogAtThree ?? 0, 10.0, 20.0, "about 15 (Stage 7)");
    Console.WriteLine();
}

void Tactics(int fixtures, ulong baseSeed)
{
    Console.WriteLine($"== tactical invariants, {fixtures:N0} fixtures per experiment ==");

    // ---- Experiment 1: an early sending-off --------------------------------------------------------
    // Both sides tackle aggressively, so either can be the side reduced, and the experiment measures the
    // red-carded side's final goal difference relative to its opponent. Even sides kick off level, and the
    // two sides are interchangeable, so a random side's expected goal difference is zero: the figure below
    // is the sending-off's cost, not a home-versus-away artefact. Matches where neither side (or both) saw
    // an early red are set aside, so the comparison is never diluted by a second dismissal.
    var aggressive = new MatchInstructionsV1 { Tackling = MatchTacklingStyle.Aggressive };

    long redSideDifference = 0;
    long redSideFixtures = 0;

    for (var index = 0; index < fixtures; index++)
    {
        var input = LaboratoryFixtures.Build(baseSeed + (ulong)index, 13, 13, aggressive, aggressive);
        var result = MatchSimulator.Simulate(input, rules);

        var homeRed = result.Events.Any(matchEvent => matchEvent.Side == MatchSide.Home
            && matchEvent.Minute < 30
            && matchEvent.Type is EngineEventType.RedCard or EngineEventType.SecondYellowCard);
        var awayRed = result.Events.Any(matchEvent => matchEvent.Side == MatchSide.Away
            && matchEvent.Minute < 30
            && matchEvent.Type is EngineEventType.RedCard or EngineEventType.SecondYellowCard);

        if (homeRed == awayRed)
        {
            continue;
        }

        redSideDifference += homeRed
            ? result.HomeGoals - result.AwayGoals
            : result.AwayGoals - result.HomeGoals;
        redSideFixtures++;
    }

    var redSideMean = (double)redSideDifference / Math.Max(1, redSideFixtures);

    Console.WriteLine($"  {"early red fixtures",-28} {redSideFixtures,10:N0}   of {fixtures:N0}");
    Console.WriteLine($"  {"red-carded side's goal difference",-28} {redSideMean,10:F3}   (zero without the card)");
    Verdict("sending-off drop, goals", -redSideMean, 0.9, 1.5, "about 1.2 (Stage 7)");
    Console.WriteLine();

    // ---- Experiment 2: fatigue at the eighty minute --------------------------------------------
    // One side presses high and plays fast, the other sits deep and plays slowly. Every starter's condition
    // is read at their last capture up to the eightieth minute — the low point of the players the side
    // actually used — and at the eightieth minute itself, which is who is still on the pitch.
    var highPress = new MatchInstructionsV1
    {
        Tempo = MatchTempo.High,
        Pressing = MatchPressing.HighPress,
    };
    var lowBlock = new MatchInstructionsV1
    {
        Tempo = MatchTempo.Low,
        Pressing = MatchPressing.LowBlock,
    };

    long pressLastSum = 0;
    long pressLastCount = 0;
    long blockLastSum = 0;
    long blockLastCount = 0;
    long pressAt80Sum = 0;
    long pressAt80Count = 0;
    long blockAt80Sum = 0;
    long blockAt80Count = 0;
    long pressSubs = 0;
    long blockSubs = 0;

    for (var index = 0; index < fixtures; index++)
    {
        var input = LaboratoryFixtures.Build(baseSeed + (ulong)index, 13, 13, highPress, lowBlock);
        var recorder = new PlayerLiveMetricsRecorder();
        var result = MatchSimulator.Simulate(input, rules, recorder);

        var lastAtOrBefore80 = new Dictionary<Guid, int>();
        var onPitchAt80 = new Dictionary<Guid, int>();

        foreach (var metric in recorder.Metrics)
        {
            if (metric.Minute <= 80)
            {
                lastAtOrBefore80[metric.ParticipantId] = metric.ConditionBasisPoints;
            }

            if (metric.Minute == 80)
            {
                onPitchAt80[metric.ParticipantId] = metric.ConditionBasisPoints;
            }
        }

        foreach (var slot in input.Home.Slots)
        {
            if (lastAtOrBefore80.TryGetValue(slot.ParticipantId, out var condition))
            {
                pressLastSum += condition;
                pressLastCount++;
            }

            if (onPitchAt80.TryGetValue(slot.ParticipantId, out var at80))
            {
                pressAt80Sum += at80;
                pressAt80Count++;
            }
        }

        foreach (var slot in input.Away.Slots)
        {
            if (lastAtOrBefore80.TryGetValue(slot.ParticipantId, out var condition))
            {
                blockLastSum += condition;
                blockLastCount++;
            }

            if (onPitchAt80.TryGetValue(slot.ParticipantId, out var at80))
            {
                blockAt80Sum += at80;
                blockAt80Count++;
            }
        }

        pressSubs += result.Home.Substitutions;
        blockSubs += result.Away.Substitutions;
    }

    var pressLast = (double)pressLastSum / Math.Max(1, pressLastCount);
    var blockLast = (double)blockLastSum / Math.Max(1, blockLastCount);
    var pressAt80 = (double)pressAt80Sum / Math.Max(1, pressAt80Count);
    var blockAt80 = (double)blockAt80Sum / Math.Max(1, blockAt80Count);

    Console.WriteLine($"  {"high press condition @ ≤80",-28} {pressLast,10:F0}   (last sighting of the starters)");
    Console.WriteLine($"  {"low block condition @ ≤80",-28} {blockLast,10:F0}");
    Verdict("high-press fatigue gap", blockLast - pressLast, 250, 5_000, "press is tired by 80'");
    Console.WriteLine($"  {"high press on pitch @ 80",-28} {pressAt80,10:F0}");
    Console.WriteLine($"  {"low block on pitch @ 80",-28} {blockAt80,10:F0}");
    Console.WriteLine($"  {"high press substitutions",-28} {(double)pressSubs / fixtures,10:F2}   low block {(double)blockSubs / fixtures:F2}");
    Console.WriteLine();

    // ---- Experiment 3: a fresh substitute against tired defenders -------------------------------
    // Every substitution after the hour, compared at the minute it happened with the opposition defenders
    // who were on the pitch beside it. The gap is what "a fresh substitute" is worth in the panel: the
    // condition (and through it the pace) the manager sees when the change is made.
    long substituteSum = 0;
    long substituteCount = 0;
    long defenderSum = 0;
    long defenderCount = 0;

    for (var index = 0; index < fixtures; index++)
    {
        var input = LaboratoryFixtures.EvenlyMatched(baseSeed + (ulong)index, rules);
        var recorder = new PlayerLiveMetricsRecorder();
        var result = MatchSimulator.Simulate(input, rules, recorder);

        var byMinute = new Dictionary<int, Dictionary<Guid, int>>();

        foreach (var metric in recorder.Metrics)
        {
            if (!byMinute.TryGetValue(metric.Minute, out var atMinute))
            {
                atMinute = [];
                byMinute[metric.Minute] = atMinute;
            }

            atMinute[metric.ParticipantId] = metric.ConditionBasisPoints;
        }

        foreach (var substitution in result.Events.Where(
            matchEvent => matchEvent.Type == EngineEventType.Substitution && matchEvent.Minute >= 60))
        {
            var incoming = substitution.SecondaryParticipantId!.Value;
            var firstMinute = recorder.Metrics
                .Where(metric => metric.ParticipantId == incoming)
                .Select(metric => (int?)metric.Minute)
                .FirstOrDefault();

            if (firstMinute is null || !byMinute.TryGetValue(firstMinute.Value, out var atMinute))
            {
                continue;
            }

            var fresh = atMinute[incoming];
            var opponent = substitution.Side == MatchSide.Home ? input.Away : input.Home;

            foreach (var slot in opponent.Slots.Where(slot => slot.Family == MatchPositionFamily.Defence))
            {
                if (atMinute.TryGetValue(slot.ParticipantId, out var condition))
                {
                    defenderSum += condition;
                    defenderCount++;
                }
            }

            substituteSum += fresh;
            substituteCount++;
        }
    }

    var substituteMean = (double)substituteSum / Math.Max(1, substituteCount);
    var defenderMean = (double)defenderSum / Math.Max(1, defenderCount);

    Console.WriteLine($"  {"substitutions after 60'",-28} {substituteCount,10:N0}");
    Console.WriteLine($"  {"substitute condition",-28} {substituteMean,10:F0}");
    Console.WriteLine($"  {"opposing defenders' condition",-28} {defenderMean,10:F0}");
    Verdict("fresh legs, condition", substituteMean - defenderMean, 800, 5_000, "> 800");
    Console.WriteLine();
}

static void Verdict(string label, double value, double minimum, double maximum, string target)
{
    var state = value >= minimum && value <= maximum ? "within" : "OUTSIDE";

    Console.WriteLine($"  {label,-28} {value,10:F3}   target {target,-22} {state}");
}

static double Percentile(double[] sorted, double fraction)
{
    var index = (int)Math.Clamp(Math.Round((sorted.Length - 1) * fraction), 0, sorted.Length - 1);

    return sorted[index];
}



static void Print(string label, double value, string target) =>
    Console.WriteLine($"  {label,-28} {value,10:F3}   target {target}");
