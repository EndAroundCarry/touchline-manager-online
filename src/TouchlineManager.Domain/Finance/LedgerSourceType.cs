namespace TouchlineManager.Domain.Finance;

/// <summary>
/// What produced a ledger entry: the workflow that moved the money (master plan §6.8).
/// </summary>
/// <remarks>
/// A category says <em>what</em> an entry is; a source type says <em>which workflow</em> wrote it, beside the
/// source's own identity. Together they are the correlation trail an operator follows when a balance is
/// disputed (`FIN-17`) — "this wage run, from this job, produced these entries" — rather than a second
/// accounting of the amount, which the entry's own deltas already carry.
/// </remarks>
public enum LedgerSourceType
{
    /// <summary>World generation opened the account (`FIN-1`).</summary>
    WorldSeed = 0,

    /// <summary>A matchday publication posted its income (`FIN-3`).</summary>
    Matchday = 1,

    /// <summary>The weekly finance run posted wages, sponsorship, and costs (`FIN-4`, `FIN-7`, `FIN-9`).</summary>
    WeeklyRun = 2,

    /// <summary>Season rollover settled awards and expiry (`FIN-5`).</summary>
    SeasonRollover = 3,

    /// <summary>A transfer resolution or bid moved money and reservations (`FIN-6`, `FIN-8`, `FIN-10`).</summary>
    Transfer = 4,

    /// <summary>The safety job granted emergency funds (`FIN-16`).</summary>
    SafetyJob = 5,

    /// <summary>An audited operator repair posted a compensating entry (`FIN-12`).</summary>
    AdminRepair = 6,

    /// <summary>A manager's stadium works paid for the new places (`STAD-4`).</summary>
    Stadium = 7,
}

/// <summary>Stable codes and parsing for <see cref="LedgerSourceType"/>.</summary>
public static class LedgerSourceTypes
{
    /// <summary>The longest code, so a column can be sized to hold every value.</summary>
    public const int MaxCodeLength = 15;

    /// <summary>Every source type, in declaration order.</summary>
    public static readonly IReadOnlyList<LedgerSourceType> All = [.. Enum.GetValues<LedgerSourceType>()];

    /// <summary>Converts a source type to its stable code.</summary>
    /// <param name="sourceType">The source type.</param>
    public static string ToCode(this LedgerSourceType sourceType) => sourceType switch
    {
        LedgerSourceType.WorldSeed => "world_seed",
        LedgerSourceType.Matchday => "matchday",
        LedgerSourceType.WeeklyRun => "weekly_run",
        LedgerSourceType.SeasonRollover => "season_rollover",
        LedgerSourceType.Transfer => "transfer",
        LedgerSourceType.SafetyJob => "safety_job",
        LedgerSourceType.AdminRepair => "admin_repair",
        LedgerSourceType.Stadium => "stadium",
        _ => throw new ArgumentOutOfRangeException(nameof(sourceType), sourceType, "Unknown ledger source."),
    };

    /// <summary>Parses a stable code back to its source type.</summary>
    /// <param name="code">The stable code.</param>
    public static LedgerSourceType FromCode(string code) => code switch
    {
        "world_seed" => LedgerSourceType.WorldSeed,
        "matchday" => LedgerSourceType.Matchday,
        "weekly_run" => LedgerSourceType.WeeklyRun,
        "season_rollover" => LedgerSourceType.SeasonRollover,
        "transfer" => LedgerSourceType.Transfer,
        "safety_job" => LedgerSourceType.SafetyJob,
        "admin_repair" => LedgerSourceType.AdminRepair,
        "stadium" => LedgerSourceType.Stadium,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown ledger source code."),
    };
}
