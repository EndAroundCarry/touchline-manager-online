using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompetitionFixturesAndMatchdays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "matchdays",
                schema: "competition",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    division_season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    round_number = table.Column<int>(type: "integer", nullable: false),
                    lock_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    kickoff_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    publication_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_matchdays", x => x.id);
                    table.CheckConstraint("ck_matchdays_lock_before_kickoff", "lock_at < kickoff_at");
                    table.CheckConstraint("ck_matchdays_publication_status", "publication_status in ('pending', 'staged', 'published')");
                    table.CheckConstraint("ck_matchdays_round_number", "round_number between 1 and 34");
                    table.ForeignKey(
                        name: "FK_matchdays_division_seasons_division_season_id",
                        column: x => x.division_season_id,
                        principalSchema: "competition",
                        principalTable: "division_seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fixtures",
                schema: "competition",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    matchday_id = table.Column<Guid>(type: "uuid", nullable: false),
                    home_club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    away_club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kickoff_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    home_score = table.Column<int>(type: "integer", nullable: true),
                    away_score = table.Column<int>(type: "integer", nullable: true),
                    match_id = table.Column<Guid>(type: "uuid", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fixtures", x => x.id);
                    table.CheckConstraint("ck_fixtures_distinct_clubs", "home_club_id <> away_club_id");
                    table.CheckConstraint("ck_fixtures_published_at", "(status = 'published') = (published_at is not null)");
                    table.CheckConstraint("ck_fixtures_result_presence", "(status in ('staged', 'published') and home_score is not null and away_score is not null and match_id is not null) or (status not in ('staged', 'published') and home_score is null and away_score is null and match_id is null)");
                    table.CheckConstraint("ck_fixtures_score_non_negative", "(home_score is null or home_score >= 0) and (away_score is null or away_score >= 0)");
                    table.CheckConstraint("ck_fixtures_status", "status in ('scheduled', 'locked', 'simulating', 'staged', 'published', 'void')");
                    table.ForeignKey(
                        name: "FK_fixtures_clubs_away_club_id",
                        column: x => x.away_club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fixtures_clubs_home_club_id",
                        column: x => x.home_club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fixtures_matchdays_matchday_id",
                        column: x => x.matchday_id,
                        principalSchema: "competition",
                        principalTable: "matchdays",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fixtures_away_club_kickoff",
                schema: "competition",
                table: "fixtures",
                columns: new[] { "away_club_id", "kickoff_at" });

            migrationBuilder.CreateIndex(
                name: "ix_fixtures_home_club_kickoff",
                schema: "competition",
                table: "fixtures",
                columns: new[] { "home_club_id", "kickoff_at" });

            migrationBuilder.CreateIndex(
                name: "ix_fixtures_matchday_status",
                schema: "competition",
                table: "fixtures",
                columns: new[] { "matchday_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_fixtures_matchday_away_club",
                schema: "competition",
                table: "fixtures",
                columns: new[] { "matchday_id", "away_club_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_fixtures_matchday_home_club",
                schema: "competition",
                table: "fixtures",
                columns: new[] { "matchday_id", "home_club_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_matchdays_kickoff_at",
                schema: "competition",
                table: "matchdays",
                column: "kickoff_at");

            migrationBuilder.CreateIndex(
                name: "ix_matchdays_lock_at",
                schema: "competition",
                table: "matchdays",
                column: "lock_at");

            migrationBuilder.CreateIndex(
                name: "ux_matchdays_division_season_id_round_number",
                schema: "competition",
                table: "matchdays",
                columns: new[] { "division_season_id", "round_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fixtures",
                schema: "competition");

            migrationBuilder.DropTable(
                name: "matchdays",
                schema: "competition");
        }
    }
}
