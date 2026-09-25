using FluentAssertions;
using TouchlineManager.Domain.Competition;

namespace TouchlineManager.Domain.Tests.Competition;

/// <summary>
/// The league table's ordering rules, in their exact sequence (`TBL-1`…`TBL-13`).
/// </summary>
/// <remarks>
/// Each test is written so that every criterion before the one under test is equal, which is the only way
/// to show that the criterion under test is the one doing the deciding. The constructions are chosen to be
/// reachable: a group of clubs tied on points, goal difference, goals scored, and wins is rare, and one whose
/// head-to-head goal difference differs while its head-to-head points agree is rarer still — which is exactly
/// why these are hand-built rather than generated.
/// </remarks>
public sealed class StandingsCalculatorTests
{
    private const string TieDrawSeed = "tie-draw-seed-for-tests";

    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-00000000000c");
    private static readonly Guid D = Guid.Parse("00000000-0000-0000-0000-00000000000d");

    [Fact]
    public void A_win_is_three_points_and_a_draw_is_one()
    {
        var table = Rank(
            [A, B, C, D],
            [
                Outcome(A, B, 2, 1),
                Outcome(C, D, 1, 1),
            ]);

        var winner = Line(table, A);
        var loser = Line(table, B);
        var drawer = Line(table, C);

        winner.Points.Should().Be(3, "TBL-1 awards three points for a win");
        winner.Won.Should().Be(1);
        winner.GoalsFor.Should().Be(2);
        winner.GoalsAgainst.Should().Be(1);
        winner.GoalDifference.Should().Be(1);

        loser.Points.Should().Be(0, "a defeat is worth nothing");
        loser.Lost.Should().Be(1);

        drawer.Points.Should().Be(1, "TBL-1 awards one point for a draw");
        drawer.Drawn.Should().Be(1);
        drawer.Played.Should().Be(1);

        table.Select(line => line.ClubId)
            .Should()
            .StartWith([A], "points order the table before anything else (TBL-2)");

        table.Select(line => line.ClubId).Should().EndWith([B]);
    }

    [Fact]
    public void Goal_difference_decides_before_goals_scored()
    {
        // Both clubs win one and lose one, on three points and a level win count. A's goal difference is the
        // better one and B scored four more goals than A, so the table can only be right if goal difference
        // (TBL-3) is applied before goals scored (TBL-4).
        var table = Rank(
            [A, B, C],
            [
                Outcome(A, C, 2, 0),
                Outcome(C, A, 1, 0),
                Outcome(B, C, 5, 3),
                Outcome(C, B, 3, 1),
            ]);

        var a = Line(table, A);
        var b = Line(table, B);

        a.Points.Should().Be(b.Points);
        a.Won.Should().Be(b.Won);
        a.GoalDifference.Should().Be(1);
        b.GoalDifference.Should().Be(0);
        b.GoalsFor.Should().BeGreaterThan(a.GoalsFor, "the club with fewer goals leads on goal difference");

        table.Select(line => line.ClubId)
            .Should()
            .ContainInOrder([A, B], "TBL-3 orders by goal difference");
    }

    [Fact]
    public void Goals_scored_decides_before_wins_when_goal_difference_is_level()
    {
        // Both clubs take three points from two matches with a level goal difference and one win each, so
        // goals scored (TBL-4) is what separates them — before the wins criterion, which is already level.
        var table = Rank(
            [A, B, C],
            [
                Outcome(A, C, 3, 1),
                Outcome(C, A, 3, 1),
                Outcome(B, C, 4, 0),
                Outcome(C, B, 6, 2),
            ]);

        var a = Line(table, A);
        var b = Line(table, B);

        a.Points.Should().Be(b.Points);
        a.Won.Should().Be(b.Won);
        a.GoalDifference.Should().Be(0);
        b.GoalDifference.Should().Be(0);
        a.GoalsFor.Should().Be(4);
        b.GoalsFor.Should().Be(6);

        table.Select(line => line.ClubId)
            .Should()
            .ContainInOrder([B, A], "TBL-4 orders by goals scored");
    }

