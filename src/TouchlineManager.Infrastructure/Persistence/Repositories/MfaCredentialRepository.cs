using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core persistence for multi-factor credentials and their recovery codes (ADR-0042).
/// </summary>
/// <remarks>
/// Recovery codes are always included: verification needs them, and an account without its codes could not
/// fall back to one.
/// </remarks>
internal sealed class MfaCredentialRepository : IMfaCredentialRepository
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public MfaCredentialRepository(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public Task<MfaCredential?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        _dbContext.MfaCredentials
            .Include(credential => credential.RecoveryCodes)
            .SingleOrDefaultAsync(credential => credential.UserId == userId, cancellationToken);

    /// <inheritdoc />
    public void Add(MfaCredential credential) => _dbContext.MfaCredentials.Add(credential);

    /// <inheritdoc />
    public void Remove(MfaCredential credential) => _dbContext.MfaCredentials.Remove(credential);
}
