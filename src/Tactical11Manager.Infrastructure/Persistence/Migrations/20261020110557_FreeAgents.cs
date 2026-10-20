using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tactical11Manager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FreeAgents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_transfer_outcomes_distinct_clubs",
                schema: "market",
                table: "transfer_outcomes");

            migrationBuilder.AlterColumn<Guid>(
                name: "seller_club_id",
                schema: "market",
                table: "transfer_outcomes",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "old_contract_id",
                schema: "market",
                table: "transfer_outcomes",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "seller_club_id",
                schema: "market",
                table: "transfer_listings",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "free_agent_batch_id",
                schema: "market",
                table: "transfer_listings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "free_agent_batches",
                schema: "market",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    world_id = table.Column<Guid>(type: "uuid", nullable: false),
                    week_start = table.Column<DateOnly>(type: "date", nullable: false),
                    opens_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    contracted_count = table.Column<int>(type: "integer", nullable: false),
                    generated_count = table.Column<int>(type: "integer", nullable: false),
                    released_count = table.Column<int>(type: "integer", nullable: false),
                    gem_count = table.Column<int>(type: "integer", nullable: false),
                    generator_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_free_agent_batches", x => x.id);
                    table.CheckConstraint("ck_free_agent_batches_counts", "contracted_count >= 0 and generated_count >= 0 and released_count >= 0 and gem_count >= 0 and gem_count <= generated_count");
                    table.CheckConstraint("ck_free_agent_batches_window", "ends_at > opens_at");
                    table.ForeignKey(
                        name: "FK_free_agent_batches_game_worlds_world_id",
                        column: x => x.world_id,
                        principalSchema: "world",
                        principalTable: "game_worlds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_transfer_outcomes_distinct_clubs",
                schema: "market",
                table: "transfer_outcomes",
                sql: "seller_club_id is null or seller_club_id <> buyer_club_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transfer_outcomes_seller_and_old_contract",
                schema: "market",
                table: "transfer_outcomes",
                sql: "(seller_club_id is null) = (old_contract_id is null)");

            migrationBuilder.CreateIndex(
                name: "ix_transfer_listings_free_agent_batch_id",
                schema: "market",
                table: "transfer_listings",
                column: "free_agent_batch_id",
                filter: "free_agent_batch_id is not null");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transfer_listings_seller_or_batch",
                schema: "market",
                table: "transfer_listings",
                sql: "(seller_club_id is not null and free_agent_batch_id is null) or (seller_club_id is null and free_agent_batch_id is not null)");

            migrationBuilder.CreateIndex(
                name: "ux_free_agent_batches_world_week",
                schema: "market",
                table: "free_agent_batches",
                columns: new[] { "world_id", "week_start" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_transfer_listings_free_agent_batches_free_agent_batch_id",
                schema: "market",
                table: "transfer_listings",
                column: "free_agent_batch_id",
                principalSchema: "market",
                principalTable: "free_agent_batches",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_transfer_listings_free_agent_batches_free_agent_batch_id",
                schema: "market",
                table: "transfer_listings");

            migrationBuilder.DropTable(
                name: "free_agent_batches",
                schema: "market");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transfer_outcomes_distinct_clubs",
                schema: "market",
                table: "transfer_outcomes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transfer_outcomes_seller_and_old_contract",
                schema: "market",
                table: "transfer_outcomes");

            migrationBuilder.DropIndex(
                name: "ix_transfer_listings_free_agent_batch_id",
                schema: "market",
                table: "transfer_listings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transfer_listings_seller_or_batch",
                schema: "market",
                table: "transfer_listings");

            migrationBuilder.DropColumn(
                name: "free_agent_batch_id",
                schema: "market",
                table: "transfer_listings");

            migrationBuilder.AlterColumn<Guid>(
                name: "seller_club_id",
                schema: "market",
                table: "transfer_outcomes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "old_contract_id",
                schema: "market",
                table: "transfer_outcomes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "seller_club_id",
                schema: "market",
                table: "transfer_listings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_transfer_outcomes_distinct_clubs",
                schema: "market",
                table: "transfer_outcomes",
                sql: "seller_club_id <> buyer_club_id");
        }
    }
}
