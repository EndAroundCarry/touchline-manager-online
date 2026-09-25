using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Squad;

/// <summary>What happened when a player's individual training focus was set or cleared.</summary>
public enum SetPlayerTrainingFocusOutcome
{
    /// <summary>The focus is now set to the requested family.</summary>
    Set = 0,

    /// <summary>The player has no individual focus.</summary>
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

    /// <summary>A focus exists and no <c>If-Match</c> version was supplied (`CONC-1`).</summary>
    PreconditionRequired = 7,

    /// <summary>The supplied version was stale. The client must reapply against current state.</summary>
    PreconditionFailed = 8,

    /// <summary>The club no longer exists.</summary>
    ClubNotFound = 9,
}

/// <summary>The result of setting or clearing a player's individual focus.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Focus">The focus as it now stands, when the command succeeded.</param>
public sealed record SetPlayerTrainingFocusResult(
    SetPlayerTrainingFocusOutcome Outcome,
    PlayerTrainingFocusResponse? Focus);

/// <summary>
/// Sets or clears one player's individual training focus (`TRN-2`).
/// </summary>
/// <remarks>
/// <para>
/// The player's own club is the one authorized against — resolved from the player's active contract rather
/// than taken from the request — so naming another club's player is refused by name, exactly as the player
/// read is (master plan §10.9).
/// </para>
/// <para>
/// A null or empty family clears the focus, returning the player to the club's team plan alone. The
/// existing focus's version is required to change a set focus, the same contract the training plan and a
/// tactical plan use (`CONC-1`).
/// </para>
/// </remarks>
public sealed class SetPlayerTrainingFocus
{
    private readonly ResolveOwnedClub _access;
    private readonly ITrainingRepository _repository;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly ISecureTokenService _secureTokens;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public SetPlayerTrainingFocus(
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

    /// <summary>Sets or clears the focus.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="playerId">The player whose focus is being changed.</param>
    /// <param name="expectedVersion">The version from the request's <c>If-Match</c>, when a focus exists.</param>
    /// <param name="request">The requested focus, or a null family to clear it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SetPlayerTrainingFocusResult> ExecuteAsync(
        Guid userId,
        Guid playerId,
        long? expectedVersion,
        SetPlayerTrainingFocusRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var clubId = await _repository.FindPlayerClubAsync(playerId, cancellationToken);

        if (clubId is null)
        {
            return new SetPlayerTrainingFocusResult(SetPlayerTrainingFocusOutcome.PlayerNotFound, null);
        }

        var access = await _access.ExecuteAsync(userId, clubId, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return new SetPlayerTrainingFocusResult(FromAccess(access.Outcome), null);
        }

        var focus = await _repository.FindFocusAsync(playerId, cancellationToken);
        var family = ParseFamily(request.FocusFamily);

        if (focus is not null)
        {
            if (expectedVersion is null)
            {
                return new SetPlayerTrainingFocusResult(
                    SetPlayerTrainingFocusOutcome.PreconditionRequired,
                    null);
            }

            if (focus.Version != expectedVersion.Value)
            {
                return new SetPlayerTrainingFocusResult(
                    SetPlayerTrainingFocusOutcome.PreconditionFailed,
                    null);
            }
        }

        var now = _clock.UtcNow;
        SetPlayerTrainingFocusOutcome outcome;
        PlayerTrainingFocus? resolved;

        if (family is null)
        {
            (outcome, resolved) = Clear(focus, userId, now);
        }
        else
        {
            (outcome, resolved) = Apply(focus, playerId, access.ClubId, family.Value, userId, now);
        }

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return new SetPlayerTrainingFocusResult(SetPlayerTrainingFocusOutcome.PreconditionFailed, null);
        }

        return new SetPlayerTrainingFocusResult(outcome, resolved.ToResponse(playerId, now));
    }

    /// <summary>
    /// Sets the family, creating the focus when there is none. Returns the row that now holds it, so the
    /// caller answers with the focus that exists rather than the one that did.
    /// </summary>
    private (SetPlayerTrainingFocusOutcome Outcome, PlayerTrainingFocus Focus) Apply(
        PlayerTrainingFocus? focus,
        Guid playerId,
        Guid clubId,
        AttributeFamily family,
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
                family,
                effectiveDate,
                now);

            _repository.AddPlayerFocus(focus);
        }
        else
        {
            focus.Revise(family, effectiveDate, now);
        }

        Record(SquadAuditActions.PlayerTrainingFocusSet, userId, focus.Id);

        return (SetPlayerTrainingFocusOutcome.Set, focus);
    }

    private (SetPlayerTrainingFocusOutcome Outcome, PlayerTrainingFocus? Focus) Clear(
        PlayerTrainingFocus? focus,
        Guid userId,
        DateTimeOffset now)
    {
        if (focus is null)
        {
            // Already clear. Idempotent, so a retried clear does not become a conflict.
            return (SetPlayerTrainingFocusOutcome.Cleared, null);
        }

        _repository.RemovePlayerFocus(focus);

        Record(SquadAuditActions.PlayerTrainingFocusCleared, userId, focus.Id);

        return (SetPlayerTrainingFocusOutcome.Cleared, null);
    }

    private static AttributeFamily? ParseFamily(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : AttributeFamilies.FromCode(code);

    private static SetPlayerTrainingFocusOutcome FromAccess(ClubAccessOutcome outcome) => outcome switch
    {
        ClubAccessOutcome.WorldNotSeeded => SetPlayerTrainingFocusOutcome.WorldNotSeeded,
        ClubAccessOutcome.NoManagerProfile => SetPlayerTrainingFocusOutcome.NoManagerProfile,
        ClubAccessOutcome.NoClub => SetPlayerTrainingFocusOutcome.NoClub,
        ClubAccessOutcome.ClubNotManaged => SetPlayerTrainingFocusOutcome.ClubNotManaged,
        ClubAccessOutcome.ClubNotFound => SetPlayerTrainingFocusOutcome.ClubNotFound,
        _ => SetPlayerTrainingFocusOutcome.PlayerNotFound,
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
