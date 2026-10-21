using Microsoft.AspNetCore.RateLimiting;
using Tactical11Manager.Api.Auth;
using Tactical11Manager.Api.Endpoints;
using Tactical11Manager.Api.Http;
using Tactical11Manager.Application.Comms;
using Tactical11Manager.Contracts.Auth;
using Tactical11Manager.Contracts.Comms;
using Tactical11Manager.Contracts.Http;
using Tactical11Manager.Contracts.World;

namespace Tactical11Manager.Api.Comms;

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
/// <para>
/// Direct messages between managers sit beside the inbox at <c>/conversations</c> and <c>/blocked-managers</c>
/// (`COM-10` to `COM-16`). Nothing is pushed: a new message reaches the other side through the unread count on
/// <c>/sync</c>. The two commands that send are limited per manager (`COM-14`).
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

        group.MapGet("/conversations", GetConversationsAsync)
            .WithName("GetConversations")
            .WithSummary("Reads a page of the manager's direct-message conversations (`COM-10`).")
            .Produces<ConversationListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/conversations", StartConversationAsync)
            .WithName("StartConversation")
            .RequireAuthorization(AuthorizationPolicies.VerifiedManager)
            .RequireRateLimiting(RateLimitPolicies.DirectMessage)
            .WithSummary("Starts a conversation with the manager who runs a club (`COM-11`).")
            .Produces<SentDirectMessageResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapGet("/conversations/{conversationId:guid}", GetConversationAsync)
            .WithName("GetConversation")
            .WithSummary("Reads a page of one conversation's messages (`COM-10`).")
            .Produces<ConversationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/conversations/{conversationId:guid}/messages", ReplyToConversationAsync)
            .WithName("ReplyToConversation")
            .RequireAuthorization(AuthorizationPolicies.VerifiedManager)
            .RequireRateLimiting(RateLimitPolicies.DirectMessage)
            .WithSummary("Replies in a conversation (`COM-12`).")
            .Produces<SentDirectMessageResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/conversations/{conversationId:guid}/read", MarkConversationReadAsync)
            .WithName("MarkConversationRead")
            .WithSummary("Marks the messages addressed to the manager in a conversation read (`COM-16`).")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/blocked-managers/{managerId:guid}", BlockManagerAsync)
            .WithName("BlockManager")
            .WithSummary("Blocks a manager, so neither can write to the other (`COM-15`).")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/blocked-managers/{managerId:guid}", UnblockManagerAsync)
            .WithName("UnblockManager")
            .WithSummary("Lifts the manager's own block on another manager (`COM-15`).")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/news", GetNewsAsync)
            .WithName("GetNews")
            .WithSummary("Reads a page of the division news feed (`COM-1`).")
            .Produces<NewsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/settings/notifications", GetNotificationPreferencesAsync)
            .WithName("GetNotificationPreferences")
            .WithSummary("Reads the manager's notification preferences (`COM-4`).")
            .Produces<NotificationPreferencesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/settings/notifications", UpdateNotificationPreferencesAsync)
            .WithName("UpdateNotificationPreferences")
            .WithSummary("Changes the manager's notification preferences (`COM-4`, `CONC-1`).")
            .Produces<NotificationPreferencesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

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

    private static async Task<IResult> GetConversationsAsync(
        HttpContext httpContext,
        string? cursor,
        GetConversations query,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var result = await query.ExecuteAsync(userId, cursor, cancellationToken);

        return result.Outcome == DirectMessageOutcome.Ok
            ? Results.Ok(result.Conversations)
            : DirectMessageRefusal(result.Outcome);
    }

    private static async Task<IResult> StartConversationAsync(
        HttpContext httpContext,
        StartConversationRequest request,
        StartConversation command,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var result = await command.ExecuteAsync(
            userId,
            request.ClubId,
            request.Subject,
            request.Body,
            cancellationToken);

        return result.Outcome == DirectMessageOutcome.Ok
            ? Results.Created(
                $"{ModuleEndpointGroups.VersionPrefix}/conversations/{result.Sent!.ConversationId}",
                result.Sent)
            : DirectMessageRefusal(result.Outcome);
    }

    private static async Task<IResult> GetConversationAsync(
        HttpContext httpContext,
        Guid conversationId,
        string? cursor,
        GetConversation query,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var result = await query.ExecuteAsync(userId, conversationId, cursor, cancellationToken);

        return result.Outcome == DirectMessageOutcome.Ok
            ? Results.Ok(result.Conversation)
            : DirectMessageRefusal(result.Outcome);
    }

    private static async Task<IResult> ReplyToConversationAsync(
        HttpContext httpContext,
        Guid conversationId,
        SendDirectMessageRequest request,
        ReplyToConversation command,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var result = await command.ExecuteAsync(userId, conversationId, request.Body, cancellationToken);

        return result.Outcome == DirectMessageOutcome.Ok
            ? Results.Created(
                $"{ModuleEndpointGroups.VersionPrefix}/conversations/{result.Sent!.ConversationId}",
                result.Sent)
            : DirectMessageRefusal(result.Outcome);
    }

    private static async Task<IResult> MarkConversationReadAsync(
        HttpContext httpContext,
        Guid conversationId,
        MarkConversationRead command,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var outcome = await command.ExecuteAsync(userId, conversationId, cancellationToken);

        return outcome == DirectMessageOutcome.Ok ? Results.NoContent() : DirectMessageRefusal(outcome);
    }

    private static async Task<IResult> BlockManagerAsync(
        HttpContext httpContext,
        Guid managerId,
        BlockManager command,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var outcome = await command.ExecuteAsync(userId, managerId, cancellationToken);

        return outcome == DirectMessageOutcome.Ok ? Results.NoContent() : DirectMessageRefusal(outcome);
    }

    private static async Task<IResult> UnblockManagerAsync(
        HttpContext httpContext,
        Guid managerId,
        UnblockManager command,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var outcome = await command.ExecuteAsync(userId, managerId, cancellationToken);

        return outcome == DirectMessageOutcome.Ok ? Results.NoContent() : DirectMessageRefusal(outcome);
    }

    private static async Task<IResult> GetNewsAsync(
        string? cursor,
        Guid? divisionId,
        Guid? countryId,
        GetNews query,
        CancellationToken cancellationToken)
    {
        // The feed is public game data, but it needs no manager profile; a client that is signed in simply
        // reads it, exactly like the division table (`COM-1`).
        var result = await query.ExecuteAsync(divisionId, countryId, cursor, cancellationToken);

        return result.Outcome == CommsOutcome.Ok
            ? Results.Ok(result.News)
            : Refusal(result.Outcome);
    }

    private static async Task<IResult> GetNotificationPreferencesAsync(
        HttpContext httpContext,
        GetNotificationPreferences query,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var result = await query.ExecuteAsync(userId, cancellationToken);

        if (result.Outcome != NotificationPreferencesOutcome.Ok || result.Preferences is null)
        {
            return PreferencesRefusal(result.Outcome);
        }

        httpContext.Response.Headers.ETag = EntityTagHeader.ForVersion(result.Preferences.Version);

        return Results.Ok(result.Preferences);
    }

    private static async Task<IResult> UpdateNotificationPreferencesAsync(
        HttpContext httpContext,
        UpdateNotificationPreferencesRequest request,
        UpdateNotificationPreferences useCase,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        // The version is read before the body: a change without If-Match cannot be conditional, and saying so
        // is more useful than applying a change that may overwrite another device's (CONC-1).
        var expectedVersion = EntityTagHeader.ParseIfMatch(httpContext.Request.Headers.IfMatch.ToString());

        if (expectedVersion is null)
        {
            return PreconditionRequired();
        }

        var result = await useCase.ExecuteAsync(userId, request, expectedVersion.Value, cancellationToken);

        if (result.Outcome != NotificationPreferencesOutcome.Ok || result.Preferences is null)
        {
            return PreferencesRefusal(result.Outcome);
        }

        httpContext.Response.Headers.ETag = EntityTagHeader.ForVersion(result.Preferences.Version);

        return Results.Ok(result.Preferences);
    }

    /// <summary>Turns a notification-preferences refusal into a status and a stable code.</summary>
    private static IResult PreferencesRefusal(NotificationPreferencesOutcome outcome) => outcome switch
    {
        NotificationPreferencesOutcome.PreconditionFailed => ProblemResults.Code(
            StatusCodes.Status412PreconditionFailed,
            ApiErrorCodes.PreconditionFailed,
            "The preferences changed.",
            "Reload your notification settings and reapply the change."),

        _ => ProblemResults.Code(
            StatusCodes.Status403Forbidden,
            WorldErrorCodes.ManagerProfileRequired,
            "No manager profile.",
            "Create your manager profile before changing your notifications."),
    };

    private static IResult PreconditionRequired() => ProblemResults.Code(
        StatusCodes.Status428PreconditionRequired,
        ApiErrorCodes.PreconditionRequired,
        "A version is required.",
        "Send the preferences' current entity tag in If-Match so a concurrent change is not overwritten.");

    /// <summary>Turns a direct-message refusal into a status and a stable code.</summary>
    private static IResult DirectMessageRefusal(DirectMessageOutcome outcome) => outcome switch
    {
        DirectMessageOutcome.InvalidCursor => ProblemResults.Code(
            StatusCodes.Status400BadRequest,
            CommsErrorCodes.InvalidCursor,
            "Invalid cursor.",
            "The page cursor is not one this server produced."),

        DirectMessageOutcome.InvalidText => ProblemResults.Code(
            StatusCodes.Status400BadRequest,
            CommsErrorCodes.InvalidMessageText,
            "Invalid message.",
            "A subject is 1 to 80 characters and a message is 1 to 1,000 characters, once trimmed."),

        DirectMessageOutcome.NoManagerProfile => ProblemResults.Code(
            StatusCodes.Status403Forbidden,
            WorldErrorCodes.ManagerProfileRequired,
            "No manager profile.",
            "Create your manager profile before writing to other managers."),

        DirectMessageOutcome.ClubNotFound => ProblemResults.Code(
            StatusCodes.Status404NotFound,
            WorldErrorCodes.ClubNotFound,
            "No such club.",
            "That club does not exist."),

        DirectMessageOutcome.ConversationNotFound => ProblemResults.Code(
            StatusCodes.Status404NotFound,
            CommsErrorCodes.ConversationNotFound,
            "No such conversation.",
            "That conversation does not exist among yours."),

        DirectMessageOutcome.ManagerNotFound => ProblemResults.Code(
            StatusCodes.Status404NotFound,
            CommsErrorCodes.ManagerNotFound,
            "No such manager.",
            "That manager does not exist."),

        DirectMessageOutcome.SenderHasNoClub => ProblemResults.Code(
            StatusCodes.Status409Conflict,
            CommsErrorCodes.SenderHasNoClub,
            "You hold no club.",
            "Take over a club before you start a conversation with another manager."),

        DirectMessageOutcome.ClubHasNoManager => ProblemResults.Code(
            StatusCodes.Status409Conflict,
            CommsErrorCodes.ClubHasNoManager,
            "Nobody to write to.",
            "The game runs this club, so there is no manager to message."),

        DirectMessageOutcome.OwnClub => ProblemResults.Code(
            StatusCodes.Status409Conflict,
            CommsErrorCodes.CannotMessageOwnClub,
            "That is your own club.",
            "You cannot start a conversation with yourself."),

        DirectMessageOutcome.RecipientUnavailable => ProblemResults.Code(
            StatusCodes.Status409Conflict,
            CommsErrorCodes.RecipientUnavailable,
            "Manager unavailable.",
            "This manager's account is closing, so they cannot receive messages."),

        _ => ProblemResults.Code(
            StatusCodes.Status409Conflict,
            CommsErrorCodes.ManagerBlocked,
            "Messages not accepted.",
            "This manager is not accepting your messages, or you have blocked them."),
    };

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
