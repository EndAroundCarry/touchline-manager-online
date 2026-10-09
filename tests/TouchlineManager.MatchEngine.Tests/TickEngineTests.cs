using System.Reflection;
using FluentAssertions;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Serialization;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The tick engine as the active engine (`tick-engine-v1`, Milestone 9): the snapshot reaches it by its version, the same snapshot always
/// plays the same match, and over many matches it lands in the bands the plan sets.
/// </summary>
public sealed class TickEngineTests
{
    private static readonly (MatchPositionFamily Family, MatchRole Role, int X, int Y)[] FourFourTwo =
    [
        (MatchPositionFamily.Goalkeeper, MatchRole.Goalkeeper, 500, 5_000),
        (MatchPositionFamily.Defence, MatchRole.FullBack, 2_000, 8_000),
        (MatchPositionFamily.Defence, MatchRole.CentreBack, 1_800, 6_000),
        (MatchPositionFamily.Defence, MatchRole.CentreBack, 1_800, 4_000),
        (MatchPositionFamily.Defence, MatchRole.FullBack, 2_000, 2_000),
        (MatchPositionFamily.Attack, MatchRole.Winger, 5_800, 8_300),
        (MatchPositionFamily.Midfield, MatchRole.CentralMidfielder, 5_200, 6_200),
        (MatchPositionFamily.Midfield, MatchRole.CentralMidfielder, 5_200, 3_800),
        (MatchPositionFamily.Attack, MatchRole.Winger, 5_800, 1_700),
        (MatchPositionFamily.Attack, MatchRole.Striker, 8_200, 6_200),
        (MatchPositionFamily.Attack, MatchRole.Striker, 8_200, 3_800),
    ];

    private static readonly (MatchPositionFamily Family, MatchRole Role, int X, int Y)[] FourThreeThree =
    [
        (MatchPositionFamily.Goalkeeper, MatchRole.Goalkeeper, 500, 5_000),
        (MatchPositionFamily.Defence, MatchRole.FullBack, 2_000, 8_000),
        (MatchPositionFamily.Defence, MatchRole.CentreBack, 1_800, 6_000),
        (MatchPositionFamily.Defence, MatchRole.CentreBack, 1_800, 4_000),
        (MatchPositionFamily.Defence, MatchRole.FullBack, 2_000, 2_000),
        (MatchPositionFamily.Midfield, MatchRole.DefensiveMidfielder, 4_200, 5_000),
        (MatchPositionFamily.Midfield, MatchRole.CentralMidfielder, 5_600, 6_800),
        (MatchPositionFamily.Midfield, MatchRole.CentralMidfielder, 5_600, 3_200),
        (MatchPositionFamily.Attack, MatchRole.Winger, 8_200, 8_200),
        (MatchPositionFamily.Attack, MatchRole.Striker, 8_600, 5_000),
        (MatchPositionFamily.Attack, MatchRole.Winger, 8_200, 1_800),
    ];

    private static readonly (MatchPositionFamily Family, MatchRole Role, int X, int Y)[] FourTwoThreeOne =
    [
        (MatchPositionFamily.Goalkeeper, MatchRole.Goalkeeper, 500, 5_000),
        (MatchPositionFamily.Defence, MatchRole.FullBack, 2_000, 8_000),
        (MatchPositionFamily.Defence, MatchRole.CentreBack, 1_800, 6_000),
        (MatchPositionFamily.Defence, MatchRole.CentreBack, 1_800, 4_000),
        (MatchPositionFamily.Defence, MatchRole.FullBack, 2_000, 2_000),
        (MatchPositionFamily.Midfield, MatchRole.DefensiveMidfielder, 4_000, 6_200),
        (MatchPositionFamily.Midfield, MatchRole.DefensiveMidfielder, 4_000, 3_800),
        (MatchPositionFamily.Midfield, MatchRole.AttackingMidfielder, 6_400, 8_200),
        (MatchPositionFamily.Midfield, MatchRole.AttackingMidfielder, 6_400, 5_000),
        (MatchPositionFamily.Midfield, MatchRole.AttackingMidfielder, 6_400, 1_800),
        (MatchPositionFamily.Attack, MatchRole.Striker, 8_600, 5_000),
    ];

