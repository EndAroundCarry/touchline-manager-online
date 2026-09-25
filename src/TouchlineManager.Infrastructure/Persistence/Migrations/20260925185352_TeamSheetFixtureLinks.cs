using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TeamSheetFixtureLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_player_unavailability_source_fixture_id",
                schema: "squad",
                table: "player_unavailability",
                column: "source_fixture_id");

            migrationBuilder.AddForeignKey(
                name: "FK_fixture_team_sheets_fixtures_fixture_id",
                schema: "squad",
                table: "fixture_team_sheets",
                column: "fixture_id",
                principalSchema: "competition",
                principalTable: "fixtures",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_player_unavailability_fixtures_source_fixture_id",
                schema: "squad",
                table: "player_unavailability",
                column: "source_fixture_id",
                principalSchema: "competition",
                principalTable: "fixtures",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_fixture_team_sheets_fixtures_fixture_id",
                schema: "squad",
                table: "fixture_team_sheets");

            migrationBuilder.DropForeignKey(
                name: "FK_player_unavailability_fixtures_source_fixture_id",
                schema: "squad",
                table: "player_unavailability");

            migrationBuilder.DropIndex(
                name: "IX_player_unavailability_source_fixture_id",
                schema: "squad",
                table: "player_unavailability");
        }
    }
}
