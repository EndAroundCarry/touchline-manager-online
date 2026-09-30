using TouchlineManager.Api.Http;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Auth;
using TouchlineManager.Contracts.Auth;

namespace TouchlineManager.Api.Auth;

/// <summary>
/// Re-asserts the second factor on an admin mutation by checking a fresh code (ADR-0002, ADR-0042).
/// </summary>
/// <remarks>
/// <para>
/// The session already carries the <c>mfa</c> claim, so the caller has completed a second factor once.
/// ADR-0002 requires it to be asserted <em>again</em> for every admin mutation, so this filter reads the
/// current code from the <c>X-MFA-Code</c> header and verifies it against the caller's credential before
/// the handler runs.
/// </para>
/// <para>
/// It runs in the endpoint pipeline, after authorization, so an unauthenticated caller is refused with a
/// 401 by the bearer challenge and a caller without the role or the claim by the 403 policy — this filter
/// only decides whether the code proves present possession now.
/// </para>
/// </remarks>
internal sealed class MfaStepUpFilter : IEndpointFilter
{
    /// <summary>The header a caller sends the current code in.</summary>
    public const string HeaderName = "X-MFA-Code";

    private readonly IClock _clock;
    private readonly IMfaCredentialRepository _credentials;
    private readonly MfaAuthenticator _authenticator;

    /// <summary>Initializes the filter.</summary>
    public MfaStepUpFilter(IClock clock, IMfaCredentialRepository credentials, MfaAuthenticator authenticator)
    {
        _clock = clock;
        _credentials = credentials;
        _authenticator = authenticator;
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var httpContext = context.HttpContext;
        var subject = httpContext.User.FindFirst(AuthClaimNames.Subject)?.Value;

        if (!Guid.TryParse(subject, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var code = httpContext.Request.Headers[HeaderName].ToString();

        if (string.IsNullOrWhiteSpace(code))
        {
            return ProblemResults.Code(
                StatusCodes.Status403Forbidden,
                AuthErrorCodes.MfaCodeInvalid,
                "A fresh code is required.",
                $"Send the current code from your authenticator in the {HeaderName} header.");
        }

        var credential = await _credentials.FindByUserIdAsync(userId, httpContext.RequestAborted);

        if (credential is null || !credential.IsConfirmed)
        {
            return ProblemResults.Code(
                StatusCodes.Status403Forbidden,
                AuthErrorCodes.MfaNotEnrolled,
                "Authenticator required.",
                "Enrol and confirm an authenticator before administering the game.");
        }

        if (!_authenticator.VerifyCode(credential, code, _clock.UtcNow))
        {
            return ProblemResults.Code(
                StatusCodes.Status403Forbidden,
                AuthErrorCodes.MfaCodeInvalid,
                "Code rejected.",
                "That code was not valid. Check the authenticator's clock and try again.");
        }

        return await next(context);
    }
}
