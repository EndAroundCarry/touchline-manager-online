using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tactical11Manager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PromotionPlayoffs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kind",
                schema: "competition",
                table: "matchdays",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "league");

            migrationBuilder.CreateTable(
                name: "playoff_ties",
                schema: "competition",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    country_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_division_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feeder_division_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    slot = table.Column<int>(type: "integer", nullable: false),
                    home_club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    home_division_id = table.Column<Guid>(type: "uuid", nullable: false),
                    home_place = table.Column<int>(type: "integer", nullable: false),
                    away_club_id = table.Column<Guid>(type: "uuid", nullable: true),
                    away_division_id = table.Column<Guid>(type: "uuid", nullable: true),
                    away_place = table.Column<int>(type: "integer", nullable: true),
                    fixture_id = table.Column<Guid>(type: "uuid", nullable: true),
                    winner_club_id = table.Column<Guid>(type: "uuid", nullable: true),
                    loser_club_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_by = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    shootout_home_score = table.Column<int>(type: "integer", nullable: true),
                    shootout_away_score = table.Column<int>(type: "integer", nullable: true),
                    shootout_kicks = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_playoff_ties", x => x.id);
                    table.CheckConstraint("ck_playoff_ties_away_side", "(away_club_id is null) = (away_division_id is null) and (away_club_id is null) = (away_place is null)");
                    table.CheckConstraint("ck_playoff_ties_decided_by", "decided_by is null or decided_by in ('score', 'penalties')");
                    table.CheckConstraint("ck_playoff_ties_decision", "(winner_club_id is null) = (loser_club_id is null) and (winner_club_id is null) = (decided_by is null)");
                    table.CheckConstraint("ck_playoff_ties_distinct_clubs", "home_club_id <> away_club_id");
                    table.CheckConstraint("ck_playoff_ties_fixture_needs_sides", "fixture_id is null or away_club_id is not null");
                    table.CheckConstraint("ck_playoff_ties_loser", "loser_club_id is null or ((loser_club_id = home_club_id or loser_club_id = away_club_id) and loser_club_id <> winner_club_id)");
                    table.CheckConstraint("ck_playoff_ties_places", "home_place >= 1 and (away_place is null or away_place >= 1)");
                    table.CheckConstraint("ck_playoff_ties_semi_final_sides", "stage <> 'semi_final' or away_club_id is not null");
                    table.CheckConstraint("ck_playoff_ties_shootout_decision", "(shootout_home_score is not null) = coalesce(decided_by = 'penalties', false)");
                    table.CheckConstraint("ck_playoff_ties_shootout_score", "(shootout_home_score is null) = (shootout_away_score is null) and (shootout_home_score is null or (shootout_home_score >= 0 and shootout_away_score >= 0))");
                    table.CheckConstraint("ck_playoff_ties_slot", "slot between 1 and 3");
                    table.CheckConstraint("ck_playoff_ties_stage", "stage in ('semi_final', 'final')");
                    table.CheckConstraint("ck_playoff_ties_winner", "winner_club_id is null or winner_club_id = home_club_id or winner_club_id = away_club_id");
                    table.ForeignKey(
                        name: "FK_playoff_ties_clubs_away_club_id",
                        column: x => x.away_club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_playoff_ties_clubs_home_club_id",
                        column: x => x.home_club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_playoff_ties_clubs_loser_club_id",
                        column: x => x.loser_club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_playoff_ties_clubs_winner_club_id",
                        column: x => x.winner_club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_playoff_ties_countries_country_id",
                        column: x => x.country_id,
                        principalSchema: "world",
                        principalTable: "countries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_playoff_ties_divisions_away_division_id",
                        column: x => x.away_division_id,
                        principalSchema: "competition",
                        principalTable: "divisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_playoff_ties_divisions_feeder_division_id",
                        column: x => x.feeder_division_id,
                        principalSchema: "competition",
                        principalTable: "divisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_playoff_ties_divisions_home_division_id",
                        column: x => x.home_division_id,
                        principalSchema: "competition",
                        principalTable: "divisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_playoff_ties_divisions_parent_division_id",
                        column: x => x.parent_division_id,
                        principalSchema: "competition",
                        principalTable: "divisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_playoff_ties_fixtures_fixture_id",
                        column: x => x.fixture_id,
                        principalSchema: "competition",
                        principalTable: "fixtures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_playoff_ties_seasons_season_id",
                        column: x => x.season_id,
                        principalSchema: "competition",
                        principalTable: "seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_matchdays_kind",
                schema: "competition",
                table: "matchdays",
                sql: "kind in ('league', 'playoff')");

            migrationBuilder.CreateIndex(
                name: "IX_playoff_ties_away_club_id",
                schema: "competition",
                table: "playoff_ties",
                column: "away_club_id");

            migrationBuilder.CreateIndex(
                name: "IX_playoff_ties_away_division_id",
                schema: "competition",
                table: "playoff_ties",
                column: "away_division_id");

            migrationBuilder.CreateIndex(
                name: "IX_playoff_ties_country_id",
                schema: "competition",
                table: "playoff_ties",
                column: "country_id");

            migrationBuilder.CreateIndex(
                name: "IX_playoff_ties_feeder_division_id",
                schema: "competition",
                table: "playoff_ties",
                column: "feeder_division_id");

            migrationBuilder.CreateIndex(
                name: "IX_playoff_ties_home_club_id",
                schema: "competition",
                table: "playoff_ties",
                column: "home_club_id");

            migrationBuilder.CreateIndex(
                name: "IX_playoff_ties_home_division_id",
                schema: "competition",
                table: "playoff_ties",
                column: "home_division_id");

            migrationBuilder.CreateIndex(
                name: "IX_playoff_ties_loser_club_id",
                schema: "competition",
                table: "playoff_ties",
                column: "loser_club_id");

            migrationBuilder.CreateIndex(
                name: "IX_playoff_ties_parent_division_id",
                schema: "competition",
                table: "playoff_ties",
                column: "parent_division_id");

            migrationBuilder.CreateIndex(
                name: "IX_playoff_ties_winner_club_id",
                schema: "competition",
                table: "playoff_ties",
                column: "winner_club_id");

            migrationBuilder.CreateIndex(
                name: "ux_playoff_ties_fixture_id",
                schema: "competition",
                table: "playoff_ties",
                column: "fixture_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_playoff_ties_season_parent_stage_feeder",
                schema: "competition",
                table: "playoff_ties",
                columns: new[] { "season_id", "parent_division_id", "stage", "feeder_division_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "playoff_ties",
                schema: "competition");

            migrationBuilder.DropCheckConstraint(
                name: "ck_matchdays_kind",
                schema: "competition",
                table: "matchdays");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "competition",
                table: "matchdays");
        }
    }
}
