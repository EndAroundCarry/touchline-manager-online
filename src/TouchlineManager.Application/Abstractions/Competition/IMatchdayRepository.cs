using TouchlineManager.Domain.Competition;
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
