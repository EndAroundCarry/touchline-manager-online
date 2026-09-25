using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps a division's stored table (master plan §6.4, `TBL-13`).
/// </summary>
/// <remarks>
/// <para>
/// A projection with an identity: the row is rewritten in place rather than deleted and re-inserted, so an
/// operator looking at a table row between two publications sees the same row move rather than a new one
/// appear. The unique pair is what makes "one line per club per division-season" a constraint instead of a
/// convention the projection is trusted to keep.
/// </para>
/// <para>
/// The consistency checks are here as well as in the aggregate, because a table is the one thing in the
/// game a manager reads directly and a row that contradicted itself would be believed: played is wins,
/// draws, and losses; points are three a win and one a draw; and nobody has a negative count of anything.
/// </para>
/// </remarks>
internal sealed class StandingConfiguration : IEntityTypeConfiguration<Standing>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Standing> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("standings", "competition", table =>
        {
            table.HasCheckConstraint(
                "ck_standings_played",
                "played = won + drawn + lost and played >= 0");
            table.HasCheckConstraint(
                "ck_standings_points",
                "points = (won * 3) + drawn");
            table.HasCheckConstraint(
                "ck_standings_goals",
                "goals_for >= 0 and goals_against >= 0");
            table.HasCheckConstraint(
                "ck_standings_cards",
                "yellow_cards >= 0 and red_cards >= 0");
            table.HasCheckConstraint(
                "ck_standings_rank",
                $"rank between 1 and {WorldRuleSet.ClubsPerDivision}");
        });

        builder.HasKey(standing => standing.Id);
        builder.Property(standing => standing.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(standing => standing.DivisionSeasonId).HasColumnName("division_season_id").IsRequired();
        builder.Property(standing => standing.ClubId).HasColumnName("club_id").IsRequired();
        builder.Property(standing => standing.Played).HasColumnName("played").IsRequired();
        builder.Property(standing => standing.Won).HasColumnName("won").IsRequired();
        builder.Property(standing => standing.Drawn).HasColumnName("drawn").IsRequired();
        builder.Property(standing => standing.Lost).HasColumnName("lost").IsRequired();
        builder.Property(standing => standing.GoalsFor).HasColumnName("goals_for").IsRequired();
        builder.Property(standing => standing.GoalsAgainst).HasColumnName("goals_against").IsRequired();
        builder.Property(standing => standing.Points).HasColumnName("points").IsRequired();
        builder.Property(standing => standing.YellowCards).HasColumnName("yellow_cards").IsRequired();
        builder.Property(standing => standing.RedCards).HasColumnName("red_cards").IsRequired();
        builder.Property(standing => standing.Rank).HasColumnName("rank").IsRequired();
        builder.Property(standing => standing.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(standing => standing.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(standing => new { standing.DivisionSeasonId, standing.ClubId })
            .IsUnique()
            .HasDatabaseName("ux_standings_division_season_id_club_id");

        // The order the table is read in, and the one the projection writes: a screen never sorts.
        builder.HasIndex(standing => new { standing.DivisionSeasonId, standing.Rank })
            .HasDatabaseName("ix_standings_division_season_id_rank");

        builder.HasOne<DivisionSeason>().WithMany()
            .HasForeignKey(standing => standing.DivisionSeasonId).OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Club>().WithMany()
            .HasForeignKey(standing => standing.ClubId).OnDelete(DeleteBehavior.Restrict);
    }
}
