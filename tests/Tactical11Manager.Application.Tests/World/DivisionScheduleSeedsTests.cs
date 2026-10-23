using FluentAssertions;
using Tactical11Manager.Application.World.Generation;

namespace Tactical11Manager.Application.Tests.World;

/// <summary>
/// A league's schedule and tie-break seeds are its own (`CAL-8`, `TBL-11`, `PYR-15`).
/// </summary>
public sealed class DivisionScheduleSeedsTests
{
    private const string Seed = "world-seed";

    [Fact]
    public void The_first_league_of_a_tier_keeps_the_seeds_it_had_before_a_tier_could_hold_several()
    {
        var before = DivisionScheduleGenerator.SeedsFor(Seed, "ENG", seasonNumber: 3, tier: 2);
        var explicitFirst = DivisionScheduleGenerator.SeedsFor(Seed, "ENG", seasonNumber: 3, tier: 2, league: 1);

        explicitFirst.Should().Be(before);
    }

    [Fact]
    public void Two_leagues_of_one_tier_do_not_share_a_fixture_list_or_a_tie_break_draw()
    {
        var first = DivisionScheduleGenerator.SeedsFor(Seed, "ENG", seasonNumber: 3, tier: 2, league: 1);
        var second = DivisionScheduleGenerator.SeedsFor(Seed, "ENG", seasonNumber: 3, tier: 2, league: 2);

        second.ScheduleSeed.Should().NotBe(first.ScheduleSeed);
        second.TieDrawSeed.Should().NotBe(first.TieDrawSeed);
        second.TieDrawHash.Should().NotBe(first.TieDrawHash);
    }

    [Fact]
    public void League_two_of_tier_three_does_not_collide_with_league_three_of_tier_two()
    {
        // "2.3" and "23" would read the same if the tier and league were joined without a separator.
        var tierTwo = DivisionScheduleGenerator.SeedsFor(Seed, "ENG", seasonNumber: 1, tier: 2, league: 3);
        var tierTwentyThree = DivisionScheduleGenerator.SeedsFor(Seed, "ENG", seasonNumber: 1, tier: 23);

        tierTwo.ScheduleSeed.Should().NotBe(tierTwentyThree.ScheduleSeed);
    }
}
