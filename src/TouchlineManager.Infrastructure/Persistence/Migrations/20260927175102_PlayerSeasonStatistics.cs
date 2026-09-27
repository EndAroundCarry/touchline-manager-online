using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlayerSeasonStatistics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "player_season_stats",
                schema: "competition",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    division_season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    appearances = table.Column<int>(type: "integer", nullable: false),
                    starts = table.Column<int>(type: "integer", nullable: false),
                    minutes_played = table.Column<int>(type: "integer", nullable: false),
                    goals = table.Column<int>(type: "integer", nullable: false),
                    assists = table.Column<int>(type: "integer", nullable: false),
                    shots = table.Column<int>(type: "integer", nullable: false),
                    shots_on_target = table.Column<int>(type: "integer", nullable: false),
                    saves = table.Column<int>(type: "integer", nullable: false),
                    yellow_cards = table.Column<int>(type: "integer", nullable: false),
                    red_cards = table.Column<int>(type: "integer", nullable: false),
                    rating_basis_points_total = table.Column<long>(type: "bigint", nullable: false),
                    rated_appearances = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_season_stats", x => x.id);
                    table.CheckConstraint("ck_player_season_stats_counts", "appearances >= 0 and starts >= 0 and starts <= appearances and minutes_played >= 0 and goals >= 0 and assists >= 0 and shots >= 0 and shots_on_target >= 0 and shots_on_target <= shots and saves >= 0 and yellow_cards >= 0 and red_cards >= 0 and rating_basis_points_total >= 0 and rated_appearances >= 0");
                    table.CheckConstraint("ck_player_season_stats_rating", "case when rated_appearances = 0 then 0 else rating_basis_points_total / rated_appearances end between 0 and 10000");
                    table.ForeignKey(
                        name: "FK_player_season_stats_clubs_club_id",
                        column: x => x.club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_player_season_stats_division_seasons_division_season_id",
                        column: x => x.division_season_id,
                        principalSchema: "competition",
                        principalTable: "division_seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_player_season_stats_players_player_id",
                        column: x => x.player_id,
                        principalSchema: "squad",
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_player_season_stats_club_id",
                schema: "competition",
                table: "player_season_stats",
                column: "club_id");

            migrationBuilder.CreateIndex(
                name: "ix_player_season_stats_division_season_id_goals",
                schema: "competition",
                table: "player_season_stats",
                columns: new[] { "division_season_id", "goals" });

            migrationBuilder.CreateIndex(
                name: "IX_player_season_stats_player_id",
                schema: "competition",
                table: "player_season_stats",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ux_player_season_stats_division_season_id_player_id_club_id",
                schema: "competition",
                table: "player_season_stats",
                columns: new[] { "division_season_id", "player_id", "club_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "player_season_stats",
                schema: "competition");
        }
    }
}
