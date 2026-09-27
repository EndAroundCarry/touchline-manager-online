using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Match;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Abstractions.Competition;

/// <summary>One fixture of a matchday, as the matchday workflow reads it.</summary>
/// <param name="FixtureId">The fixture.</param>
/// <param name="HomeClubId">The host.</param>
/// <param name="AwayClubId">The visitor.</param>
/// <param name="Status">The fixture's lifecycle state.</param>
/// <param name="MatchId">The simulated match, once the result is staged.</param>
public sealed record MatchdayFixtureRow(
    Guid FixtureId,
    Guid HomeClubId,
    Guid AwayClubId,
    FixtureStatus Status,
    Guid? MatchId);

/// <summary>One slot of the shape a club takes the field in.</summary>
/// <param name="SlotNumber">The slot number, 1–11.</param>
/// <param name="PositionFamily">The family the slot asks for.</param>
/// <param name="Role">The role the slot asks for.</param>
/// <param name="NormalizedX">The normalized depth, 0–10,000.</param>
/// <param name="NormalizedY">The normalized width, 0–10,000.</param>
public sealed record SnapshotSlotRow(
    int SlotNumber,
    PositionFamily PositionFamily,
    PlayerRole Role,
    int NormalizedX,
    int NormalizedY);

/// <summary>One player a club's sheet named for a slot.</summary>
/// <param name="SlotNumber">The slot number, 1–18.</param>
/// <param name="PlayerId">The named player.</param>
/// <param name="RoleOverride">A role for this fixture only, when the manager set one.</param>
public sealed record SnapshotSelectionRow(int SlotNumber, Guid PlayerId, PlayerRole? RoleOverride);

/// <summary>One player the snapshot may pick, with everything the engine needs frozen about them.</summary>
/// <param name="PlayerId">The player.</param>
/// <param name="FullName">The generated full name.</param>
/// <param name="ShortName">The abbreviation commentary uses.</param>
/// <param name="PrimaryPosition">The position the player is most at home in.</param>
/// <param name="SecondaryPositions">The further positions the player covers.</param>
/// <param name="Attributes">The twenty-eight attributes, in canonical order.</param>
/// <param name="ConditionBp">Condition in basis points.</param>
/// <param name="FatigueBp">Fatigue in basis points.</param>
/// <param name="MoraleBp">Morale in basis points.</param>
/// <param name="MatchSharpnessBp">Match sharpness in basis points.</param>
/// <param name="IsAvailable">Whether the player has no open injury or suspension (`TRN-12`, `DIS-5`).</param>
public sealed record SnapshotPlayerRow(
    Guid PlayerId,
    string FullName,
    string ShortName,
    PlayerPosition PrimaryPosition,
    IReadOnlyList<PlayerPosition> SecondaryPositions,
    IReadOnlyList<int> Attributes,
    int ConditionBp,
    int FatigueBp,
    int MoraleBp,
    int MatchSharpnessBp,
    bool IsAvailable);

/// <summary>Everything one club's side is built from.</summary>
/// <param name="ClubId">The club.</param>
/// <param name="ClubName">The club's generated name.</param>
/// <param name="Instructions">
/// The team instructions of the club's default plan, or null when the club has saved no plan and takes
/// the field with the neutral set.
/// </param>
/// <param name="Slots">
/// The default plan's eleven slots, or empty when the club has no plan and the builder lays out the
/// default formation.
/// </param>
/// <param name="Selection">What the club's sheet for this fixture named, in slot order. Empty when it has none.</param>
/// <param name="Players">
/// Every player who may be picked: an active contract and an active registration agree on the club. The
/// unavailable ones are carried rather than filtered out, because a repair has to be able to say that the
/// player it dropped was injured rather than merely not selected.
/// </param>
public sealed record ClubSideSource(
    Guid ClubId,
    string ClubName,
    TeamInstructionSet? Instructions,
    IReadOnlyList<SnapshotSlotRow> Slots,
    IReadOnlyList<SnapshotSelectionRow> Selection,
    IReadOnlyList<SnapshotPlayerRow> Players);

/// <summary>Both clubs of one fixture, with everything a snapshot is built from.</summary>
/// <param name="FixtureId">The fixture.</param>
/// <param name="SeasonId">The season being played.</param>
/// <param name="WorldId">The world.</param>
/// <param name="Home">The host's side source.</param>
/// <param name="Away">The visitor's side source.</param>
/// <param name="Sheets">
/// The club's prepared sheets for this fixture, tracked, so the lock workflow can freeze them in the same
/// transaction as the snapshot (`CAL-3`, `SQ-7`). Empty when neither club prepared one.
/// </param>
public sealed record FixtureSidesSnapshot(
    Guid FixtureId,
    Guid SeasonId,
    Guid WorldId,
    ClubSideSource Home,
    ClubSideSource Away,
    IReadOnlyList<FixtureTeamSheet> Sheets);

