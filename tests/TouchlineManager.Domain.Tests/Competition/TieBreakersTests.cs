using FluentAssertions;
using TouchlineManager.Domain.Competition;

namespace TouchlineManager.Domain.Tests.Competition;

/// <summary>
/// The table's tie-break order as the competition-rules view lists it (`TBL-2`…`TBL-10`).
/// </summary>
/// <remarks>
/// The sequence is pinned as literal codes rather than through the constants that name them, because a
/// criterion whose code was silently renamed would still compare equal to itself — and the client's wording
/// is keyed on these exact strings, so a rename that skipped the client would be a rules page with three
/// unexplained codes on it.
/// </remarks>
public sealed class TieBreakersTests
{
    [Fact]
    public void The_order_runs_from_points_to_the_stored_draw()
    {
        TieBreakers.Ordered.Should().Equal(
            "points",
            "goal_difference",
            "goals_scored",
            "wins",
            "head_to_head_points",
            "head_to_head_goal_difference",
            "fewer_red_cards",
            "fewer_yellow_cards",
            "draw_key");

        TieBreakers.Ordered.Should().OnlyHaveUniqueItems("a criterion listed twice does not describe an order");
    }
}
