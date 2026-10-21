using Microsoft.EntityFrameworkCore;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Domain.Auth;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Infrastructure.Persistence.Repositories;

/// <summary>
/// Reads who runs a club and whether their account is still open (`COM-11`, `COM-12`).
/// </summary>
/// <remarks>
/// A read-only join across the world and auth tables, kept behind a port so the modules that need a manager's
/// name never reach into those tables themselves. "Open" is the same test the tenure repository uses:
/// anything but a closed tenure still runs the club.
/// </remarks>
internal sealed class ClubManagerLookup : IClubManagerLookup
{
    private readonly Tactical11ManagerDbContext _dbContext;

    /// <summary>Initializes the lookup.</summary>
    public ClubManagerLookup(Tactical11ManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<ClubManagerRow?> FindByClubAsync(Guid clubId, CancellationToken cancellationToken)
    {
        // A club has at most one open tenure, but the newest wins if data ever says otherwise.
        var row = await (
            from tenure in _dbContext.ClubTenures
            join manager in _dbContext.Managers on tenure.ManagerId equals manager.Id
            join user in _dbContext.Users on manager.UserId equals user.Id
            where tenure.ClubId == clubId && tenure.ControlStatus != ClubTenureControlStatus.Closed
            orderby tenure.StartedAt descending
            select new { ManagerId = manager.Id, user.DisplayName, user.Status })
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new ClubManagerRow(row.ManagerId, row.DisplayName, IsClosing(row.Status));
    }

    /// <inheritdoc />
    public async Task<bool> IsAccountClosingAsync(Guid managerId, CancellationToken cancellationToken)
    {
        var status = await (
            from manager in _dbContext.Managers
            join user in _dbContext.Users on manager.UserId equals user.Id
            where manager.Id == managerId
            select new { user.Status })
            .FirstOrDefaultAsync(cancellationToken);

        return status is not null && IsClosing(status.Status);
    }

    private static bool IsClosing(UserStatus status) =>
        status is UserStatus.DeletionPending or UserStatus.Anonymized;
}
