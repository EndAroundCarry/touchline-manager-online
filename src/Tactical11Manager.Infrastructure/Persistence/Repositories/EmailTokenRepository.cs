using Microsoft.EntityFrameworkCore;
using Tactical11Manager.Application.Abstractions.Auth;
using Tactical11Manager.Domain.Auth;

namespace Tactical11Manager.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core persistence for single-use email tokens.
/// </summary>
internal sealed class EmailTokenRepository : IEmailTokenRepository
{
    private readonly Tactical11ManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public EmailTokenRepository(Tactical11ManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public Task<EmailToken?> FindByTokenHashAsync(
        EmailTokenPurpose purpose,
        string tokenHash,
        CancellationToken cancellationToken) =>
        _dbContext.EmailTokens.SingleOrDefaultAsync(
            token => token.TokenHash == tokenHash && token.Purpose == purpose,
            cancellationToken);

    /// <inheritdoc />
    public async Task SupersedeActiveAsync(
        Guid userId,
        EmailTokenPurpose purpose,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await _dbContext.EmailTokens
            .Where(token => token.UserId == userId && token.Purpose == purpose && token.ConsumedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.ConsumedAt, (DateTimeOffset?)now),
                cancellationToken);

    /// <inheritdoc />
    public void Add(EmailToken token) => _dbContext.EmailTokens.Add(token);
}
