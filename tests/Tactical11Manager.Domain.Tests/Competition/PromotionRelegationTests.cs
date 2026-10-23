using FluentAssertions;
using Tactical11Manager.Domain.Competition;

namespace Tactical11Manager.Domain.Tests.Competition;

/// <summary>
/// The movement rules between a league and the league it feeds (`PR-1`…`PR-10`).
/// </summary>
/// <remarks>
/// Every case is built from a 16-club league, the real size, so "the bottom two" and "the 14th" are meaningful
/// phrases and a rule that quietly overlapped the bands would fail rather than slip through a small abstraction.
/// </remarks>
public sealed class PromotionRelegationTests
{
    [Fact]
    public void A_country_with_one_league_moves_nobody()
    {
        // PR-2 and PR-9: there is no league below to drop into and none above to climb to.
        var top = League(1, 16);

        var movements = PromotionRelegation.Compute([top.Standings], []);

        movements.Should().HaveCount(16);
        movements.Should().OnlyContain(movement => movement.IsStationary);
        movements.Should().OnlyContain(movement => movement.FromTier == 1 && movement.ToTier == 1);
        movements.Should().OnlyContain(movement => movement.FromDivisionId == movement.ToDivisionId);
    }

    [Fact]
    public void When_the_challenger_wins_the_final_three_clubs_move_each_way()
    {
        var top = League(1, 16);
        var second = League(2, 16, parent: top);
        var challenger = second.Clubs[2];
        var holder = top.Clubs[13];

        var movements = PromotionRelegation.Compute(
            [top.Standings, second.Standings],
            Bracket(top, second, semiWinner: challenger, semiLoser: second.Clubs[3], finalWinner: challenger, finalLoser: holder));

        movements.Should().HaveCount(32);
        movements.Count(movement => movement.IsPromoted).Should().Be(3);
        movements.Count(movement => movement.IsRelegated).Should().Be(3);

        foreach (var clubId in second.Clubs.Take(2))
        {
            MovementOf(movements, clubId).Should().Be(
                new ClubMovement(clubId, 2, 1, true, false, second.DivisionId, top.DivisionId, MovementRoute.Direct));
        }

        MovementOf(movements, challenger).Should().Be(
            new ClubMovement(challenger, 2, 1, true, false, second.DivisionId, top.DivisionId, MovementRoute.Playoff));

        foreach (var clubId in top.Clubs.TakeLast(2))
        {
            MovementOf(movements, clubId).Should().Be(
                new ClubMovement(clubId, 1, 2, false, true, top.DivisionId, second.DivisionId, MovementRoute.Direct));
        }

        MovementOf(movements, holder).Should().Be(
            new ClubMovement(holder, 1, 2, false, true, top.DivisionId, second.DivisionId, MovementRoute.Playoff));

        movements.Where(movement => movement.IsStationary).Should().HaveCount(26);
        PerLeague(movements, top).Should().Be(16);
        PerLeague(movements, second).Should().Be(16);
    }

    [Fact]
    public void When_the_holder_wins_the_final_two_clubs_move_each_way()
    {
        var top = League(1, 16);
        var second = League(2, 16, parent: top);
        var holder = top.Clubs[13];

        var movements = PromotionRelegation.Compute(
            [top.Standings, second.Standings],
            Bracket(top, second, semiWinner: second.Clubs[3], semiLoser: second.Clubs[2], finalWinner: holder, finalLoser: second.Clubs[3]));

        movements.Count(movement => movement.IsPromoted).Should().Be(2);
        movements.Count(movement => movement.IsRelegated).Should().Be(2);
        MovementOf(movements, holder).IsStationary.Should().BeTrue();
        MovementOf(movements, second.Clubs[3]).IsStationary.Should().BeTrue();
        PerLeague(movements, top).Should().Be(16);
        PerLeague(movements, second).Should().Be(16);
    }