/// <summary>Everything a division's table is computed from.</summary>
/// <param name="DivisionSeasonId">The division-season.</param>
/// <param name="TieDrawSeed">The stored tie-break draw seed (`TBL-10`, `TBL-11`).</param>
/// <param name="ClubIds">Every club in the division.</param>
/// <param name="Outcomes">The division's published results, in fixture order.</param>
public sealed record DivisionTableSource(
    Guid DivisionSeasonId,
    string TieDrawSeed,
    IReadOnlyList<Guid> ClubIds,
    IReadOnlyList<MatchOutcome> Outcomes);

/// <summary>
/// One event of a matchday whose published result changes a player's availability (`DIS-1`, `DIS-2`,
/// `DIS-4`).
/// </summary>
/// <remarks>
/// Only the events that have an effect are carried — a booking, a sending-off, an injury — and only those
/// that name a participant, because the rest describe the match rather than a player. The events are the
/// durable narrative, so the effects are re-derived from them rather than stored a second time, the same
/// reading that makes the table's card columns a count over events (`MAT-5`).
/// </remarks>
/// <param name="FixtureId">The fixture the effect happened in.</param>
/// <param name="ClubId">The club the affected player played for.</param>
/// <param name="Type">What happened.</param>
/// <param name="PlayerId">The affected player.</param>
/// <param name="AbsenceFixtures">The fixtures an injury rules the player out for, or zero.</param>
public sealed record MatchEffectEvent(
    Guid FixtureId,
    Guid ClubId,
    MatchEventType Type,
    Guid PlayerId,
    int AbsenceFixtures);

/// <summary>
/// One shot or save event of a matchday, as the season-statistics rule counts them (`STA-*`).
/// </summary>
/// <remarks>
/// The engine's player line carries the attacking and disciplinary summary, and the event stream is where
/// the shots and the saves are: a shot names its taker as the principal participant, and a save names the
/// goalkeeper who made it as the secondary one. Counting them from the same events the score is derived
/// from is what keeps a player's shots and their side's shots one answer (`MAT-5`).
/// </remarks>
/// <param name="FixtureId">The fixture the event happened in.</param>
/// <param name="Type">The event's type.</param>
/// <param name="ParticipantId">The shooter or scorer, when the event names one.</param>
/// <param name="SecondaryParticipantId">The goalkeeper a shot was taken against or saved by, when any.</param>
public sealed record MatchStatEvent(
    Guid FixtureId,
    MatchEventType Type,
    Guid? ParticipantId,
    Guid? SecondaryParticipantId);

/// <summary>
/// One published fixture of a matchday, as the match-load rule reads it (`TRN-11`, `TRN-13`).
/// </summary>
/// <remarks>
/// The frozen snapshot and the stored result are carried as their versioned documents, the same way the
/// match read carries them, so the application layer parses one shape in one place rather than the
/// repository reaching into engine types. The rule reads the snapshot for each player's stamina and side
/// instructions and the result for the minutes played and the scoreline — the facts the match was played
/// from, not whatever the live tables now hold (`MAT-1`).
/// </remarks>
/// <param name="FixtureId">The fixture that was played.</param>
/// <param name="HomeGoals">The host's goals.</param>
/// <param name="AwayGoals">The visitor's goals.</param>
/// <param name="SnapshotJson">The stored input snapshot document.</param>
/// <param name="StatisticsJson">The stored result document, which carries the player lines.</param>
public sealed record FixtureMatchLoadRow(
    Guid FixtureId,
    int HomeGoals,
    int AwayGoals,
    string SnapshotJson,
    string StatisticsJson);

/// <summary>
/// The competition module's port for the matchday workflow (master plan §7.3, §7.4).
/// </summary>
/// <remarks>
/// <para>
/// It reads <em>and</em> stages, which the module's other ports deliberately do not. A matchday workflow
/// mutates the aggregates it read — a fixture locks, a matchday marks itself staged — and stages the rows
/// its own module owns, all in one transaction; splitting the load from the stage would mean two ports
/// that could only ever be used together (§5.2's "cross-module writes occur through application use cases").
/// </para>
/// <para>
/// The loads return tracked aggregates rather than rows, because the workflow's transitions
/// (<see cref="Fixture.Lock"/>, <see cref="Matchday.MarkStaged"/>) belong to the domain and a row copy
/// would move them into the caller.
/// </para>
/// </remarks>
public interface IMatchdayRepository
{
    /// <summary>
    /// Loads one matchday with its fixtures, and the season they are played in.
    /// </summary>
    /// <param name="matchdayId">The matchday.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The workload, or null when the matchday is unknown.</returns>
    Task<MatchdayWorkload?> LoadMatchdayAsync(Guid matchdayId, CancellationToken cancellationToken);

