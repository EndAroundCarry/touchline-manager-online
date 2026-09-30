namespace TouchlineManager.Application.Abstractions.Ops;

/// <summary>The stable keys of the operator's incident-control flags (`ops.feature_flags`).</summary>
public static class IncidentFlags
{
    /// <summary>The scope a world-wide incident flag is stored under.</summary>
    public const string WorldScope = "world";

    /// <summary>
    /// The flag that puts the game in read-only mode: while it is on, manager commands are refused and
    /// published content stays available (master plan §13, `F-51`).
    /// </summary>
    public const string ReadOnly = "incident.read_only";
}

/// <summary>The current incident-control state, as the game reads it.</summary>
/// <param name="Enabled">Whether read-only mode is on.</param>
/// <param name="Message">The operator's stated reason while read-only, otherwise null.</param>
public sealed record ReadOnlyModeState(bool Enabled, string? Message)
{
    /// <summary>The ordinary state: not read-only.</summary>
    public static readonly ReadOnlyModeState Off = new(false, null);
}

/// <summary>
/// Reads whether the game is in read-only mode (master plan §13, `F-51`).
/// </summary>
/// <remarks>
/// <para>
/// The operator sets the switch through the admin feature-flag command; this is the read that turns it into
/// behaviour. It is deliberately lenient — a flag that is unset or malformed means "not read-only", so only
/// an operator's explicit opt-in stops manager writes. The read is cheap and cached, because the request
/// gate consults it on every command.
/// </para>
/// <para>
/// The API is both the only writer and the only reader of the flag, so setting it invalidates this read and
/// the change takes effect immediately in the process that served the operator. The cache's own short
/// lifetime is the safety net for any reader that does not share that process.
/// </para>
/// </remarks>
public interface IReadOnlyMode
{
    /// <summary>Reads the current state.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ReadOnlyModeState> GetStateAsync(CancellationToken cancellationToken);

    /// <summary>Drops the cached state, so the next read goes back to the store.</summary>
    void Invalidate();
}
