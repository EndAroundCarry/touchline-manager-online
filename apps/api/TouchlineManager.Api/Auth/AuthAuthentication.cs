using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using TouchlineManager.Api.Http;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Contracts.Http;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Api.Auth;

/// <summary>
/// Configures bearer-token authentication, the policies built on it, and the stamp check that makes
/// stateless tokens revocable (ADR-0002).
/// </summary>
internal static class AuthAuthentication
{
    /// <summary>Registers authentication and authorization.</summary>
    public static IServiceCollection AddAuthAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var authOptions = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
        var keyBytes = Encoding.UTF8.GetBytes(authOptions.SigningKey ?? string.Empty);

        if (keyBytes.Length < 32)
        {
            throw new InvalidOperationException(
                "Auth:SigningKey must be configured with at least 32 bytes of key material. "
                + "Set Auth__SigningKey in the environment for non-development deployments.");
        }

        if (Encoding.UTF8.GetByteCount(authOptions.EncryptionKey ?? string.Empty) < 32)
        {
            throw new InvalidOperationException(
                "Auth:EncryptionKey must be configured with at least 32 bytes of key material to protect "
                + "multi-factor secrets (ADR-0042). Set Auth__EncryptionKey in the environment for "
                + "non-development deployments.");
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Short claim names are written by the issuer; mapping them to Microsoft URIs would
                // make the token's contents differ from what was signed.
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = authOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = authOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
                    ValidateLifetime = true,

                    // A small skew tolerates modest clock drift between the API instances; the access
                    // token lifetime is short enough that this cannot be exploited meaningfully.
                    ClockSkew = TimeSpan.FromSeconds(30),

                    NameClaimType = AuthClaimNames.Subject,
                    RoleClaimType = AuthClaimNames.Role,
                };

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = ValidateSecurityStampAsync,
                    OnChallenge = WriteUnauthenticatedProblemAsync,
                    OnForbidden = WriteForbiddenProblemAsync,
                };
            });

        // Token lifetimes are issued from the game clock (TIME-2), so they must be judged against it. The
        // default validator reads the system clock, which disagrees with a stepped or compressed clock
        // (ADR-0015, ADR-0049): a token issued at a game instant ahead of real time is "not yet valid", and
        // under a frozen clock every token would expire after its lifetime in real time. In System mode the
        // two clocks are the same, so behaviour there is unchanged.
        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IClock>((options, clock) =>
            {
                var skew = options.TokenValidationParameters.ClockSkew;

                options.TokenValidationParameters.LifetimeValidator = (notBefore, expires, _, _) =>
                {
                    var now = clock.UtcNow.UtcDateTime;

                    return (notBefore is null || notBefore.Value <= now + skew)
                        && (expires is null || expires.Value >= now - skew);
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                AuthorizationPolicies.VerifiedManager,
                policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim(AuthClaimNames.EmailVerified, "true"));

            options.AddPolicy(AuthorizationPolicies.Admin, policy => policy.RequireRole(UserRoles.Admin));
            options.AddPolicy(AuthorizationPolicies.Operator, policy => policy.RequireRole(UserRoles.Operator));
            options.AddPolicy(AuthorizationPolicies.Support, policy => policy.RequireRole(UserRoles.Support));

            // Operator or admin (F-54, ADR-0041). RequireRole with two roles is satisfied by either.
            options.AddPolicy(
                AuthorizationPolicies.OperationalAnalyticsRead,
                policy => policy.RequireRole(UserRoles.Operator, UserRoles.Admin));

            // The admin surface (master plan §10.8, ADR-0042). Reads are open to support, operator, and
            // admin once a second factor has been completed; support is excluded from mutations (E-3).
            options.AddPolicy(
                AuthorizationPolicies.AdminRead,
                policy => policy
                    .RequireRole(UserRoles.Support, UserRoles.Operator, UserRoles.Admin)
                    .RequireClaim(AuthClaimNames.Mfa, "true"));

            options.AddPolicy(
                AuthorizationPolicies.AdminMutate,
                policy => policy
                    .RequireRole(UserRoles.Operator, UserRoles.Admin)
                    .RequireClaim(AuthClaimNames.Mfa, "true"));
        });

        return services;
    }

    /// <summary>
    /// Re-reads the account and compares the security stamp carried by the token.
    /// </summary>
    /// <remarks>
    /// This is one indexed primary-key lookup per authenticated request, and it is what makes
    /// "a suspended account loses write access immediately" and "sign out everywhere" true even
    /// though the access token itself has not expired. A caching layer belongs here only if
    /// profiling shows it is needed.
    /// </remarks>
    private static async Task ValidateSecurityStampAsync(TokenValidatedContext context)
    {
        // ADR-0042: a token that carries a purpose is not an access token. The multi-factor login challenge
        // is signed with the same key, so without this check it would be accepted as a session that never
        // completed a second factor.
        if (context.Principal?.HasClaim(claim => claim.Type == AuthClaimNames.Purpose) == true)
        {
            context.Fail("The token is not an access token.");
            return;
        }

        var subject = context.Principal?.FindFirst(AuthClaimNames.Subject)?.Value;
        var stamp = context.Principal?.FindFirst(AuthClaimNames.SecurityStamp)?.Value;

        if (!Guid.TryParse(subject, out var userId) || string.IsNullOrEmpty(stamp))
        {
            context.Fail("The access token is missing its identity.");
            return;
        }

        var users = context.HttpContext.RequestServices.GetRequiredService<IUserRepository>();
        var user = await users.FindByIdAsync(userId, context.HttpContext.RequestAborted);

        if (user is null
            || !string.Equals(user.SecurityStamp, stamp, StringComparison.Ordinal)
            || !UserStatusRules.CanAuthenticate(user.Status))
        {
            context.Fail("The session is no longer valid.");
        }
    }

    private static async Task WriteUnauthenticatedProblemAsync(JwtBearerChallengeContext context)
    {
        context.HandleResponse();
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;

        await ProblemResults
            .Code(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.Unauthenticated,
                "Not signed in.",
                "Sign in to continue, or refresh the session.")
            .ExecuteAsync(context.HttpContext);
    }

    private static async Task WriteForbiddenProblemAsync(ForbiddenContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;

        await ProblemResults
            .Code(
                StatusCodes.Status403Forbidden,
                ApiErrorCodes.Forbidden,
                "Not permitted.",
                "This account does not hold the role or verification this action requires.")
            .ExecuteAsync(context.HttpContext);
    }
}
