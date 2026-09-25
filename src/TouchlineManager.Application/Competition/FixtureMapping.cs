using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Squad;
using TouchlineManager.Contracts.Competition;
using TouchlineManager.Domain.Competition;

namespace TouchlineManager.Application.Competition;

/// <summary>
/// Maps the competition read snapshots to their transport projections.
/// </summary>
/// <remarks>
/// <para>
/// One place, so a fixture cannot be shaped differently by the calendar and by the club's own list. The
/// score is published only from a <see cref="FixtureStatus.Published"/> fixture, never from a staged one:
/// a matchday publishes as a unit (`MAT-7`, `CAL-10`), and a staged score leaking through a read would be
/// one of the nine results appearing before all of them are meant to.
/// </para>
/// <para>
/// The next fixture is the first one not yet published and not void, in round order. Voiding is an
/// operator repair rather than something a club plays, so it is skipped rather than reported as next.
/// </para>
/// </remarks>
public static class FixtureMapping
{
    /// <summary>Carries the access verdict over into a competition read outcome.</summary>
    /// <param name="outcome">The access verdict.</param>
    public static CompetitionReadOutcome ToCompetitionOutcome(this ClubAccessOutcome outcome) => outcome switch
    {
        ClubAccessOutcome.Granted => CompetitionReadOutcome.Found,
        ClubAccessOutcome.WorldNotSeeded => CompetitionReadOutcome.WorldNotSeeded,
        ClubAccessOutcome.NoManagerProfile => CompetitionReadOutcome.NoManagerProfile,
        ClubAccessOutcome.NoClub => CompetitionReadOutcome.NoClub,
        ClubAccessOutcome.ClubNotManaged => CompetitionReadOutcome.ClubNotManaged,
        _ => CompetitionReadOutcome.ClubNotFound,
    };

