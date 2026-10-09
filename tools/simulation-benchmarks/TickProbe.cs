using System.Diagnostics;
using System.Text.Json;
using TouchlineManager.Application.Match;
using TouchlineManager.Domain.Squad;
using TouchlineManager.MatchEngine;
using TouchlineManager.MatchEngine.Commentary;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.SimulationBenchmarks;

/// <summary>
/// The tick probe (`tick-engine-v1`, Milestone 8): plays matches on the tick engine's loop and prints what they came to.
/// </summary>
/// <remarks>
/// The loop is run on the engine directly, so the probe works whether or not the tick engine is the default. It prints the
/// per-match figures the plan's calibration bands are written in, how long a match takes to play and to film, and the film's own
/// shape: how long it runs, how many passages and cuts it has and how large it is. With a dump path the first match's film is
/// written as the API would return it, for the fluidity harness in <c>apps/web/.preview</c>.
/// </remarks>
internal static class TickProbe
{
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
    /// <param name="dump">Where to write the first match's film, or null.</param>
    public static void Run(int matches, ulong seed, EngineRulesV2 rules, string? dump)
    {
        Console.WriteLine($"== Tick engine, {matches:N0} matches ==");

        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var goals = new double[matches];
        var shots = new double[matches];
        var onTarget = new double[matches];
        var saves = new double[matches];
        var corners = new double[matches];
        var fouls = new double[matches];
        var yellows = new double[matches];
        var passes = new double[matches];
        var completed = new double[matches];
        var filmSeconds = new double[matches];
        var passageCounts = new double[matches];
        var cuts = new double[matches];
        var kilobytes = new double[matches];
        var homeWins = 0;
        var draws = 0;
        var play = new Stopwatch();
        var film = new Stopwatch();

        for (var index = 0; index < matches; index++)
        {
            // The sides take the board's formations in turn, so the figures are not those of the one shape that scores least.
            var input = TickCalibration.Stand(
                LaboratoryFixtures.EvenlyMatched(seed + (ulong)index, rules),
                Shapes[index % Shapes.Length],
                Shapes[(index / Shapes.Length) % Shapes.Length]);
            var liveMetrics = new PlayerLiveMetricsRecorder();
            var recorder = new MatchPassageRecorder();

            play.Start();
            var result = MatchSimulator.Simulate(input, rules, liveMetrics, recorder, TickMatchEngine.Instance);
            play.Stop();

            film.Start();
            var presentation = ReplayDirector.Build(input, result, recorder, liveMetrics: liveMetrics.Metrics);
            film.Stop();

            goals[index] = result.HomeGoals + result.AwayGoals;
            shots[index] = result.Home.Shots + result.Away.Shots;
            onTarget[index] = result.Home.ShotsOnTarget + result.Away.ShotsOnTarget;
            saves[index] = result.Home.Saves + result.Away.Saves;
            corners[index] = result.Home.Corners + result.Away.Corners;
            fouls[index] = result.Home.Fouls + result.Away.Fouls;
            yellows[index] = result.Home.YellowCards + result.Away.YellowCards;
            passes[index] = result.PlayerLines.Sum(line => line.PassesAttempted);
            completed[index] = result.PlayerLines.Sum(line => line.PassesCompleted);
            filmSeconds[index] = presentation.TotalPlaybackMilliseconds / 1_000.0;
            passageCounts[index] = presentation.Passages.Count;
            cuts[index] = presentation.Passages.Sum(passage => passage.Cuts.Count);
            kilobytes[index] = presentation.EstimatedPayloadBytes / 1_024.0;

            if (result.HomeGoals > result.AwayGoals)
            {
                homeWins++;
            }
            else if (result.HomeGoals == result.AwayGoals)
            {
                draws++;
            }

            if (index == 0 && dump is not null)
            {
                var response = presentation.ToResponse(Guid.Empty, CommentaryTokenBuilder.Build(input, result));
                var serialized = JsonSerializer.SerializeToUtf8Bytes(response, json);

                File.WriteAllBytes(dump, serialized);
                Console.WriteLine($"  dumped match {seed} to {dump} ({serialized.Length / 1024.0:F0} KB)");
            }
        }

        static string Row(string label, double[] values, string target) =>
            $"  {label,-24} {values.Average(),10:F2}   {target}";

        Console.WriteLine(Row("goals per match", goals, "target 2.60 - 2.90"));
        Console.WriteLine(Row("shots per match", shots, "target 22 - 28"));
        Console.WriteLine($"  {"shots on target",-24} {100.0 * onTarget.Sum() / Math.Max(1, shots.Sum()),9:F1}%   target 32% - 38%");
        Console.WriteLine(Row("saves per match", saves, string.Empty));
        Console.WriteLine(Row("corners per match", corners, string.Empty));
        Console.WriteLine(Row("fouls per match", fouls, string.Empty));
        Console.WriteLine(Row("yellow cards per match", yellows, "target 3.0 - 4.5"));
        Console.WriteLine($"  {"pass completion",-24} {100.0 * completed.Sum() / Math.Max(1, passes.Sum()),9:F1}%   target 75% - 85%");
        Console.WriteLine($"  {"home win / draw",-24} {100.0 * homeWins / matches,5:F1}% / {100.0 * draws / matches:F1}%   target 42 - 48 / 22 - 26");
        Console.WriteLine(Row("film seconds", filmSeconds, "target 570 - 660"));
        Console.WriteLine(Row("passages per film", passageCounts, "cap 75"));
        Console.WriteLine(Row("cuts per film", cuts, string.Empty));
        Console.WriteLine(Row("payload estimate (KB)", kilobytes, "budget 750"));
        Console.WriteLine($"  {"play (ms per match)",-24} {play.Elapsed.TotalMilliseconds / matches,10:F0}");
        Console.WriteLine($"  {"film (ms per match)",-24} {film.Elapsed.TotalMilliseconds / matches,10:F0}");
        Console.WriteLine();
    }
}
