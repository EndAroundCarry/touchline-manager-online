using TouchlineManager.Contracts.Auth;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Auth;

/// <summary>
/// Maps an account aggregate to its public projection.
/// </summary>
/// <remarks>
/// This is the only place an account becomes a DTO, which is what keeps the promise that hidden
/// fields — the password hash, the security stamp, the lockout counters — can never reach a
/// player-facing response (master plan §10.9, MAT-11 applies the same rule to matches).
/// </remarks>
public static class UserProfileMapper
{
    /// <summary>Projects an account for its owner.</summary>
    public static UserProfileResponse ToProfile(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return new UserProfileResponse(
            user.Id,
            user.Email,
            user.DisplayName,
            user.EmailVerifiedAt.HasValue,
            user.Status.ToCode(),
            user.RoleNames(),
            user.LastLoginAt,
            user.CreatedAt);
    }
}
