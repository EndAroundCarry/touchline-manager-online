using Microsoft.Extensions.Logging;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Comms;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Application.Comms;
using TouchlineManager.Domain.Auth;
using TouchlineManager.Domain.Comms;
using TouchlineManager.Domain.Ops;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Application.World;

/// <summary>What one inactivity evaluation did.</summary>
/// <param name="Evaluated">How many open tenures were considered.</param>
/// <param name="Warned">How many earned an inactivity warning.</param>
/// <param name="MarkedInactive">How many became inactive, handing routine decisions to the AI.</param>
/// <param name="Closed">How many tenures ended and returned their club to the AI.</param>
public sealed record InactivityResult(int Evaluated, int Warned, int MarkedInactive, int Closed);

/// <summary>
/// Walks the inactivity ladder over the whole membership: warn, hand routine decisions to the AI, then close
/// (`OCC-1`–`OCC-3`, `OCC-8`, ADR-0027).
/// </summary>
/// <remarks>
/// <para>
/// One evaluation, run on a schedule, that advances every open tenure to whichever rung real elapsed time puts
/// it on. The thresholds are the rule set's, read from <see cref="IClock.UtcNow"/> and each tenure's
/// <see cref="ClubTenure.LastActiveAt"/>, so the ladder is a fact about time rather than about how often the
/// job happens to run — a worker that was down for a week catches up on its next pass rather than skipping a
/// rung.
/// </para>
/// <para>
/// A suspended or closing account is skipped: its club is still held (`OCC-5`) but it is not an inactivity
/// lapse, because the manager did not choose to be away, and ending a tenure the game itself blocked would be
/// punishing the wrong party. Administrative suspension is Stage 14's to resolve.
/// </para>
/// <para>
/// The warning is sent once per lapse: <see cref="ClubTenure.InactivityWarningAt"/> records it, and logging in
/// clears it, so a manager who returns and lapses again is warned again (`OCC-1`).
/// </para>
/// </remarks>
public sealed partial class EvaluateInactivity
{
    private readonly IClock _clock;
    private readonly IClubTenureRepository _tenures;
    private readonly IInboxRepository _inbox;
    private readonly INotificationPreferencesRepository _preferences;
    private readonly IOutboxWriter _outbox;
    private readonly CapacityEvaluator _capacity;
    private readonly IAuditWriter _audit;
    private readonly IOperationalMetrics _metrics;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<EvaluateInactivity> _logger;

    /// <summary>Initializes the use case.</summary>
    public EvaluateInactivity(
        IClock clock,
        IClubTenureRepository tenures,
        IInboxRepository inbox,
        INotificationPreferencesRepository preferences,
        IOutboxWriter outbox,
        CapacityEvaluator capacity,
        IAuditWriter audit,
        IOperationalMetrics metrics,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork,
        ILogger<EvaluateInactivity> logger)
    {
        _clock = clock;
        _tenures = tenures;
        _inbox = inbox;
        _preferences = preferences;
        _outbox = outbox;
        _capacity = capacity;
        _audit = audit;
        _metrics = metrics;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>Runs one pass of the ladder over every open tenure.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<InactivityResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var rows = await _tenures.ListOpenAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return new InactivityResult(0, 0, 0, 0);
        }

        // One read for every preference that might suppress an email, rather than one per recipient.
        var preferences = await _preferences.FindByManagersAsync(
            [.. rows.Select(row => row.Manager.Id).Distinct()],
            cancellationToken);

        var warned = 0;
        var markedInactive = 0;
        var closed = 0;
        var freedCountries = new HashSet<Guid>();

