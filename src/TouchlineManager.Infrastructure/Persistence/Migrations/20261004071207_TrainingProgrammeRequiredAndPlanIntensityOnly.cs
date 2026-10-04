using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Expand step for the training rework (<c>MIG-3</c>): a plan holds only an intensity and a player's override
    /// only a programme, so the retired <c>team_focus</c> and <c>focus_family</c> columns stop being required.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Forward validation: after the migration no <c>squad.player_training_focus</c> row has a null
    /// <c>programme</c>, and <c>squad.training_plans</c> accepts rows without a <c>team_focus</c>. Existing
    /// <c>team_focus</c> and <c>focus_family</c> values are kept, unread, until the contract migration drops
    /// both columns.
    /// </para>
    /// <para>
    /// Rollback: <c>Down</c> restores the NOT NULL on <c>team_focus</c> and the check constraints, giving any
    /// plan written since a <c>balanced</c> focus first. The override rows deleted by <c>Up</c> are not
    /// restored; they were attribute-family focuses with no programme counterpart, and a player with no row
    /// trains the position default, so nothing observable is lost.
    /// </para>
    /// </remarks>
    public partial class TrainingProgrammeRequiredAndPlanIntensityOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A technical family focus has no programme counterpart (the previous migration left its
            // programme null). Dropping the row returns the player to the position default, which is what a
            // missing override means, and lets the column become required.
            migrationBuilder.Sql("DELETE FROM squad.player_training_focus WHERE programme IS NULL;");

            migrationBuilder.DropCheckConstraint(
                name: "ck_training_plans_focus",
                schema: "squad",
                table: "training_plans");

            migrationBuilder.DropCheckConstraint(
                name: "ck_player_training_focus_family",
                schema: "squad",
                table: "player_training_focus");

            migrationBuilder.AlterColumn<string>(
                name: "team_focus",
                schema: "squad",
                table: "training_plans",
                type: "character varying(9)",
                maxLength: 9,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(9)",
                oldMaxLength: 9);

            migrationBuilder.AlterColumn<string>(
                name: "programme",
                schema: "squad",
                table: "player_training_focus",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Plans written after Up carry no team focus; give them the neutral one the check accepts.
            migrationBuilder.Sql("UPDATE squad.training_plans SET team_focus = 'balanced' WHERE team_focus IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "team_focus",
                schema: "squad",
                table: "training_plans",
                type: "character varying(9)",
                maxLength: 9,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(9)",
                oldMaxLength: 9,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "programme",
                schema: "squad",
                table: "player_training_focus",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10);

            migrationBuilder.AddCheckConstraint(
                name: "ck_training_plans_focus",
                schema: "squad",
                table: "training_plans",
                sql: "team_focus in ('balanced', 'recovery', 'fitness', 'attacking', 'defending', 'technical', 'tactical')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_player_training_focus_family",
                schema: "squad",
                table: "player_training_focus",
                sql: "focus_family in ('technical', 'mental', 'physical', 'goalkeeping')");
        }
    }
}
