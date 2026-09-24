using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <c>competition.seasons</c>. One season number per world, which is what makes "the current
/// season" a lookup rather than a search.
/// </summary>
internal sealed class SeasonConfiguration : IEntityTypeConfiguration<Season>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Season> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("seasons", "competition", table =>
        {
            table.HasCheckConstraint(
                "ck_seasons_status",
                "status in ('scheduled', 'active', 'rollover', 'completed')");
            table.HasCheckConstraint("ck_seasons_sequence_number", "sequence_number >= 1");
            table.HasCheckConstraint("ck_seasons_window", "ends_at >= starts_at and rollover_ends_at >= ends_at");
        });

        builder.HasKey(season => season.Id);
        builder.Property(season => season.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(season => season.WorldId).HasColumnName("world_id").IsRequired();
        builder.Property(season => season.SequenceNumber).HasColumnName("sequence_number").IsRequired();
        builder.Property(season => season.DisplayLabel).HasColumnName("display_label").HasMaxLength(16).IsRequired();
        builder.Property(season => season.GameYear).HasColumnName("game_year").IsRequired();
        builder.Property(season => season.StartsAt).HasColumnName("starts_at").IsRequired();
        builder.Property(season => season.EndsAt).HasColumnName("ends_at").IsRequired();
        builder.Property(season => season.RolloverEndsAt).HasColumnName("rollover_ends_at").IsRequired();
        builder.Property(season => season.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .HasConversion(status => status.ToCode(), code => SeasonStatusRules.FromCode(code))
            .IsRequired();
        builder.Property(season => season.RuleSetVersion)
            .HasColumnName("rule_set_version")
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(season => season.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(season => season.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(season => season.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(season => new { season.WorldId, season.SequenceNumber })
            .IsUnique()
            .HasDatabaseName("ux_seasons_world_id_sequence_number");

        builder.HasOne<GameWorld>()
            .WithMany()
            .HasForeignKey(season => season.WorldId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>competition.divisions</c>. The unique index on <c>(country_id, tier_number)</c> is what
/// makes a duplicate tier impossible even if two provisioning runs raced past every application check
/// (`PYR-3`), and the capacity check pins every division at 18 clubs (`WORLD-4`).
/// </summary>
internal sealed class DivisionConfiguration : IEntityTypeConfiguration<Division>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Division> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("divisions", "competition", table =>
        {
            table.HasCheckConstraint(
                "ck_divisions_status",
                "status in ('provisioning', 'active', 'retired')");
            table.HasCheckConstraint("ck_divisions_tier_number", "tier_number >= 1");
            table.HasCheckConstraint("ck_divisions_capacity", "capacity = 18");
        });

        builder.HasKey(division => division.Id);
        builder.Property(division => division.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(division => division.CountryId).HasColumnName("country_id").IsRequired();
        builder.Property(division => division.TierNumber).HasColumnName("tier_number").IsRequired();
        builder.Property(division => division.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(80)
            .IsRequired();
        builder.Property(division => division.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .HasConversion(status => status.ToCode(), code => DivisionStatusRules.FromCode(code))
            .IsRequired();
        builder.Property(division => division.CreatedSeasonId).HasColumnName("created_season_id").IsRequired();
        builder.Property(division => division.Capacity).HasColumnName("capacity").IsRequired();
        builder.Property(division => division.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(division => division.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(division => division.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(division => new { division.CountryId, division.TierNumber })
            .IsUnique()
            .HasDatabaseName("ux_divisions_country_id_tier_number");

        builder.HasOne<Country>()
            .WithMany()
            .HasForeignKey(division => division.CountryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Season>()
            .WithMany()
            .HasForeignKey(division => division.CreatedSeasonId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Maps <c>competition.division_seasons</c>: one instance of a tier per season.</summary>
internal sealed class DivisionSeasonConfiguration : IEntityTypeConfiguration<DivisionSeason>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DivisionSeason> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("division_seasons", "competition", table => table.HasCheckConstraint(
            "ck_division_seasons_status",
            "status in ('scheduled', 'active', 'completed')"));

        builder.HasKey(divisionSeason => divisionSeason.Id);
        builder.Property(divisionSeason => divisionSeason.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(divisionSeason => divisionSeason.DivisionId).HasColumnName("division_id").IsRequired();
        builder.Property(divisionSeason => divisionSeason.SeasonId).HasColumnName("season_id").IsRequired();
        builder.Property(divisionSeason => divisionSeason.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .HasConversion(
                status => status.ToCode(),
                code => DivisionSeasonStatusRules.FromCode(code))
            .IsRequired();
        builder.Property(divisionSeason => divisionSeason.ScheduleSeed)
            .HasColumnName("schedule_seed")
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(divisionSeason => divisionSeason.TieDrawSeed)
            .HasColumnName("tie_draw_seed")
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(divisionSeason => divisionSeason.TieDrawHash)
            .HasColumnName("tie_draw_hash")
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(divisionSeason => divisionSeason.StandingsFinalizedAt)
            .HasColumnName("standings_finalized_at");
        builder.Property(divisionSeason => divisionSeason.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(divisionSeason => divisionSeason.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(divisionSeason => divisionSeason.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(divisionSeason => new { divisionSeason.DivisionId, divisionSeason.SeasonId })
            .IsUnique()
            .HasDatabaseName("ux_division_seasons_division_id_season_id");

        builder.HasIndex(divisionSeason => divisionSeason.SeasonId)
            .HasDatabaseName("ix_division_seasons_season_id");

        builder.HasOne<Division>()
            .WithMany()
            .HasForeignKey(divisionSeason => divisionSeason.DivisionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Season>()
            .WithMany()
            .HasForeignKey(divisionSeason => divisionSeason.SeasonId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>competition.club_season_entries</c>: who played where, immutably.
/// </summary>
/// <remarks>
/// Two unique indexes here, and the second is the one that is easy to miss: a club may appear once in a
/// division-season, and once in a whole season. Without the second, a bug in promotion could enter one
/// club into two divisions in the same season and every standings query would double-count it.
/// </remarks>
internal sealed class ClubSeasonEntryConfiguration : IEntityTypeConfiguration<ClubSeasonEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ClubSeasonEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("club_season_entries", "competition", table =>
        {
            table.HasCheckConstraint(
                "ck_club_season_entries_control_type",
                "initial_control_type in ('ai', 'human')");
            table.HasCheckConstraint(
                "ck_club_season_entries_final_rank",
                "final_rank is null or final_rank >= 1");
            table.HasCheckConstraint(
                "ck_club_season_entries_movement",
                "not (is_promoted and is_relegated)");
            table.HasCheckConstraint(
                "ck_club_season_entries_closing_cash",
                "closing_cash_minor is null or closing_cash_minor >= 0");
        });

        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(entry => entry.DivisionSeasonId).HasColumnName("division_season_id").IsRequired();
        builder.Property(entry => entry.SeasonId).HasColumnName("season_id").IsRequired();
        builder.Property(entry => entry.ClubId).HasColumnName("club_id").IsRequired();
        builder.Property(entry => entry.InitialControlType)
            .HasColumnName("initial_control_type")
            .HasMaxLength(8)
            .HasConversion(type => type.ToCode(), code => ClubControlTypes.FromCode(code))
            .IsRequired();
        builder.Property(entry => entry.FinalRank).HasColumnName("final_rank");
        builder.Property(entry => entry.IsPromoted).HasColumnName("is_promoted").IsRequired();
        builder.Property(entry => entry.IsRelegated).HasColumnName("is_relegated").IsRequired();
        builder.Property(entry => entry.ClosingReputation).HasColumnName("closing_reputation");
        builder.Property(entry => entry.ClosingCashMinor).HasColumnName("closing_cash_minor");
        builder.Property(entry => entry.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(entry => entry.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(entry => entry.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(entry => new { entry.DivisionSeasonId, entry.ClubId })
            .IsUnique()
            .HasDatabaseName("ux_club_season_entries_division_season_id_club_id");

        builder.HasIndex(entry => new { entry.SeasonId, entry.ClubId })
            .IsUnique()
            .HasDatabaseName("ux_club_season_entries_season_id_club_id");

        builder.HasIndex(entry => entry.ClubId).HasDatabaseName("ix_club_season_entries_club_id");

        builder.HasOne<DivisionSeason>()
            .WithMany()
            .HasForeignKey(entry => entry.DivisionSeasonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Season>()
            .WithMany()
            .HasForeignKey(entry => entry.SeasonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(entry => entry.ClubId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
