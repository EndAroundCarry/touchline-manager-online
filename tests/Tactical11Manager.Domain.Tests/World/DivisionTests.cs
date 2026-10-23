using FluentAssertions;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Domain.Tests.World;

/// <summary>
/// Division identity and the activation gate (`WORLD-3`, `WORLD-4`, `PYR-8`).
/// </summary>
public sealed class DivisionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_tier_name_is_generic_and_brand_free()
    {
        // No real competition name or mark may appear in generated content (WORLD-3).
        Division.NameFor("England", 1, 1).Should().Be("England Top Division");
        Division.NameFor("Spain", 2, 1).Should().Be("Spain Division 2.1", "a lone league of a tier still carries its number");
        Division.NameFor("Spain", 2, 2).Should().Be("Spain Division 2.2");
        Division.NameFor("Romania", 9, 17).Should().Be("Romania Division 9.17");
    }

    [Fact]
    public void A_division_remembers_its_league_number_and_the_league_it_feeds()
    {
        var parentId = Guid.CreateVersion7();

        var division = Division.Provision(
            Guid.CreateVersion7(), Guid.CreateVersion7(), 3, 4, parentId, "England", Guid.CreateVersion7(), Now);

        division.LeagueNumber.Should().Be(4);
        division.ParentDivisionId.Should().Be(parentId);
        division.DisplayName.Should().Be("England Division 3.4");
    }

    [Fact]
    public void The_top_division_has_no_parent_and_a_tier_cannot_hold_more_leagues_than_the_pyramid_allows()
    {
        var topWithParent = () => Division.Provision(
            Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1, Guid.CreateVersion7(), "England", Guid.CreateVersion7(), Now);
        var secondTopDivision = () => Division.Provision(
            Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 2, null, "England", Guid.CreateVersion7(), Now);
        var fifthLeagueOfTierThree = () => Division.Provision(
            Guid.CreateVersion7(), Guid.CreateVersion7(), 3, 5, Guid.CreateVersion7(), "England", Guid.CreateVersion7(), Now);
        var noLeagueZero = () => Division.Provision(
            Guid.CreateVersion7(), Guid.CreateVersion7(), 2, 0, Guid.CreateVersion7(), "England", Guid.CreateVersion7(), Now);

        topWithParent.Should().Throw<ArgumentException>();
        secondTopDivision.Should().Throw<ArgumentOutOfRangeException>();
        fifthLeagueOfTierThree.Should().Throw<ArgumentOutOfRangeException>();
        noLeagueZero.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_new_division_starts_provisioning_and_is_not_claimable()
    {
        // PYR-8: a tier becomes claimable only after generation, validation, and backfill complete.
        var division = Provision(tier: 2);

        division.Status.Should().Be(DivisionStatus.Provisioning);
        division.IsClaimable.Should().BeFalse();
        division.Capacity.Should().Be(WorldRuleSet.ClubsPerDivision);
        division.TierNumber.Should().Be(2);
    }

    [Fact]
    public void Activating_a_division_makes_its_clubs_claimable()
    {
        var division = Provision(tier: 2);

        division.Activate(Now.AddHours(1));

        division.Status.Should().Be(DivisionStatus.Active);
        division.IsClaimable.Should().BeTrue();
    }

    [Fact]
    public void A_tier_below_one_is_a_programming_error()
    {
        var act = () => Division.Provision(Guid.CreateVersion7(), Guid.CreateVersion7(), 0, 1, null, "England", Guid.CreateVersion7(), Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_retired_division_cannot_be_activated()
    {
        var division = Provision(tier: 1);
        division.Retire(Now);

        var act = () => division.Activate(Now.AddHours(1));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Every_status_round_trips_through_its_code()
    {
        DivisionStatus.Provisioning.ToCode().Should().Be("provisioning");
        DivisionStatusRules.FromCode("active").Should().Be(DivisionStatus.Active);
        DivisionStatusRules.IsClaimable(DivisionStatus.Retired).Should().BeFalse();

        var act = () => DivisionStatusRules.FromCode("suspended");
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static Division Provision(int tier) =>
        Division.Provision(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            tier,
            1,
            null,
            "England",
            Guid.CreateVersion7(),
            Now);
}
