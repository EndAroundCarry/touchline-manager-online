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
/// The tactics HTTP surface (master plan §10.4, §11.1; F-19).
/// </summary>
/// <remarks>
/// <para>
/// Like the squad reads, these sit at the version root rather than under <c>/api/v1/tactics</c>'s module
/// group, because §10.4 addresses them as <c>/tactics</c> and <c>/tactics/{planId}</c>. The plan's
/// <em>version</em> is the strong entity tag: a read returns it in the body, and a save must send it back
/// in <c>If-Match</c>, so a formation changed on one device cannot be silently overwritten on another
/// (`CONC-1`, ADR-0009).
/// </para>
/// <para>
/// Handlers do three things: read the account, call one use case, and translate the outcome into a status
/// and a stable code. Which club the caller may plan for comes from their tenure, never from the request.
/// </para>
/// </remarks>
internal static class TacticsEndpoints
{
    /// <summary>Maps the tactics routes.</summary>
    public static IEndpointRouteBuilder MapTacticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup(Endpoints.ModuleEndpointGroups.VersionPrefix)
            .WithTags("squad")
            .RequireAuthorization();

        group.MapGet("/tactics", GetTacticsAsync)
            .WithName("GetTactics")
            .WithSummary("Reads the tactical plans of the club the manager holds, with the squad and formations.")
            .Produces<TacticsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/tactics", CreateTacticsAsync)
            .WithName("CreateTacticalPlan")
            .WithSummary("Creates a tactical plan, laid out from its formation preset.")
            .WithDescription(
                "The first plan a club saves becomes its default. A lineup, when sent, must name all "
                + "eleven slots; an invalid plan answers 400 PLAN_VALIDATION_FAILED with the issues.")
            .Produces<TacticalPlanResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/tactics/{planId:guid}", UpdateTacticsAsync)
            .WithName("UpdateTacticalPlan")
            .WithSummary("Revises a plan. Requires If-Match with the plan's current version.")
            .Produces<TacticalPlanResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        group.MapPost("/tactics/{planId:guid}/make-default", MakeDefaultAsync)
            .WithName("MakeTacticalPlanDefault")
            .WithSummary("Makes a plan the club's default. Requires If-Match.")
            .Produces<TacticalPlanResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        return endpoints;
    }

    private static async Task<IResult> GetTacticsAsync(
        HttpContext httpContext,
        GetTactics query,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var result = await query.ExecuteAsync(userId, cancellationToken);

        return result.Outcome == SquadReadOutcome.Found
            ? Results.Ok(result.Tactics)
            : SquadEndpoints.Refusal(result.Outcome);
    }

    private static async Task<IResult> CreateTacticsAsync(
        HttpContext httpContext,
        SaveTacticalPlanRequest request,
        IValidator<SaveTacticalPlanRequest> validator,
        SaveTacticalPlan useCase,
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

        var result = await useCase.ExecuteAsync(userId, planId: null, expectedVersion: null, request, cancellationToken);

        return result.Outcome switch
        {
            SaveTacticalPlanOutcome.Created => Created(httpContext, result),
            SaveTacticalPlanOutcome.Updated => Updated(httpContext, result),
            _ => WriteRefusal(result),
        };
    }

    private static async Task<IResult> UpdateTacticsAsync(
        HttpContext httpContext,
        Guid planId,
        SaveTacticalPlanRequest request,
        IValidator<SaveTacticalPlanRequest> validator,
        SaveTacticalPlan useCase,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        // The version is read before the body: a save without If-Match cannot be conditional, and saying so
        // is more useful than validating a plan that will not be applied (CONC-1).
        var expectedVersion = EntityTagHeader.ParseIfMatch(httpContext.Request.Headers.IfMatch.ToString());

        if (expectedVersion is null)
        {
            return PreconditionRequired();
        }

        var invalid = await RequestValidation.ValidateAsync(validator, request, cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        var result = await useCase.ExecuteAsync(userId, planId, expectedVersion, request, cancellationToken);

        return result.Outcome switch
        {
            SaveTacticalPlanOutcome.Updated => Updated(httpContext, result),
            _ => WriteRefusal(result),
        };
    }

    private static async Task<IResult> MakeDefaultAsync(
        HttpContext httpContext,
        Guid planId,
        MakeTacticalPlanDefault useCase,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return ProblemResults.Unauthenticated("Sign in to continue.");
        }

        var expectedVersion = EntityTagHeader.ParseIfMatch(httpContext.Request.Headers.IfMatch.ToString());

        if (expectedVersion is null)
        {
            return PreconditionRequired();
        }

        var result = await useCase.ExecuteAsync(userId, planId, expectedVersion, cancellationToken);

        return result.Outcome switch
        {
            MakeTacticalPlanDefaultOutcome.MadeDefault => Updated(
                httpContext,
                new SaveTacticalPlanResult(SaveTacticalPlanOutcome.Updated, result.Plan, null)),

            MakeTacticalPlanDefaultOutcome.PlanNotFound => PlanNotFound(),
            MakeTacticalPlanDefaultOutcome.PreconditionRequired => PreconditionRequired(),

            MakeTacticalPlanDefaultOutcome.PreconditionFailed => ProblemResults.Code(
                StatusCodes.Status412PreconditionFailed,
                ApiErrorCodes.PreconditionFailed,
                "The plan changed.",
                "Reload the plan and reapply the change."),

            MakeTacticalPlanDefaultOutcome.NoClub => ProblemResults.Code(
                StatusCodes.Status403Forbidden,
                SquadErrorCodes.NoClub,
                "No club.",
                "You do not manage a club yet, so there is no tactic to change."),

            MakeTacticalPlanDefaultOutcome.NoManagerProfile => ProblemResults.Code(
                StatusCodes.Status403Forbidden,
                WorldErrorCodes.ManagerProfileRequired,
                "No manager profile.",
                "Create your manager profile before managing a club."),

            MakeTacticalPlanDefaultOutcome.WorldNotSeeded => ProblemResults.Code(
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
    }

    private static IResult Created(HttpContext httpContext, SaveTacticalPlanResult result)
    {
        httpContext.Response.Headers.ETag = EntityTagHeader.ForVersion(result.Plan!.Version);

        return Results.Created(
            $"{Endpoints.ModuleEndpointGroups.VersionPrefix}/tactics",
            result.Plan);
    }

    private static IResult Updated(HttpContext httpContext, SaveTacticalPlanResult result)
    {
        httpContext.Response.Headers.ETag = EntityTagHeader.ForVersion(result.Plan!.Version);

        return Results.Ok(result.Plan);
    }

    /// <summary>Turns a write refusal into a status and a stable code.</summary>
    private static IResult WriteRefusal(SaveTacticalPlanResult result) => result.Outcome switch
    {
        SaveTacticalPlanOutcome.Invalid => ProblemResults.Code(
            StatusCodes.Status400BadRequest,
            SquadErrorCodes.PlanValidationFailed,
            "That plan is not valid.",
            "Fix the highlighted slots and save again.",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["validation"] = result.Validation,
            }),

        SaveTacticalPlanOutcome.PlanNotFound => PlanNotFound(),

        SaveTacticalPlanOutcome.PreconditionRequired => PreconditionRequired(),

        SaveTacticalPlanOutcome.PreconditionFailed => ProblemResults.Code(
            StatusCodes.Status412PreconditionFailed,
            ApiErrorCodes.PreconditionFailed,
            "The plan changed.",
            "Reload the plan and reapply the change."),

        SaveTacticalPlanOutcome.NoClub => ProblemResults.Code(
            StatusCodes.Status403Forbidden,
            SquadErrorCodes.NoClub,
            "No club.",
            "You do not manage a club yet, so there is no tactic to save."),

        SaveTacticalPlanOutcome.NoManagerProfile => ProblemResults.Code(
            StatusCodes.Status403Forbidden,
            WorldErrorCodes.ManagerProfileRequired,
            "No manager profile.",
            "Create your manager profile before managing a club."),

        SaveTacticalPlanOutcome.WorldNotSeeded => ProblemResults.Code(
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

    private static IResult PlanNotFound() => ProblemResults.Code(
        StatusCodes.Status404NotFound,
        SquadErrorCodes.PlanNotFound,
        "No such plan.",
        "That tactic does not exist for your club.");

    private static IResult PreconditionRequired() => ProblemResults.Code(
        StatusCodes.Status428PreconditionRequired,
        ApiErrorCodes.PreconditionRequired,
        "A version is required.",
        "Send the plan's current entity tag in If-Match so a concurrent change is not overwritten.");

    private static bool TryGetUserId(HttpContext httpContext, out Guid userId) =>
        Guid.TryParse(httpContext.User.FindFirst(AuthClaimNames.Subject)?.Value, out userId);
}
