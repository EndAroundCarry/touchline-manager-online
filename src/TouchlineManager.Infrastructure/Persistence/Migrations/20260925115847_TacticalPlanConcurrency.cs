using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TacticalPlanConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No schema change. Marking squad.tactical_plans.version as a concurrency token is a model-only
            // change, and this migration exists to record it in the model snapshot. The token makes the
            // plan's UPDATE carry `where version = @original`, which is what turns a raced save into
            // 412 Precondition Failed rather than a silent overwrite (CONC-1, ADR-0009).
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
