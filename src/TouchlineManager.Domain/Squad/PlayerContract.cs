using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Domain.Squad;

/// <summary>The lifecycle state of a player contract (`CON-5`, `CON-6`).</summary>
public enum ContractStatus
{
    /// <summary>In force. A player has at most one of these (`SQ-6`).</summary>
    Active = 0,

    /// <summary>Ended, for one of the reasons in <see cref="PlayerContractCloseReasons"/>.</summary>
    Closed = 1,
}

/// <summary>
/// The player's standing in the squad.
/// </summary>
/// <remarks>
/// A classification rather than a rule: it is what a manager sees and what the AI market weighs in
/// Stage 10, and it does not affect eligibility. The generator derives it from a player's ability rank
/// inside the squad.
/// </remarks>
public enum SquadStatus
{
    /// <summary>Among the club's best players.</summary>
    KeyPlayer = 0,

    /// <summary>An expected starter.</summary>
    FirstTeam = 1,

    /// <summary>Squad depth and rotation.</summary>
    Rotation = 2,

    /// <summary>A developing or fringe player.</summary>
    Prospect = 3,
}

/// <summary>Storage and transport representation for contracts and squad standing.</summary>
public static class ContractCodes
{
    /// <summary>The code for <see cref="ContractStatus.Active"/>.</summary>
    public const string ActiveCode = "active";

    /// <summary>The code for <see cref="ContractStatus.Closed"/>.</summary>
    public const string ClosedCode = "closed";

    /// <summary>The code for <see cref="SquadStatus.KeyPlayer"/>.</summary>
    public const string KeyPlayerCode = "key_player";

    /// <summary>The code for <see cref="SquadStatus.FirstTeam"/>.</summary>
    public const string FirstTeamCode = "first_team";

    /// <summary>The code for <see cref="SquadStatus.Rotation"/>.</summary>
    public const string RotationCode = "rotation";

    /// <summary>The code for <see cref="SquadStatus.Prospect"/>.</summary>
    public const string ProspectCode = "prospect";

    /// <summary>The longest contract status code, so a column can be sized to hold every value.</summary>
    public const int MaxStatusCodeLength = 6;

    /// <summary>The longest squad status code, so a column can be sized to hold every value.</summary>
    public const int MaxSquadStatusCodeLength = 10;

    /// <summary>Converts a contract status to its stable code.</summary>
    /// <param name="status">The contract status.</param>
    public static string ToCode(this ContractStatus status) => status switch
    {
        ContractStatus.Active => ActiveCode,
        ContractStatus.Closed => ClosedCode,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown contract status."),
    };

    /// <summary>Parses a stable code back to its contract status.</summary>
    /// <param name="code">The stable code.</param>
    public static ContractStatus StatusFromCode(string code) => code switch
    {
        ActiveCode => ContractStatus.Active,
        ClosedCode => ContractStatus.Closed,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown contract status code."),
    };

    /// <summary>Converts a squad status to its stable code.</summary>
    /// <param name="status">The squad status.</param>
    public static string ToCode(this SquadStatus status) => status switch
    {
        SquadStatus.KeyPlayer => KeyPlayerCode,
        SquadStatus.FirstTeam => FirstTeamCode,
        SquadStatus.Rotation => RotationCode,
        SquadStatus.Prospect => ProspectCode,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown squad status."),
    };

    /// <summary>Parses a stable code back to its squad status.</summary>
    /// <param name="code">The stable code.</param>
    public static SquadStatus SquadStatusFromCode(string code) => code switch
    {
        KeyPlayerCode => SquadStatus.KeyPlayer,
        FirstTeamCode => SquadStatus.FirstTeam,
        RotationCode => SquadStatus.Rotation,
        ProspectCode => SquadStatus.Prospect,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown squad status code."),
    };
}

/// <summary>Why a contract ended.</summary>
/// <remarks>
/// Recorded rather than inferred, because rollover, a transfer, and an administrative release have
/// different downstream consequences and the support answer to "where did this player go?" depends on
/// which one it was (`CON-5`, `CON-6`).
/// </remarks>
public static class PlayerContractCloseReasons
{
    /// <summary>The contract ran to its end season and was not renewed (`CON-6`).</summary>
    public const string Expired = "expired";

    /// <summary>The player was sold, and the buyer's contract replaced this one (`CON-5`).</summary>
    public const string Transferred = "transferred";

    /// <summary>The club released the player.</summary>
    public const string Released = "released";

    /// <summary>The player retired.</summary>
    public const string Retired = "retired";

    /// <summary>Whether a reason is one the contract aggregate recognises.</summary>
    /// <param name="reason">The reason code.</param>
    public static bool IsKnown(string reason) =>
        reason is Expired or Transferred or Released or Retired;
}

