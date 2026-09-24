namespace TouchlineManager.Domain.World;

/// <summary>
/// The lifecycle state of a game world (master plan §6.3).
/// </summary>
/// <remarks>
/// Production runs exactly one world (`WORLD-1`), but the state exists because an operator needs to
/// stop onboarding without stopping the game: a frozen world keeps serving published content while
/// refusing new claims (master plan §13).
/// </remarks>
public enum GameWorldStatus
{
    /// <summary>Open for claims, matches, and transfers.</summary>
    Active = 0,

    /// <summary>Maintenance: existing managers keep playing, but onboarding is closed.</summary>
    Frozen = 1,

    /// <summary>Permanently closed. Read-only for history.</summary>
    Retired = 2,
}

/// <summary>
/// The authorization questions asked about <see cref="GameWorldStatus"/>.
/// </summary>
/// <remarks>
/// Naming the rules keeps "a frozen world does not accept claims" one enforced statement instead of a
/// status comparison each endpoint has to remember.
/// </remarks>
public static class GameWorldStatusRules
{
    /// <summary>Whether a world in this state accepts new club claims.</summary>
    public static bool AcceptsClaims(GameWorldStatus status) => status == GameWorldStatus.Active;

    /// <summary>Whether a world in this state still advances matchdays.</summary>
    public static bool AdvancesMatchdays(GameWorldStatus status) =>
        status is GameWorldStatus.Active or GameWorldStatus.Frozen;
}

/// <summary>
/// Storage and transport representation of <see cref="GameWorldStatus"/>.
/// </summary>
/// <remarks>
/// A stable lowercase code is persisted rather than the enum's numeric value, so the database stays
/// readable and reordering the enum cannot reinterpret existing rows.
/// </remarks>
public static class GameWorldStatuses
{
    /// <summary>The code for <see cref="GameWorldStatus.Active"/>.</summary>
    public const string ActiveCode = "active";

    /// <summary>The code for <see cref="GameWorldStatus.Frozen"/>.</summary>
    public const string FrozenCode = "frozen";

    /// <summary>The code for <see cref="GameWorldStatus.Retired"/>.</summary>
    public const string RetiredCode = "retired";

    /// <summary>Converts a status to its stable code.</summary>
    public static string ToCode(this GameWorldStatus status) => status switch
    {
        GameWorldStatus.Active => ActiveCode,
        GameWorldStatus.Frozen => FrozenCode,
        GameWorldStatus.Retired => RetiredCode,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown world status."),
    };

    /// <summary>Parses a stable code back to its status.</summary>
    public static GameWorldStatus FromCode(string code) => code switch
    {
        ActiveCode => GameWorldStatus.Active,
        FrozenCode => GameWorldStatus.Frozen,
        RetiredCode => GameWorldStatus.Retired,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown world status code."),
    };
}
