using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Squad;

/// <summary>What happened when a player's training programme was set or cleared.</summary>
public enum SetPlayerTrainingProgrammeOutcome
{
    /// <summary>The player now trains the requested programme.</summary>
    Set = 0,

    /// <summary>The player has no override and trains the programme matching their position.</summary>
    Cleared = 1,

    /// <summary>No world has been seeded.</summary>
    WorldNotSeeded = 2,

    /// <summary>The account has no manager profile.</summary>
    NoManagerProfile = 3,

    /// <summary>The manager holds no club.</summary>
    NoClub = 4,

    /// <summary>No player holds an active contract at the caller's club.</summary>
    PlayerNotFound = 5,

    /// <summary>The player holds a club, but not the caller's (master plan §10.9).</summary>
    ClubNotManaged = 6,

    /// <summary>An override exists and no <c>If-Match</c> version was supplied (`CONC-1`).</summary>
    PreconditionRequired = 7,

    /// <summary>The supplied version was stale. The client must reapply against current state.</summary>
    PreconditionFailed = 8,

    /// <summary>The club no longer exists.</summary>
    ClubNotFound = 9,
}

/// <summary>The result of setting or clearing a player's training programme.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Programme">The programme as it now stands, when the command succeeded.</param>
public sealed record SetPlayerTrainingProgrammeResult(
    SetPlayerTrainingProgrammeOutcome Outcome,
    PlayerTrainingProgrammeResponse? Programme);

/// <summary>
/// Sets or clears one player's training programme override (`TRN-1`, `TRN-2`).
/// </summary>
/// <remarks>
/// <para>
/// The player's own club is the one authorized against — resolved from the player's active contract rather
/// than taken from the request — so naming another club's player is refused by name, exactly as the player
/// read is (master plan §10.9).
/// </para>
/// <para>
/// A null or empty programme clears the override, returning the player to the programme that matches their
/// position. The existing override's version is required to change a set override, the same contract the
/// training plan and a tactical plan use (`CONC-1`).
/// </para>
/// </remarks>
public sealed class SetPlayerTrainingProgramme
{
    private readonly ResolveOwnedClub _access;
    private readonly ITrainingRepository _repository;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly ISecureTokenService _secureTokens;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public SetPlayerTrainingProgramme(
        ResolveOwnedClub access,
        ITrainingRepository repository,
        IClock clock,
        IAuditWriter audit,
        ISecureTokenService secureTokens,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork)
    {
        _access = access;
        _repository = repository;
        _clock = clock;
        _audit = audit;
        _secureTokens = secureTokens;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Sets or clears the programme.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="playerId">The player whose programme is being changed.</param>
    /// <param name="expectedVersion">The version from the request's <c>If-Match</c>, when an override exists.</param>
    /// <param name="request">The requested programme, or a null programme to clear it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SetPlayerTrainingProgrammeResult> ExecuteAsync(
        Guid userId,
        Guid playerId,
        long? expectedVersion,
        SetPlayerTrainingProgrammeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var player = await _repository.FindTrainablePlayerAsync(playerId, cancellationToken);

        if (player is null)
        {
            return Refused(SetPlayerTrainingProgrammeOutcome.PlayerNotFound);
        }

        var access = await _access.ExecuteAsync(userId, player.ClubId, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return Refused(FromAccess(access.Outcome));
        }

        var focus = await _repository.FindFocusAsync(playerId, cancellationToken);
        var programme = ParseProgramme(request.Programme);

        if (focus is not null)
        {
            if (expectedVersion is null)
            {
                return Refused(SetPlayerTrainingProgrammeOutcome.PreconditionRequired);
            }

            if (focus.Version != expectedVersion.Value)
            {
                return Refused(SetPlayerTrainingProgrammeOutcome.PreconditionFailed);
            }
        }

        var now = _clock.UtcNow;
        SetPlayerTrainingProgrammeOutcome outcome;
        PlayerTrainingFocus? resolved;

        if (programme is null)
        {
            (outcome, resolved) = Clear(focus, userId);
        }
        else
        {
            (outcome, resolved) = Apply(focus, playerId, access.ClubId, programme.Value, userId, now);
        }

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return Refused(SetPlayerTrainingProgrammeOutcome.PreconditionFailed);
        }

        return new SetPlayerTrainingProgrammeResult(
            outcome,
            resolved.ToResponse(playerId, player.PrimaryPosition, now));
    }

    /// <summary>
    /// Sets the programme, creating the override when there is none. Returns the row that now holds it, so
    /// the caller answers with the override that exists rather than the one that did.
    /// </summary>
    private (SetPlayerTrainingProgrammeOutcome Outcome, PlayerTrainingFocus Focus) Apply(
        PlayerTrainingFocus? focus,
        Guid playerId,
        Guid clubId,
        TrainingProgramme programme,
        Guid userId,
        DateTimeOffset now)
    {
        var effectiveDate = DateOnly.FromDateTime(now.UtcDateTime);

        if (focus is null)
        {
            focus = PlayerTrainingFocus.Set(
                Guid.CreateVersion7(),
                playerId,
                clubId,
                programme,
                effectiveDate,
                now);

            _repository.AddPlayerFocus(focus);
        }
        else
        {
            focus.Revise(programme, effectiveDate, now);
        }

        Record(SquadAuditActions.PlayerTrainingProgrammeSet, userId, focus.Id);

        return (SetPlayerTrainingProgrammeOutcome.Set, focus);
    }

    private (SetPlayerTrainingProgrammeOutcome Outcome, PlayerTrainingFocus? Focus) Clear(
        PlayerTrainingFocus? focus,
        Guid userId)
    {
        if (focus is null)
        {
            // Already clear. Idempotent, so a retried clear does not become a conflict.
            return (SetPlayerTrainingProgrammeOutcome.Cleared, null);
        }

        _repository.RemovePlayerFocus(focus);

        Record(SquadAuditActions.PlayerTrainingProgrammeCleared, userId, focus.Id);

        return (SetPlayerTrainingProgrammeOutcome.Cleared, null);
    }

    private static SetPlayerTrainingProgrammeResult Refused(SetPlayerTrainingProgrammeOutcome outcome) =>
        new(outcome, null);

    private static TrainingProgramme? ParseProgramme(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : TrainingProgrammes.FromCode(code);

    private static SetPlayerTrainingProgrammeOutcome FromAccess(ClubAccessOutcome outcome) => outcome switch
    {
        ClubAccessOutcome.WorldNotSeeded => SetPlayerTrainingProgrammeOutcome.WorldNotSeeded,
        ClubAccessOutcome.NoManagerProfile => SetPlayerTrainingProgrammeOutcome.NoManagerProfile,
        ClubAccessOutcome.NoClub => SetPlayerTrainingProgrammeOutcome.NoClub,
        ClubAccessOutcome.ClubNotManaged => SetPlayerTrainingProgrammeOutcome.ClubNotManaged,
        ClubAccessOutcome.ClubNotFound => SetPlayerTrainingProgrammeOutcome.ClubNotFound,
        _ => SetPlayerTrainingProgrammeOutcome.PlayerNotFound,
    };

    private void Record(string action, Guid userId, Guid focusId) =>
        _audit.Record(new AuditEntry(
            action,
            AuditActorTypes.User,
            userId,
            AuditTargetTypes.PlayerTrainingFocus,
            focusId,
            _requestContext.CorrelationId,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            Reason: null));
}
