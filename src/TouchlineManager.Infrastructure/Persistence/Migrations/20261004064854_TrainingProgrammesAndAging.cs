using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrainingProgrammesAndAging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "focus_family",
                schema: "squad",
                table: "player_training_focus",
                type: "character varying(11)",
                maxLength: 11,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(11)",
                oldMaxLength: 11);

            migrationBuilder.AddColumn<string>(
                name: "programme",
                schema: "squad",
                table: "player_training_focus",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            // Backfill (expand step, MIG-3): carry each legacy family focus onto its nearest programme. A
            // technical focus has no programme counterpart, so its row keeps a null programme and the player
            // trains the position default; nothing is deleted here, and the contract step removes those rows
            // together with the retired columns.
            migrationBuilder.Sql(
                """
                UPDATE squad.player_training_focus
                SET programme = CASE focus_family
                    WHEN 'mental' THEN 'mental'
                    WHEN 'physical' THEN 'physical'
                    WHEN 'goalkeeping' THEN 'goalkeeper'
                END
                WHERE programme IS NULL;
                """);

            migrationBuilder.AddColumn<int>(
                name: "decline_remainder",
                schema: "squad",
                table: "player_state",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "player_training_days",
                schema: "squad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    programme = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    intensity = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    development_milli = table.Column<int>(type: "integer", nullable: false),
                    decline_milli = table.Column<int>(type: "integer", nullable: false),
                    points_gained = table.Column<int>(type: "integer", nullable: false),
                    points_lost = table.Column<int>(type: "integer", nullable: false),
                    attribute_changes = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_training_days", x => x.id);
                    table.CheckConstraint("ck_player_training_days_amounts", "development_milli >= 0 and decline_milli >= 0 and points_gained >= 0 and points_lost >= 0");
                    table.CheckConstraint("ck_player_training_days_intensity", "intensity in ('light', 'normal', 'intense')");
                    table.CheckConstraint("ck_player_training_days_programme", "programme in ('goalkeeper', 'defender', 'wingback', 'midfielder', 'winger', 'forward', 'mental', 'physical', 'recovery')");
                    table.ForeignKey(
                        name: "FK_player_training_days_players_player_id",
                        column: x => x.player_id,
                        principalSchema: "squad",
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_player_training_focus_programme",
                schema: "squad",
                table: "player_training_focus",
                sql: "programme in ('goalkeeper', 'defender', 'wingback', 'midfielder', 'winger', 'forward', 'mental', 'physical', 'recovery')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_player_state_decline_remainder",
                schema: "squad",
                table: "player_state",
                sql: "decline_remainder >= 0");

            migrationBuilder.CreateIndex(
                name: "ux_player_training_days_player_day",
                schema: "squad",
                table: "player_training_days",
                columns: new[] { "player_id", "day" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rows written after this migration may carry only a programme. Give each one the nearest legacy
            // family before the column is made required again; every non-family programme maps to technical.
            migrationBuilder.Sql(
                """
                UPDATE squad.player_training_focus
                SET focus_family = CASE programme
                    WHEN 'mental' THEN 'mental'
                    WHEN 'physical' THEN 'physical'
                    WHEN 'goalkeeper' THEN 'goalkeeping'
                    ELSE 'technical'
                END
                WHERE focus_family IS NULL;
                """);

            migrationBuilder.DropTable(
                name: "player_training_days",
                schema: "squad");

            migrationBuilder.DropCheckConstraint(
                name: "ck_player_training_focus_programme",
                schema: "squad",
                table: "player_training_focus");

            migrationBuilder.DropCheckConstraint(
                name: "ck_player_state_decline_remainder",
                schema: "squad",
                table: "player_state");

            migrationBuilder.DropColumn(
                name: "programme",
                schema: "squad",
                table: "player_training_focus");

            migrationBuilder.DropColumn(
                name: "decline_remainder",
                schema: "squad",
                table: "player_state");

            migrationBuilder.AlterColumn<string>(
                name: "focus_family",
                schema: "squad",
                table: "player_training_focus",
                type: "character varying(11)",
                maxLength: 11,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(11)",
                oldMaxLength: 11,
                oldNullable: true);
        }
    }
}
