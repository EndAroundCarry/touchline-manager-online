using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tactical11Manager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MatchdayMoneyAndAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_club_tenures_end_reason_value",
                schema: "world",
                table: "club_tenures");

            migrationBuilder.AddColumn<bool>(
                name: "listed_by_board",
                schema: "market",
                table: "transfer_listings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_club_tenures_end_reason_value",
                schema: "world",
                table: "club_tenures",
                sql: "end_reason is null or end_reason in ('resigned', 'inactivity_closed', 'administrator_closed', 'dismissed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_club_tenures_end_reason_value",
                schema: "world",
                table: "club_tenures");

            migrationBuilder.DropColumn(
                name: "listed_by_board",
                schema: "market",
                table: "transfer_listings");

            migrationBuilder.AddCheckConstraint(
                name: "ck_club_tenures_end_reason_value",
                schema: "world",
                table: "club_tenures",
                sql: "end_reason is null or end_reason in ('resigned', 'inactivity_closed', 'administrator_closed')");
        }
    }
}
