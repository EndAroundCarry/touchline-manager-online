using TouchlineManager.Api.Auth;
using TouchlineManager.Api.Http;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Competition;
using TouchlineManager.Application.Ops;
using TouchlineManager.Contracts.Http;
using TouchlineManager.Contracts.Ops;
using TouchlineManager.Domain.Auth;
using TouchlineManager.Domain.Ops;

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
    private const int ReasonMaxLength = 200;

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
            .MapGet("/jobs", ListJobsAsync)
            .WithName("ListAdminJobs")
            .WithSummary("Reads a page of the durable job queue (F-46).")
            .WithDescription(
                "Support, operator, or admin, with a completed second factor. Filter by status and job "
                + "type; the failure, attempts, and lease fields tell a stuck job from a busy one.")
            .RequireAuthorization(AuthorizationPolicies.AdminRead)
            .Produces<AdminJobPageResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group
            .MapGet("/matchdays/{id:guid}", GetMatchdayAsync)
            .WithName("GetAdminMatchday")
            .WithSummary("Reads one round's publication state, fixtures, and driving jobs (F-46).")
            .WithDescription(
                "Support, operator, or admin, with a completed second factor. The nine fixtures with their "
                + "latest simulation attempt, and the lock, resolution, and publication job rows.")
            .RequireAuthorization(AuthorizationPolicies.AdminRead)
            .Produces<AdminMatchdayDetailResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group
            .MapGet("/audit", ListAuditAsync)
            .WithName("ListAdminAudit")
            .WithSummary("Searches the append-only audit trail (F-47).")
            .WithDescription(
                "Support, operator, or admin, with a completed second factor. Filter by action prefix, "
                + "actor, and target; the hashed client IP and any repair metadata are not returned.")
            .RequireAuthorization(AuthorizationPolicies.AdminRead)
            .Produces<AdminAuditPageResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

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

        group
            .MapPost("/jobs/{id:guid}/retry", RetryJobAsync)
            .WithName("RetryAdminJob")
            .WithSummary("Returns a dead-lettered job to the queue (F-46).")
            .WithDescription(
                "Operator or admin, with a fresh second-factor code. Requires a reason and an idempotency key. "
                + "Only a dead-lettered job can be retried; the queue already owns any other state.")
            .RequireAuthorization(AuthorizationPolicies.AdminMutate)
            .AddEndpointFilter<MfaStepUpFilter>()
            .Produces<AdminJobActionResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group
            .MapPost("/jobs/{id:guid}/cancel", CancelJobAsync)
            .WithName("CancelAdminJob")
            .WithSummary("Stops a stuck job (F-46).")
            .WithDescription(
                "Operator or admin, with a fresh second-factor code. Requires a reason and an idempotency key. "
                + "A pending, leased, or dead-lettered job becomes cancelled; a completed job cannot be cancelled.")
            .RequireAuthorization(AuthorizationPolicies.AdminMutate)
            .AddEndpointFilter<MfaStepUpFilter>()
            .Produces<AdminJobActionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group
            .MapPost("/matchdays/{id:guid}/resume", ResumeMatchdayAsync)
            .WithName("ResumeAdminMatchday")
            .WithSummary("Requeues a stuck round's resolution or publication job (F-46).")
            .WithDescription(
                "Operator or admin, with a fresh second-factor code. Requires a reason and an idempotency key. "
                + "A pending round resumes resolution and a staged round resumes publication; a published round "
                + "cannot be resumed.")
            .RequireAuthorization(AuthorizationPolicies.AdminMutate)
            .AddEndpointFilter<MfaStepUpFilter>()
            .Produces<AdminMatchdayResumeResponse>(StatusCodes.Status202Accepted)
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
        var refused = Validate(
            request.Reason,
            httpContext,
            "Send why the account's status is being changed, of at most 200 characters.");

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
        var refused = Validate(
            request.Reason,
            httpContext,
            "Send why the account's status is being changed, of at most 200 characters.");

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

    private static async Task<IResult> RetryJobAsync(
        Guid id,
        AdminActionRequest request,
        HttpContext httpContext,
        RetryJob useCase,
        CancellationToken cancellationToken)
    {
        var refused = Validate(
            request.Reason,
            httpContext,
            "Send why the job is being retried, of at most 200 characters.");

        if (refused is not null)
        {
            return refused;
        }

        var result = await useCase.ExecuteAsync(id, request.Reason, cancellationToken);

        return result.Outcome switch
        {
            JobAdministrationOutcome.Applied => Results.Json(
                new AdminJobActionResponse(result.JobId, result.Status!),
                statusCode: StatusCodes.Status202Accepted),

            JobAdministrationOutcome.NotFound => ProblemResults.Code(
                StatusCodes.Status404NotFound,
                AdminErrorCodes.JobNotFound,
                "No such job.",
                "That job does not exist."),

            _ => ProblemResults.Code(
                StatusCodes.Status409Conflict,
                AdminErrorCodes.JobNotRetryable,
                "Job cannot be retried.",
                "Only a dead-lettered job can be retried; the queue already owns this one, or it is done."),
        };
    }

    private static async Task<IResult> CancelJobAsync(
        Guid id,
        AdminActionRequest request,
        HttpContext httpContext,
        CancelJob useCase,
        CancellationToken cancellationToken)
    {
        var refused = Validate(
            request.Reason,
            httpContext,
            "Send why the job is being cancelled, of at most 200 characters.");

        if (refused is not null)
        {
            return refused;
        }

        var result = await useCase.ExecuteAsync(id, request.Reason, cancellationToken);

        return result.Outcome switch
        {
            JobAdministrationOutcome.Applied => Results.Ok(
                new AdminJobActionResponse(result.JobId, result.Status!)),

            JobAdministrationOutcome.NotFound => ProblemResults.Code(
                StatusCodes.Status404NotFound,
                AdminErrorCodes.JobNotFound,
                "No such job.",
                "That job does not exist."),

            _ => ProblemResults.Code(
                StatusCodes.Status409Conflict,
                AdminErrorCodes.JobNotCancellable,
                "Job cannot be cancelled.",
                "That job is already completed or cancelled, so there is nothing to stop."),
        };
    }

    private static async Task<IResult> ResumeMatchdayAsync(
        Guid id,
        AdminActionRequest request,
        HttpContext httpContext,
        ResumeMatchday useCase,
        CancellationToken cancellationToken)
    {
        var refused = Validate(
            request.Reason,
            httpContext,
            "Send why the round is being resumed, of at most 200 characters.");

        if (refused is not null)
        {
            return refused;
        }

        var result = await useCase.ExecuteAsync(id, request.Reason, cancellationToken);

        return result.Outcome switch
        {
            ResumeMatchdayOutcome.Resumed => Results.Json(
                new AdminMatchdayResumeResponse(result.MatchdayId, result.Step!, result.Requeued),
                statusCode: StatusCodes.Status202Accepted),

            ResumeMatchdayOutcome.MatchdayNotFound => ProblemResults.Code(
                StatusCodes.Status404NotFound,
                AdminErrorCodes.MatchdayNotFound,
                "No such matchday.",
                "That matchday does not exist."),

            _ => ProblemResults.Code(
                StatusCodes.Status409Conflict,
                AdminErrorCodes.MatchdayNotResumable,
                "Round cannot be resumed.",
                "The round is already published, or the queue already owns its resolution or publication job."),
        };
    }

    private static async Task<IResult> ListJobsAsync(
        HttpContext httpContext,
        string? cursor,
        string? status,
        string? jobType,
        IClock clock,
        IAdminQueries queries,
        CancellationToken cancellationToken)
    {
        if (!AdminJobCursor.TryDecode(cursor, out var before))
        {
            return InvalidCursor();
        }

        if (status is not null && !JobStatuses.TryFromCode(status, out _))
        {
            return InvalidFilter("Send a status of pending, leased, completed, dead_letter, or cancelled.");
        }

        var page = await queries.ListJobsAsync(new AdminJobQuery(status, jobType, before), cancellationToken);

        httpContext.Response.Headers.CacheControl = "no-store";

        return Results.Ok(new AdminJobPageResponse(
            page.Items.Select(ToJobResponse).ToList(),
            page.NextCursor,
            clock.UtcNow));
    }

    private static async Task<IResult> GetMatchdayAsync(
        Guid id,
        HttpContext httpContext,
        IAdminQueries queries,
        CancellationToken cancellationToken)
    {
        var detail = await queries.GetMatchdayAsync(id, cancellationToken);

        if (detail is null)
        {
            return ProblemResults.Code(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.NotFound,
                "No such matchday.",
                "That matchday does not exist.");
        }

        httpContext.Response.Headers.CacheControl = "no-store";

        return Results.Ok(new AdminMatchdayDetailResponse(
            detail.Id,
            detail.DivisionSeasonId,
            detail.RoundNumber,
            detail.LockAt,
            detail.KickoffAt,
            detail.PublicationStatus,
            detail.DivisionId,
            detail.DivisionName,
            detail.TierNumber,
            detail.CountryCode,
            detail.CountryName,
            detail.SeasonNumber,
            detail.SeasonLabel,
            detail.Fixtures.Select(ToFixtureResponse).ToList(),
            detail.Jobs.Select(ToJobResponse).ToList()));
    }

    private static async Task<IResult> ListAuditAsync(
        HttpContext httpContext,
        string? cursor,
        string? action,
        Guid? actorUserId,
        string? targetType,
        Guid? targetId,
        IClock clock,
        IAdminQueries queries,
        CancellationToken cancellationToken)
    {
        if (!AdminAuditCursor.TryDecode(cursor, out var before))
        {
            return InvalidCursor();
        }

        var page = await queries.ListAuditAsync(
            new AdminAuditQuery(action, actorUserId, targetType, targetId, before),
            cancellationToken);

        httpContext.Response.Headers.CacheControl = "no-store";

        return Results.Ok(new AdminAuditPageResponse(
            page.Items.Select(ToAuditResponse).ToList(),
            page.NextCursor,
            clock.UtcNow));
    }

    private static IResult InvalidCursor() => ProblemResults.Code(
        StatusCodes.Status400BadRequest,
        AdminErrorCodes.InvalidCursor,
        "Invalid cursor.",
        "The page cursor is not one this server produced.");

    private static IResult InvalidFilter(string detail) => ProblemResults.Code(
        StatusCodes.Status400BadRequest,
        AdminErrorCodes.InvalidFilter,
        "Invalid filter.",
        detail);

    private static AdminJobSummaryResponse ToJobResponse(AdminJobSummary job) => new(
        job.Id,
        job.JobType,
        job.BusinessKey,
        job.Status,
        job.AttemptCount,
        job.MaxAttempts,
        job.DueAt,
        job.CreatedAt,
        job.UpdatedAt,
        job.CompletedAt,
        job.LeaseOwner,
        job.LeaseUntil,
        job.LastError);

    private static AdminFixtureStatusResponse ToFixtureResponse(AdminFixtureStatus fixture) => new(
        fixture.Id,
        fixture.HomeClubId,
        fixture.HomeClubName,
        fixture.AwayClubId,
        fixture.AwayClubName,
        fixture.KickoffAt,
        fixture.Status,
        fixture.HomeScore,
        fixture.AwayScore,
        fixture.MatchId,
        fixture.LatestAttempt is { } attempt
            ? new AdminSimulationAttemptResponse(
                attempt.AttemptNumber,
                attempt.Status,
                attempt.ErrorCategory,
                attempt.ErrorMessage,
                attempt.CompletedAt)
            : null);

    private static AdminAuditEntryResponse ToAuditResponse(AdminAuditEntry entry) => new(
        entry.Id,
        entry.ActorType,
        entry.ActorUserId,
        entry.Action,
        entry.TargetType,
        entry.TargetId,
        entry.CorrelationId,
        entry.OccurredAt,
        entry.Reason);

    private static IResult? Validate(string reason, HttpContext httpContext, string reasonDetail)
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

        if (string.IsNullOrWhiteSpace(reason) || reason.Length > ReasonMaxLength)
        {
            return ProblemResults.Code(
                StatusCodes.Status400BadRequest,
                AdminErrorCodes.ReasonRequired,
                "A reason is required.",
                reasonDetail);
        }

        return null;
    }
}
