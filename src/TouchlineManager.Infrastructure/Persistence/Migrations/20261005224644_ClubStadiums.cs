using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClubStadiums : Migration
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
                name: "ck_clubs_stadium_baseline",
                schema: "world",
                table: "clubs");

            migrationBuilder.DropColumn(
                name: "stadium_baseline",
                schema: "world",
                table: "clubs");

            migrationBuilder.AlterColumn<string>(
                name: "category",
                schema: "finance",
                table: "ledger_entries",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(19)",
                oldMaxLength: 19);

            migrationBuilder.AlterColumn<string>(
                name: "category",
                schema: "finance",
                table: "club_season_finance_lines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(19)",
                oldMaxLength: 19);

            migrationBuilder.CreateTable(
                name: "club_stadiums",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    standing_seats = table.Column<int>(type: "integer", nullable: false),
                    seating_seats = table.Column<int>(type: "integer", nullable: false),
                    covered_seats = table.Column<int>(type: "integer", nullable: false),
                    vip_seats = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_club_stadiums", x => x.id);
                    table.CheckConstraint("ck_club_stadiums_capacity", "standing_seats + seating_seats + covered_seats + vip_seats between 1 and 50000");
                    table.CheckConstraint("ck_club_stadiums_places", "standing_seats >= 0 and seating_seats >= 0 and covered_seats >= 0 and vip_seats >= 0");
                    table.ForeignKey(
                        name: "FK_club_stadiums_clubs_club_id",
                        column: x => x.club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Every club that already exists gets the opening ground (STAD-2), so gate revenue and the stadium
            // page never meet a club without one. New clubs are given theirs by world generation. The identity
            // is generated here because PostgreSQL 17 has no UUIDv7 function; it is still server-generated.
            migrationBuilder.Sql(
                "insert into world.club_stadiums "
                + "(id, club_id, standing_seats, seating_seats, covered_seats, vip_seats, created_at, updated_at, version) "
                + "select gen_random_uuid(), id, 3000, 1000, 900, 100, now(), now(), 1 from world.clubs;");

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

            migrationBuilder.CreateIndex(
                name: "ux_club_stadiums_club_id",
                schema: "world",
                table: "club_stadiums",
                column: "club_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "club_stadiums",
                schema: "world");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_entries_category",
                schema: "finance",
                table: "ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_entries_source_type",
                schema: "finance",
                table: "ledger_entries");

            migrationBuilder.AlterColumn<string>(
                name: "category",
                schema: "finance",
                table: "ledger_entries",
                type: "character varying(19)",
                maxLength: 19,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<long>(
                name: "stadium_baseline",
                schema: "world",
                table: "clubs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AlterColumn<string>(
                name: "category",
                schema: "finance",
                table: "club_season_finance_lines",
                type: "character varying(19)",
                maxLength: 19,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_entries_category",
                schema: "finance",
                table: "ledger_entries",
                sql: "category in ('opening_balance', 'gate_receipt', 'sponsorship', 'wages', 'operating_cost', 'position_award', 'transfer_payment', 'transfer_proceeds', 'bid_reservation', 'reservation_release', 'emergency_grant', 'compensation')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_entries_source_type",
                schema: "finance",
                table: "ledger_entries",
                sql: "source_type in ('world_seed', 'matchday', 'weekly_run', 'season_rollover', 'transfer', 'safety_job', 'admin_repair')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_clubs_stadium_baseline",
                schema: "world",
                table: "clubs",
                sql: "stadium_baseline >= 0");
        }
    }
}