    private static readonly (MatchPositionFamily Family, MatchRole Role, int X, int Y)[] FiveThreeTwo =
    [
        (MatchPositionFamily.Goalkeeper, MatchRole.Goalkeeper, 500, 5_000),
        (MatchPositionFamily.Defence, MatchRole.WingBack, 2_600, 9_000),
        (MatchPositionFamily.Defence, MatchRole.CentreBack, 1_800, 6_600),
        (MatchPositionFamily.Defence, MatchRole.CentreBack, 1_700, 5_000),
        (MatchPositionFamily.Defence, MatchRole.CentreBack, 1_800, 3_400),
        (MatchPositionFamily.Defence, MatchRole.WingBack, 2_600, 1_000),
        (MatchPositionFamily.Midfield, MatchRole.CentralMidfielder, 5_400, 6_600),
        (MatchPositionFamily.Midfield, MatchRole.CentralMidfielder, 5_200, 5_000),
        (MatchPositionFamily.Midfield, MatchRole.CentralMidfielder, 5_400, 3_400),
        (MatchPositionFamily.Attack, MatchRole.Striker, 8_300, 6_200),
        (MatchPositionFamily.Attack, MatchRole.Striker, 8_300, 3_800),
    ];

    private static readonly (MatchPositionFamily Family, MatchRole Role, int X, int Y)[][] Shapes = [FourFourTwo, FourThreeThree, FourTwoThreeOne, FiveThreeTwo];

    /// <summary>A snapshot frozen against the tick engine, on the tactics board's shape.</summary>
    private static MatchInputV1 Current(ulong seed, int homeShape = 0, int awayShape = 0) =>
        Restand(TestMatchFactory.Even(seed), Shapes[homeShape], Shapes[awayShape]) with
        {
            EngineVersion = EngineVersions.EngineLabel,
            RuleSetVersion = EngineVersions.RuleSetLabel,
            FormulaConfigurationHash = EngineConfiguration.HashOf(EngineRulesV2.Default, EngineVersions.RuleSetLabel),
        };

    private static MatchInputV1 Restand(MatchInputV1 input, (MatchPositionFamily Family, MatchRole Role, int X, int Y)[] home, (MatchPositionFamily Family, MatchRole Role, int X, int Y)[] away) =>
        input with { Home = Restand(input.Home, home), Away = Restand(input.Away, away) };

    private static MatchSideV1 Restand(MatchSideV1 side, (MatchPositionFamily Family, MatchRole Role, int X, int Y)[] board) =>
        side with
        {
            Slots =
            [
                .. side.Slots.Select(slot =>
                {
                    var place = board[slot.SlotNumber - 1];

                    return slot with { Family = place.Family, Role = place.Role, X = place.X, Y = place.Y };
                }),
            ],
        };

    [Fact]
    public void A_snapshot_frozen_against_the_current_engine_is_played_by_the_tick_loop_and_a_legacy_one_by_the_possession_engine()
    {
        var current = new MatchPassageRecorder();
        var legacy = new MatchPassageRecorder();

        MatchSimulator.Simulate(Current(42), EngineRulesV2.Default, passages: current);
        MatchSimulator.Simulate(TestMatchFactory.Even(42), EngineRulesV2.Default, passages: legacy);

        current.Tick.Should().NotBeNull("the tick loop records every tick it plays");
        current.Tick!.FrameCount.Should().BeGreaterThan(54_000, "ninety minutes at ten ticks a second, and the stoppages");
        legacy.Tick.Should().BeNull("the possession engine plays in possessions");
        legacy.Passages.Should().NotBeEmpty();
    }

    [Fact]
    public void The_same_snapshot_plays_the_same_match_every_time()
    {
        var input = Current(20_260_925);
        var first = MatchSimulator.Simulate(input);
        var second = MatchSimulator.Simulate(input);

        second.OutputHash.Should().Be(first.OutputHash);
        CanonicalMatchSerializer.CanonicalText(second).Should().Be(CanonicalMatchSerializer.CanonicalText(first), "character for character");
    }

    [Fact]
    public void Matches_played_at_the_same_time_on_different_threads_do_not_disturb_each_other()
    {
        var seeds = Enumerable.Range(1, 8).Select(index => (ulong)index).ToArray();
        var alone = seeds.Select(seed => MatchSimulator.Simulate(Current(seed)).OutputHash).ToArray();
        var together = new string[seeds.Length];

        Parallel.For(0, seeds.Length, index => together[index] = MatchSimulator.Simulate(Current(seeds[index])).OutputHash);

        together.Should().Equal(alone);
    }

    [Fact]
    public void A_different_seed_plays_a_different_match()
    {
        MatchSimulator.Simulate(Current(1)).OutputHash.Should().NotBe(MatchSimulator.Simulate(Current(2)).OutputHash);
    }

    [Fact]
    public void Recording_the_match_for_a_film_does_not_change_it()
    {
        var input = Current(7);
        var plain = MatchSimulator.Simulate(input);
        var filmed = MatchSimulator.Simulate(input, EngineRulesV2.Default, new PlayerLiveMetricsRecorder(), new MatchPassageRecorder());

        filmed.OutputHash.Should().Be(plain.OutputHash);
    }

