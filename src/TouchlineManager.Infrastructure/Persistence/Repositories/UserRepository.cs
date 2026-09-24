using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core persistence for accounts and their roles.
/// </summary>
/// <remarks>
/// Roles are always included: a lookup that returned an account without its roles would silently
/// authorize it as a plain manager, which is a correctness bug rather than a performance trade-off.
/// </remarks>
internal sealed class UserRepository : IUserRepository
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public UserRepository(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

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
    public void Add(User user) => _dbContext.Users.Add(user);

    /// <inheritdoc />
    public void AddConsent(UserConsent consent) => _dbContext.UserConsents.Add(consent);
}
