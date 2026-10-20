namespace Tactical11Manager.Domain.Fans;

/// <summary>What moved a club's fans, as it is written in the club's fan history (`FAN-10`).</summary>
public enum FanEventKind
{
    /// <summary>The club's fans were created at its tier's opening numbers.</summary>
    Opening = 0,

    /// <summary>A league match was published.</summary>
    Match = 1,

    /// <summary>The weekly review of how close the club is to the fans' target.</summary>
    Weekly = 2,

    /// <summary>The end of a season: the last verdict on the target and the move halfway back to calm.</summary>
    SeasonReset = 3,

    /// <summary>The club climbed a tier.</summary>
    Promotion = 4,

    /// <summary>The club fell a tier.</summary>
    Relegation = 5,

    /// <summary>A human manager took the club over and its fans went back to the tier's opening numbers.</summary>
    Takeover = 6,
}

/// <summary>The stored codes of the fan event kinds.</summary>
public static class FanEventKinds
{
    /// <summary>The longest code, for the column.</summary>
    public const int MaxCodeLength = 16;

    /// <summary>The stable code a kind is stored under.</summary>
    /// <param name="kind">The kind.</param>
    public static string Code(FanEventKind kind) => kind switch
    {
        FanEventKind.Opening => "opening",
        FanEventKind.Match => "match",
        FanEventKind.Weekly => "weekly",
        FanEventKind.SeasonReset => "season_reset",
        FanEventKind.Promotion => "promotion",
        FanEventKind.Relegation => "relegation",
        FanEventKind.Takeover => "takeover",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown fan event kind."),
    };

    /// <summary>Reads a stored code back to its kind.</summary>
    /// <param name="code">The code.</param>
    /// <exception cref="ArgumentOutOfRangeException">When the code is not one of ours.</exception>
    public static FanEventKind FromCode(string code) => code switch
    {
        "opening" => FanEventKind.Opening,
        "match" => FanEventKind.Match,
        "weekly" => FanEventKind.Weekly,
        "season_reset" => FanEventKind.SeasonReset,
        "promotion" => FanEventKind.Promotion,
        "relegation" => FanEventKind.Relegation,
        "takeover" => FanEventKind.Takeover,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown fan event code."),
    };

    /// <summary>Every code, for the check constraint.</summary>
    public static IReadOnlyList<string> AllCodes { get; } =
        [.. Enum.GetValues<FanEventKind>().Select(Code)];
}