        foreach (var row in rows)
        {
            // A blocked account is not an absence: it keeps the club (OCC-5) but the ladder leaves it alone.
            if (!UserStatusRules.CanAuthenticate(row.User.Status))
            {
                continue;
            }

            var idle = now - row.Tenure.LastActiveAt;
            var days = (int)idle.TotalDays;
            var emailAllowed = preferences.TryGetValue(row.Manager.Id, out var preference)
                ? preference.EmailInactivityWarnings
                : true;

            if (idle >= WorldRuleSet.InactivityCloseAfter)
            {
                row.Tenure.Close(ClubTenureEndReasons.InactivityClosed, now);
                freedCountries.Add(row.Club.CountryId);

                Notify(row.Manager.Id, InboxTemplates.ClosedForInactivity(days), now, row.User.Email, row.Club.Name, days, closed: true, emailAllowed);
                Audit(WorldAuditActions.TenureClosedForInactivity, row, days);

                closed++;
            }
            else if (idle >= WorldRuleSet.InactivityAiAssistanceAfter
                && row.Tenure.ControlStatus == ClubTenureControlStatus.Active)
            {
                row.Tenure.MarkInactive(now);
                Audit(WorldAuditActions.TenureBecameInactive, row, days);

                markedInactive++;
            }
            else if (idle >= WorldRuleSet.InactivityWarningAfter && row.Tenure.InactivityWarningAt is null)
            {
                Notify(row.Manager.Id, InboxTemplates.Warned(days), now, row.User.Email, row.Club.Name, days, closed: false, emailAllowed);
                row.Tenure.Warn(now);
                Audit(WorldAuditActions.InactivityWarningSent, row, days);

                warned++;
            }
        }

        if (warned > 0 || markedInactive > 0 || closed > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Counted after the commit, so a pass that failed to save leaves no phantom transition in the
            // funnel (`F-54`, ADR-0041).
            for (var transition = 0; transition < markedInactive; transition++)
            {
                _metrics.TenureBecameInactive();
            }

            for (var transition = 0; transition < closed; transition++)
            {
                _metrics.TenureClosed(ClubTenureEndReasons.InactivityClosed);
            }
        }

        // PYR-1: a club freed in the lowest tier is what fills it, so each affected country is re-measured once
        // the closures are visible. The evaluation takes the country lock, so each runs in its own transaction.
        foreach (var countryId in freedCountries)
        {
            await EnsureGrowthAsync(countryId, cancellationToken);
        }

        if (closed > 0 || markedInactive > 0 || warned > 0)
        {
            LogEvaluated(rows.Count, warned, markedInactive, closed);
        }

        return new InactivityResult(rows.Count, warned, markedInactive, closed);
    }

    private async Task EnsureGrowthAsync(Guid countryId, CancellationToken cancellationToken)
    {
        await using var transaction = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.ReadCommitted,
            cancellationToken);

        await _capacity.EnsureNextTierRequestedAsync(countryId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private void Notify(
        Guid managerId,
        InboxDraft draft,
        DateTimeOffset now,
        string email,
        string clubName,
        int days,
        bool closed,
        bool emailAllowed)
    {
        _inbox.Add(InboxMessage.Record(
            Guid.CreateVersion7(),
            managerId,
            draft.Category,
            draft.TemplateKey,
            draft.ParametersJson,
            draft.RelatedEntityId,
            now));

        if (!emailAllowed)
        {
            return;
        }

        var message = closed
            ? OccupancyEmails.InactivityClosed(email, clubName, days)
            : OccupancyEmails.InactivityWarning(email, clubName, days);

        _outbox.Enqueue(EmailOutbox.For(message));
    }

    private void Audit(string action, OpenTenureRow row, int days) =>
        _audit.Record(new AuditEntry(
            action,
            AuditActorTypes.Service,
            _requestContext.ActorUserId,
            AuditTargetTypes.ClubTenure,
            row.Tenure.Id,
            _requestContext.CorrelationId,
            IpHash: null,
            Reason: $"{days}d"));

    [LoggerMessage(
        EventId = 5120,
        Level = LogLevel.Information,
        Message = "Inactivity ladder evaluated {Evaluated} tenures: {Warned} warned, {MarkedInactive} inactive, "
            + "{Closed} closed (OCC-1..OCC-3).")]
    private partial void LogEvaluated(int evaluated, int warned, int markedInactive, int closed);
}
