using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tactical11Manager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GrowingPyramidLeagues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_divisions_country_id_tier_number",
                schema: "competition",
                table: "divisions");

            migrationBuilder.DropIndex(
                name: "ux_division_provisioning_requests_country_id_target_tier",
                schema: "world",
                table: "division_provisioning_requests");

            migrationBuilder.AddColumn<int>(
                name: "league_number",
                schema: "competition",
                table: "divisions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_division_id",
                schema: "competition",
                table: "divisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_division_id",
                schema: "world",
                table: "division_provisioning_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "target_league_number",
                schema: "world",
                table: "division_provisioning_requests",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // Until now every tier had one league and fed the tier above it, so the parent of a tier is the division one
            // tier up (PYR-15). The names gain their league number: "England Division 2" becomes "England Division 2.1".
            migrationBuilder.Sql(
                """
                update competition.divisions child
                set parent_division_id = parent.id
                from competition.divisions parent
                where parent.country_id = child.country_id
                  and parent.tier_number = child.tier_number - 1
                  and parent.league_number = 1
                  and child.tier_number >= 2;

                update competition.divisions
                set display_name = display_name || '.1'
                where tier_number >= 2 and display_name ~ ' Division [0-9]+$';

                update world.division_provisioning_requests request
                set parent_division_id = parent.id
                from competition.divisions parent
                where parent.country_id = request.country_id
                  and parent.tier_number = request.target_tier - 1
                  and parent.league_number = 1;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "parent_division_id",
                schema: "world",
                table: "division_provisioning_requests",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_divisions_parent_division_id",
                schema: "competition",
                table: "divisions",
                column: "parent_division_id");

            migrationBuilder.CreateIndex(
                name: "ux_divisions_country_id_tier_number_league_number",
                schema: "competition",
                table: "divisions",
                columns: new[] { "country_id", "tier_number", "league_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_division_provisioning_requests_parent_division_id",
                schema: "world",
                table: "division_provisioning_requests",
                column: "parent_division_id");

            migrationBuilder.CreateIndex(
                name: "ux_division_provisioning_requests_country_target_league",
                schema: "world",
                table: "division_provisioning_requests",
                columns: new[] { "country_id", "target_tier", "target_league_number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_division_provisioning_requests_divisions_parent_division_id",
                schema: "world",
                table: "division_provisioning_requests",
                column: "parent_division_id",
                principalSchema: "competition",
                principalTable: "divisions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_divisions_divisions_parent_division_id",
                schema: "competition",
                table: "divisions",
                column: "parent_division_id",
                principalSchema: "competition",
                principalTable: "divisions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only a world with league 1 of each tier can go back: the old unique key allows one league per tier.
            migrationBuilder.Sql(
                """
                delete from world.division_provisioning_requests where target_league_number <> 1;
                update competition.divisions
                set display_name = regexp_replace(display_name, '\.1$', '')
                where tier_number >= 2 and league_number = 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_division_provisioning_requests_divisions_parent_division_id",
                schema: "world",
                table: "division_provisioning_requests");

            migrationBuilder.DropForeignKey(
                name: "FK_divisions_divisions_parent_division_id",
                schema: "competition",
                table: "divisions");

            migrationBuilder.DropIndex(
                name: "IX_divisions_parent_division_id",
                schema: "competition",
                table: "divisions");

            migrationBuilder.DropIndex(
                name: "ux_divisions_country_id_tier_number_league_number",
                schema: "competition",
                table: "divisions");

            migrationBuilder.DropIndex(
                name: "IX_division_provisioning_requests_parent_division_id",
                schema: "world",
                table: "division_provisioning_requests");

            migrationBuilder.DropIndex(
                name: "ux_division_provisioning_requests_country_target_league",
                schema: "world",
                table: "division_provisioning_requests");

            migrationBuilder.DropColumn(
                name: "league_number",
                schema: "competition",
                table: "divisions");

            migrationBuilder.DropColumn(
                name: "parent_division_id",
                schema: "competition",
                table: "divisions");

            migrationBuilder.DropColumn(
                name: "parent_division_id",
                schema: "world",
                table: "division_provisioning_requests");

            migrationBuilder.DropColumn(
                name: "target_league_number",
                schema: "world",
                table: "division_provisioning_requests");

            migrationBuilder.CreateIndex(
                name: "ux_divisions_country_id_tier_number",
                schema: "competition",
                table: "divisions",
                columns: new[] { "country_id", "tier_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_division_provisioning_requests_country_id_target_tier",
                schema: "world",
                table: "division_provisioning_requests",
                columns: new[] { "country_id", "target_tier" },
                unique: true);
        }
    }
}
