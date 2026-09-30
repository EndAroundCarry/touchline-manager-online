using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Stage14CompensatingEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "reverses_entry_id",
                schema: "finance",
                table: "ledger_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_reverses_entry_id",
                schema: "finance",
                table: "ledger_entries",
                column: "reverses_entry_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_entries_reverses",
                schema: "finance",
                table: "ledger_entries",
                sql: "reverses_entry_id is null or (category = 'compensation' and reverses_entry_id <> id)");

            migrationBuilder.AddForeignKey(
                name: "FK_ledger_entries_ledger_entries_reverses_entry_id",
                schema: "finance",
                table: "ledger_entries",
                column: "reverses_entry_id",
                principalSchema: "finance",
                principalTable: "ledger_entries",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ledger_entries_ledger_entries_reverses_entry_id",
                schema: "finance",
                table: "ledger_entries");

            migrationBuilder.DropIndex(
                name: "ix_ledger_entries_reverses_entry_id",
                schema: "finance",
                table: "ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_entries_reverses",
                schema: "finance",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "reverses_entry_id",
                schema: "finance",
                table: "ledger_entries");
        }
    }
}
