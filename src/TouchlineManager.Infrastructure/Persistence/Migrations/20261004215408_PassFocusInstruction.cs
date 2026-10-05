using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PassFocusInstruction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pass_focus",
                schema: "squad",
                table: "tactical_plans",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "balanced");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tactical_plans_pass_focus",
                schema: "squad",
                table: "tactical_plans",
                sql: "pass_focus in ('balanced', 'centre', 'centre_left', 'centre_right', 'wings')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tactical_plans_pass_focus",
                schema: "squad",
                table: "tactical_plans");

            migrationBuilder.DropColumn(
                name: "pass_focus",
                schema: "squad",
                table: "tactical_plans");
        }
    }
}
