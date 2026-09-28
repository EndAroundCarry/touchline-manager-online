using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Comms;
using TouchlineManager.Domain.Comms;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The comms module's staging and addressing: the messages a workflow writes and the managers they go to
/// (master plan §6.9).
/// </summary>
/// <remarks>
/// It stages and never saves, so a round's messages commit with the results, cards, and absences that
/// produced them. The club targets read joins the clubs to their open tenures, which is a cross-module read
/// the same way onboarding's occupancy count is: a read is allowed, and the write that follows is still the
/// workflow's own.
/// </remarks>
internal sealed class InboxRepository : IInboxRepository
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public InboxRepository(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public void Add(InboxMessage message) => _dbContext.InboxMessages.Add(message);

    /// <inheritdoc />
    public async Task<InboxMessage?> FindAsync(
        Guid managerId,
        Guid messageId,
        CancellationToken cancellationToken) =>
        await _dbContext.InboxMessages.FirstOrDefaultAsync(
            message => message.Id == messageId && message.RecipientManagerId == managerId,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<InboxMessage>> LoadUnreadAsync(
        Guid managerId,
        CancellationToken cancellationToken) =>
        await _dbContext.InboxMessages
            .Where(message => message.RecipientManagerId == managerId && message.ReadAt == null)
            .OrderBy(message => message.Id)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClubInboxTarget>> FindClubTargetsAsync(
        IReadOnlyCollection<Guid> clubIds,
        CancellationToken cancellationToken)
    {
        if (clubIds.Count == 0)
        {
            return [];
        }

        // A left join rather than a projection that calls FirstOrDefault, so a club nobody holds is still a
        // row with no manager: an AI club is a target that tells nobody.
        return await (
            from club in _dbContext.Clubs
            where clubIds.Contains(club.Id)
            join tenure in _dbContext.ClubTenures
                .Where(candidate => candidate.ControlStatus != ClubTenureControlStatus.Closed)
                on club.Id equals tenure.ClubId into tenures
            from tenure in tenures.DefaultIfEmpty()
            orderby club.Id
            select new ClubInboxTarget(club.Id, tenure == null ? null : tenure.ManagerId, club.Name))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, string>> FindPlayerNamesAsync(
        IReadOnlyCollection<Guid> playerIds,
        CancellationToken cancellationToken)
    {
        if (playerIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await _dbContext.Players
            .Where(player => playerIds.Contains(player.Id))
            .Select(player => new { player.Id, player.FullName })
            .ToDictionaryAsync(player => player.Id, player => player.FullName, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, string>> FindManagerEmailsAsync(
        IReadOnlyCollection<Guid> managerIds,
        CancellationToken cancellationToken)
    {
        if (managerIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await (
            from manager in _dbContext.Managers
            join user in _dbContext.Users on manager.UserId equals user.Id
            where managerIds.Contains(manager.Id)
            select new { manager.Id, user.Email })
            .ToDictionaryAsync(row => row.Id, row => row.Email, cancellationToken);
    }
}
