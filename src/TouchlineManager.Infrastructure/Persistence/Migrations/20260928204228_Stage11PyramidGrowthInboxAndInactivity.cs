using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Stage11PyramidGrowthInboxAndInactivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_inbox_messages_category",
                schema: "comms",
                table: "inbox_messages");

            migrationBuilder.AddColumn<bool>(
                name: "is_bootstrap",
                schema: "competition",
                table: "fixtures",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "inactivity_warning_at",
                schema: "world",
                table: "club_tenures",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "news_items",
                schema: "comms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    world_id = table.Column<Guid>(type: "uuid", nullable: false),
                    country_id = table.Column<Guid>(type: "uuid", nullable: true),
                    division_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    template_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    parameters = table.Column<string>(type: "jsonb", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_news_items", x => x.id);
                    table.CheckConstraint("ck_news_items_category", "category in ('division', 'transfer', 'result')");
                    table.CheckConstraint("ck_news_items_expiry", "expires_at is null or expires_at > published_at");
                    table.CheckConstraint("ck_news_items_template_key", "length(template_key) > 0");
                    table.CheckConstraint("ck_news_items_version", "version >= 1");
                    table.ForeignKey(
                        name: "FK_news_items_countries_country_id",
                        column: x => x.country_id,
                        principalSchema: "world",
                        principalTable: "countries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_news_items_divisions_division_id",
                        column: x => x.division_id,
                        principalSchema: "competition",
                        principalTable: "divisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_news_items_game_worlds_world_id",
                        column: x => x.world_id,
                        principalSchema: "world",
                        principalTable: "game_worlds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notification_preferences",
                schema: "comms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    manager_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email_deadline_reminders = table.Column<bool>(type: "boolean", nullable: false),
                    email_inactivity_warnings = table.Column<bool>(type: "boolean", nullable: false),
                    email_market_messages = table.Column<bool>(type: "boolean", nullable: false),
                    email_news_digest = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_preferences", x => x.id);
                    table.CheckConstraint("ck_notification_preferences_version", "version >= 1");
                    table.ForeignKey(
                        name: "FK_notification_preferences_managers_manager_id",
                        column: x => x.manager_id,
                        principalSchema: "world",
                        principalTable: "managers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "ops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    aggregate_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    aggregate_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.id);
                    table.CheckConstraint("ck_outbox_messages_attempts", "attempt_count >= 0 and max_attempts >= 1");
                    table.CheckConstraint("ck_outbox_messages_status", "status in ('pending', 'published', 'dead_letter')");
                    table.CheckConstraint("ck_outbox_messages_type", "length(type) > 0");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_inbox_messages_category",
                schema: "comms",
                table: "inbox_messages",
                sql: "category in ('result', 'table', 'discipline', 'injury', 'squad', 'occupancy', 'reminder', 'system')");

            migrationBuilder.CreateIndex(
                name: "ix_news_items_country_id_published_at",
                schema: "comms",
                table: "news_items",
                columns: new[] { "country_id", "published_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_news_items_division_id_published_at",
                schema: "comms",
                table: "news_items",
                columns: new[] { "division_id", "published_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_news_items_published_at",
                schema: "comms",
                table: "news_items",
                column: "published_at");

            migrationBuilder.CreateIndex(
                name: "IX_news_items_world_id",
                schema: "comms",
                table: "news_items",
                column: "world_id");

            migrationBuilder.CreateIndex(
                name: "ux_notification_preferences_manager_id",
                schema: "comms",
                table: "notification_preferences",
                column: "manager_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_correlation_id",
                schema: "ops",
                table: "outbox_messages",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_status_due_at",
                schema: "ops",
                table: "outbox_messages",
                columns: new[] { "status", "due_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "news_items",
                schema: "comms");

            migrationBuilder.DropTable(
                name: "notification_preferences",
                schema: "comms");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "ops");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inbox_messages_category",
                schema: "comms",
                table: "inbox_messages");

            migrationBuilder.DropColumn(
                name: "is_bootstrap",
                schema: "competition",
                table: "fixtures");

            migrationBuilder.DropColumn(
                name: "inactivity_warning_at",
                schema: "world",
                table: "club_tenures");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inbox_messages_category",
                schema: "comms",
                table: "inbox_messages",
                sql: "category in ('result', 'table', 'discipline', 'injury', 'squad')");
        }
    }
}