    [Fact]
    public void A_pair_with_no_play_off_moves_the_direct_places_only()
    {
        // A league activated too late for its play-off exchanges its 1st and 2nd against the 15th and 16th.
        var top = League(1, 16);
        var second = League(2, 16, parent: top);

        var movements = PromotionRelegation.Compute([top.Standings, second.Standings], []);

        movements.Count(movement => movement.IsPromoted).Should().Be(2);
        movements.Count(movement => movement.IsRelegated).Should().Be(2);
        movements.Should().OnlyContain(movement => movement.Route == MovementRoute.Direct);
        PerLeague(movements, top).Should().Be(16);
        PerLeague(movements, second).Should().Be(16);
    }

    [Fact]
    public void A_middle_league_both_promotes_and_relegates()
    {
        var top = League(1, 16);
        var second = League(2, 16, parent: top);
        var third = League(3, 16, parent: second);

        var movements = PromotionRelegation.Compute(
            [top.Standings, second.Standings, third.Standings],
            [
                .. Bracket(top, second, second.Clubs[3], second.Clubs[2], second.Clubs[3], top.Clubs[13]),
                .. Bracket(second, third, third.Clubs[2], third.Clubs[3], second.Clubs[13], third.Clubs[2]),
            ]);

        movements.Should().HaveCount(48);

        MovementOf(movements, second.Clubs[0]).ToTier.Should().Be(1);
        MovementOf(movements, second.Clubs[1]).ToTier.Should().Be(1);
        MovementOf(movements, second.Clubs[3]).Should().Match<ClubMovement>(movement => movement.IsPromoted && movement.Route == MovementRoute.Playoff);
        MovementOf(movements, second.Clubs[14]).ToTier.Should().Be(3);
        MovementOf(movements, second.Clubs[15]).ToTier.Should().Be(3);
        MovementOf(movements, second.Clubs[13]).IsStationary.Should().BeTrue("the holder won its final");
        MovementOf(movements, third.Clubs[0]).ToTier.Should().Be(2);
        MovementOf(movements, third.Clubs[1]).ToTier.Should().Be(2);
        MovementOf(movements, third.Clubs[2]).IsStationary.Should().BeTrue("the challenger lost its final");
        movements.Should().OnlyContain(movement => !(movement.IsPromoted && movement.IsRelegated));

        PerLeague(movements, top).Should().Be(16);
        PerLeague(movements, second).Should().Be(16);
        PerLeague(movements, third).Should().Be(16);
    }

    [Fact]
    public void The_movement_does_not_depend_on_the_order_the_leagues_arrive_in()
    {
        var top = League(1, 16);
        var second = League(2, 16, parent: top);

        var ascending = PromotionRelegation.Compute([top.Standings, second.Standings], []);
        var shuffled = PromotionRelegation.Compute([second.Standings, top.Standings], []);

        shuffled.Should().BeEquivalentTo(ascending);
    }

    [Fact]
    public void Every_club_has_exactly_one_placement()
    {
        var top = League(1, 16);
        var second = League(2, 16, parent: top);
        var challenger = second.Clubs[2];

        var movements = PromotionRelegation.Compute(
            [top.Standings, second.Standings],
            Bracket(top, second, challenger, second.Clubs[3], challenger, top.Clubs[13]));

        movements.Select(movement => movement.ClubId).Should().OnlyHaveUniqueItems();
        movements.Select(movement => movement.ClubId).Should().BeEquivalentTo([.. top.Clubs, .. second.Clubs]);
    }

