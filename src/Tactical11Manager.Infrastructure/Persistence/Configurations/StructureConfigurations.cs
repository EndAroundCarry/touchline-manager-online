using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <c>world.club_structures</c>: one row per building per club, with its level and the upgrade under way
/// (`STRC-1`…`STRC-3`).
/// </summary>
/// <remarks>
/// <para>
/// The unique index on <c>(club_id, kind)</c> is what makes "a club has exactly one snack bar and one shop" a
/// database fact. The level and the pending upgrade are check constraints, so no write — including a repair —
/// can leave a building above the top level or an upgrade that skips a level or has a target without a due
/// time.
/// </para>
/// <para>
/// The version is the strong entity tag an upgrade order is made against, so it is also the concurrency
/// token: an order that raced another is refused with 0 rows affected rather than paying twice for one
/// decision (`CONC-1`, ADR-0009).
/// </para>
/// </remarks>
internal sealed class ClubStructureConfiguration : IEntityTypeConfiguration<ClubStructure>
{
    /// <summary>The longest structure kind code, so the column can be sized to hold every value.</summary>
    internal const int MaxKindCodeLength = 16;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ClubStructure> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("club_structures", "world", table =>
        {
            table.HasCheckConstraint(
                "ck_club_structures_kind",
                "kind in ('snack_bar', 'merchandise')");
            table.HasCheckConstraint(
                "ck_club_structures_level",
                $"level between 1 and {StructureRuleSet.MaxLevel}");
            table.HasCheckConstraint(
                "ck_club_structures_upgrade",
                "(upgrade_target_level is null and upgrade_due_at is null) "
                + "or (upgrade_target_level = level + 1 and upgrade_due_at is not null)");
        });

