using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Auth;
using TouchlineManager.Application.Auth.Validation;
using TouchlineManager.Application.Competition;
using TouchlineManager.Application.Jobs;
using TouchlineManager.Application.Match;
using TouchlineManager.Application.Squad;
using TouchlineManager.Application.Squad.Validation;
using TouchlineManager.Application.World;
using TouchlineManager.Application.World.Validation;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Contracts.Competition;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Contracts.World;

namespace TouchlineManager.Application;

/// <summary>
/// Composition for the application layer: use cases, handlers, and policies.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers application use cases, job handlers, the handler registry, and request validators.
    /// </summary>
    /// <remarks>
    /// Everything here is scoped, because use cases and job handlers work through the per-request or
    /// per-job unit of work. Registering them as singletons would capture a scoped
    /// <c>DbContext</c> and fail validation at startup.
    /// </remarks>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IJobHandler, NoOpJobHandler>();
        services.AddScoped<IJobHandler, DailyPlayerProgressionJobHandler>();
        services.AddScoped<IJobHandler, LockMatchdayJobHandler>();
        services.AddScoped<IJobHandler, ResolveMatchdayJobHandler>();
        services.AddScoped<IJobHandler, PublishMatchdayJobHandler>();
        services.AddScoped<JobHandlerRegistry>();
        services.AddScoped<EnqueueNoOpJob>();

        AddAuthUseCases(services);
        AddWorldUseCases(services);
        AddSquadUseCases(services);
        AddCompetitionUseCases(services);
        AddMatchUseCases(services);

        return services;
    }

    /// <summary>
    /// Registers the match center's reads (master plan §9.5, §10.5).
    /// </summary>
    /// <remarks>
    /// Reads only, and both public game data. Nothing here produces or changes a result: the matchday worker
    /// owns that (`MAT-2`), and the replay is re-derived from the frozen snapshot the worker already wrote
    /// rather than stored a second time (`MAT-8`, `MAT-9`).
    /// </remarks>
    private static void AddMatchUseCases(IServiceCollection services)
    {
        services.AddScoped<GetMatch>();
        services.AddScoped<GetMatchPresentation>();
    }

    private static void AddAuthUseCases(IServiceCollection services)
    {
        services.AddScoped<EmailTokenIssuer>();
        services.AddScoped<SessionIssuer>();

        services.AddScoped<RegisterUser>();
        services.AddScoped<VerifyEmail>();
        services.AddScoped<ResendVerificationEmail>();
        services.AddScoped<Login>();
        services.AddScoped<RefreshAccessToken>();
        services.AddScoped<Logout>();
        services.AddScoped<LogoutAll>();
        services.AddScoped<ForgotPassword>();
        services.AddScoped<ResetPassword>();
        services.AddScoped<GetProfile>();
        services.AddScoped<UpdateProfile>();
        services.AddScoped<DeleteAccount>();

        // Validators are registered explicitly rather than by assembly scanning, so that adding a
        // validator to the assembly cannot silently change which requests are validated.
        services.AddScoped<IValidator<RegisterRequest>, RegisterRequestValidator>();
        services.AddScoped<IValidator<LoginRequest>, LoginRequestValidator>();
        services.AddScoped<IValidator<VerifyEmailRequest>, VerifyEmailRequestValidator>();
        services.AddScoped<IValidator<ResendVerificationRequest>, ResendVerificationRequestValidator>();
        services.AddScoped<IValidator<ForgotPasswordRequest>, ForgotPasswordRequestValidator>();
        services.AddScoped<IValidator<ResetPasswordRequest>, ResetPasswordRequestValidator>();
        services.AddScoped<IValidator<UpdateProfileRequest>, UpdateProfileRequestValidator>();
        services.AddScoped<IValidator<DeleteAccountRequest>, DeleteAccountRequestValidator>();
    }

    /// <summary>
    /// Registers the world and onboarding use cases.
    /// </summary>
    /// <remarks>
    /// <see cref="CapacityEvaluator"/> is registered here rather than beside the seed use case because it
    /// is shared: the takeover and resignation commands both run it, and Stage 11's provisioning worker
    /// will be its third caller.
    /// </remarks>
    private static void AddWorldUseCases(IServiceCollection services)
    {
        services.AddScoped<CapacityEvaluator>();

        services.AddScoped<SeedWorld>();
        services.AddScoped<CreateManagerProfile>();
        services.AddScoped<ClaimClub>();
        services.AddScoped<ResignClub>();
        services.AddScoped<GetWorld>();
        services.AddScoped<ListCountries>();
        services.AddScoped<GetCountryCapacity>();
        services.AddScoped<GetAvailableClubs>();
        services.AddScoped<GetClubDashboard>();
        services.AddScoped<GetOnboardingState>();

        services.AddScoped<IValidator<CreateManagerProfileRequest>, CreateManagerProfileRequestValidator>();
        services.AddScoped<IValidator<ClaimClubRequest>, ClaimClubRequestValidator>();
    }

    /// <summary>
    /// Registers the squad module's read and tactics use cases.
    /// </summary>
    /// <remarks>
    /// <see cref="ResolveOwnedClub"/> is registered beside them rather than in the world module even though
    /// it reads world tables: it exists so the squad and tactics commands decide ownership once and refuse
    /// with the same codes, and it has no caller outside them.
    /// </remarks>
    private static void AddSquadUseCases(IServiceCollection services)
    {
        services.AddScoped<ResolveOwnedClub>();
        services.AddScoped<GetSquad>();
        services.AddScoped<GetPlayer>();
        services.AddScoped<ListContracts>();
        services.AddScoped<GetTactics>();
        services.AddScoped<SaveTacticalPlan>();
        services.AddScoped<MakeTacticalPlanDefault>();
        services.AddScoped<GetTraining>();
        services.AddScoped<SaveTrainingPlan>();
        services.AddScoped<SetPlayerTrainingFocus>();
        services.AddScoped<RunDailyProgression>();
        services.AddScoped<GetFixtureTeamSheet>();
        services.AddScoped<SaveFixtureTeamSheet>();

        services.AddScoped<IValidator<SaveTacticalPlanRequest>, SaveTacticalPlanRequestValidator>();
        services.AddScoped<IValidator<SaveTrainingRequest>, SaveTrainingRequestValidator>();
        services.AddScoped<IValidator<SetPlayerTrainingFocusRequest>, SetPlayerTrainingFocusRequestValidator>();
        services.AddScoped<IValidator<SaveFixtureTeamSheetRequest>, SaveFixtureTeamSheetRequestValidator>();
    }

    /// <summary>
    /// Registers the competition module's reads and its matchday workflow (master plan §10.5).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The transitions that change a fixture — lock, stage, publish — have no use case an HTTP command can
    /// reach: they are driven by the matchday worker through its own three use cases, which is what makes
    /// "there is no public command that simulates or influences a match" true by construction (`MAT-2`).
    /// </para>
    /// <para>
    /// <see cref="MatchSnapshotFactory"/> is registered here rather than beside a single caller because two
    /// workflows freeze snapshots: the lock job, and a resolution that finds the lock never ran.
    /// </para>
    /// </remarks>
    private static void AddCompetitionUseCases(IServiceCollection services)
    {
        services.AddScoped<ListDivisionFixtures>();
        services.AddScoped<GetMyFixtures>();
        services.AddScoped<GetFixture>();
        services.AddScoped<GetDivisionTable>();

        services.AddScoped<MatchSnapshotFactory>();
        services.AddScoped<LockMatchday>();
        services.AddScoped<ResolveMatchday>();
        services.AddScoped<PublishMatchday>();
    }
}
