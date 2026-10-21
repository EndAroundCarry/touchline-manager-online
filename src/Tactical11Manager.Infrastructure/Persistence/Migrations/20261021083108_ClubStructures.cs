using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tactical11Manager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClubStructures : Migration
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

            migrationBuilder.DropCheckConstraint(
                name: "ck_club_season_finance_lines_category",
                schema: "finance",
                table: "club_season_finance_lines");

            migrationBuilder.CreateTable(
                name: "club_structure_takings",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    row_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    fixture_id = table.Column<Guid>(type: "uuid", nullable: true),
                    week_of = table.Column<DateOnly>(type: "date", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    customers = table.Column<int>(type: "integer", nullable: false),
                    base_minor = table.Column<long>(type: "bigint", nullable: false),
                    bonus_bp = table.Column<int>(type: "integer", nullable: false),
                    bonus_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    ledger_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_club_structure_takings", x => x.id);
                    table.CheckConstraint("ck_club_structure_takings_amounts", "customers >= 0 and base_minor >= 0 and bonus_bp >= 0");
                    table.CheckConstraint("ck_club_structure_takings_kind", "kind in ('snack_bar', 'merchandise')");
                    table.CheckConstraint("ck_club_structure_takings_level", "level between 1 and 10");
                    table.CheckConstraint("ck_club_structure_takings_match_sale_shape", "row_kind <> 'match_sale' or (kind = 'snack_bar' and fixture_id is not null and amount_minor >= 0)");
                    table.CheckConstraint("ck_club_structure_takings_row_kind", "row_kind in ('match_sale', 'week_sale', 'upkeep')");
                    table.CheckConstraint("ck_club_structure_takings_upkeep_shape", "row_kind <> 'upkeep' or (fixture_id is null and amount_minor <= 0)");
                    table.CheckConstraint("ck_club_structure_takings_week_sale_shape", "row_kind <> 'week_sale' or (kind = 'merchandise' and fixture_id is null and amount_minor >= 0)");
                    table.ForeignKey(
                        name: "FK_club_structure_takings_clubs_club_id",
                        column: x => x.club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_club_structure_takings_fixtures_fixture_id",
                        column: x => x.fixture_id,
                        principalSchema: "competition",
                        principalTable: "fixtures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_club_structure_takings_ledger_entries_ledger_entry_id",
                        column: x => x.ledger_entry_id,
                        principalSchema: "finance",
                        principalTable: "ledger_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "club_structures",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    upgrade_target_level = table.Column<int>(type: "integer", nullable: true),
                    upgrade_due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_club_structures", x => x.id);
                    table.CheckConstraint("ck_club_structures_kind", "kind in ('snack_bar', 'merchandise')");
                    table.CheckConstraint("ck_club_structures_level", "level between 1 and 10");
                    table.CheckConstraint("ck_club_structures_upgrade", "(upgrade_target_level is null and upgrade_due_at is null) or (upgrade_target_level = level + 1 and upgrade_due_at is not null)");
                    table.ForeignKey(
                        name: "FK_club_structures_clubs_club_id",
                        column: x => x.club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_entries_category",
                schema: "finance",
                table: "ledger_entries",
                sql: "category in ('opening_balance', 'gate_receipt', 'sponsorship', 'wages', 'operating_cost', 'position_award', 'transfer_payment', 'transfer_proceeds', 'bid_reservation', 'reservation_release', 'emergency_grant', 'compensation', 'stadium_construction', 'release_payout', 'snack_bar', 'merchandise', 'structure_upkeep', 'structure_upgrade')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_entries_source_type",
                schema: "finance",
                table: "ledger_entries",
                sql: "source_type in ('world_seed', 'matchday', 'weekly_run', 'season_rollover', 'transfer', 'safety_job', 'admin_repair', 'stadium', 'contract', 'structure')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_club_season_finance_lines_category",
                schema: "finance",
                table: "club_season_finance_lines",
                sql: "category in ('opening_balance', 'gate_receipt', 'sponsorship', 'wages', 'operating_cost', 'position_award', 'transfer_payment', 'transfer_proceeds', 'bid_reservation', 'reservation_release', 'emergency_grant', 'compensation', 'stadium_construction', 'release_payout', 'snack_bar', 'merchandise', 'structure_upkeep', 'structure_upgrade')");

            migrationBuilder.CreateIndex(
                name: "ix_club_structure_takings_club_kind_week",
                schema: "world",
                table: "club_structure_takings",
                columns: new[] { "club_id", "kind", "week_of" });

            migrationBuilder.CreateIndex(
                name: "IX_club_structure_takings_fixture_id",
                schema: "world",
                table: "club_structure_takings",
                column: "fixture_id");

            migrationBuilder.CreateIndex(
                name: "IX_club_structure_takings_ledger_entry_id",
                schema: "world",
                table: "club_structure_takings",
                column: "ledger_entry_id");

            migrationBuilder.CreateIndex(
                name: "ux_club_structure_takings_match_sale",
                schema: "world",
                table: "club_structure_takings",
                columns: new[] { "club_id", "fixture_id" },
                unique: true,
                filter: "row_kind = 'match_sale'");

            migrationBuilder.CreateIndex(
                name: "ux_club_structure_takings_weekly",
                schema: "world",
                table: "club_structure_takings",
                columns: new[] { "club_id", "kind", "row_kind", "week_of" },
                unique: true,
                filter: "row_kind <> 'match_sale'");

            migrationBuilder.CreateIndex(
                name: "ux_club_structures_club_id_kind",
                schema: "world",
                table: "club_structures",
                columns: new[] { "club_id", "kind" },
                unique: true);

            // Every club that already exists gets both buildings at level 1, free, as a newly generated club
            // does (STRC-1).
            migrationBuilder.Sql(
                "insert into world.club_structures (id, club_id, kind, level, created_at, updated_at, version) "
                + "select gen_random_uuid(), club.id, kind.code, 1, now(), now(), 1 "
                + "from world.clubs as club "
                + "cross join (values ('snack_bar'), ('merchandise')) as kind(code);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "club_structure_takings",
                schema: "world");

            migrationBuilder.DropTable(
                name: "club_structures",
                schema: "world");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_entries_category",
                schema: "finance",
                table: "ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_entries_source_type",
                schema: "finance",
                table: "ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_club_season_finance_lines_category",
                schema: "finance",
                table: "club_season_finance_lines");

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

            migrationBuilder.AddCheckConstraint(
                name: "ck_club_season_finance_lines_category",
                schema: "finance",
                table: "club_season_finance_lines",
                sql: "category in ('opening_balance', 'gate_receipt', 'sponsorship', 'wages', 'operating_cost', 'position_award', 'transfer_payment', 'transfer_proceeds', 'bid_reservation', 'reservation_release', 'emergency_grant', 'compensation')");
        }
    }
}
