namespace TouchlineManager.Contracts.World;

/// <summary>The world a manager is onboarding into (master plan §10.2).</summary>
/// <param name="Id">The world identity.</param>
/// <param name="Name">The world's display name.</param>
/// <param name="Status">The lifecycle state, as a stable lowercase code.</param>
/// <param name="RuleSetVersion">The rule-set version in force.</param>
/// <param name="CurrentSeasonNumber">The season currently running.</param>
/// <param name="AcceptsClaims">Whether onboarding is currently open.</param>
/// <param name="CurrentSeason">The running season, or null before one is scheduled.</param>
/// <param name="ServerTime">The server's current instant, so client clock drift is visible.</param>
public sealed record WorldResponse(
    Guid Id,
    string Name,
    string Status,
    string RuleSetVersion,
    int CurrentSeasonNumber,
    bool AcceptsClaims,
    SeasonSummaryResponse? CurrentSeason,
    DateTimeOffset ServerTime);

/// <summary>One season's real-time window (`CAL-2`, `CAL-6`).</summary>
/// <param name="Id">The season identity.</param>
/// <param name="SequenceNumber">The season's ordinal within the world, starting at 1.</param>
/// <param name="DisplayLabel">The label managers see, e.g. <c>2026/27</c>.</param>
/// <param name="GameYear">The game year. Aging and contract years advance from here (`TIME-3`).</param>
/// <param name="Status">The lifecycle state, as a stable lowercase code.</param>
/// <param name="RuleSetVersion">The rule-set version this season is interpreted against.</param>
/// <param name="StartsAt">Kickoff of matchday 1.</param>
/// <param name="EndsAt">Kickoff of the final matchday.</param>
/// <param name="RolloverEndsAt">When the rollover period closes.</param>
public sealed record SeasonSummaryResponse(
    Guid Id,
    int SequenceNumber,
    string DisplayLabel,
    int GameYear,
    string Status,
    string RuleSetVersion,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateTimeOffset RolloverEndsAt);

/// <summary>One of the world's countries, as offered during onboarding.</summary>
/// <param name="Id">The country identity.</param>
/// <param name="Code">The stable three-letter code.</param>
/// <param name="DisplayName">The name shown to managers.</param>
/// <param name="Locale">The locale for formatting, e.g. <c>en-GB</c>.</param>
/// <param name="SortOrder">The presentation order.</param>
public sealed record CountrySummaryResponse(
    Guid Id,
    string Code,
    string DisplayName,
    string Locale,
    int SortOrder);

/// <summary>
/// How full a country's pyramid is, measured at its lowest active tier (`WORLD-8`, `PYR-1`).
/// </summary>
/// <param name="CountryId">The country being measured.</param>
/// <param name="LowestActiveTier">The tier a new manager may join.</param>
/// <param name="LowestActiveTierDivisionId">The division row for that tier.</param>
/// <param name="LowestActiveTierName">The generated name of that tier.</param>
/// <param name="ClubsInLowestTier">How many clubs the tier holds.</param>
/// <param name="HumanOccupiedClubs">How many hold an open human tenure, including inactive ones (`OCC-8`).</param>
/// <param name="AvailableClubs">How many clubs a manager could take over right now.</param>
/// <param name="LowestTierIsFull">Whether every club is held by a human.</param>
/// <param name="TargetTierForExpansion">The tier that would be created if the pyramid grows (`PYR-11`).</param>
/// <param name="Provisioning">The state of the next tier's generation, when one is under way.</param>
/// <param name="ServerTime">The server's current instant.</param>
public sealed record CountryCapacityResponse(
    Guid CountryId,
    int LowestActiveTier,
    Guid LowestActiveTierDivisionId,
    string LowestActiveTierName,
    int ClubsInLowestTier,
    int HumanOccupiedClubs,
    int AvailableClubs,
    bool LowestTierIsFull,
    int TargetTierForExpansion,
    ProvisioningStatusResponse? Provisioning,
    DateTimeOffset ServerTime);

