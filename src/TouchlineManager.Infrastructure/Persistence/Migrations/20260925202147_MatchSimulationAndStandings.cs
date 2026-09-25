using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MatchSimulationAndStandings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "match");

            migrationBuilder.CreateTable(
                name: "input_snapshots",
                schema: "match",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fixture_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engine_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    rule_set_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    seed = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    seed_commitment = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    snapshot_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_input_snapshots", x => x.id);
                    table.CheckConstraint("ck_input_snapshots_engine_version", "length(engine_version) > 0");
                    table.CheckConstraint("ck_input_snapshots_seed_commitment", "length(seed_commitment) > 0");
                    table.CheckConstraint("ck_input_snapshots_snapshot_hash", "length(snapshot_hash) > 0");
                    table.ForeignKey(
                        name: "FK_input_snapshots_fixtures_fixture_id",
                        column: x => x.fixture_id,
                        principalSchema: "competition",
                        principalTable: "fixtures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "simulation_attempts",
                schema: "match",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fixture_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    engine_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    input_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    output_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    error_category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_simulation_attempts", x => x.id);
                    table.CheckConstraint("ck_simulation_attempts_completed", "completed_at >= started_at");
                    table.CheckConstraint("ck_simulation_attempts_number", "attempt_number >= 1");
                    table.CheckConstraint("ck_simulation_attempts_result", "(status = 'succeeded' and input_hash is not null and output_hash is not null) or (status = 'failed' and error_message is not null)");
                    table.CheckConstraint("ck_simulation_attempts_status", "status in ('succeeded', 'failed')");
                    table.ForeignKey(
                        name: "FK_simulation_attempts_fixtures_fixture_id",
                        column: x => x.fixture_id,
                        principalSchema: "competition",
                        principalTable: "fixtures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "standings",
                schema: "competition",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    division_season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    played = table.Column<int>(type: "integer", nullable: false),
                    won = table.Column<int>(type: "integer", nullable: false),
                    drawn = table.Column<int>(type: "integer", nullable: false),
                    lost = table.Column<int>(type: "integer", nullable: false),
                    goals_for = table.Column<int>(type: "integer", nullable: false),
                    goals_against = table.Column<int>(type: "integer", nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    yellow_cards = table.Column<int>(type: "integer", nullable: false),
                    red_cards = table.Column<int>(type: "integer", nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_standings", x => x.id);
                    table.CheckConstraint("ck_standings_cards", "yellow_cards >= 0 and red_cards >= 0");
                    table.CheckConstraint("ck_standings_goals", "goals_for >= 0 and goals_against >= 0");
                    table.CheckConstraint("ck_standings_played", "played = won + drawn + lost and played >= 0");
                    table.CheckConstraint("ck_standings_points", "points = (won * 3) + drawn");
                    table.CheckConstraint("ck_standings_rank", "rank between 1 and 18");
                    table.ForeignKey(
                        name: "FK_standings_clubs_club_id",
                        column: x => x.club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_standings_division_seasons_division_season_id",
                        column: x => x.division_season_id,
                        principalSchema: "competition",
                        principalTable: "division_seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "matches",
                schema: "match",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fixture_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engine_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    rule_set_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    seed_commitment = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    home_goals = table.Column<int>(type: "integer", nullable: false),
                    away_goals = table.Column<int>(type: "integer", nullable: false),
                    statistics = table.Column<string>(type: "jsonb", nullable: false),
                    input_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    output_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    simulation_attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_matches", x => x.id);
                    table.CheckConstraint("ck_matches_completed_after_started", "completed_at >= started_at");
                    table.CheckConstraint("ck_matches_output_hash", "length(output_hash) > 0");
                    table.CheckConstraint("ck_matches_score_non_negative", "home_goals >= 0 and away_goals >= 0");
                    table.ForeignKey(
                        name: "FK_matches_fixtures_fixture_id",
                        column: x => x.fixture_id,
                        principalSchema: "competition",
                        principalTable: "fixtures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_matches_simulation_attempts_simulation_attempt_id",
                        column: x => x.simulation_attempt_id,
                        principalSchema: "match",
                        principalTable: "simulation_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "events",
                schema: "match",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    minute = table.Column<int>(type: "integer", nullable: false),
                    stoppage_minute = table.Column<int>(type: "integer", nullable: false),
                    club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(18)", maxLength: 18, nullable: false),
                    participant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    secondary_participant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    zone = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    quality_basis_points = table.Column<int>(type: "integer", nullable: true),
                    absence_fixtures = table.Column<int>(type: "integer", nullable: true),
                    substitution_cause = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_events", x => x.id);
                    table.CheckConstraint("ck_events_absence", "absence_fixtures is null or absence_fixtures >= 0");
                    table.CheckConstraint("ck_events_minute", "minute >= 0 and stoppage_minute >= 0");
                    table.CheckConstraint("ck_events_quality", "quality_basis_points is null or (quality_basis_points between 0 and 10000)");
                    table.CheckConstraint("ck_events_sequence", "sequence >= 1");
                    table.CheckConstraint("ck_events_type", "event_type in ('kick_off', 'half_time', 'second_half_start', 'full_time', 'goal', 'penalty_awarded', 'penalty_goal', 'penalty_missed', 'shot_saved', 'shot_blocked', 'shot_off_target', 'woodwork', 'foul', 'yellow_card', 'second_yellow_card', 'red_card', 'offside', 'corner', 'injury', 'substitution')");
                    table.ForeignKey(
                        name: "FK_events_clubs_club_id",
                        column: x => x.club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_events_matches_match_id",
                        column: x => x.match_id,
                        principalSchema: "match",
                        principalTable: "matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_events_club_id_event_type",
                schema: "match",
                table: "events",
                columns: new[] { "club_id", "event_type" });

            migrationBuilder.CreateIndex(
                name: "ix_events_match_id_minute",
                schema: "match",
                table: "events",
                columns: new[] { "match_id", "minute" });

            migrationBuilder.CreateIndex(
                name: "ux_events_match_id_sequence",
                schema: "match",
                table: "events",
                columns: new[] { "match_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_input_snapshots_fixture_id",
                schema: "match",
                table: "input_snapshots",
                column: "fixture_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_matches_output_hash",
                schema: "match",
                table: "matches",
                column: "output_hash");

            migrationBuilder.CreateIndex(
                name: "IX_matches_simulation_attempt_id",
                schema: "match",
                table: "matches",
                column: "simulation_attempt_id");

            migrationBuilder.CreateIndex(
                name: "ux_matches_fixture_id",
                schema: "match",
                table: "matches",
                column: "fixture_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_simulation_attempts_fixture_id_attempt_number",
                schema: "match",
                table: "simulation_attempts",
                columns: new[] { "fixture_id", "attempt_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_standings_club_id",
                schema: "competition",
                table: "standings",
                column: "club_id");

            migrationBuilder.CreateIndex(
                name: "ix_standings_division_season_id_rank",
                schema: "competition",
                table: "standings",
                columns: new[] { "division_season_id", "rank" });

            migrationBuilder.CreateIndex(
                name: "ux_standings_division_season_id_club_id",
                schema: "competition",
                table: "standings",
                columns: new[] { "division_season_id", "club_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "events",
                schema: "match");

            migrationBuilder.DropTable(
                name: "input_snapshots",
                schema: "match");

            migrationBuilder.DropTable(
                name: "standings",
                schema: "competition");

            migrationBuilder.DropTable(
                name: "matches",
                schema: "match");

            migrationBuilder.DropTable(
                name: "simulation_attempts",
                schema: "match");
        }
    }
}
