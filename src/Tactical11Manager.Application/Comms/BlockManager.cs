using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Comms;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Domain.Comms;

namespace Tactical11Manager.Application.Comms;

/// <summary>
/// Blocks another manager, so neither of them can start a conversation with the other or reply to them
/// (`COM-15`).
/// </summary>
/// <remarks>
/// Idempotent: blocking someone already blocked succeeds and writes nothing, so a retried request or a
/// double click is harmless. Existing messages stay readable. A manager cannot block themselves, and an
/// identity that is not a manager is answered as not found.
/// </remarks>
public sealed class BlockManager
{
    private readonly IDirectMessageRepository _messages;
    private readonly IManagerRepository _managers;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    /// <summary>Initializes the command.</summary>
    public BlockManager(
        IDirectMessageRepository messages,
        IManagerRepository managers,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _messages = messages;
        _managers = managers;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <summary>Blocks the manager.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="managerId">The manager to block.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<DirectMessageOutcome> ExecuteAsync(
        Guid userId,
        Guid managerId,
        CancellationToken cancellationToken)
    {
        var blocker = await _managers.FindByUserIdAsync(userId, cancellationToken);

        if (blocker is null)
        {
            return DirectMessageOutcome.NoManagerProfile;
        }

        if (blocker.Id == managerId || await _managers.FindByIdAsync(managerId, cancellationToken) is null)
        {
            return DirectMessageOutcome.ManagerNotFound;
        }

        if (await _messages.FindBlockAsync(blocker.Id, managerId, cancellationToken) is not null)
        {
            return DirectMessageOutcome.Ok;
        }

        _messages.Add(ManagerBlock.Create(blocker.Id, managerId, _clock.UtcNow));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return DirectMessageOutcome.Ok;
    }
}
