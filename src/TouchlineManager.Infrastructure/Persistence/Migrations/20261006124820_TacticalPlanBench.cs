using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TacticalPlanBench : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tactical_bench_slots",
                schema: "squad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slot_number = table.Column<short>(type: "smallint", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tactical_bench_slots", x => x.id);
                    table.CheckConstraint("ck_tactical_bench_slots_number", "slot_number between 12 and 18");
                    table.ForeignKey(
                        name: "FK_tactical_bench_slots_players_player_id",
                        column: x => x.player_id,
                        principalSchema: "squad",
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tactical_bench_slots_tactical_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "squad",
                        principalTable: "tactical_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tactical_bench_slots_player_id",
                schema: "squad",
                table: "tactical_bench_slots",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ux_tactical_bench_slots_plan_slot",
                schema: "squad",
                table: "tactical_bench_slots",
                columns: new[] { "plan_id", "slot_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tactical_bench_slots",
                schema: "squad");
        }
    }
}
