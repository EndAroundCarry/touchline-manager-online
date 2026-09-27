using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Comms;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.World;

namespace TouchlineManager.Application.Comms;

/// <summary>
/// Marks a manager's inbox read (master plan §10.7).
/// </summary>
/// <remarks>
/// <para>
/// One message or all of them, because the screen offers both and the two rules are the same: only the
/// caller's own messages are loaded, and marking an already-read one is a no-op. That no-op is what makes a
/// retried command safe — it cannot advance a version or move a timestamp for a message that was already
/// read.
/// </para>
/// <para>
/// It reads before it writes for the same reason the team-sheet save does: the recipient comes from the
/// account and the message must be one of theirs, so a message that is not theirs is answered as one that
/// does not exist rather than disclosing that it does (`§10.9`).
/// </para>
/// </remarks>
public sealed class MarkInboxMessagesRead
{
    private readonly IInboxRepository _messages;
    private readonly IWorldRepository _world;
    private readonly IManagerRepository _managers;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    /// <summary>Initializes the command.</summary>
    public MarkInboxMessagesRead(
        IInboxRepository messages,
        IWorldRepository world,
        IManagerRepository managers,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _messages = messages;
        _world = world;
        _managers = managers;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <summary>Marks one message read.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="messageId">The message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<CommsOutcome> ExecuteAsync(
        Guid userId,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        var (outcome, managerId) = await ResolveAsync(userId, cancellationToken);

        if (outcome != CommsOutcome.Ok)
        {
            return outcome;
        }

        var message = await _messages.FindAsync(managerId, messageId, cancellationToken);

        if (message is null)
        {
            return CommsOutcome.MessageNotFound;
        }

        message.MarkRead(_clock.UtcNow);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return CommsOutcome.Ok;
    }

    /// <summary>Marks every unread message read.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<CommsOutcome> ExecuteAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        var (outcome, managerId) = await ResolveAsync(userId, cancellationToken);

        if (outcome != CommsOutcome.Ok)
        {
            return outcome;
        }

        var messages = await _messages.LoadUnreadAsync(managerId, cancellationToken);
        var now = _clock.UtcNow;

        foreach (var message in messages)
        {
            message.MarkRead(now);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return CommsOutcome.Ok;
    }

    private async Task<(CommsOutcome Outcome, Guid ManagerId)> ResolveAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var world = await _world.FindWorldAsync(cancellationToken);

        if (world is null)
        {
            return (CommsOutcome.WorldNotSeeded, Guid.Empty);
        }

        var manager = await _managers.FindByUserIdAsync(userId, cancellationToken);

        return manager is null
            ? (CommsOutcome.NoManagerProfile, Guid.Empty)
            : (CommsOutcome.Ok, manager.Id);
    }
}
