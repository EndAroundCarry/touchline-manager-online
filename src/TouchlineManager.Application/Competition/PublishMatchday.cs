using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Application.Comms;
using TouchlineManager.Application.Finance;
using TouchlineManager.Application.Match;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Competition;

/// <summary>What publishing a matchday did.</summary>
public enum PublishMatchdayOutcome
{
    /// <summary>The round's results and its table are public.</summary>
    Published = 0,

    /// <summary>The round was already published; nothing was done.</summary>
    AlreadyPublished = 1,

    /// <summary>The matchday does not exist.</summary>
    MatchdayNotFound = 2,

    /// <summary>At least one fixture has no staged result, so nothing was published.</summary>
    NotFullyStaged = 3,
}

/// <summary>The result of publishing a matchday.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Published">How many fixtures became public.</param>
/// <param name="TableRows">How many table rows the division now carries.</param>
public sealed record PublishMatchdayResult(PublishMatchdayOutcome Outcome, int Published, int TableRows);

/// <summary>
/// Publishes a division's round and applies its projections, all or nothing (`MAT-7`, `TBL-13`, §7.4).
/// </summary>
/// <remarks>
/// <para>
/// The whole point of the staged state is this method. Nine results exist privately, and this is the one
/// transaction in which they become public together and the table moves with them. A failure before the
/// commit leaves the world exactly as it was — nine staged results, no published scoreline, an unmoved
/// table — which is what "never publish five of nine" means in practice (§7.4.7).
/// </para>
/// <para>
/// The table is <em>rebuilt</em> rather than incremented: the division's published results are read back
/// and ranked from scratch, so a row cannot drift away from the fixtures it claims to summarise, and an
/// operator repairing a projection runs the same code path the live publication does (`TBL-13`).
/// </para>
/// <para>
/// Publication is where a result reaches the squad, too: the round's cards and injuries become discipline
/// records and absences, each club's open absences are served one fixture (`DIS-1`, `DIS-2`, `DIS-4`,
/// `DIS-5`), and every player who appeared carries the match's load in condition, fatigue, and morale
/// (`TRN-11`, `TRN-13`). It is the publication's transaction and not the simulation's because an effect must
/// not be visible until its result is: a suspension nobody can see yet would still be repaired away by the
/// next lock, while one applied and published together is the fact the rules describe.
/// </para>
/// <para>
/// It is also where a manager hears about it (`F-41`): the result, the club's new position, a booking's
/// suspension, and an injury each become an inbox message, written in this same transaction so the result
/// and the news of it become public together. An AI club has no manager, so it is told nothing — which is
/// the whole of the addressing rule.
/// </para>
/// <para>
/// Publication is serializable. It reads a whole division's results and rewrites its table, and the
/// concurrency rule names matchday publication explicitly: two transactions that both read the table and
/// wrote it in turn would give the second one a table missing the first one's round (`CONC-2`). A
/// serialization failure is a transient job failure, and the retry re-runs the same deterministic work.
/// </para>
/// </remarks>
public sealed class PublishMatchday
{
    private readonly IMatchdayRepository _matchdays;
    private readonly IAvailabilityRepository _availability;
    private readonly IPlayerStateRepository _playerStates;
    private readonly MatchdayNotifications _notifications;
    private readonly MatchdayFinances _finances;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    /// <summary>Initializes the use case.</summary>
    public PublishMatchday(
        IMatchdayRepository matchdays,
        IAvailabilityRepository availability,
        IPlayerStateRepository playerStates,
        MatchdayNotifications notifications,
        MatchdayFinances finances,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _matchdays = matchdays;
        _availability = availability;
        _playerStates = playerStates;
        _notifications = notifications;
        _finances = finances;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <summary>Publishes one matchday.</summary>
    /// <param name="matchdayId">The matchday to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<PublishMatchdayResult> ExecuteAsync(
        Guid matchdayId,
        CancellationToken cancellationToken)
    {
        var workload = await _matchdays.LoadMatchdayAsync(matchdayId, cancellationToken);

        if (workload is null)
        {
            return new PublishMatchdayResult(PublishMatchdayOutcome.MatchdayNotFound, 0, 0);
        }

        if (workload.Matchday.PublicationStatus == MatchdayPublicationStatus.Published)
        {
            return new PublishMatchdayResult(PublishMatchdayOutcome.AlreadyPublished, 0, 0);
        }

        if (!workload.AllFixturesStaged)
        {
            // Refused rather than partly published. The resolver's job will finish the round and ask again.
            return new PublishMatchdayResult(PublishMatchdayOutcome.NotFullyStaged, 0, 0);
        }

        var now = _clock.UtcNow;

        await using var transaction = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.Serializable,
            cancellationToken);

