using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tactical11Manager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FanBase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "fans");

            migrationBuilder.CreateTable(
                name: "club_fan_bases",
                schema: "fans",
                columns: table => new
                {
                    club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fans = table.Column<int>(type: "integer", nullable: false),
                    mood_bp = table.Column<int>(type: "integer", nullable: false),
                    season_target_rank = table.Column<int>(type: "integer", nullable: true),
                    last_week_gap = table.Column<int>(type: "integer", nullable: true),
                    rule_set_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_club_fan_bases", x => x.club_id);
                    table.CheckConstraint("ck_club_fan_bases_fans", "fans >= 1");
                    table.CheckConstraint("ck_club_fan_bases_mood", "mood_bp between 0 and 10000");
                    table.CheckConstraint("ck_club_fan_bases_rule_set", "length(rule_set_version) > 0");
                    table.CheckConstraint("ck_club_fan_bases_target", "season_target_rank is null or season_target_rank >= 1");
                    table.ForeignKey(
                        name: "FK_club_fan_bases_clubs_club_id",
                        column: x => x.club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "club_fan_events",
                schema: "fans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    tenure_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fixture_id = table.Column<Guid>(type: "uuid", nullable: true),
                    week_of = table.Column<DateOnly>(type: "date", nullable: true),
                    fans_before = table.Column<int>(type: "integer", nullable: false),
                    fans_after = table.Column<int>(type: "integer", nullable: false),
                    mood_before_bp = table.Column<int>(type: "integer", nullable: false),
                    mood_after_bp = table.Column<int>(type: "integer", nullable: false),
                    expected_points_c = table.Column<int>(type: "integer", nullable: true),
                    points_won = table.Column<int>(type: "integer", nullable: true),
                    gap = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_club_fan_events", x => x.id);
                    table.CheckConstraint("ck_club_fan_events_expected_points", "expected_points_c is null or expected_points_c between 0 and 300");
                    table.CheckConstraint("ck_club_fan_events_fans", "fans_before >= 1 and fans_after >= 1");
                    table.CheckConstraint("ck_club_fan_events_kind", "kind in ('opening', 'match', 'weekly', 'season_reset', 'promotion', 'relegation', 'takeover')");
                    table.CheckConstraint("ck_club_fan_events_match_shape", "kind <> 'match' or (fixture_id is not null and expected_points_c is not null and points_won is not null)");
                    table.CheckConstraint("ck_club_fan_events_mood", "mood_before_bp between 0 and 10000 and mood_after_bp between 0 and 10000");
                    table.CheckConstraint("ck_club_fan_events_points_won", "points_won is null or points_won in (0, 1, 3)");
                    table.CheckConstraint("ck_club_fan_events_takeover_shape", "kind <> 'takeover' or tenure_id is not null");
                    table.CheckConstraint("ck_club_fan_events_weekly_shape", "kind <> 'weekly' or week_of is not null");
                    table.ForeignKey(
                        name: "FK_club_fan_events_club_tenures_tenure_id",
                        column: x => x.tenure_id,
                        principalSchema: "world",
                        principalTable: "club_tenures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_club_fan_events_clubs_club_id",
                        column: x => x.club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_club_fan_events_fixtures_fixture_id",
                        column: x => x.fixture_id,
                        principalSchema: "competition",
                        principalTable: "fixtures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_club_fan_events_seasons_season_id",
                        column: x => x.season_id,
                        principalSchema: "competition",
                        principalTable: "seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fixture_fan_expectations",
                schema: "fans",
                columns: table => new
                {
                    fixture_id = table.Column<Guid>(type: "uuid", nullable: false),
                    club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expected_points_c = table.Column<int>(type: "integer", nullable: false),
                    win_bp = table.Column<int>(type: "integer", nullable: false),
                    draw_bp = table.Column<int>(type: "integer", nullable: false),
                    loss_bp = table.Column<int>(type: "integer", nullable: false),
                    label_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    predicted_goals_for = table.Column<int>(type: "integer", nullable: false),
                    predicted_goals_against = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fixture_fan_expectations", x => new { x.fixture_id, x.club_id });
                    table.CheckConstraint("ck_fixture_fan_expectations_chances", "win_bp >= 0 and draw_bp >= 0 and loss_bp >= 0 and win_bp + draw_bp + loss_bp = 10000");
                    table.CheckConstraint("ck_fixture_fan_expectations_label", "label_code in ('damage_limitation', 'tough_game', 'a_point_would_do', 'could_go_either_way', 'should_win', 'expect_a_win', 'should_hammer_them')");
                    table.CheckConstraint("ck_fixture_fan_expectations_points", "expected_points_c between 0 and 300");
                    table.CheckConstraint("ck_fixture_fan_expectations_score", "predicted_goals_for >= 0 and predicted_goals_against >= 0");
                    table.ForeignKey(
                        name: "FK_fixture_fan_expectations_clubs_club_id",
                        column: x => x.club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fixture_fan_expectations_fixtures_fixture_id",
                        column: x => x.fixture_id,
                        principalSchema: "competition",
                        principalTable: "fixtures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_club_fan_events_club_time",
                schema: "fans",
                table: "club_fan_events",
                columns: new[] { "club_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_club_fan_events_fixture_id",
                schema: "fans",
                table: "club_fan_events",
                column: "fixture_id");

            migrationBuilder.CreateIndex(
                name: "IX_club_fan_events_season_id",
                schema: "fans",
                table: "club_fan_events",
                column: "season_id");

            migrationBuilder.CreateIndex(
                name: "IX_club_fan_events_tenure_id",
                schema: "fans",
                table: "club_fan_events",
                column: "tenure_id");

            migrationBuilder.CreateIndex(
                name: "ux_club_fan_events_match",
                schema: "fans",
                table: "club_fan_events",
                columns: new[] { "club_id", "fixture_id" },
                unique: true,
                filter: "kind = 'match'");

            migrationBuilder.CreateIndex(
                name: "ux_club_fan_events_weekly",
                schema: "fans",
                table: "club_fan_events",
                columns: new[] { "club_id", "week_of" },
                unique: true,
                filter: "kind = 'weekly'");

            migrationBuilder.CreateIndex(
                name: "IX_fixture_fan_expectations_club_id",
                schema: "fans",
                table: "fixture_fan_expectations",
                column: "club_id");

            // Backfill (expand step, MIG-3): every club that already exists gets the fans a new club of its tier opens with, at the tier it
            // plays in this season (a club with no entry reads as tier 1). The numbers are FanRuleSet's: 10,000 fans at tier 1 and three
            // quarters of the tier above (rounded half up, tier by tier) for each tier down, calm mood (5,000), rule set fan-rules-v1.
            // No target place is set: the first weekly review gives one from the squads as they stand.
            migrationBuilder.Sql(
                """
                WITH RECURSIVE tier_fans(tier, fans) AS (
                    SELECT 1, 10000
                    UNION ALL
                    SELECT tier + 1, (fans * 3 + 2) / 4 FROM tier_fans WHERE tier < 30
                )
                INSERT INTO fans.club_fan_bases
                    (club_id, fans, mood_bp, season_target_rank, last_week_gap, rule_set_version, created_at, updated_at, version)
                SELECT club.id, GREATEST(1, tier_fans.fans), 5000, NULL, NULL, 'fan-rules-v1', now(), now(), 1
                FROM world.clubs AS club
                LEFT JOIN LATERAL (
                    SELECT division.tier_number
                    FROM world.game_worlds AS game_world
                    JOIN competition.seasons AS season
                        ON season.world_id = game_world.id AND season.sequence_number = game_world.current_season_number
                    JOIN competition.club_season_entries AS entry
                        ON entry.season_id = season.id AND entry.club_id = club.id
                    JOIN competition.division_seasons AS division_season ON division_season.id = entry.division_season_id
                    JOIN competition.divisions AS division ON division.id = division_season.division_id
                    WHERE game_world.id = club.world_id
                    LIMIT 1
                ) AS placed ON TRUE
                JOIN tier_fans ON tier_fans.tier = COALESCE(placed.tier_number, 1);
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO fans.club_fan_events
                    (id, club_id, season_id, occurred_at, kind, tenure_id, fixture_id, week_of,
                     fans_before, fans_after, mood_before_bp, mood_after_bp, expected_points_c, points_won, gap)
                SELECT gen_random_uuid(), fan_base.club_id, season.id, now(), 'opening', NULL, NULL, NULL,
                       fan_base.fans, fan_base.fans, fan_base.mood_bp, fan_base.mood_bp, NULL, NULL, NULL
                FROM fans.club_fan_bases AS fan_base
                JOIN world.clubs AS club ON club.id = fan_base.club_id
                JOIN world.game_worlds AS game_world ON game_world.id = club.world_id
                JOIN competition.seasons AS season
                    ON season.world_id = game_world.id AND season.sequence_number = game_world.current_season_number;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "club_fan_bases",
                schema: "fans");

            migrationBuilder.DropTable(
                name: "club_fan_events",
                schema: "fans");

            migrationBuilder.DropTable(
                name: "fixture_fan_expectations",
                schema: "fans");
        }
    }
}
