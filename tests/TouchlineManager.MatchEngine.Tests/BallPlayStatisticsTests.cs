using FluentAssertions;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The passes and take-ons a player line carries (`engine-v6`).
/// </summary>
/// <remarks>
/// Like the assists beside them, these are facts the simulation decided, so the engine is where their
/// contract lives: a completed count is a subset of the attempted one, a goalkeeper plays neither, an assist
/// is a completed pass, and the volumes are those of a match rather than of a tally gone wrong.
/// </remarks>
public sealed class BallPlayStatisticsTests
{
    /// <summary>A spread of seeds, so an invariant is not demonstrated by one lucky match.</summary>
    private static IEnumerable<ulong> SeedValues => Enumerable.Range(1, 40).Select(seed => (ulong)seed * 4_211UL);

    /// <summary>The same spread, as theory data.</summary>
    public static TheoryData<ulong> Seeds() => [.. SeedValues];

    [Theory]
    [MemberData(nameof(Seeds))]
    public void A_completed_count_is_a_nonnegative_subset_of_the_attempted_one(ulong seed)
    {
        var result = MatchSimulator.Simulate(TestMatchFactory.Even(seed));

        foreach (var line in result.PlayerLines)
        {
            line.PassesAttempted.Should().BeGreaterThanOrEqualTo(0);
            line.PassesCompleted.Should().BeInRange(0, line.PassesAttempted);
            line.DribblesAttempted.Should().BeGreaterThanOrEqualTo(0);
            line.DribblesCompleted.Should().BeInRange(0, line.DribblesAttempted);
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Only_a_player_who_took_the_pitch_has_passed_or_dribbled(ulong seed)
    {
        var result = MatchSimulator.Simulate(TestMatchFactory.Even(seed));

        foreach (var line in result.PlayerLines.Where(line => line.MinutesPlayed == 0))
        {
            line.PassesAttempted.Should().Be(0);
            line.DribblesAttempted.Should().Be(0);
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void An_assist_is_a_completed_pass(ulong seed)
    {
        var result = MatchSimulator.Simulate(TestMatchFactory.Even(seed));

        foreach (var line in result.PlayerLines)
        {
            line.PassesCompleted.Should().BeGreaterThanOrEqualTo(
                line.Assists,
                "the ball that sets a goal up is a pass that found its man");
        }
    }

    [Fact]
    public void The_same_snapshot_counts_the_same_passes_and_take_ons()
    {
        var first = MatchSimulator.Simulate(TestMatchFactory.Even(12_345));
        var second = MatchSimulator.Simulate(TestMatchFactory.Even(12_345));

        second.PlayerLines
            .Select(line => (line.ParticipantId, line.PassesAttempted, line.PassesCompleted, line.DribblesAttempted, line.DribblesCompleted))
            .Should().Equal(first.PlayerLines
                .Select(line => (line.ParticipantId, line.PassesAttempted, line.PassesCompleted, line.DribblesAttempted, line.DribblesCompleted)));
    }

    [Fact]
    public void A_sides_volumes_are_those_of_a_match()
    {
        var passes = 0;
        var completed = 0;
        var dribbles = 0;
        var won = 0;
        var sides = 0;

        foreach (var seed in SeedValues)
        {
            var result = MatchSimulator.Simulate(TestMatchFactory.Even(seed));

            foreach (var side in new[] { MatchSide.Home, MatchSide.Away })
            {
                var lines = result.PlayerLines.Where(line => line.Side == side).ToList();

                passes += lines.Sum(line => line.PassesAttempted);
                completed += lines.Sum(line => line.PassesCompleted);
                dribbles += lines.Sum(line => line.DribblesAttempted);
                won += lines.Sum(line => line.DribblesCompleted);
                sides++;
            }
        }

        (passes / sides).Should().BeInRange(200, 500, "a side makes a few hundred passes a match");
        ((double)completed / passes).Should().BeInRange(0.75, 0.92, "a pass completion rate reads like football's");
        (dribbles / sides).Should().BeGreaterThan(0);
        ((double)won / dribbles).Should().BeInRange(0.40, 0.60, "a take-on between evenly matched players is a contest");
    }

    [Fact]
    public void A_better_passer_completes_a_higher_share_of_his_passes()
    {
        var strong = 0;
        var strongCompleted = 0;
        var weak = 0;
        var weakCompleted = 0;

        foreach (var seed in SeedValues)
        {
            // Every other player in each eleven is a gifted passer and the rest are poor ones.
            var input = TestMatchFactory.Even(seed);
            input = input with
            {
                Home = input.Home with { Squad = [.. input.Home.Squad.Select(WithPassingByShirt)] },
                Away = input.Away with { Squad = [.. input.Away.Squad.Select(WithPassingByShirt)] },
            };

            var result = MatchSimulator.Simulate(input);
            var shirts = input.Home.Squad.Concat(input.Away.Squad).ToDictionary(p => p.ParticipantId, p => p.ShirtNumber);

            foreach (var line in result.PlayerLines.Where(line => line.PassesAttempted > 0))
            {
                if (shirts[line.ParticipantId] % 2 == 0)
                {
                    strong += line.PassesAttempted;
                    strongCompleted += line.PassesCompleted;
                }
                else
                {
                    weak += line.PassesAttempted;
                    weakCompleted += line.PassesCompleted;
                }
            }
        }

        strong.Should().BeGreaterThan(0);
        weak.Should().BeGreaterThan(0);
        ((double)strongCompleted / strong).Should().BeGreaterThan((double)weakCompleted / weak);
        strong.Should().BeGreaterThan(weak, "the better passer has the ball more");
    }

    private static MatchParticipantV1 WithPassingByShirt(MatchParticipantV1 participant)
    {
        var values = participant.Attributes.Values.ToArray();
        values[(int)MatchAttributeName.Passing] = participant.ShirtNumber % 2 == 0 ? 18 : 4;

        return participant with { Attributes = PlayerAttributesV1.From(values) };
    }
}