        var published = new List<Fixture>();

        foreach (var fixture in workload.Fixtures.Where(fixture => fixture.Status == FixtureStatus.Staged))
        {
            fixture.Publish(now);
            published.Add(fixture);
        }

        // Committed before the table is read, so the results this round just published are part of what the
        // rebuild sees. Reading the table's inputs before that would rank a division missing its own round.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var positions = await RebuildTableAsync(workload.Matchday.DivisionSeasonId, now, cancellationToken);

        // The gate is drawn against the table the round just produced, and staged in this transaction so
        // the result and the money it earned become public together (FIN-3, §7.4.7).
        await _finances.PostGateReceiptsAsync(
            workload.Matchday.Id,
            published,
            positions,
            now,
            cancellationToken);

        var effects = await ApplyEffectsAsync(workload, published, now, cancellationToken);

        // Written in the same transaction as the results, so the round and the news of it become public
        // together: a message about a result nobody can read yet would be the same defect as a leaked score.
        await _notifications.NotifyPublishedAsync(
            workload.Matchday.RoundNumber,
            workload.WorldId,
            workload.DivisionId,
            PlayedFacts(published),
            positions,
            effects,
            now,
            cancellationToken);

        workload.Matchday.Publish(now);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new PublishMatchdayResult(PublishMatchdayOutcome.Published, published.Count, positions.Count);
    }

    private static IReadOnlyList<PlayedFixtureFact> PlayedFacts(IReadOnlyList<Fixture> published) =>
    [
        .. published
            .OrderBy(fixture => fixture.Id)
            .Select(fixture => new PlayedFixtureFact(
                fixture.Id,
                fixture.MatchId!.Value,
                fixture.HomeClubId,
                fixture.AwayClubId,
                fixture.HomeScore!.Value,
                fixture.AwayScore!.Value)),
    ];

    /// <summary>
    /// Serves each club's open absences one fixture and opens the ones this round created (`DIS-1`,
    /// `DIS-2`, `DIS-4`, `DIS-5`).
    /// </summary>
    /// <returns>What the round did to each booked, sent-off, or injured player (`F-41`).</returns>
    /// <remarks>
    /// <para>
    /// Serving happens before the round's own cards and injuries are applied, so an absence is never served
    /// by the match that caused it: a player sent off in this round misses the next one, and a three-fixture
    /// injury keeps them out of the next three.
    /// </para>
    /// <para>
    /// A sending-off and a yellow accumulation each earn a suspension, and both are summed into one record,
    /// because they are two rules that both fired rather than two things for the player to serve twice over
    /// the same span. A second yellow is both the booking it was and the red it became, exactly as the match
    /// statistics count it (`MAT-5`), so a player can reach the threshold and be sent off in one match.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<PlayerEffectFact>> ApplyEffectsAsync(
        MatchdayWorkload workload,
        List<Fixture> published,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (published.Count == 0)
        {
            return [];
        }

        var clubIds = published
            .SelectMany(fixture => new[] { fixture.HomeClubId, fixture.AwayClubId })
            .Distinct()
            .ToList();

        // Every club that played has one fixture served against each open absence, which is what "measured
        // in fixtures, not days" means (DIS-5, TRN-12).
        foreach (var absence in await _availability.LoadOpenAsync(clubIds, cancellationToken))
        {
            absence.ServeFixture(now);
        }

        var effects = MatchEffectsCalculator.Calculate(
            await _matchdays.LoadMatchEffectsAsync(workload.Matchday.Id, cancellationToken));

        var facts = effects.Count > 0
            ? await ApplyDisciplineAsync(workload, effects, now, cancellationToken)
            : [];

        // The round's results are read once and feed both the load and the season statistics: the load
        // wants the minutes and the snapshot, and the statistics want the minutes, the goals, the assists,
        // and the rating. Two reads of the same nine documents would be two chances to disagree.
        var played = await _matchdays.LoadMatchLoadsAsync(workload.Matchday.Id, cancellationToken);

        await ApplyMatchLoadAsync(played, cancellationToken);
        await ApplySeasonStatisticsAsync(workload.Matchday, played, now, cancellationToken);

        return facts;
    }

    /// <summary>
    /// Advances every player who appeared in the round into their season statistics (`STA-1`…`STA-4`).
    /// </summary>
    /// <remarks>
    /// The deltas are the pure <see cref="SeasonStatisticsCalculator"/>'s, read from each fixture's stored
    /// result and its shot and save events, and they are applied in the publication's serializable
    /// transaction so the totals and the results they summarise become public together. A round that is not
    /// fully staged never reaches here (`MAT-7`), and a republished round returns before it does, so a
    /// player's totals cannot be counted twice.
    /// </remarks>
    private async Task ApplySeasonStatisticsAsync(
        Matchday matchday,
        IReadOnlyList<FixtureMatchLoadRow> played,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (played.Count == 0)
        {
            return;
        }

        var lines = SeasonStatisticsCalculator.Calculate(
            played,
            await _matchdays.LoadMatchStatEventsAsync(matchday.Id, cancellationToken));

        if (lines.Count == 0)
        {
            return;
        }

        var playerIds = lines
            .Select(line => line.PlayerId)
            .Distinct()
            .ToList();

        var stored = (await _matchdays.LoadPlayerSeasonStatsAsync(
                matchday.DivisionSeasonId,
                playerIds,
                cancellationToken))
            .ToDictionary(stat => (stat.PlayerId, stat.ClubId));

        foreach (var line in lines)
        {
            if (stored.TryGetValue((line.PlayerId, line.ClubId), out var stat))
            {
                stat.Accumulate(line, now);

                continue;
            }

            // Opened and advanced in one go: a new line is created with the match that first produced it, so
            // the create and the accumulate are the same transaction and cannot leave an empty row behind.
            stat = PlayerSeasonStat.Open(
                Guid.CreateVersion7(),
                matchday.DivisionSeasonId,
                line.PlayerId,
                line.ClubId,
                now);

            stat.Accumulate(line, now);

            _matchdays.AddPlayerSeasonStat(stat);
            stored[(line.PlayerId, line.ClubId)] = stat;
        }
    }

    /// <summary>
    /// Applies a round's cards and injuries as discipline records and absences (`DIS-1`, `DIS-2`, `DIS-4`).
    /// </summary>
    /// <returns>One fact per affected player, naming the suspension and injury the round produced.</returns>
    private async Task<IReadOnlyList<PlayerEffectFact>> ApplyDisciplineAsync(
        MatchdayWorkload workload,
        IReadOnlyList<MatchPlayerEffect> effects,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var playerIds = effects.Select(effect => effect.PlayerId).Distinct().ToList();

        var records = (await _matchdays.LoadDisciplineAsync(
                workload.Matchday.DivisionSeasonId,
                playerIds,
                cancellationToken))
            .ToDictionary(record => record.PlayerId);

        var facts = new List<PlayerEffectFact>(effects.Count);

        foreach (var effect in effects)
        {
            var suspensionFixtures = 0;
            var fromBookings = false;

            if (effect.YellowCards > 0 || effect.RedCards > 0)
            {
                if (!records.TryGetValue(effect.PlayerId, out var record))
                {
                    record = DisciplineRecord.Open(
                        Guid.CreateVersion7(),
                        workload.Matchday.DivisionSeasonId,
                        effect.PlayerId,
                        now);

                    _matchdays.AddDisciplineRecord(record);
                    records[effect.PlayerId] = record;
                }

                // Asked before the count advances, because "is this booking the fifth" is a question about
                // the accumulation the record held until now (DIS-2).
                fromBookings = record.YellowSuspensionsEarned(
                    effect.YellowCards,
                    WorldRuleSet.YellowSuspensionThreshold) > 0;

                var fromRedCard = effect.RedCards > 0;

                suspensionFixtures =
                    (fromBookings ? WorldRuleSet.YellowSuspensionFixtures : 0)
                    + (fromRedCard ? WorldRuleSet.RedCardSuspensionFixtures : 0);

                record.AddCards(effect.YellowCards, effect.RedCards, now);

                if (suspensionFixtures > 0)
                {
                    _availability.Add(PlayerUnavailability.Open(
                        Guid.CreateVersion7(),
                        effect.PlayerId,
                        effect.ClubId,
                        UnavailabilityType.Suspension,
                        InjurySeverity.Minor,
                        suspensionFixtures,
                        effect.FixtureId,
                        now));
                }
            }

            if (effect.AbsenceFixtures > 0)
            {
                _availability.Add(PlayerUnavailability.Open(
                    Guid.CreateVersion7(),
                    effect.PlayerId,
                    effect.ClubId,
                    UnavailabilityType.Injury,
                    WorldRuleSet.InjurySeverityFor(effect.AbsenceFixtures),
                    effect.AbsenceFixtures,
                    effect.FixtureId,
                    now));
            }

            facts.Add(new PlayerEffectFact(
                effect.ClubId,
                effect.PlayerId,
                suspensionFixtures,
                fromBookings,
                effect.RedCards > 0,
                effect.AbsenceFixtures));
        }

        return facts;
    }

    /// <summary>
    /// Applies the round's load to every player who appeared: condition consumed, fatigue accumulated, and
    /// morale moved by the result and their minutes (`TRN-11`, `TRN-13`).
    /// </summary>
    /// <remarks>
    /// The deltas are the pure <see cref="MatchLoadCalculator"/>'s, derived from each fixture's frozen
    /// snapshot and stored result. They are added to the state the player actually holds rather than to the
    /// state the snapshot froze: the daily progression job writes the same row, so a match that wrote the
    /// snapshot's own arithmetic as an absolute value would erase whatever training happened since the lock.
    /// </remarks>
    private async Task ApplyMatchLoadAsync(
        IReadOnlyList<FixtureMatchLoadRow> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var loads = MatchLoadCalculator.Calculate(
            rows.Select(row => new MatchLoadInput(
                row.FixtureId,
                row.HomeGoals,
                row.AwayGoals,
                MatchSnapshotDocument.Read(row.SnapshotJson).Input,
                MatchStatisticsDocument.Read(row.StatisticsJson).PlayerLines)));

        var states = (await _playerStates.LoadAsync(
                [.. loads.Select(load => load.PlayerId).Distinct()],
                cancellationToken))
            .ToDictionary(state => state.PlayerId);

        // A participant always has a state row, because the generator opens one per player and the snapshot
        // is built from contracted players. A missing one is a defect worth refusing, not a case to skip.
        foreach (var load in loads)
        {
            states[load.PlayerId].ApplyMatchLoad(
                load.ConditionDeltaBp,
                load.FatigueDeltaBp,
                load.MoraleDeltaBp);
        }
    }

    /// <summary>
    /// Recomputes the division's table from its published results and writes it back (`TBL-13`).
    /// </summary>
    /// <returns>Every club's position and the position it held before the round (`F-41`).</returns>
    private async Task<IReadOnlyList<ClubPositionFact>> RebuildTableAsync(
        Guid divisionSeasonId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var source = await _matchdays.LoadTableSourceAsync(divisionSeasonId, cancellationToken);

        if (source is null)
        {
            return [];
        }

        var stored = (await _matchdays.LoadStandingsAsync(divisionSeasonId, cancellationToken))
            .ToDictionary(standing => standing.ClubId);

        // Captured before the rebuild, because Rebuild rewrites the rank in place: the previous rank is the
        // one the table held going into this round, which is what "the table moved" compares against.
        var before = stored.ToDictionary(entry => entry.Key, entry => entry.Value.Rank);

        var lines = StandingsCalculator.Rank(
            source.ClubIds,
            source.Outcomes,
            clubId => StandingsCalculator.DrawKeyOf(source.TieDrawSeed, clubId));

        foreach (var line in lines)
        {
            if (stored.TryGetValue(line.ClubId, out var standing))
            {
                standing.Rebuild(line, now);

                continue;
            }

            _matchdays.AddStanding(Standing.Create(Guid.CreateVersion7(), divisionSeasonId, line, now));
        }

        return
        [
            .. lines
                .OrderBy(line => line.ClubId)
                .Select(line => new ClubPositionFact(
                    line.ClubId,
                    line.Rank,
                    before.TryGetValue(line.ClubId, out var previous) ? previous : line.Rank)),
        ];
    }
}
