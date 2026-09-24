using FluentAssertions;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Domain.Tests.World;

/// <summary>
/// The capacity arithmetic behind onboarding and pyramid expansion (`WORLD-8`, `PYR-1`, `PYR-2`).
/// </summary>
public sealed class CountryCapacityTests
{
    private static readonly Guid CountryId = Guid.CreateVersion7();

    [Fact]
    public void A_fresh_tier_offers_every_club()
    {
        var capacity = Measure(occupied: 0);

        capacity.AvailableClubs.Should().Be(18);
        capacity.HasAvailableClubs.Should().BeTrue();
        capacity.LowestTierIsFull.Should().BeFalse();
        capacity.NeedsExpansion.Should().BeFalse();
    }

    [Fact]
    public void A_partly_filled_tier_offers_the_remainder()
    {
        var capacity = Measure(occupied: 11);

        capacity.AvailableClubs.Should().Be(7);
        capacity.HasAvailableClubs.Should().BeTrue();
        capacity.LowestTierIsFull.Should().BeFalse();
    }

    [Fact]
    public void A_full_tier_is_the_expansion_trigger_and_offers_nothing()
    {
        // PYR-2: all 18 held by humans, so tier N+1 is requested.
        var capacity = Measure(occupied: 18);

        capacity.AvailableClubs.Should().Be(0);
        capacity.HasAvailableClubs.Should().BeFalse();
        capacity.LowestTierIsFull.Should().BeTrue();
        capacity.NeedsExpansion.Should().BeTrue();
    }

    [Fact]
    public void The_expansion_target_is_the_next_tier()
    {
        Measure(occupied: 18, tier: 1).TargetTierForExpansion.Should().Be(2);
        Measure(occupied: 18, tier: 2).TargetTierForExpansion.Should().Be(3);
        Measure(occupied: 18, tier: 7).TargetTierForExpansion.Should().Be(8, "there is no maximum tier (PYR-11)");
    }

    [Fact]
    public void An_empty_division_is_not_treated_as_full()
    {
        // Guards the division-by-nothing case: a tier with no clubs must not trigger expansion.
        var capacity = CountryCapacity.Measure(CountryId, 1, Guid.CreateVersion7(), 0, 0);

        capacity.LowestTierIsFull.Should().BeFalse();
        capacity.HasAvailableClubs.Should().BeFalse();
    }

    [Fact]
    public void Measuring_a_tier_below_one_or_with_negative_counts_is_a_programming_error()
    {
        var tier = () => CountryCapacity.Measure(CountryId, 0, Guid.CreateVersion7(), 18, 0);
        var clubs = () => CountryCapacity.Measure(CountryId, 1, Guid.CreateVersion7(), -1, 0);
        var occupied = () => CountryCapacity.Measure(CountryId, 1, Guid.CreateVersion7(), 18, -1);

        tier.Should().Throw<ArgumentOutOfRangeException>();
        clubs.Should().Throw<ArgumentOutOfRangeException>();
        occupied.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static CountryCapacity Measure(int occupied, int tier = 1) =>
        CountryCapacity.Measure(CountryId, tier, Guid.CreateVersion7(), 18, occupied);
}
