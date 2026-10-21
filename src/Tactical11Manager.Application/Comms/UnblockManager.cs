using Tactical11Manager.Application.Abstractions.Comms;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Application.Abstractions.World;

namespace Tactical11Manager.Application.Comms;

/// <summary>
/// Lifts the caller's block on another manager (`COM-15`).
/// </summary>
/// <remarks>
/// Idempotent: lifting a block that is not there succeeds and writes nothing. Only the caller's own block is
/// ever removed. A block the other manager placed on the caller stays, so unblocking never opens a door the
/// other side shut.
/// </remarks>
public sealed class UnblockManager
{
    private readonly IDirectMessageRepository _messages;
    private readonly IManagerRepository _managers;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the command.</summary>
    public UnblockManager(
        IDirectMessageRepository messages,
        IManagerRepository managers,
        IUnitOfWork unitOfWork)
    {
        _messages = messages;
        _managers = managers;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Lifts the block.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="managerId">The manager to unblock.</param>
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

        var block = await _messages.FindBlockAsync(blocker.Id, managerId, cancellationToken);

        if (block is null)
        {
            return DirectMessageOutcome.Ok;
        }

        _messages.Remove(block);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return DirectMessageOutcome.Ok;
    }
}
