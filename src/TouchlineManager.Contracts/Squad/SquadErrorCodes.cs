namespace TouchlineManager.Contracts.Squad;

/// <summary>
/// The stable refusal codes the squad reads return.
/// </summary>
/// <remarks>
/// A client branches on these rather than on prose. They are separate from
/// <c>WorldErrorCodes</c> because they answer a different question: that class refuses a takeover, this
/// one refuses a read of somebody else's squad. <see cref="ClubNotManaged"/> in particular is
/// distinguishable from <see cref="NoClub"/> on purpose — "you manage a different club" and "you manage
/// no club at all" lead a manager to two different screens (master plan §15.4).
/// </remarks>
public static class SquadErrorCodes
{
    /// <summary>The caller holds a club, but not the one they asked about (master plan §10.9).</summary>
    public const string ClubNotManaged = "CLUB_NOT_MANAGED";

    /// <summary>The caller holds no club.</summary>
    public const string NoClub = "NO_CLUB";

    /// <summary>No player exists with the requested identity.</summary>
    public const string PlayerNotFound = "PLAYER_NOT_FOUND";

    /// <summary>No contract exists with the requested identity.</summary>
    public const string ContractNotFound = "CONTRACT_NOT_FOUND";

    /// <summary>No tactical plan exists with the requested identity, or it is not the caller's.</summary>
    public const string PlanNotFound = "PLAN_NOT_FOUND";

    /// <summary>The submitted plan breaks a tactics rule; the response carries the issues (`TAC-7`…`TAC-9`, `SQ-4`).</summary>
    public const string PlanValidationFailed = "PLAN_VALIDATION_FAILED";
}
