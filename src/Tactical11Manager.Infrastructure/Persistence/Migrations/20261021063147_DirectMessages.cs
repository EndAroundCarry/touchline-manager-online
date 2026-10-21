using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tactical11Manager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DirectMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conversations",
                schema: "comms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    started_by_manager_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_manager_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_from_club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_message_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversations", x => x.id);
                    table.CheckConstraint("ck_conversations_participants", "started_by_manager_id <> recipient_manager_id");
                    table.CheckConstraint("ck_conversations_subject", "length(subject) between 1 and 80");
                    table.CheckConstraint("ck_conversations_version", "version >= 1");
                    table.ForeignKey(
                        name: "FK_conversations_clubs_started_from_club_id",
                        column: x => x.started_from_club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_conversations_managers_recipient_manager_id",
                        column: x => x.recipient_manager_id,
                        principalSchema: "world",
                        principalTable: "managers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_conversations_managers_started_by_manager_id",
                        column: x => x.started_by_manager_id,
                        principalSchema: "world",
                        principalTable: "managers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "manager_blocks",
                schema: "comms",
                columns: table => new
                {
                    blocker_manager_id = table.Column<Guid>(type: "uuid", nullable: false),
                    blocked_manager_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manager_blocks", x => new { x.blocker_manager_id, x.blocked_manager_id });
                    table.CheckConstraint("ck_manager_blocks_distinct", "blocker_manager_id <> blocked_manager_id");
                    table.ForeignKey(
                        name: "FK_manager_blocks_managers_blocked_manager_id",
                        column: x => x.blocked_manager_id,
                        principalSchema: "world",
                        principalTable: "managers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_manager_blocks_managers_blocker_manager_id",
                        column: x => x.blocker_manager_id,
                        principalSchema: "world",
                        principalTable: "managers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "direct_messages",
                schema: "comms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_manager_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_manager_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_direct_messages", x => x.id);
                    table.CheckConstraint("ck_direct_messages_body", "length(body) between 1 and 1000");
                    table.CheckConstraint("ck_direct_messages_participants", "sender_manager_id <> recipient_manager_id");
                    table.ForeignKey(
                        name: "FK_direct_messages_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalSchema: "comms",
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_direct_messages_managers_recipient_manager_id",
                        column: x => x.recipient_manager_id,
                        principalSchema: "world",
                        principalTable: "managers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_direct_messages_managers_sender_manager_id",
                        column: x => x.sender_manager_id,
                        principalSchema: "world",
                        principalTable: "managers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_recipient_manager_id_last_message_at_id",
                schema: "comms",
                table: "conversations",
                columns: new[] { "recipient_manager_id", "last_message_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_started_by_manager_id_last_message_at_id",
                schema: "comms",
                table: "conversations",
                columns: new[] { "started_by_manager_id", "last_message_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_conversations_started_from_club_id",
                schema: "comms",
                table: "conversations",
                column: "started_from_club_id");

            migrationBuilder.CreateIndex(
                name: "ix_direct_messages_conversation_id_sent_at_id",
                schema: "comms",
                table: "direct_messages",
                columns: new[] { "conversation_id", "sent_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_direct_messages_recipient_manager_id_unread",
                schema: "comms",
                table: "direct_messages",
                column: "recipient_manager_id",
                filter: "read_at is null");

            migrationBuilder.CreateIndex(
                name: "IX_direct_messages_sender_manager_id",
                schema: "comms",
                table: "direct_messages",
                column: "sender_manager_id");

            migrationBuilder.CreateIndex(
                name: "ix_manager_blocks_blocked_manager_id",
                schema: "comms",
                table: "manager_blocks",
                column: "blocked_manager_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "direct_messages",
                schema: "comms");

            migrationBuilder.DropTable(
                name: "manager_blocks",
                schema: "comms");

            migrationBuilder.DropTable(
                name: "conversations",
                schema: "comms");
        }
    }
}
