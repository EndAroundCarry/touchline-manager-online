using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TouchlineManager.Infrastructure.Persistence.Entities;

namespace TouchlineManager.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <c>ops.jobs</c>. The indexes here are what make the claim path cheap and what make
/// enqueue idempotent (ADR-0003).
/// </summary>
internal sealed class OpsJobConfiguration : IEntityTypeConfiguration<OpsJob>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OpsJob> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("jobs", "ops", table =>
        {
            table.HasCheckConstraint("ck_jobs_attempt_count", "attempt_count >= 0");
            table.HasCheckConstraint("ck_jobs_max_attempts", "max_attempts >= 1");
            table.HasCheckConstraint("ck_jobs_priority", "priority >= 0");
            table.HasCheckConstraint(
                "ck_jobs_status",
                "status in ('pending', 'leased', 'completed', 'dead_letter')");
            table.HasCheckConstraint(
                "ck_jobs_lease_consistency",
                "(status = 'leased' and lease_owner is not null and lease_until is not null) "
                + "or (status <> 'leased' and lease_owner is null and lease_until is null)");
        });

        builder.HasKey(job => job.Id);
        builder.Property(job => job.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(job => job.JobType).HasColumnName("job_type").HasMaxLength(120).IsRequired();
        builder.Property(job => job.BusinessKey).HasColumnName("business_key").HasMaxLength(200).IsRequired();
        builder.Property(job => job.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(job => job.DueAt).HasColumnName("due_at").IsRequired();
        builder.Property(job => job.Priority).HasColumnName("priority").IsRequired();
        builder.Property(job => job.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(job => job.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(job => job.MaxAttempts).HasColumnName("max_attempts").IsRequired();
        builder.Property(job => job.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(200);
        builder.Property(job => job.LeaseUntil).HasColumnName("lease_until");
        builder.Property(job => job.LastError).HasColumnName("last_error").HasMaxLength(2000);
        builder.Property(job => job.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(job => job.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(job => job.CompletedAt).HasColumnName("completed_at");
        builder.Property(job => job.Version).HasColumnName("version").IsRequired();

        builder
            .HasIndex(job => new { job.JobType, job.BusinessKey })
            .IsUnique()
            .HasDatabaseName("ux_jobs_job_type_business_key");

        builder
            .HasIndex(job => new { job.Status, job.DueAt, job.Priority })
            .HasDatabaseName("ix_jobs_status_due_at_priority");

        builder
            .HasIndex(job => job.LeaseUntil)
            .HasDatabaseName("ix_jobs_lease_until")
            .HasFilter("status = 'leased'");
    }
}
