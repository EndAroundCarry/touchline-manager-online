using Tactical11Manager.Application.Abstractions.Market;
using Tactical11Manager.Application.Squad;

namespace Tactical11Manager.Application.Market;

/// <summary>
/// What happened when a market use case ran (`TRF-1`…`TRF-15`, `INT-2`).
/// </summary>
/// <remarks>
/// One vocabulary for every market command, mapped to a stable HTTP code by the endpoint, so the same refusal
/// reads the same way on the scouting, listing, and bid surfaces.
/// </remarks>
public enum MarketOutcome
{
    /// <summary>The command or read succeeded.</summary>
    Found = 0,

    /// <summary>No world has been seeded.</summary>
    WorldNotSeeded = 1,

    /// <summary>The account has no manager profile.</summary>
    NoManagerProfile = 2,

    /// <summary>The manager holds no club.</summary>
    NoClub = 3,

    /// <summary>The manager holds a club, but not the one the command named.</summary>
    ClubNotManaged = 4,

    /// <summary>The club, listing, or player does not exist.</summary>
    NotFound = 5,

    /// <summary>The player cannot be listed: not the caller's, or not legally sellable (`TRF-1`, `SQ-2`).</summary>
    NotEligible = 6,

    /// <summary>The player already has an open listing (`TRF-14`).</summary>
    AlreadyListed = 7,

    /// <summary>The listing is no longer open (`TRF-9`, `TRF-15`).</summary>
    NotOpen = 8,

    /// <summary>The bidder is the seller (`TRF-1`).</summary>
    CannotBidOnOwnPlayer = 9,

    /// <summary>The bid does not clear the minimum fee or increment (`TRF-5`).</summary>
    BidTooLow = 10,

    /// <summary>The club does not have the cash after existing reservations (`FIN-10`).</summary>
    InsufficientFunds = 11,

    /// <summary>The idempotency key was reused with a different request (`T-4`).</summary>
    IdempotencyKeyReused = 12,

    /// <summary>The request conflicted with a concurrent change.</summary>
    Conflict = 13,

    /// <summary>The cursor did not decode (master plan §10).</summary>
    InvalidCursor = 14,

    /// <summary>The bidder's squad already holds the maximum, so a free agent has no place (`FA-5`).</summary>
    SquadFull = 15,

    /// <summary>The listing is a free agent's, and only human managers may bid on those (`FA-5`).</summary>
    HumanManagersOnly = 16,

    /// <summary>The bidder is in the red or in administration, so it may not bid (`DEBT-3`).</summary>
    Embargo = 17,

    /// <summary>The listing was opened by the board of a club in administration, so its manager cannot cancel it (`DEBT-4`).</summary>
    BoardListing = 18,
}

/// <summary>A scouting search's result (`SCT-1`).</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Page">The page, when the search ran.</param>
public sealed record SearchPlayersResult(MarketOutcome Outcome, PlayerSearchPage? Page);

/// <summary>A single player read's result (`SCT-1`).</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Player">The player, when they were found.</param>
public sealed record PlayerCardResult(MarketOutcome Outcome, PlayerSearchRow? Player);

/// <summary>A shortlist read's or write's result (`SCT-3`).</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Shortlist">The shortlist after the operation, when it succeeded.</param>
public sealed record ShortlistResult(MarketOutcome Outcome, ShortlistSnapshot? Shortlist);

/// <summary>A listing read's or write's result (`TRF-1`, `TRF-4`).</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Listing">The listing, when the read or write succeeded.</param>
public sealed record ListingResult(MarketOutcome Outcome, ListingRow? Listing);

/// <summary>A listing browse's result (`INT-6`).</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Page">The page, when the browse ran.</param>
public sealed record ListingPageResult(MarketOutcome Outcome, ListingPage? Page);

/// <summary>The caller's own market activity (`TRF-4`, `TRF-6`).</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Activity">The activity, when the read succeeded.</param>
public sealed record MarketActivityResult(MarketOutcome Outcome, MyMarketActivity? Activity);

/// <summary>The public transfer history (`INT-6`).</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Page">The page, when the read ran.</param>
public sealed record TransferHistoryResult(MarketOutcome Outcome, TransferHistoryPage? Page);

/// <summary>Carries the shared access verdict into the market's vocabulary.</summary>
/// <param name="outcome">The access verdict.</param>
public static class MarketOutcomes
{
    /// <summary>Maps a club-access verdict to a market outcome.</summary>
    /// <param name="outcome">The access verdict.</param>
    public static MarketOutcome FromAccess(this ClubAccessOutcome outcome) => outcome switch
    {
        ClubAccessOutcome.Granted => MarketOutcome.Found,
        ClubAccessOutcome.WorldNotSeeded => MarketOutcome.WorldNotSeeded,
        ClubAccessOutcome.NoManagerProfile => MarketOutcome.NoManagerProfile,
        ClubAccessOutcome.NoClub => MarketOutcome.NoClub,
        ClubAccessOutcome.ClubNotManaged => MarketOutcome.ClubNotManaged,
        _ => MarketOutcome.NotFound,
    };
}
