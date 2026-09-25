using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Rules;
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

/// <summary>
/// Maps <c>competition.matchdays</c>: one round of one division's season, the unit that locks and
/// publishes (`CAL-10`).
/// </summary>
/// <remarks>
/// The unique index on <c>(division_season_id, round_number)</c> is what makes "34 rounds, one row each"
/// a database fact rather than a generation promise, and the lock check keeps the derived lock instant
/// from drifting past the kickoff it belongs to (`CAL-3`).
/// </remarks>
internal sealed class MatchdayConfiguration : IEntityTypeConfiguration<Matchday>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Matchday> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("matchdays", "competition", table =>
        {
            table.HasCheckConstraint(
                "ck_matchdays_round_number",
                $"round_number between 1 and {WorldRuleSet.MatchdaysPerSeason}");
            table.HasCheckConstraint(
                "ck_matchdays_publication_status",
                "publication_status in ('pending', 'staged', 'published')");
            table.HasCheckConstraint("ck_matchdays_lock_before_kickoff", "lock_at < kickoff_at");
        });

        builder.HasKey(matchday => matchday.Id);
        builder.Property(matchday => matchday.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(matchday => matchday.DivisionSeasonId).HasColumnName("division_season_id").IsRequired();
        builder.Property(matchday => matchday.RoundNumber).HasColumnName("round_number").IsRequired();
        builder.Property(matchday => matchday.LockAt).HasColumnName("lock_at").IsRequired();
        builder.Property(matchday => matchday.KickoffAt).HasColumnName("kickoff_at").IsRequired();
        builder.Property(matchday => matchday.PublicationStatus)
            .HasColumnName("publication_status")
            .HasMaxLength(MatchdayPublicationStatuses.MaxCodeLength)
            .HasConversion(
                status => status.ToCode(),
                code => MatchdayPublicationStatusRules.FromCode(code))
            .IsRequired();
        builder.Property(matchday => matchday.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(matchday => matchday.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(matchday => matchday.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(matchday => new { matchday.DivisionSeasonId, matchday.RoundNumber })
            .IsUnique()
            .HasDatabaseName("ux_matchdays_division_season_id_round_number");

        // The worker's materialiser reads upcoming rounds by due time across all divisions ("the next
        // lock", "the next kickoff"), which this index serves without a division filter.
        builder.HasIndex(matchday => matchday.KickoffAt).HasDatabaseName("ix_matchdays_kickoff_at");
        builder.HasIndex(matchday => matchday.LockAt).HasDatabaseName("ix_matchdays_lock_at");

        builder.HasOne<DivisionSeason>()
            .WithMany()
            .HasForeignKey(matchday => matchday.DivisionSeasonId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>competition.fixtures</c>: one match, and the score it publishes (`MAT-7`).
/// </summary>
/// <remarks>
/// <para>
/// The score-presence check is the database half of "a score exists only once a result is staged or
/// published": <c>home_score</c>, <c>away_score</c>, and <c>match_id</c> are all present exactly in those
/// two states, so a half-resolved fixture cannot exist even if application code were wrong. The
/// home- and away-club checks and the two matchday-scoped unique indexes are the database's share of
/// "one fixture per club per matchday"; the full round-robin property is checked at generation
/// (master plan §6.4).
/// </para>
/// <para>
/// A published fixture is never deleted and its score is never edited; a void is a status, not a delete
/// (`PR-6`, `MAT-10`).
/// </para>
/// </remarks>
internal sealed class FixtureConfiguration : IEntityTypeConfiguration<Fixture>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Fixture> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("fixtures", "competition", table =>
        {
            table.HasCheckConstraint(
                "ck_fixtures_distinct_clubs",
                "home_club_id <> away_club_id");
            table.HasCheckConstraint(
                "ck_fixtures_status",
                "status in ('scheduled', 'locked', 'simulating', 'staged', 'published', 'void')");
            table.HasCheckConstraint(
                "ck_fixtures_score_non_negative",
                "(home_score is null or home_score >= 0) and (away_score is null or away_score >= 0)");
            table.HasCheckConstraint(
                "ck_fixtures_result_presence",
                "(status in ('staged', 'published') and home_score is not null and away_score is not null "
                + "and match_id is not null) or (status not in ('staged', 'published') "
                + "and home_score is null and away_score is null and match_id is null)");
            table.HasCheckConstraint(
                "ck_fixtures_published_at",
                "(status = 'published') = (published_at is not null)");
        });

        builder.HasKey(fixture => fixture.Id);
        builder.Property(fixture => fixture.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(fixture => fixture.MatchdayId).HasColumnName("matchday_id").IsRequired();
        builder.Property(fixture => fixture.HomeClubId).HasColumnName("home_club_id").IsRequired();
        builder.Property(fixture => fixture.AwayClubId).HasColumnName("away_club_id").IsRequired();
        builder.Property(fixture => fixture.KickoffAt).HasColumnName("kickoff_at").IsRequired();
        builder.Property(fixture => fixture.Status)
            .HasColumnName("status")
            .HasMaxLength(FixtureStatuses.MaxCodeLength)
            .HasConversion(status => status.ToCode(), code => FixtureStatusRules.FromCode(code))
            .IsRequired();
        builder.Property(fixture => fixture.HomeScore).HasColumnName("home_score");
        builder.Property(fixture => fixture.AwayScore).HasColumnName("away_score");
        builder.Property(fixture => fixture.MatchId).HasColumnName("match_id");
        builder.Property(fixture => fixture.PublishedAt).HasColumnName("published_at");
        builder.Property(fixture => fixture.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(fixture => fixture.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(fixture => fixture.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(fixture => new { fixture.MatchdayId, fixture.HomeClubId })
            .IsUnique()
            .HasDatabaseName("ux_fixtures_matchday_home_club");
        builder.HasIndex(fixture => new { fixture.MatchdayId, fixture.AwayClubId })
            .IsUnique()
            .HasDatabaseName("ux_fixtures_matchday_away_club");
        builder.HasIndex(fixture => new { fixture.MatchdayId, fixture.Status })
            .HasDatabaseName("ix_fixtures_matchday_status");
        builder.HasIndex(fixture => new { fixture.HomeClubId, fixture.KickoffAt })
            .HasDatabaseName("ix_fixtures_home_club_kickoff");
        builder.HasIndex(fixture => new { fixture.AwayClubId, fixture.KickoffAt })
            .HasDatabaseName("ix_fixtures_away_club_kickoff");

        builder.HasOne<Matchday>()
            .WithMany()
            .HasForeignKey(fixture => fixture.MatchdayId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(fixture => fixture.HomeClubId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(fixture => fixture.AwayClubId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
