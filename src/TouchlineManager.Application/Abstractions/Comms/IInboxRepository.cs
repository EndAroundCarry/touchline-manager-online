using TouchlineManager.Domain.Comms;

namespace TouchlineManager.Application.Abstractions.Comms;

/// <summary>One club a message can be addressed to, with its name (`DIS-7`, F-41).</summary>
/// <remarks>
/// The manager is null for a club nobody holds, because an AI club has nobody to tell. The name is carried
/// rather than looked up separately because a message that names an opponent or a player needs it, and two
/// reads that must agree are two reads that can disagree.
/// </remarks>
/// <param name="ClubId">The club.</param>
/// <param name="ManagerId">The manager with the club's open tenure, or null when it is AI-controlled.</param>
/// <param name="Name">The club's generated name.</param>
public sealed record ClubInboxTarget(Guid ClubId, Guid? ManagerId, string Name);

/// <summary>
/// The comms module's staging port: the inbox messages a workflow writes and the recipients it addresses them
/// to (master plan §6.9).
/// </summary>
/// <remarks>
/// It reads as well as stages, which the module's other write ports generally do not, because addressing a
/// message needs the club's name and whoever holds it. That is a cross-module read, which §5.2 allows: the
/// write that follows still happens through the use case that owns the transaction. It stages and never
/// saves, so a round's messages commit with the results that produced them.
/// </remarks>
public interface IInboxRepository
{
    /// <summary>Stages a message.</summary>
    /// <param name="message">The message.</param>
    void Add(InboxMessage message);

    /// <summary>Finds one of a manager's messages, tracked, so it can be marked read.</summary>
    /// <param name="managerId">The recipient.</param>
    /// <param name="messageId">The message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<InboxMessage?> FindAsync(Guid managerId, Guid messageId, CancellationToken cancellationToken);

    /// <summary>Loads a manager's unread messages, tracked, so they can be marked read.</summary>
    /// <param name="managerId">The recipient.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<InboxMessage>> LoadUnreadAsync(Guid managerId, CancellationToken cancellationToken);

    /// <summary>Reads the name and the holder of each club a message may be addressed to.</summary>
    /// <param name="clubIds">The clubs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<ClubInboxTarget>> FindClubTargetsAsync(
        IReadOnlyCollection<Guid> clubIds,
        CancellationToken cancellationToken);

    /// <summary>Reads the displayed name of each player a message may name.</summary>
    /// <param name="playerIds">The players.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyDictionary<Guid, string>> FindPlayerNamesAsync(
        IReadOnlyCollection<Guid> playerIds,
        CancellationToken cancellationToken);
}
