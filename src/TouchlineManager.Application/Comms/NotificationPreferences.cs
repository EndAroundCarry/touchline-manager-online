using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Comms;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Contracts.Comms;
using TouchlineManager.Domain.Comms;

namespace TouchlineManager.Application.Comms;

/// <summary>What a notification-preferences read or change did (`COM-4`).</summary>
public enum NotificationPreferencesOutcome
{
    /// <summary>The read or change succeeded.</summary>
    Ok = 0,

    /// <summary>The account has no manager profile, so it has no preferences.</summary>
    NoManagerProfile = 1,

    /// <summary>The supplied version did not match, so the change was refused (`CONC-1`).</summary>
    PreconditionFailed = 2,
}

/// <summary>The result of reading a manager's notification preferences.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Preferences">The preferences, when the read succeeded.</param>
public sealed record GetNotificationPreferencesResult(
    NotificationPreferencesOutcome Outcome,
    NotificationPreferencesResponse? Preferences);

/// <summary>The result of changing a manager's notification preferences.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Preferences">The preferences, when the change succeeded.</param>
public sealed record UpdateNotificationPreferencesResult(
    NotificationPreferencesOutcome Outcome,
    NotificationPreferencesResponse? Preferences);

/// <summary>
/// Reads the authenticated manager's notification preferences (`COM-4`, master plan §10.7).
/// </summary>
/// <remarks>
/// A manager who has never set a preference has no row, so the read answers with the defaults at version zero
/// rather than creating a row nothing asked for. Version zero is the token the change command expects back, so
/// the first save is conditional on "still unset" like every later one.
/// </remarks>
public sealed class GetNotificationPreferences
{
    private readonly IManagerRepository _managers;
    private readonly INotificationPreferencesRepository _preferences;
    private readonly IClock _clock;

    /// <summary>Initializes the query.</summary>
    public GetNotificationPreferences(
        IManagerRepository managers,
        INotificationPreferencesRepository preferences,
        IClock clock)
    {
        _managers = managers;
        _preferences = preferences;
        _clock = clock;
    }

    /// <summary>Reads the manager's preferences.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetNotificationPreferencesResult> ExecuteAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var manager = await _managers.FindByUserIdAsync(userId, cancellationToken);

        if (manager is null)
        {
            return new GetNotificationPreferencesResult(NotificationPreferencesOutcome.NoManagerProfile, null);
        }

        var preferences = await _preferences.FindByManagerAsync(manager.Id, cancellationToken);

        return new GetNotificationPreferencesResult(
            NotificationPreferencesOutcome.Ok,
            preferences is null
                ? Defaults(_clock.UtcNow)
                : preferences.ToResponse(_clock.UtcNow));
    }

    /// <summary>The response for a manager who has never set a preference.</summary>
    internal static NotificationPreferencesResponse Defaults(DateTimeOffset now) =>
        new(
            EmailDeadlineReminders: true,
            EmailInactivityWarnings: true,
            EmailMarketMessages: true,
            EmailNewsDigest: true,
            Version: 0,
            ServerTime: now);
}

/// <summary>
/// Changes the authenticated manager's notification preferences (`COM-4`, `CONC-1`).
/// </summary>
/// <remarks>
/// Conditional on the version the client last read, so a change made on one device cannot silently overwrite
/// one made on another. A manager with no row has one created by the first change, and version zero is the
/// token that authorises it.
/// </remarks>
public sealed class UpdateNotificationPreferences
{
    private readonly IManagerRepository _managers;
    private readonly INotificationPreferencesRepository _preferences;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    /// <summary>Initializes the use case.</summary>
    public UpdateNotificationPreferences(
        IManagerRepository managers,
        INotificationPreferencesRepository preferences,
        IAuditWriter audit,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _managers = managers;
        _preferences = preferences;
        _audit = audit;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <summary>Changes the manager's preferences.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="request">The new switches.</param>
    /// <param name="expectedVersion">The version the client read, from <c>If-Match</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<UpdateNotificationPreferencesResult> ExecuteAsync(
        Guid userId,
        UpdateNotificationPreferencesRequest request,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var manager = await _managers.FindByUserIdAsync(userId, cancellationToken);

        if (manager is null)
        {
            return new UpdateNotificationPreferencesResult(
                NotificationPreferencesOutcome.NoManagerProfile,
                null);
        }

        var now = _clock.UtcNow;
        var preferences = await _preferences.FindByManagerAsync(manager.Id, cancellationToken);
        var currentVersion = preferences?.Version ?? 0;

        if (currentVersion != expectedVersion)
        {
            return new UpdateNotificationPreferencesResult(
                NotificationPreferencesOutcome.PreconditionFailed,
                null);
        }

        if (preferences is null)
        {
            preferences = NotificationPreferences.CreateDefault(Guid.CreateVersion7(), manager.Id, now);
            _preferences.Add(preferences);
        }

        preferences.Update(
            request.EmailDeadlineReminders,
            request.EmailInactivityWarnings,
            request.EmailMarketMessages,
            request.EmailNewsDigest,
            now);

        _audit.Record(new AuditEntry(
            WorldAuditActions.NotificationPreferencesChanged,
            AuditActorTypes.User,
            userId,
            AuditTargetTypes.Manager,
            manager.Id,
            _requestContext.CorrelationId,
            IpHash: null,
            Reason: null));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new UpdateNotificationPreferencesResult(
            NotificationPreferencesOutcome.Ok,
            preferences.ToResponse(now));
    }
}

/// <summary>Projects notification preferences for the client (`COM-4`).</summary>
internal static class NotificationPreferencesMapping
{
    /// <summary>Projects the preferences.</summary>
    public static NotificationPreferencesResponse ToResponse(
        this NotificationPreferences preferences,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        return new NotificationPreferencesResponse(
            preferences.EmailDeadlineReminders,
            preferences.EmailInactivityWarnings,
            preferences.EmailMarketMessages,
            preferences.EmailNewsDigest,
            preferences.Version,
            now);
    }
}
