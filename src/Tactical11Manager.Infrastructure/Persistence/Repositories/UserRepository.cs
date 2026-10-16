using Microsoft.EntityFrameworkCore;
using Tactical11Manager.Application.Abstractions.Auth;
using Tactical11Manager.Domain.Auth;

namespace Tactical11Manager.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core persistence for accounts and their roles.
/// </summary>
/// <remarks>
/// Roles are always included: a lookup that returned an account without its roles would silently
/// authorize it as a plain manager, which is a correctness bug rather than a performance trade-off.
/// </remarks>
internal sealed class UserRepository : IUserRepository
{
    private readonly Tactical11ManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public UserRepository(Tactical11ManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.Users
            .Include(user => user.Roles)
            .SingleOrDefaultAsync(user => user.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<User?> FindByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        _dbContext.Users
            .Include(user => user.Roles)
            .SingleOrDefaultAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken);

    /// <inheritdoc />
    public Task<User?> FindByNormalizedDisplayNameAsync(
        string normalizedDisplayName,
        CancellationToken cancellationToken) =>
        _dbContext.Users
            .Include(user => user.Roles)
            .SingleOrDefaultAsync(user => user.NormalizedDisplayName == normalizedDisplayName, cancellationToken);

    /// <inheritdoc />
    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        _dbContext.Users.AnyAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken);

    /// <inheritdoc />
    public Task<bool> DisplayNameExistsAsync(string normalizedDisplayName, CancellationToken cancellationToken) =>
        _dbContext.Users.AnyAsync(user => user.NormalizedDisplayName == normalizedDisplayName, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserConsent>> ListConsentsAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await _dbContext.UserConsents
            .Where(consent => consent.UserId == userId)
            .OrderBy(consent => consent.AcceptedAt)
            .ThenBy(consent => consent.Id)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(User user) => _dbContext.Users.Add(user);

    /// <inheritdoc />
    public void AddConsent(UserConsent consent) => _dbContext.UserConsents.Add(consent);
}