    [Fact]
    public void A_club_that_appears_in_two_leagues_is_a_programming_error()
    {
        // A club has exactly one placement per season (PR-3); two placements would move it twice.
        var top = League(1, 16);
        var second = League(2, 16, parent: top, shared: top.Clubs[0]);

        var act = () => PromotionRelegation.Compute([top.Standings, second.Standings], []);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_league_too_small_to_exchange_two_is_a_programming_error()
    {
        var top = League(1, 16);
        var second = League(2, 1, parent: top);

        var act = () => PromotionRelegation.Compute([top.Standings, second.Standings], []);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_semi_final_with_no_final_is_an_error_not_a_direct_only_season()
    {
        var top = League(1, 16);
        var second = League(2, 16, parent: top);
        PlayoffOutcome[] semiOnly =
        [
            new(top.DivisionId, second.DivisionId, PlayoffStage.SemiFinal, second.Clubs[2], second.Clubs[3]),
        ];

        var act = () => PromotionRelegation.Compute([top.Standings, second.Standings], semiOnly);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_final_that_the_wrong_clubs_played_is_an_error()
    {
        var top = League(1, 16);
        var second = League(2, 16, parent: top);

        // The 5th of the lower league cannot be in the final.
        var act = () => PromotionRelegation.Compute(
            [top.Standings, second.Standings],
            Bracket(top, second, second.Clubs[4], second.Clubs[3], second.Clubs[4], top.Clubs[13]));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void An_outcome_for_leagues_that_are_not_a_pair_is_an_error()
    {
        var top = League(1, 16);
        var second = League(2, 16, parent: top);
        PlayoffOutcome[] stray =
        [
            new(second.DivisionId, top.DivisionId, PlayoffStage.Final, top.Clubs[0], second.Clubs[0]),
        ];

        var act = () => PromotionRelegation.Compute([top.Standings, second.Standings], stray);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validate_rejects_a_league_with_two_feeders()
    {
        var top = League(1, 16);
        var secondA = League(2, 16, parent: top, number: 1);
        var secondB = League(2, 16, parent: top, number: 2);

        var problems = PromotionRelegation.Validate([top.Standings, secondA.Standings, secondB.Standings]);
        var act = () => PromotionRelegation.Compute([top.Standings, secondA.Standings, secondB.Standings], []);

        problems.Should().Contain(problem => problem.Contains("feeder"));
        problems.Should().Contain(problem => problem.Contains("Tier 2 holds 2 leagues"));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validate_accepts_a_one_league_per_tier_pyramid()
    {
        var top = League(1, 16);
        var second = League(2, 16, parent: top);
        var third = League(3, 16, parent: second);

        PromotionRelegation.Validate([top.Standings, second.Standings, third.Standings]).Should().BeEmpty();
    }

    [Fact]
    public void No_leagues_moves_nobody()
    {
        PromotionRelegation.Compute([], []).Should().BeEmpty();
    }

    [Fact]
    public void The_version_names_the_rule()
    {
        PromotionRelegation.Version.Should().Be("promotion-relegation-v2");
    }

    private static int PerLeague(IReadOnlyList<ClubMovement> movements, TestLeague league) =>
        movements.Count(movement => movement.ToDivisionId == league.DivisionId);

    private static ClubMovement MovementOf(IReadOnlyList<ClubMovement> movements, Guid clubId) =>
        movements.Single(movement => movement.ClubId == clubId);

    private static PlayoffOutcome[] Bracket(
        TestLeague parent,
        TestLeague feeder,
        Guid semiWinner,
        Guid semiLoser,
        Guid finalWinner,
        Guid finalLoser) =>
    [
        new(parent.DivisionId, feeder.DivisionId, PlayoffStage.SemiFinal, semiWinner, semiLoser),
        new(parent.DivisionId, feeder.DivisionId, PlayoffStage.Final, finalWinner, finalLoser),
    ];

    private static TestLeague League(int tier, int count, TestLeague? parent = null, int number = 1, Guid? shared = null)
    {
        var divisionId = new Guid(tier, (short)number, 0, new byte[8]);
        var clubs = new List<Guid>(count);

        for (var index = 0; index < count; index++)
        {
            // Deterministic, distinct ids so a test failure names a club by its tier and position.
            clubs.Add(new Guid(0, (short)tier, (short)number, [(byte)index, 0, 0, 0, 0, 0, 0, 0]));
        }

        if (shared is not null)
        {
            clubs[0] = shared.Value;
        }

        return new TestLeague(divisionId, clubs, new LeagueStandings(divisionId, tier, number, parent?.DivisionId, clubs));
    }

    private sealed record TestLeague(Guid DivisionId, List<Guid> Clubs, LeagueStandings Standings);
}
