namespace TouchlineManager.Domain.Competition;

/// <summary>
/// The durable, resumable record of one world's season rollover (master plan §7.5, ADR-0031).
/// </summary>
/// <remarks>
/// <para>
/// Rollover is the one workflow the plan calls a genuine singleton: one season per world closes at a time,
/// and a partially-applied rollover — half the countries promoted, the game year not yet advanced — is a
/// corrupt world. This row is the checkpoint that keeps that from happening. A worker takes the world's
/// advisory lock, reads this row, and resumes at <see cref="Phase"/> rather than from the top; each phase
/// commits its own work together with the phase it reached, so a crash leaves a resumable state rather than
/// a half-finished movement.
/// </para>
/// <para>
/// <c>unique (world_id, season_id)</c> means a closing season has exactly one rollover, however many times
/// the materialiser runs or the queue redelivers. The transitions are idempotent for the same reason the
/// matchday transitions are: a repeat of a step that has already happened must be a no-op, because the
/// queue's at-least-once delivery will repeat it.
/// </para>
/// </remarks>
public sealed class SeasonRollover
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private SeasonRollover()
    {
    }

    /// <summary>Gets the identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the world whose season is closing.</summary>
    public Guid WorldId { get; private set; }

    /// <summary>Gets the season being rolled over.</summary>
    public Guid SeasonId { get; private set; }

    /// <summary>Gets the next season, once it has been created. Null until the move phase.</summary>
    public Guid? NextSeasonId { get; private set; }

    /// <summary>Gets the checkpoint reached.</summary>
    public SeasonRolloverPhase Phase { get; private set; }

    /// <summary>Gets why a failed rollover stopped. Retained for operations, never exposed to managers.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>Gets when the rollover was recorded.</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>Gets when the rollover completed.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Gets when the row was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the row was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Records a rollover for a closing season.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="worldId">The world whose season is closing.</param>
    /// <param name="seasonId">The closing season.</param>
    /// <param name="now">The current instant.</param>
    public static SeasonRollover Start(Guid id, Guid worldId, Guid seasonId, DateTimeOffset now)
    {
        if (worldId == Guid.Empty)
        {
            throw new ArgumentException("A rollover belongs to a world.", nameof(worldId));
        }

        if (seasonId == Guid.Empty)
        {
            throw new ArgumentException("A rollover closes a season.", nameof(seasonId));
        }

        return new SeasonRollover
        {
            Id = id,
            WorldId = worldId,
            SeasonId = seasonId,
            Phase = SeasonRolloverPhase.Started,
            StartedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>
    /// Records that preflight passed and the season was frozen (`PR-4`).
    /// </summary>
    /// <remarks>A repeat once the rollover is already frozen or further along is a no-op.</remarks>
    /// <param name="now">The current instant.</param>
    public void Freeze(DateTimeOffset now)
    {
        if (SeasonRolloverPhaseRules.IsAtLeast(Phase, SeasonRolloverPhase.Frozen))
        {
            return;
        }

        RequirePhase(SeasonRolloverPhase.Started, nameof(Freeze));

        Phase = SeasonRolloverPhase.Frozen;

        Touch(now);
    }

    /// <summary>Records that the standings are final and the closing entries are closed (`PR-4`).</summary>
    /// <param name="now">The current instant.</param>
    public void Finalize(DateTimeOffset now)
    {
        if (SeasonRolloverPhaseRules.IsAtLeast(Phase, SeasonRolloverPhase.Finalized))
        {
            return;
        }

        RequirePhase(SeasonRolloverPhase.Frozen, nameof(Finalize));

        Phase = SeasonRolloverPhase.Finalized;

        Touch(now);
    }

    /// <summary>Records that the next season exists and every club has been placed into it.</summary>
    /// <param name="nextSeasonId">The next season that was created.</param>
    /// <param name="now">The current instant.</param>
    public void Move(Guid nextSeasonId, DateTimeOffset now)
    {
        if (nextSeasonId == Guid.Empty)
        {
            throw new ArgumentException("A rollover moves into a season.", nameof(nextSeasonId));
        }

        if (SeasonRolloverPhaseRules.IsAtLeast(Phase, SeasonRolloverPhase.Moved))
        {
            return;
        }

        RequirePhase(SeasonRolloverPhase.Finalized, nameof(Move));

        NextSeasonId = nextSeasonId;
        Phase = SeasonRolloverPhase.Moved;

        Touch(now);
    }

    /// <summary>Seals the rollover: the closing season is completed and the next one is active (`PR-6`).</summary>
    /// <param name="now">The current instant.</param>
    public void Complete(DateTimeOffset now)
    {
        if (Phase == SeasonRolloverPhase.Completed)
        {
            return;
        }

        RequirePhase(SeasonRolloverPhase.Moved, nameof(Complete));

        Phase = SeasonRolloverPhase.Completed;
        CompletedAt = now;
        FailureReason = null;

        Touch(now);
    }

    /// <summary>
    /// Marks the rollover failed for an operator.
    /// </summary>
    /// <remarks>
    /// Deliberately not automatic. A preflight or generation failure means the world is not in the shape the
    /// rollover expects, and re-running it in a loop would turn a diagnosable fault into a hot loop against
    /// the database. <see cref="Retry"/> is how an operator resumes it.
    /// </remarks>
    /// <param name="reason">What went wrong. Retained for operations, never exposed to managers.</param>
    /// <param name="now">The current instant.</param>
    public void Fail(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (Phase == SeasonRolloverPhase.Completed)
        {
            throw new InvalidOperationException("A completed rollover cannot fail.");
        }

        Phase = SeasonRolloverPhase.Failed;
        FailureReason = reason;

        Touch(now);
    }

    /// <summary>
    /// Returns a failed rollover to the queue for another attempt.
    /// </summary>
    /// <remarks>
    /// The rollover restarts from <see cref="SeasonRolloverPhase.Started"/> and every phase is idempotent
    /// (each step is a find-or-create or a guarded close), so a retry re-does nothing it has already done.
    /// Reusing this row rather than creating a new one preserves the closing season's identity, which
    /// <c>unique (world_id, season_id)</c> requires.
    /// </remarks>
    /// <param name="now">The current instant.</param>
    public void Retry(DateTimeOffset now)
    {
        if (Phase != SeasonRolloverPhase.Failed)
        {
            throw new InvalidOperationException($"Only a failed rollover can be retried, not one in phase '{Phase}'.");
        }

        Phase = SeasonRolloverPhase.Started;
        NextSeasonId = null;
        FailureReason = null;
        CompletedAt = null;

        Touch(now);
    }

    private void RequirePhase(SeasonRolloverPhase expected, string transition)
    {
        if (Phase == SeasonRolloverPhase.Failed)
        {
            throw new InvalidOperationException(
                $"A failed rollover must be retried before it can {transition} (ADR-0031).");
        }

        if (Phase != expected)
        {
            throw new InvalidOperationException(
                $"A rollover can only {transition} from '{expected}', not from '{Phase}'.");
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
