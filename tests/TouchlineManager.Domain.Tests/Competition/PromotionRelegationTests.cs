using FluentAssertions;
using TouchlineManager.Domain.Competition;

namespace TouchlineManager.Domain.Tests.Competition;

/// <summary>
/// The movement rules between adjacent active tiers (`PR-1`…`PR-10`).
/// </summary>
/// <remarks>
/// Every case is built from an 18-club tier, the real size, so "the bottom three" is a meaningful phrase
/// and a rule that quietly overlapped the promoted and relegated bands would fail rather than slip through a
/// three-club abstraction.
/// </remarks>
public sealed class PromotionRelegationTests
{
    [Fact]
    public void A_country_with_one_active_tier_moves_nobody()
    {
        // PR-2 and PR-9: there is no tier below to drop into and none above to climb to.
        var tier1 = Clubs("t1", 18);

        var movements = PromotionRelegation.Compute([Tier(1, tier1)]);

        movements.Should().HaveCount(18);
        movements.Should().OnlyContain(movement => movement.IsStationary);
        movements.Should().OnlyContain(movement => movement.FromTier == 1 && movement.ToTier == 1);
    }

    [Fact]
    public void Two_tiers_exchange_three_clubs_each_way()
    {
        // PR-1, PR-2, PR-9: three up from the lowest tier, three down from the top tier, and nobody
        // promoted out of the top or relegated out of the bottom.
        var tier1 = Clubs("t1", 18);
        var tier2 = Clubs("t2", 18);

        var movements = PromotionRelegation.Compute([Tier(1, tier1), Tier(2, tier2)]);

        movements.Should().HaveCount(36);

        foreach (var clubId in tier2.Take(3))
        {
            MovementOf(movements, clubId).Should().Be(
                new ClubMovement(clubId, 2, 1, IsPromoted: true, IsRelegated: false));
        }

        foreach (var clubId in tier1.TakeLast(3))
        {
            MovementOf(movements, clubId).Should().Be(
                new ClubMovement(clubId, 1, 2, IsPromoted: false, IsRelegated: true));
        }

        // The rest of the top tier stays up, and the rest of the lowest stays down: the lowest tier
        // relegates nobody (PR-2) and the top promotes nobody (PR-9).
        movements.Where(movement => movement.IsStationary).Should().HaveCount(30);
        movements.Should().NotContain(movement => movement.ClubId == tier1[0] && movement.IsRelegated);
        movements.Should().NotContain(movement => movement.ClubId == tier2[17] && movement.IsPromoted);
    }

    [Fact]
    public void A_middle_tier_both_promotes_and_relegates_three_clubs()
    {
        // Three active tiers: the middle tier is the case that exercises both directions at once.
        var tier1 = Clubs("t1", 18);
        var tier2 = Clubs("t2", 18);
        var tier3 = Clubs("t3", 18);

        var movements = PromotionRelegation.Compute([Tier(1, tier1), Tier(2, tier2), Tier(3, tier3)]);

        movements.Should().HaveCount(54);

        foreach (var clubId in tier2.Take(3))
        {
            MovementOf(movements, clubId).Should().Be(
                new ClubMovement(clubId, 2, 1, IsPromoted: true, IsRelegated: false));
        }

        foreach (var clubId in tier2.TakeLast(3))
        {
            MovementOf(movements, clubId).Should().Be(
                new ClubMovement(clubId, 2, 3, IsPromoted: false, IsRelegated: true));
        }

        foreach (var clubId in tier3.Take(3))
        {
            MovementOf(movements, clubId).Should().Be(
                new ClubMovement(clubId, 3, 2, IsPromoted: true, IsRelegated: false));
        }

        foreach (var clubId in tier1.TakeLast(3))
        {
            MovementOf(movements, clubId).Should().Be(
                new ClubMovement(clubId, 1, 2, IsPromoted: false, IsRelegated: true));
        }

        movements.Should().OnlyContain(movement => !(movement.IsPromoted && movement.IsRelegated));
    }

    [Fact]
    public void The_movement_does_not_depend_on_the_order_the_tiers_arrive_in()
    {
        var tier1 = Clubs("t1", 18);
        var tier2 = Clubs("t2", 18);

        var ascending = PromotionRelegation.Compute([Tier(1, tier1), Tier(2, tier2)]);
        var shuffled = PromotionRelegation.Compute([Tier(2, tier2), Tier(1, tier1)]);

        shuffled.Should().BeEquivalentTo(ascending);
    }

    [Fact]
    public void A_club_that_appears_in_two_tiers_is_a_programming_error()
    {
        // A club has exactly one placement per season (PR-3); two placements would move it twice.
        var shared = Guid.CreateVersion7();
        var tier1 = new List<Guid> { shared }.Concat(Clubs("t1", 17)).ToList();
        var tier2 = new List<Guid> { shared }.Concat(Clubs("t2", 17)).ToList();

        var act = () => PromotionRelegation.Compute([Tier(1, tier1), Tier(2, tier2)]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_tier_too_small_to_exchange_three_is_a_programming_error()
    {
        var tier1 = Clubs("t1", 18);
        var tier2 = Clubs("t2", 2);

        var act = () => PromotionRelegation.Compute([Tier(1, tier1), Tier(2, tier2)]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void No_tiers_moves_nobody()
    {
        PromotionRelegation.Compute([]).Should().BeEmpty();
    }

    [Fact]
    public void The_version_names_the_rule()
    {
        PromotionRelegation.Version.Should().Be("promotion-relegation-v1");
    }

    private static TierStandings Tier(int number, IReadOnlyList<Guid> clubs) => new(number, clubs);

    private static ClubMovement MovementOf(IReadOnlyList<ClubMovement> movements, Guid clubId) =>
        movements.Single(movement => movement.ClubId == clubId);

    private static List<Guid> Clubs(string prefix, int count)
    {
        var clubs = new List<Guid>(count);

        for (var index = 0; index < count; index++)
        {
            // Deterministic, distinct ids so a test failure names a club by its tier and position.
            var bytes = new byte[16];
            bytes[0] = (byte)prefix[^1];
            bytes[1] = (byte)index;
            clubs.Add(new Guid(bytes));
        }

        return clubs;
    }
}