    [Fact]
    public void The_tick_engine_has_a_golden_output_hash_so_that_a_change_to_it_is_a_new_version()
    {
        var result = MatchSimulator.Simulate(Current(20_260_925));

        result.InputHash.Should().Be(GoldenInputHash);
        result.OutputHash.Should().Be(GoldenOutputHash, "a change to the tick engine's behaviour is an engine-version change (ADR-0004)");
    }

    [Fact]
    public void The_simulation_does_its_sums_in_integers()
    {
        var floating = new[] { typeof(float), typeof(double), typeof(decimal) };
        var simulation = typeof(TickMatchLoop).Assembly.GetTypes()
            .Where(type => type.Namespace is "TouchlineManager.MatchEngine.Tick" or "TouchlineManager.MatchEngine.Spatial"
                && type.Name.StartsWith("Tick", StringComparison.Ordinal)
                && type.Name is not ("TickReplaySynthesizer" or "TickFilmSelector" or "TickMatchRecording"))
            .ToList();
        var offences = new List<string>();
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        simulation.Should().NotBeEmpty();

        foreach (var type in simulation)
        {
            offences.AddRange(type.GetFields(All).Where(field => floating.Contains(field.FieldType)).Select(field => $"{type.Name}.{field.Name}"));

            foreach (var method in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))
            {
                if (method is MethodInfo info && floating.Contains(info.ReturnType))
                {
                    offences.Add($"{type.Name}.{method.Name} returns {info.ReturnType.Name}");
                }

                offences.AddRange(method.GetParameters().Where(parameter => floating.Contains(parameter.ParameterType)).Select(parameter => $"{type.Name}.{method.Name}({parameter.Name})"));
                offences.AddRange(
                    (method.GetMethodBody()?.LocalVariables ?? [])
                    .Where(local => floating.Contains(local.LocalType))
                    .Select(local => $"{type.Name}.{method.Name} local {local.LocalType.Name}"));
            }
        }

        offences.Should().BeEmpty("a float can come out differently on another processor, and a match must not (ADR-0004)");
    }

    [Fact]
    public void Over_many_matches_between_the_board_formations_the_tick_engine_lands_in_the_plans_bands()
    {
        const int Matches = 72;
        var lines = new (int Goals, int Shots, int OnTarget, int Passes, int Completed, int Yellows, int HomeWin, int Draw)[Matches];

        Parallel.For(0, Matches, index =>
        {
            var input = Current(1_000UL + (ulong)index, index % Shapes.Length, (index / Shapes.Length) % Shapes.Length);
            var result = MatchSimulator.Simulate(input);

            lines[index] =
            (
                result.HomeGoals + result.AwayGoals,
                result.Home.Shots + result.Away.Shots,
                result.Home.ShotsOnTarget + result.Away.ShotsOnTarget,
                result.PlayerLines.Sum(line => line.PassesAttempted),
                result.PlayerLines.Sum(line => line.PassesCompleted),
                result.Home.YellowCards + result.Away.YellowCards,
                result.HomeGoals > result.AwayGoals ? 1 : 0,
                result.HomeGoals == result.AwayGoals ? 1 : 0
            );
        });

        var goals = lines.Average(line => line.Goals);
        var shots = lines.Average(line => line.Shots);
        var onTarget = lines.Sum(line => line.OnTarget) / (double)lines.Sum(line => line.Shots);
        var completion = lines.Sum(line => line.Completed) / (double)lines.Sum(line => line.Passes);
        var yellows = lines.Average(line => line.Yellows);

        // The plan's bands are for the mean of thousands of matches; a few dozen are scattered about it, so the test's bands are wider
        // and only catch a calibration that has come loose. The benchmark tool (`tickmatrix`, `ticksnap`) holds the plan's own.
        goals.Should().BeInRange(1.6, 3.8);
        shots.Should().BeInRange(18, 36);
        onTarget.Should().BeInRange(0.24, 0.48);
        completion.Should().BeInRange(0.70, 0.90);
        yellows.Should().BeInRange(2.0, 5.5);
        lines.Count(line => line.HomeWin == 1 || line.Draw == 1).Should().BeLessThan(Matches, "the away side wins some");
    }

    private const string GoldenInputHash = "5f74ae0160d042255a831b10ead014aa172a6e7cc951b2c0a0d1737dc906bda2";

    private const string GoldenOutputHash = "4bec3547959e0cf8acd3f322ae44e30630b2ce17f849ae44106b5b96357f3c15";
}
