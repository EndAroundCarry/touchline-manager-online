using FluentAssertions;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Domain.Tests.World;

/// <summary>
/// The capacity arithmetic behind onboarding and pyramid expansion (`WORLD-8`, `PYR-1`, `PYR-2`, `PYR-15`).
/// </summary>
public sealed class CountryCapacityTests
{
    private static readonly Guid CountryId = Guid.CreateVersion7();

    [Fact]
    public void A_fresh_pyramid_offers_every_club()
    {
        var capacity = Measure(clubs: 32, occupied: 0);

        capacity.AvailableClubs.Should().Be(32);
        capacity.HasAvailableClubs.Should().BeTrue();
        capacity.CountryIsFull.Should().BeFalse();
        capacity.NeedsExpansion.Should().BeFalse();
    }

    [Fact]
    public void A_partly_filled_pyramid_offers_the_remainder()
    {
        var capacity = Measure(clubs: 32, occupied: 11);

        capacity.AvailableClubs.Should().Be(21);
        capacity.HasAvailableClubs.Should().BeTrue();
        capacity.CountryIsFull.Should().BeFalse();
        capacity.NeedsExpansion.Should().BeFalse();
    }

    [Fact]
    public void A_full_country_offers_nothing_and_still_needs_a_league()
    {
        var capacity = Measure(clubs: 32, occupied: 32);

        capacity.AvailableClubs.Should().Be(0);
        capacity.HasAvailableClubs.Should().BeFalse();
        capacity.CountryIsFull.Should().BeTrue();
        capacity.NeedsExpansion.Should().BeTrue();
    }

    [Theory]
    [InlineData(32, 23, 3, 1)]
    [InlineData(48, 34, 4, 1)]
    [InlineData(64, 45, 5, 1)]
    [InlineData(80, 56, 6, 1)]
    [InlineData(96, 68, 7, 1)]
    [InlineData(112, 79, 8, 1)]
    public void Each_trigger_point_asks_for_the_next_tier_while_a_tier_holds_one_league(int clubs, int managers, int tier, int league)
    {
        var capacity = Measure(clubs, managers);

        capacity.NeedsExpansion.Should().BeTrue($"{clubs - managers} free of {clubs} is 30% or less");
        capacity.CountryIsFull.Should().BeFalse("the country keeps accepting managers while the league is built");
        capacity.TargetLeague.Tier.Should().Be(tier);
        capacity.TargetLeague.League.Should().Be(league);

        Measure(clubs, managers - 1).NeedsExpansion.Should().BeFalse("one manager fewer leaves more than 30% free");
    }

    [Fact]
    public void The_boundary_is_inclusive_so_nine_free_of_thirty_two_triggers_and_ten_does_not()
    {
        Measure(clubs: 32, occupied: 23).NeedsExpansion.Should().BeTrue();
        Measure(clubs: 32, occupied: 22).NeedsExpansion.Should().BeFalse();
    }

    [Fact]
    public void While_a_tier_holds_one_league_the_target_is_always_the_tier_below()
    {
        Measure(clubs: 32, occupied: 0).TargetLeague.Should().Be(new PyramidLeague(3, 1, 1), "2.2 is held back");
        Measure(clubs: 112, occupied: 0).TargetLeague.Should().Be(new PyramidLeague(8, 1, 1));
        CountryCapacity.Measure(CountryId, 4, Guid.CreateVersion7(), 240, 0, FullPyramid(upToTier: 4))
            .TargetLeague.Should().Be(new PyramidLeague(5, 1, 1), "tier 4 holds eight leagues, so tier 5 opens after 4.8");
    }

    [Fact]
    public void An_empty_pyramid_is_not_treated_as_full_or_in_need()
    {
        // Guards the division-by-nothing case: a country with no clubs must not trigger expansion.
        var capacity = CountryCapacity.Measure(CountryId, 1, Guid.CreateVersion7(), 0, 0, [(1, 1)]);

        capacity.CountryIsFull.Should().BeFalse();
        capacity.NeedsExpansion.Should().BeFalse();
        capacity.HasAvailableClubs.Should().BeFalse();
    }

    [Fact]
    public void Measuring_a_tier_below_one_or_with_negative_counts_is_a_programming_error()
    {
        var tier = () => CountryCapacity.Measure(CountryId, 0, Guid.CreateVersion7(), 32, 0, [(1, 1)]);
        var clubs = () => CountryCapacity.Measure(CountryId, 1, Guid.CreateVersion7(), -1, 0, [(1, 1)]);
        var occupied = () => CountryCapacity.Measure(CountryId, 1, Guid.CreateVersion7(), 32, -1, [(1, 1)]);
        var leagues = () => CountryCapacity.Measure(CountryId, 1, Guid.CreateVersion7(), 32, 0, null!);

        tier.Should().Throw<ArgumentOutOfRangeException>();
        clubs.Should().Throw<ArgumentOutOfRangeException>();
        occupied.Should().Throw<ArgumentOutOfRangeException>();
        leagues.Should().Throw<ArgumentNullException>();
    }

    /// <summary>Measures a country whose leagues are created in the pyramid's order until they hold <paramref name="clubs"/> clubs.</summary>
    private static CountryCapacity Measure(int clubs, int occupied)
    {
        var leagues = new List<(int Tier, int League)>();

        for (var count = 0; count < clubs / 16; count++)
        {
            var next = PyramidShape.NextLeague(leagues);

            leagues.Add((next.Tier, next.League));
        }

        return CountryCapacity.Measure(CountryId, leagues.Max(league => league.Tier), Guid.CreateVersion7(), clubs, occupied, leagues);
    }

    private static List<(int Tier, int League)> FullPyramid(int upToTier) =>
        [.. Enumerable.Range(1, upToTier).SelectMany(tier => Enumerable.Range(1, PyramidShape.LeaguesAllowed(tier)).Select(league => (tier, league)))];
}
