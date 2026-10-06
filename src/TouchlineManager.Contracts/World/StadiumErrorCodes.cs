namespace TouchlineManager.Contracts.World;

/// <summary>
/// Stable machine-readable error codes for the stadium (`STAD-*`).
/// </summary>
/// <remarks>
/// The authorization refusals are the world and squad codes, because a stadium read refuses for the same
/// reasons a squad read does. These two are the ones a build order adds.
/// </remarks>
public static class StadiumErrorCodes
{
    /// <summary>The club cannot pay for the places, after what it has already committed to bids (`FIN-10`).</summary>
    public const string InsufficientFunds = "INSUFFICIENT_FUNDS";

    /// <summary>The order would take the ground past its largest size (`STAD-1`).</summary>
    public const string StadiumFull = "STADIUM_FULL";
}
