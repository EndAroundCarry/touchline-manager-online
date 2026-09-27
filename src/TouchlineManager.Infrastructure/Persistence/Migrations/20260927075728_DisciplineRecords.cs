using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DisciplineRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "discipline_records",
                schema: "competition",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    division_season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    yellow_cards = table.Column<int>(type: "integer", nullable: false),
                    red_cards = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_discipline_records", x => x.id);
                    table.CheckConstraint("ck_discipline_records_cards", "yellow_cards >= 0 and red_cards >= 0");
                    table.ForeignKey(
                        name: "FK_discipline_records_division_seasons_division_season_id",
                        column: x => x.division_season_id,
                        principalSchema: "competition",
                        principalTable: "division_seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_discipline_records_players_player_id",
                        column: x => x.player_id,
                        principalSchema: "squad",
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_discipline_records_player_id",
                schema: "competition",
                table: "discipline_records",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ux_discipline_records_division_season_id_player_id",
                schema: "competition",
                table: "discipline_records",
                columns: new[] { "division_season_id", "player_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "discipline_records",
                schema: "competition");
        }
    }
}
