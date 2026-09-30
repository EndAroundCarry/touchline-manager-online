namespace TouchlineManager.Application.Abstractions.Ops;

/// <summary>
/// The one persisted instant a stepped game clock reads, so the API and the worker agree on "now"
/// (ADR-0049, `TIME-6`).
/// </summary>
/// <remarks>
/// <para>
/// A stepped clock is frozen between steps: the value only changes when an operator advances it, which is
/// what makes a season reproducible press by press. Because the API and the worker are separate processes,
/// the instant is stored in the database rather than in either process, and each host refreshes it through
/// this port.
/// </para>
/// <para>
/// <see cref="Current"/> is a synchronous, never-blocking read of the last value this process observed, for
/// <c>IClock.UtcNow</c>; <see cref="ReadAsync"/> and <see cref="WriteAsync"/> touch the store. It is only
/// registered in non-production environments, and the clock that uses it refuses to start in Production
/// (`TIME-6`).
/// </para>
/// </remarks>
public interface IGameClockStore
{
    /// <summary>
    /// Gets the last instant this process observed, without touching the database.
    /// </summary>
    /// <remarks>
    /// It falls back to the configured starting instant until the first read, so a short-lived tool that
    /// never starts a host (the world seeder) still sees the right value.
    /// </remarks>
    DateTimeOffset Current { get; }

    /// <summary>Reads the stored instant, creating the row from the configured start on first use.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The instant now stored.</returns>
    Task<DateTimeOffset> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Writes a new instant, which every host observes on its next refresh.</summary>
    /// <param name="gameNow">The instant to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task WriteAsync(DateTimeOffset gameNow, CancellationToken cancellationToken);
}
