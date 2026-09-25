namespace TouchlineManager.Domain.Squad;

/// <summary>
/// The lifecycle state of a player (`SQ-8`, `CON-6`; master plan §6.5).
/// </summary>
/// <remarks>
/// <see cref="Anonymized"/> exists for the same reason the account lifecycle needs it: a data-subject
/// request removes the person, but the player's effect on competition history must remain readable
/// (`data-classification.md` §3). A player is never deleted.
/// </remarks>
public enum PlayerStatus
{
    /// <summary>Contracted to a club and eligible for selection.</summary>
    Active = 0,

    /// <summary>Retired from play. History preserved.</summary>
    Retired = 1,

    /// <summary>Out of contract and between clubs (`CON-6`).</summary>
    FreeAgent = 2,

    /// <summary>Identity removed on a data-subject request; historical record retained.</summary>
    Anonymized = 3,
}

/// <summary>Stable codes and storage representation for <see cref="PlayerStatus"/>.</summary>
public static class PlayerStatuses
{
    /// <summary>The code for <see cref="PlayerStatus.Active"/>.</summary>
    public const string ActiveCode = "active";

    /// <summary>The code for <see cref="PlayerStatus.Retired"/>.</summary>
    public const string RetiredCode = "retired";

    /// <summary>The code for <see cref="PlayerStatus.FreeAgent"/>.</summary>
    public const string FreeAgentCode = "free_agent";

    /// <summary>The code for <see cref="PlayerStatus.Anonymized"/>.</summary>
    public const string AnonymizedCode = "anonymized";

    /// <summary>The longest stable code, so a column can be sized to hold every value.</summary>
    public const int MaxCodeLength = 10;

    /// <summary>Converts a status to its stable code.</summary>
    /// <param name="status">The player status.</param>
    public static string ToCode(this PlayerStatus status) => status switch
    {
        PlayerStatus.Active => ActiveCode,
        PlayerStatus.Retired => RetiredCode,
        PlayerStatus.FreeAgent => FreeAgentCode,
        PlayerStatus.Anonymized => AnonymizedCode,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown player status."),
    };

    /// <summary>Parses a stable code back to its status.</summary>
    /// <param name="code">The stable code.</param>
    public static PlayerStatus FromCode(string code) => code switch
    {
        ActiveCode => PlayerStatus.Active,
        RetiredCode => PlayerStatus.Retired,
        FreeAgentCode => PlayerStatus.FreeAgent,
        AnonymizedCode => PlayerStatus.Anonymized,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown player status code."),
    };
}
