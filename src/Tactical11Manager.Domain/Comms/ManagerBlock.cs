namespace Tactical11Manager.Domain.Comms;

/// <summary>
/// One manager's block on another: while it exists, in either direction, neither can start a conversation
/// with the other or reply to them (`COM-15`).
/// </summary>
/// <remarks>
/// Existing messages stay readable. A block is the whole fact, so unblocking deletes the row rather than
/// flipping a flag, and there is nothing to version.
/// </remarks>
public sealed class ManagerBlock
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private ManagerBlock()
    {
    }

    /// <summary>Gets the manager who blocked.</summary>
    public Guid BlockerManagerId { get; private set; }

    /// <summary>Gets the manager who was blocked.</summary>
    public Guid BlockedManagerId { get; private set; }

    /// <summary>Gets when the block was made.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Blocks another manager.</summary>
    /// <param name="blockerManagerId">The manager who blocks.</param>
    /// <param name="blockedManagerId">The manager who is blocked.</param>
    /// <param name="now">The current instant.</param>
    public static ManagerBlock Create(Guid blockerManagerId, Guid blockedManagerId, DateTimeOffset now)
    {
        if (blockerManagerId == Guid.Empty || blockedManagerId == Guid.Empty)
        {
            throw new ArgumentException("A block is between two managers.", nameof(blockerManagerId));
        }

        if (blockerManagerId == blockedManagerId)
        {
            throw new ArgumentException("A manager cannot block themselves.", nameof(blockedManagerId));
        }

        return new ManagerBlock
        {
            BlockerManagerId = blockerManagerId,
            BlockedManagerId = blockedManagerId,
            CreatedAt = now,
        };
    }
}
