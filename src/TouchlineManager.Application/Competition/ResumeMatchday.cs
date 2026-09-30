using Microsoft.Extensions.Logging;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Application.Jobs;
using TouchlineManager.Domain.Competition;

namespace TouchlineManager.Application.Competition;

/// <summary>What resuming a matchday did.</summary>
public enum ResumeMatchdayOutcome
{
    /// <summary>The round's stuck step was returned to the queue.</summary>
    Resumed = 0,

    /// <summary>The round is already published; there is nothing to resume.</summary>
    AlreadyPublished = 1,

    /// <summary>The queue already owns the round's job, so a resume would change nothing.</summary>
    NotResumable = 2,

    /// <summary>The matchday does not exist.</summary>
    MatchdayNotFound = 3,
}

/// <summary>The result of resuming a matchday.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="MatchdayId">The round that was addressed.</param>
/// <param name="Step">The workflow step that was requeued: <c>resolve</c> or <c>publish</c>.</param>
/// <param name="Requeued"><see langword="true"/> when a dead-lettered job row was reset.</param>
public sealed record ResumeMatchdayResult(
    ResumeMatchdayOutcome Outcome,
    Guid MatchdayId,
    string? Step,
    bool Requeued);

/// <summary>
/// Resumes a stuck matchday on an operator's authority: returns its resolution or publication job to the
/// queue (`F-46`, §13, ADR-0014, ADR-0044).
/// </summary>
/// <remarks>
/// <para>
/// A round stops permanently when a fixture cannot be simulated from its frozen snapshot: the resolution
/// job dead-letters, nothing publishes, and the round sits while the materialiser cannot re-enqueue the
/// job because its business key already exists. This is the operator decision that unblocks it, and it is
/// the same shape as the season-rollover resume (ADR-0034): it requeues the governing job, and if no
/// dead-lettered row exists, enqueues a fresh one under the same key.
/// </para>
/// <para>
/// The step follows the round's publication state: a <c>pending</c> round is stuck in resolution and a
/// <c>staged</c> round in publication. No domain transition is needed, because resolution re-runs the
/// fixtures that never staged — a fixture left <c>simulating</c> is one of them, and its snapshot is
/// re-read, not rebuilt. An already-<c>published</c> round has nothing to resume.
/// </para>
/// <para>
/// This is not a way to force a healthy round forward: when the queue already holds a pending or leased
/// job for the round, nothing is requeued and the caller is told the round is not resumable.
/// </para>
/// </remarks>
public sealed partial class ResumeMatchday
{
    /// <summary>The longest operator reason the audit trail stores.</summary>
    public const int ReasonMaxLength = 200;

    private readonly IClock _clock;
    private readonly IMatchdayRepository _matchdays;
    private readonly IJobQueue _jobs;
    private readonly IAdvisoryLock _locks;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ResumeMatchday> _logger;

    /// <summary>Initializes the use case.</summary>
    public ResumeMatchday(
        IClock clock,
        IMatchdayRepository matchdays,
        IJobQueue jobs,
        IAdvisoryLock locks,
        IAuditWriter audit,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork,
        ILogger<ResumeMatchday> logger)
    {
        _clock = clock;
        _matchdays = matchdays;
        _jobs = jobs;
        _locks = locks;
        _audit = audit;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>Returns a stuck round's resolution or publication job to the queue.</summary>
    /// <param name="matchdayId">The round to resume.</param>
    /// <param name="reason">Why the operator is resuming it. Required, and stored in the audit trail.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ResumeMatchdayResult> ExecuteAsync(
        Guid matchdayId,
        string reason,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (reason.Length > ReasonMaxLength)
        {
            throw new ArgumentException(
                $"The operator reason must be at most {ReasonMaxLength} characters.",
                nameof(reason));
        }

        var workload = await _matchdays.LoadMatchdayAsync(matchdayId, cancellationToken);

        if (workload is null)
        {
            return new ResumeMatchdayResult(ResumeMatchdayOutcome.MatchdayNotFound, matchdayId, null, false);
        }

        if (workload.Matchday.PublicationStatus == MatchdayPublicationStatus.Published)
        {
            return new ResumeMatchdayResult(ResumeMatchdayOutcome.AlreadyPublished, matchdayId, null, false);
        }

        var (jobType, businessKey, step) =
            workload.Matchday.PublicationStatus == MatchdayPublicationStatus.Staged
                ? (MatchdayJobTypes.Publish, MatchdayJobTypes.PublishKey(matchdayId), "publish")
                : (MatchdayJobTypes.Resolve, MatchdayJobTypes.ResolveKey(matchdayId), "resolve");

        await using var transaction = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.ReadCommitted,
            cancellationToken);
        await _locks.AcquireAsync(AdvisoryLockKey.Matchday(matchdayId), cancellationToken);

        var now = _clock.UtcNow;
        var requeued = await _jobs.RequeueAsync(jobType, businessKey, cancellationToken);

        if (!requeued)
        {
            // No dead-lettered row exists. Enqueue a fresh one under the same key: it is a no-op if the queue
            // already holds a pending or leased row, which is what tells a healthy round from a stuck one.
            var enqueued = await _jobs.EnqueueAsync(
                new JobEnqueueRequest
                {
                    JobType = jobType,
                    BusinessKey = businessKey,
                    PayloadJson = MatchdayJobPayload.For(matchdayId),
                    DueAt = now,
                },
                cancellationToken);

            if (!enqueued)
            {
                await transaction.RollbackAsync(cancellationToken);

                return new ResumeMatchdayResult(ResumeMatchdayOutcome.NotResumable, matchdayId, step, false);
            }
        }

        _audit.Record(new AuditEntry(
            AdminAuditActions.MatchdayResumed,
            AuditActorTypes.User,
            _requestContext.ActorUserId,
            AuditTargetTypes.Matchday,
            matchdayId,
            _requestContext.CorrelationId,
            IpHash: null,
            Reason: reason));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogResumed(matchdayId, step, requeued);

        return new ResumeMatchdayResult(ResumeMatchdayOutcome.Resumed, matchdayId, step, requeued);
    }

    [LoggerMessage(
        EventId = 5232,
        Level = LogLevel.Warning,
        Message = "An operator resumed matchday {MatchdayId} at step {Step}; requeued={Requeued} (F-46, ADR-0044).")]
    private partial void LogResumed(Guid matchdayId, string step, bool requeued);
}
