using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TouchlineManager.Infrastructure.Persistence.Entities;

namespace TouchlineManager.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <c>ops.game_clock</c>: the one instant a stepped world is frozen at (ADR-0049, `TIME-6`).
/// </summary>
/// <remarks>
/// The check constraint on <c>id</c> is what makes the table single-row: the only identity it will accept is
/// the fixed one, so the clock can never fork into two rows that disagree. There is no index beyond the
/// primary key because every read is by that one identity.
/// </remarks>
internal sealed class OpsGameClockConfiguration : IEntityTypeConfiguration<OpsGameClock>
{
    /// <summary>The fixed identity of the single row.</summary>
    public static readonly Guid SingleRowId = Guid.Parse("00000000-0000-0000-0000-00000000c10c");

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OpsGameClock> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("game_clock", "ops", table =>
        {
            table.HasCheckConstraint(
                "ck_game_clock_single_row",
                $"id = '{SingleRowId:D}'");
            table.HasCheckConstraint("ck_game_clock_version", "version >= 1");
        });

        builder.HasKey(clock => clock.Id);
        builder.Property(clock => clock.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(clock => clock.GameNow).HasColumnName("game_now").IsRequired();
        builder.Property(clock => clock.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(clock => clock.Version).HasColumnName("version").IsRequired();
    }
}
