using FootballManager.Application.Time;
using FootballManager.Infrastructure.Ops;
using Microsoft.EntityFrameworkCore;

namespace FootballManager.Infrastructure.Persistence;

public sealed class GameDbContext : DbContext
{
    private readonly IClock _clock;

    public GameDbContext(DbContextOptions<GameDbContext> options, IClock clock)
        : base(options)
    {
        _clock = clock;
    }

    public DbSet<JobRecord> Jobs => Set<JobRecord>();

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyJobBookkeeping();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyJobBookkeeping()
    {
        var now = _clock.UtcNow;
        foreach (var entry in ChangeTracker.Entries<JobRecord>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
                if (entry.Entity.Version == 0)
                {
                    entry.Entity.Version = 1;
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.Version = entry.Property(job => job.Version).OriginalValue + 1;
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var job = modelBuilder.Entity<JobRecord>();
        job.ToTable("jobs", "ops");
        job.HasKey(j => j.Id);
        job.Property(j => j.Id).HasColumnName("id").ValueGeneratedNever();
        job.Property(j => j.JobType).HasColumnName("job_type").HasMaxLength(100).IsRequired();
        job.Property(j => j.BusinessKey).HasColumnName("business_key").HasMaxLength(200).IsRequired();
        job.Property(j => j.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb");
        job.Property(j => j.DueAt).HasColumnName("due_at").IsRequired();
        job.Property(j => j.Priority).HasColumnName("priority");
        job.Property(j => j.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        job.Property(j => j.AttemptCount).HasColumnName("attempt_count");
        job.Property(j => j.MaxAttempts).HasColumnName("max_attempts");
        job.Property(j => j.LeaseOwner).HasColumnName("lease_owner");
        job.Property(j => j.LeaseUntil).HasColumnName("lease_until");
        job.Property(j => j.LastError).HasColumnName("last_error").HasMaxLength(2000);
        job.Property(j => j.CreatedAt).HasColumnName("created_at");
        job.Property(j => j.UpdatedAt).HasColumnName("updated_at");
        job.Property(j => j.Version).HasColumnName("version").IsConcurrencyToken().HasDefaultValue(1L);
        job.HasIndex(j => new { j.JobType, j.BusinessKey }).IsUnique().HasDatabaseName("ux_jobs_job_type_business_key");
        job.HasIndex(j => new { j.Status, j.DueAt, j.Priority }).HasDatabaseName("ix_jobs_status_due_at_priority");

        base.OnModelCreating(modelBuilder);
    }
}
