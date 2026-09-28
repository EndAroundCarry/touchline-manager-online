namespace TouchlineManager.Contracts.Squad;

/// <summary>
/// The deterministic terms the server offers for a renewal (`CON-3`, master plan §10.3).
/// </summary>
/// <remarks>
/// Carries no hidden value: the quote is derived from potential, but only the wage it produces is returned.
/// The season range is the one the new contract would cover if it were accepted now.
/// </remarks>
/// <param name="ContractId">The contract the quote is for.</param>
/// <param name="PlayerId">The contracted player.</param>
/// <param name="Seasons">The length the quote proposes.</param>
/// <param name="StartSeasonNumber">The season the new contract would begin in.</param>
/// <param name="EndSeasonNumber">The season it would end in.</param>
/// <param name="WeeklyWageMinor">The proposed weekly wage, in minor units.</param>
/// <param name="ContractVersion">
/// The contract's version at the moment it was quoted. The renewal must send it back as the strong entity
/// tag in <c>If-Match</c>, so a renewal agreed against a stale read is refused rather than applied
/// (`CONC-1`, ADR-0009).
/// </param>
/// <param name="ServerTime">The server's current instant.</param>
public sealed record RenewalQuoteResponse(
    Guid ContractId,
    Guid PlayerId,
    int Seasons,
    int StartSeasonNumber,
    int EndSeasonNumber,
    long WeeklyWageMinor,
    long ContractVersion,
    DateTimeOffset ServerTime);

/// <summary>The contract a manager has just signed a player to (`CON-4`).</summary>
/// <param name="ContractId">The new contract's identity.</param>
/// <param name="PlayerId">The player re-signed.</param>
/// <param name="Seasons">The new contract's length.</param>
/// <param name="StartSeasonNumber">The season it begins in.</param>
/// <param name="EndSeasonNumber">The season it ends in.</param>
/// <param name="WeeklyWageMinor">The new weekly wage, in minor units.</param>
/// <param name="Version">The new contract's concurrency version, for a later conditional write.</param>
/// <param name="ServerTime">The server's current instant.</param>
public sealed record ContractRenewalResponse(
    Guid ContractId,
    Guid PlayerId,
    int Seasons,
    int StartSeasonNumber,
    int EndSeasonNumber,
    long WeeklyWageMinor,
    long Version,
    DateTimeOffset ServerTime);
