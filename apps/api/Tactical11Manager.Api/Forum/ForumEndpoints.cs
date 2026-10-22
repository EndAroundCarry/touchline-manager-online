using System.Security.Claims;
using Microsoft.AspNetCore.RateLimiting;
using Tactical11Manager.Api.Auth;
using Tactical11Manager.Api.Endpoints;
using Tactical11Manager.Api.Http;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Application.Forum;
using Tactical11Manager.Contracts.Auth;
using Tactical11Manager.Contracts.Forum;
using Tactical11Manager.Contracts.Http;
using Tactical11Manager.Domain.Auth;

namespace Tactical11Manager.Api.Forum;

/// <summary>HTTP endpoints for the community forum.</summary>
internal static class ForumEndpoints
{
    public static IEndpointRouteBuilder MapForumEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup(ModuleEndpointGroups.VersionPrefix)
            .WithTags("forum")
            .RequireAuthorization();

        group.MapGet("/forum/overview", GetOverviewAsync)
            .WithName("GetForumOverview")
            .WithSummary("Reads the community forum overview visible to the signed-in manager.")
            .Produces<ForumOverviewResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/forum/subforums/{subforumId}/threads", GetThreadsAsync)
            .WithName("GetSubforumThreads")
            .WithSummary("Reads the thread list of an International or Country subforum.")
            .Produces<ForumThreadListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/forum/subforums/{subforumId}/threads", CreateThreadAsync)
            .WithName("CreateForumThread")
            .RequireAuthorization(AuthorizationPolicies.VerifiedManager)
            .RequireRateLimiting(RateLimitPolicies.ForumPost)
            .WithSummary("Creates a new thread in an allowed subforum.")
            .Produces(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/forum/threads/{threadId:guid}", GetThreadDetailAsync)
            .WithName("GetForumThreadDetail")
            .WithSummary("Reads the posts and details of a single forum thread.")
            .Produces<ForumThreadDetailResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/forum/threads/{threadId:guid}/posts", CreatePostAsync)
            .WithName("CreateForumPost")
            .RequireAuthorization(AuthorizationPolicies.VerifiedManager)
            .RequireRateLimiting(RateLimitPolicies.ForumPost)
            .WithSummary("Replies to an existing forum thread.")
            .Produces<ForumPostResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetOverviewAsync(
        HttpContext httpContext,
        GetForumOverview query,
        IManagerRepository managers,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var manager = await managers.FindByUserIdAsync(userId, cancellationToken);
        if (manager == null)
        {
            return ProblemResults.Code(StatusCodes.Status403Forbidden, "NO_MANAGER_PROFILE", "No manager profile.", "Create your manager profile first.");
        }

        var overview = await query.ExecuteAsync(manager.Id, cancellationToken);
        return Results.Ok(overview);
    }

    private static async Task<IResult> GetThreadsAsync(
        HttpContext httpContext,
        string subforumId,
        GetSubforumThreads query,
        IManagerRepository managers,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var manager = await managers.FindByUserIdAsync(userId, cancellationToken);
        if (manager == null)
        {
            return ProblemResults.Code(StatusCodes.Status403Forbidden, "NO_MANAGER_PROFILE", "No manager profile.", "Create your manager profile first.");
        }

        var list = await query.ExecuteAsync(subforumId, manager.Id, cancellationToken);
        if (list == null)
        {
            return ProblemResults.Code(StatusCodes.Status404NotFound, ForumErrorCodes.SubforumNotFound, "Subforum not found.", "The specified subforum does not exist or is not accessible.");
        }

        return Results.Ok(list);
    }

    private static async Task<IResult> CreateThreadAsync(
        HttpContext httpContext,
        string subforumId,
        CreateForumThreadRequest request,
        CreateForumThread command,
        IManagerRepository managers,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var manager = await managers.FindByUserIdAsync(userId, cancellationToken);
        if (manager == null)
        {
            return ProblemResults.Code(StatusCodes.Status403Forbidden, "NO_MANAGER_PROFILE", "No manager profile.", "Create your manager profile first.");
        }

        var isOperator = IsOperator(httpContext);
        var result = await command.ExecuteAsync(subforumId, manager.Id, isOperator, request, cancellationToken);

        return result.Outcome switch
        {
            ForumOutcome.Success => Results.Created(
                $"{ModuleEndpointGroups.VersionPrefix}/forum/threads/{result.ThreadId}",
                new { result.ThreadId }),

            ForumOutcome.AnnouncementsRestricted => ProblemResults.Code(
                StatusCodes.Status403Forbidden,
                ForumErrorCodes.AnnouncementsRestricted,
                "Announcements restricted.",
                "Only Touchline Manager staff can create threads in the Announcements subforum."),

            ForumOutcome.InvalidTitle => ProblemResults.Code(
                StatusCodes.Status400BadRequest,
                ForumErrorCodes.TitleLength,
                "Invalid title.",
                "Thread title must be between 3 and 150 characters."),

            ForumOutcome.InvalidBody => ProblemResults.Code(
                StatusCodes.Status400BadRequest,
                ForumErrorCodes.BodyLength,
                "Invalid body.",
                "Thread body must be between 3 and 10,000 characters."),

            ForumOutcome.CountryMismatch => ProblemResults.Code(
                StatusCodes.Status403Forbidden,
                ForumErrorCodes.CountryMismatch,
                "Country mismatch.",
                "You cannot create threads in another country's forum."),

            _ => ProblemResults.Code(
                StatusCodes.Status404NotFound,
                ForumErrorCodes.SubforumNotFound,
                "Subforum not found.",
                "The specified subforum was not found."),
        };
    }

    private static async Task<IResult> GetThreadDetailAsync(
        HttpContext httpContext,
        Guid threadId,
        GetForumThreadDetail query,
        IManagerRepository managers,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var manager = await managers.FindByUserIdAsync(userId, cancellationToken);
        if (manager == null)
        {
            return ProblemResults.Code(StatusCodes.Status403Forbidden, "NO_MANAGER_PROFILE", "No manager profile.", "Create your manager profile first.");
        }

        var thread = await query.ExecuteAsync(threadId, manager.Id, cancellationToken);
        if (thread == null)
        {
            return ProblemResults.Code(StatusCodes.Status404NotFound, ForumErrorCodes.ThreadNotFound, "Thread not found.", "The specified thread does not exist or is in an inaccessible forum.");
        }

        return Results.Ok(thread);
    }

    private static async Task<IResult> CreatePostAsync(
        HttpContext httpContext,
        Guid threadId,
        CreateForumPostRequest request,
        CreateForumPost command,
        IManagerRepository managers,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var manager = await managers.FindByUserIdAsync(userId, cancellationToken);
        if (manager == null)
        {
            return ProblemResults.Code(StatusCodes.Status403Forbidden, "NO_MANAGER_PROFILE", "No manager profile.", "Create your manager profile first.");
        }

        var isOperator = IsOperator(httpContext);
        var result = await command.ExecuteAsync(threadId, manager.Id, isOperator, request, cancellationToken);

        return result.Outcome switch
        {
            ForumOutcome.Success => Results.Created(
                $"{ModuleEndpointGroups.VersionPrefix}/forum/threads/{threadId}#post-{result.Post!.PostNumber}",
                result.Post),

            ForumOutcome.InvalidBody => ProblemResults.Code(
                StatusCodes.Status400BadRequest,
                ForumErrorCodes.BodyLength,
                "Invalid body.",
                "Reply body must be between 1 and 10,000 characters."),

            ForumOutcome.CountryMismatch => ProblemResults.Code(
                StatusCodes.Status403Forbidden,
                ForumErrorCodes.CountryMismatch,
                "Country mismatch.",
                "You cannot reply to threads in another country's forum."),

            _ => ProblemResults.Code(
                StatusCodes.Status404NotFound,
                ForumErrorCodes.ThreadNotFound,
                "Thread not found.",
                "The specified thread does not exist."),
        };
    }

    private static bool TryGetUserId(HttpContext httpContext, out Guid userId) =>
        Guid.TryParse(httpContext.User.FindFirst(AuthClaimNames.Subject)?.Value, out userId);

    private static bool IsOperator(HttpContext httpContext) =>
        httpContext.User.IsInRole(UserRoles.Operator) || httpContext.User.IsInRole(UserRoles.Admin);
}
