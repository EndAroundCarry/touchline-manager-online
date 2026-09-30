using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Stage14JobCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_jobs_status",
                schema: "ops",
                table: "jobs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_jobs_status",
                schema: "ops",
                table: "jobs",
                sql: "status in ('pending', 'leased', 'completed', 'dead_letter', 'cancelled')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_jobs_status",
                schema: "ops",
                table: "jobs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_jobs_status",
                schema: "ops",
                table: "jobs",
                sql: "status in ('pending', 'leased', 'completed', 'dead_letter')");
        }
    }
}
