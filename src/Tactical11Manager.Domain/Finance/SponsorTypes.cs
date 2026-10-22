namespace Tactical11Manager.Domain.Finance;

/// <summary>The three offers a sponsor makes a club each season, which differ by how much of the money depends on the result (`sponsor-v1`).</summary>
public enum SponsorOfferKind
{
    /// <summary>All of the fixed part, and half the bonus.</summary>
    Steady = 0,

    /// <summary>85% of the fixed part, and the whole bonus.</summary>
    Balanced = 1,

    /// <summary>65% of the fixed part, and twice the bonus.</summary>
    Performance = 2,
}

/// <summary>The target a manager picks for a season, as a final place in the division.</summary>
public enum SeasonObjective
{
    /// <summary>Win the league (1st).</summary>
    WinLeague = 1,

    /// <summary>Finish in the top three.</summary>
    TopThree = 2,

    /// <summary>Finish in the top six.</summary>
    TopSix = 3,

    /// <summary>Finish in the top half (9th or better).</summary>
    TopHalf = 4,

    /// <summary>Finish 12th or better.</summary>
    TwelfthPlace = 5,

    /// <summary>Stay up (15th or better).</summary>
    StayUp = 6,
}

/// <summary>How a season ended against its objective.</summary>
public enum ObjectiveOutcome
{
    /// <summary>The club finished below the objective's place.</summary>
    Missed = 0,

    /// <summary>The club finished at the objective's place or better.</summary>
    Met = 1,

    /// <summary>The club finished three or more places better than the objective asked.</summary>
    Exceeded = 2,
}

/// <summary>Stable codes for the sponsor and objective enums, so a stored value never moves with the enum's order.</summary>
public static class SponsorCodes
{
    /// <summary>The code of an offer.</summary>
    /// <param name="kind">The offer.</param>
    public static string ToCode(this SponsorOfferKind kind) => kind switch
    {
        SponsorOfferKind.Steady => "steady",
        SponsorOfferKind.Balanced => "balanced",
        SponsorOfferKind.Performance => "performance",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown sponsor offer."),
    };

    /// <summary>The code of an objective.</summary>
    /// <param name="objective">The objective.</param>
    public static string ToCode(this SeasonObjective objective) => objective switch
    {
        SeasonObjective.WinLeague => "win_league",
        SeasonObjective.TopThree => "top_three",
        SeasonObjective.TopSix => "top_six",
        SeasonObjective.TopHalf => "top_half",
        SeasonObjective.TwelfthPlace => "twelfth",
        SeasonObjective.StayUp => "stay_up",
        _ => throw new ArgumentOutOfRangeException(nameof(objective), objective, "Unknown season objective."),
    };

    /// <summary>The code of an outcome.</summary>
    /// <param name="outcome">The outcome.</param>
    public static string ToCode(this ObjectiveOutcome outcome) => outcome switch
    {
        ObjectiveOutcome.Missed => "missed",
        ObjectiveOutcome.Met => "met",
        ObjectiveOutcome.Exceeded => "exceeded",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown objective outcome."),
    };
}
