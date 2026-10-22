using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tactical11Manager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SixteenClubsThirtyMatchdays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_divisions_capacity",
                schema: "competition",
                table: "divisions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_divisions_capacity",
                schema: "competition",
                table: "divisions",
                sql: "capacity in (16, 18)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_divisions_capacity",
                schema: "competition",
                table: "divisions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_divisions_capacity",
                schema: "competition",
                table: "divisions",
                sql: "capacity = 18");
        }
    }
}
