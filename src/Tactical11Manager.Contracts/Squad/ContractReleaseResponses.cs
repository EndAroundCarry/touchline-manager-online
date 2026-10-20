namespace Tactical11Manager.Contracts.Squad;

/// <summary>
/// What releasing a player now would cost, and whether the club may do it (`CON-12`).
/// </summary>
/// <remarks>
/// Carries no hidden value: the payout is half the wages left on the contract, and the contract's wage is the
/// club's own to read.
/// </remarks>
/// <param name="ContractId">The contract the quote is for.</param>
/// <param name="PlayerId">The contracted player.</param>
/// <param name="RemainingWeeks">The weeks of wages left on the contract, rounded up.</param>
/// <param name="PayoutMinor">What the club pays to release the player, in minor units.</param>
/// <param name="WeeklyWageMinor">The wage the club stops paying each week, in minor units.</param>
/// <param name="AvailableMinor">The club's cash less what its bids have reserved, in minor units.</param>
/// <param name="JoinsPoolAt">When the player joins the free-agent pool: the next weekly batch.</param>
/// <param name="BlockedReason">
/// The stable code of the rule that refuses the release now, or null when it is allowed. The client shows the
/// reason beside a disabled confirm button instead of letting the manager find out by trying.
/// </param>
/// <param name="ContractVersion">
/// The contract's version when it was quoted. The release must send it back as the strong entity tag in
/// <c>If-Match</c>, so a release agreed against a stale read is refused rather than applied (`CONC-1`).
/// </param>
/// <param name="ServerTime">The server's current instant.</param>
public sealed record ReleaseQuoteResponse(
    Guid ContractId,
    Guid PlayerId,
    int RemainingWeeks,
    long PayoutMinor,
    long WeeklyWageMinor,
    long AvailableMinor,
    DateTimeOffset JoinsPoolAt,
    string? BlockedReason,
    long ContractVersion,
    DateTimeOffset ServerTime);

/// <summary>A player a manager has just released (`CON-12`).</summary>
/// <param name="ContractId">The contract that was closed.</param>
/// <param name="PlayerId">The player released.</param>
/// <param name="PayoutMinor">What the club paid, in minor units.</param>
/// <param name="RemainingWeeks">The weeks of wages the payout was half of.</param>
/// <param name="WeeklyWageSavedMinor">The wage the club no longer pays each week, in minor units.</param>
/// <param name="JoinsPoolAt">When the player joins the free-agent pool: the next weekly batch.</param>
/// <param name="ServerTime">The server's current instant.</param>
public sealed record ReleasePlayerResponse(
    Guid ContractId,
    Guid PlayerId,
    long PayoutMinor,
    int RemainingWeeks,
    long WeeklyWageSavedMinor,
    DateTimeOffset JoinsPoolAt,
    DateTimeOffset ServerTime);
