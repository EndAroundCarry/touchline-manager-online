using FluentAssertions;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Domain.Tests.World;

/// <summary>
/// The doubling pyramid: how many leagues a tier holds, which league is created next and which league each one feeds (`PYR-15`).
/// </summary>
public sealed class PyramidShapeTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(10, 512)]
    public void Each_tier_holds_twice_the_leagues_of_the_tier_above(int tier, int leagues)
    {
        PyramidShape.LeaguesAllowed(tier).Should().Be(leagues);
    }

    [Fact]
    public void A_tier_below_one_or_beyond_the_arithmetic_is_a_programming_error()
    {
        var below = () => PyramidShape.LeaguesAllowed(0);
        var beyond = () => PyramidShape.LeaguesAllowed(PyramidShape.MaximumTier + 1);

        below.Should().Throw<ArgumentOutOfRangeException>();
        beyond.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Leagues_are_created_in_the_order_two_one_two_two_three_one_to_three_four_then_four_one()
    {
        var existing = new List<(int Tier, int League)> { (1, 1) };
        var created = new List<string>();

        for (var step = 0; step < 6; step++)
        {
            var next = PyramidShape.NextLeague(existing);

            created.Add($"{next.Tier}.{next.League}");
            existing.Add((next.Tier, next.League));
        }

        created.Should().Equal("2.1", "2.2", "3.1", "3.2", "3.3", "3.4");
        PyramidShape.NextLeague(existing).Should().Be(new PyramidLeague(4, 1, 1));
    }

    [Fact]
    public void The_next_league_of_a_country_with_nothing_is_the_top_division()
    {
        PyramidShape.NextLeague([]).Should().Be(new PyramidLeague(1, 1, null));
    }

    [Fact]
    public void A_league_that_is_still_provisioning_already_holds_its_slot()
    {
        // The list carries divisions of every status, so 2.2 is taken the moment it is requested.
        PyramidShape.NextLeague([(1, 1), (2, 1), (2, 2)]).Should().Be(new PyramidLeague(3, 1, 1));
    }

    [Fact]
    public void The_next_league_never_repeats_a_league_number_even_when_one_is_missing()
    {
        PyramidShape.NextLeague([(1, 1), (2, 1), (3, 1), (3, 3)]).Should().Be(new PyramidLeague(3, 4, 2));
    }

    [Theory]
    [InlineData(2, 1, 1)]
    [InlineData(2, 2, 1)]
    [InlineData(3, 1, 1)]
    [InlineData(3, 2, 2)]
    [InlineData(3, 3, 1)]
    [InlineData(3, 4, 2)]
    [InlineData(4, 1, 1)]
    [InlineData(4, 2, 2)]
    [InlineData(4, 3, 3)]
    [InlineData(4, 4, 4)]
    [InlineData(4, 5, 1)]
    [InlineData(4, 8, 4)]
    public void A_league_feeds_the_league_of_the_tier_above_that_shares_its_remainder(int tier, int league, int parent)
    {
        PyramidShape.ParentLeagueOf(tier, league).Should().Be(parent);
    }

    [Fact]
    public void Every_league_of_a_tier_gets_a_first_feeder_before_any_gets_a_second()
    {
        foreach (var tier in Enumerable.Range(2, 8))
        {
            var parents = Enumerable.Range(1, PyramidShape.LeaguesAllowed(tier))
                .Select(league => PyramidShape.ParentLeagueOf(tier, league))
                .ToList();
            var parentLeagues = PyramidShape.LeaguesAllowed(tier - 1);

            parents.Take(parentLeagues).Should().BeEquivalentTo(Enumerable.Range(1, parentLeagues), $"tier {tier}'s first leagues");
            parents.GroupBy(parent => parent).Should().OnlyContain(group => group.Count() == 2, $"tier {tier}: two feeders each");
        }
    }

    [Fact]
    public void The_parent_in_a_created_league_is_the_one_the_parent_rule_gives()
    {
        PyramidShape.NextLeague([(1, 1), (2, 1), (2, 2), (3, 1)]).ParentLeague.Should().Be(PyramidShape.ParentLeagueOf(3, 2));
    }

    [Fact]
    public void The_top_division_has_no_parent_and_a_league_outside_its_tier_is_a_programming_error()
    {
        var top = () => PyramidShape.ParentLeagueOf(1, 1);
        var zero = () => PyramidShape.ParentLeagueOf(3, 0);
        var past = () => PyramidShape.ParentLeagueOf(3, 5);

        top.Should().Throw<ArgumentOutOfRangeException>();
        zero.Should().Throw<ArgumentOutOfRangeException>();
        past.Should().Throw<ArgumentOutOfRangeException>();
    }
}