        builder.HasKey(structure => structure.Id);
        builder.Property(structure => structure.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(structure => structure.ClubId).HasColumnName("club_id").IsRequired();
        builder.Property(structure => structure.Kind)
            .HasColumnName("kind")
            .HasMaxLength(MaxKindCodeLength)
            .HasConversion(kind => kind.ToCode(), code => StructureKinds.FromCode(code))
            .IsRequired();
        builder.Property(structure => structure.Level).HasColumnName("level").IsRequired();
        builder.Property(structure => structure.UpgradeTargetLevel).HasColumnName("upgrade_target_level");
        builder.Property(structure => structure.UpgradeDueAt).HasColumnName("upgrade_due_at");
        builder.Property(structure => structure.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(structure => structure.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(structure => structure.Version).HasColumnName("version").IsRequired().IsConcurrencyToken();

        // The derived readings are never columns: a flag stored beside the target could disagree with it.
        builder.Ignore(structure => structure.IsBuilding);
        builder.Ignore(structure => structure.IsAtMaxLevel);

        builder.HasIndex(structure => new { structure.ClubId, structure.Kind })
            .IsUnique()
            .HasDatabaseName("ux_club_structures_club_id_kind");

        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(structure => structure.ClubId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>world.club_structure_takings</c>: the append-only history of what each building earned and cost
/// (`STRC-4`, `STRC-6`, `STRC-8`).
/// </summary>
/// <remarks>
/// <para>
/// The two partial unique indexes make the history idempotent the way the ledger is: a snack bar's match sale
/// is unique per club and fixture, and a weekly row is unique per club, building, kind and week, so a retried
/// publication or weekly run cannot write a line twice.
/// </para>
/// <para>
/// The shape constraints say what each row kind carries: a match sale is the snack bar's and names its
/// fixture, a week sale is the shop's and does not, and upkeep is never positive.
/// </para>
/// </remarks>
internal sealed class ClubStructureTakingsConfiguration : IEntityTypeConfiguration<ClubStructureTakings>
{
    /// <summary>The longest row kind code, so the column can be sized to hold every value.</summary>
    internal const int MaxRowKindCodeLength = 16;

    /// <summary>The longest bonus reason code, so the column can be sized to hold every value.</summary>
    internal const int MaxBonusReasonLength = 32;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ClubStructureTakings> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("club_structure_takings", "world", table =>
        {
            table.HasCheckConstraint(
                "ck_club_structure_takings_kind",
                "kind in ('snack_bar', 'merchandise')");
            table.HasCheckConstraint(
                "ck_club_structure_takings_row_kind",
                "row_kind in ('match_sale', 'week_sale', 'upkeep')");
            table.HasCheckConstraint(
                "ck_club_structure_takings_level",
                $"level between 1 and {StructureRuleSet.MaxLevel}");
            table.HasCheckConstraint(
                "ck_club_structure_takings_amounts",
                "customers >= 0 and base_minor >= 0 and bonus_bp >= 0");
            table.HasCheckConstraint(
                "ck_club_structure_takings_match_sale_shape",
                "row_kind <> 'match_sale' or (kind = 'snack_bar' and fixture_id is not null and amount_minor >= 0)");
            table.HasCheckConstraint(
                "ck_club_structure_takings_week_sale_shape",
                "row_kind <> 'week_sale' or (kind = 'merchandise' and fixture_id is null and amount_minor >= 0)");
            table.HasCheckConstraint(
                "ck_club_structure_takings_upkeep_shape",
                "row_kind <> 'upkeep' or (fixture_id is null and amount_minor <= 0)");
        });

        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(row => row.ClubId).HasColumnName("club_id").IsRequired();
        builder.Property(row => row.Kind)
            .HasColumnName("kind")
            .HasMaxLength(ClubStructureConfiguration.MaxKindCodeLength)
            .HasConversion(kind => kind.ToCode(), code => StructureKinds.FromCode(code))
            .IsRequired();
        builder.Property(row => row.RowKind)
            .HasColumnName("row_kind")
            .HasMaxLength(MaxRowKindCodeLength)
            .HasConversion(kind => kind.ToCode(), code => StructureTakingsKinds.FromCode(code))
            .IsRequired();
        builder.Property(row => row.FixtureId).HasColumnName("fixture_id");
        builder.Property(row => row.WeekOf).HasColumnName("week_of").IsRequired();
        builder.Property(row => row.Level).HasColumnName("level").IsRequired();
        builder.Property(row => row.Customers).HasColumnName("customers").IsRequired();
        builder.Property(row => row.BaseMinor).HasColumnName("base_minor").IsRequired();
        builder.Property(row => row.BonusBp).HasColumnName("bonus_bp").IsRequired();
        builder.Property(row => row.BonusReason)
            .HasColumnName("bonus_reason")
            .HasMaxLength(MaxBonusReasonLength)
            .IsRequired();
        builder.Property(row => row.AmountMinor).HasColumnName("amount_minor").IsRequired();
        builder.Property(row => row.LedgerEntryId).HasColumnName("ledger_entry_id").IsRequired();
        builder.Property(row => row.CreatedAt).HasColumnName("created_at").IsRequired();

        // A retried publication cannot sell a match's snacks twice.
        builder.HasIndex(row => new { row.ClubId, row.FixtureId })
            .IsUnique()
            .HasFilter("row_kind = 'match_sale'")
            .HasDatabaseName("ux_club_structure_takings_match_sale");

        // A retried weekly run cannot book a building's week twice.
        builder.HasIndex(row => new { row.ClubId, row.Kind, row.RowKind, row.WeekOf })
            .IsUnique()
            .HasFilter("row_kind <> 'match_sale'")
            .HasDatabaseName("ux_club_structure_takings_weekly");

        // The page reads one building's recent weeks and matches in time order.
        builder.HasIndex(row => new { row.ClubId, row.Kind, row.WeekOf })
            .HasDatabaseName("ix_club_structure_takings_club_kind_week");

        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(row => row.ClubId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Fixture>()
            .WithMany()
            .HasForeignKey(row => row.FixtureId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<LedgerEntry>()
            .WithMany()
            .HasForeignKey(row => row.LedgerEntryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
