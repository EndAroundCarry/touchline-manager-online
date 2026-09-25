using Microsoft.EntityFrameworkCore;
using TouchlineManager.Domain.Auth;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Finance;
using TouchlineManager.Domain.Squad;
using TouchlineManager.Domain.World;
using TouchlineManager.Infrastructure.Persistence.Entities;

namespace TouchlineManager.Infrastructure.Persistence;

/// <summary>
/// The single database context for the modular monolith.
/// </summary>
/// <remarks>
/// Modules are separated by PostgreSQL schema, not by context, so a workflow that spans modules
/// (takeover, auction resolution, matchday publication, rollover) can be one local transaction
/// (ADR-0001). Each module owns its own entity configurations and never writes another module's
/// tables.
/// </remarks>
public sealed class TouchlineManagerDbContext : DbContext
{
    /// <summary>Initializes the context.</summary>
    public TouchlineManagerDbContext(DbContextOptions<TouchlineManagerDbContext> options)
        : base(options)
    {
    }

    /// <summary>Gets the ops module's durable job rows.</summary>
    public DbSet<OpsJob> Jobs => Set<OpsJob>();

    /// <summary>Gets the ops module's append-only audit rows.</summary>
    public DbSet<OpsAuditEntry> AuditEntries => Set<OpsAuditEntry>();

    /// <summary>Gets the auth module's accounts.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>Gets the auth module's refresh sessions.</summary>
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();

    /// <summary>Gets the auth module's single-use email tokens.</summary>
    public DbSet<EmailToken> EmailTokens => Set<EmailToken>();

    /// <summary>Gets the auth module's recorded consent rows.</summary>
    public DbSet<UserConsent> UserConsents => Set<UserConsent>();

    /// <summary>Gets the world module's game world.</summary>
    public DbSet<GameWorld> GameWorlds => Set<GameWorld>();

    /// <summary>Gets the world module's countries.</summary>
    public DbSet<Country> Countries => Set<Country>();

    /// <summary>Gets the world module's manager profiles.</summary>
    public DbSet<Manager> Managers => Set<Manager>();

    /// <summary>Gets the world module's clubs.</summary>
    public DbSet<Club> Clubs => Set<Club>();

    /// <summary>Gets the world module's club tenures, the whole of the ownership model.</summary>
    public DbSet<ClubTenure> ClubTenures => Set<ClubTenure>();

    /// <summary>Gets the world module's division-provisioning requests.</summary>
    public DbSet<DivisionProvisioningRequest> DivisionProvisioningRequests =>
        Set<DivisionProvisioningRequest>();

    /// <summary>Gets the world module's generation-run records.</summary>
    public DbSet<GenerationRun> GenerationRuns => Set<GenerationRun>();

    /// <summary>Gets the competition module's seasons.</summary>
    public DbSet<Season> Seasons => Set<Season>();

    /// <summary>Gets the competition module's divisions.</summary>
    public DbSet<Division> Divisions => Set<Division>();

    /// <summary>Gets the competition module's division-seasons.</summary>
    public DbSet<DivisionSeason> DivisionSeasons => Set<DivisionSeason>();

    /// <summary>Gets the competition module's immutable club season entries.</summary>
    public DbSet<ClubSeasonEntry> ClubSeasonEntries => Set<ClubSeasonEntry>();

    /// <summary>Gets the finance module's club accounts.</summary>
    public DbSet<ClubAccount> ClubAccounts => Set<ClubAccount>();

    /// <summary>Gets the squad module's players.</summary>
    public DbSet<Player> Players => Set<Player>();

    /// <summary>Gets the squad module's player attributes.</summary>
    public DbSet<PlayerAttributes> PlayerAttributes => Set<PlayerAttributes>();

    /// <summary>Gets the squad module's player state rows.</summary>
    public DbSet<PlayerState> PlayerStates => Set<PlayerState>();

    /// <summary>Gets the squad module's player contracts.</summary>
    public DbSet<PlayerContract> PlayerContracts => Set<PlayerContract>();

    /// <summary>Gets the squad module's player registrations.</summary>
    public DbSet<PlayerRegistration> PlayerRegistrations => Set<PlayerRegistration>();

    /// <summary>Gets the squad module's player unavailability records.</summary>
    public DbSet<PlayerUnavailability> PlayerUnavailabilities => Set<PlayerUnavailability>();

    /// <summary>Gets the squad module's tactical plans.</summary>
    public DbSet<TacticalPlan> TacticalPlans => Set<TacticalPlan>();

    /// <summary>Gets the squad module's tactical slots.</summary>
    public DbSet<TacticalSlot> TacticalSlots => Set<TacticalSlot>();

    /// <summary>Gets the squad module's club training plans.</summary>
    public DbSet<TrainingPlan> TrainingPlans => Set<TrainingPlan>();

    /// <summary>Gets the squad module's per-player training focuses.</summary>
    public DbSet<PlayerTrainingFocus> PlayerTrainingFocuses => Set<PlayerTrainingFocus>();

    /// <summary>Gets the squad module's fixture team sheets.</summary>
    public DbSet<FixtureTeamSheet> FixtureTeamSheets => Set<FixtureTeamSheet>();

    /// <summary>Gets the squad module's team-sheet entries.</summary>
    public DbSet<TeamSheetEntry> TeamSheetEntries => Set<TeamSheetEntry>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TouchlineManagerDbContext).Assembly);
    }
}
