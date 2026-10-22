namespace Tactical11Manager.Contracts.World;

/// <summary>
/// Stable machine-readable error codes for the snack bar and the merchandise shop (`STRC-*`).
/// </summary>
/// <remarks>
/// The authorization refusals are the world and squad codes, because a structures read refuses for the same
/// reasons a squad read does. These are the ones an upgrade order adds.
/// </remarks>
public static class StructureErrorCodes
{
    /// <summary>The building is already at its highest level (`STRC-1`).</summary>
    public const string MaxLevel = "STRUCTURE_MAX_LEVEL";

    /// <summary>The building is already being upgraded; one upgrade runs at a time (`STRC-3`).</summary>
    public const string Building = "STRUCTURE_BUILDING";

    /// <summary>The club cannot pay for the upgrade, after what it has already committed to bids (`FIN-10`).</summary>
    public const string InsufficientFunds = "INSUFFICIENT_FUNDS";

    /// <summary>The club is in the red or in administration, so it may not build until its cash is back above zero (`DEBT-3`).</summary>
    public const string BuildEmbargo = "BUILD_EMBARGO";

    /// <summary>The route named a building that does not exist.</summary>
    public const string UnknownKind = "STRUCTURE_KIND_UNKNOWN";
}
