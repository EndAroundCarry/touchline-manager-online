using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Auth;
using TouchlineManager.Application.Auth.Validation;
using TouchlineManager.Application.Comms;
using TouchlineManager.Application.Competition;
using TouchlineManager.Application.Finance;
using TouchlineManager.Application.Jobs;
using TouchlineManager.Application.Market;
using TouchlineManager.Application.Market.Validation;
using TouchlineManager.Application.Match;
using TouchlineManager.Application.Squad;
using TouchlineManager.Application.Squad.Validation;
using TouchlineManager.Application.World;
using TouchlineManager.Application.World.Generation;
using TouchlineManager.Application.World.Validation;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Contracts.Competition;
using TouchlineManager.Contracts.Market;
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
        services.AddScoped<IJobHandler, EvaluateAiClubsJobHandler>();
        services.AddScoped<IJobHandler, LockMatchdayJobHandler>();
        services.AddScoped<IJobHandler, ResolveMatchdayJobHandler>();
        services.AddScoped<IJobHandler, PublishMatchdayJobHandler>();
        services.AddScoped<IJobHandler, RebuildDivisionProjectionsJobHandler>();
        services.AddScoped<IJobHandler, WeeklyFinanceRunJobHandler>();
        services.AddScoped<IJobHandler, ResolveAuctionJobHandler>();
        services.AddScoped<IJobHandler, EvaluateAiMarketJobHandler>();
        services.AddScoped<IJobHandler, ProvisionDivisionJobHandler>();
        services.AddScoped<JobHandlerRegistry>();
        services.AddScoped<EnqueueNoOpJob>();

        AddAuthUseCases(services);
        AddWorldUseCases(services);
        AddSquadUseCases(services);
        AddCompetitionUseCases(services);
        AddMatchUseCases(services);
        AddCommsUseCases(services);
        AddFinanceUseCases(services);
        AddMarketUseCases(services);

        return services;
    }

    /// <summary>
    /// Registers the market module's reads and commands (master plan §16 Stage 10; `SCT-*`, `TRF-*`).
    /// </summary>
    /// <remarks>
    /// <see cref="ResolveListing"/> has no public command — the worker drives it through the resolution job
    /// (`TRF-9`), the same way the matchday transitions are driven. <see cref="MarketNotifications"/> is
    /// registered here rather than beside one caller because a bid, a cancellation, and a resolution each
    /// report their own event.
    /// </remarks>
    private static void AddMarketUseCases(IServiceCollection services)
    {
        services.AddScoped<MarketNotifications>();

        // The listing and bid cores are shared by a manager's command and the AI's market evaluation
        // (INS-12, TRF-12), so they are registered once rather than per caller.
        services.AddScoped<IListingWriter, ListingWriter>();
        services.AddScoped<IBidWriter, BidWriter>();

        services.AddScoped<SearchPlayers>();
        services.AddScoped<Shortlists>();
        services.AddScoped<ListListings>();
        services.AddScoped<CreateListing>();
        services.AddScoped<CancelListing>();
        services.AddScoped<PlaceBid>();
        services.AddScoped<ResolveListing>();

        // The AI market evaluation: a worker-only writer of listings and bids via the shared cores above
        // (TRF-12, INS-12).
        services.AddScoped<EvaluateAiMarket>();

        // Reachable only from the non-production diagnostics trigger (§17.12).
        services.AddScoped<TriggerAuctions>();

        services.AddScoped<IValidator<CreateListingRequest>, CreateListingRequestValidator>();
        services.AddScoped<IValidator<PlaceBidRequest>, PlaceBidRequestValidator>();
        services.AddScoped<IValidator<ShortlistRequest>, ShortlistRequestValidator>();
    }

    /// <summary>
    /// Registers the finance module's collaborator and use cases (master plan §16 Stage 9; `FIN-3`…`FIN-9`).
    /// </summary>
    /// <remarks>
    /// <see cref="MatchdayFinances"/> is registered here rather than beside a single caller because it is
    /// the publication's money step: the matchday workflow composes it the way it composes
    /// <c>MatchdayNotifications</c>, and both commit inside the publication's transaction.
    /// </remarks>
    private static void AddFinanceUseCases(IServiceCollection services)
    {
        services.AddScoped<MatchdayFinances>();
        services.AddScoped<RunWeeklyFinance>();
        services.AddScoped<GetFinanceSummary>();
        services.AddScoped<GetFinanceLedger>();
    }

    /// <summary>
    /// Registers the comms module's inbox reads and commands, and the composer the matchday workflows use
    /// (master plan §10.7, F-41).
    /// </summary>
    /// <remarks>
    /// <see cref="MatchdayNotifications"/> is registered here rather than beside one caller because two
    /// workflows compose messages from their own facts: the lock reports a repaired side (`DIS-7`) and the
    /// publication reports results, cards, injuries, and table movement.
    /// </remarks>
    private static void AddCommsUseCases(IServiceCollection services)
    {
        services.AddScoped<MatchdayNotifications>();
        services.AddScoped<GetInbox>();
        services.AddScoped<MarkInboxMessagesRead>();
        services.AddScoped<GetSync>();
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

        // One generation path, shared by the seeder and the provisioning worker, so a provisioned tier cannot
        // drift from a seeded one (§16 Stage 11; PYR-14).
        services.AddScoped<WorldGenerator>();

        services.AddScoped<SeedWorld>();
        services.AddScoped<ProvisionDivision>();
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
        services.AddScoped<EvaluateAiClubs>();
        services.AddScoped<GetFixtureTeamSheet>();
        services.AddScoped<SaveFixtureTeamSheet>();
        services.AddScoped<RequestRenewalQuote>();
        services.AddScoped<RenewContract>();

        services.AddScoped<IValidator<SaveTacticalPlanRequest>, SaveTacticalPlanRequestValidator>();
        services.AddScoped<IValidator<SaveTrainingRequest>, SaveTrainingRequestValidator>();
        services.AddScoped<IValidator<SetPlayerTrainingFocusRequest>, SetPlayerTrainingFocusRequestValidator>();
        services.AddScoped<IValidator<SaveFixtureTeamSheetRequest>, SaveFixtureTeamSheetRequestValidator>();
        services.AddScoped<IValidator<RenewalQuoteRequest>, RenewalQuoteRequestValidator>();
        services.AddScoped<IValidator<RenewContractRequest>, RenewContractRequestValidator>();
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
        services.AddScoped<GetDivisionStatistics>();
        services.AddScoped<GetDivisionRules>();
        services.AddScoped<GetDivisionDiscipline>();

        services.AddScoped<MatchSnapshotFactory>();
        services.AddScoped<LockMatchday>();
        services.AddScoped<ResolveMatchday>();
        services.AddScoped<PublishMatchday>();

        // Driven by the worker's repair job; it has no public command (TBL-13, §7.2).
        services.AddScoped<RebuildDivisionProjections>();

        // Reachable only from the non-production diagnostics trigger (§17.12).
        services.AddScoped<TriggerMatchday>();
    }
}
