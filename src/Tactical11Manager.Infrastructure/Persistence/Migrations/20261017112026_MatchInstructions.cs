using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tactical11Manager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MatchInstructions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "assistant_fatigue_substitutions",
                schema: "squad",
                table: "tactical_plans",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "tactical_plan_instructions",
                schema: "squad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<short>(type: "smallint", nullable: false),
                    name = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    from_minute = table.Column<int>(type: "integer", nullable: false),
                    until_minute = table.Column<int>(type: "integer", nullable: true),
                    score = table.Column<string>(type: "character varying(28)", maxLength: 28, nullable: false),
                    score_margin = table.Column<int>(type: "integer", nullable: false),
                    @event = table.Column<string>(name: "event", type: "character varying(28)", maxLength: 28, nullable: false),
                    event_family = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    event_threshold = table.Column<int>(type: "integer", nullable: true),
                    formation = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    overrides = table.Column<string>(type: "jsonb", nullable: false),
                    mentality = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    mentality_steps = table.Column<int>(type: "integer", nullable: false),
                    tempo = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    passing = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    width = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    pass_focus = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    pressing = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    defensive_line = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    tackling = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    time_wasting = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    counter_attack = table.Column<bool>(type: "boolean", nullable: true),
                    substitutions = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tactical_plan_instructions", x => x.id);
                    table.CheckConstraint("ck_tactical_plan_instructions_defensive_line", "defensive_line is null or defensive_line in ('deep', 'normal', 'high')");
                    table.CheckConstraint("ck_tactical_plan_instructions_event", "event in ('none', 'own_sent_off', 'opponent_sent_off', 'own_booked', 'playing_badly', 'tired', 'opponent_mentality_at_least', 'opponent_mentality_at_most')");
                    table.CheckConstraint("ck_tactical_plan_instructions_event_family", "event_family is null or event_family in ('goalkeeper', 'defence', 'midfield', 'attack')");
                    table.CheckConstraint("ck_tactical_plan_instructions_formation", "formation is null or formation in ('4-4-2', '4-3-3', '4-2-3-1', '4-1-4-1', '3-5-2', '5-3-2', '4-4-1-1', '4-5-1', '4-3-2-1', '4-2-2-2', '3-4-3', '3-4-2-1', '5-4-1')");
                    table.CheckConstraint("ck_tactical_plan_instructions_mentality", "mentality is null or mentality in ('defensive', 'cautious', 'balanced', 'positive', 'attacking')");
                    table.CheckConstraint("ck_tactical_plan_instructions_mentality_steps", "mentality_steps between -2 and 2");
                    table.CheckConstraint("ck_tactical_plan_instructions_minutes", "from_minute between 0 and 90 and (until_minute is null or (until_minute >= from_minute and until_minute <= 90))");
                    table.CheckConstraint("ck_tactical_plan_instructions_name", "length(name) between 1 and 48");
                    table.CheckConstraint("ck_tactical_plan_instructions_ordinal", "ordinal between 1 and 10");
                    table.CheckConstraint("ck_tactical_plan_instructions_pass_focus", "pass_focus is null or pass_focus in ('balanced', 'centre', 'centre_left', 'centre_right', 'wings')");
                    table.CheckConstraint("ck_tactical_plan_instructions_passing", "passing is null or passing in ('short', 'mixed', 'direct')");
                    table.CheckConstraint("ck_tactical_plan_instructions_pressing", "pressing is null or pressing in ('low_block', 'mid_block', 'high_press')");
                    table.CheckConstraint("ck_tactical_plan_instructions_score", "score in ('any', 'leading', 'level', 'trailing', 'not_winning', 'not_losing')");
                    table.CheckConstraint("ck_tactical_plan_instructions_score_margin", "score_margin between 1 and 5");
                    table.CheckConstraint("ck_tactical_plan_instructions_tackling", "tackling is null or tackling in ('stay_on_feet', 'normal', 'aggressive')");
                    table.CheckConstraint("ck_tactical_plan_instructions_tempo", "tempo is null or tempo in ('low', 'normal', 'high')");
                    table.CheckConstraint("ck_tactical_plan_instructions_time_wasting", "time_wasting is null or time_wasting in ('off', 'situational', 'on')");
                    table.CheckConstraint("ck_tactical_plan_instructions_width", "width is null or width in ('narrow', 'normal', 'wide')");
                    table.ForeignKey(
                        name: "FK_tactical_plan_instructions_tactical_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "squad",
                        principalTable: "tactical_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_tactical_plan_instructions_plan_ordinal",
                schema: "squad",
                table: "tactical_plan_instructions",
                columns: new[] { "plan_id", "ordinal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tactical_plan_instructions",
                schema: "squad");

            migrationBuilder.DropColumn(
                name: "assistant_fatigue_substitutions",
                schema: "squad",
                table: "tactical_plans");
        }
    }
}
