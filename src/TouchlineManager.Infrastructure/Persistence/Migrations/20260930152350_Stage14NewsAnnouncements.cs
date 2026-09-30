using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Stage14NewsAnnouncements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_news_items_category",
                schema: "comms",
                table: "news_items");

            migrationBuilder.AlterColumn<string>(
                name: "category",
                schema: "comms",
                table: "news_items",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10);

            migrationBuilder.AddCheckConstraint(
                name: "ck_news_items_category",
                schema: "comms",
                table: "news_items",
                sql: "category in ('division', 'transfer', 'result', 'announcement')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_news_items_category",
                schema: "comms",
                table: "news_items");

            migrationBuilder.AlterColumn<string>(
                name: "category",
                schema: "comms",
                table: "news_items",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(12)",
                oldMaxLength: 12);

            migrationBuilder.AddCheckConstraint(
                name: "ck_news_items_category",
                schema: "comms",
                table: "news_items",
                sql: "category in ('division', 'transfer', 'result')");
        }
    }
}
