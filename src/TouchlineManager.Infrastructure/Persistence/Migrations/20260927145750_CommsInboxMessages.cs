using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommsInboxMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "comms");

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "comms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_manager_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    template_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    parameters = table.Column<string>(type: "jsonb", nullable: false),
                    related_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_messages", x => x.id);
                    table.CheckConstraint("ck_inbox_messages_category", "category in ('result', 'table', 'discipline', 'injury', 'squad')");
                    table.CheckConstraint("ck_inbox_messages_template_key", "length(template_key) > 0");
                    table.CheckConstraint("ck_inbox_messages_version", "version >= 1");
                    table.ForeignKey(
                        name: "FK_inbox_messages_managers_recipient_manager_id",
                        column: x => x.recipient_manager_id,
                        principalSchema: "world",
                        principalTable: "managers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inbox_messages_recipient_manager_id_created_at_id",
                schema: "comms",
                table: "inbox_messages",
                columns: new[] { "recipient_manager_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_inbox_messages_recipient_manager_id_unread",
                schema: "comms",
                table: "inbox_messages",
                column: "recipient_manager_id",
                filter: "read_at is null");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "comms");
        }
    }
}
