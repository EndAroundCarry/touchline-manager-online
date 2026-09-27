using TouchlineManager.Api.Auth;
using TouchlineManager.Api.Endpoints;
using TouchlineManager.Api.Http;
using TouchlineManager.Application.Comms;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Contracts.Comms;
using TouchlineManager.Contracts.World;

namespace TouchlineManager.Api.Comms;

/// <summary>
/// The comms module's inbox and synchronization surface (master plan §10.7, F-41).
/// </summary>
/// <remarks>
/// <para>
/// The endpoints sit at the version root rather than under <c>/api/v1/comms</c>, because §10.7 addresses the
/// inbox as <c>/inbox</c> and the summary as <c>/sync</c>. Those paths are the contract, the same way
/// <c>/me</c> and <c>/fixtures/mine</c> are.
/// </para>
/// <para>
/// The recipient is resolved from the account and never named by the request, so a manager can only read and
/// mark their own messages (`INT-1`, §10.9).
/// </para>
/// </remarks>
internal static class CommsEndpoints
{
    /// <summary>Maps the inbox and synchronization routes.</summary>
    public static IEndpointRouteBuilder MapCommsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup(ModuleEndpointGroups.VersionPrefix)
            .WithTags("comms")
            .RequireAuthorization();

        group.MapGet("/inbox", GetInboxAsync)
            .WithName("GetInbox")
            .WithSummary("Reads a page of the manager's inbox, with the unread count.")
            .Produces<InboxResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/inbox/{messageId:guid}/read", MarkReadAsync)
            .WithName("MarkInboxMessageRead")
            .WithSummary("Marks one of the manager's messages read.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/inbox/read-all", MarkAllReadAsync)
            .WithName("MarkAllInboxRead")
            .WithSummary("Marks every unread message read.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/sync", GetSyncAsync)
            .WithName("GetSync")
            .WithSummary("Reads the unread inbox count the shell polls.")
            .Produces<SyncResponse>(StatusCodes.Status200OK);

        return endpoints;
    }

    private static async Task<IResult> GetInboxAsync(
        HttpContext httpContext,
        string? cursor,
        bool? unread,
        GetInbox query,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var result = await query.ExecuteAsync(userId, cursor, unread ?? false, cancellationToken);

        return result.Outcome == CommsOutcome.Ok
            ? Results.Ok(result.Inbox)
            : Refusal(result.Outcome);
    }

    private static async Task<IResult> MarkReadAsync(
        HttpContext httpContext,
        Guid messageId,
        MarkInboxMessagesRead command,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var outcome = await command.ExecuteAsync(userId, messageId, cancellationToken);

        return outcome == CommsOutcome.Ok ? Results.NoContent() : Refusal(outcome);
    }

    private static async Task<IResult> MarkAllReadAsync(
        HttpContext httpContext,
        MarkInboxMessagesRead command,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var outcome = await command.ExecuteAllAsync(userId, cancellationToken);

        return outcome == CommsOutcome.Ok ? Results.NoContent() : Refusal(outcome);
    }

    private static async Task<IResult> GetSyncAsync(
        HttpContext httpContext,
        GetSync query,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var result = await query.ExecuteAsync(userId, cancellationToken);

        return Results.Ok(result.Sync);
    }

    /// <summary>Turns a comms refusal into a status and a stable code.</summary>
    private static IResult Refusal(CommsOutcome outcome) => outcome switch
    {
        CommsOutcome.InvalidCursor => ProblemResults.Code(
            StatusCodes.Status400BadRequest,
            CommsErrorCodes.InvalidCursor,
            "Invalid cursor.",
            "The page cursor is not one this server produced."),

        CommsOutcome.MessageNotFound => ProblemResults.Code(
            StatusCodes.Status404NotFound,
            CommsErrorCodes.MessageNotFound,
            "No such message.",
            "That message does not exist in your inbox."),

        CommsOutcome.NoManagerProfile => ProblemResults.Code(
            StatusCodes.Status403Forbidden,
            WorldErrorCodes.ManagerProfileRequired,
            "No manager profile.",
            "Create your manager profile before reading your inbox."),

        _ => ProblemResults.Code(
            StatusCodes.Status404NotFound,
            WorldErrorCodes.WorldNotSeeded,
            "No world yet.",
            "The world has not been created."),
    };

    private static bool TryGetUserId(HttpContext httpContext, out Guid userId) =>
        Guid.TryParse(httpContext.User.FindFirst(AuthClaimNames.Subject)?.Value, out userId);
}
