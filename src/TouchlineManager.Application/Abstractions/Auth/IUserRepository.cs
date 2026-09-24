using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Abstractions.Auth;

/// <summary>
/// Persistence for accounts, their roles, and their recorded consents (<c>auth.users</c> and
/// dependencies).
/// </summary>
/// <remarks>
/// Every lookup loads roles with the account: they are a handful of rows, and an account without
/// its roles cannot be authorized. <see cref="Add"/> and <see cref="AddConsent"/> stage changes;
/// nothing is written until <see cref="Abstractions.Persistence.IUnitOfWork.SaveChangesAsync"/>.
/// </remarks>
public interface IUserRepository
{
    /// <summary>Finds an account by identity.</summary>
    Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Finds an account by normalized email.</summary>
    Task<User?> FindByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken);

    /// <summary>Finds an account by normalized display name.</summary>
    Task<User?> FindByNormalizedDisplayNameAsync(string normalizedDisplayName, CancellationToken cancellationToken);

    /// <summary>Whether a normalized email is already registered.</summary>
    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken);

    /// <summary>Whether a normalized display name is already taken.</summary>
    Task<bool> DisplayNameExistsAsync(string normalizedDisplayName, CancellationToken cancellationToken);

    /// <summary>Stages a new account.</summary>
    void Add(User user);

    /// <summary>Stages a consent record.</summary>
    void AddConsent(UserConsent consent);
}
