using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClubCrest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "crest_pattern",
                schema: "world",
                table: "clubs",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "crest_shape",
                schema: "world",
                table: "clubs",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "crest_pattern",
                schema: "world",
                table: "clubs");

            migrationBuilder.DropColumn(
                name: "crest_shape",
                schema: "world",
                table: "clubs");
        }
    }
}
