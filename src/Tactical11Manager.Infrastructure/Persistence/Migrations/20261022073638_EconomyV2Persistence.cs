using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tactical11Manager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EconomyV2Persistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_transfer_listings_buyer_wage",
                schema: "market",
                table: "transfer_listings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_player_contracts_wage",
                schema: "squad",
                table: "player_contracts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_entries_balances",
                schema: "finance",
                table: "ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_entries_category",
                schema: "finance",
                table: "ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_club_season_finances_cash",
                schema: "finance",
                table: "club_season_finances");

            migrationBuilder.DropCheckConstraint(
                name: "ck_club_season_finance_lines_category",
                schema: "finance",
                table: "club_season_finance_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_club_accounts_cash_nonnegative",
                schema: "finance",
                table: "club_accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_club_accounts_reserved_within_cash",
                schema: "finance",
                table: "club_accounts");

            migrationBuilder.RenameColumn(
                name: "generated_buyer_wage_minor",
                schema: "market",
                table: "transfer_listings",
                newName: "generated_buyer_season_wage_minor");

            migrationBuilder.RenameColumn(
                name: "weekly_wage_minor",
                schema: "squad",
                table: "player_contracts",
                newName: "season_wage_minor");

            // A weekly run settled three matchdays and a season is 34, so a season wage is the weekly wage times 34/3 (plan toasty-scott, M2).
            // Every contract is converted, closed ones too, so a closed contract still reads in the units of the column.
            migrationBuilder.Sql(
                "update squad.player_contracts set season_wage_minor = season_wage_minor * 34 / 3;");
            migrationBuilder.Sql(
                "update market.transfer_listings set generated_buyer_season_wage_minor = generated_buyer_season_wage_minor * 34 / 3;");

            migrationBuilder.AddColumn<int>(
                name: "standing_centis",
                schema: "world",
                table: "clubs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // The standing was kept in whole points as the club reputation; it opens in hundredths at the same value.
            migrationBuilder.Sql("update world.clubs set standing_centis = reputation * 100;");

            migrationBuilder.AddColumn<string>(
                name: "financial_state",
                schema: "finance",
                table: "club_accounts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "healthy");

            migrationBuilder.AddColumn<long>(
                name: "overdraft_floor_minor",
                schema: "finance",
                table: "club_accounts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "overdraft_limit_minor",
                schema: "finance",
                table: "club_accounts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "state_since_matchday",
                schema: "finance",
                table: "club_accounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "club_standing_events",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    finished_tier = table.Column<int>(type: "integer", nullable: false),
                    next_tier = table.Column<int>(type: "integer", nullable: false),
                    final_place = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    before_centis = table.Column<int>(type: "integer", nullable: false),
                    after_centis = table.Column<int>(type: "integer", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_club_standing_events", x => x.id);
                    table.CheckConstraint("ck_club_standing_events_outcome", "outcome is null or outcome in ('missed', 'met', 'exceeded')");
                    table.CheckConstraint("ck_club_standing_events_place", "final_place between 1 and 18");
                    table.CheckConstraint("ck_club_standing_events_standing", "before_centis between 100 and 10000 and after_centis between 100 and 10000");
                    table.CheckConstraint("ck_club_standing_events_tiers", "finished_tier >= 1 and next_tier >= 1");
                    table.ForeignKey(
                        name: "FK_club_standing_events_clubs_club_id",
                        column: x => x.club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_club_standing_events_seasons_season_id",
                        column: x => x.season_id,
                        principalSchema: "competition",
                        principalTable: "seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sponsor_offers",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    sponsor_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    fixed_season_minor = table.Column<long>(type: "bigint", nullable: false),
                    bonus_multiplier_bp = table.Column<int>(type: "integer", nullable: false),
                    tier = table.Column<int>(type: "integer", nullable: false),
                    fans = table.Column<int>(type: "integer", nullable: false),
                    standing_points = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sponsor_offers", x => x.id);
                    table.CheckConstraint("ck_sponsor_offers_amounts", "fixed_season_minor >= 0 and bonus_multiplier_bp > 0");
                    table.CheckConstraint("ck_sponsor_offers_basis", "tier >= 1 and fans >= 0 and standing_points between 1 and 100");
                    table.CheckConstraint("ck_sponsor_offers_kind", "kind in ('steady', 'balanced', 'performance')");
                    table.CheckConstraint("ck_sponsor_offers_name", "length(sponsor_name) > 0");
                    table.ForeignKey(
                        name: "FK_sponsor_offers_clubs_club_id",
                        column: x => x.club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sponsor_offers_seasons_season_id",
                        column: x => x.season_id,
                        principalSchema: "competition",
                        principalTable: "seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sponsor_deals",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    objective = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    target_place = table.Column<int>(type: "integer", nullable: false),
                    fixed_matchday_minor = table.Column<long>(type: "bigint", nullable: false),
                    bonus_met_minor = table.Column<long>(type: "bigint", nullable: false),
                    bonus_exceeded_minor = table.Column<long>(type: "bigint", nullable: false),
                    signed_by = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    signed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    bonus_paid_minor = table.Column<long>(type: "bigint", nullable: true),
                    final_place = table.Column<int>(type: "integer", nullable: true),
                    settled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sponsor_deals", x => x.id);
                    table.CheckConstraint("ck_sponsor_deals_amounts", "fixed_matchday_minor >= 0 and bonus_met_minor >= 0 and bonus_exceeded_minor >= bonus_met_minor");
                    table.CheckConstraint("ck_sponsor_deals_kind", "kind in ('steady', 'balanced', 'performance')");
                    table.CheckConstraint("ck_sponsor_deals_objective", "objective in ('win_league', 'top_three', 'top_six', 'top_half', 'twelfth', 'stay_up')");
                    table.CheckConstraint("ck_sponsor_deals_outcome", "outcome is null or outcome in ('missed', 'met', 'exceeded')");
                    table.CheckConstraint("ck_sponsor_deals_settled_shape", "(outcome is null and bonus_paid_minor is null and final_place is null and settled_at is null) or (outcome is not null and bonus_paid_minor is not null and bonus_paid_minor >= 0 and final_place >= 1 and settled_at is not null)");
                    table.CheckConstraint("ck_sponsor_deals_signed_by", "signed_by in ('manager', 'board', 'ai')");
                    table.CheckConstraint("ck_sponsor_deals_target_place", "target_place between 1 and 18");
                    table.ForeignKey(
                        name: "FK_sponsor_deals_clubs_club_id",
                        column: x => x.club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sponsor_deals_seasons_season_id",
                        column: x => x.season_id,
                        principalSchema: "competition",
                        principalTable: "seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sponsor_deals_sponsor_offers_offer_id",
                        column: x => x.offer_id,
                        principalSchema: "finance",
                        principalTable: "sponsor_offers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_transfer_listings_buyer_wage",
                schema: "market",
                table: "transfer_listings",
                sql: "generated_buyer_season_wage_minor >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_player_contracts_wage",
                schema: "squad",
                table: "player_contracts",
                sql: "season_wage_minor >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_entries_balances",
                schema: "finance",
                table: "ledger_entries",
                sql: "resulting_reserved_minor >= 0 and resulting_reserved_minor <= greatest(resulting_cash_minor, 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_entries_category",
                schema: "finance",
                table: "ledger_entries",
                sql: "category in ('opening_balance', 'gate_receipt', 'sponsorship', 'wages', 'operating_cost', 'position_award', 'transfer_payment', 'transfer_proceeds', 'bid_reservation', 'reservation_release', 'emergency_grant', 'compensation', 'stadium_construction', 'release_payout', 'snack_bar', 'merchandise', 'structure_upkeep', 'structure_upgrade', 'league_tv', 'sponsor_matchday', 'sponsor_bonus', 'parachute', 'ground_upkeep', 'clean_start')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_clubs_standing",
                schema: "world",
                table: "clubs",
                sql: "standing_centis between 100 and 10000");

            migrationBuilder.AddCheckConstraint(
                name: "ck_club_season_finance_lines_category",
                schema: "finance",
                table: "club_season_finance_lines",
                sql: "category in ('opening_balance', 'gate_receipt', 'sponsorship', 'wages', 'operating_cost', 'position_award', 'transfer_payment', 'transfer_proceeds', 'bid_reservation', 'reservation_release', 'emergency_grant', 'compensation', 'stadium_construction', 'release_payout', 'snack_bar', 'merchandise', 'structure_upkeep', 'structure_upgrade', 'league_tv', 'sponsor_matchday', 'sponsor_bonus', 'parachute', 'ground_upkeep', 'clean_start')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_club_accounts_cash_floor",
                schema: "finance",
                table: "club_accounts",
                sql: "cash_minor >= -overdraft_floor_minor");

            migrationBuilder.AddCheckConstraint(
                name: "ck_club_accounts_financial_state",
                schema: "finance",
                table: "club_accounts",
                sql: "financial_state in ('healthy', 'in_the_red', 'administration')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_club_accounts_overdraft",
                schema: "finance",
                table: "club_accounts",
                sql: "overdraft_limit_minor >= 0 and overdraft_floor_minor >= overdraft_limit_minor");

            migrationBuilder.AddCheckConstraint(
                name: "ck_club_accounts_reserved_within_cash",
                schema: "finance",
                table: "club_accounts",
                sql: "reserved_minor <= greatest(cash_minor, 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_club_accounts_state_since",
                schema: "finance",
                table: "club_accounts",
                sql: "state_since_matchday >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_club_standing_events_season_id",
                schema: "world",
                table: "club_standing_events",
                column: "season_id");

            migrationBuilder.CreateIndex(
                name: "ux_club_standing_events_club_season",
                schema: "world",
                table: "club_standing_events",
                columns: new[] { "club_id", "season_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sponsor_deals_offer_id",
                schema: "finance",
                table: "sponsor_deals",
                column: "offer_id");

            migrationBuilder.CreateIndex(
                name: "IX_sponsor_deals_season_id",
                schema: "finance",
                table: "sponsor_deals",
                column: "season_id");

            migrationBuilder.CreateIndex(
                name: "ux_sponsor_deals_club_season",
                schema: "finance",
                table: "sponsor_deals",
                columns: new[] { "club_id", "season_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sponsor_offers_season_id",
                schema: "finance",
                table: "sponsor_offers",
                column: "season_id");

            migrationBuilder.CreateIndex(
                name: "ux_sponsor_offers_club_season_kind",
                schema: "finance",
                table: "sponsor_offers",
                columns: new[] { "club_id", "season_id", "kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to a weekly wage. Rolling back is refused by the old constraints when a club is in the red or a new category was posted.
            migrationBuilder.Sql(
                "update squad.player_contracts set season_wage_minor = season_wage_minor * 3 / 34;");
            migrationBuilder.Sql(
                "update market.transfer_listings set generated_buyer_season_wage_minor = generated_buyer_season_wage_minor * 3 / 34;");

            migrationBuilder.DropTable(
                name: "club_standing_events",
                schema: "world");

            migrationBuilder.DropTable(
                name: "sponsor_deals",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "sponsor_offers",
                schema: "finance");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transfer_listings_buyer_wage",
                schema: "market",
                table: "transfer_listings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_player_contracts_wage",
                schema: "squad",
                table: "player_contracts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_entries_balances",
                schema: "finance",
                table: "ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_entries_category",
                schema: "finance",
                table: "ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_clubs_standing",
                schema: "world",
                table: "clubs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_club_season_finance_lines_category",
                schema: "finance",
                table: "club_season_finance_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_club_accounts_cash_floor",
                schema: "finance",
                table: "club_accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_club_accounts_financial_state",
                schema: "finance",
                table: "club_accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_club_accounts_overdraft",
                schema: "finance",
                table: "club_accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_club_accounts_reserved_within_cash",
                schema: "finance",
                table: "club_accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_club_accounts_state_since",
                schema: "finance",
                table: "club_accounts");

            migrationBuilder.DropColumn(
                name: "standing_centis",
                schema: "world",
                table: "clubs");

            migrationBuilder.DropColumn(
                name: "financial_state",
                schema: "finance",
                table: "club_accounts");

            migrationBuilder.DropColumn(
                name: "overdraft_floor_minor",
                schema: "finance",
                table: "club_accounts");

            migrationBuilder.DropColumn(
                name: "overdraft_limit_minor",
                schema: "finance",
                table: "club_accounts");

            migrationBuilder.DropColumn(
                name: "state_since_matchday",
                schema: "finance",
                table: "club_accounts");

            migrationBuilder.RenameColumn(
                name: "generated_buyer_season_wage_minor",
                schema: "market",
                table: "transfer_listings",
                newName: "generated_buyer_wage_minor");

            migrationBuilder.RenameColumn(
                name: "season_wage_minor",
                schema: "squad",
                table: "player_contracts",
                newName: "weekly_wage_minor");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transfer_listings_buyer_wage",
                schema: "market",
                table: "transfer_listings",
                sql: "generated_buyer_wage_minor >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_player_contracts_wage",
                schema: "squad",
                table: "player_contracts",
                sql: "weekly_wage_minor >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_entries_balances",
                schema: "finance",
                table: "ledger_entries",
                sql: "resulting_cash_minor >= 0 and resulting_reserved_minor >= 0 and resulting_reserved_minor <= resulting_cash_minor");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_entries_category",
                schema: "finance",
                table: "ledger_entries",
                sql: "category in ('opening_balance', 'gate_receipt', 'sponsorship', 'wages', 'operating_cost', 'position_award', 'transfer_payment', 'transfer_proceeds', 'bid_reservation', 'reservation_release', 'emergency_grant', 'compensation', 'stadium_construction', 'release_payout', 'snack_bar', 'merchandise', 'structure_upkeep', 'structure_upgrade')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_club_season_finances_cash",
                schema: "finance",
                table: "club_season_finances",
                sql: "opening_cash_minor >= 0 and closing_cash_minor >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_club_season_finance_lines_category",
                schema: "finance",
                table: "club_season_finance_lines",
                sql: "category in ('opening_balance', 'gate_receipt', 'sponsorship', 'wages', 'operating_cost', 'position_award', 'transfer_payment', 'transfer_proceeds', 'bid_reservation', 'reservation_release', 'emergency_grant', 'compensation', 'stadium_construction', 'release_payout', 'snack_bar', 'merchandise', 'structure_upkeep', 'structure_upgrade')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_club_accounts_cash_nonnegative",
                schema: "finance",
                table: "club_accounts",
                sql: "cash_minor >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_club_accounts_reserved_within_cash",
                schema: "finance",
                table: "club_accounts",
                sql: "reserved_minor <= cash_minor");
        }
    }
}