/// <summary>
/// The state of a country's next-tier generation (`PYR-2`, `PYR-10`).
/// </summary>
/// <remarks>
/// Exposed so onboarding can poll instead of guessing, and so an operator can see why a country is
/// refusing claims. Nothing here identifies a manager or a club.
/// </remarks>
/// <param name="RequestId">The provisioning request identity.</param>
/// <param name="TargetTier">The tier being created.</param>
/// <param name="Status">The request state, as a stable lowercase code.</param>
/// <param name="RequestedAt">When the request was recorded.</param>
/// <param name="StartedAt">When a worker began generating, if it has.</param>
/// <param name="CompletedAt">When generation finished, if it has.</param>
/// <param name="PollAfterSeconds">How long the client should wait before asking again.</param>
public sealed record ProvisioningStatusResponse(
    Guid RequestId,
    int TargetTier,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int PollAfterSeconds);

/// <summary>The clubs a manager may take over in a country's lowest active tier.</summary>
/// <param name="CountryId">The country.</param>
/// <param name="DivisionId">The lowest active tier's division.</param>
/// <param name="DivisionName">The tier's generated name.</param>
/// <param name="TierNumber">The tier number.</param>
/// <param name="Clubs">The clubs, in a stable order. Availability is per club.</param>
/// <param name="ServerTime">The server's current instant.</param>
public sealed record AvailableClubsResponse(
    Guid CountryId,
    Guid DivisionId,
    string DivisionName,
    int TierNumber,
    IReadOnlyList<AvailableClubResponse> Clubs,
    DateTimeOffset ServerTime);

/// <summary>One club offered to a manager, with everything needed to choose between two of them.</summary>
/// <param name="Id">The club identity.</param>
/// <param name="Name">The generated club name.</param>
/// <param name="ShortName">The abbreviation used in tables.</param>
/// <param name="City">The generated home city.</param>
/// <param name="Region">The generated region.</param>
/// <param name="BadgeSeed">The seed a procedurally drawn badge is derived from. No real mark is involved.</param>
/// <param name="Reputation">The club's reputation, on the 1–100 scale.</param>
/// <param name="StadiumBaseline">The fixed stadium baseline used by gate revenue (`FIN-3`).</param>
/// <param name="IsAvailable">Whether the club is currently AI-controlled and claimable.</param>
public sealed record AvailableClubResponse(
    Guid Id,
    string Name,
    string ShortName,
    string City,
    string Region,
    string BadgeSeed,
    int Reputation,
    long StadiumBaseline,
    bool IsAvailable);

/// <summary>A manager profile (master plan §10.2).</summary>
/// <param name="Id">The manager identity.</param>
/// <param name="Reputation">The manager's reputation, on the 1–100 scale.</param>
/// <param name="TakeoverCooldownUntil">When the post-resignation cooldown lapses, if one is running (`OCC-4`).</param>
/// <param name="Locale">The preferred locale.</param>
/// <param name="TimeZone">The IANA time zone.</param>
/// <param name="Version">The concurrency version.</param>
public sealed record ManagerProfileResponse(
    Guid Id,
    int Reputation,
    DateTimeOffset? TakeoverCooldownUntil,
    string Locale,
    string TimeZone,
    long Version);

/// <summary>A manager's current control of a club.</summary>
/// <param name="Id">The tenure identity.</param>
/// <param name="ClubId">The controlled club.</param>
/// <param name="ClubName">The club's name.</param>
/// <param name="ClubShortName">The club's abbreviation.</param>
/// <param name="CountryDisplayName">The country the club plays in.</param>
/// <param name="DivisionName">The tier the club plays in.</param>
/// <param name="TierNumber">The tier number.</param>
/// <param name="ControlStatus">The control state: <c>active</c> or <c>inactive</c> (`OCC-2`).</param>
/// <param name="StartedAt">When control began.</param>
/// <param name="LastActiveAt">When the manager was last seen.</param>
/// <param name="Version">The concurrency version.</param>
public sealed record ClubTenureSummaryResponse(
    Guid Id,
    Guid ClubId,
    string ClubName,
    string ClubShortName,
    string CountryDisplayName,
    string DivisionName,
    int TierNumber,
    string ControlStatus,
    DateTimeOffset StartedAt,
    DateTimeOffset LastActiveAt,
    long Version);

/// <summary>
/// Where an account stands in onboarding: no profile, a profile but no club, or a club.
/// </summary>
/// <remarks>
/// One read answers the whole routing question, which is why it is one response rather than three. A
/// client that had to ask "is there a profile?" and then "is there a tenure?" would render a decision it
/// could have been told, and would have to decide what a half-completed state means.
/// </remarks>
/// <param name="Manager">The account's manager profile, or null if it has not been created yet.</param>
/// <param name="Tenure">The club the manager controls, or null between tenures.</param>
/// <param name="ServerTime">The server's current instant.</param>
public sealed record OnboardingStateResponse(
    ManagerProfileResponse? Manager,
    ClubTenureSummaryResponse? Tenure,
    DateTimeOffset ServerTime);

