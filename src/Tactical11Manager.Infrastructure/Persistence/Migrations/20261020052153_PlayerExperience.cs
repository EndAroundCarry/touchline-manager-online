using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tactical11Manager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlayerExperience : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "experience_bp",
                schema: "squad",
                table: "player_state",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "ck_player_state_experience_range",
                schema: "squad",
                table: "player_state",
                sql: "experience_bp between 0 and 10000");

            // Backfill (expand step, MIG-3): players who already exist start with what their age gives a new
            // player, measured at the world's latest season: 4 points a year from 18, at most 50 points. The
            // numbers are PlayerExperience's StartingAge, PerYearOfAgeBasisPoints and StartingCapBasisPoints.
            migrationBuilder.Sql(
                """
                UPDATE squad.player_state AS state
                SET experience_bp = LEAST(5000, GREATEST(0,
                    (season.game_year - player.birth_game_year - 18) * 400))
                FROM squad.players AS player
                JOIN LATERAL (
                    SELECT s.game_year
                    FROM competition.seasons AS s
                    WHERE s.world_id = player.world_id
                    ORDER BY s.sequence_number DESC
                    LIMIT 1
                ) AS season ON TRUE
                WHERE player.id = state.player_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_player_state_experience_range",
                schema: "squad",
                table: "player_state");

            migrationBuilder.DropColumn(
                name: "experience_bp",
                schema: "squad",
                table: "player_state");
        }
    }
}
