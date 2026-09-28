using TouchlineManager.Contracts.Squad;

namespace TouchlineManager.Contracts.Market;

/// <summary>The body of a request to shortlist a player (`SCT-3`).</summary>
/// <param name="Notes">The private note to keep, or null for none.</param>
public sealed record ShortlistRequest(string? Notes);

/// <summary>
/// One player as a scouting result reads them (`SCT-1`).
/// </summary>
/// <remarks>
/// Exact public attributes and nothing else: no hidden potential, reputation, or internal valuation reaches a
/// client (`I-1`), and condition and contract terms — club state — are deliberately absent, because scouting
/// is a public surface across every club (`SCT-1`).
/// </remarks>
/// <param name="PlayerId">The player identity.</param>
/// <param name="FullName">The generated full name.</param>
/// <param name="ShortName">The abbreviation.</param>
/// <param name="NationalityCode">The nationality country code.</param>
/// <param name="Age">The player's age in game years.</param>
/// <param name="PreferredFoot">The foot the player favours.</param>
/// <param name="PrimaryPosition">The player's position.</param>
/// <param name="SecondaryPositions">The other positions the player covers.</param>
/// <param name="ClubId">The club the player plays for, or null when unattached.</param>
/// <param name="ClubName">The club's name, or null.</param>
/// <param name="TierNumber">The club's tier, or null.</param>
/// <param name="DivisionId">The club's division, or null.</param>
/// <param name="DivisionName">The division's name, or null.</param>
/// <param name="IsListed">Whether the player currently has an open listing (`TRF-14`).</param>
/// <param name="Attributes">The player's exact public attributes (`SCT-1`).</param>
public sealed record PlayerSearchResultResponse(
    Guid PlayerId,
    string FullName,
    string ShortName,
    string NationalityCode,
    int Age,
    string PreferredFoot,
    string PrimaryPosition,
    IReadOnlyList<string> SecondaryPositions,
    Guid? ClubId,
    string? ClubName,
    int? TierNumber,
    Guid? DivisionId,
    string? DivisionName,
    bool IsListed,
    PlayerAttributesResponse Attributes);

/// <summary>One page of scouting results (`SCT-1`).</summary>
/// <param name="Players">The players on this page.</param>
/// <param name="NextCursor">The cursor for the next page, or null at the end.</param>
/// <param name="ServerTime">When the response was produced.</param>
public sealed record PlayerSearchResponse(
    IReadOnlyList<PlayerSearchResultResponse> Players,
    string? NextCursor,
    DateTimeOffset ServerTime);

/// <summary>One shortlisted player (`SCT-3`).</summary>
/// <param name="PlayerId">The player identity.</param>
/// <param name="PlayerName">The player's full name.</param>
/// <param name="ShortName">The abbreviation.</param>
/// <param name="PrimaryPosition">The player's position.</param>
/// <param name="Age">The player's age in game years.</param>
/// <param name="Notes">The manager's private note, or null.</param>
/// <param name="IsListed">Whether the player currently has an open listing (`TRF-14`).</param>
public sealed record ShortlistEntryResponse(
    Guid PlayerId,
    string PlayerName,
    string ShortName,
    string PrimaryPosition,
    int Age,
    string? Notes,
    bool IsListed);

/// <summary>A manager's private shortlist (`SCT-3`).</summary>
/// <param name="Entries">The shortlisted players.</param>
/// <param name="ServerTime">When the response was produced.</param>
public sealed record ShortlistResponse(
    IReadOnlyList<ShortlistEntryResponse> Entries,
    DateTimeOffset ServerTime);
