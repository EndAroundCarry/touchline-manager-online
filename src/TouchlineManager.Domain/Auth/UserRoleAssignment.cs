namespace TouchlineManager.Domain.Auth;

/// <summary>
/// The grant of a role to an account (<c>auth.user_roles</c>).
/// </summary>
/// <remarks>
/// A separate row rather than a column on <see cref="User"/>, because the product needs to give an
/// account more than one role over time and to keep the grant auditable.
/// </remarks>
public sealed class UserRoleAssignment
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private UserRoleAssignment()
    {
    }

    /// <summary>Gets the account the role was granted to.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Gets the role name. Composite key with <see cref="UserId"/>.</summary>
    public string Role { get; private set; } = string.Empty;

    /// <summary>Gets when the role was granted.</summary>
    public DateTimeOffset GrantedAt { get; private set; }

    internal static UserRoleAssignment Create(Guid userId, string role, DateTimeOffset now) => new()
    {
        UserId = userId,
        Role = role,
        GrantedAt = now,
    };
}
