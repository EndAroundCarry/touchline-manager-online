using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Expand step for per-attribute training progress (<c>MIG-3</c>, <c>training-v3</c>): each player's
    /// partial development is held per attribute instead of in two pooled remainders, and each training day
    /// records how its development was split.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Forward validation: every <c>squad.player_state</c> and <c>squad.player_training_days</c> row has an
    /// <c>attribute_progress</c> of <c>[]</c> after the migration, which is no progress on any attribute. The
    /// old <c>development_remainder</c> and <c>decline_remainder</c> columns are kept, defaulted to zero and
    /// never written, until the contract migration drops them. The partial points they held, under one point
    /// per player, are not carried over: there is no honest way to say which attribute they belonged to.
    /// </para>
    /// <para>
    /// Rollback: <c>Down</c> drops the two new columns and the default. A rolled-back world resumes
    /// <c>training-v2</c> with whatever the old remainders last held.
    /// </para>
    /// </remarks>
    public partial class PerAttributeTrainingProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "attribute_progress",
                schema: "squad",
                table: "player_training_days",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AlterColumn<int>(
                name: "development_remainder",
                schema: "squad",
                table: "player_state",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "attribute_progress",
                schema: "squad",
                table: "player_state",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "attribute_progress",
                schema: "squad",
                table: "player_training_days");

            migrationBuilder.DropColumn(
                name: "attribute_progress",
                schema: "squad",
                table: "player_state");

            migrationBuilder.AlterColumn<int>(
                name: "development_remainder",
                schema: "squad",
                table: "player_state",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0);
        }
    }
}
