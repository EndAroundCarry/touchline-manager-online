using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tactical11Manager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RaiseStadiumCapacityTo75000 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_club_stadiums_capacity",
                schema: "world",
                table: "club_stadiums");

            migrationBuilder.AddCheckConstraint(
                name: "ck_club_stadiums_capacity",
                schema: "world",
                table: "club_stadiums",
                sql: "standing_seats + seating_seats + covered_seats + vip_seats between 1 and 75000");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_club_stadiums_capacity",
                schema: "world",
                table: "club_stadiums");

            migrationBuilder.AddCheckConstraint(
                name: "ck_club_stadiums_capacity",
                schema: "world",
                table: "club_stadiums",
                sql: "standing_seats + seating_seats + covered_seats + vip_seats between 1 and 50000");
        }
    }
}
