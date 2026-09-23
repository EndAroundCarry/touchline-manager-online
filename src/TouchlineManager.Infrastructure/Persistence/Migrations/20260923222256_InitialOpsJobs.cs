using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialOpsJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ops");

            migrationBuilder.CreateTable(
                name: "jobs",
                schema: "ops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    business_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    priority = table.Column<short>(type: "smallint", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    lease_owner = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jobs", x => x.id);
                    table.CheckConstraint("ck_jobs_attempt_count", "attempt_count >= 0");
                    table.CheckConstraint("ck_jobs_lease_consistency", "(status = 'leased' and lease_owner is not null and lease_until is not null) or (status <> 'leased' and lease_owner is null and lease_until is null)");
                    table.CheckConstraint("ck_jobs_max_attempts", "max_attempts >= 1");
                    table.CheckConstraint("ck_jobs_priority", "priority >= 0");
                    table.CheckConstraint("ck_jobs_status", "status in ('pending', 'leased', 'completed', 'dead_letter')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_lease_until",
                schema: "ops",
                table: "jobs",
                column: "lease_until",
                filter: "status = 'leased'");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_status_due_at_priority",
                schema: "ops",
                table: "jobs",
                columns: new[] { "status", "due_at", "priority" });

            migrationBuilder.CreateIndex(
                name: "ux_jobs_job_type_business_key",
                schema: "ops",
                table: "jobs",
                columns: new[] { "job_type", "business_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "jobs",
                schema: "ops");
        }
    }
}
