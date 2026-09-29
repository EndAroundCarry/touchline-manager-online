using TouchlineManager.Api.Http;
using TouchlineManager.Application.Competition;
using TouchlineManager.Application.Jobs;
using TouchlineManager.Application.Market;
using TouchlineManager.Application.World;
using TouchlineManager.Contracts.Competition;
using TouchlineManager.Contracts.Market;
using TouchlineManager.Contracts.World;

namespace TouchlineManager.Api.Endpoints;

/// <summary>
/// Stage 1 diagnostics for the ops module.
/// </summary>
/// <remarks>
/// This is the walking skeleton: it proves that a request can enqueue a durable job, that the row
/// lands in <c>ops.jobs</c>, and that a separate worker process claims, executes, and completes it.
/// Real deadline endpoints replace it from Stage 2 onward.
/// </remarks>
internal static class OpsEndpoints
{
    /// <summary>Maps the diagnostic job probe onto the ops module group.</summary>
    public static RouteGroupBuilder MapJobProbe(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group
            .MapPost("/diagnostics/noop-job", EnqueueNoOpJobAsync)
            .WithName("EnqueueNoOpJobProbe")
            .WithSummary("Enqueues a no-op durable job to verify the API, database, and worker pipeline.")
            .WithDescription(
                "Development and staging diagnostics only. Supply the same key twice to observe "
                + "enqueue idempotency: the second call reports enqueued=false and creates no second row.")
            .Produces<NoOpJobProbeResponse>(StatusCodes.Status202Accepted);

        return group;
    }

