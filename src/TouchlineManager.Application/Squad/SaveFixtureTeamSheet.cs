using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Application.Competition;
using TouchlineManager.Contracts.Competition;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Squad;

/// <summary>What happened when a fixture team sheet was saved.</summary>
public enum SaveFixtureTeamSheetOutcome
{
    /// <summary>A selection was prepared for the first time.</summary>
    Created = 0,

    /// <summary>An existing selection was replaced.</summary>
    Updated = 1,

    /// <summary>The selection breaks a team-sheet rule. The result carries the issues.</summary>
    Invalid = 2,

    /// <summary>No world has been seeded.</summary>
    WorldNotSeeded = 3,

    /// <summary>The account has no manager profile.</summary>
    NoManagerProfile = 4,

    /// <summary>The manager holds no club.</summary>
    NoClub = 5,

    /// <summary>The manager's club does not exist.</summary>
    ClubNotFound = 6,

    /// <summary>No fixture exists with that identity.</summary>
    FixtureNotFound = 7,

    /// <summary>The manager's club does not play in that fixture.</summary>
    FixtureNotYours = 8,

    /// <summary>The fixture has locked, so its sheet can no longer be changed (`CAL-3`, `SQ-7`).</summary>
    FixtureLocked = 9,

    /// <summary>The club has no default plan to prepare a side from (`INS-11`).</summary>
    NoDefaultPlan = 10,

    /// <summary>The command replaces a saved selection and no <c>If-Match</c> version was supplied (`CONC-1`).</summary>
    PreconditionRequired = 11,

    /// <summary>The supplied version was stale. The client must reapply against current state.</summary>
    PreconditionFailed = 12,
}

/// <summary>The result of saving a fixture team sheet.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="TeamSheet">The saved side, when the save succeeded.</param>
/// <param name="Validation">The validation preview, present when the selection was refused.</param>
public sealed record SaveFixtureTeamSheetResult(
    SaveFixtureTeamSheetOutcome Outcome,
    FixtureTeamSheetResponse? TeamSheet,
    TeamSheetValidationResponse? Validation);

/// <summary>
/// Prepares the caller's club's side for one fixture (master plan §10.4, §11.1; `SQ-4`, `SQ-9`, `CAL-3`).
/// </summary>
/// <remarks>
/// <para>
/// The whole selection is replaced on every save, so what the manager submitted is exactly what is stored.
/// The club, the fixture, the plan version the sheet references, and the deadline all come from the server:
/// none of them is taken from the request, which is what stops a client from preparing a side for another
/// club or after its deadline (`INT-1`).
/// </para>
/// <para>
/// The caller's <c>If-Match</c> version is mandatory once a sheet exists, and absent for the first save —
/// there is nothing to be conditional against yet. A stale one is refused rather than overwriting a
/// selection made on another device (`CONC-1`, ADR-0009).
/// </para>
/// <para>
/// Validation is the same pure rule the snapshot builder will run at lock time (`INS-12`), so a side that
/// could not be locked can never have been saved, and the manager learns about it while there is still
/// time to change it.
/// </para>
/// </remarks>
public sealed class SaveFixtureTeamSheet
{
    private readonly ResolveOwnedClub _access;
    private readonly ITeamSheetQueries _queries;
    private readonly ITeamSheetRepository _repository;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly ISecureTokenService _secureTokens;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public SaveFixtureTeamSheet(
        ResolveOwnedClub access,
        ITeamSheetQueries queries,
        ITeamSheetRepository repository,
        IClock clock,
        IAuditWriter audit,
        ISecureTokenService secureTokens,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork)
    {
        _access = access;
        _queries = queries;
        _repository = repository;
        _clock = clock;
        _audit = audit;
        _secureTokens = secureTokens;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Replaces the club's selection for a fixture.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="fixtureId">The fixture.</param>
    /// <param name="expectedVersion">The version from the request's <c>If-Match</c>, when replacing a saved selection.</param>
    /// <param name="request">The selection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SaveFixtureTeamSheetResult> ExecuteAsync(
        Guid userId,
        Guid fixtureId,
        long? expectedVersion,
        SaveFixtureTeamSheetRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return Refused(FromAccess(access.Outcome));
        }

        var snapshot = await _queries.GetTeamSheetAsync(fixtureId, access.ClubId, cancellationToken);

        if (snapshot is null)
        {
            return Refused(SaveFixtureTeamSheetOutcome.FixtureNotFound);
        }

        if (snapshot.Fixture.HomeClubId != access.ClubId && snapshot.Fixture.AwayClubId != access.ClubId)
        {
            return Refused(SaveFixtureTeamSheetOutcome.FixtureNotYours);
        }

        var now = _clock.UtcNow;

        if (FixtureMapping.IsLocked(snapshot.Fixture.Status, snapshot.Fixture.LockAt, now))
        {
            return Refused(SaveFixtureTeamSheetOutcome.FixtureLocked);
        }

        if (snapshot.Plan is not { } plan)
        {
            return Refused(SaveFixtureTeamSheetOutcome.NoDefaultPlan);
        }

