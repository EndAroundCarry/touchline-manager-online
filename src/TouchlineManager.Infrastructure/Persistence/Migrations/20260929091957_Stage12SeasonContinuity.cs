using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Stage12SeasonContinuity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_season_rollovers_phase",
                schema: "competition",
                table: "season_rollovers");

            migrationBuilder.AddColumn<int>(
                name: "retirement_announced_season_number",
                schema: "squad",
                table: "players",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "club_season_finances",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opening_cash_minor = table.Column<long>(type: "bigint", nullable: false),
                    closing_cash_minor = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_club_season_finances", x => x.id);
                    table.CheckConstraint("ck_club_season_finances_cash", "opening_cash_minor >= 0 and closing_cash_minor >= 0");
                    table.ForeignKey(
                        name: "FK_club_season_finances_clubs_club_id",
                        column: x => x.club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_club_season_finances_seasons_season_id",
                        column: x => x.season_id,
                        principalSchema: "competition",
                        principalTable: "seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "club_season_finance_lines",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    club_season_finance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(19)", maxLength: 19, nullable: false),
                    cash_delta_minor = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_club_season_finance_lines", x => x.id);
                    table.CheckConstraint("ck_club_season_finance_lines_category", "category in ('opening_balance', 'gate_receipt', 'sponsorship', 'wages', 'operating_cost', 'position_award', 'transfer_payment', 'transfer_proceeds', 'bid_reservation', 'reservation_release', 'emergency_grant', 'compensation')");
                    table.ForeignKey(
                        name: "FK_club_season_finance_lines_club_season_finances_club_season_~",
                        column: x => x.club_season_finance_id,
                        principalSchema: "finance",
                        principalTable: "club_season_finances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_season_rollovers_phase",
                schema: "competition",
                table: "season_rollovers",
                sql: "phase in ('started', 'frozen', 'finalized', 'squads', 'moved', 'completed', 'failed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_players_retirement_announced",
                schema: "squad",
                table: "players",
                sql: "retirement_announced_season_number is null or retirement_announced_season_number >= 1");

            migrationBuilder.CreateIndex(
                name: "ux_club_season_finance_lines_summary_category",
                schema: "finance",
                table: "club_season_finance_lines",
                columns: new[] { "club_season_finance_id", "category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_club_season_finances_season_id",
                schema: "finance",
                table: "club_season_finances",
                column: "season_id");

            migrationBuilder.CreateIndex(
                name: "ux_club_season_finances_club_id_season_id",
                schema: "finance",
                table: "club_season_finances",
                columns: new[] { "club_id", "season_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "club_season_finance_lines",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "club_season_finances",
                schema: "finance");

            migrationBuilder.DropCheckConstraint(
                name: "ck_season_rollovers_phase",
                schema: "competition",
                table: "season_rollovers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_players_retirement_announced",
                schema: "squad",
                table: "players");

            migrationBuilder.DropColumn(
                name: "retirement_announced_season_number",
                schema: "squad",
                table: "players");

            migrationBuilder.AddCheckConstraint(
                name: "ck_season_rollovers_phase",
                schema: "competition",
                table: "season_rollovers",
                sql: "phase in ('started', 'frozen', 'finalized', 'moved', 'completed', 'failed')");
        }
    }
}
