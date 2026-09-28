using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AiMarketDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_market_decisions",
                schema: "market",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evaluated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    action = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: true),
                    bid_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inputs_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    policy_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_market_decisions", x => x.id);
                    table.CheckConstraint("ck_ai_market_decisions_action_value", "action in ('listed', 'bid')");
                    table.CheckConstraint("ck_ai_market_decisions_inputs_hash", "length(inputs_hash) = 64");
                    table.CheckConstraint("ck_ai_market_decisions_policy_version", "length(policy_version) > 0");
                    table.CheckConstraint("ck_ai_market_decisions_resulting_entity", "(action = 'listed' and listing_id is not null and bid_id is null) or (action = 'bid' and bid_id is not null and listing_id is null)");
                    table.ForeignKey(
                        name: "FK_ai_market_decisions_clubs_club_id",
                        column: x => x.club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ai_market_decisions_players_player_id",
                        column: x => x.player_id,
                        principalSchema: "squad",
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ai_market_decisions_transfer_bids_bid_id",
                        column: x => x.bid_id,
                        principalSchema: "market",
                        principalTable: "transfer_bids",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ai_market_decisions_transfer_listings_listing_id",
                        column: x => x.listing_id,
                        principalSchema: "market",
                        principalTable: "transfer_listings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_market_decisions_bid_id",
                schema: "market",
                table: "ai_market_decisions",
                column: "bid_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_market_decisions_club_evaluated_at",
                schema: "market",
                table: "ai_market_decisions",
                columns: new[] { "club_id", "evaluated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_ai_market_decisions_listing_id",
                schema: "market",
                table: "ai_market_decisions",
                column: "listing_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_market_decisions_player_id",
                schema: "market",
                table: "ai_market_decisions",
                column: "player_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_market_decisions",
                schema: "market");
        }
    }
}