    /// <summary>Projects a division's stored table.</summary>
    /// <param name="snapshot">The stored table.</param>
    /// <param name="serverTime">When the response was produced.</param>
    public static DivisionTableResponse ToResponse(
        this DivisionTableSnapshot snapshot,
        DateTimeOffset serverTime)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new DivisionTableResponse(
            snapshot.DivisionId,
            snapshot.DivisionName,
            snapshot.TierNumber,
            snapshot.CountryId,
            snapshot.CountryCode,
            snapshot.CountryName,
            snapshot.SeasonNumber,
            snapshot.SeasonLabel,
            [.. snapshot.Rows.Select(ToResponse)],
            serverTime);
    }

    /// <summary>Projects one table row.</summary>
    /// <param name="row">The row.</param>
    public static DivisionTableRowResponse ToResponse(this DivisionTableRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new DivisionTableRowResponse(
            row.Rank,
            row.ClubId,
            row.ClubName,
            row.ClubShortName,
            row.Played,
            row.Won,
            row.Drawn,
            row.Lost,
            row.GoalsFor,
            row.GoalsAgainst,
            // Derived here rather than stored, so a row cannot disagree with the two columns it is a
            // function of, and the client does not have to know the rule (TBL-3).
            row.GoalsFor - row.GoalsAgainst,
            row.Points,
            row.YellowCards,
            row.RedCards);
    }

    /// <summary>Projects a division's whole fixture calendar.</summary>
    /// <param name="snapshot">The stored calendar.</param>
    /// <param name="serverTime">When the response was produced.</param>
    public static DivisionFixturesResponse ToResponse(
        this DivisionFixturesSnapshot snapshot,
        DateTimeOffset serverTime)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new DivisionFixturesResponse(
            snapshot.DivisionId,
            snapshot.DivisionName,
            snapshot.TierNumber,
            snapshot.CountryId,
            snapshot.CountryCode,
            snapshot.CountryName,
            snapshot.SeasonNumber,
            snapshot.SeasonLabel,
            [.. snapshot.Clubs.Select(club => new FixtureClubResponse(club.Id, club.Name, club.ShortName))],
            [.. snapshot.Matchdays.Select(ToResponse)],
            serverTime);
    }

    /// <summary>Projects one round and its fixtures.</summary>
    /// <param name="matchday">The stored round.</param>
    public static FixtureMatchdayResponse ToResponse(this FixtureMatchdayRow matchday)
    {
        ArgumentNullException.ThrowIfNull(matchday);

        return new FixtureMatchdayResponse(
            matchday.Id,
            matchday.RoundNumber,
            matchday.LockAt,
            matchday.KickoffAt,
            matchday.PublicationStatus.ToCode(),
            [.. matchday.Fixtures.Select(fixture => fixture.ToResponse(matchday.Id, matchday.RoundNumber))]);
    }

    /// <summary>Projects one fixture as a calendar shows it.</summary>
    /// <param name="fixture">The stored fixture.</param>
    /// <param name="matchdayId">The round the fixture belongs to.</param>
    /// <param name="roundNumber">The round number.</param>
    public static FixtureSummaryResponse ToResponse(
        this FixtureRow fixture,
        Guid matchdayId,
        int roundNumber)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var (home, away, matchId) = Visible(fixture.Status, fixture.HomeScore, fixture.AwayScore, fixture.MatchId);

        return new FixtureSummaryResponse(
            fixture.Id,
            matchdayId,
            roundNumber,
            fixture.HomeClubId,
            fixture.AwayClubId,
            fixture.KickoffAt,
            fixture.Status.ToCode(),
            home,
            away,
            matchId);
    }

    /// <summary>Projects a club's own fixture list, naming its next fixture.</summary>
    /// <param name="snapshot">The stored list.</param>
    /// <param name="serverTime">When the response was produced.</param>
    public static MyFixturesResponse ToResponse(
        this ClubFixturesSnapshot snapshot,
        DateTimeOffset serverTime)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var fixtures = snapshot.Fixtures
            .Select(ToResponse)
            .ToList();

        var next = snapshot.Fixtures.FirstOrDefault(
            fixture => fixture.Status is not (FixtureStatus.Published or FixtureStatus.Void));

        return new MyFixturesResponse(
            snapshot.ClubId,
            snapshot.ClubName,
            snapshot.ClubShortName,
            snapshot.DivisionId,
            snapshot.DivisionName,
            snapshot.TierNumber,
            snapshot.SeasonNumber,
            snapshot.SeasonLabel,
            next?.Id,
            fixtures,
            serverTime);
    }

    /// <summary>Projects one of a club's fixtures from that club's point of view.</summary>
    /// <param name="fixture">The stored fixture.</param>
    public static ClubFixtureResponse ToResponse(this ClubFixtureRow fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var (home, away, matchId) = Visible(fixture.Status, fixture.HomeScore, fixture.AwayScore, fixture.MatchId);

        return new ClubFixtureResponse(
            fixture.Id,
            fixture.RoundNumber,
            fixture.IsHome ? "home" : "away",
            fixture.OpponentClubId,
            fixture.OpponentName,
            fixture.OpponentShortName,
            fixture.KickoffAt,
            fixture.LockAt,
            fixture.Status.ToCode(),
            home,
            away,
            matchId,
            Outcome(fixture));
    }

    /// <summary>Projects a fixture in full, resolving the caller's side when they hold one of the clubs.</summary>
    /// <param name="snapshot">The stored fixture.</param>
    /// <param name="managedClubId">The club the caller holds among the two, or null when they hold neither.</param>
    /// <param name="serverTime">When the response was produced.</param>
    public static FixtureDetailResponse ToResponse(
        this FixtureDetailSnapshot snapshot,
        Guid? managedClubId,
        DateTimeOffset serverTime)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var (home, away, matchId) = Visible(
            snapshot.Status,
            snapshot.HomeScore,
            snapshot.AwayScore,
            snapshot.MatchId);

        var side = managedClubId is not { } clubId
            ? null
            : snapshot.Home.ClubId == clubId ? "home" : "away";

        return new FixtureDetailResponse(
            snapshot.Id,
            snapshot.MatchdayId,
            snapshot.DivisionId,
            snapshot.DivisionName,
            snapshot.TierNumber,
            snapshot.CountryCode,
            snapshot.RoundNumber,
            snapshot.LockAt,
            snapshot.KickoffAt,
            snapshot.Status.ToCode(),
            IsLocked(snapshot.Status, snapshot.LockAt, serverTime),
            side is null ? null : managedClubId,
            side,
            new FixtureSideResponse(
                snapshot.Home.ClubId,
                snapshot.Home.Name,
                snapshot.Home.ShortName,
                snapshot.Home.City,
                snapshot.Home.Region),
            new FixtureSideResponse(
                snapshot.Away.ClubId,
                snapshot.Away.Name,
                snapshot.Away.ShortName,
                snapshot.Away.City,
                snapshot.Away.Region),
            home,
            away,
            matchId,
            serverTime);
    }

    /// <summary>
    /// Whether a fixture's team sheet can no longer be changed (`CAL-3`, `SQ-7`).
    /// </summary>
    /// <remarks>
    /// Both halves are needed. The status is the lock job's own record, and the deadline is what makes a
    /// delayed job harmless: a sheet is closed at its deadline whether or not the job that writes the
    /// status has run yet, because the deadline is a rule and the status is bookkeeping.
    /// </remarks>
    /// <param name="status">The fixture's lifecycle state.</param>
    /// <param name="lockAt">The sheet deadline.</param>
    /// <param name="now">The current instant.</param>
    public static bool IsLocked(FixtureStatus status, DateTimeOffset lockAt, DateTimeOffset now) =>
        status != FixtureStatus.Scheduled || now >= lockAt;

    /// <summary>
    /// The score a manager may see, which is none until the whole matchday publishes (`MAT-7`, `CAL-10`).
    /// </summary>
    /// <param name="status">The fixture's lifecycle state.</param>
    /// <param name="homeScore">The stored home score.</param>
    /// <param name="awayScore">The stored away score.</param>
    /// <param name="matchId">The stored match.</param>
    public static (int? Home, int? Away, Guid? MatchId) Visible(
        FixtureStatus status,
        int? homeScore,
        int? awayScore,
        Guid? matchId) =>
        status == FixtureStatus.Published
            ? (homeScore, awayScore, matchId)
            : (null, null, null);

    /// <summary>The managed club's result for a published fixture, or null while there is not one.</summary>
    /// <param name="fixture">The stored fixture, from the managed club's point of view.</param>
    public static string? Outcome(ClubFixtureRow fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        if (fixture.Status != FixtureStatus.Published
            || fixture.HomeScore is not { } home
            || fixture.AwayScore is not { } away)
        {
            return null;
        }

        if (home == away)
        {
            return "draw";
        }

        var managedScoredMore = fixture.IsHome ? home > away : away > home;

        return managedScoredMore ? "win" : "loss";
    }
}
