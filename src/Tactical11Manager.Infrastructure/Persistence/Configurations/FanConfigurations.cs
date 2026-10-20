using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <c>fans.club_fan_bases</c>, one row of fans for every club.
/// </summary>
/// <remarks>
/// The check constraints are the bounds the fan rules keep: a club always has fans, and the mood stays on its scale. The floor and the soft
/// cap depend on the club's tier, which is not a column here, so they stay in the rules.
/// </remarks>
internal sealed class ClubFanBaseConfiguration : IEntityTypeConfiguration<ClubFanBase>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ClubFanBase> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("club_fan_bases", "fans", table =>
        {
            table.HasCheckConstraint("ck_club_fan_bases_fans", "fans >= 1");
            table.HasCheckConstraint("ck_club_fan_bases_mood", "mood_bp between 0 and 10000");
            table.HasCheckConstraint(
                "ck_club_fan_bases_target",
                "season_target_rank is null or season_target_rank >= 1");
            table.HasCheckConstraint("ck_club_fan_bases_rule_set", "length(rule_set_version) > 0");
        });

        builder.HasKey(fanBase => fanBase.ClubId);
        builder.Property(fanBase => fanBase.ClubId).HasColumnName("club_id").ValueGeneratedNever();
        builder.Property(fanBase => fanBase.Fans).HasColumnName("fans").IsRequired();
        builder.Property(fanBase => fanBase.MoodBp).HasColumnName("mood_bp").IsRequired();
        builder.Property(fanBase => fanBase.SeasonTargetRank).HasColumnName("season_target_rank");
        builder.Property(fanBase => fanBase.LastWeekGap).HasColumnName("last_week_gap");
        builder.Property(fanBase => fanBase.RuleSetVersion)
            .HasColumnName("rule_set_version")
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(fanBase => fanBase.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(fanBase => fanBase.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(fanBase => fanBase.Version).HasColumnName("version").IsRequired().IsConcurrencyToken();

        builder.Ignore(fanBase => fanBase.State);

        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(fanBase => fanBase.ClubId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>fans.club_fan_events</c>, the append-only history the Fan Base page draws its charts from.
/// </summary>
/// <remarks>
/// <para>
/// The shape constraints say which fields each kind of event carries: a match names its fixture and what was expected and won, a takeover
/// names its tenure, a weekly review names its week.
/// </para>
/// <para>
/// The two unique indexes are what makes the writes happen once: a fixture moves a club's fans once, and a club is reviewed once a week.
/// A retried publication or review collides with the first rather than moving the fans twice.
/// </para>
/// </remarks>
internal sealed class ClubFanEventConfiguration : IEntityTypeConfiguration<ClubFanEvent>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ClubFanEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("club_fan_events", "fans", table =>
        {
            table.HasCheckConstraint(
                "ck_club_fan_events_kind",
                "kind in (" + string.Join(", ", FanEventKinds.AllCodes.Select(code => $"'{code}'")) + ")");
            table.HasCheckConstraint("ck_club_fan_events_fans", "fans_before >= 1 and fans_after >= 1");
            table.HasCheckConstraint(
                "ck_club_fan_events_mood",
                "mood_before_bp between 0 and 10000 and mood_after_bp between 0 and 10000");
            table.HasCheckConstraint(
                "ck_club_fan_events_expected_points",
                "expected_points_c is null or expected_points_c between 0 and 300");
            table.HasCheckConstraint(
                "ck_club_fan_events_points_won",
                "points_won is null or points_won in (0, 1, 3)");
            table.HasCheckConstraint(
                "ck_club_fan_events_match_shape",
                "kind <> 'match' or (fixture_id is not null and expected_points_c is not null and points_won is not null)");
            table.HasCheckConstraint(
                "ck_club_fan_events_takeover_shape",
                "kind <> 'takeover' or tenure_id is not null");
            table.HasCheckConstraint(
                "ck_club_fan_events_weekly_shape",
                "kind <> 'weekly' or week_of is not null");
        });

        builder.HasKey(fanEvent => fanEvent.Id);
        builder.Property(fanEvent => fanEvent.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(fanEvent => fanEvent.ClubId).HasColumnName("club_id").IsRequired();
        builder.Property(fanEvent => fanEvent.SeasonId).HasColumnName("season_id").IsRequired();
        builder.Property(fanEvent => fanEvent.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(fanEvent => fanEvent.Kind)
            .HasColumnName("kind")
            .HasMaxLength(FanEventKinds.MaxCodeLength)
            .HasConversion(kind => FanEventKinds.Code(kind), code => FanEventKinds.FromCode(code))
            .IsRequired();
        builder.Property(fanEvent => fanEvent.TenureId).HasColumnName("tenure_id");
        builder.Property(fanEvent => fanEvent.FixtureId).HasColumnName("fixture_id");
        builder.Property(fanEvent => fanEvent.WeekOf).HasColumnName("week_of");
        builder.Property(fanEvent => fanEvent.FansBefore).HasColumnName("fans_before").IsRequired();
        builder.Property(fanEvent => fanEvent.FansAfter).HasColumnName("fans_after").IsRequired();
        builder.Property(fanEvent => fanEvent.MoodBeforeBp).HasColumnName("mood_before_bp").IsRequired();
        builder.Property(fanEvent => fanEvent.MoodAfterBp).HasColumnName("mood_after_bp").IsRequired();
        builder.Property(fanEvent => fanEvent.ExpectedPointsCentis).HasColumnName("expected_points_c");
        builder.Property(fanEvent => fanEvent.PointsWon).HasColumnName("points_won");
        builder.Property(fanEvent => fanEvent.Gap).HasColumnName("gap");

        // The charts read one club's history in time order.
        builder.HasIndex(fanEvent => new { fanEvent.ClubId, fanEvent.OccurredAt })
            .HasDatabaseName("ix_club_fan_events_club_time");

        builder.HasIndex(fanEvent => new { fanEvent.ClubId, fanEvent.FixtureId })
            .IsUnique()
            .HasFilter("kind = 'match'")
            .HasDatabaseName("ux_club_fan_events_match");

        builder.HasIndex(fanEvent => new { fanEvent.ClubId, fanEvent.WeekOf })
            .IsUnique()
            .HasFilter("kind = 'weekly'")
            .HasDatabaseName("ux_club_fan_events_weekly");

        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(fanEvent => fanEvent.ClubId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Season>()
            .WithMany()
            .HasForeignKey(fanEvent => fanEvent.SeasonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ClubTenure>()
            .WithMany()
            .HasForeignKey(fanEvent => fanEvent.TenureId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Fixture>()
            .WithMany()
            .HasForeignKey(fanEvent => fanEvent.FixtureId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>fans.fixture_fan_expectations</c>, what each side's fans expected from a fixture, frozen at the lock.
/// </summary>
internal sealed class FixtureFanExpectationConfiguration : IEntityTypeConfiguration<FixtureFanExpectation>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<FixtureFanExpectation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("fixture_fan_expectations", "fans", table =>
        {
            table.HasCheckConstraint("ck_fixture_fan_expectations_points", "expected_points_c between 0 and 300");
            table.HasCheckConstraint(
                "ck_fixture_fan_expectations_chances",
                "win_bp >= 0 and draw_bp >= 0 and loss_bp >= 0 and win_bp + draw_bp + loss_bp = 10000");
            table.HasCheckConstraint(
                "ck_fixture_fan_expectations_label",
                "label_code in ('damage_limitation', 'tough_game', 'a_point_would_do', 'could_go_either_way', "
                + "'should_win', 'expect_a_win', 'should_hammer_them')");
            table.HasCheckConstraint(
                "ck_fixture_fan_expectations_score",
                "predicted_goals_for >= 0 and predicted_goals_against >= 0");
        });

        builder.HasKey(expectation => new { expectation.FixtureId, expectation.ClubId });
        builder.Property(expectation => expectation.FixtureId).HasColumnName("fixture_id").ValueGeneratedNever();
        builder.Property(expectation => expectation.ClubId).HasColumnName("club_id").ValueGeneratedNever();
        builder.Property(expectation => expectation.ExpectedPointsCentis).HasColumnName("expected_points_c").IsRequired();
        builder.Property(expectation => expectation.WinBp).HasColumnName("win_bp").IsRequired();
        builder.Property(expectation => expectation.DrawBp).HasColumnName("draw_bp").IsRequired();
        builder.Property(expectation => expectation.LossBp).HasColumnName("loss_bp").IsRequired();
        builder.Property(expectation => expectation.Label)
            .HasColumnName("label_code")
            .HasMaxLength(32)
            .HasConversion(label => FanExpectationLabels.Code(label), code => FanExpectationLabels.FromCode(code)!.Value)
            .IsRequired();
        builder.Property(expectation => expectation.PredictedGoalsFor).HasColumnName("predicted_goals_for").IsRequired();
        builder.Property(expectation => expectation.PredictedGoalsAgainst).HasColumnName("predicted_goals_against").IsRequired();
        builder.Property(expectation => expectation.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Ignore(expectation => expectation.Expectation);

        builder.HasOne<Fixture>()
            .WithMany()
            .HasForeignKey(expectation => expectation.FixtureId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(expectation => expectation.ClubId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
