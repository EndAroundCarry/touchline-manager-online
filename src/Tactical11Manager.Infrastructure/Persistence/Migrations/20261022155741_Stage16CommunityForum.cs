using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tactical11Manager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Stage16CommunityForum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "forum_posts",
                schema: "comms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    thread_id = table.Column<Guid>(type: "uuid", nullable: false),
                    post_number = table.Column<int>(type: "integer", nullable: false),
                    author_manager_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_manager_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    author_club_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    is_staff = table.Column<bool>(type: "boolean", nullable: false),
                    body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_forum_posts", x => x.id);
                    table.CheckConstraint("ck_forum_posts_body", "length(body) between 1 and 10000");
                    table.CheckConstraint("ck_forum_posts_post_number", "post_number >= 1");
                });

            migrationBuilder.CreateTable(
                name: "forum_threads",
                schema: "comms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subforum_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    country_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    author_manager_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_manager_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    author_club_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    is_pinned = table.Column<bool>(type: "boolean", nullable: false),
                    is_announcement = table.Column<bool>(type: "boolean", nullable: false),
                    reply_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    latest_reply_author_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_forum_threads", x => x.id);
                    table.CheckConstraint("ck_forum_threads_reply_count", "reply_count >= 0");
                    table.CheckConstraint("ck_forum_threads_title", "length(title) between 3 and 150");
                    table.CheckConstraint("ck_forum_threads_version", "version >= 1");
                });

            migrationBuilder.CreateIndex(
                name: "ux_forum_posts_thread_post_number",
                schema: "comms",
                table: "forum_posts",
                columns: new[] { "thread_id", "post_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_forum_threads_country_subforum",
                schema: "comms",
                table: "forum_threads",
                columns: new[] { "country_id", "subforum_id", "last_activity_at" });

            migrationBuilder.CreateIndex(
                name: "ix_forum_threads_subforum_activity",
                schema: "comms",
                table: "forum_threads",
                columns: new[] { "subforum_id", "is_pinned", "last_activity_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "forum_posts",
                schema: "comms");

            migrationBuilder.DropTable(
                name: "forum_threads",
                schema: "comms");
        }
    }
}
