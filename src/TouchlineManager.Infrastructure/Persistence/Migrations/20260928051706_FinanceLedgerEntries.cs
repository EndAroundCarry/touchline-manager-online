using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinanceLedgerEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ledger_entries",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    category = table.Column<string>(type: "character varying(19)", maxLength: 19, nullable: false),
                    cash_delta_minor = table.Column<long>(type: "bigint", nullable: false),
                    reserved_delta_minor = table.Column<long>(type: "bigint", nullable: false),
                    resulting_cash_minor = table.Column<long>(type: "bigint", nullable: false),
                    resulting_reserved_minor = table.Column<long>(type: "bigint", nullable: false),
                    source_type = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description_template = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    description_parameters = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_ledger_entries_balances", "resulting_cash_minor >= 0 and resulting_reserved_minor >= 0 and resulting_reserved_minor <= resulting_cash_minor");
                    table.CheckConstraint("ck_ledger_entries_category", "category in ('opening_balance', 'gate_receipt', 'sponsorship', 'wages', 'operating_cost', 'position_award', 'transfer_payment', 'transfer_proceeds', 'bid_reservation', 'reservation_release', 'emergency_grant', 'compensation')");
                    table.CheckConstraint("ck_ledger_entries_correlation_id", "length(correlation_id) > 0");
                    table.CheckConstraint("ck_ledger_entries_description_template", "length(description_template) > 0");
                    table.CheckConstraint("ck_ledger_entries_moves", "cash_delta_minor <> 0 or reserved_delta_minor <> 0");
                    table.CheckConstraint("ck_ledger_entries_sequence", "sequence >= 1");
                    table.CheckConstraint("ck_ledger_entries_source_type", "source_type in ('world_seed', 'matchday', 'weekly_run', 'season_rollover', 'transfer', 'safety_job', 'admin_repair')");
                    table.ForeignKey(
                        name: "FK_ledger_entries_clubs_club_id",
                        column: x => x.club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_source",
                schema: "finance",
                table: "ledger_entries",
                columns: new[] { "source_type", "source_id" });

            migrationBuilder.CreateIndex(
                name: "ux_ledger_entries_club_sequence",
                schema: "finance",
                table: "ledger_entries",
                columns: new[] { "club_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_ledger_entries_correlation_category",
                schema: "finance",
                table: "ledger_entries",
                columns: new[] { "correlation_id", "category" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ledger_entries",
                schema: "finance");
        }
    }
}
