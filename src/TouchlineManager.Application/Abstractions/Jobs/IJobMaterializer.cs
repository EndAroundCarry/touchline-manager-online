namespace TouchlineManager.Application.Abstractions.Jobs;

/// <summary>
/// Turns one game instant into the deadline jobs that instant makes due.
/// </summary>
/// <remarks>
/// <para>
/// Each worker scheduler is normally the caller: it wakes on its own real-time interval, reads
/// <c>IClock.UtcNow</c>, and inserts the rows whose deadlines have arrived (ADR-0003). The stepped clock
/// makes a second caller useful — an operator's step must materialise the day's jobs at once rather than
/// wait out an interval that is measured in real seconds — so the same materialisation is exposed here and
/// invoked with the stepped instant.
/// </para>
/// <para>
/// Materialising is idempotent because every job carries a domain-derived business key, so calling a
/// materialiser from both a scheduler pass and a step is safe: the queue refuses the duplicate. It never
/// runs a job or produces a result, so the worker remains the only component that advances the game.
/// </para>
/// </remarks>
public interface IJobMaterializer
{
    /// <summary>Enqueues every deadline job that <paramref name="now"/> makes due.</summary>
    /// <param name="now">The game instant to materialise against.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MaterializeAsync(DateTimeOffset now, CancellationToken cancellationToken);
}
