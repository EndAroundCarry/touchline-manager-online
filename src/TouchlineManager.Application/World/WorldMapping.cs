using TouchlineManager.Contracts.World;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Application.World;

/// <summary>
/// Maps world aggregates to their transport projections.
/// </summary>
/// <remarks>
/// One place, so a response shape cannot be assembled differently by two endpoints that both return a
/// club, and so no aggregate type ever reaches the API by accident: everything here returns a
/// <c>Contracts</c> record, and nothing here reads a hidden field.
/// </remarks>
public static class WorldMapping
{
    /// <summary>Projects a season.</summary>
    public static SeasonSummaryResponse ToResponse(this Season season)
    {
        ArgumentNullException.ThrowIfNull(season);

        return new SeasonSummaryResponse(
            season.Id,
            season.SequenceNumber,
            season.DisplayLabel,
            season.GameYear,
            season.Status.ToCode(),
            season.RuleSetVersion,
            season.StartsAt,
            season.EndsAt,
            season.RolloverEndsAt);
    }

    /// <summary>Projects a country.</summary>
    public static CountrySummaryResponse ToResponse(this Country country)
    {
        ArgumentNullException.ThrowIfNull(country);

        return new CountrySummaryResponse(
            country.Id,
            country.Code,
            country.DisplayName,
            country.Locale,
            country.SortOrder);
    }

    /// <summary>Projects a club's identity.</summary>
    public static ClubSummaryResponse ToResponse(this Club club)
    {
        ArgumentNullException.ThrowIfNull(club);

        return new ClubSummaryResponse(
            club.Id,
            club.Name,
            club.ShortName,
            club.Slug,
            club.City,
            club.Region,
            club.BadgeSeed,
            club.FoundingGameYear,
            club.Status.ToCode(),
            club.Reputation,
            club.StadiumBaseline);
    }

    /// <summary>Projects a division.</summary>
    public static DivisionSummaryResponse ToResponse(this Division division)
    {
        ArgumentNullException.ThrowIfNull(division);

        return new DivisionSummaryResponse(
            division.Id,
            division.TierNumber,
            division.DisplayName,
            division.Status.ToCode(),
            division.Capacity);
    }

    /// <summary>Projects a manager profile.</summary>
    public static ManagerProfileResponse ToResponse(this Manager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);

        return new ManagerProfileResponse(
            manager.Id,
            manager.Reputation,
            manager.TakeoverCooldownUntil,
            manager.Locale,
            manager.TimeZone,
            manager.Version);
    }

    /// <summary>Projects a tenure with the club context a manager needs to recognise it.</summary>
    public static ClubTenureSummaryResponse ToSummary(
        this ClubTenure tenure,
        Club club,
        Country country,
        Division division)
    {
        ArgumentNullException.ThrowIfNull(tenure);
        ArgumentNullException.ThrowIfNull(club);
        ArgumentNullException.ThrowIfNull(country);
        ArgumentNullException.ThrowIfNull(division);

        return new ClubTenureSummaryResponse(
            tenure.Id,
            tenure.ClubId,
            club.Name,
            club.ShortName,
            country.DisplayName,
            division.DisplayName,
            division.TierNumber,
            tenure.ControlStatus.ToCode(),
            tenure.StartedAt,
            tenure.LastActiveAt,
            tenure.Version);
    }

    /// <summary>
    /// Projects who controls a club.
    /// </summary>
    /// <remarks>
    /// A club with no open tenure is reported as <c>ai</c> rather than omitted. "No human holds this
    /// club" is a fact a manager needs in order to see it as claimable, and a null in a response is
    /// easy for a client to render as an error state instead of as information.
    /// </remarks>
    public static ClubControlResponse ToControl(this ClubTenure? tenure) =>
        tenure is null
            ? new ClubControlResponse(ClubControlStatusCodes.Ai, TenureId: null, StartedAt: null, LastActiveAt: null)
            : new ClubControlResponse(
                tenure.ControlStatus.ToCode(),
                tenure.Id,
                tenure.StartedAt,
                tenure.LastActiveAt);

    /// <summary>Projects the inherited-club dashboard (`WORLD-9`).</summary>
    /// <param name="snapshot">The read itself.</param>
    /// <param name="serverTime">The server's current instant.</param>
    public static ClubDashboardResponse ToDashboard(
        this Abstractions.World.ClubDashboardSnapshot snapshot,
        DateTimeOffset serverTime)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // A club always has an account once it exists — world generation opens one for every club — so a
        // missing account reads as zero rather than as a failure. Reading a club must not break because
        // one of its parts is absent.
        var cash = snapshot.CashMinor ?? 0;
        var reserved = snapshot.ReservedMinor ?? 0;

        return new ClubDashboardResponse(
            snapshot.Club.ToResponse(),
            snapshot.Country.ToResponse(),
            snapshot.Division.ToResponse(),
            snapshot.Season.ToResponse(),
            snapshot.Tenure.ToControl(),
            new ClubFinancesResponse(cash, reserved, cash - reserved),
            serverTime);
    }
}

/// <summary>The control state a club reports when no human holds it.</summary>
public static class ClubControlStatusCodes
{
    /// <summary>AI control.</summary>
    public const string Ai = "ai";
}
