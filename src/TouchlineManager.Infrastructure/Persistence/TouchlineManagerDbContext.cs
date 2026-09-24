using Microsoft.EntityFrameworkCore;
using TouchlineManager.Domain.Auth;
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

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TouchlineManagerDbContext).Assembly);
    }
}