    /// <summary>Loads both clubs of a fixture with everything their sides are built from.</summary>
    /// <param name="fixtureId">The fixture.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The sides, or null when the fixture is unknown.</returns>
    Task<FixtureSidesSnapshot?> LoadFixtureSidesAsync(Guid fixtureId, CancellationToken cancellationToken);

    /// <summary>Loads everything a division's table is computed from.</summary>
    /// <param name="divisionSeasonId">The division-season.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The table's inputs, or null when the division-season is unknown.</returns>
    Task<DivisionTableSource?> LoadTableSourceAsync(Guid divisionSeasonId, CancellationToken cancellationToken);

    /// <summary>Loads a division-season's stored table, for rebuilding in place (`TBL-13`).</summary>
    /// <param name="divisionSeasonId">The division-season.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Standing>> LoadStandingsAsync(Guid divisionSeasonId, CancellationToken cancellationToken);

    /// <summary>Stages a club's line in a division's table.</summary>
    /// <param name="standing">The line.</param>
    void AddStanding(Standing standing);

    /// <summary>
    /// Loads the card and injury events of every fixture in a matchday, in fixture and sequence order
    /// (`DIS-1`, `DIS-2`, `DIS-4`).
    /// </summary>
    /// <param name="matchdayId">The matchday.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<MatchEffectEvent>> LoadMatchEffectsAsync(Guid matchdayId, CancellationToken cancellationToken);

    /// <summary>Loads the card accumulations of a division-season for the given players (`DIS-2`).</summary>
    /// <param name="divisionSeasonId">The division-season.</param>
    /// <param name="playerIds">The players the round touched.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<DisciplineRecord>> LoadDisciplineAsync(
        Guid divisionSeasonId,
        IReadOnlyCollection<Guid> playerIds,
        CancellationToken cancellationToken);

    /// <summary>Stages a player's season card accumulation.</summary>
    /// <param name="record">The record.</param>
    void AddDisciplineRecord(DisciplineRecord record);

    /// <summary>
    /// Loads the shot and save events of a matchday's fixtures, so publication can count each player's
    /// shots and saves into their season statistics (`STA-*`).
    /// </summary>
    /// <param name="matchdayId">The matchday.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One row per shot or save event, in fixture and sequence order.</returns>
    Task<IReadOnlyList<MatchStatEvent>> LoadMatchStatEventsAsync(
        Guid matchdayId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads the season statistics of the given players for a division-season, for advancing in place
    /// (`TBL-13`, applied to players).
    /// </summary>
    /// <param name="divisionSeasonId">The division-season.</param>
    /// <param name="playerIds">The players the round touched.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<PlayerSeasonStat>> LoadPlayerSeasonStatsAsync(
        Guid divisionSeasonId,
        IReadOnlyCollection<Guid> playerIds,
        CancellationToken cancellationToken);

    /// <summary>Stages a player's season statistics line.</summary>
    /// <param name="stat">The line.</param>
    void AddPlayerSeasonStat(PlayerSeasonStat stat);

    /// <summary>
    /// Loads the frozen snapshot and stored result of every published fixture in a matchday, so publication
    /// can apply the load the match placed on the players who appeared (`TRN-11`, `TRN-13`).
    /// </summary>
    /// <param name="matchdayId">The matchday.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One row per published fixture, in fixture order.</returns>
    Task<IReadOnlyList<FixtureMatchLoadRow>> LoadMatchLoadsAsync(
        Guid matchdayId,
        CancellationToken cancellationToken);
}

/// <summary>A matchday with the aggregates its workflow mutates.</summary>
/// <param name="Matchday">The round, tracked.</param>
/// <param name="Fixtures">The round's fixtures, tracked.</param>
/// <param name="SeasonId">The season being played.</param>
/// <param name="WorldId">The world.</param>
/// <param name="TieDrawSeed">The division-season's stored tie-break seed.</param>
public sealed record MatchdayWorkload(
    Matchday Matchday,
    IReadOnlyList<Fixture> Fixtures,
    Guid SeasonId,
    Guid WorldId,
    string TieDrawSeed)
{
    /// <summary>Gets whether every fixture in the round has a result staged or published.</summary>
    public bool AllFixturesStaged => Fixtures.All(
        fixture => fixture.Status is FixtureStatus.Staged or FixtureStatus.Published or FixtureStatus.Void);

    /// <summary>Gets how many fixtures the round still has to simulate.</summary>
    public int UnresolvedFixtureCount => Fixtures.Count(
        fixture => fixture.Status is FixtureStatus.Scheduled or FixtureStatus.Locked or FixtureStatus.Simulating);
}
