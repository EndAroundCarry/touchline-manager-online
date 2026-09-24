using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Contracts.Auth;

namespace TouchlineManager.Application.Auth;

/// <summary>An account projection together with the version that guards conditional writes.</summary>
/// <param name="Profile">The public projection.</param>
/// <param name="Version">The current version, exposed as a strong ETag.</param>
public sealed record ProfileView(UserProfileResponse Profile, long Version);

/// <summary>Reads the authenticated account's own profile.</summary>
public sealed class GetProfile
{
    private readonly IUserRepository _users;

    /// <summary>Initializes the query.</summary>
    public GetProfile(IUserRepository users) => _users = users;

    /// <summary>Returns the profile, or <see langword="null"/> when the account no longer exists.</summary>
    public async Task<ProfileView?> ExecuteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _users.FindByIdAsync(userId, cancellationToken);

        return user is null ? null : new ProfileView(UserProfileMapper.ToProfile(user), user.Version);
    }
}
