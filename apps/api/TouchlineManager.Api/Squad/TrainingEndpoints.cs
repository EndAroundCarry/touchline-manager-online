using FluentValidation;
using TouchlineManager.Api.Auth;
using TouchlineManager.Api.Http;
using TouchlineManager.Application.Squad;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Contracts.Http;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Contracts.World;

namespace TouchlineManager.Api.Squad;

/// <summary>
/// The training HTTP surface (master plan §10.4, §11.1; `TRN-1`, `TRN-2`).
/// </summary>
/// <remarks>
/// <para>
/// Like the squad and tactics endpoints, these sit at the version root rather than under a module group,
/// because §10.4 addresses them as <c>/training</c> and <c>/players/{playerId}/training-focus</c>. The
/// training plan's <em>version</em> is the strong entity tag: a read returns it in the body, and a change
/// to a set plan must send it back in <c>If-Match</c>, so two devices editing a club's training cannot
/// silently overwrite each other (`CONC-1`, ADR-0009).
/// </para>
/// <para>
/// Handlers do three things: read the account, call one use case, and translate the outcome into a status
/// and a stable code. Which club the caller may train comes from their tenure, and which player from the
/// player's own contract, never from the request.
/// </para>
/// </remarks>
internal static class TrainingEndpoints
{
    /// <summary>Maps the training routes.</summary>
    public static IEndpointRouteBuilder MapTrainingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup(Endpoints.ModuleEndpointGroups.VersionPrefix)
            .WithTags("squad")
            .RequireAuthorization();