    [Fact]
    public void Wins_decide_before_anything_below_them()
    {
        // A wins two and loses two; B wins one and draws three. The two arrive at the table level on points,
        // goal difference, and goals scored, so the win count is the criterion that orders them (TBL-5).
        var table = Rank(
            [A, B, C, D],
            [
                Outcome(A, C, 2, 0),
                Outcome(A, D, 1, 0),
                Outcome(C, A, 1, 0),
                Outcome(D, A, 1, 0),
                Outcome(B, C, 1, 0),
                Outcome(C, B, 1, 1),
                Outcome(B, D, 1, 1),
                Outcome(D, B, 0, 0),
            ]);

        var a = Line(table, A);
        var b = Line(table, B);

        a.Points.Should().Be(b.Points);
        a.GoalDifference.Should().Be(b.GoalDifference);
        a.GoalsFor.Should().Be(b.GoalsFor);
        a.Won.Should().Be(2);
        b.Won.Should().Be(1);

        table.Select(line => line.ClubId).Should().StartWith([A, B], "TBL-5 orders by wins");
    }

    [Fact]
    public void Clubs_tied_on_everything_above_are_separated_by_their_own_meetings()
    {
        // A and B are level on points, goal difference, goals scored, and wins. A beat B, so A leads the
        // head-to-head (TBL-6); the fourth club exists only to give both of them a defeat.
        var table = Rank(
            [A, B, D],
            [
                Outcome(A, B, 1, 0),
                Outcome(B, D, 1, 0),
                Outcome(D, A, 1, 0),
            ]);

        var a = Line(table, A);
        var b = Line(table, B);

        a.Points.Should().Be(3);
        b.Points.Should().Be(3);
        a.GoalDifference.Should().Be(b.GoalDifference);
        a.GoalsFor.Should().Be(b.GoalsFor);
        a.Won.Should().Be(b.Won);

        table.Select(line => line.ClubId).Should().ContainInOrder(A, B);
    }

    [Fact]
    public void Head_to_head_goal_difference_separates_clubs_level_on_their_own_points()
    {
        // A and B trade wins, so their head-to-head points are equal (3 each) and TBL-6 cannot separate
        // them; A's win was by two and B's by one, so A leads on head-to-head goal difference (TBL-7).
        // Their filler results are chosen so the two clubs arrive at the table level on points, goal
        // difference, goals scored, and wins — which is what makes TBL-7 the deciding rule.
        var table = Rank(
            [A, B, C, D],
            [
                Outcome(A, B, 3, 1),
                Outcome(B, A, 2, 1),
                Outcome(A, C, 2, 1),
                Outcome(D, A, 1, 0),
                Outcome(B, C, 3, 0),
                Outcome(D, B, 1, 0),
            ]);

        var a = Line(table, A);
        var b = Line(table, B);

        a.Points.Should().Be(b.Points);
        a.Won.Should().Be(b.Won);
        a.GoalsFor.Should().Be(b.GoalsFor);
        a.GoalDifference.Should().Be(b.GoalDifference, "everything above head-to-head goal difference is level");

        table.Select(line => line.ClubId)
            .Should()
            .ContainInOrder([A, B], "TBL-7 orders by head-to-head goal difference");
    }

    [Fact]
    public void A_club_that_is_level_on_everything_is_separated_by_its_cards()
    {
        // Two clubs, identical results, identical cards except that B was sent off once more than A. Fewer
        // red cards comes first (TBL-8).
        var table = Rank(
            [A, B],
            [
                Outcome(A, B, 1, 0, new DisciplineCounts(1, 0), new DisciplineCounts(1, 1)),
                Outcome(B, A, 1, 0, new DisciplineCounts(2, 1), new DisciplineCounts(0, 0)),
            ]);

        var a = Line(table, A);
        var b = Line(table, B);

        a.Points.Should().Be(b.Points);
        a.GoalDifference.Should().Be(b.GoalDifference);
        a.GoalsFor.Should().Be(b.GoalsFor);
        a.Won.Should().Be(b.Won);
        a.RedCards.Should().BeLessThan(b.RedCards);

        table.Select(line => line.ClubId).Should().Equal([A, B], "TBL-8 counts red cards before yellows");
    }

