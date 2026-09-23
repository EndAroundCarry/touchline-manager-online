using Microsoft.EntityFrameworkCore;
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

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TouchlineManagerDbContext).Assembly);
    }
}
