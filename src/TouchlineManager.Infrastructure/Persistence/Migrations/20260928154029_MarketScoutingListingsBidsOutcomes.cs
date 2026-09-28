using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Creates the market schema: shortlists, transfer listings, bids, and outcomes (master plan §6.7;
    /// `SCT-3`, `TRF-1`…`TRF-15`).
    /// </summary>
    /// <remarks>
    /// Forward validation: the four tables are new, so this is additive and touches no existing row. The
    /// partial unique indexes that make the rules structural — one open listing per player (`TRF-14`) and one
    /// leading bid per listing per club (`TRF-6`) — and the unique `transfer_outcomes.listing_id` ("resolution
    /// happens once") are covered by the market integration tests.
    /// Rollback: <see cref="Down"/> drops the four tables and the schema's objects; because the tables hold
    /// only market state created after this release, dropping them loses no historical record that predates it.
    /// </remarks>
    public partial class MarketScoutingListingsBidsOutcomes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "market");

            migrationBuilder.CreateTable(
                name: "shortlists",
                schema: "market",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    manager_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notes = table.Column<string>(type: "character varying(280)", maxLength: 280, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shortlists", x => x.id);
                    table.CheckConstraint("ck_shortlists_notes_length", "length(notes) <= 280");
                    table.ForeignKey(
                        name: "FK_shortlists_managers_manager_id",
                        column: x => x.manager_id,
                        principalSchema: "world",
                        principalTable: "managers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shortlists_players_player_id",
                        column: x => x.player_id,
                        principalSchema: "squad",
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transfer_listings",
                schema: "market",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seller_club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    minimum_fee_minor = table.Column<long>(type: "bigint", nullable: false),
                    generated_buyer_wage_minor = table.Column<long>(type: "bigint", nullable: false),
                    generated_contract_seasons = table.Column<int>(type: "integer", nullable: false),
                    opens_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transfer_listings", x => x.id);
                    table.CheckConstraint("ck_transfer_listings_buyer_wage", "generated_buyer_wage_minor >= 0");
                    table.CheckConstraint("ck_transfer_listings_minimum_fee", "minimum_fee_minor > 0");
                    table.CheckConstraint("ck_transfer_listings_seasons", "generated_contract_seasons between 1 and 3");
                    table.CheckConstraint("ck_transfer_listings_window", "ends_at > opens_at");
                    table.ForeignKey(
                        name: "FK_transfer_listings_clubs_seller_club_id",
                        column: x => x.seller_club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transfer_listings_players_player_id",
                        column: x => x.player_id,
                        principalSchema: "squad",
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transfer_bids",
                schema: "market",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bidder_club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    bid_sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    placed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    reservation_correlation_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transfer_bids", x => x.id);
                    table.CheckConstraint("ck_transfer_bids_amount", "amount_minor > 0");
                    table.CheckConstraint("ck_transfer_bids_sequence", "bid_sequence >= 1");
                    table.ForeignKey(
                        name: "FK_transfer_bids_clubs_bidder_club_id",
                        column: x => x.bidder_club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transfer_bids_transfer_listings_listing_id",
                        column: x => x.listing_id,
                        principalSchema: "market",
                        principalTable: "transfer_listings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transfer_outcomes",
                schema: "market",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    winning_bid_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seller_club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    buyer_club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fee_minor = table.Column<long>(type: "bigint", nullable: false),
                    old_contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    new_contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outcome_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transfer_outcomes", x => x.id);
                    table.CheckConstraint("ck_transfer_outcomes_distinct_clubs", "seller_club_id <> buyer_club_id");
                    table.CheckConstraint("ck_transfer_outcomes_fee", "fee_minor > 0");
                    table.ForeignKey(
                        name: "FK_transfer_outcomes_clubs_buyer_club_id",
                        column: x => x.buyer_club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transfer_outcomes_clubs_seller_club_id",
                        column: x => x.seller_club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transfer_outcomes_transfer_listings_listing_id",
                        column: x => x.listing_id,
                        principalSchema: "market",
                        principalTable: "transfer_listings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shortlists_player_id",
                schema: "market",
                table: "shortlists",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ux_shortlists_manager_player",
                schema: "market",
                table: "shortlists",
                columns: new[] { "manager_id", "player_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transfer_bids_bidder_club_id",
                schema: "market",
                table: "transfer_bids",
                column: "bidder_club_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfer_bids_listing_status_amount_sequence",
                schema: "market",
                table: "transfer_bids",
                columns: new[] { "listing_id", "status", "amount_minor", "bid_sequence" });

            migrationBuilder.CreateIndex(
                name: "ux_transfer_bids_idempotency_key",
                schema: "market",
                table: "transfer_bids",
                column: "idempotency_key",
                unique: true,
                filter: "idempotency_key is not null");

            migrationBuilder.CreateIndex(
                name: "ux_transfer_bids_leading_listing_club",
                schema: "market",
                table: "transfer_bids",
                columns: new[] { "listing_id", "bidder_club_id" },
                unique: true,
                filter: "status = 'leading'");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_listings_seller_club_id",
                schema: "market",
                table: "transfer_listings",
                column: "seller_club_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfer_listings_status_ends_at",
                schema: "market",
                table: "transfer_listings",
                columns: new[] { "status", "ends_at" });

            migrationBuilder.CreateIndex(
                name: "ux_transfer_listings_idempotency_key",
                schema: "market",
                table: "transfer_listings",
                column: "idempotency_key",
                unique: true,
                filter: "idempotency_key is not null");

            migrationBuilder.CreateIndex(
                name: "ux_transfer_listings_open_player",
                schema: "market",
                table: "transfer_listings",
                column: "player_id",
                unique: true,
                filter: "status = 'open'");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_outcomes_buyer_club_id",
                schema: "market",
                table: "transfer_outcomes",
                column: "buyer_club_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfer_outcomes_resolved_at",
                schema: "market",
                table: "transfer_outcomes",
                column: "resolved_at");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_outcomes_seller_club_id",
                schema: "market",
                table: "transfer_outcomes",
                column: "seller_club_id");

            migrationBuilder.CreateIndex(
                name: "ux_transfer_outcomes_listing",
                schema: "market",
                table: "transfer_outcomes",
                column: "listing_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shortlists",
                schema: "market");

            migrationBuilder.DropTable(
                name: "transfer_bids",
                schema: "market");

            migrationBuilder.DropTable(
                name: "transfer_outcomes",
                schema: "market");

            migrationBuilder.DropTable(
                name: "transfer_listings",
                schema: "market");
        }
    }
}
