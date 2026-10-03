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

/// <summary>
/// A player's own season statistics for the club they play for, as the profile reads them (`STA-2`).
/// </summary>
/// <remarks>
/// The player's line is the same projection the division leaderboard reads, narrowed to one player: it is
/// advanced by the matchday publication and never recomputed by the read, so the profile and the leaderboard
/// cannot disagree about a player's season. The average is a function of the stored sum and count, exactly
/// as the division row's is, so the storage unit is converted in one place on the way out (`TRN-8`).
/// </remarks>
/// <param name="Appearances">Matches the player took the pitch in.</param>
/// <param name="Starts">Matches the player started.</param>
/// <param name="MinutesPlayed">Total minutes played.</param>
/// <param name="Goals">Goals scored.</param>
/// <param name="Assists">Goals set up.</param>
/// <param name="Shots">Shots taken.</param>
/// <param name="ShotsOnTarget">Shots on target.</param>
/// <param name="Saves">Saves made.</param>
/// <param name="PassesAttempted">Passes attempted.</param>
/// <param name="PassesCompleted">Passes that found a teammate.</param>
/// <param name="DribblesAttempted">Take-ons attempted.</param>
/// <param name="DribblesCompleted">Take-ons won.</param>
/// <param name="YellowCards">Bookings accumulated (`DIS-2`).</param>
/// <param name="RedCards">Sendings-off accumulated (`DIS-4`).</param>
/// <param name="AverageRatingBasisPoints">The average match rating in basis points, or null before the player is rated (`STA-5`).</param>
public sealed record SquadSeasonStatRow(
    int Appearances,
    int Starts,
    int MinutesPlayed,
    int Goals,
    int Assists,
    int Shots,
    int ShotsOnTarget,
    int Saves,
    int PassesAttempted,
    int PassesCompleted,
    int DribblesAttempted,
    int DribblesCompleted,
    int YellowCards,
    int RedCards,
    int? AverageRatingBasisPoints);

/// <summary>One season of a player's career, as the profile's career section reads it (`STA-2`).</summary>
/// <param name="SeasonNumber">The season's ordinal in the world.</param>
/// <param name="SeasonLabel">The season's display label.</param>
/// <param name="ClubId">The club the player appeared for that season.</param>
/// <param name="ClubName">The club's generated name.</param>
/// <param name="Stats">The player's line for that season at that club.</param>
public sealed record SquadCareerSeasonRow(
    int SeasonNumber,
    string SeasonLabel,
    Guid ClubId,
    string ClubName,
    SquadSeasonStatRow Stats);

/// <summary>
/// A player's whole career: the totals across every season and each season's own line.
/// </summary>
/// <remarks>
/// A pure aggregation over <c>competition.player_season_stats</c>, whose rows survive rollover, so a career is
/// nothing more than the same projection the leaderboard reads, summed. The rating is recomputed from the
/// summed basis points and rated appearances rather than averaged from the season averages, so a season with
/// more rated games carries the weight it should (`TRN-8`).
/// </remarks>
/// <param name="Totals">The summed totals across every season the player has appeared in.</param>
/// <param name="SeasonsPlayed">How many distinct seasons the player has appeared in.</param>
/// <param name="Seasons">Each season's line, most recent first.</param>
public sealed record SquadCareer(
    SquadSeasonStatRow Totals,
    int SeasonsPlayed,
    IReadOnlyList<SquadCareerSeasonRow> Seasons);

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
/// <param name="SeasonStat">The player's own season line for the club they play for, or null before they have appeared (`STA-2`).</param>
/// <param name="Career">The player's whole career across seasons, or null before they have ever appeared (`STA-2`).</param>
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
    SquadSeasonStatRow? SeasonStat,
    SquadCareer? Career,
    int SeasonNumber,
    int GameYear);

/// <summary>
/// Everything a renewal quote is derived from, gathered server-side (`CON-3`).
/// </summary>
/// <remarks>
/// A dedicated projection rather than reusing the player profile, because the profile deliberately omits the
/// hidden potential the quote needs: this read is server-only and never crosses the wire. The contract's
/// version rides along so the renewal can be conditional without a second read.
/// </remarks>
/// <param name="ContractId">The contract being renewed.</param>
/// <param name="PlayerId">The contracted player.</param>
/// <param name="ClubId">The club the contract binds the player to, which ownership is checked against.</param>
/// <param name="ContractVersion">The contract's optimistic-concurrency version.</param>
/// <param name="SquadStatus">The player's standing in the squad, which the new contract keeps.</param>
/// <param name="Ability">The player's current ability, the mean of their attributes.</param>
/// <param name="Potential">The hidden development ceiling. Class C2: server-only.</param>
/// <param name="Age">The player's age in game years.</param>
/// <param name="Appearances">Matches played this season.</param>
/// <param name="MoraleBp">The player's morale in basis points (`TRN-7`).</param>
/// <param name="TierNumber">The tier the club plays in.</param>
/// <param name="CurrentSeasonNumber">The season the new contract would begin in.</param>
/// <param name="RemainingSeasons">Full seasons left on the current contract after the current one.</param>
public sealed record ContractRenewalContext(
    Guid ContractId,
    Guid PlayerId,
    Guid ClubId,
    long ContractVersion,
    SquadStatus SquadStatus,
    int Ability,
    int Potential,
    int Age,
    int Appearances,
    int MoraleBp,
    int TierNumber,
    int CurrentSeasonNumber,
    int RemainingSeasons);

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

    /// <summary>
    /// Reads the server-only context a renewal quote is derived from, or null when the contract is unknown or
    /// no longer active (`CON-3`).
    /// </summary>
    /// <param name="contractId">The contract to quote.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ContractRenewalContext?> GetRenewalContextAsync(
        Guid contractId,
        CancellationToken cancellationToken);
}
