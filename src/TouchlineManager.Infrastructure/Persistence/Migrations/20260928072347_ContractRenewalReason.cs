using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContractRenewalReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_player_contracts_closed_reason",
                schema: "squad",
                table: "player_contracts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_player_contracts_closed_reason",
                schema: "squad",
                table: "player_contracts",
                sql: "closed_reason is null or closed_reason in ('expired', 'transferred', 'renewed', 'released', 'retired')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_player_contracts_closed_reason",
                schema: "squad",
                table: "player_contracts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_player_contracts_closed_reason",
                schema: "squad",
                table: "player_contracts",
                sql: "closed_reason is null or closed_reason in ('expired', 'transferred', 'released', 'retired')");
        }
    }
}
