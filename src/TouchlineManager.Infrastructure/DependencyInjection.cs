using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Comms;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Abstractions.Finance;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Abstractions.Market;
using TouchlineManager.Application.Abstractions.Match;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Infrastructure.Comms;
using TouchlineManager.Infrastructure.Competition;
using TouchlineManager.Infrastructure.Email;
using TouchlineManager.Infrastructure.Finance;
using TouchlineManager.Infrastructure.Jobs;
using TouchlineManager.Infrastructure.Market;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.Infrastructure.Persistence.Repositories;
using TouchlineManager.Infrastructure.Requests;
using TouchlineManager.Infrastructure.Security;
using TouchlineManager.Infrastructure.Telemetry;
using TouchlineManager.Infrastructure.Time;
using TouchlineManager.Infrastructure.Training;
using TouchlineManager.Infrastructure.World;

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
        AddCompetitionInfrastructure(services, configuration);
        AddMatchInfrastructure(services);
        AddCommsInfrastructure(services, configuration);
        AddSquadInfrastructure(services);
        AddTrainingInfrastructure(services, configuration);
        AddAiClubInfrastructure(services, configuration);
        AddMatchdayInfrastructure(services, configuration);
        AddFinanceInfrastructure(services, configuration);
        AddMarketInfrastructure(services, configuration);
        AddOpsInfrastructure(services);

        return services;
    }

    /// <summary>
    /// Registers the ops module's read-only analytics projection and the operational funnel counters
    /// (master plan §16 Stage 13, `F-54`, ADR-0041).
    /// </summary>
    /// <remarks>
    /// The query is scoped because it reads through the per-request unit of work; the counters are a
    /// singleton because the meter holds no per-request state and every caller shares the one instrument.
    /// </remarks>
    private static void AddOpsInfrastructure(IServiceCollection services)
    {
        services.AddScoped<IOperationalAnalyticsQueries, OperationalAnalyticsQueries>();
        services.AddSingleton<IOperationalMetrics, OperationalMetrics>();

        // The operator's game-health read (master plan §13, F-46, ADR-0042).
        services.AddScoped<IAdminQueries, AdminQueries>();

        // The operator's feature-flag store (master plan §6.9, §13, F-46, ADR-0045).
        services.AddScoped<IFeatureFlagStore, FeatureFlagStore>();
    }

    /// <summary>
    /// Registers the market module's persistence and the transfer-auction resolver's configuration
    /// (master plan §6.7; `TRF-1`…`TRF-15`).
    /// </summary>
    /// <remarks>
    /// The options are bound here rather than in <see cref="AddJobQueueWorker"/> so a misconfigured interval
    /// fails at startup in every host, and the scheduler is registered there rather than here for the same
    /// reason the queue poller is: the API must never settle a transfer (ADR-0001, ADR-0008).
    /// </remarks>
    private static void AddMarketInfrastructure(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IShortlistRepository, ShortlistRepository>();
        services.AddScoped<IListingRepository, ListingRepository>();
        services.AddScoped<IBidRepository, BidRepository>();
        services.AddScoped<ITransferOutcomeRepository, TransferOutcomeRepository>();
        services.AddScoped<IMarketQueries, MarketQueries>();
        services.AddScoped<IRosterQueries, RosterQueries>();
        services.AddScoped<IAiMarketRepository, AiMarketRepository>();
        services.AddScoped<IAiMarketDecisionRepository, AiMarketDecisionRepository>();

        services
            .AddOptions<MarketOptions>()
            .Bind(configuration.GetSection(MarketOptions.SectionName))
            .Validate(
                options => options.CheckIntervalSeconds is >= 30 and <= 86_400,
                "Auctions:CheckIntervalSeconds must be between 30 and 86400.");

        services
            .AddOptions<AiMarketOptions>()
            .Bind(configuration.GetSection(AiMarketOptions.SectionName))
            .Validate(
                options => options.CheckIntervalSeconds is >= 30 and <= 86_400,
                "AiMarket:CheckIntervalSeconds must be between 30 and 86400.");
    }

    /// <summary>
    /// Registers the weekly finance settlement's configuration (`CON-2`, `FIN-4`, `FIN-7`, `FIN-9`).
    /// </summary>
    /// <remarks>
    /// The options are bound here rather than in <see cref="AddJobQueueWorker"/> so the validator that guards
    /// the interval runs in every host, and a misconfiguration fails at startup rather than the first time the
    /// scheduler ticks.
    /// </remarks>
    private static void AddFinanceInfrastructure(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<FinanceOptions>()
            .Bind(configuration.GetSection(FinanceOptions.SectionName))
            .Validate(
                options => options.CheckIntervalSeconds is >= 30 and <= 86_400,
                "Finance:CheckIntervalSeconds must be between 30 and 86400.");
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
    /// Registers the comms module's persistence (master plan §6.9).
    /// </summary>
    /// <remarks>
    /// A module of its own with ports of its own (`MOD-1`). The write port both stages a message and reads
    /// the club targets a message is addressed to, because the two are one concern; the read port serves the
    /// inbox, separate so a screen can change without widening what a command can reach (`MOD-3`).
    /// </remarks>
    private static void AddCommsInfrastructure(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IInboxRepository, InboxRepository>();
        services.AddScoped<IInboxQueries, InboxQueries>();
        services.AddScoped<INewsRepository, NewsRepository>();
        services.AddScoped<INotificationPreferencesRepository, NotificationPreferencesRepository>();
        services.AddScoped<IOutboxRepository, OutboxRepository>();

        // Bound here rather than in AddJobQueueWorker so a misconfigured interval fails at startup in every
        // host, and the schedulers that read them are registered there for the same reason the queue poller
        // is: the API must never send mail, age a tenure, or grow the pyramid (ADR-0001, ADR-0008).
        services
            .AddOptions<ReminderOptions>()
            .Bind(configuration.GetSection(ReminderOptions.SectionName))
            .Validate(
                options => options.CheckIntervalSeconds is >= 30 and <= 86_400,
                "Reminders:CheckIntervalSeconds must be between 30 and 86400.");

        services
            .AddOptions<OutboxOptions>()
            .Bind(configuration.GetSection(OutboxOptions.SectionName))
            .Validate(
                options => options.CheckIntervalSeconds is >= 30 and <= 86_400,
                "Outbox:CheckIntervalSeconds must be between 30 and 86400.")
            .Validate(
                options => options.BatchSize is >= 1 and <= 500,
                "Outbox:BatchSize must be between 1 and 500.");
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
    /// so a screen can change without widening what a command can reach (`MOD-3`). The rollover's options are
    /// bound here, and the scheduler that reads them is registered in <see cref="AddJobQueueWorker"/> so the
    /// API never closes a season (ADR-0001, ADR-0008).
    /// </remarks>
    private static void AddCompetitionInfrastructure(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICompetitionRepository, CompetitionRepository>();
        services.AddScoped<ICompetitionQueries, CompetitionQueries>();
        services.AddScoped<ISeasonRolloverRepository, SeasonRolloverRepository>();

        services
            .AddOptions<SeasonRolloverOptions>()
            .Bind(configuration.GetSection(SeasonRolloverOptions.SectionName))
            .Validate(
                options => options.CheckIntervalSeconds is >= 30 and <= 86_400,
                "Rollover:CheckIntervalSeconds must be between 30 and 86400.");
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
    /// Registers the AI club evaluation's configuration (`INS-12`).
    /// </summary>
    /// <remarks>
    /// Bound here rather than in <see cref="AddJobQueueWorker"/> so the validator that guards the interval
    /// runs in every host, and a misconfiguration fails at startup rather than the first time the scheduler
    /// ticks.
    /// </remarks>
    private static void AddAiClubInfrastructure(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<AiClubOptions>()
            .Bind(configuration.GetSection(AiClubOptions.SectionName))
            .Validate(
                options => options.CheckIntervalSeconds is >= 30 and <= 86_400,
                "AiClubs:CheckIntervalSeconds must be between 30 and 86400.");
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

        services
            .AddOptions<ProvisioningOptions>()
            .Bind(configuration.GetSection(ProvisioningOptions.SectionName))
            .Validate(
                options => options.CheckIntervalSeconds is >= 30 and <= 86_400,
                "Provisioning:CheckIntervalSeconds must be between 30 and 86400.");

        services
            .AddOptions<InactivityOptions>()
            .Bind(configuration.GetSection(InactivityOptions.SectionName))
            .Validate(
                options => options.CheckIntervalSeconds is >= 30 and <= 86_400,
                "Inactivity:CheckIntervalSeconds must be between 30 and 86400.");

        services.AddScoped<IWorldRepository, WorldRepository>();
        services.AddScoped<IClubRepository, ClubRepository>();
        services.AddScoped<IManagerRepository, ManagerRepository>();
        services.AddScoped<IClubTenureRepository, ClubTenureRepository>();
        services.AddScoped<IDivisionProvisioningRequestRepository, DivisionProvisioningRequestRepository>();
        services.AddScoped<IGenerationRunRepository, GenerationRunRepository>();
        services.AddScoped<IClubAccountRepository, ClubAccountRepository>();
        services.AddScoped<ILedgerRepository, LedgerRepository>();
        services.AddScoped<IFinanceQueries, FinanceQueries>();
        services.AddScoped<IClubSeasonFinanceRepository, ClubSeasonFinanceRepository>();
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
        services.AddScoped<IContractContinuityQueries, ContractContinuityQueries>();
        services.AddScoped<ITacticsRepository, TacticsRepository>();
        services.AddScoped<ITacticsQueries, TacticsQueries>();
        services.AddScoped<ITrainingRepository, TrainingRepository>();
        services.AddScoped<ITrainingQueries, TrainingQueries>();
        services.AddScoped<ITeamSheetRepository, TeamSheetRepository>();
        services.AddScoped<ITeamSheetQueries, TeamSheetQueries>();
        services.AddScoped<IAvailabilityRepository, AvailabilityRepository>();
        services.AddScoped<IPlayerStateRepository, PlayerStateRepository>();
        services.AddScoped<IAiClubRepository, AiClubRepository>();
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
        services.AddSingleton<IMfaSecretProtector, MfaSecretProtector>();
        services.AddSingleton<IMfaChallengeIssuer, MfaChallengeIssuer>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshSessionRepository, RefreshSessionRepository>();
        services.AddScoped<IEmailTokenRepository, EmailTokenRepository>();
        services.AddScoped<IMfaCredentialRepository, MfaCredentialRepository>();
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

        // And the same for the AI: this service places the day's evaluation row, and the row is what gives
        // every club nobody holds a side and a training plan (INS-12).
        services.AddHostedService<AiClubScheduler>();

        // And the same for money: this service places the week's settlement row, and the row is what charges
        // every club its wages after the Sunday matchday (CON-2, FIN-7).
        services.AddHostedService<WeeklyFinanceScheduler>();

        // And the same for the market: this service places a resolution row for every due listing, and the row
        // is what settles the winning bid (TRF-2, TRF-9).
        services.AddHostedService<AuctionScheduler>();

        // And the same for the AI's own trading: this service places the day's evaluation row, and the row is
        // what lists a surplus player and bids for a better one through the same writers a manager's command
        // uses (TRF-12, ADR-0025).
        services.AddHostedService<AiMarketScheduler>();

        // And the same for the pyramid: this service places a provisioning row for every pending tier request,
        // and the row is what generates, backfills, and activates the next tier (PYR-4).
        services.AddHostedService<ProvisioningScheduler>();

        // And the same for tenure activity: this service places one ladder row a day, and the row is what
        // warns, hands routine decisions to the AI, and finally frees a club (OCC-1..OCC-3).
        services.AddHostedService<InactivityScheduler>();

        // And the same for the calendar's notifications: this service places a reminder row for every round
        // about to lock, and the row is what tells the managers holding its clubs (COM-3).
        services.AddHostedService<ReminderScheduler>();

        // And the same for the outbox: this service places a dispatch row every minute, and the row is what
        // sends the notifications the game has already committed to sending (MOD-4).
        services.AddHostedService<OutboxScheduler>();

        // And the same for the season: this service places a rollover row once the current season's deadline
        // has passed, and the row is what closes the season and opens the next one (PR-4, ADR-0031).
        services.AddHostedService<SeasonRolloverScheduler>();

        return services;
    }
}
