using FluentAssertions;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The assists and the match rating a player line now carries (`engine-v2`).
/// </summary>
/// <remarks>
/// These are the two facts the season-statistics projection reads from a stored result, so the engine is
/// where their contract lives: an assist is attributed to exactly one teammate per non-penalty goal, and a
/// rating is bounded and absent for a player who never came on.
/// </remarks>
public sealed class PlayerLineTests
{
    /// <summary>A spread of seeds, so an invariant is not demonstrated by one lucky match.</summary>
    public static TheoryData<ulong> Seeds()
    {
        var data = new TheoryData<ulong>();

        for (var seed = 1UL; seed <= 40; seed++)
        {
            data.Add(seed * 4_211);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Assists_are_nonnegative_and_never_exceed_the_goals(ulong seed)
    {
        var result = MatchSimulator.Simulate(TestMatchFactory.Even(seed));

        foreach (var side in new[] { MatchSide.Home, MatchSide.Away })
        {
            var assists = result.PlayerLines
                .Where(line => line.Side == side)
                .Sum(line => line.Assists);

            assists.Should().BeGreaterThanOrEqualTo(0);
            assists.Should().BeLessThanOrEqualTo(result.GoalsOf(side), "a goal has at most one assister");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void A_player_who_did_not_appear_has_no_rating_and_everyone_else_is_in_range(ulong seed)
    {
        var rules = EngineRulesV2.Default;
        var result = MatchSimulator.Simulate(TestMatchFactory.Even(seed));

        foreach (var line in result.PlayerLines)
        {
            if (line.MinutesPlayed == 0)
            {
                line.RatingBasisPoints.Should().Be(0, "a match has no opinion about a player who did not play");
            }
            else
            {
                line.RatingBasisPoints.Should().BeInRange(
                    rules.RatingMinBasisPoints,
                    rules.RatingMaxBasisPoints);
            }
        }
    }

    [Fact]
    public void Every_goal_that_was_not_a_penalty_is_credited_with_an_assist()
    {
        var result = MatchSimulator.Simulate(TestMatchFactory.Even());

        foreach (var side in new[] { MatchSide.Home, MatchSide.Away })
        {
            var goals = result.GoalsOf(side);
            var penalties = result.StatsOf(side).PenaltiesScored;
            var assists = result.PlayerLines
                .Where(line => line.Side == side)
                .Sum(line => line.Assists);

            assists.Should().Be(goals - penalties, "a penalty has no assister and every other goal has one");
        }
    }

    [Fact]
    public void A_player_who_scored_is_rated_above_the_players_who_did_not_contribute()
    {
        // A rating that never moved with what a player did would be a column, not a statistic. The goal
        // bonus is the largest term, so over an even match it separates a scorer from a teammate who only
        // played — the property a season's average rating is built on.
        var result = MatchSimulator.Simulate(TestMatchFactory.Even());

        var scorers = result.PlayerLines.Where(line => line.Goals > 0).ToList();

        scorers.Should().NotBeEmpty();

        var ordinary = result.PlayerLines
            .Where(line =>
                line.MinutesPlayed > 0
                && line.Goals == 0
                && line.Side == scorers[0].Side)
            .ToList();

        ordinary.Should().NotBeEmpty();
        scorers[0].RatingBasisPoints.Should().BeGreaterThan(ordinary[0].RatingBasisPoints);
    }
}
