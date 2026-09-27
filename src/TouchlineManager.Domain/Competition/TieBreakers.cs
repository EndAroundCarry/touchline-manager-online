namespace TouchlineManager.Domain.Competition;

/// <summary>
/// The league table's tie-breakers, in the exact order they are applied (`TBL-2`…`TBL-10`).
/// </summary>
/// <remarks>
/// <para>
/// The same sequence <see cref="StandingsCalculator"/> implements, named as stable codes so it can be shown
/// to a manager without a second, drifting copy of the rule. <see cref="Ordered"/> is the ordering a
/// competition-rules view lists and the reason the draw key is visible at all (`TBL-11`): an ordering whose
/// last criterion is a stored draw is only meaningful if a manager can see the draw the season committed to
/// before it was played.
/// </para>
/// <para>
/// Codes rather than prose, because the wording is the client's and the sequence is the server's — the same
/// split the commentary tokens and the inbox templates use (§8.6). A criterion added here must also be
/// applied by the calculator, or the rules view would describe an order the table does not follow; the
/// domain tests pin the two together where a table can demonstrate it.
/// </para>
/// </remarks>
public static class TieBreakers
{
    /// <summary>Points (`TBL-2`).</summary>
    public const string Points = "points";

    /// <summary>Goal difference (`TBL-3`).</summary>
    public const string GoalDifference = "goal_difference";

    /// <summary>Goals scored (`TBL-4`).</summary>
    public const string GoalsScored = "goals_scored";

    /// <summary>Wins (`TBL-5`).</summary>
    public const string Wins = "wins";

    /// <summary>Head-to-head points among the tied clubs (`TBL-6`).</summary>
    public const string HeadToHeadPoints = "head_to_head_points";

    /// <summary>Head-to-head goal difference among the tied clubs (`TBL-7`).</summary>
    public const string HeadToHeadGoalDifference = "head_to_head_goal_difference";

    /// <summary>Fewer red cards (`TBL-8`).</summary>
    public const string FewerRedCards = "fewer_red_cards";

    /// <summary>Fewer yellow cards (`TBL-9`).</summary>
    public const string FewerYellowCards = "fewer_yellow_cards";

    /// <summary>The deterministic season draw derived from season and club identity (`TBL-10`).</summary>
    public const string DrawKey = "draw_key";

    /// <summary>
    /// Every tie-breaker, first difference deciding, in the order the table applies them (`TBL-2`…`TBL-10`).
    /// </summary>
    public static readonly IReadOnlyList<string> Ordered =
    [
        Points,
        GoalDifference,
        GoalsScored,
        Wins,
        HeadToHeadPoints,
        HeadToHeadGoalDifference,
        FewerRedCards,
        FewerYellowCards,
        DrawKey,
    ];
}
