using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Abstractions.Squad;

/// <summary>A player's state as stored, in basis points (`TRN-5`…`TRN-7`).</summary>
/// <param name="ConditionBp">Condition in basis points.</param>
/// <param name="FatigueBp">Fatigue in basis points.</param>
/// <param name="MoraleBp">Morale in basis points.</param>
/// <param name="MatchSharpnessBp">Match sharpness in basis points.</param>
public sealed record SquadStateRow(
    int ConditionBp,
    int FatigueBp,
    int MoraleBp,
    int MatchSharpnessBp);

/// <summary>A player's active contract, as the squad and contract screens read it.</summary>
/// <param name="Id">The contract identity.</param>
/// <param name="StartSeasonNumber">The first season the contract covers.</param>
/// <param name="EndSeasonNumber">The last season the contract covers.</param>
/// <param name="WeeklyWageMinor">The weekly wage in minor units.</param>
/// <param name="SquadStatus">The player's standing in the squad.</param>
/// <param name="Status">The contract's lifecycle state.</param>
public sealed record SquadContractRow(
    Guid Id,
    int StartSeasonNumber,
    int EndSeasonNumber,
    long WeeklyWageMinor,
    SquadStatus SquadStatus,
    ContractStatus Status);

/// <summary>A player's registration, which is what makes them selectable (`SQ-6`).</summary>
/// <param name="Id">The registration identity.</param>
/// <param name="ClubId">The club the player is registered to.</param>
/// <param name="Status">The registration's lifecycle state.</param>
/// <param name="EffectiveFixtureBoundaryRound">The round the registration takes effect from.</param>
public sealed record SquadRegistrationRow(
    Guid Id,
    Guid ClubId,
    RegistrationStatus Status,
    int EffectiveFixtureBoundaryRound);

/// <summary>An open injury or suspension, measured in fixtures (`TRN-12`).</summary>
/// <param name="Id">The record identity.</param>
/// <param name="Type">The kind of unavailability.</param>
/// <param name="Severity">How serious the injury is.</param>
/// <param name="RemainingFixtures">How many eligible fixtures the player still misses.</param>
/// <param name="StartedAt">When the record started.</param>
public sealed record SquadAvailabilityRow(
    Guid Id,
    UnavailabilityType Type,
    InjurySeverity Severity,
    int RemainingFixtures,
    DateTimeOffset StartedAt);

/// <summary>One player in a club's squad, as stored.</summary>
/// <remarks>
/// Carries domain values rather than transport shapes — positions as <see cref="PlayerPosition"/>,
/// state in basis points — so the conversion `TRN-8` requires happens once, in the application mapper,
/// rather than in the query.
/// </remarks>
/// <param name="Id">The player identity.</param>
/// <param name="FullName">The generated full name.</param>
/// <param name="ShortName">The abbreviation.</param>
/// <param name="NationalityCode">The nationality country code.</param>
/// <param name="BirthGameYear">The game year the player was born in.</param>
/// <param name="PreferredFoot">The foot the player favours.</param>
/// <param name="PrimaryPosition">The player's position.</param>
/// <param name="SecondaryPositions">The other positions the player covers.</param>
/// <param name="State">The player's state.</param>
/// <param name="Contract">The player's active contract.</param>
/// <param name="Availability">Every open injury and suspension.</param>
public sealed record SquadPlayerRow(
    Guid Id,
    string FullName,
    string ShortName,
    string NationalityCode,
    int BirthGameYear,
    PreferredFoot PreferredFoot,
    PlayerPosition PrimaryPosition,
    IReadOnlyList<PlayerPosition> SecondaryPositions,
    SquadStateRow State,
    SquadContractRow Contract,
    IReadOnlyList<SquadAvailabilityRow> Availability);

/// <summary>Everything the squad screen reads (master plan §10.3).</summary>
/// <param name="ClubId">The club.</param>
/// <param name="ClubName">The generated club name.</param>
/// <param name="ClubShortName">The club abbreviation.</param>
/// <param name="CountryCode">The club's country code.</param>
/// <param name="SeasonNumber">The season the squad is read against.</param>
/// <param name="GameYear">The season's game year, which is what fixes each player's age (`TIME-3`).</param>
/// <param name="Players">The players, goalkeepers first.</param>
public sealed record SquadSnapshot(
    Guid ClubId,
    string ClubName,
    string ClubShortName,
    string CountryCode,
    int SeasonNumber,
    int GameYear,
    IReadOnlyList<SquadPlayerRow> Players);

