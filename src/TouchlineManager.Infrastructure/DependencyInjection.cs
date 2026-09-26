using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Abstractions.Finance;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Abstractions.Match;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Infrastructure.Competition;
using TouchlineManager.Infrastructure.Email;
using TouchlineManager.Infrastructure.Jobs;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.Infrastructure.Persistence.Repositories;
using TouchlineManager.Infrastructure.Requests;
using TouchlineManager.Infrastructure.Security;
using TouchlineManager.Infrastructure.Time;
using TouchlineManager.Infrastructure.Training;

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

        AddClockInfrastructure(services, configuration);
        AddAuthInfrastructure(services, configuration);
        AddWorldInfrastructure(services, configuration);
        AddCompetitionInfrastructure(services);
        AddMatchInfrastructure(services);
        AddSquadInfrastructure(services);
        AddTrainingInfrastructure(services, configuration);
        AddMatchdayInfrastructure(services, configuration);

        return services;
    }

    /// <summary>
    /// Binds the clock configuration, then replaces the real clock with a compressed one when a
    /// non-production environment has opted in (ADR-0009, `TIME-6`).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called by the composition roots, which are the only places that know the environment. The clock
    /// itself is chosen here rather than in each root so the rule lives in one place: real time by
    /// default, compressed only where the configuration asks for it and the environment allows it.
    /// </para>
    /// <para>
    /// A production host that asks for compression does not silently fall back to real time — it fails to
    /// start, by name. A quiet fallback would leave an operator believing a season was accelerated when it
    /// was not, which is exactly the accident this guard exists to prevent (`TIME-6`).
    /// </para>
    /// </remarks>
    /// <returns>The clock configuration in force, so a root can report a compressed clock in its logs.</returns>
    public static ClockOptions AddGameClock(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var options = configuration.GetSection(ClockOptions.SectionName).Get<ClockOptions>()
            ?? new ClockOptions();

        if (!options.IsCompressed)
        {
            return options;
        }

        if (environment.IsProduction())
        {
            throw new InvalidOperationException(
                "A compressed clock is never permitted in Production (ADR-0009, TIME-6). "
                + "Remove Clock:Mode=Compressed, or run this configuration in a non-production environment.");
        }

        // Last registration wins, so this replaces the SystemClock AddInfrastructure registered. The clock
        // reads the validated options, so an unsound rate or a missing anchor fails when it is resolved.
        services.AddSingleton<IClock>(provider =>
            new CompressedClock(provider.GetRequiredService<IOptions<ClockOptions>>().Value));

        return options;
    }

    /// <summary>
    /// Registers and validates the clock configuration in every host, before anything chooses a clock.
    /// </summary>
    /// <remarks>
    /// The anchor and rate are only meaningful when compressed, so the checks are conditional; an operator
    /// who is not compressing is not asked to supply them. Validated at startup like the world and auth
    /// options, so a half-written compressed configuration fails immediately rather than at the first job.
    /// </remarks>
    private static void AddClockInfrastructure(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<ClockOptions>()
            .Bind(configuration.GetSection(ClockOptions.SectionName))
            .Validate(
                options => !options.IsCompressed || options.RealAnchorUtc is not null,
                "Clock:RealAnchorUtc must be set when Clock:Mode is Compressed (TIME-6).")
            .Validate(
                options => !options.IsCompressed || options.Rate is >= 2 and <= 100_000,
                "Clock:Rate must be between 2 and 100000 when Clock:Mode is Compressed (TIME-6).")
            .ValidateOnStart();
    }

    /// <summary>
    /// Registers the match module's persistence (master plan §6.6).
    /// </summary>
    /// <remarks>
    /// A module of its own with ports of its own (`MOD-1`). The write-and-lookup port serves the matchday
    /// workflow; the read port serves the match center, and keeping them apart means the viewer cannot reach
    /// a command's surface (`MOD-3`).
    /// </remarks>
    private static void AddMatchInfrastructure(IServiceCollection services)
    {
        services.AddScoped<IMatchRepository, MatchRepository>();
        services.AddScoped<IMatchQueries, MatchQueries>();
    }

    /// <summary>
    /// Registers the matchday workflow's persistence and the scheduler that materialises its deadlines.
    /// </summary>
    /// <remarks>
    /// The options are bound here rather than in <see cref="AddJobQueueWorker"/> so a misconfigured interval
    /// fails at startup in every host, and the scheduler is registered there rather than here for the same
    /// reason the queue poller is: the API must never advance a matchday (ADR-0001, ADR-0008).
    /// </remarks>
    private static void AddMatchdayInfrastructure(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IMatchdayRepository, MatchdayRepository>();

        services
            .AddOptions<MatchdayOptions>()
            .Bind(configuration.GetSection(MatchdayOptions.SectionName))
            .Validate(
                options => options.CheckIntervalSeconds is >= 30 and <= 86_400,
                "Matchday:CheckIntervalSeconds must be between 30 and 86400.")
            .Validate(
                options => options.MaterializeHorizonDays is >= 1 and <= 120,
                "Matchday:MaterializeHorizonDays must be between 1 and 120.");
    }

    /// <summary>
    /// Registers the competition module's persistence.
    /// </summary>
    /// <remarks>
    /// A port of its own because the competition module owns its tables (`MOD-1`); the world seeder stages
    /// the first season's schedule through it as a cross-module write (`MOD-2`). The read port is separate
    /// so a screen can change without widening what a command can reach (`MOD-3`).
    /// </remarks>
    private static void AddCompetitionInfrastructure(IServiceCollection services)
    {
        services.AddScoped<ICompetitionRepository, CompetitionRepository>();
        services.AddScoped<ICompetitionQueries, CompetitionQueries>();
    }

    /// <summary>
    /// Registers the daily training progression's configuration (`TRN-3`).
    /// </summary>
    /// <remarks>
    /// The options are bound here rather than in <see cref="AddJobQueueWorker"/> so the validator that
    /// guards the interval runs in every host, and so a misconfiguration fails at startup rather than the
    /// first time the scheduler ticks.
    /// </remarks>
    private static void AddTrainingInfrastructure(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<TrainingOptions>()
            .Bind(configuration.GetSection(TrainingOptions.SectionName))
            .Validate(
                options => options.CheckIntervalSeconds is >= 30 and <= 86_400,
                "Training:CheckIntervalSeconds must be between 30 and 86400.");
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
        services.AddScoped<ITacticsRepository, TacticsRepository>();
        services.AddScoped<ITacticsQueries, TacticsQueries>();
        services.AddScoped<ITrainingRepository, TrainingRepository>();
        services.AddScoped<ITrainingQueries, TrainingQueries>();
        services.AddScoped<ITeamSheetRepository, TeamSheetRepository>();
        services.AddScoped<ITeamSheetQueries, TeamSheetQueries>();
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

        // The materializer that makes the daily progression row exist, and the row that becomes the
        // deadline (`TRN-3`). Worker-only for the same reason the poller is: the API must never advance
        // a player's training.
        services.AddHostedService<DailyProgressionScheduler>();

        // The same arrangement for the season: this service turns the calendar into lock, resolution, and
        // publication jobs, and the rows it inserts are the deadlines (§7.2, ADR-0003).
        services.AddHostedService<MatchdayScheduleScheduler>();

        return services;
    }
}
