using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TouchlineManager.Infrastructure.Persistence.Entities;

namespace TouchlineManager.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <c>ops.feature_flags</c>: the operator's switches (master plan §6.9, §13).
/// </summary>
/// <remarks>
/// The value and the rollout metadata are JSON documents because a flag's value is the flag's own business —
/// a boolean today, a percentage tomorrow — and no query filters on it. The unique index on
/// <c>(scope, key)</c> is what makes setting a flag an upsert rather than a race that could leave two rows
/// for one switch.
/// </remarks>
internal sealed class OpsFeatureFlagConfiguration : IEntityTypeConfiguration<OpsFeatureFlag>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OpsFeatureFlag> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("feature_flags", "ops", table =>
        {
            table.HasCheckConstraint("ck_feature_flags_scope", "length(scope) > 0");
            table.HasCheckConstraint("ck_feature_flags_key", "length(key) > 0");
            table.HasCheckConstraint("ck_feature_flags_version", "version >= 1");
        });

        builder.HasKey(flag => flag.Id);
        builder.Property(flag => flag.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(flag => flag.Scope).HasColumnName("scope").HasMaxLength(40).IsRequired();
        builder.Property(flag => flag.Key).HasColumnName("key").HasMaxLength(100).IsRequired();
        builder.Property(flag => flag.ValueJson).HasColumnName("value").HasColumnType("jsonb").IsRequired();
        builder.Property(flag => flag.RolloutMetadataJson)
            .HasColumnName("rollout_metadata")
            .HasColumnType("jsonb");
        builder.Property(flag => flag.Version).HasColumnName("version").IsRequired();
        builder.Property(flag => flag.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(flag => flag.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(flag => new { flag.Scope, flag.Key })
            .IsUnique()
            .HasDatabaseName("ux_feature_flags_scope_key");
    }
}
