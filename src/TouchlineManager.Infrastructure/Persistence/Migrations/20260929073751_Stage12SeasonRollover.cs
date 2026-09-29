using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Stage12SeasonRollover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "season_rollovers",
                schema: "competition",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    world_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    next_season_id = table.Column<Guid>(type: "uuid", nullable: true),
                    phase = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_season_rollovers", x => x.id);
                    table.CheckConstraint("ck_season_rollovers_phase", "phase in ('started', 'frozen', 'finalized', 'moved', 'completed', 'failed')");
                    table.ForeignKey(
                        name: "FK_season_rollovers_game_worlds_world_id",
                        column: x => x.world_id,
                        principalSchema: "world",
                        principalTable: "game_worlds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_season_rollovers_seasons_next_season_id",
                        column: x => x.next_season_id,
                        principalSchema: "competition",
                        principalTable: "seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_season_rollovers_seasons_season_id",
                        column: x => x.season_id,
                        principalSchema: "competition",
                        principalTable: "seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_season_rollovers_next_season_id",
                schema: "competition",
                table: "season_rollovers",
                column: "next_season_id");

            migrationBuilder.CreateIndex(
                name: "ix_season_rollovers_phase",
                schema: "competition",
                table: "season_rollovers",
                column: "phase");

            migrationBuilder.CreateIndex(
                name: "IX_season_rollovers_season_id",
                schema: "competition",
                table: "season_rollovers",
                column: "season_id");

            migrationBuilder.CreateIndex(
                name: "ux_season_rollovers_world_id_season_id",
                schema: "competition",
                table: "season_rollovers",
                columns: new[] { "world_id", "season_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "season_rollovers",
                schema: "competition");
        }
    }
}
