using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlayerMatchStatsTacklesCrossesSetPieces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_player_season_stats_counts",
                schema: "competition",
                table: "player_season_stats");

            migrationBuilder.AddColumn<int>(
                name: "crosses_attempted",
                schema: "competition",
                table: "player_season_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "crosses_completed",
                schema: "competition",
                table: "player_season_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "free_kicks_scored",
                schema: "competition",
                table: "player_season_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "free_kicks_taken",
                schema: "competition",
                table: "player_season_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "penalties_faced",
                schema: "competition",
                table: "player_season_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "penalties_saved",
                schema: "competition",
                table: "player_season_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "penalties_scored",
                schema: "competition",
                table: "player_season_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "penalties_taken",
                schema: "competition",
                table: "player_season_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "shots_on_target_faced",
                schema: "competition",
                table: "player_season_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "tackles_attempted",
                schema: "competition",
                table: "player_season_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "tackles_won",
                schema: "competition",
                table: "player_season_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Rows written before the line counted shots faced hold saves with none faced, which the new
            // constraint (saves <= shots on target faced) would refuse; a goalkeeper faced at least as many shots as they saved.
            migrationBuilder.Sql(
                "update competition.player_season_stats set shots_on_target_faced = saves where saves > shots_on_target_faced");

            migrationBuilder.AddCheckConstraint(
                name: "ck_player_season_stats_counts",
                schema: "competition",
                table: "player_season_stats",
                sql: "appearances >= 0 and starts >= 0 and starts <= appearances and minutes_played >= 0 and goals >= 0 and assists >= 0 and shots >= 0 and shots_on_target >= 0 and shots_on_target <= shots and saves >= 0 and yellow_cards >= 0 and red_cards >= 0 and rating_basis_points_total >= 0 and rated_appearances >= 0 and passes_attempted >= 0 and passes_completed >= 0 and passes_completed <= passes_attempted and dribbles_attempted >= 0 and dribbles_completed >= 0 and dribbles_completed <= dribbles_attempted and tackles_attempted >= 0 and tackles_won >= 0 and tackles_won <= tackles_attempted and crosses_attempted >= 0 and crosses_completed >= 0 and crosses_completed <= crosses_attempted and penalties_taken >= 0 and penalties_scored >= 0 and penalties_scored <= penalties_taken and penalties_faced >= 0 and penalties_saved >= 0 and penalties_saved <= penalties_faced and free_kicks_taken >= 0 and free_kicks_scored >= 0 and free_kicks_scored <= free_kicks_taken and shots_on_target_faced >= 0 and saves <= shots_on_target_faced");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_player_season_stats_counts",
                schema: "competition",
                table: "player_season_stats");

            migrationBuilder.DropColumn(
                name: "crosses_attempted",
                schema: "competition",
                table: "player_season_stats");

            migrationBuilder.DropColumn(
                name: "crosses_completed",
                schema: "competition",
                table: "player_season_stats");

            migrationBuilder.DropColumn(
                name: "free_kicks_scored",
                schema: "competition",
                table: "player_season_stats");

            migrationBuilder.DropColumn(
                name: "free_kicks_taken",
                schema: "competition",
                table: "player_season_stats");

            migrationBuilder.DropColumn(
                name: "penalties_faced",
                schema: "competition",
                table: "player_season_stats");

            migrationBuilder.DropColumn(
                name: "penalties_saved",
                schema: "competition",
                table: "player_season_stats");

            migrationBuilder.DropColumn(
                name: "penalties_scored",
                schema: "competition",
                table: "player_season_stats");

            migrationBuilder.DropColumn(
                name: "penalties_taken",
                schema: "competition",
                table: "player_season_stats");

            migrationBuilder.DropColumn(
                name: "shots_on_target_faced",
                schema: "competition",
                table: "player_season_stats");

            migrationBuilder.DropColumn(
                name: "tackles_attempted",
                schema: "competition",
                table: "player_season_stats");

            migrationBuilder.DropColumn(
                name: "tackles_won",
                schema: "competition",
                table: "player_season_stats");

            migrationBuilder.AddCheckConstraint(
                name: "ck_player_season_stats_counts",
                schema: "competition",
                table: "player_season_stats",
                sql: "appearances >= 0 and starts >= 0 and starts <= appearances and minutes_played >= 0 and goals >= 0 and assists >= 0 and shots >= 0 and shots_on_target >= 0 and shots_on_target <= shots and saves >= 0 and yellow_cards >= 0 and red_cards >= 0 and rating_basis_points_total >= 0 and rated_appearances >= 0 and passes_attempted >= 0 and passes_completed >= 0 and passes_completed <= passes_attempted and dribbles_attempted >= 0 and dribbles_completed >= 0 and dribbles_completed <= dribbles_attempted");
        }
    }
}
