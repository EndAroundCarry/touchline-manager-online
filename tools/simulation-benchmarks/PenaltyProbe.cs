using Tactical11Manager.Application.Match;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.MatchEngine;
using Tactical11Manager.MatchEngine.Configuration;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Shootout;
using Tactical11Manager.MatchEngine.Simulation;
using Tactical11Manager.MatchEngine.Tick;

namespace Tactical11Manager.SimulationBenchmarks;

/// <summary>
/// Penalty probe for the promotion play-off plan: how often the tick engine converts a penalty against how often the
/// shoot-out formula says the same taker would, so the two can be compared.
/// </summary>
internal static class PenaltyProbe
{
    private static readonly int[] Levels = [6, 13, 20];

    private static readonly FormationPreset[] Shapes =
    [
        FormationPreset.FourFourTwo,
        FormationPreset.FourThreeThree,
        FormationPreset.FourTwoThreeOne,
        FormationPreset.FiveThreeTwo,
    ];

    /// <summary>
    /// Plays matches in which each side's outfield ability and each goalkeeper's ability are drawn from low, middle and
    /// high, and prints the tick engine's penalty conversion beside the formula's average chance for the same
    /// takers and keepers.
    /// </summary>
    public static void Run(int matches, ulong seed, EngineRulesV2 rules)
    {
        var rows = new System.Collections.Concurrent.ConcurrentBag<(int Taker, int Keeper, int Taken, int Scored, double Formula)>();

        Parallel.For(0, matches, new ParallelOptions { MaxDegreeOfParallelism = 10 }, index =>
        {
            var homeLevel = Levels[index % 3];
            var awayLevel = Levels[(index / 3) % 3];
            var homeKeeper = Levels[(index / 9) % 3];
            var awayKeeper = Levels[(index / 27) % 3];

            var input = LaboratoryFixtures.Build(seed + (ulong)index, homeLevel, awayLevel, new MatchInstructionsV1(), new MatchInstructionsV1(), rules);

            input = TickCalibration.Stand(input, Shapes[index % Shapes.Length], Shapes[(index / Shapes.Length) % Shapes.Length]);
            input = input with { Home = WithKeeper(input.Home, homeKeeper), Away = WithKeeper(input.Away, awayKeeper) };

            var result = MatchSimulator.Simulate(input, rules, new PlayerLiveMetricsRecorder(), new MatchPassageRecorder(), TickMatchEngine.Instance);

            // Everyone as if on the pitch at full time, so a taker who was later substituted still gets a slot.
            var everyone = result.PlayerLines
                .Select(line => line with { Started = true, SubbedOutMinute = null, SentOff = false, IsInjured = false })
                .ToList();

            var home = ShootoutSide.FromFullTime(input.Home, everyone);
            var away = ShootoutSide.FromFullTime(input.Away, everyone);

            foreach (var line in result.PlayerLines.Where(line => line.PenaltiesTaken > 0))
            {
                var takerSide = line.Side == MatchSide.Home ? home : away;
                var keeperSide = line.Side == MatchSide.Home ? away : home;
                var taker = takerSide.OnPitch.Single(slot => slot.Participant.ParticipantId == line.ParticipantId);
                var keeper = keeperSide.OnPitch.FirstOrDefault(slot => slot.Participant.IsGoalkeeper);
                var chance = SetPieceDirector.PenaltyGoalChance(taker, keeper, rules) / 10_000.0;

                rows.Add((
                    line.Side == MatchSide.Home ? homeLevel : awayLevel,
                    line.Side == MatchSide.Home ? awayKeeper : homeKeeper,
                    line.PenaltiesTaken,
                    line.PenaltiesScored,
                    chance * line.PenaltiesTaken));
            }
        });

        var all = rows.ToList();
        var taken = all.Sum(row => row.Taken);

        Console.WriteLine($"== Penalties, {matches} matches: {taken} taken ({taken / (double)matches:F2} per match) ==");
        Console.WriteLine($"  all           taken {taken,4}  tick {100.0 * all.Sum(row => row.Scored) / taken,5:F1}%  formula {100.0 * all.Sum(row => row.Formula) / taken,5:F1}%");

        Console.WriteLine("  by taker side's outfield ability (formula reads the taker's finishing and composure):");

        foreach (var level in Levels)
        {
            Print($"taker {level,2}", all.Where(row => row.Taker == level));
        }

        Console.WriteLine("  by the defending goalkeeper's ability (formula reads his reflexes):");

        foreach (var level in Levels)
        {
            Print($"keeper {level,2}", all.Where(row => row.Keeper == level));
        }

        Console.WriteLine("  corners:");
        Print("taker 20 v keeper 6", all.Where(row => row.Taker == 20 && row.Keeper == 6));
        Print("taker 6 v keeper 20", all.Where(row => row.Taker == 6 && row.Keeper == 20));
    }

    private static void Print(string label, IEnumerable<(int Taker, int Keeper, int Taken, int Scored, double Formula)> rows)
    {
        var list = rows.ToList();
        var taken = list.Sum(row => row.Taken);

        if (taken == 0)
        {
            Console.WriteLine($"  {label,-20} taken    0");

            return;
        }

        Console.WriteLine($"  {label,-20} taken {taken,4}  tick {100.0 * list.Sum(row => row.Scored) / taken,5:F1}%  formula {100.0 * list.Sum(row => row.Formula) / taken,5:F1}%");
    }

    private static MatchSideV1 WithKeeper(MatchSideV1 side, int ability) =>
        side with
        {
            Squad = [.. side.Squad.Select(player => player.IsGoalkeeper ? player with { Attributes = PlayerAttributesV1.Uniform(ability) } : player)],
        };
}
