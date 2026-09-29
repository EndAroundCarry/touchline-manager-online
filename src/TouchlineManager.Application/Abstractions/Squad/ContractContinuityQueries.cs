using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Abstractions.Squad;

/// <summary>One contracted player as the rollover's contract-continuity step reads them (`CON-6`, `CON-8`).</summary>
/// <remarks>
/// The decision inputs the retirement rule, the AI contract policy, and the renewal quote all need, gathered
/// in one server-only projection. <see cref="Potential"/> is class C2 and never leaves the server; it is an
/// input here and never an output.
/// </remarks>
/// <param name="PlayerId">The player.</param>
/// <param name="ContractId">The player's active contract.</param>
/// <param name="EndSeasonNumber">The last season the contract covers.</param>
/// <param name="WeeklyWageMinor">The weekly wage, in minor units.</param>
/// <param name="SquadStatus">The player's standing in the squad.</param>
/// <param name="Family">The player's position family.</param>
/// <param name="IsGoalkeeper">Whether the player counts against the goalkeeper minimum (`SQ-2`).</param>
/// <param name="Age">The player's age in the game year the read was asked for.</param>
/// <param name="Ability">The player's ability, the mean of their attributes, on the 1–20 scale.</param>
/// <param name="Potential">The hidden development ceiling. Class C2: server-only.</param>
/// <param name="MoraleBp">The player's morale in basis points (`TRN-7`).</param>
/// <param name="ConditionBp">The player's condition in basis points (`TRN-5`).</param>
/// <param name="Appearances">Matches played this season.</param>
/// <param name="RetirementAnnounced">Whether the player has already announced a retirement.</param>
public sealed record ContractContinuityPlayer(
    Guid PlayerId,
    Guid ContractId,
    int EndSeasonNumber,
    long WeeklyWageMinor,
    SquadStatus SquadStatus,
    PositionFamily Family,
    bool IsGoalkeeper,
    int Age,
    int Ability,
    int Potential,
    int MoraleBp,
    int ConditionBp,
    int Appearances,
    bool RetirementAnnounced);

/// <summary>One club's contracted squad as the contract-continuity step reads it (`CON-6`).</summary>
/// <param name="ClubId">The club.</param>
/// <param name="ClubName">The club's generated name.</param>
/// <param name="CountryCode">The club's country code, needed to generate an emergency replacement.</param>
/// <param name="NamePoolKey">The club's country name pool key, needed to generate a replacement.</param>
/// <param name="TierNumber">The tier the club plays in.</param>
/// <param name="AttentiveManager">
/// Whether a manager with an <em>active</em> tenure holds the club. False means the club is unmanaged or its
/// manager is inactive, so the AI renews on its behalf.
/// </param>
/// <param name="Players">The club's contracted players.</param>
public sealed record ContractContinuityClub(
    Guid ClubId,
    string ClubName,
    string CountryCode,
    string NamePoolKey,
    int TierNumber,
    bool AttentiveManager,
    IReadOnlyList<ContractContinuityPlayer> Players);

/// <summary>
/// The read the rollover's contract-continuity step makes (`CON-6`, `CON-8`).
/// </summary>
/// <remarks>
/// A projection, not a tracked graph: the decision inputs are gathered once, and the entities that are then
/// closed and re-signed are loaded tracked through <see cref="ISquadRepository"/>, so the read never widens
/// what a write can reach.
/// </remarks>
public interface IContractContinuityQueries
{
    /// <summary>Reads every club's contracted squad for the season about to be played.</summary>
    /// <param name="worldId">The world.</param>
    /// <param name="seasonId">The closing season, which places each club in its current tier.</param>
    /// <param name="gameYear">
    /// The game year the players' ages are measured against, which is the year they are entering.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<ContractContinuityClub>> LoadAsync(
        Guid worldId,
        Guid seasonId,
        int gameYear,
        CancellationToken cancellationToken);
}
