using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EngineV3EventVocabulary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_events_type",
                schema: "match",
                table: "events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_events_type",
                schema: "match",
                table: "events",
                sql: "event_type in ('kick_off', 'half_time', 'second_half_start', 'full_time', 'goal', 'penalty_awarded', 'penalty_goal', 'penalty_missed', 'shot_saved', 'shot_blocked', 'shot_off_target', 'woodwork', 'foul', 'yellow_card', 'second_yellow_card', 'red_card', 'offside', 'corner', 'injury', 'substitution', 'free_kick_won', 'free_kick_shot')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_events_type",
                schema: "match",
                table: "events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_events_type",
                schema: "match",
                table: "events",
                sql: "event_type in ('kick_off', 'half_time', 'second_half_start', 'full_time', 'goal', 'penalty_awarded', 'penalty_goal', 'penalty_missed', 'shot_saved', 'shot_blocked', 'shot_off_target', 'woodwork', 'foul', 'yellow_card', 'second_yellow_card', 'red_card', 'offside', 'corner', 'injury', 'substitution')");
        }
    }
}
