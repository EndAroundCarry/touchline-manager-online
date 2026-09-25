using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Finance;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Infrastructure.Email;
using TouchlineManager.Infrastructure.Jobs;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.Infrastructure.Persistence.Repositories;
using TouchlineManager.Infrastructure.Requests;
using TouchlineManager.Infrastructure.Security;
using TouchlineManager.Infrastructure.Time;

namespace TouchlineManager.Infrastructure;

/// <summary>
/// Composition for the infrastructure layer: persistence, jobs, time, security, email, and external
/// providers.
/// </summary>
public static class DependencyInjection
{
    /// <summary>Registers persistence, the clock, the durable job queue, and the auth providers.</summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException(
                "Connection string 'Database' is not configured. Set ConnectionStrings__Database.");

        services.AddDbContext<TouchlineManagerDbContext>(options => options.UseNpgsql(connectionString));

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IJobQueue, PostgresJobQueue>();
        services.Configure<JobQueueOptions>(configuration.GetSection(JobQueueOptions.SectionName));

        AddAuthInfrastructure(services, configuration);
        AddWorldInfrastructure(services, configuration);
        AddSquadInfrastructure(services);

        return services;
    }

    /// <summary>
    /// Registers the world module's persistence and the advisory locks its deadlines and claims depend on.
    /// </summary>
    /// <remarks>
    /// <see cref="WorldOptions"/> is validated at startup like the auth options, so an operator who has not
    /// configured a world name or a first-season date learns that from a failed start rather than from an
    /// odd-looking world.
    /// </remarks>
    private static void AddWorldInfrastructure(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<WorldOptions>()
            .Bind(configuration.GetSection(WorldOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Name),
                "World:Name must be set.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.GenerationSeed)
                    && options.GenerationSeed.Length <= 64,
                "World:GenerationSeed must be set and at most 64 characters (PYR-14).")
            .Validate(
                options => options.ProvisioningPollSeconds is >= 5 and <= 3600,
                "World:ProvisioningPollSeconds must be between 5 and 3600.")
            .ValidateOnStart();

        services.AddScoped<IWorldRepository, WorldRepository>();
        services.AddScoped<IClubRepository, ClubRepository>();
        services.AddScoped<IManagerRepository, ManagerRepository>();
        services.AddScoped<IClubTenureRepository, ClubTenureRepository>();
        services.AddScoped<IDivisionProvisioningRequestRepository, DivisionProvisioningRequestRepository>();
        services.AddScoped<IGenerationRunRepository, GenerationRunRepository>();
        services.AddScoped<IClubAccountRepository, ClubAccountRepository>();
        services.AddScoped<IOnboardingQueries, OnboardingQueries>();
        services.AddScoped<IAdvisoryLock, PostgresAdvisoryLock>();

        // The default request context, for work with no HTTP request behind it: worker jobs and the
        // operator tools. The API registers its own implementation over this one, so a manager's actions
        // are attributed to their request.
        services.AddScoped<IRequestContext, ServiceRequestContext>();
    }

    /// <summary>
    /// Registers the squad module's persistence.
    /// </summary>
    /// <remarks>
    /// A port of its own rather than another world repository: the squad module owns its tables
    /// (`MOD-1`), and the seeded world stages them through a use case like any other cross-module write
    /// (`MOD-2`). The read port is separate from the write port for the same reason onboarding splits
    /// them — a screen can change without widening what a command can reach (`MOD-3`).
    /// </remarks>
    private static void AddSquadInfrastructure(IServiceCollection services)
    {
        services.AddScoped<ISquadRepository, SquadRepository>();
        services.AddScoped<ISquadQueries, SquadQueries>();
    }

    /// <summary>
    /// Registers the auth module's persistence and providers.
    /// </summary>
    /// <remarks>
    /// The hasher, the token service, and the token issuer are singletons because they hold no
    /// per-request state — the hasher in particular precomputes a throwaway hash once, and rebuilding
    /// it per request would both waste that work and change its timing profile.
    /// </remarks>
    private static void AddAuthInfrastructure(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));

        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<ISecureTokenService, SecureTokenService>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshSessionRepository, RefreshSessionRepository>();
        services.AddScoped<IEmailTokenRepository, EmailTokenRepository>();
        services.AddScoped<IAuditWriter, EfAuditWriter>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
    }

    /// <summary>
    /// Registers the job queue polling service.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="AddInfrastructure"/>: registering it there would make
    /// the API process execute deadlines, and the API must never advance a matchday (ADR-0001,
    /// ADR-0008). Only the worker composition root calls this.
    /// </remarks>
    public static IServiceCollection AddJobQueueWorker(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHostedService<JobQueueWorker>();

        return services;
    }
}
