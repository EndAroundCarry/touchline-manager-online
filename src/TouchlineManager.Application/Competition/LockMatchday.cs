using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Application.Match;
using TouchlineManager.Domain.Competition;

namespace TouchlineManager.Application.Competition;

/// <summary>What locking a matchday did.</summary>
public enum LockMatchdayOutcome
{
    /// <summary>At least one fixture was frozen by this call.</summary>
    Locked = 0,

    /// <summary>Every fixture of the round was already past its lock, so nothing was done.</summary>
    AlreadyLocked = 1,

    /// <summary>The matchday does not exist.</summary>
    MatchdayNotFound = 2,
}

/// <summary>The result of locking a matchday.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="FrozenFixtures">How many fixtures had their input frozen by this call.</param>
/// <param name="Repairs">How many slots the builder decided rather than the clubs.</param>
public sealed record LockMatchdayResult(LockMatchdayOutcome Outcome, int FrozenFixtures, int Repairs);

/// <summary>
/// Locks a division's round: freezes every fixture's immutable input and closes every team sheet
/// (`CAL-3`, `SQ-7`, master plan §7.3).
/// </summary>
/// <remarks>
/// <para>
/// The round is the unit, and one transaction covers it. A half-locked round — three fixtures frozen and
/// six still open — would be a state where some managers can still edit a side and others cannot, for no
/// reason the rules give; committing the whole round or none of it makes a retried job find either nothing
/// done or everything done, which is what an at-least-once queue needs (§7.1, ADR-0003).
/// </para>
/// <para>
/// Locking does not simulate. It is the deadline that makes a result reproducible: from here until the
/// match is played, training, injuries, transfers, and tactics changes cannot touch what was frozen
/// (`MAT-1`).
/// </para>
/// <para>
/// A fixture that cannot be frozen at all — a club with no available goalkeeper, say — refuses the whole
/// round and names the club. That is deliberate: a round is published as a unit (`MAT-7`), so a round that
/// cannot be played cannot be half-locked either, and an operator has to see it rather than the world
/// quietly inventing a side (§7.4.9).
/// </para>
/// </remarks>
public sealed class LockMatchday
{
    private readonly IMatchdayRepository _matchdays;
    private readonly MatchSnapshotFactory _snapshots;
    private readonly IAdvisoryLock _locks;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    /// <summary>Initializes the use case.</summary>
    public LockMatchday(
        IMatchdayRepository matchdays,
        MatchSnapshotFactory snapshots,
        IAdvisoryLock locks,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _matchdays = matchdays;
        _snapshots = snapshots;
        _locks = locks;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <summary>Locks one matchday.</summary>
    /// <param name="matchdayId">The matchday to lock.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<LockMatchdayResult> ExecuteAsync(Guid matchdayId, CancellationToken cancellationToken)
    {
        var workload = await _matchdays.LoadMatchdayAsync(matchdayId, cancellationToken);

        if (workload is null)
        {
            return new LockMatchdayResult(LockMatchdayOutcome.MatchdayNotFound, 0, 0);
        }

        var now = _clock.UtcNow;
        var frozen = 0;
        var repairs = 0;

        // One transaction, one advisory lock, one round. The lock is held for the round's whole critical
        // section so a resolution that came due at the same moment — which happens when the worker was down
        // across both deadlines — cannot freeze the same fixture alongside this job.
        await using var transaction = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.ReadCommitted,
            cancellationToken);

        await _locks.AcquireAsync(AdvisoryLockKey.Matchday(matchdayId), cancellationToken);

        foreach (var fixture in workload.Fixtures.Where(fixture => fixture.Status == FixtureStatus.Scheduled))
        {
            var sides = await _matchdays.LoadFixtureSidesAsync(fixture.Id, cancellationToken);

            if (sides is null)
            {
                continue;
            }

            var snapshot = await _snapshots.FreezeAsync(sides, cancellationToken);

            if (snapshot.Built)
            {
                frozen++;
            }

            repairs += snapshot.Repairs.Count;

            foreach (var sheet in sides.Sheets.Where(sheet => sheet.IsEditable))
            {
                sheet.Lock(now);
            }

            fixture.Lock(now);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return frozen > 0
            ? new LockMatchdayResult(LockMatchdayOutcome.Locked, frozen, repairs)
            : new LockMatchdayResult(LockMatchdayOutcome.AlreadyLocked, 0, 0);
    }
}
