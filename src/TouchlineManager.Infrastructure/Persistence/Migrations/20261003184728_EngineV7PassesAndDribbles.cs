using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EngineV7PassesAndDribbles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_player_season_stats_counts",
                schema: "competition",
                table: "player_season_stats");

            migrationBuilder.AddColumn<int>(
                name: "dribbles_attempted",
                schema: "competition",
                table: "player_season_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "dribbles_completed",
                schema: "competition",
                table: "player_season_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "passes_attempted",
                schema: "competition",
                table: "player_season_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "passes_completed",
                schema: "competition",
                table: "player_season_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "ck_player_season_stats_counts",
                schema: "competition",
                table: "player_season_stats",
                sql: "appearances >= 0 and starts >= 0 and starts <= appearances and minutes_played >= 0 and goals >= 0 and assists >= 0 and shots >= 0 and shots_on_target >= 0 and shots_on_target <= shots and saves >= 0 and yellow_cards >= 0 and red_cards >= 0 and rating_basis_points_total >= 0 and rated_appearances >= 0 and passes_attempted >= 0 and passes_completed >= 0 and passes_completed <= passes_attempted and dribbles_attempted >= 0 and dribbles_completed >= 0 and dribbles_completed <= dribbles_attempted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_player_season_stats_counts",
                schema: "competition",
                table: "player_season_stats");

            migrationBuilder.DropColumn(
                name: "dribbles_attempted",
                schema: "competition",
                table: "player_season_stats");

            migrationBuilder.DropColumn(
                name: "dribbles_completed",
                schema: "competition",
                table: "player_season_stats");

            migrationBuilder.DropColumn(
                name: "passes_attempted",
                schema: "competition",
                table: "player_season_stats");

            migrationBuilder.DropColumn(
                name: "passes_completed",
                schema: "competition",
                table: "player_season_stats");

            migrationBuilder.AddCheckConstraint(
                name: "ck_player_season_stats_counts",
                schema: "competition",
                table: "player_season_stats",
                sql: "appearances >= 0 and starts >= 0 and starts <= appearances and minutes_played >= 0 and goals >= 0 and assists >= 0 and shots >= 0 and shots_on_target >= 0 and shots_on_target <= shots and saves >= 0 and yellow_cards >= 0 and red_cards >= 0 and rating_basis_points_total >= 0 and rated_appearances >= 0");
        }
    }
}
