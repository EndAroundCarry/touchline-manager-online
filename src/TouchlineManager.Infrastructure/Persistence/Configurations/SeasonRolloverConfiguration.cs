using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <c>competition.season_rollovers</c>: the resumable checkpoint of one season's rollover
/// (`PR-4`, master plan §7.5, ADR-0031).
/// </summary>
/// <remarks>
/// The unique index on <c>(world_id, season_id)</c> is what makes the rollover a singleton for a closing
/// season however many times the materialiser runs or the queue redelivers: a second row for the same season
/// cannot exist, so the checkpoint that resumes is always the one the first attempt wrote.
/// </remarks>
internal sealed class SeasonRolloverConfiguration : IEntityTypeConfiguration<SeasonRollover>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SeasonRollover> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("season_rollovers", "competition", table => table.HasCheckConstraint(
            "ck_season_rollovers_phase",
            "phase in ('started', 'frozen', 'finalized', 'squads', 'moved', 'completed', 'failed')"));

        builder.HasKey(rollover => rollover.Id);
        builder.Property(rollover => rollover.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(rollover => rollover.WorldId).HasColumnName("world_id").IsRequired();
        builder.Property(rollover => rollover.SeasonId).HasColumnName("season_id").IsRequired();
        builder.Property(rollover => rollover.NextSeasonId).HasColumnName("next_season_id");
        builder.Property(rollover => rollover.Phase)
            .HasColumnName("phase")
            .HasMaxLength(SeasonRolloverPhases.MaxCodeLength)
            .HasConversion(phase => phase.ToCode(), code => SeasonRolloverPhaseRules.FromCode(code))
            .IsRequired();
        builder.Property(rollover => rollover.FailureReason)
            .HasColumnName("failure_reason")
            .HasMaxLength(2000);
        builder.Property(rollover => rollover.StartedAt).HasColumnName("started_at").IsRequired();
        builder.Property(rollover => rollover.CompletedAt).HasColumnName("completed_at");
        builder.Property(rollover => rollover.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(rollover => rollover.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(rollover => rollover.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(rollover => new { rollover.WorldId, rollover.SeasonId })
            .IsUnique()
            .HasDatabaseName("ux_season_rollovers_world_id_season_id");

        builder.HasIndex(rollover => rollover.Phase).HasDatabaseName("ix_season_rollovers_phase");

        builder.HasOne<GameWorld>()
            .WithMany()
            .HasForeignKey(rollover => rollover.WorldId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Season>()
            .WithMany()
            .HasForeignKey(rollover => rollover.SeasonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Season>()
            .WithMany()
            .HasForeignKey(rollover => rollover.NextSeasonId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