/// <summary>
/// An agreement binding a player to a club for a range of game seasons at a weekly wage (`CON-1`,
/// `CON-2`).
/// </summary>
/// <remarks>
/// <para>
/// The term is expressed in season numbers, not dates, because contract years advance at rollover
/// rather than on a real-world anniversary (`CON-8`, `TIME-3`). Wages are in minor units of the one
/// canonical currency, and the weekly charge itself is finance's job in Stage 9.
/// </para>
/// <para>
/// "At most one active contract per player" (`SQ-6`) is enforced by a partial unique index in the
/// database; the aggregate enforces that a term is a legal length and that closing is a known reason.
/// </para>
/// </remarks>
public sealed class PlayerContract
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private PlayerContract()
    {
    }

    /// <summary>Gets the contract identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the contracted player.</summary>
    public Guid PlayerId { get; private set; }

    /// <summary>Gets the club the player is contracted to.</summary>
    public Guid ClubId { get; private set; }

    /// <summary>Gets the first season the contract covers.</summary>
    public int StartSeasonNumber { get; private set; }

    /// <summary>Gets the last season the contract covers.</summary>
    public int EndSeasonNumber { get; private set; }

    /// <summary>Gets the weekly wage, in minor units (`CON-2`).</summary>
    public long WeeklyWageMinor { get; private set; }

    /// <summary>Gets the player's standing in the squad.</summary>
    public SquadStatus SquadStatus { get; private set; }

    /// <summary>Gets the lifecycle state.</summary>
    public ContractStatus Status { get; private set; }

    /// <summary>Gets why the contract closed, when it has.</summary>
    public string? ClosedReason { get; private set; }

    /// <summary>Gets when the contract closed, when it has.</summary>
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>Gets when the contract was signed.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the contract was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Gets whether the contract is in force. A player has at most one of these (`SQ-6`).</summary>
    public bool IsActive => Status == ContractStatus.Active;

    /// <summary>Signs a player to a club for a legal number of game seasons (`CON-1`).</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="playerId">The contracted player.</param>
    /// <param name="clubId">The club the player is contracted to.</param>
    /// <param name="startSeasonNumber">The first season covered.</param>
    /// <param name="endSeasonNumber">The last season covered.</param>
    /// <param name="weeklyWageMinor">The weekly wage in minor units.</param>
    /// <param name="squadStatus">The player's standing in the squad.</param>
    /// <param name="now">The current instant.</param>
    public static PlayerContract Sign(
        Guid id,
        Guid playerId,
        Guid clubId,
        int startSeasonNumber,
        int endSeasonNumber,
        long weeklyWageMinor,
        SquadStatus squadStatus,
        DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(startSeasonNumber, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(weeklyWageMinor);

        if (endSeasonNumber < startSeasonNumber)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endSeasonNumber),
                endSeasonNumber,
                "A contract never ends before it starts.");
        }

        var seasons = (endSeasonNumber - startSeasonNumber) + 1;

        if (seasons is < WorldRuleSet.ContractMinSeasons or > WorldRuleSet.ContractMaxSeasons)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endSeasonNumber),
                seasons,
                $"A contract is between {WorldRuleSet.ContractMinSeasons} and {WorldRuleSet.ContractMaxSeasons} game seasons (CON-1).");
        }

        return new PlayerContract
        {
            Id = id,
            PlayerId = playerId,
            ClubId = clubId,
            StartSeasonNumber = startSeasonNumber,
            EndSeasonNumber = endSeasonNumber,
            WeeklyWageMinor = weeklyWageMinor,
            SquadStatus = squadStatus,
            Status = ContractStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Closes the contract, recording why.</summary>
    /// <param name="reason">One of <see cref="PlayerContractCloseReasons"/>.</param>
    /// <param name="now">The current instant.</param>
    public void Close(string reason, DateTimeOffset now)
    {
        if (!PlayerContractCloseReasons.IsKnown(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown contract close reason.");
        }

        if (Status == ContractStatus.Closed)
        {
            throw new InvalidOperationException(
                $"A contract cannot be closed twice; it is already closed as '{ClosedReason}'.");
        }

        Status = ContractStatus.Closed;
        ClosedReason = reason;
        ClosedAt = now;

        Touch(now);
    }

    /// <summary>Changes the player's standing in the squad.</summary>
    /// <param name="squadStatus">The new standing.</param>
    /// <param name="now">The current instant.</param>
    public void ChangeSquadStatus(SquadStatus squadStatus, DateTimeOffset now)
    {
        SquadStatus = squadStatus;

        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
