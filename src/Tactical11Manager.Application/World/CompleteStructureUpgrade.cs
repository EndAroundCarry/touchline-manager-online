using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Comms;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Domain.Comms;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Application.World;

/// <summary>What finishing a building's upgrade did.</summary>
public enum CompleteStructureUpgradeOutcome
{
    /// <summary>The level rose.</summary>
    Completed = 0,

    /// <summary>Nothing was being built to that level, so a retried or stale job did nothing.</summary>
    NothingToComplete = 1,

    /// <summary>The building does not exist.</summary>
    NotFound = 2,
}

/// <summary>
/// Finishes an upgrade when its build time is up and tells the manager (`STRC-3`).
/// </summary>
/// <remarks>
/// Idempotent on the building's state, which is the guarantee the queue's at-least-once delivery needs: the
/// level rises once, the message is written once, and a second delivery finds nothing pending and changes
/// nothing. The level the job names must be the level being built, so a job left over from an earlier order
/// cannot finish a later one.
/// </remarks>
public sealed class CompleteStructureUpgrade
{
    private readonly IStructureRepository _structures;
    private readonly IInboxRepository _inbox;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public CompleteStructureUpgrade(
        IStructureRepository structures,
        IInboxRepository inbox,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _structures = structures;
        _inbox = inbox;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Finishes the upgrade, or does nothing.</summary>
    /// <param name="structureId">The building.</param>
    /// <param name="targetLevel">The level the job completes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<CompleteStructureUpgradeOutcome> ExecuteAsync(
        Guid structureId,
        int targetLevel,
        CancellationToken cancellationToken)
    {
        var building = await _structures.FindAsync(structureId, cancellationToken);

        if (building is null)
        {
            return CompleteStructureUpgradeOutcome.NotFound;
        }

        if (building.UpgradeTargetLevel != targetLevel)
        {
            return CompleteStructureUpgradeOutcome.NothingToComplete;
        }

        var now = _clock.UtcNow;

        if (!building.CompleteUpgrade(now))
        {
            return CompleteStructureUpgradeOutcome.NothingToComplete;
        }

        // An AI club has no manager to tell, and never orders an upgrade anyway (`STRC-2`).
        var targets = await _inbox.FindClubTargetsAsync([building.ClubId], cancellationToken);

        if (targets.FirstOrDefault(target => target.ClubId == building.ClubId)?.ManagerId is { } manager
            && manager != Guid.Empty)
        {
            var draft = StructureInboxTemplates.UpgradeCompletedMessage(
                building.Kind.ToCode(),
                building.Level,
                building.Id);

            _inbox.Add(InboxMessage.Record(
                Guid.CreateVersion7(),
                manager,
                draft.Category,
                draft.TemplateKey,
                draft.ParametersJson,
                draft.RelatedEntityId,
                now));
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return CompleteStructureUpgradeOutcome.Completed;
    }
}
