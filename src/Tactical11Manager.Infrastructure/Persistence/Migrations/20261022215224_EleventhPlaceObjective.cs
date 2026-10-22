using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tactical11Manager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EleventhPlaceObjective : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_sponsor_deals_objective",
                schema: "finance",
                table: "sponsor_deals");

            // A deal signed on 18 clubs asked for 12th; the same objective is 11th on 16 (plan luminous-cosmos). Existing worlds are not migrated
            // (RULE-4), so this only keeps the constraint from refusing an old row.
            migrationBuilder.Sql("update finance.sponsor_deals set objective = 'eleventh', target_place = 11 where objective = 'twelfth';");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sponsor_deals_objective",
                schema: "finance",
                table: "sponsor_deals",
                sql: "objective in ('win_league', 'top_three', 'top_six', 'top_half', 'eleventh', 'stay_up')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_sponsor_deals_objective",
                schema: "finance",
                table: "sponsor_deals");

            migrationBuilder.Sql("update finance.sponsor_deals set objective = 'twelfth', target_place = 12 where objective = 'eleventh';");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sponsor_deals_objective",
                schema: "finance",
                table: "sponsor_deals",
                sql: "objective in ('win_league', 'top_three', 'top_six', 'top_half', 'twelfth', 'stay_up')");
        }
    }
}
