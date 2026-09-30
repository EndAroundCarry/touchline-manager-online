using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Stage14OperatorAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "mfa_completed_at",
                schema: "auth",
                table: "refresh_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "mfa_credentials",
                schema: "auth",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    protected_secret = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mfa_credentials", x => x.user_id);
                    table.CheckConstraint("ck_mfa_credentials_confirmed_at", "confirmed_at is null or confirmed_at >= created_at");
                    table.ForeignKey(
                        name: "FK_mfa_credentials_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "auth",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mfa_recovery_codes",
                schema: "auth",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mfa_recovery_codes", x => new { x.user_id, x.code_hash });
                    table.ForeignKey(
                        name: "FK_mfa_recovery_codes_mfa_credentials_user_id",
                        column: x => x.user_id,
                        principalSchema: "auth",
                        principalTable: "mfa_credentials",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mfa_recovery_codes",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "mfa_credentials",
                schema: "auth");

            migrationBuilder.DropColumn(
                name: "mfa_completed_at",
                schema: "auth",
                table: "refresh_sessions");
        }
    }
}