/// <summary>
/// The club a manager has just taken over — the inherited-club dashboard summary (master plan §7.6).
/// </summary>
/// <remarks>
/// <para>
/// A takeover inherits the club exactly as it exists (`WORLD-9`). This response is what "as it exists"
/// means at this stage of the build: identity, competition placement, control, and money. The squad,
/// contracts, fixtures, and history it also mentions join this summary in Stages 4, 6, and 9 rather than
/// being invented here as empty fields.
/// </para>
/// </remarks>
/// <param name="Club">The club itself.</param>
/// <param name="Country">The country the club plays in.</param>
/// <param name="Division">The tier the club plays in.</param>
/// <param name="Season">The season in progress.</param>
/// <param name="Control">Who controls the club, and since when.</param>
/// <param name="Finances">The club's money.</param>
/// <param name="ServerTime">The server's current instant.</param>
public sealed record ClubDashboardResponse(
    ClubSummaryResponse Club,
    CountrySummaryResponse Country,
    DivisionSummaryResponse Division,
    SeasonSummaryResponse Season,
    ClubControlResponse Control,
    ClubFinancesResponse Finances,
    DateTimeOffset ServerTime);

/// <summary>A club's identity and standing as presented to a manager.</summary>
/// <param name="Id">The club identity.</param>
/// <param name="Name">The generated name.</param>
/// <param name="ShortName">The abbreviation used in tables.</param>
/// <param name="Slug">The URL-safe identifier.</param>
/// <param name="City">The generated home city.</param>
/// <param name="Region">The generated region.</param>
/// <param name="BadgeSeed">The badge seed. No real mark is involved (`FIC-3`).</param>
/// <param name="FoundingGameYear">The game year the club was founded in.</param>
/// <param name="Status">The lifecycle state, as a stable lowercase code.</param>
/// <param name="Reputation">The club's reputation, on the 1–100 scale.</param>
/// <param name="StadiumBaseline">The fixed stadium baseline (`FIN-3`).</param>
public sealed record ClubSummaryResponse(
    Guid Id,
    string Name,
    string ShortName,
    string Slug,
    string City,
    string Region,
    string BadgeSeed,
    int FoundingGameYear,
    string Status,
    int Reputation,
    long StadiumBaseline);

/// <summary>A tier of a country's pyramid.</summary>
/// <param name="Id">The division identity.</param>
/// <param name="TierNumber">The tier number. Tier 1 is the top (`WORLD-4`).</param>
/// <param name="DisplayName">The generated, unbranded tier name.</param>
/// <param name="Status">The lifecycle state, as a stable lowercase code.</param>
/// <param name="Capacity">How many clubs the tier holds.</param>
public sealed record DivisionSummaryResponse(
    Guid Id,
    int TierNumber,
    string DisplayName,
    string Status,
    int Capacity);

/// <summary>Who controls a club.</summary>
/// <remarks>
/// <c>ai</c> means no human holds the club. It is derived from tenures rather than stored on the club
/// (`WORLD-7`), so a club returning to AI control does not change its own row.
/// </remarks>
/// <param name="Status">The control state: <c>ai</c>, <c>active</c>, or <c>inactive</c>.</param>
/// <param name="TenureId">The tenure, when a human holds the club.</param>
/// <param name="StartedAt">When the current tenure began, if there is one.</param>
/// <param name="LastActiveAt">When the manager was last seen, if there is a tenure.</param>
public sealed record ClubControlResponse(
    string Status,
    Guid? TenureId,
    DateTimeOffset? StartedAt,
    DateTimeOffset? LastActiveAt);

/// <summary>A club's money, in minor units of the single canonical currency (`FIN-1`).</summary>
/// <param name="CashMinor">The club's cash.</param>
/// <param name="ReservedMinor">The part already committed to open bids.</param>
/// <param name="AvailableMinor">What the club can still commit (`FIN-10`).</param>
public sealed record ClubFinancesResponse(
    long CashMinor,
    long ReservedMinor,
    long AvailableMinor);
