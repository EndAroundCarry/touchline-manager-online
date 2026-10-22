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

/// <summary>Who put a sponsor deal's name to the contract.</summary>
public enum SponsorSigner
{
    /// <summary>The club's manager chose the offer and the objective.</summary>
    Manager = 0,

    /// <summary>The deadline passed, and the board signed the balanced offer at its realistic objective.</summary>
    Board = 1,

    /// <summary>A computer club signed it through `ai-finance-v1`.</summary>
    Ai = 2,
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

    /// <summary>The code of a signer.</summary>
    /// <param name="signer">The signer.</param>
    public static string ToCode(this SponsorSigner signer) => signer switch
    {
        SponsorSigner.Manager => "manager",
        SponsorSigner.Board => "board",
        SponsorSigner.Ai => "ai",
        _ => throw new ArgumentOutOfRangeException(nameof(signer), signer, "Unknown sponsor signer."),
    };

    /// <summary>The offer a stored code stands for.</summary>
    /// <param name="code">The code.</param>
    public static SponsorOfferKind OfferFromCode(string code) => code switch
    {
        "steady" => SponsorOfferKind.Steady,
        "balanced" => SponsorOfferKind.Balanced,
        "performance" => SponsorOfferKind.Performance,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown sponsor offer code."),
    };

    /// <summary>The objective a stored code stands for.</summary>
    /// <param name="code">The code.</param>
    public static SeasonObjective ObjectiveFromCode(string code) => code switch
    {
        "win_league" => SeasonObjective.WinLeague,
        "top_three" => SeasonObjective.TopThree,
        "top_six" => SeasonObjective.TopSix,
        "top_half" => SeasonObjective.TopHalf,
        "twelfth" => SeasonObjective.TwelfthPlace,
        "stay_up" => SeasonObjective.StayUp,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown season objective code."),
    };

    /// <summary>The outcome a stored code stands for.</summary>
    /// <param name="code">The code.</param>
    public static ObjectiveOutcome OutcomeFromCode(string code) => code switch
    {
        "missed" => ObjectiveOutcome.Missed,
        "met" => ObjectiveOutcome.Met,
        "exceeded" => ObjectiveOutcome.Exceeded,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown objective outcome code."),
    };

    /// <summary>The signer a stored code stands for.</summary>
    /// <param name="code">The code.</param>
    public static SponsorSigner SignerFromCode(string code) => code switch
    {
        "manager" => SponsorSigner.Manager,
        "board" => SponsorSigner.Board,
        "ai" => SponsorSigner.Ai,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown sponsor signer code."),
    };

    /// <summary>The longest code of any of these enums, so a column can be sized to hold every value.</summary>
    public const int MaxCodeLength = 16;

    /// <summary>The codes of every offer.</summary>
    public static IReadOnlyList<string> OfferCodes { get; } = [.. Enum.GetValues<SponsorOfferKind>().Select(kind => kind.ToCode())];

    /// <summary>The codes of every objective.</summary>
    public static IReadOnlyList<string> ObjectiveCodes { get; } = [.. Enum.GetValues<SeasonObjective>().Select(objective => objective.ToCode())];

    /// <summary>The codes of every outcome.</summary>
    public static IReadOnlyList<string> OutcomeCodes { get; } = [.. Enum.GetValues<ObjectiveOutcome>().Select(outcome => outcome.ToCode())];

    /// <summary>The codes of every signer.</summary>
    public static IReadOnlyList<string> SignerCodes { get; } = [.. Enum.GetValues<SponsorSigner>().Select(signer => signer.ToCode())];
}
