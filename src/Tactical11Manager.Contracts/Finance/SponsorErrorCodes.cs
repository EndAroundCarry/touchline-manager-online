namespace Tactical11Manager.Contracts.Finance;

/// <summary>Stable machine-readable error codes for the sponsor surface (`SPN-*`, `OBJ-*`).</summary>
public static class SponsorErrorCodes
{
    /// <summary>The request named an offer that does not exist.</summary>
    public const string UnknownOffer = "UNKNOWN_SPONSOR_OFFER";

    /// <summary>The request named an objective that does not exist.</summary>
    public const string UnknownObjective = "UNKNOWN_SEASON_OBJECTIVE";

    /// <summary>The club has no offers this season.</summary>
    public const string NoOffers = "NO_SPONSOR_OFFERS";

    /// <summary>The club has signed and may no longer choose.</summary>
    public const string AlreadySigned = "SPONSOR_ALREADY_SIGNED";
}
