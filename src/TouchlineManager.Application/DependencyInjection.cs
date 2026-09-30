using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.Comms;
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
using TouchlineManager.Application.Ops;
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
        services.AddScoped<IJobHandler, EvaluateInactivityJobHandler>();
        services.AddScoped<IJobHandler, SendDeadlineRemindersJobHandler>();
        services.AddScoped<IJobHandler, DispatchOutboxJobHandler>();
        services.AddScoped<IJobHandler, RunSeasonRolloverJobHandler>();
        services.AddScoped<IJobHandler, AdvanceGameClockJobHandler>();
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

        // Operator account administration (master plan §13, F-46, ADR-0042).
        services.AddScoped<SuspendAccount>();
        services.AddScoped<RestoreAccount>();

        // Operator recovery commands (master plan §10.8, F-46, ADR-0044).
        services.AddScoped<RetryJob>();
        services.AddScoped<CancelJob>();

        // Operator feature flags (master plan §10.8, §13, F-46, ADR-0045).
        services.AddScoped<SetFeatureFlag>();

        // The non-production stepped clock's advance (ADR-0049, §17.12). Reachable only from a
        // development-flagged endpoint; the worker's advance job does the actual step.
        services.AddScoped<AdvanceGameClock>();
        services.AddScoped<GetGameClockStatus>();

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
        services.AddScoped<SettleSeasonFinances>();
        services.AddScoped<GetFinanceSummary>();
        services.AddScoped<GetFinanceLedger>();

        // Operator finance repair (master plan §10.8, FIN-12, F-46, ADR-0045).
        services.AddScoped<PostCompensatingEntry>();
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

        // The news feed (COM-1): the writer the provisioning, market, and matchday workflows compose, and the
        // read the client's screen uses.
        services.AddScoped<PostNews>();
        services.AddScoped<GetNews>();

        // The operator's announcement, published as a scoped news item (master plan §10.8, F-46, ADR-0045).
        services.AddScoped<PublishAnnouncement>();

        // Notification preferences (COM-4) and the deadline reminder (COM-3).
        services.AddScoped<GetNotificationPreferences>();
        services.AddScoped<UpdateNotificationPreferences>();
        services.AddScoped<SendDeadlineReminders>();

        // The outbox (MOD-4): the writer that stages an intention, and the dispatcher the worker drives.
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddScoped<DispatchOutbox>();
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
        services.AddScoped<ListSessions>();
        services.AddScoped<RevokeSession>();
        services.AddScoped<ExportAccountData>();

        // Role administration: how an operator, support, or admin account comes to exist (F-46, ADR-0042).
        services.AddScoped<GrantRole>();
        services.AddScoped<RevokeRole>();

        // Multi-factor authentication: enrolment, the login challenge, and its recovery paths (ADR-0042).
        services.AddScoped<MfaAuthenticator>();
        services.AddScoped<MfaRecoveryCodeIssuer>();
        services.AddScoped<EnrolMfa>();
        services.AddScoped<ConfirmMfaEnrolment>();
        services.AddScoped<DisableMfa>();
        services.AddScoped<RegenerateRecoveryCodes>();
        services.AddScoped<CompleteMfaLogin>();

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
        services.AddScoped<IValidator<MfaConfirmRequest>, MfaConfirmRequestValidator>();
        services.AddScoped<IValidator<MfaLoginRequest>, MfaLoginRequestValidator>();
        services.AddScoped<IValidator<MfaDisableRequest>, MfaDisableRequestValidator>();
        services.AddScoped<IValidator<RecoveryCodesRequest>, RecoveryCodesRequestValidator>();
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

        // The schedule and opening-table generation, shared by the seeder, the provisioning worker, and the
        // season rollover, so a new season's fixture list cannot drift from the first one (CAL-8, CAL-9).
        services.AddScoped<DivisionScheduleGenerator>();

        services.AddScoped<SeedWorld>();
        services.AddScoped<ProvisionDivision>();
        services.AddScoped<EvaluateInactivity>();
        services.AddScoped<CreateManagerProfile>();
        services.AddScoped<UpdateManagerProfile>();
        services.AddScoped<ClaimClub>();
        services.AddScoped<ResignClub>();

        // Operator ownership repair (master plan §10.8, OCC-6, F-46, ADR-0045).
        services.AddScoped<AssignClubToAi>();
        services.AddScoped<GetWorld>();
        services.AddScoped<ListCountries>();
        services.AddScoped<GetCountryCapacity>();
        services.AddScoped<GetAvailableClubs>();
        services.AddScoped<GetClubDashboard>();
        services.AddScoped<GetOnboardingState>();

        // Reachable only from the non-production diagnostics triggers (§17.12).
        services.AddScoped<TriggerProvisioning>();
        services.AddScoped<TriggerInactivity>();

        services.AddScoped<IValidator<CreateManagerProfileRequest>, CreateManagerProfileRequestValidator>();
        services.AddScoped<IValidator<UpdateManagerProfileRequest>, UpdateManagerProfileRequestValidator>();
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
        services.AddScoped<SettleSquadContinuity>();
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
        services.AddScoped<GetClubSeasonHistory>();

        services.AddScoped<MatchSnapshotFactory>();
        services.AddScoped<LockMatchday>();
        services.AddScoped<ResolveMatchday>();
        services.AddScoped<PublishMatchday>();

        // The operator's resume of a stuck round (master plan §10.8, F-46, ADR-0044).
        services.AddScoped<ResumeMatchday>();

        // Driven by the worker's repair job; it has no public command (TBL-13, §7.2).
        services.AddScoped<RebuildDivisionProjections>();

        // Driven by the worker's rollover job; it has no public command (PR-4, §7.2, ADR-0031).
        services.AddScoped<RunSeasonRollover>();

        // The closing season's shape, shared by the rollover machine and its operator preview so a dry run and
        // the run it previews read the same plan (ADR-0031, ADR-0034).
        services.AddScoped<SeasonRolloverPlanLoader>();

        // Reachable only from the non-production diagnostics controls (§17.12, ADR-0034): a read-only preview,
        // a run-now enqueue of the real job, and an audited resume of a failed rollover.
        services.AddScoped<PreviewSeasonRollover>();
        services.AddScoped<TriggerSeasonRollover>();
        services.AddScoped<ResumeSeasonRollover>();

        // Reachable only from the non-production diagnostics trigger (§17.12).
        services.AddScoped<TriggerMatchday>();
    }
}
