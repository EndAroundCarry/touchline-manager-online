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

/// <summary>What resuming a rollover did.</summary>
public enum ResumeSeasonRolloverOutcome
{
    /// <summary>The failed rollover was retried and its job requeued.</summary>
    Resumed = 0,

    /// <summary>The rollover already completed; there is nothing to resume.</summary>
    AlreadyCompleted = 1,

    /// <summary>The rollover has not failed; the queue is already responsible for it.</summary>
    NotFailed = 2,

    /// <summary>No rollover exists for the season.</summary>
    NotFound = 3,
}

/// <summary>The result of resuming a rollover.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="SeasonId">The closing season.</param>
/// <param name="RolloverId">The rollover row, when one exists.</param>
/// <param name="BusinessKey">The business key of the rollover job.</param>
/// <param name="Requeued"><see langword="true"/> when a dead-lettered job row was reset.</param>
public sealed record ResumeSeasonRolloverResult(
    ResumeSeasonRolloverOutcome Outcome,
    Guid SeasonId,
    Guid? RolloverId,
    string BusinessKey,
    bool Requeued);

/// <summary>
/// Resumes a failed season rollover on an operator's authority: retries the checkpoint and returns its
/// dead-lettered job to the queue (`PR-4`, ADR-0031, ADR-0034).
/// </summary>
/// <remarks>
/// <para>
/// A rollover stops permanently when preflight finds drift, and the queue cannot re-run a dead-lettered
/// job. This is the operator decision ADR-0031 anticipated: it returns the row to
/// <see cref="SeasonRolloverPhase.Started"/> — every phase is idempotent, so a resume re-does nothing it
/// already did — records who resumed it and why, and requeues the job. The worker still does all of the
/// work; this only puts the job back on the queue.
/// </para>
/// <para>
/// Reachable only from a development-flagged endpoint and never mapped in production (§17.12).
/// </para>
/// </remarks>
public sealed partial class ResumeSeasonRollover
{
    /// <summary>The longest operator reason the audit trail stores.</summary>
    public const int ReasonMaxLength = 200;

    private readonly IClock _clock;
    private readonly IWorldRepository _world;
    private readonly ISeasonRolloverRepository _rollovers;
    private readonly IJobQueue _jobs;
    private readonly IAdvisoryLock _locks;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ResumeSeasonRollover> _logger;

    /// <summary>Initializes the use case.</summary>
    public ResumeSeasonRollover(
        IClock clock,
        IWorldRepository world,
        ISeasonRolloverRepository rollovers,
        IJobQueue jobs,
        IAdvisoryLock locks,
        IAuditWriter audit,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork,
        ILogger<ResumeSeasonRollover> logger)
    {
        _clock = clock;
        _world = world;
        _rollovers = rollovers;
        _jobs = jobs;
        _locks = locks;
        _audit = audit;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>Retries a failed rollover and requeues its job.</summary>
    /// <param name="seasonId">The closing season.</param>
    /// <param name="reason">Why the operator is resuming it. Required, and stored in the audit trail.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ResumeSeasonRolloverResult> ExecuteAsync(
        Guid seasonId,
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

        var world = await _world.FindWorldAsync(cancellationToken);

        if (world is null)
        {
            return NotFound(seasonId);
        }

        var season = await _world.FindSeasonByIdAsync(seasonId, cancellationToken);

        if (season is null || season.WorldId != world.Id)
        {
            return NotFound(seasonId);
        }

        var businessKey = SeasonRolloverJobTypes.RolloverKey(seasonId);

        await using var transaction = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.ReadCommitted,
            cancellationToken);
        await _locks.AcquireAsync(AdvisoryLockKey.SeasonRollover(world.Id), cancellationToken);

        var rollover = await _rollovers.FindBySeasonAsync(world.Id, seasonId, cancellationToken);

        if (rollover is null)
        {
            await transaction.RollbackAsync(cancellationToken);

            return NotFound(seasonId);
        }

        if (rollover.Phase == SeasonRolloverPhase.Completed)
        {
            await transaction.RollbackAsync(cancellationToken);

            return new ResumeSeasonRolloverResult(
                ResumeSeasonRolloverOutcome.AlreadyCompleted,
                seasonId,
                rollover.Id,
                businessKey,
                Requeued: false);
        }

        if (rollover.Phase != SeasonRolloverPhase.Failed)
        {
            // It is mid-flight or not started; the queue owns it, so a resume is a no-op.
            await transaction.RollbackAsync(cancellationToken);

            return new ResumeSeasonRolloverResult(
                ResumeSeasonRolloverOutcome.NotFailed,
                seasonId,
                rollover.Id,
                businessKey,
                Requeued: false);
        }

        var now = _clock.UtcNow;
        rollover.Retry(now);

        _audit.Record(new AuditEntry(
            WorldAuditActions.RolloverResumed,
            AuditActorTypes.User,
            _requestContext.ActorUserId,
            AuditTargetTypes.SeasonRollover,
            rollover.Id,
            _requestContext.CorrelationId,
            IpHash: null,
            Reason: reason));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var requeued = await _jobs.RequeueAsync(
            SeasonRolloverJobTypes.Rollover,
            businessKey,
            cancellationToken);

        if (!requeued)
        {
            // No dead-lettered row exists (it was never written, or has been cleaned up). Enqueue a fresh one
            // under the same key so the resumed rollover has a job to run; the key makes it a no-op if the row
            // is, in fact, already present.
            await _jobs.EnqueueAsync(
                new JobEnqueueRequest
                {
                    JobType = SeasonRolloverJobTypes.Rollover,
                    BusinessKey = businessKey,
                    PayloadJson = SeasonRolloverJobPayload.For(seasonId),
                    DueAt = now,
                },
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        LogResumed(seasonId, rollover.Id, requeued);

        return new ResumeSeasonRolloverResult(
            ResumeSeasonRolloverOutcome.Resumed,
            seasonId,
            rollover.Id,
            businessKey,
            requeued);
    }

    private static ResumeSeasonRolloverResult NotFound(Guid seasonId) =>
        new(ResumeSeasonRolloverOutcome.NotFound, seasonId, null, string.Empty, Requeued: false);

    [LoggerMessage(
        EventId = 5220,
        Level = LogLevel.Warning,
        Message = "An operator resumed the failed rollover {RolloverId} of season {SeasonId}; "
            + "requeued={Requeued} (PR-4, ADR-0031).")]
    private partial void LogResumed(Guid seasonId, Guid rolloverId, bool requeued);
}
