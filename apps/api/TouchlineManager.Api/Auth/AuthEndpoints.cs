using FluentValidation;
using TouchlineManager.Api.Http;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Auth;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Contracts.Http;

namespace TouchlineManager.Api.Auth;

/// <summary>
/// The auth module's HTTP surface (master plan §10.1).
/// </summary>
/// <remarks>
/// Handlers do exactly three things: validate the request, call one use case, and translate the
/// outcome into a status and a stable error code. No rule about accounts lives here, which is what
/// lets the worker and tests invoke the same behaviour without HTTP.
/// </remarks>
internal static class AuthEndpoints
{
    /// <summary>Maps the auth routes onto the auth module group.</summary>
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPost("/register", RegisterAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.AuthSensitive)
            .WithName("RegisterAccount")
            .WithSummary("Creates an account and sends a verification email.")
            .Produces<RegistrationAcceptedResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/verify-email", VerifyEmailAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.AuthSensitive)
            .WithName("VerifyEmail")
            .WithSummary("Confirms an email address using a single-use token.")
            .Produces<RequestAcceptedResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/resend-verification", ResendVerificationAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.AuthSensitive)
            .WithName("ResendVerification")
            .WithSummary("Sends a fresh verification link if the account exists and is unverified.")
            .Produces<RequestAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.AuthSensitive)
            .WithName("Login")
            .WithSummary("Authenticates an account and starts a session.")
            .Produces<AuthSessionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status423Locked);

        group.MapPost("/refresh", RefreshAsync)
            .AllowAnonymous()
            .WithName("RefreshSession")
            .WithSummary("Rotates the refresh session and issues a new access token.")
            .Produces<AuthSessionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", LogoutAsync)
            .AllowAnonymous()
            .WithName("Logout")
            .WithSummary("Revokes the presented refresh session.")
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/logout-all", LogoutAllAsync)
            .RequireAuthorization()
            .WithName("LogoutAll")
            .WithSummary("Revokes every session and invalidates every issued access token.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/forgot-password", ForgotPasswordAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.AuthSensitive)
            .WithName("ForgotPassword")
            .WithSummary("Sends a password-reset link if the account exists.")
            .Produces<RequestAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/reset-password", ResetPasswordAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.AuthSensitive)
            .WithName("ResetPassword")
            .WithSummary("Completes a password reset and revokes every existing session.")
            .Produces<RequestAcceptedResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return group;
    }

    /// <summary>
    /// Maps the authenticated account's own resource.
    /// </summary>
    /// <remarks>
    /// <c>/me</c> sits at the version root rather than under a module prefix because the account is
    /// not owned by a game module.
    /// </remarks>
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup($"{Endpoints.ModuleEndpointGroups.VersionPrefix}/me")
            .WithTags("auth")
            .RequireAuthorization();

        group.MapGet(string.Empty, GetMeAsync)
            .WithName("GetMe")
            .WithSummary("Returns the authenticated account's profile and its concurrency version.")
            .Produces<UserProfileResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPatch(string.Empty, UpdateMeAsync)
            .WithName("UpdateMe")
            .WithSummary("Changes the display name. Requires a verified account and If-Match.")
            .Produces<ProfileUpdatedResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        group.MapDelete(string.Empty, DeleteMeAsync)
            .WithName("DeleteMe")
            .WithSummary("Closes the account and revokes every session.")
            .Produces<RequestAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        IValidator<RegisterRequest> validator,
        RegisterUser useCase,
        CancellationToken cancellationToken)
    {
        var invalid = await RequestValidation.ValidateAsync(validator, request, cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        var result = await useCase.ExecuteAsync(request, cancellationToken);

        return result.Outcome switch
        {
            RegisterUserOutcome.Registered => Results.Created(
                $"{Endpoints.ModuleEndpointGroups.VersionPrefix}/me",
                new RegistrationAcceptedResponse(result.UserId, result.Email, result.VerificationEmailSent)),

            RegisterUserOutcome.EmailAlreadyRegistered => ProblemResults.Code(
                StatusCodes.Status409Conflict,
                AuthErrorCodes.EmailAlreadyRegistered,
                "Email already registered.",
                "An account already exists for that email address."),

            _ => ProblemResults.Code(
                StatusCodes.Status409Conflict,
                AuthErrorCodes.DisplayNameTaken,
                "Display name taken.",
                "That display name is already in use."),
        };
    }

    private static async Task<IResult> VerifyEmailAsync(
        VerifyEmailRequest request,
        IValidator<VerifyEmailRequest> validator,
        VerifyEmail useCase,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var invalid = await RequestValidation.ValidateAsync(validator, request, cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        var outcome = await useCase.ExecuteAsync(request, cancellationToken);

        return outcome == VerifyEmailOutcome.Verified
            ? Results.Ok(new RequestAcceptedResponse(
                "Your email address is confirmed.",
                clock.UtcNow))
            : ProblemResults.Code(
                StatusCodes.Status400BadRequest,
                AuthErrorCodes.InvalidToken,
                "Invalid confirmation link.",
                "That confirmation link is not valid, has expired, or has already been used.");
    }

    private static async Task<IResult> ResendVerificationAsync(
        ResendVerificationRequest request,
        IValidator<ResendVerificationRequest> validator,
        ResendVerificationEmail useCase,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var invalid = await RequestValidation.ValidateAsync(validator, request, cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        await useCase.ExecuteAsync(request.Email, cancellationToken);

        // The answer never depends on whether the account exists (ADR-0002).
        return Results.Accepted(
            value: new RequestAcceptedResponse(
                "If that address needs confirming, a new link is on its way.",
                clock.UtcNow));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        IValidator<LoginRequest> validator,
        Login useCase,
        AuthCookieWriter cookies,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var invalid = await RequestValidation.ValidateAsync(validator, request, cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        var result = await useCase.ExecuteAsync(request, cancellationToken);

        if (result.Outcome == LoginOutcome.Succeeded)
        {
            var session = result.Session!;
            cookies.Write(httpContext.Response, session.RefreshToken, session.RefreshTokenExpiresAt);

            return Results.Ok(session.Response);
        }

        return result.Outcome switch
        {
            LoginOutcome.InvalidCredentials => ProblemResults.Code(
                StatusCodes.Status401Unauthorized,
                AuthErrorCodes.InvalidCredentials,
                "Sign-in failed.",
                "The email address or password is incorrect."),

            LoginOutcome.AccountLocked => ProblemResults.Code(
                StatusCodes.Status423Locked,
                AuthErrorCodes.AccountLocked,
                "Account locked.",
                "Too many failed attempts. Try again later."),

            LoginOutcome.AccountSuspended => ProblemResults.Code(
                StatusCodes.Status403Forbidden,
                AuthErrorCodes.AccountSuspended,
                "Account suspended.",
                "This account is suspended and cannot sign in."),

            _ => ProblemResults.Code(
                StatusCodes.Status403Forbidden,
                AuthErrorCodes.AccountDeleted,
                "Account closed.",
                "This account is closing and cannot sign in."),
        };
    }

    private static async Task<IResult> RefreshAsync(
        HttpContext httpContext,
        RefreshAccessToken useCase,
        AuthCookieWriter cookies,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(cookies.Read(httpContext.Request), cancellationToken);

        if (result.Outcome == RefreshAccessTokenOutcome.Refreshed)
        {
            var session = result.Session!;
            cookies.Write(httpContext.Response, session.RefreshToken, session.RefreshTokenExpiresAt);

            return Results.Ok(session.Response);
        }

        // A session that cannot be refreshed is worse than useless: drop it so the client does not
        // keep presenting a dead token.
        cookies.Clear(httpContext.Response);

        return result.Outcome == RefreshAccessTokenOutcome.AccountUnavailable
            ? ProblemResults.Code(
                StatusCodes.Status403Forbidden,
                AuthErrorCodes.AccountSuspended,
                "Account unavailable.",
                "This account can no longer sign in.")
            : ProblemResults.Code(
                StatusCodes.Status401Unauthorized,
                AuthErrorCodes.SessionInvalid,
                "Session expired.",
                "Sign in again to continue.");
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext httpContext,
        Logout useCase,
        AuthCookieWriter cookies,
        CancellationToken cancellationToken)
    {
        await useCase.ExecuteAsync(cookies.Read(httpContext.Request), cancellationToken);
        cookies.Clear(httpContext.Response);

        return Results.NoContent();
    }

    private static async Task<IResult> LogoutAllAsync(
        HttpContext httpContext,
        LogoutAll useCase,
        AuthCookieWriter cookies,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        await useCase.ExecuteAsync(userId, cancellationToken);
        cookies.Clear(httpContext.Response);

        return Results.NoContent();
    }

    private static async Task<IResult> ForgotPasswordAsync(
        ForgotPasswordRequest request,
        IValidator<ForgotPasswordRequest> validator,
        ForgotPassword useCase,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var invalid = await RequestValidation.ValidateAsync(validator, request, cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        await useCase.ExecuteAsync(request.Email, cancellationToken);

        return Results.Accepted(
            value: new RequestAcceptedResponse(
                "If that address has an account, a reset link is on its way.",
                clock.UtcNow));
    }

    private static async Task<IResult> ResetPasswordAsync(
        ResetPasswordRequest request,
        IValidator<ResetPasswordRequest> validator,
        ResetPassword useCase,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var invalid = await RequestValidation.ValidateAsync(validator, request, cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        var outcome = await useCase.ExecuteAsync(request, cancellationToken);

        return outcome switch
        {
            ResetPasswordOutcome.Reset => Results.Ok(new RequestAcceptedResponse(
                "Your password has been changed. Every other session has been signed out.",
                clock.UtcNow)),

            ResetPasswordOutcome.AccountUnavailable => ProblemResults.Code(
                StatusCodes.Status403Forbidden,
                AuthErrorCodes.AccountSuspended,
                "Account unavailable.",
                "This account cannot reset its password right now."),

            _ => ProblemResults.Code(
                StatusCodes.Status400BadRequest,
                AuthErrorCodes.InvalidToken,
                "Invalid reset link.",
                "That reset link is not valid, has expired, or has already been used."),
        };
    }

    private static async Task<IResult> GetMeAsync(
        HttpContext httpContext,
        GetProfile query,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var view = await query.ExecuteAsync(userId, cancellationToken);

        if (view is null)
        {
            return ProblemResults.Unauthenticated("Your session is no longer valid.");
        }

        httpContext.Response.Headers.ETag = EntityTagHeader.ForVersion(view.Version);

        return Results.Ok(view.Profile);
    }

    private static async Task<IResult> UpdateMeAsync(
        HttpContext httpContext,
        UpdateProfileRequest request,
        IValidator<UpdateProfileRequest> validator,
        UpdateProfile useCase,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var expectedVersion = EntityTagHeader.ParseIfMatch(httpContext.Request.Headers.IfMatch.ToString());

        if (expectedVersion is null)
        {
            return ProblemResults.Code(
                StatusCodes.Status428PreconditionRequired,
                ApiErrorCodes.PreconditionRequired,
                "A version is required.",
                "Send the current entity tag in If-Match so a concurrent change is not overwritten.");
        }

        var invalid = await RequestValidation.ValidateAsync(validator, request, cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        var result = await useCase.ExecuteAsync(userId, expectedVersion.Value, request, cancellationToken);

        switch (result.Outcome)
        {
            case UpdateProfileOutcome.Updated:
                httpContext.Response.Headers.ETag = EntityTagHeader.ForVersion(result.Profile!.Version);

                return Results.Ok(new ProfileUpdatedResponse(result.Profile.Profile, result.Profile.Version));

            case UpdateProfileOutcome.DisplayNameTaken:
                return ProblemResults.Code(
                    StatusCodes.Status409Conflict,
                    AuthErrorCodes.DisplayNameTaken,
                    "Display name taken.",
                    "That display name is already in use.");

            case UpdateProfileOutcome.PreconditionFailed:
                return ProblemResults.Code(
                    StatusCodes.Status412PreconditionFailed,
                    ApiErrorCodes.PreconditionFailed,
                    "The account changed.",
                    "Reload the profile and reapply the change.");

            case UpdateProfileOutcome.NotVerified:
                return ProblemResults.Code(
                    StatusCodes.Status403Forbidden,
                    AuthErrorCodes.AccountNotVerified,
                    "Email not confirmed.",
                    "Confirm your email address before changing your display name.");

            default:
                return ProblemResults.Unauthenticated("Your session is no longer valid.");
        }
    }

    private static async Task<IResult> DeleteMeAsync(
        HttpContext httpContext,
        [Microsoft.AspNetCore.Mvc.FromBody] DeleteAccountRequest request,
        IValidator<DeleteAccountRequest> validator,
        DeleteAccount useCase,
        AuthCookieWriter cookies,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var invalid = await RequestValidation.ValidateAsync(validator, request, cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        var outcome = await useCase.ExecuteAsync(userId, request, cancellationToken);

        switch (outcome)
        {
            case DeleteAccountOutcome.Accepted:
                cookies.Clear(httpContext.Response);

                return Results.Accepted(
                    value: new RequestAcceptedResponse(
                        "Your account is closing. Your match and transfer history is retained.",
                        clock.UtcNow));

            case DeleteAccountOutcome.InvalidCredentials:
                return ProblemResults.Code(
                    StatusCodes.Status403Forbidden,
                    AuthErrorCodes.InvalidCredentials,
                    "Confirmation failed.",
                    "The password was incorrect.");

            case DeleteAccountOutcome.AlreadyClosed:
                cookies.Clear(httpContext.Response);

                return Results.Accepted(
                    value: new RequestAcceptedResponse(
                        "Your account is already closing.",
                        clock.UtcNow));

            default:
                return ProblemResults.Unauthenticated("Your session is no longer valid.");
        }
    }

    private static bool TryGetUserId(HttpContext httpContext, out Guid userId) =>
        Guid.TryParse(httpContext.User.FindFirst(AuthClaimNames.Subject)?.Value, out userId);
}
