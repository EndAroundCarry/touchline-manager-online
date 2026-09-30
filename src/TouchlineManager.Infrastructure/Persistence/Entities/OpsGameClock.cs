namespace TouchlineManager.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence model for the single stepped game-clock row in <c>ops.game_clock</c> (ADR-0049, `TIME-6`).
/// </summary>
/// <remarks>
/// This is the ops module's storage shape, not a domain aggregate: a stepped world holds exactly one row,
/// fixed by identity, and the instant only moves when an operator advances it. Reads and writes go through
/// <c>IGameClockStore</c>.
/// </remarks>
public sealed class OpsGameClock
{
    /// <summary>Gets or sets the row identity. Fixed, because the table holds exactly one row.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the current game instant.</summary>
    public DateTimeOffset GameNow { get; set; }

    /// <summary>Gets or sets when the row was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Gets or sets the optimistic concurrency version.</summary>
    public long Version { get; set; }
}
