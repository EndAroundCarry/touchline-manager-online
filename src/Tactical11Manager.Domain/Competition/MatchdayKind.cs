namespace Tactical11Manager.Domain.Competition;

/// <summary>What a matchday is for (`PR-4`).</summary>
public enum MatchdayKind
{
    /// <summary>A round of the league's double round-robin; it counts in the table.</summary>
    League = 0,

    /// <summary>A promotion play-off match, hosted in a league's division-season but outside its table.</summary>
    Playoff = 1,
}

/// <summary>The questions asked about <see cref="MatchdayKind"/>.</summary>
public static class MatchdayKindRules
{
    /// <summary>Storage and transport representation.</summary>
    public static string ToCode(this MatchdayKind kind) => kind switch
    {
        MatchdayKind.League => MatchdayKinds.LeagueCode,
        MatchdayKind.Playoff => MatchdayKinds.PlayoffCode,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown matchday kind."),
    };

    /// <summary>Parses a stable code back to its kind.</summary>
    public static MatchdayKind FromCode(string code) => code switch
    {
        MatchdayKinds.LeagueCode => MatchdayKind.League,
        MatchdayKinds.PlayoffCode => MatchdayKind.Playoff,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown matchday kind code."),
    };
}

/// <summary>Stable codes for <see cref="MatchdayKind"/>.</summary>
public static class MatchdayKinds
{
    /// <summary>The code for <see cref="MatchdayKind.League"/>.</summary>
    public const string LeagueCode = "league";

    /// <summary>The code for <see cref="MatchdayKind.Playoff"/>.</summary>
    public const string PlayoffCode = "playoff";

    /// <summary>The longest code, used to size the storage column.</summary>
    public const int MaxCodeLength = 16;
}
