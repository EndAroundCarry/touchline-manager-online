using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Abstractions.Match;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Application.Jobs;
using TouchlineManager.Application.Match;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Match;
using TouchlineManager.MatchEngine;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.Application.Competition;

/// <summary>What resolving a matchday did.</summary>
public enum ResolveMatchdayOutcome
{
    /// <summary>Every fixture of the round now has a staged result.</summary>
    Resolved = 0,

    /// <summary>The round already had its results, or had already published.</summary>
    AlreadyResolved = 1,

    /// <summary>The matchday does not exist.</summary>
    MatchdayNotFound = 2,
}

/// <summary>The result of resolving a matchday.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Simulated">How many fixtures were simulated by this call.</param>
/// <param name="Staged">How many fixtures the round now has staged.</param>
public sealed record ResolveMatchdayResult(ResolveMatchdayOutcome Outcome, int Simulated, int Staged);

/// <summary>
/// Simulates a division's round from its frozen snapshots and stages the results (`MAT-7`, §7.4).
/// </summary>
/// <remarks>
/// <para>
/// Each fixture is simulated and staged in its own transaction, which is what makes a round resumable: a
/// worker that dies after six of nine fixtures has six staged results that a retry finds and leaves alone,
/// rather than nine fixtures to redo. Nothing about a result is public yet — staging is the state where a
/// scoreline exists and the world cannot see it (`MAT-7`).
/// </para>
/// <para>
/// A snapshot is never taken here unless the lock workflow never ran. §7.3 says a delayed lock blocks
/// kickoff rather than letting a match be simulated from live tables, and taking the snapshot itself is
/// how that is honoured: the fixture is frozen with the same rules and the same determinism, minutes late,
/// and nothing else changes about it.
/// </para>
/// <para>
/// The engine is called with an explicitly supplied rules set and refuses a snapshot frozen against a
/// different one, so a result can only ever be produced by the configuration its input names (`MAT-9`).
/// </para>
/// </remarks>
public sealed class ResolveMatchday
{
    private readonly IMatchdayRepository _matchdays;
    private readonly IMatchRepository _matches;
    private readonly MatchSnapshotFactory _snapshots;
    private readonly IAdvisoryLock _locks;
    private readonly IJobQueue _jobs;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    /// <summary>Initializes the use case.</summary>
    public ResolveMatchday(
        IMatchdayRepository matchdays,
        IMatchRepository matches,
        MatchSnapshotFactory snapshots,
        IAdvisoryLock locks,
        IJobQueue jobs,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _matchdays = matchdays;
        _matches = matches;
        _snapshots = snapshots;
        _locks = locks;
        _jobs = jobs;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <summary>Resolves one matchday.</summary>
    /// <param name="matchdayId">The matchday to resolve.</param>
    /// <param name="jobId">The job driving the attempt, when one is.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="UnplayableSquadException">When a delayed lock finds a club that cannot field a side.</exception>
    /// <exception cref="InvalidMatchInputException">When a stored snapshot no longer reproduces itself.</exception>
    public async Task<ResolveMatchdayResult> ExecuteAsync(
        Guid matchdayId,
        Guid? jobId,
        CancellationToken cancellationToken)
    {
        var workload = await _matchdays.LoadMatchdayAsync(matchdayId, cancellationToken);

        if (workload is null)
        {
            return new ResolveMatchdayResult(ResolveMatchdayOutcome.MatchdayNotFound, 0, 0);
        }

        if (workload.Matchday.PublicationStatus != MatchdayPublicationStatus.Pending)
        {
            return new ResolveMatchdayResult(ResolveMatchdayOutcome.AlreadyResolved, 0, workload.Fixtures.Count);
        }

        var simulated = 0;

        foreach (var fixture in workload.Fixtures.Where(NeedsSimulation))
        {
            var snapshot = await EnsureSnapshotAsync(matchdayId, fixture, cancellationToken);

            await SimulateAsync(fixture, snapshot, jobId, cancellationToken);

            simulated++;
        }

        if (workload.AllFixturesStaged)
        {
            // Staged and the publish job in one transaction: a round whose results are all in but whose
            // publication was never queued would sit invisible until an operator noticed (ADR-0003).
            await using var transaction = await _unitOfWork.BeginTransactionAsync(
                TransactionIsolation.ReadCommitted,
                cancellationToken);

            workload.Matchday.MarkStaged(_clock.UtcNow);

            await _jobs.EnqueueAsync(
                new JobEnqueueRequest
                {
                    JobType = MatchdayJobTypes.Publish,
                    BusinessKey = MatchdayJobTypes.PublishKey(matchdayId),
                    DueAt = _clock.UtcNow,
                    PayloadJson = MatchdayJobPayload.For(matchdayId),
                },
                cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }

        return new ResolveMatchdayResult(
            ResolveMatchdayOutcome.Resolved,
            simulated,
            workload.Fixtures.Count(IsStaged));
    }

    /// <summary>Whether a fixture still has to be simulated.</summary>
    private static bool NeedsSimulation(Fixture fixture) =>
        fixture.Status is FixtureStatus.Scheduled or FixtureStatus.Locked or FixtureStatus.Simulating;

    private static bool IsStaged(Fixture fixture) =>
        fixture.Status is FixtureStatus.Staged or FixtureStatus.Published or FixtureStatus.Void;

    /// <summary>
    /// Reads the fixture's frozen input, taking it now if the lock workflow never did, and commits the
    /// fixture's own transition to locked with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The snapshot and the fixture's lock state commit together, because each without the other is a state
    /// the next run cannot use: a snapshot without a locked fixture would have a retry simulate from a
    /// fixture that is still open to edits, and a locked fixture without a snapshot would have nothing to
    /// simulate from.
    /// </para>
    /// <para>
    /// The matchday's advisory lock is held while the snapshot is taken. It is the same lock the lock
    /// workflow takes, so the two can never freeze one fixture between them — which is possible only when
    /// both deadlines are overdue at once, and is exactly the recovery the queue has to handle (§7.3).
    /// </para>
    /// </remarks>
    private async Task<InputSnapshot> EnsureSnapshotAsync(
        Guid matchdayId,
        Fixture fixture,
        CancellationToken cancellationToken)
    {
        var sides = await _matchdays.LoadFixtureSidesAsync(fixture.Id, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Fixture {fixture.Id:D} is on a matchday but has no clubs to read.");

        await using var transaction = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.ReadCommitted,
            cancellationToken);

        await _locks.AcquireAsync(AdvisoryLockKey.Matchday(matchdayId), cancellationToken);

        var snapshot = await _snapshots.FreezeAsync(sides, cancellationToken);

        if (fixture.Status == FixtureStatus.Scheduled)
        {
            fixture.Lock(_clock.UtcNow);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return snapshot.Snapshot;
    }

    /// <summary>Simulates one fixture and stages its result.</summary>
    private async Task SimulateAsync(
        Fixture fixture,
        InputSnapshot snapshot,
        Guid? jobId,
        CancellationToken cancellationToken)
    {
        var rules = EngineRulesV1.Default;
        var input = MatchSnapshotFactory.ReadVerified(snapshot);
        var startedAt = _clock.UtcNow;

        // The fixture is marked as simulating before the engine is called, in its own committed step, so a
        // run that dies mid-simulation is visible as one that started rather than one that never happened.
        if (fixture.Status == FixtureStatus.Locked)
        {
            await using var marking = await _unitOfWork.BeginTransactionAsync(
                TransactionIsolation.ReadCommitted,
                cancellationToken);

            fixture.BeginSimulation(startedAt);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await marking.CommitAsync(cancellationToken);
        }

        MatchResultV1 result;

        try
        {
            result = MatchSimulator.Simulate(input, rules);
        }
        catch (InvalidMatchInputException exception)
        {
            await RecordFailureAsync(fixture, snapshot, jobId, startedAt, exception, cancellationToken);

            throw new PermanentJobFailureException(
                $"Fixture {fixture.Id:D} cannot be simulated from its frozen snapshot: {exception.Message}",
                exception);
        }

        var completedAt = _clock.UtcNow;
        var matchId = Guid.CreateVersion7();
        var attemptId = Guid.CreateVersion7();
        var attemptNumber = await _matches.CountAttemptsAsync(fixture.Id, cancellationToken) + 1;

        await using var staging = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.ReadCommitted,
            cancellationToken);

        _matches.AddMatch(SimulatedMatch.Record(
            matchId,
            fixture.Id,
            result.EngineVersion,
            result.RuleSetVersion,
            snapshot.SeedCommitment,
            result.HomeGoals,
            result.AwayGoals,
            MatchStatisticsDocument.Write(result),
            result.InputHash,
            result.OutputHash,
            attemptId,
            startedAt,
            completedAt));

        _matches.AddAttempt(SimulationAttempt.Succeeded(
            attemptId,
            fixture.Id,
            jobId,
            attemptNumber,
            result.EngineVersion,
            result.InputHash,
            result.OutputHash,
            startedAt,
            completedAt));

        _matches.AddEvents(
            result.Events.Select(matchEvent => MatchEvent.Record(
                Guid.CreateVersion7(),
                matchId,
                matchEvent.Sequence,
                matchEvent.Minute,
                matchEvent.StoppageMinute,
                matchEvent.ClubId,
                EngineVocabulary.EventType(matchEvent.Type),
                matchEvent.ParticipantId,
                matchEvent.SecondaryParticipantId,
                matchEvent.Zone is { } zone ? EngineVocabulary.Zone(zone) : null,
                matchEvent.QualityBasisPoints,
                matchEvent.AbsenceFixtures,
                matchEvent.SubstitutionReason is { } reason
                    ? EngineVocabulary.SubstitutionCause(reason)
                    : null)));

        fixture.Stage(result.HomeGoals, result.AwayGoals, matchId, completedAt);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await staging.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Records an attempt that produced nothing, which is what operations reads when a round is stuck.
    /// </summary>
    /// <remarks>
    /// The failure is committed on its own, before the exception unwinds, because a rollback would take the
    /// record of the failure with it and leave the only evidence in a log file.
    /// </remarks>
    private async Task RecordFailureAsync(
        Fixture fixture,
        InputSnapshot snapshot,
        Guid? jobId,
        DateTimeOffset startedAt,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var completedAt = _clock.UtcNow;
        var attemptNumber = await _matches.CountAttemptsAsync(fixture.Id, cancellationToken) + 1;

        await using var recording = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.ReadCommitted,
            cancellationToken);

        _matches.AddAttempt(SimulationAttempt.Failed(
            Guid.CreateVersion7(),
            fixture.Id,
            jobId,
            attemptNumber,
            snapshot.EngineVersion,
            exception is InvalidMatchInputException ? "invalid-input" : exception.GetType().Name,
            exception.Message,
            startedAt,
            completedAt));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await recording.CommitAsync(cancellationToken);
    }
}