    [Fact]
    public void Yellow_cards_are_counted_after_red_cards_and_before_the_draw()
    {
        var table = Rank(
            [A, B],
            [
                Outcome(A, B, 1, 0, new DisciplineCounts(0, 0), new DisciplineCounts(3, 0)),
                Outcome(B, A, 1, 0, new DisciplineCounts(0, 0), new DisciplineCounts(0, 0)),
            ]);

        Line(table, A).RedCards.Should().Be(Line(table, B).RedCards);
        Line(table, A).YellowCards.Should().Be(0);
        Line(table, B).YellowCards.Should().Be(3);

        table.Select(line => line.ClubId).Should().Equal([A, B], "TBL-9 counts yellow cards");
    }

    [Fact]
    public void The_stored_draw_decides_when_two_clubs_cannot_be_separated_any_other_way()
    {
        // Two goalless draws in which nobody was booked: the clubs are identical, so the draw decides
        // (TBL-10) — and the order is the draw keys', not the order the clubs were passed in.
        var clubIds = new[] { A, B };
        var outcomes = new[] { Outcome(A, B, 0, 0), Outcome(B, A, 0, 0) };

        var table = Rank(clubIds, outcomes);

        var expected = clubIds
            .OrderBy(clubId => StandingsCalculator.DrawKeyOf(TieDrawSeed, clubId), StringComparer.Ordinal)
            .ToList();

        table.Select(line => line.ClubId).Should().Equal(expected);
    }

    [Fact]
    public void The_draw_key_is_a_function_of_the_seed_and_the_club()
    {
        var first = StandingsCalculator.DrawKeyOf(TieDrawSeed, A);
        var second = StandingsCalculator.DrawKeyOf(TieDrawSeed, A);

        first.Should().Be(second, "TBL-11 requires the draw to be reproducible from what was stored");
        first.Should().NotBe(StandingsCalculator.DrawKeyOf(TieDrawSeed, B));
        first.Should().NotBe(
            StandingsCalculator.DrawKeyOf("another-seed", A),
            "a different season's seed is a different draw");
    }

    [Fact]
    public void The_order_of_the_results_does_not_change_the_table()
    {
        var outcomes = new[]
        {
            Outcome(A, B, 2, 2),
            Outcome(C, D, 1, 0),
            Outcome(A, C, 0, 0),
            Outcome(B, D, 3, 1),
        };

        var forwards = Rank([A, B, C, D], outcomes);
        var backwards = Rank([A, B, C, D], outcomes.Reverse().ToList());

        backwards.Should().BeEquivalentTo(forwards, options => options.WithStrictOrdering());
        forwards.Select(line => line.Rank).Should().Equal([1, 2, 3, 4], "every club has a rank of its own");
    }

    [Fact]
    public void A_division_with_no_results_is_ranked_by_its_draw()
    {
        var table = Rank([A, B, C, D], []);

        table.Should().HaveCount(4);
        table.Should().OnlyContain(line => line.Played == 0 && line.Points == 0 && line.Rank > 0);
        table.Select(line => line.ClubId).Should().BeEquivalentTo([A, B, C, D]);
    }

    [Fact]
    public void A_result_between_clubs_outside_the_division_is_refused()
    {
        var act = () => Rank([A, B], [Outcome(A, D, 1, 0)]);

        act.Should().Throw<ArgumentException>(
            "a table that silently ignored a foreign result would hide a genuine data defect");
    }

    private static IReadOnlyList<StandingLine> Rank(
        IReadOnlyCollection<Guid> clubIds,
        IEnumerable<MatchOutcome> outcomes) =>
        StandingsCalculator.Rank(
            clubIds,
            outcomes,
            clubId => StandingsCalculator.DrawKeyOf(TieDrawSeed, clubId));

    private static StandingLine Line(IReadOnlyList<StandingLine> table, Guid clubId) =>
        table.Single(line => line.ClubId == clubId);

    private static MatchOutcome Outcome(
        Guid home,
        Guid away,
        int homeGoals,
        int awayGoals,
        DisciplineCounts? homeCards = null,
        DisciplineCounts? awayCards = null) =>
        new(
            home,
            away,
            homeGoals,
            awayGoals,
            homeCards ?? new DisciplineCounts(0, 0),
            awayCards ?? new DisciplineCounts(0, 0));
}