/// <summary>One contract in the club's contract list.</summary>
/// <param name="Id">The contract identity.</param>
/// <param name="PlayerId">The contracted player.</param>
/// <param name="PlayerName">The player's full name.</param>
/// <param name="PlayerShortName">The player's abbreviation.</param>
/// <param name="PrimaryPosition">The player's position.</param>
/// <param name="BirthGameYear">The game year the player was born in.</param>
/// <param name="StartSeasonNumber">The first season the contract covers.</param>
/// <param name="EndSeasonNumber">The last season the contract covers.</param>
/// <param name="WeeklyWageMinor">The weekly wage in minor units.</param>
/// <param name="SquadStatus">The player's standing in the squad.</param>
/// <param name="Status">The contract's lifecycle state.</param>
public sealed record ContractRow(
    Guid Id,
    Guid PlayerId,
    string PlayerName,
    string PlayerShortName,
    PlayerPosition PrimaryPosition,
    int BirthGameYear,
    int StartSeasonNumber,
    int EndSeasonNumber,
    long WeeklyWageMinor,
    SquadStatus SquadStatus,
    ContractStatus Status);

/// <summary>Everything the contract list reads (master plan §10.3).</summary>
/// <param name="ClubId">The club the contracts bind players to.</param>
/// <param name="ClubName">The generated club name.</param>
/// <param name="SeasonNumber">The season the remaining terms are counted from.</param>
/// <param name="GameYear">The season's game year, which is what fixes each player's age.</param>
/// <param name="Contracts">The contracts, closest to expiring first.</param>
public sealed record ContractsSnapshot(
    Guid ClubId,
    string ClubName,
    int SeasonNumber,
    int GameYear,
    IReadOnlyList<ContractRow> Contracts);

/// <summary>Everything the player profile reads (master plan §10.3, §11.1).</summary>
/// <param name="Id">The player identity.</param>
/// <param name="ClubId">The club the player is registered to, which is what the ownership check is made against.</param>
/// <param name="FullName">The generated full name.</param>
/// <param name="ShortName">The abbreviation.</param>
/// <param name="NationalityCode">The nationality country code.</param>
/// <param name="BirthGameYear">The game year the player was born in.</param>
/// <param name="BirthDayOfYear">The day of the birth game year.</param>
/// <param name="PreferredFoot">The foot the player favours.</param>
/// <param name="HeightCm">The player's height in centimetres.</param>
/// <param name="WeightKg">The player's weight in kilograms.</param>
/// <param name="PrimaryPosition">The player's position.</param>
/// <param name="SecondaryPositions">The other positions the player covers.</param>
/// <param name="Status">The player's lifecycle state.</param>
/// <param name="Attributes">The player's displayed attributes.</param>
/// <param name="State">The player's state.</param>
/// <param name="Contract">The player's active contract, if any.</param>
/// <param name="Registration">The player's active registration, if any.</param>
/// <param name="Availability">Every open injury and suspension.</param>
/// <param name="SeasonNumber">The season the player is read against.</param>
/// <param name="GameYear">The season's game year, which is what fixes the player's age.</param>
public sealed record PlayerSnapshot(
    Guid Id,
    Guid ClubId,
    string FullName,
    string ShortName,
    string NationalityCode,
    int BirthGameYear,
    int BirthDayOfYear,
    PreferredFoot PreferredFoot,
    int HeightCm,
    int WeightKg,
    PlayerPosition PrimaryPosition,
    IReadOnlyList<PlayerPosition> SecondaryPositions,
    PlayerStatus Status,
    PlayerAttributeSet Attributes,
    SquadStateRow State,
    SquadContractRow? Contract,
    SquadRegistrationRow? Registration,
    IReadOnlyList<SquadAvailabilityRow> Availability,
    int SeasonNumber,
    int GameYear);

/// <summary>
/// The read side of the squad module.
/// </summary>
/// <remarks>
/// <para>
/// Projections, not aggregates: one query per screen, none used to make a decision, and none returning a
/// tracked graph. Keeping them off the write repositories means a screen can change without widening what
/// a command can reach (`MOD-3`).
/// </para>
/// <para>
/// These reads are scoped to the club the caller manages, which is decided by the use cases rather than
/// here: this port answers "what is in this squad", and the caller is responsible for having established
/// that the caller may ask.
/// </para>
/// </remarks>
public interface ISquadQueries
{
    /// <summary>Reads a club's squad, or returns null if the club is unknown.</summary>
    /// <param name="clubId">The club to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SquadSnapshot?> GetSquadAsync(Guid clubId, CancellationToken cancellationToken);

    /// <summary>Reads one player's profile, or returns null if the player is unknown.</summary>
    /// <param name="playerId">The player to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PlayerSnapshot?> GetPlayerAsync(Guid playerId, CancellationToken cancellationToken);

    /// <summary>Reads a club's active contracts, or returns null if the club is unknown.</summary>
    /// <param name="clubId">The club to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ContractsSnapshot?> GetContractsAsync(Guid clubId, CancellationToken cancellationToken);
}
