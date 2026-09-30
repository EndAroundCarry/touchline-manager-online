using TouchlineManager.Api.Auth;
using TouchlineManager.Api.Http;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Ops;
using TouchlineManager.Contracts.Http;
using TouchlineManager.Contracts.Ops;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Api.Endpoints;

/// <summary>
/// The operator surface: how an on-call operator sees the live game and acts on it (master plan §10.8,
/// §13, `F-46`, ADR-0042).
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="OpsEndpoints"/>, whose members are non-production diagnostics probes, this surface is
/// always mapped and gated by role. Reads require the <c>AdminRead</c> policy (support, operator, or admin
/// with a completed second factor); mutations require <c>AdminMutate</c> (operator or admin, support
/// excluded) and a fresh code in the <c>X-MFA-Code</c> header.
/// </para>
/// <para>
/// Every mutation carries an explicit reason and an idempotency key, and records an audit entry in the same
/// unit of work as the change, as §10.8 requires.
/// </para>
/// </remarks>
internal static class AdminEndpoints
{
    private const int IdempotencyKeyMaxLength = 80;

    /// <summary>Maps the admin surface onto the admin module group.</summary>
    public static RouteGroupBuilder MapAdminEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group
            .MapGet("/health/game", GetGameHealthAsync)
            .WithName("GetAdminGameHealth")
            .WithSummary("Reads the operator's at-a-glance game health (F-46).")
            .WithDescription(
                "Support, operator, or admin, with a completed second factor. World and season status, the "
                + "next deadline, and the state of the durable queue; no manager, club, or account data.")
            .RequireAuthorization(AuthorizationPolicies.AdminRead)
            .Produces<AdminGameHealthResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group
            .MapPost("/users/{id:guid}/suspend", SuspendAccountAsync)
            .WithName("SuspendAccount")
            .WithSummary("Suspends an account and closes its sessions (F-46).")
            .WithDescription("Operator or admin, with a fresh second-factor code. Requires a reason and an idempotency key.")
            .RequireAuthorization(AuthorizationPolicies.AdminMutate)
            .AddEndpointFilter<MfaStepUpFilter>()
            .Produces<AccountStatusResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group
            .MapPost("/users/{id:guid}/restore", RestoreAccountAsync)
            .WithName("RestoreAccount")
            .WithSummary("Restores a suspended account (F-46).")
            .WithDescription("Operator or admin, with a fresh second-factor code. Requires a reason and an idempotency key.")
            .RequireAuthorization(AuthorizationPolicies.AdminMutate)
            .AddEndpointFilter<MfaStepUpFilter>()
            .Produces<AccountStatusResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return group;
    }

    private static async Task<IResult> GetGameHealthAsync(
        HttpContext httpContext,
        IAdminQueries queries,
        CancellationToken cancellationToken)
    {
        var health = await queries.GetGameHealthAsync(cancellationToken);

        if (health is null)
        {
            return ProblemResults.Code(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.NotFound,
                "No world was found.",
                "A world has not been seeded, so there is nothing to report.");
        }

        // Live operator data, so it is never cached.
        httpContext.Response.Headers.CacheControl = "no-store";

        return Results.Ok(new AdminGameHealthResponse(
            health.WorldId,
            health.WorldStatus,
            health.CurrentSeasonNumber,
            health.SeasonLabel,
            health.SeasonStatus,
            health.NextMatchdayKickoffAt,
            health.PendingJobs,
            health.DeadLetterJobs,
            health.OldestOverdueJobDueAt,
            health.GeneratedAt));
    }

    private static async Task<IResult> SuspendAccountAsync(
        Guid id,
        AccountStatusRequest request,
        HttpContext httpContext,
        SuspendAccount useCase,
        CancellationToken cancellationToken)
    {
        var refused = Validate(request, httpContext);

        if (refused is not null)
        {
            return refused;
        }

        var result = await useCase.ExecuteAsync(id, request.Reason, cancellationToken);

        return result.Outcome switch
        {
            AccountAdministrationOutcome.Applied => Results.Ok(
                new AccountStatusResponse(result.UserId, result.Status!)),

            AccountAdministrationOutcome.AlreadySuspended => ProblemResults.Code(
                StatusCodes.Status409Conflict,
                AdminErrorCodes.AccountStateUnchanged,
                "Already suspended.",
                "That account is already suspended."),

            AccountAdministrationOutcome.SelfNotAllowed => ProblemResults.Code(
                StatusCodes.Status409Conflict,
                AdminErrorCodes.SelfSuspensionNotAllowed,
                "Not permitted.",
                "An operator cannot suspend its own account."),

            _ => ProblemResults.Code(
                StatusCodes.Status404NotFound,
                AdminErrorCodes.AccountNotFound,
                "No such account.",
                "That account does not exist, or is anonymized."),
        };
    }

    private static async Task<IResult> RestoreAccountAsync(
        Guid id,
        AccountStatusRequest request,
        HttpContext httpContext,
        RestoreAccount useCase,
        CancellationToken cancellationToken)
    {
        var refused = Validate(request, httpContext);

        if (refused is not null)
        {
            return refused;
        }

        var result = await useCase.ExecuteAsync(id, request.Reason, cancellationToken);

        return result.Outcome switch
        {
            AccountAdministrationOutcome.Applied => Results.Ok(
                new AccountStatusResponse(result.UserId, result.Status!)),

            AccountAdministrationOutcome.NotSuspended => ProblemResults.Code(
                StatusCodes.Status409Conflict,
                AdminErrorCodes.AccountStateUnchanged,
                "Not suspended.",
                "That account is not suspended, so there is nothing to restore."),

            _ => ProblemResults.Code(
                StatusCodes.Status404NotFound,
                AdminErrorCodes.AccountNotFound,
                "No such account.",
                "That account does not exist, or is anonymized."),
        };
    }

    private static IResult? Validate(AccountStatusRequest request, HttpContext httpContext)
    {
        var idempotencyKey = httpContext.Request.Headers[ApiHeaders.IdempotencyKey].ToString();

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > IdempotencyKeyMaxLength)
        {
            return ProblemResults.Code(
                StatusCodes.Status400BadRequest,
                AdminErrorCodes.IdempotencyKeyRequired,
                "An idempotency key is required.",
                $"Send a stable {ApiHeaders.IdempotencyKey} of at most {IdempotencyKeyMaxLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 200)
        {
            return ProblemResults.Code(
                StatusCodes.Status400BadRequest,
                AdminErrorCodes.ReasonRequired,
                "A reason is required.",
                "Send why the account's status is being changed, of at most 200 characters.");
        }

        return null;
    }
}