        group.MapGet("/training", GetTrainingAsync)
            .WithName("GetTraining")
            .WithSummary("Reads the training plan and squad of the club the manager holds.")
            .Produces<TrainingResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/training", SaveTrainingAsync)
            .WithName("SaveTrainingPlan")
            .WithSummary("Sets or revises the club's training plan. Requires If-Match when one exists.")
            .Produces<TrainingResponse>(StatusCodes.Status200OK)
            .Produces<TrainingResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        group.MapPut("/players/{playerId:guid}/training-focus", SetTrainingFocusAsync)
            .WithName("SetPlayerTrainingFocus")
            .WithSummary("Sets or clears one player's individual training focus. Requires If-Match when one is set.")
            .Produces<PlayerTrainingFocusResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        return endpoints;
    }

    private static async Task<IResult> GetTrainingAsync(
        HttpContext httpContext,
        GetTraining query,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var result = await query.ExecuteAsync(userId, cancellationToken);

        if (result.Outcome != SquadReadOutcome.Found)
        {
            return SquadEndpoints.Refusal(result.Outcome);
        }

        httpContext.Response.Headers.ETag = EntityTagHeader.ForVersion(result.Training!.Version);

        return Results.Ok(result.Training);
    }

    private static async Task<IResult> SaveTrainingAsync(
        HttpContext httpContext,
        SaveTrainingRequest request,
        IValidator<SaveTrainingRequest> validator,
        SaveTrainingPlan useCase,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        // The version is read before the body: a change to a set plan without If-Match cannot be
        // conditional, and saying so is more useful than validating a plan that will not be applied.
        var expectedVersion = EntityTagHeader.ParseIfMatch(httpContext.Request.Headers.IfMatch.ToString());

        var invalid = await RequestValidation.ValidateAsync(validator, request, cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        var result = await useCase.ExecuteAsync(userId, expectedVersion, request, cancellationToken);

        return result.Outcome switch
        {
            SaveTrainingPlanOutcome.Created => Written(httpContext, result.Training!, created: true),
            SaveTrainingPlanOutcome.Updated => Written(httpContext, result.Training!, created: false),
            _ => WriteRefusal(result.Outcome),
        };
    }

    private static async Task<IResult> SetTrainingFocusAsync(
        HttpContext httpContext,
        Guid playerId,
        SetPlayerTrainingFocusRequest request,
        IValidator<SetPlayerTrainingFocusRequest> validator,
        SetPlayerTrainingFocus useCase,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var expectedVersion = EntityTagHeader.ParseIfMatch(httpContext.Request.Headers.IfMatch.ToString());

        var invalid = await RequestValidation.ValidateAsync(validator, request, cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        var result = await useCase.ExecuteAsync(
            userId,
            playerId,
            expectedVersion,
            request,
            cancellationToken);

        if (result.Outcome is SetPlayerTrainingFocusOutcome.Set or SetPlayerTrainingFocusOutcome.Cleared)
        {
            httpContext.Response.Headers.ETag = EntityTagHeader.ForVersion(result.Focus!.Version);

            return Results.Ok(result.Focus);
        }

        return FocusRefusal(result.Outcome);
    }

    private static IResult Written(HttpContext httpContext, TrainingResponse training, bool created)
    {
        httpContext.Response.Headers.ETag = EntityTagHeader.ForVersion(training.Version);

        return created
            ? Results.Created($"{Endpoints.ModuleEndpointGroups.VersionPrefix}/training", training)
            : Results.Ok(training);
    }

    /// <summary>Turns a training-plan write refusal into a status and a stable code.</summary>
    private static IResult WriteRefusal(SaveTrainingPlanOutcome outcome) => outcome switch
    {
        SaveTrainingPlanOutcome.PreconditionRequired => PreconditionRequired(),

        SaveTrainingPlanOutcome.PreconditionFailed => PreconditionFailed(),

        SaveTrainingPlanOutcome.NoClub => ProblemResults.Code(
            StatusCodes.Status403Forbidden,
            SquadErrorCodes.NoClub,
            "No club.",
            "You do not manage a club yet, so there is no training to set."),

        SaveTrainingPlanOutcome.NoManagerProfile => ProblemResults.Code(
            StatusCodes.Status403Forbidden,
            WorldErrorCodes.ManagerProfileRequired,
            "No manager profile.",
            "Create your manager profile before managing a club."),

        SaveTrainingPlanOutcome.WorldNotSeeded => ProblemResults.Code(
            StatusCodes.Status404NotFound,
            WorldErrorCodes.WorldNotSeeded,
            "No world yet.",
            "The world has not been created."),

        _ => ProblemResults.Code(
            StatusCodes.Status404NotFound,
            WorldErrorCodes.ClubNotFound,
            "No such club.",
            "That club does not exist in this world."),
    };

    /// <summary>Turns an individual-focus write refusal into a status and a stable code.</summary>
    private static IResult FocusRefusal(SetPlayerTrainingFocusOutcome outcome) => outcome switch
    {
        SetPlayerTrainingFocusOutcome.PreconditionRequired => PreconditionRequired(),

        SetPlayerTrainingFocusOutcome.PreconditionFailed => PreconditionFailed(),

        SetPlayerTrainingFocusOutcome.NoClub => ProblemResults.Code(
            StatusCodes.Status403Forbidden,
            SquadErrorCodes.NoClub,
            "No club.",
            "You do not manage a club yet, so there is no training to set."),

        SetPlayerTrainingFocusOutcome.ClubNotManaged => ProblemResults.Code(
            StatusCodes.Status403Forbidden,
            SquadErrorCodes.ClubNotManaged,
            "Not your club.",
            "You do not manage that player's club."),

        SetPlayerTrainingFocusOutcome.NoManagerProfile => ProblemResults.Code(
            StatusCodes.Status403Forbidden,
            WorldErrorCodes.ManagerProfileRequired,
            "No manager profile.",
            "Create your manager profile before managing a club."),

        SetPlayerTrainingFocusOutcome.PlayerNotFound => ProblemResults.Code(
            StatusCodes.Status404NotFound,
            SquadErrorCodes.PlayerNotFound,
            "No such player.",
            "That player is not in a squad in this world."),

        SetPlayerTrainingFocusOutcome.WorldNotSeeded => ProblemResults.Code(
            StatusCodes.Status404NotFound,
            WorldErrorCodes.WorldNotSeeded,
            "No world yet.",
            "The world has not been created."),

        _ => ProblemResults.Code(
            StatusCodes.Status404NotFound,
            WorldErrorCodes.ClubNotFound,
            "No such club.",
            "That club does not exist in this world."),
    };

    private static IResult PreconditionRequired() => ProblemResults.Code(
        StatusCodes.Status428PreconditionRequired,
        ApiErrorCodes.PreconditionRequired,
        "A version is required.",
        "Send the current entity tag in If-Match so a concurrent change is not overwritten.");

    private static IResult PreconditionFailed() => ProblemResults.Code(
        StatusCodes.Status412PreconditionFailed,
        ApiErrorCodes.PreconditionFailed,
        "That changed.",
        "Reload the training state and reapply the change.");

    private static bool TryGetUserId(HttpContext httpContext, out Guid userId) =>
        Guid.TryParse(httpContext.User.FindFirst(AuthClaimNames.Subject)?.Value, out userId);
}
