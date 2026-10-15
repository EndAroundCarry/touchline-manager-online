using TouchlineManager.Application.Match;
using TouchlineManager.Domain.Squad;
using TouchlineManager.MatchEngine;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.SimulationBenchmarks;

/// <summary>Direct free kick probe: how each free kick shot ended, by distance.</summary>
internal static class FreeKickProbe
{
    private static readonly FormationPreset[] Shapes =
    [
        FormationPreset.FourFourTwo,
        FormationPreset.FourThreeThree,
        FormationPreset.FourTwoThreeOne,
        FormationPreset.FiveThreeTwo,
    ];

    public static void Run(int matches, ulong seed, EngineRulesV2 rules)
    {
        var rows = new List<(int Distance, int Quality, string Outcome)>();
        var gate = new object();

        Parallel.For(0, matches, new ParallelOptions { MaxDegreeOfParallelism = 10 }, index =>
        {
            var input = TickCalibration.Stand(
                LaboratoryFixtures.EvenlyMatched(seed + (ulong)index, rules),
                Shapes[index % Shapes.Length],
                Shapes[(index / Shapes.Length) % Shapes.Length]);
            var result = MatchSimulator.Simulate(input, rules, new PlayerLiveMetricsRecorder(), new MatchPassageRecorder(), TickMatchEngine.Instance);
            var events = result.Events;
            var local = new List<(int, int, string)>();

            for (var i = 0; i < events.Count; i++)
            {
                if (events[i].Type != EngineEventType.FreeKickShot)
                {
                    continue;
                }

                var kick = events[i];
                var outcome = "none";

                for (var j = i + 1; j < events.Count && j <= i + 3; j++)
                {
                    if (events[j].Type is EngineEventType.Goal or EngineEventType.ShotSaved or EngineEventType.ShotBlocked
                        or EngineEventType.ShotOffTarget or EngineEventType.Woodwork && events[j].Side == kick.Side)
                    {
                        outcome = events[j].Type.ToString();
                        break;
                    }
                }

                // X is normalised 0-10000 along the pitch in the home view? distance from the goal being attacked.
                var x = kick.X ?? 0;
                var attackingHome = kick.Side == MatchSide.Home;
                var distance = attackingHome ? 10_000 - x : x;

                local.Add((distance, kick.QualityBasisPoints ?? 0, outcome));
            }

            lock (gate)
            {
                rows.AddRange(local);
            }
        });

        Console.WriteLine($"== Free kick shots, {matches} matches: {rows.Count} ({rows.Count / (double)matches:F2} per match) ==");

        foreach (var group in rows.GroupBy(r => r.Outcome).OrderByDescending(g => g.Count()))
        {
            Console.WriteLine($"  {group.Key,-14} {group.Count(),5}  {100.0 * group.Count() / rows.Count,5:F1}%");
        }

        Console.WriteLine($"  mean quality {rows.Average(r => r.Quality) / 100.0:F1}%  mean distance {rows.Average(r => r.Distance) / 100.0 * 1.05:F1} (units/100)");

        foreach (var band in rows.GroupBy(r => r.Distance / 500).OrderBy(g => g.Key))
        {
            var goals = band.Count(r => r.Outcome == "Goal");

            Console.WriteLine($"  dist {band.Key * 500,5}-{band.Key * 500 + 499,5}: {band.Count(),4} kicks, {goals,3} goals ({100.0 * goals / band.Count():F0}%), quality {band.Average(r => r.Quality) / 100.0:F1}%");
        }
    }

    /// <summary>Plays a grid of outfield ability against goalkeeper ability and prints how free kicks end in each cell.</summary>
    public static void Grid(int matches, ulong seed, EngineRulesV2 rules)
    {
        int[] levels = [6, 13, 20];
        int[] takers = Environment.GetEnvironmentVariable("FK_TAKER") is { Length: > 0 } t ? [int.Parse(t)] : levels;
        int[] keeperLevels = Environment.GetEnvironmentVariable("FK_KEEPER") is { Length: > 0 } k ? [int.Parse(k)] : levels;

        Console.WriteLine($"== Free kick shots by outfield ability (taker) and goalkeeper ability, {matches} matches a cell ==");
        Console.WriteLine($"  {"taker",5} {"keeper",6} {"kicks",6} {"goal%",6} {"saved%",7} {"wall%",6} {"off%",6} {"wood%",6} {"onTgt%",7} {"goals/match",11}");

        foreach (var outfield in takers)
        {
            foreach (var keeper in keeperLevels)
            {
                var rows = new System.Collections.Concurrent.ConcurrentBag<string>();

                Parallel.For(0, matches, new ParallelOptions { MaxDegreeOfParallelism = 10 }, index =>
                {
                    var input = LaboratoryFixtures.Build(seed + (ulong)index, outfield, outfield, new MatchInstructionsV1(), new MatchInstructionsV1(), rules);

                    input = TickCalibration.Stand(input, Shapes[index % Shapes.Length], Shapes[(index / Shapes.Length) % Shapes.Length]);
                    input = input with { Home = WithKeeper(input.Home, keeper), Away = WithKeeper(input.Away, keeper) };

                    var result = MatchSimulator.Simulate(input, rules, new PlayerLiveMetricsRecorder(), new MatchPassageRecorder(), TickMatchEngine.Instance);
                    var events = result.Events;

                    for (var i = 0; i < events.Count; i++)
                    {
                        if (events[i].Type != EngineEventType.FreeKickShot)
                        {
                            continue;
                        }

                        var outcome = "None";

                        for (var j = i + 1; j < events.Count && j <= i + 3; j++)
                        {
                            if (events[j].Type is EngineEventType.Goal or EngineEventType.ShotSaved or EngineEventType.ShotBlocked
                                or EngineEventType.ShotOffTarget or EngineEventType.Woodwork && events[j].Side == events[i].Side)
                            {
                                outcome = events[j].Type.ToString();
                                break;
                            }
                        }

                        rows.Add(outcome);
                    }
                });

                var n = Math.Max(1, rows.Count);
                double Share(string name) => 100.0 * rows.Count(r => r == name) / n;
                var goals = rows.Count(r => r == "Goal");

                Console.WriteLine($"  {outfield,5} {keeper,6} {rows.Count,6} {Share("Goal"),6:F1} {Share("ShotSaved"),7:F1} {Share("ShotBlocked"),6:F1} {Share("ShotOffTarget"),6:F1} {Share("Woodwork"),6:F1} {Share("Goal") + Share("ShotSaved"),7:F1} {goals / (double)matches,11:F2}");
            }
        }
    }

    private static MatchSideV1 WithKeeper(MatchSideV1 side, int ability) =>
        side with
        {
            Squad = [.. side.Squad.Select(player => player.IsGoalkeeper ? player with { Attributes = PlayerAttributesV1.Uniform(ability) } : player)],
        };
}
