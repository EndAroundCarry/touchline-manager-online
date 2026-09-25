using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Auth;
using TouchlineManager.Application.Auth.Validation;
using TouchlineManager.Application.Jobs;
using TouchlineManager.Application.Squad;
using TouchlineManager.Application.World;
using TouchlineManager.Application.World.Validation;
using TouchlineManager.Contracts.Auth;
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
        services.AddScoped<JobHandlerRegistry>();
        services.AddScoped<EnqueueNoOpJob>();

        AddAuthUseCases(services);
        AddWorldUseCases(services);
        AddSquadUseCases(services);

        return services;
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
    /// Registers the squad module's read use cases.
    /// </summary>
    /// <remarks>
    /// <see cref="ResolveOwnedClub"/> is registered beside them rather than in the world module even though
    /// it reads world tables: it exists so the three squad reads decide ownership once and refuse with the
    /// same codes, and it has no caller outside them.
    /// </remarks>
    private static void AddSquadUseCases(IServiceCollection services)
    {
        services.AddScoped<ResolveOwnedClub>();
        services.AddScoped<GetSquad>();
        services.AddScoped<GetPlayer>();
        services.AddScoped<ListContracts>();
    }
}
