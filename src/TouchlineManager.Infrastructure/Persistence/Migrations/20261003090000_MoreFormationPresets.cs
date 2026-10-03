using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoreFormationPresets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tactical_plans_formation",
                schema: "squad",
                table: "tactical_plans");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tactical_plans_formation",
                schema: "squad",
                table: "tactical_plans",
                sql: "formation_preset in ('4-4-2', '4-3-3', '4-2-3-1', '4-1-4-1', '3-5-2', '5-3-2', '4-4-1-1', '4-5-1', '4-3-2-1', '4-2-2-2', '3-4-3', '3-4-2-1', '5-4-1')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tactical_plans_formation",
                schema: "squad",
                table: "tactical_plans");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tactical_plans_formation",
                schema: "squad",
                table: "tactical_plans",
                sql: "formation_preset in ('4-4-2', '4-3-3', '4-2-3-1', '4-1-4-1', '3-5-2', '5-3-2')");
        }
    }
}