    /// <summary>Maps the Stage 7 matchday trigger onto the ops module group.</summary>
    /// <remarks>
    /// It exists so the end-to-end journey can watch a real round play without waiting for its calendar
    /// deadline. It enqueues the round's real lock and resolution jobs; the worker still does all of the
    /// work (ADR-0016). Like the job probe it is mapped only when the diagnostics flag is on.
    /// </remarks>
    public static RouteGroupBuilder MapMatchdayTrigger(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group
            .MapPost("/diagnostics/play-matchday", PlayMatchdayAsync)
            .WithName("PlayMatchdayProbe")
            .WithSummary("Enqueues a round's real lock and resolution jobs, due now, so a matchday can be watched.")
            .WithDescription(
                "Development and staging diagnostics only. The worker locks, simulates, and publishes the "
                + "round exactly as it would on the calendar; this only does what the worker-only scheduler "
                + "normally does, and a repeated call is a no-op.")
            .Produces<MatchdayTriggerResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    /// <summary>Maps the market's non-production auction trigger onto the ops module group.</summary>
    /// <remarks>
    /// The market counterpart of the matchday trigger: it enqueues a listing's real resolution job due now, so
    /// a journey can watch an auction settle without waiting for its window. The worker still resolves the
    /// listing exactly as it would on the calendar (ADR-0016). Mapped only when the diagnostics flag is on.
    /// </remarks>
    public static RouteGroupBuilder MapAuctionTrigger(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group
            .MapPost("/diagnostics/play-auction", PlayAuctionAsync)
            .WithName("PlayAuctionProbe")
            .WithSummary("Enqueues a listing's resolution job, due now, so an auction can be watched.")
            .WithDescription(
                "Development and staging diagnostics only. The worker resolves the listing exactly as it "
                + "would at its window; this only does what the worker-only scheduler normally does, and a "
                + "repeated call is a no-op.")
            .Produces<AuctionTriggerResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    /// <summary>Maps the Stage 11 provisioning trigger onto the ops module group.</summary>
    /// <remarks>
    /// It exists so a journey can grow the pyramid without filling a tier by hand. It creates the request and
    /// enqueues the worker's real provisioning job, due now; the worker still generates, backfills, validates,
    /// and activates the tier (§17.12, ADR-0016's pattern).
    /// </remarks>
    public static RouteGroupBuilder MapProvisioningTrigger(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group
            .MapPost("/diagnostics/provision-division", ProvisionDivisionAsync)
            .WithName("ProvisionDivisionProbe")
            .WithSummary("Creates a tier's provisioning request and enqueues its job, due now.")
            .WithDescription(
                "Development and staging diagnostics only. The worker generates, backfills, validates, and "
                + "activates the tier exactly as it would when a takeover fills one; this only does what the "
                + "worker-only scheduler normally does, and a repeated call is a no-op.")
            .Produces<ProvisioningTriggerResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    /// <summary>Maps the Stage 11 inactivity trigger onto the ops module group.</summary>
    /// <remarks>
    /// It enqueues the worker's real daily ladder job, due now, so a journey can watch the ladder warn,
    /// inactivate, and close tenures without waiting for the day's schedule (§17.12).
    /// </remarks>
    public static RouteGroupBuilder MapInactivityTrigger(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group
            .MapPost("/diagnostics/run-inactivity", RunInactivityAsync)
            .WithName("RunInactivityProbe")
            .WithSummary("Enqueues today's inactivity-ladder job, due now.")
            .WithDescription(
                "Development and staging diagnostics only. The worker runs the ladder exactly as it would on "
                + "its daily tick, and a repeated call on the same day is a no-op.")
            .Produces<InactivityTriggerResponse>(StatusCodes.Status202Accepted);

        return group;
    }

    /// <summary>Maps the Stage 12 operator rollover controls onto the ops module group.</summary>
    /// <remarks>
    /// Three non-production controls for the season rollover (ADR-0034): a read-only preview of what a
    /// rollover would move, pay, and refuse; a run-now enqueue of the season's real rollover job; and an
    /// audited resume of a failed rollover. The worker still runs the state machine, so the rollover itself
    /// stays worker-only (ADR-0031 §7), and the whole group is mapped only when the diagnostics flag is on.
    /// </remarks>
    public static RouteGroupBuilder MapRolloverControls(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group
            .MapPost("/diagnostics/preview-rollover", PreviewRolloverAsync)
            .WithName("PreviewSeasonRollover")
            .WithSummary("Reads what a season rollover would do, without writing anything (ADR-0034).")
            .WithDescription(
                "Development and staging diagnostics only. Reports preflight, the promotion/relegation plan "
                + "for every country, and the position awards the finalize phase would pay. It takes no lock "
                + "and writes nothing.")
            .Produces<RolloverPreviewResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group
            .MapPost("/diagnostics/run-rollover", RunRolloverAsync)
            .WithName("RunSeasonRollover")
            .WithSummary("Enqueues a season's real rollover job, due now, so a season can be closed on demand.")
            .WithDescription(
                "Development and staging diagnostics only. The worker runs the resumable state machine exactly "
                + "as it would when the calendar deadline passes; this only does what the worker-only "
                + "scheduler normally does, and a repeated call is a no-op.")
            .Produces<RolloverTriggerResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group
            .MapPost("/diagnostics/resume-rollover", ResumeRolloverAsync)
            .WithName("ResumeSeasonRollover")
            .WithSummary("Retries a failed rollover and returns its dead-lettered job to the queue.")
            .WithDescription(
                "Development and staging diagnostics only. Requires a reason, recorded in the audit trail. "
                + "The rollover resumes from its checkpoint; the worker still does all of the work.")
            .Produces<RolloverResumeResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return group;
    }

    private static async Task<IResult> ProvisionDivisionAsync(
        ProvisionDivisionRequest request,
        TriggerProvisioning trigger,
        CancellationToken cancellationToken)
    {
        var result = await trigger.ExecuteAsync(request.CountryId, request.TargetTier, cancellationToken);

        return result.Outcome switch
        {
            TriggerProvisioningOutcome.Enqueued => Results.Json(
                new ProvisioningTriggerResponse(
                    result.CountryId,
                    result.TargetTier,
                    result.RequestId,
                    result.BusinessKey,
                    result.Enqueued),
                statusCode: StatusCodes.Status202Accepted),

            TriggerProvisioningOutcome.InvalidTier or TriggerProvisioningOutcome.NoSeason => ProblemResults.Code(
                StatusCodes.Status400BadRequest,
                WorldErrorCodes.ProvisioningTargetInvalid,
                "The target tier cannot be provisioned.",
                "Tier 1 is seeded; ask for a tier of 2 or above in a world that has a running season."),

            _ => ProblemResults.Code(
                StatusCodes.Status404NotFound,
                WorldErrorCodes.CountryNotFound,
                "No country was found.",
                "The requested country does not exist."),
        };
    }

    private static async Task<IResult> RunInactivityAsync(
        TriggerInactivity trigger,
        CancellationToken cancellationToken)
    {
        var result = await trigger.ExecuteAsync(cancellationToken);

        return Results.Json(
            new InactivityTriggerResponse(result.BusinessKey, result.Enqueued),
            statusCode: StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> PreviewRolloverAsync(
        RolloverPreviewRequest? request,
        PreviewSeasonRollover preview,
        CancellationToken cancellationToken)
    {
        var result = await preview.ExecuteAsync(request?.SeasonId, cancellationToken);

        if (result is null)
        {
            return ProblemResults.Code(
                StatusCodes.Status404NotFound,
                CompetitionErrorCodes.RolloverNotFound,
                "No season was found.",
                "The world or the requested season does not exist.");
        }

        return Results.Json(RolloverPreviewResponse.From(result), statusCode: StatusCodes.Status200OK);
    }

    private static async Task<IResult> RunRolloverAsync(
        RolloverTriggerRequest? request,
        TriggerSeasonRollover trigger,
        CancellationToken cancellationToken)
    {
        var result = await trigger.ExecuteAsync(request?.SeasonId, cancellationToken);

        if (result.Outcome == TriggerSeasonRolloverOutcome.SeasonNotFound)
        {
            return ProblemResults.Code(
                StatusCodes.Status404NotFound,
                CompetitionErrorCodes.RolloverNotFound,
                "No season was found.",
                "The world or the requested season does not exist.");
        }

        return Results.Json(
            new RolloverTriggerResponse(result.SeasonId, result.BusinessKey, result.Enqueued),
            statusCode: StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> ResumeRolloverAsync(
        RolloverResumeRequest request,
        ResumeSeasonRollover resume,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Length > ResumeSeasonRollover.ReasonMaxLength)
        {
            return ProblemResults.Code(
                StatusCodes.Status400BadRequest,
                CompetitionErrorCodes.RolloverResumeReasonRequired,
                "A reason is required to resume a rollover.",
                "Send why the rollover is being resumed, of at most "
                + $"{ResumeSeasonRollover.ReasonMaxLength} characters.");
        }

        var result = await resume.ExecuteAsync(request.SeasonId, request.Reason, cancellationToken);

        return result.Outcome switch
        {
            ResumeSeasonRolloverOutcome.Resumed => Results.Json(
                new RolloverResumeResponse(
                    result.SeasonId,
                    result.RolloverId!.Value,
                    result.BusinessKey,
                    result.Requeued,
                    ResumeSeasonRolloverOutcome.Resumed.ToString()),
                statusCode: StatusCodes.Status202Accepted),

            ResumeSeasonRolloverOutcome.NotFound => ProblemResults.Code(
                StatusCodes.Status404NotFound,
                CompetitionErrorCodes.RolloverNotFound,
                "No rollover was found.",
                "The requested season has no rollover to resume."),

            _ => ProblemResults.Code(
                StatusCodes.Status409Conflict,
                CompetitionErrorCodes.RolloverNotResumable,
                "The rollover cannot be resumed.",
                "Only a failed rollover can be resumed; this one has already completed or is in progress."),
        };
    }

    private static async Task<IResult> PlayAuctionAsync(
        PlayAuctionRequest request,
        TriggerAuctions trigger,
        CancellationToken cancellationToken)
    {
        var result = await trigger.ExecuteAsync(request.ListingId, cancellationToken);

        if (result.Outcome == MarketOutcome.NotFound)
        {
            return ProblemResults.Code(
                StatusCodes.Status404NotFound,
                MarketErrorCodes.ListingNotFound,
                "No listing was found.",
                "The requested listing does not exist.");
        }

        return Results.Json(
            new AuctionTriggerResponse(result.ListingId, result.ResolveKey, result.Enqueued),
            statusCode: StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> PlayMatchdayAsync(
        PlayMatchdayRequest request,
        TriggerMatchday trigger,
        CancellationToken cancellationToken)
    {
        var result = await trigger.ExecuteAsync(request.MatchdayId, cancellationToken);

        if (result.Outcome == TriggerMatchdayOutcome.MatchdayNotFound)
        {
            return ProblemResults.Code(
                StatusCodes.Status404NotFound,
                CompetitionErrorCodes.MatchdayNotFound,
                "No matchday was found.",
                "The requested matchday does not exist.");
        }

        return Results.Json(
            new MatchdayTriggerResponse(
                result.MatchdayId,
                result.LockKey,
                result.ResolveKey,
                result.LockEnqueued,
                result.ResolveEnqueued),
            statusCode: StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> EnqueueNoOpJobAsync(
        EnqueueNoOpJob useCase,
        CancellationToken cancellationToken,
        string? key = null)
    {
        // A caller-supplied key makes the probe repeatable and demonstrates idempotency. Real jobs
        // derive their business key from domain identity, never from a random value.
        var suffix = string.IsNullOrWhiteSpace(key) ? Guid.CreateVersion7().ToString() : key;

        var (businessKey, enqueued) = await useCase.ExecuteAsync(suffix, cancellationToken);

        return Results.Json(
            new NoOpJobProbeResponse(businessKey, enqueued),
            statusCode: StatusCodes.Status202Accepted);
    }
}

/// <summary>Response of the diagnostic job probe.</summary>
/// <param name="BusinessKey">The business key the job was enqueued under.</param>
/// <param name="Enqueued">
/// <see langword="true"/> when a new row was inserted; <see langword="false"/> when a job with the
/// same business key already existed and the enqueue was a no-op.
/// </param>
internal sealed record NoOpJobProbeResponse(string BusinessKey, bool Enqueued);

/// <summary>The body of the matchday trigger.</summary>
/// <param name="MatchdayId">The round to play.</param>
internal sealed record PlayMatchdayRequest(Guid MatchdayId);

/// <summary>Response of the matchday trigger.</summary>
/// <param name="MatchdayId">The round that was triggered.</param>
/// <param name="LockKey">The business key of the enqueued lock job.</param>
/// <param name="ResolveKey">The business key of the enqueued resolution job.</param>
/// <param name="LockEnqueued"><see langword="true"/> when the lock row was newly inserted.</param>
/// <param name="ResolveEnqueued"><see langword="true"/> when the resolution row was newly inserted.</param>
internal sealed record MatchdayTriggerResponse(
    Guid MatchdayId,
    string LockKey,
    string ResolveKey,
    bool LockEnqueued,
    bool ResolveEnqueued);

/// <summary>The body of the auction trigger.</summary>
/// <param name="ListingId">The listing to resolve.</param>
internal sealed record PlayAuctionRequest(Guid ListingId);

/// <summary>Response of the auction trigger.</summary>
/// <param name="ListingId">The listing that was triggered.</param>
/// <param name="ResolveKey">The business key of the enqueued resolution job.</param>
/// <param name="Enqueued"><see langword="true"/> when the resolution row was newly inserted.</param>
internal sealed record AuctionTriggerResponse(Guid ListingId, string ResolveKey, bool Enqueued);

/// <summary>The body of the provisioning trigger.</summary>
/// <param name="CountryId">The country whose pyramid grows.</param>
/// <param name="TargetTier">The tier to create, at least 2.</param>
internal sealed record ProvisionDivisionRequest(Guid CountryId, int TargetTier);

/// <summary>Response of the provisioning trigger.</summary>
/// <param name="CountryId">The country that was triggered.</param>
/// <param name="TargetTier">The tier that was requested.</param>
/// <param name="RequestId">The provisioning request, once it exists.</param>
/// <param name="BusinessKey">The business key of the enqueued job.</param>
/// <param name="Enqueued"><see langword="true"/> when the provisioning row was newly inserted.</param>
internal sealed record ProvisioningTriggerResponse(
    Guid CountryId,
    int TargetTier,
    Guid? RequestId,
    string BusinessKey,
    bool Enqueued);

/// <summary>Response of the inactivity trigger.</summary>
/// <param name="BusinessKey">The business key of the enqueued job.</param>
/// <param name="Enqueued"><see langword="true"/> when the ladder row was newly inserted.</param>
internal sealed record InactivityTriggerResponse(string BusinessKey, bool Enqueued);

/// <summary>The body of the rollover preview request.</summary>
/// <param name="SeasonId">The season to preview, or null for the world's current season.</param>
internal sealed record RolloverPreviewRequest(Guid? SeasonId);

/// <summary>The body of the rollover trigger request.</summary>
/// <param name="SeasonId">The season to close, or null for the world's current season.</param>
internal sealed record RolloverTriggerRequest(Guid? SeasonId);

/// <summary>Response of the rollover trigger.</summary>
/// <param name="SeasonId">The season that will close.</param>
/// <param name="BusinessKey">The business key of the enqueued job.</param>
/// <param name="Enqueued"><see langword="true"/> when a new job row was inserted.</param>
internal sealed record RolloverTriggerResponse(Guid SeasonId, string BusinessKey, bool Enqueued);

/// <summary>The body of the rollover resume request.</summary>
/// <param name="SeasonId">The season whose failed rollover is resumed.</param>
/// <param name="Reason">Why the operator is resuming it. Required, and stored in the audit trail.</param>
internal sealed record RolloverResumeRequest(Guid SeasonId, string? Reason);

/// <summary>Response of the rollover resume.</summary>
/// <param name="SeasonId">The season whose rollover was resumed.</param>
/// <param name="RolloverId">The rollover row.</param>
/// <param name="BusinessKey">The business key of the requeued job.</param>
/// <param name="Requeued"><see langword="true"/> when a dead-lettered job row was reset.</param>
/// <param name="Outcome">The resume outcome, as a stable code.</param>
internal sealed record RolloverResumeResponse(
    Guid SeasonId,
    Guid RolloverId,
    string BusinessKey,
    bool Requeued,
    string Outcome);

/// <summary>One club's movement in a previewed rollover.</summary>
/// <param name="ClubId">The club.</param>
/// <param name="FromTier">The tier it played in.</param>
/// <param name="ToTier">The tier it would play in next.</param>
/// <param name="IsPromoted">Whether it would go up.</param>
/// <param name="IsRelegated">Whether it would go down.</param>
internal sealed record RolloverMovementResponse(
    Guid ClubId,
    int FromTier,
    int ToTier,
    bool IsPromoted,
    bool IsRelegated);

/// <summary>One country's movements in a previewed rollover.</summary>
/// <param name="CountryId">The country.</param>
/// <param name="Code">The country's code.</param>
/// <param name="DisplayName">The country's name.</param>
/// <param name="Movements">One movement per club.</param>
internal sealed record RolloverCountryResponse(
    Guid CountryId,
    string Code,
    string DisplayName,
    IReadOnlyList<RolloverMovementResponse> Movements);

/// <summary>One division-season's preflight reconciliation.</summary>
/// <param name="DivisionSeasonId">The division-season.</param>
/// <param name="TierNumber">The tier.</param>
/// <param name="Outcome">The reconciliation outcome, as a stable code.</param>
/// <param name="StandingsDrifted">How many table rows differed.</param>
/// <param name="PlayerStatsDrifted">How many player lines differed or had no source.</param>
internal sealed record RolloverDivisionResponse(
    Guid DivisionSeasonId,
    int TierNumber,
    string Outcome,
    int StandingsDrifted,
    int PlayerStatsDrifted);

/// <summary>The preflight a rollover would run.</summary>
/// <param name="UnpublishedMatchdays">Rounds of the closing season not yet published.</param>
/// <param name="Reconciles">Whether every division-season reconciles with its published results.</param>
/// <param name="Problem">Why the plan could not be read at all, when it could not.</param>
/// <param name="Divisions">The per-division reconciliation reads.</param>
internal sealed record RolloverPreflightResponse(
    int UnpublishedMatchdays,
    bool Reconciles,
    string? Problem,
    IReadOnlyList<RolloverDivisionResponse> Divisions);

/// <summary>The totals a previewed rollover would produce.</summary>
/// <param name="Countries">How many countries would move.</param>
/// <param name="Clubs">How many clubs would be placed.</param>
/// <param name="Promotions">How many clubs would go up.</param>
/// <param name="Relegations">How many clubs would go down.</param>
/// <param name="PositionAwardsMinor">The position awards the finalize phase would pay, in minor units.</param>
internal sealed record RolloverTotalsResponse(
    int Countries,
    int Clubs,
    int Promotions,
    int Relegations,
    long PositionAwardsMinor);

/// <summary>What a rollover would do, read without writing anything.</summary>
/// <param name="WorldId">The world.</param>
/// <param name="SeasonId">The season that would close.</param>
/// <param name="SeasonLabel">The season's label.</param>
/// <param name="SeasonStatus">The season's lifecycle state, as a stable code.</param>
/// <param name="RolloverPhase">The rollover's checkpoint, or null when none has started.</param>
/// <param name="FailureReason">Why a failed rollover stopped, when it did.</param>
/// <param name="Preflight">The preflight checks the run would perform.</param>
/// <param name="NextSeasonExists">Whether the next season already exists.</param>
/// <param name="Countries">The movement plan per country.</param>
/// <param name="Totals">The plan's totals.</param>
/// <param name="Ready">Whether preflight passes and the rollover is not already completed.</param>
internal sealed record RolloverPreviewResponse(
    Guid WorldId,
    Guid SeasonId,
    string SeasonLabel,
    string SeasonStatus,
    string? RolloverPhase,
    string? FailureReason,
    RolloverPreflightResponse Preflight,
    bool NextSeasonExists,
    IReadOnlyList<RolloverCountryResponse> Countries,
    RolloverTotalsResponse Totals,
    bool Ready)
{
    /// <summary>Projects the application preview onto the transport shape.</summary>
    /// <param name="preview">The preview to project.</param>
    public static RolloverPreviewResponse From(SeasonRolloverPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        return new RolloverPreviewResponse(
            preview.WorldId,
            preview.SeasonId,
            preview.SeasonLabel,
            preview.SeasonStatus,
            preview.RolloverPhase,
            preview.FailureReason,
            new RolloverPreflightResponse(
                preview.Preflight.UnpublishedMatchdays,
                preview.Preflight.Reconciles,
                preview.Preflight.Problem,
                preview.Preflight.Divisions
                    .Select(division => new RolloverDivisionResponse(
                        division.DivisionSeasonId,
                        division.TierNumber,
                        division.Outcome,
                        division.StandingsDrifted,
                        division.PlayerStatsDrifted))
                    .ToList()),
            preview.NextSeasonExists,
            preview.Countries
                .Select(country => new RolloverCountryResponse(
                    country.CountryId,
                    country.Code,
                    country.DisplayName,
                    country.Movements
                        .Select(movement => new RolloverMovementResponse(
                            movement.ClubId,
                            movement.FromTier,
                            movement.ToTier,
                            movement.IsPromoted,
                            movement.IsRelegated))
                        .ToList()))
                .ToList(),
            new RolloverTotalsResponse(
                preview.Totals.Countries,
                preview.Totals.Clubs,
                preview.Totals.Promotions,
                preview.Totals.Relegations,
                preview.Totals.PositionAwardsMinor),
            preview.Ready);
    }
}
