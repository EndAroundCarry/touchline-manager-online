using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Competition;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Application.Competition.Playoffs;

/// <summary>
/// Draws a season's promotion play-off brackets from the leagues' final tables (`PR-4`, ADR-0087).
/// </summary>
/// <remarks>
/// <para>
/// A pair of leagues is drawn once both have published their thirtieth matchday, so the draw happens in the publication of the second
/// of the two and the semi-finals exist the moment the league season ends. Everything here is idempotent: a pair that already has its
/// ties is left alone, which is what lets the same method be the catch-up for a league that was activated after matchday 30.
/// </para>
/// <para>
/// A pair that has no time left plays no play-off that season. When the semi-final's team sheets have already locked there is no
/// semi-final to schedule, nothing is drawn, and the rollover moves the direct places only (`PR-5`). The rule reads the clock, so a world
/// that was already past matchday 30 when the play-off shipped gets none for that season either, unless it is still before the lock.
/// </para>
/// <para>
/// <see cref="EnsureForDivisionAsync"/> and <see cref="EnsureForCountryAsync"/> stage their work in the caller's transaction, which is the
/// matchday publication's. <see cref="CatchUpAsync"/> is the standalone entry: it takes the publication turn and a transaction of its own.
/// </para>
/// </remarks>
public sealed class PlayoffDraw
{
    private readonly IWorldRepository _world;
    private readonly IMatchdayRepository _matchdays;
    private readonly IPlayoffRepository _playoffs;
    private readonly ICompetitionRepository _competition;
    private readonly PlayoffNotifications _notifications;
    private readonly IAdvisoryLock _locks;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    /// <summary>Initializes the use case.</summary>
    public PlayoffDraw(
        IWorldRepository world,
        IMatchdayRepository matchdays,
        IPlayoffRepository playoffs,
        ICompetitionRepository competition,
        PlayoffNotifications notifications,
        IAdvisoryLock locks,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _world = world;
        _matchdays = matchdays;
        _playoffs = playoffs;
        _competition = competition;
        _notifications = notifications;
        _locks = locks;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <summary>Draws the pairs a division is in, once both leagues of a pair have published matchday 30. Stages; the caller commits.</summary>
    /// <param name="seasonId">The season.</param>
    /// <param name="divisionId">The division whose thirtieth matchday was just published.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many pairs were drawn.</returns>
    public async Task<int> EnsureForDivisionAsync(
        Guid seasonId,
        Guid divisionId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var division = await _world.FindDivisionAsync(divisionId, cancellationToken);

        return division is null
            ? 0
            : await EnsureForCountryAsync(seasonId, division.CountryId, now, cancellationToken);
    }

    /// <summary>Draws every pair of a country that is ready and has none. Stages; the caller commits.</summary>
    /// <param name="seasonId">The season.</param>
    /// <param name="countryId">The country.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many pairs were drawn.</returns>
    public async Task<int> EnsureForCountryAsync(
        Guid seasonId,
        Guid countryId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var season = await _world.FindSeasonByIdAsync(seasonId, cancellationToken);

        if (season is null)
        {
            return 0;
        }

        var kickoffs = SeasonCalendar.PlayoffKickoffs(DateOnly.FromDateTime(season.StartsAt.UtcDateTime));

        // A pair with no time left for its semi-final plays none this season (a late pair, PR-5).
        if (now >= LockOf(kickoffs, WorldRuleSet.PlayoffSemiSlot))
        {
            return 0;
        }

        var divisions = (await _world.ListActiveDivisionsAsync(countryId, cancellationToken))
            .ToDictionary(division => division.Id);

        if (divisions.Count < 2)
        {
            return 0;
        }

        var divisionSeasons = (await _world.ListDivisionSeasonsAsync(seasonId, cancellationToken))
            .ToDictionary(divisionSeason => divisionSeason.DivisionId);

        var drawn = (await _playoffs.ListBySeasonAsync(seasonId, cancellationToken))
            .Where(tie => tie.CountryId == countryId)
            .Select(tie => (tie.ParentDivisionId, tie.FeederDivisionId))
            .ToHashSet();

        var leagues = new List<LeagueStandings>();

        foreach (var division in divisions.Values.OrderBy(division => division.TierNumber).ThenBy(division => division.LeagueNumber))
        {
            if (!divisionSeasons.TryGetValue(division.Id, out var divisionSeason)
                || !await HasPublishedFinalRoundAsync(divisionSeason.Id, cancellationToken))
            {
                continue;
            }

            // Ranked in memory from the tracked rows: the publication that is drawing has just rebuilt this table and not yet saved it.
            var ranked = (await _matchdays.LoadStandingsAsync(divisionSeason.Id, cancellationToken))
                .OrderBy(standing => standing.Rank)
                .Select(standing => standing.ClubId)
                .ToList();

            leagues.Add(new LeagueStandings(
                division.Id,
                division.TierNumber,
                division.LeagueNumber,
                division.ParentDivisionId,
                ranked));
        }

        var pairs = 0;

        foreach (var pair in PlayoffBracket.Draw(Eligible(leagues, divisions.Values))
            .GroupBy(pairing => (pairing.ParentDivisionId, pairing.FeederDivisionId)))
        {
            if (drawn.Contains((pair.Key.ParentDivisionId, (Guid?)pair.Key.FeederDivisionId)))
            {
                continue;
            }

            await DrawPairAsync(season, countryId, [.. pair], divisions, divisionSeasons, kickoffs, now, cancellationToken);

            pairs++;
        }

        return pairs;
    }

    /// <summary>
    /// Draws what a league that was activated after matchday 30 is owed, in a transaction of its own (`PR-4`). Safe to repeat.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many pairs were drawn.</returns>
    public async Task<int> CatchUpAsync(CancellationToken cancellationToken)
    {
        var world = await _world.FindWorldAsync(cancellationToken);

        if (world is null)
        {
            return 0;
        }

        var season = await _world.FindSeasonAsync(world.Id, world.CurrentSeasonNumber, cancellationToken);

        if (season is null || season.Status != SeasonStatus.Active)
        {
            return 0;
        }

        var kickoffs = SeasonCalendar.PlayoffKickoffs(DateOnly.FromDateTime(season.StartsAt.UtcDateTime));

        // Before the season's last matchday there is nothing to draw, and after the semi-final's lock nothing may be: the cheap answers come
        // first, so the periodic pass does not take the publication turn for nothing.
        var now = _clock.UtcNow;

        if (now < season.EndsAt || now >= LockOf(kickoffs, WorldRuleSet.PlayoffSemiSlot))
        {
            return 0;
        }

        await using var turn = await _locks.HoldAsync(AdvisoryLockKey.MatchdayPublication(), cancellationToken);
        await using var transaction = await _unitOfWork.BeginTransactionAsync(TransactionIsolation.Serializable, cancellationToken);

        var drawn = 0;

        foreach (var country in await _world.ListCountriesAsync(world.Id, cancellationToken))
        {
            drawn += await EnsureForCountryAsync(season.Id, country.Id, now, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return drawn;
    }

    /// <summary>The instant a play-off slot's team sheets lock.</summary>
    internal static DateTimeOffset LockOf(IReadOnlyList<DateTimeOffset> kickoffs, int slot) =>
        kickoffs[slot - 1].AddMinutes(-WorldRuleSet.TeamSheetLockMinutes);

    /// <summary>
    /// Keeps the leagues a bracket may be drawn for: a higher league with exactly one active feeder, both with a table long enough, so a
    /// league with two feeders (not yet played) is never drawn half.
    /// </summary>
    private static List<LeagueStandings> Eligible(List<LeagueStandings> ready, IEnumerable<Division> active)
    {
        var feeders = active
            .Where(division => division.ParentDivisionId is not null)
            .GroupBy(division => division.ParentDivisionId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());

        var byDivision = ready.ToDictionary(league => league.DivisionId);

        return
        [
            .. ready.Where(league =>
            {
                var asFeeder = league.ParentDivisionId is { } parent
                    && feeders.GetValueOrDefault(parent) == 1
                    && league.RankedClubIds.Count >= WorldRuleSet.PlayoffSemiAwayPlace
                    && byDivision.TryGetValue(parent, out var upper)
                    && upper.RankedClubIds.Count >= WorldRuleSet.PlayoffHolderPlace;

                var asParent = feeders.GetValueOrDefault(league.DivisionId) == 1
                    && ready.Any(candidate => candidate.ParentDivisionId == league.DivisionId);

                return asFeeder || asParent;
            }),
        ];
    }

    private async Task<bool> HasPublishedFinalRoundAsync(Guid divisionSeasonId, CancellationToken cancellationToken) =>
        (await _matchdays.LoadDivisionMatchdaysAsync(divisionSeasonId, cancellationToken))
            .Any(matchday => matchday.Kind == MatchdayKind.League
                && matchday.RoundNumber == WorldRuleSet.MatchdaysPerSeason
                && matchday.PublicationStatus == MatchdayPublicationStatus.Published);

    private async Task DrawPairAsync(
        Season season,
        Guid countryId,
        IReadOnlyList<PlayoffPairing> pairings,
        Dictionary<Guid, Division> divisions,
        Dictionary<Guid, DivisionSeason> divisionSeasons,
        IReadOnlyList<DateTimeOffset> kickoffs,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var semiPairing = pairings.Single(pairing => pairing.Stage == PlayoffStage.SemiFinal);
        var finalPairing = pairings.Single(pairing => pairing.Stage == PlayoffStage.Final);

        var semi = PlayoffTie.Draw(Guid.CreateVersion7(), season.Id, countryId, semiPairing, now);
        var final = PlayoffTie.Draw(Guid.CreateVersion7(), season.Id, countryId, finalPairing, now);

        // The semi-final is a round of the lower league, hosted in its division-season; the final is added when the semi-final is decided.
        var kickoff = kickoffs[semi.Slot - 1];
        var matchday = Matchday.SchedulePlayoff(
            Guid.CreateVersion7(),
            divisionSeasons[semiPairing.HostDivisionId].Id,
            semi.Slot,
            kickoff,
            now);

        var fixture = Fixture.Schedule(
            Guid.CreateVersion7(),
            matchday.Id,
            semi.HomeClubId,
            semi.AwayClubId!.Value,
            kickoff,
            now);

        semi.AttachFixture(fixture.Id, now);

        _competition.AddMatchday(matchday);
        _competition.AddFixture(fixture);
        _playoffs.Add(semi);
        _playoffs.Add(final);

        await _notifications.NotifyDrawnAsync(
            new PlayoffDrawFact(
                season.WorldId,
                countryId,
                final.ParentDivisionId,
                divisions[final.ParentDivisionId].DisplayName,
                divisions[semiPairing.FeederDivisionId].DisplayName,
                semi,
                fixture.Id,
                kickoff,
                final,
                kickoffs[final.Slot - 1]),
            now,
            cancellationToken);
    }
}
