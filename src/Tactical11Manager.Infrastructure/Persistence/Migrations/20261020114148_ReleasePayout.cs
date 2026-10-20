using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tactical11Manager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReleasePayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_entries_category",
                schema: "finance",
                table: "ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_entries_source_type",
                schema: "finance",
                table: "ledger_entries");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_entries_category",
                schema: "finance",
                table: "ledger_entries",
                sql: "category in ('opening_balance', 'gate_receipt', 'sponsorship', 'wages', 'operating_cost', 'position_award', 'transfer_payment', 'transfer_proceeds', 'bid_reservation', 'reservation_release', 'emergency_grant', 'compensation', 'stadium_construction', 'release_payout')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_entries_source_type",
                schema: "finance",
                table: "ledger_entries",
                sql: "source_type in ('world_seed', 'matchday', 'weekly_run', 'season_rollover', 'transfer', 'safety_job', 'admin_repair', 'stadium', 'contract')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_entries_category",
                schema: "finance",
                table: "ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_entries_source_type",
                schema: "finance",
                table: "ledger_entries");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_entries_category",
                schema: "finance",
                table: "ledger_entries",
                sql: "category in ('opening_balance', 'gate_receipt', 'sponsorship', 'wages', 'operating_cost', 'position_award', 'transfer_payment', 'transfer_proceeds', 'bid_reservation', 'reservation_release', 'emergency_grant', 'compensation', 'stadium_construction')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_entries_source_type",
                schema: "finance",
                table: "ledger_entries",
                sql: "source_type in ('world_seed', 'matchday', 'weekly_run', 'season_rollover', 'transfer', 'safety_job', 'admin_repair', 'stadium')");
        }
    }
}