        var existing = await _repository.FindAsync(fixtureId, access.ClubId, cancellationToken);

        if (existing is not null)
        {
            if (expectedVersion is null)
            {
                return Refused(SaveFixtureTeamSheetOutcome.PreconditionRequired);
            }

            if (existing.Sheet.Version != expectedVersion.Value)
            {
                return Refused(SaveFixtureTeamSheetOutcome.PreconditionFailed);
            }
        }

        var selection = request.Selection
            .Select(entry => new TeamSheetSelection(entry.SlotNumber, entry.PlayerId))
            .ToList();

        var validation = FixtureTeamSheetValidator.Validate(
            selection,
            [.. snapshot.SelectablePlayers.Select(player => player.Id)],
            [.. snapshot.SelectablePlayers.Where(player => player.IsUnavailable).Select(player => player.Id)]);

        if (!validation.IsValid)
        {
            return new SaveFixtureTeamSheetResult(
                SaveFixtureTeamSheetOutcome.Invalid,
                null,
                validation.ToResponse());
        }

        var sheet = existing is null
            ? OpenSheet(fixtureId, access.ClubId, plan, now)
            : Rebase(existing, plan, now);

        foreach (var entry in selection.OrderBy(entry => entry.SlotNumber))
        {
            _repository.AddEntry(TeamSheetEntry.Select(
                Guid.CreateVersion7(),
                sheet.Id,
                entry.PlayerId,
                DesignationOf(entry.SlotNumber),
                entry.SlotNumber,
                roleOverride: null,
                now));
        }

        Record(userId, sheet.Id);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // The sheet moved between the read and the write. Report a stale precondition and let the
            // manager reload, rather than overwriting the selection that won the race.
            return Refused(SaveFixtureTeamSheetOutcome.PreconditionFailed);
        }

        // Re-read rather than projecting the staged rows: the response then describes exactly what is
        // stored, down to the version a client takes back for its next conditional save.
        var saved = await _queries.GetTeamSheetAsync(fixtureId, access.ClubId, cancellationToken);

        return new SaveFixtureTeamSheetResult(
            existing is null ? SaveFixtureTeamSheetOutcome.Created : SaveFixtureTeamSheetOutcome.Updated,
            saved?.ToResponse(access.ClubId, now),
            null);
    }

    /// <summary>Opens the club's first sheet for a fixture.</summary>
    private FixtureTeamSheet OpenSheet(
        Guid fixtureId,
        Guid clubId,
        TeamSheetPlanRow plan,
        DateTimeOffset now)
    {
        var sheet = FixtureTeamSheet.Draft(
            Guid.CreateVersion7(),
            fixtureId,
            clubId,
            plan.PlanId,
            plan.Version,
            now);

        _repository.AddSheet(sheet);

        return sheet;
    }

    /// <summary>Replaces an existing sheet's selection, rebasing it onto the current plan version.</summary>
    /// <remarks>
    /// The previous entries are removed rather than edited in place. A selection is a set of player-and-slot
    /// pairs, and swapping two players between two slots cannot be expressed as a sequence of single-slot
    /// edits without passing through a state that violates the sheet's unique indexes; the delete and the
    /// insert commit in one save, so no reader ever sees the selection half-replaced.
    /// </remarks>
    private FixtureTeamSheet Rebase(TeamSheetRecord existing, TeamSheetPlanRow plan, DateTimeOffset now)
    {
        _repository.RemoveEntries(existing.Entries);

        existing.Sheet.Rebase(plan.PlanId, plan.Version, now);

        return existing.Sheet;
    }

    private static TeamSheetDesignation DesignationOf(int slotNumber) =>
        slotNumber <= WorldRuleSet.TeamSheetStarters
            ? TeamSheetDesignation.Starter
            : TeamSheetDesignation.Substitute;

    private static SaveFixtureTeamSheetOutcome FromAccess(ClubAccessOutcome outcome) => outcome switch
    {
        ClubAccessOutcome.WorldNotSeeded => SaveFixtureTeamSheetOutcome.WorldNotSeeded,
        ClubAccessOutcome.NoManagerProfile => SaveFixtureTeamSheetOutcome.NoManagerProfile,
        ClubAccessOutcome.NoClub => SaveFixtureTeamSheetOutcome.NoClub,
        ClubAccessOutcome.ClubNotManaged => SaveFixtureTeamSheetOutcome.FixtureNotYours,
        _ => SaveFixtureTeamSheetOutcome.ClubNotFound,
    };

    private static SaveFixtureTeamSheetResult Refused(SaveFixtureTeamSheetOutcome outcome) =>
        new(outcome, null, null);

    private void Record(Guid userId, Guid sheetId) =>
        _audit.Record(new AuditEntry(
            SquadAuditActions.FixtureTeamSheetSaved,
            AuditActorTypes.User,
            userId,
            AuditTargetTypes.FixtureTeamSheet,
            sheetId,
            _requestContext.CorrelationId,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            Reason: null));
}
