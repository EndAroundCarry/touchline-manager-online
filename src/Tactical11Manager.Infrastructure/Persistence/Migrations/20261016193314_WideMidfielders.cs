using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tactical11Manager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WideMidfielders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_team_sheet_entries_role_override",
                schema: "squad",
                table: "team_sheet_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tactical_slots_role",
                schema: "squad",
                table: "tactical_slots");

            migrationBuilder.DropCheckConstraint(
                name: "ck_players_primary_position",
                schema: "squad",
                table: "players");

            migrationBuilder.DropCheckConstraint(
                name: "ck_player_training_focus_programme",
                schema: "squad",
                table: "player_training_focus");

            migrationBuilder.DropCheckConstraint(
                name: "ck_player_training_days_programme",
                schema: "squad",
                table: "player_training_days");

            migrationBuilder.AddCheckConstraint(
                name: "ck_team_sheet_entries_role_override",
                schema: "squad",
                table: "team_sheet_entries",
                sql: "role_override is null or role_override in ('goalkeeper', 'centre_back', 'full_back', 'wing_back', 'defensive_midfielder', 'central_midfielder', 'attacking_midfielder', 'winger', 'striker', 'wide_midfielder')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tactical_slots_role",
                schema: "squad",
                table: "tactical_slots",
                sql: "role in ('goalkeeper', 'centre_back', 'full_back', 'wing_back', 'defensive_midfielder', 'central_midfielder', 'attacking_midfielder', 'winger', 'striker', 'wide_midfielder')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_players_primary_position",
                schema: "squad",
                table: "players",
                sql: "primary_position in ('gk', 'rb', 'cb', 'lb', 'dm', 'cm', 'am', 'rw', 'lw', 'st', 'rm', 'lm')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_player_training_focus_programme",
                schema: "squad",
                table: "player_training_focus",
                sql: "programme in ('goalkeeper', 'defender', 'wingback', 'midfielder', 'winger', 'forward', 'mental', 'physical', 'recovery', 'widemid')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_player_training_days_programme",
                schema: "squad",
                table: "player_training_days",
                sql: "programme in ('goalkeeper', 'defender', 'wingback', 'midfielder', 'winger', 'forward', 'mental', 'physical', 'recovery', 'widemid')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_team_sheet_entries_role_override",
                schema: "squad",
                table: "team_sheet_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tactical_slots_role",
                schema: "squad",
                table: "tactical_slots");

            migrationBuilder.DropCheckConstraint(
                name: "ck_players_primary_position",
                schema: "squad",
                table: "players");

            migrationBuilder.DropCheckConstraint(
                name: "ck_player_training_focus_programme",
                schema: "squad",
                table: "player_training_focus");

            migrationBuilder.DropCheckConstraint(
                name: "ck_player_training_days_programme",
                schema: "squad",
                table: "player_training_days");

            migrationBuilder.AddCheckConstraint(
                name: "ck_team_sheet_entries_role_override",
                schema: "squad",
                table: "team_sheet_entries",
                sql: "role_override is null or role_override in ('goalkeeper', 'centre_back', 'full_back', 'wing_back', 'defensive_midfielder', 'central_midfielder', 'attacking_midfielder', 'winger', 'striker')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tactical_slots_role",
                schema: "squad",
                table: "tactical_slots",
                sql: "role in ('goalkeeper', 'centre_back', 'full_back', 'wing_back', 'defensive_midfielder', 'central_midfielder', 'attacking_midfielder', 'winger', 'striker')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_players_primary_position",
                schema: "squad",
                table: "players",
                sql: "primary_position in ('gk', 'rb', 'cb', 'lb', 'dm', 'cm', 'am', 'rw', 'lw', 'st')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_player_training_focus_programme",
                schema: "squad",
                table: "player_training_focus",
                sql: "programme in ('goalkeeper', 'defender', 'wingback', 'midfielder', 'winger', 'forward', 'mental', 'physical', 'recovery')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_player_training_days_programme",
                schema: "squad",
                table: "player_training_days",
                sql: "programme in ('goalkeeper', 'defender', 'wingback', 'midfielder', 'winger', 'forward', 'mental', 'physical', 'recovery')");
        }
    }
}
