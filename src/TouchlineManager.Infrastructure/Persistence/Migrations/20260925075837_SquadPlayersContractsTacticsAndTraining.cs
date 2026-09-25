using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SquadPlayersContractsTacticsAndTraining : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "squad");

            migrationBuilder.CreateTable(
                name: "players",
                schema: "squad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    world_id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    short_name = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    nationality_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    name_seed = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    birth_game_year = table.Column<int>(type: "integer", nullable: false),
                    birth_day_of_year = table.Column<int>(type: "integer", nullable: false),
                    preferred_foot = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    height_cm = table.Column<short>(type: "smallint", nullable: false),
                    weight_kg = table.Column<short>(type: "smallint", nullable: false),
                    primary_position = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    secondary_positions = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    potential = table.Column<short>(type: "smallint", nullable: false),
                    reputation = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_players", x => x.id);
                    table.CheckConstraint("ck_players_birth_day_of_year", "birth_day_of_year between 1 and 366");
                    table.CheckConstraint("ck_players_hidden_values_range", "potential between 1 and 20 and reputation between 1 and 20");
                    table.CheckConstraint("ck_players_physique", "height_cm > 0 and weight_kg > 0");
                    table.CheckConstraint("ck_players_preferred_foot", "preferred_foot in ('left', 'right', 'both')");
                    table.CheckConstraint("ck_players_primary_position", "primary_position in ('gk', 'rb', 'cb', 'lb', 'dm', 'cm', 'am', 'rw', 'lw', 'st')");
                    table.CheckConstraint("ck_players_status", "status in ('active', 'retired', 'free_agent', 'anonymized')");
                    table.ForeignKey(
                        name: "FK_players_game_worlds_world_id",
                        column: x => x.world_id,
                        principalSchema: "world",
                        principalTable: "game_worlds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tactical_plans",
                schema: "squad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    formation_preset = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    mentality = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    tempo = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    passing = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    width = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    pressing = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    defensive_line = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    tackling = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    time_wasting = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tactical_plans", x => x.id);
                    table.CheckConstraint("ck_tactical_plans_defensive_line", "defensive_line in ('deep', 'normal', 'high')");
                    table.CheckConstraint("ck_tactical_plans_formation", "formation_preset in ('4-4-2', '4-3-3', '4-2-3-1', '4-1-4-1', '3-5-2', '5-3-2')");
                    table.CheckConstraint("ck_tactical_plans_mentality", "mentality in ('defensive', 'cautious', 'balanced', 'positive', 'attacking')");
                    table.CheckConstraint("ck_tactical_plans_passing", "passing in ('short', 'mixed', 'direct')");
                    table.CheckConstraint("ck_tactical_plans_pressing", "pressing in ('low_block', 'mid_block', 'high_press')");
                    table.CheckConstraint("ck_tactical_plans_tackling", "tackling in ('stay_on_feet', 'normal', 'aggressive')");
                    table.CheckConstraint("ck_tactical_plans_tempo", "tempo in ('low', 'normal', 'high')");
                    table.CheckConstraint("ck_tactical_plans_time_wasting", "time_wasting in ('off', 'situational', 'on')");
                    table.CheckConstraint("ck_tactical_plans_width", "width in ('narrow', 'normal', 'wide')");
                    table.ForeignKey(
                        name: "FK_tactical_plans_clubs_club_id",
                        column: x => x.club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "training_plans",
                schema: "squad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_focus = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    intensity = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_training_plans", x => x.id);
                    table.CheckConstraint("ck_training_plans_focus", "team_focus in ('balanced', 'recovery', 'fitness', 'attacking', 'defending', 'technical', 'tactical')");
                    table.CheckConstraint("ck_training_plans_intensity", "intensity in ('light', 'normal', 'intense')");
                    table.ForeignKey(
                        name: "FK_training_plans_clubs_club_id",
                        column: x => x.club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "player_attributes",
                schema: "squad",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    finishing = table.Column<short>(type: "smallint", nullable: false),
                    passing = table.Column<short>(type: "smallint", nullable: false),
                    crossing = table.Column<short>(type: "smallint", nullable: false),
                    dribbling = table.Column<short>(type: "smallint", nullable: false),
                    first_touch = table.Column<short>(type: "smallint", nullable: false),
                    tackling = table.Column<short>(type: "smallint", nullable: false),
                    marking = table.Column<short>(type: "smallint", nullable: false),
                    heading = table.Column<short>(type: "smallint", nullable: false),
                    technique = table.Column<short>(type: "smallint", nullable: false),
                    set_pieces = table.Column<short>(type: "smallint", nullable: false),
                    decisions = table.Column<short>(type: "smallint", nullable: false),
                    vision = table.Column<short>(type: "smallint", nullable: false),
                    positioning = table.Column<short>(type: "smallint", nullable: false),
                    composure = table.Column<short>(type: "smallint", nullable: false),
                    anticipation = table.Column<short>(type: "smallint", nullable: false),
                    work_rate = table.Column<short>(type: "smallint", nullable: false),
                    aggression = table.Column<short>(type: "smallint", nullable: false),
                    leadership = table.Column<short>(type: "smallint", nullable: false),
                    pace = table.Column<short>(type: "smallint", nullable: false),
                    acceleration = table.Column<short>(type: "smallint", nullable: false),
                    stamina = table.Column<short>(type: "smallint", nullable: false),
                    strength = table.Column<short>(type: "smallint", nullable: false),
                    agility = table.Column<short>(type: "smallint", nullable: false),
                    jumping_reach = table.Column<short>(type: "smallint", nullable: false),
                    handling = table.Column<short>(type: "smallint", nullable: false),
                    reflexes = table.Column<short>(type: "smallint", nullable: false),
                    one_on_ones = table.Column<short>(type: "smallint", nullable: false),
                    aerial_ability = table.Column<short>(type: "smallint", nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_attributes", x => x.player_id);
                    table.CheckConstraint("ck_player_attributes_range_goalkeeping", "handling between 1 and 20 and reflexes between 1 and 20 and one_on_ones between 1 and 20 and aerial_ability between 1 and 20");
                    table.CheckConstraint("ck_player_attributes_range_mental", "decisions between 1 and 20 and vision between 1 and 20 and positioning between 1 and 20 and composure between 1 and 20 and anticipation between 1 and 20 and work_rate between 1 and 20 and aggression between 1 and 20 and leadership between 1 and 20");
                    table.CheckConstraint("ck_player_attributes_range_physical", "pace between 1 and 20 and acceleration between 1 and 20 and stamina between 1 and 20 and strength between 1 and 20 and agility between 1 and 20 and jumping_reach between 1 and 20");
                    table.CheckConstraint("ck_player_attributes_range_technical", "finishing between 1 and 20 and passing between 1 and 20 and crossing between 1 and 20 and dribbling between 1 and 20 and first_touch between 1 and 20 and tackling between 1 and 20 and marking between 1 and 20 and heading between 1 and 20 and technique between 1 and 20 and set_pieces between 1 and 20");
                    table.ForeignKey(
                        name: "FK_player_attributes_players_player_id",
                        column: x => x.player_id,
                        principalSchema: "squad",
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "player_contracts",
                schema: "squad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_season_number = table.Column<int>(type: "integer", nullable: false),
                    end_season_number = table.Column<int>(type: "integer", nullable: false),
                    weekly_wage_minor = table.Column<long>(type: "bigint", nullable: false),
                    squad_status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    status = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    closed_reason = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_contracts", x => x.id);
                    table.CheckConstraint("ck_player_contracts_closed_reason", "closed_reason is null or closed_reason in ('expired', 'transferred', 'released', 'retired')");
                    table.CheckConstraint("ck_player_contracts_squad_status", "squad_status in ('key_player', 'first_team', 'rotation', 'prospect')");
                    table.CheckConstraint("ck_player_contracts_status", "status in ('active', 'closed')");
                    table.CheckConstraint("ck_player_contracts_term", "end_season_number >= start_season_number");
                    table.CheckConstraint("ck_player_contracts_wage", "weekly_wage_minor >= 0");
                    table.ForeignKey(
                        name: "FK_player_contracts_clubs_club_id",
                        column: x => x.club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_player_contracts_players_player_id",
                        column: x => x.player_id,
                        principalSchema: "squad",
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "player_registrations",
                schema: "squad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_fixture_boundary_round = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_registrations", x => x.id);
                    table.CheckConstraint("ck_player_registrations_boundary", "effective_fixture_boundary_round >= 0");
                    table.CheckConstraint("ck_player_registrations_status", "status in ('active', 'ended')");
                    table.ForeignKey(
                        name: "FK_player_registrations_clubs_club_id",
                        column: x => x.club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_player_registrations_players_player_id",
                        column: x => x.player_id,
                        principalSchema: "squad",
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_player_registrations_seasons_effective_season_id",
                        column: x => x.effective_season_id,
                        principalSchema: "competition",
                        principalTable: "seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "player_state",
                schema: "squad",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    condition_bp = table.Column<int>(type: "integer", nullable: false),
                    fatigue_bp = table.Column<int>(type: "integer", nullable: false),
                    morale_bp = table.Column<int>(type: "integer", nullable: false),
                    match_sharpness_bp = table.Column<int>(type: "integer", nullable: false),
                    development_remainder = table.Column<int>(type: "integer", nullable: false),
                    last_progression_date = table.Column<DateOnly>(type: "date", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_state", x => x.player_id);
                    table.CheckConstraint("ck_player_state_basis_points", "condition_bp between 0 and 10000 and fatigue_bp between 0 and 10000 and morale_bp between 0 and 10000 and match_sharpness_bp between 0 and 10000");
                    table.CheckConstraint("ck_player_state_development_remainder", "development_remainder >= 0");
                    table.ForeignKey(
                        name: "FK_player_state_players_player_id",
                        column: x => x.player_id,
                        principalSchema: "squad",
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "player_training_focus",
                schema: "squad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    focus_family = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_training_focus", x => x.id);
                    table.CheckConstraint("ck_player_training_focus_family", "focus_family in ('technical', 'mental', 'physical', 'goalkeeping')");
                    table.ForeignKey(
                        name: "FK_player_training_focus_clubs_club_id",
                        column: x => x.club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_player_training_focus_players_player_id",
                        column: x => x.player_id,
                        principalSchema: "squad",
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "player_unavailability",
                schema: "squad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    source_fixture_id = table.Column<Guid>(type: "uuid", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    remaining_fixtures = table.Column<int>(type: "integer", nullable: false),
                    severity = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_unavailability", x => x.id);
                    table.CheckConstraint("ck_player_unavailability_remaining", "remaining_fixtures >= 0");
                    table.CheckConstraint("ck_player_unavailability_severity", "severity in ('minor', 'moderate', 'major')");
                    table.CheckConstraint("ck_player_unavailability_type", "type in ('injury', 'suspension')");
                    table.ForeignKey(
                        name: "FK_player_unavailability_clubs_club_id",
                        column: x => x.club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_player_unavailability_players_player_id",
                        column: x => x.player_id,
                        principalSchema: "squad",
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fixture_team_sheets",
                schema: "squad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fixture_id = table.Column<Guid>(type: "uuid", nullable: false),
                    club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tactical_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tactical_plan_version = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    locked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fixture_team_sheets", x => x.id);
                    table.CheckConstraint("ck_fixture_team_sheets_plan_version", "tactical_plan_version > 0");
                    table.CheckConstraint("ck_fixture_team_sheets_status", "status in ('draft', 'locked')");
                    table.ForeignKey(
                        name: "FK_fixture_team_sheets_clubs_club_id",
                        column: x => x.club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fixture_team_sheets_tactical_plans_tactical_plan_id",
                        column: x => x.tactical_plan_id,
                        principalSchema: "squad",
                        principalTable: "tactical_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tactical_slots",
                schema: "squad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slot_number = table.Column<short>(type: "smallint", nullable: false),
                    position_family = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    role = table.Column<string>(type: "character varying(21)", maxLength: 21, nullable: false),
                    normalized_x = table.Column<int>(type: "integer", nullable: false),
                    normalized_y = table.Column<int>(type: "integer", nullable: false),
                    assigned_player_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tactical_slots", x => x.id);
                    table.CheckConstraint("ck_tactical_slots_coordinates", "normalized_x between 0 and 10000 and normalized_y between 0 and 10000");
                    table.CheckConstraint("ck_tactical_slots_number", "slot_number between 1 and 11");
                    table.CheckConstraint("ck_tactical_slots_position_family", "position_family in ('goalkeeper', 'defence', 'midfield', 'attack')");
                    table.CheckConstraint("ck_tactical_slots_role", "role in ('goalkeeper', 'centre_back', 'full_back', 'wing_back', 'defensive_midfielder', 'central_midfielder', 'attacking_midfielder', 'winger', 'striker')");
                    table.ForeignKey(
                        name: "FK_tactical_slots_players_assigned_player_id",
                        column: x => x.assigned_player_id,
                        principalSchema: "squad",
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tactical_slots_tactical_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "squad",
                        principalTable: "tactical_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "team_sheet_entries",
                schema: "squad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_sheet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    designation = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    slot_number = table.Column<short>(type: "smallint", nullable: false),
                    role_override = table.Column<string>(type: "character varying(21)", maxLength: 21, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_sheet_entries", x => x.id);
                    table.CheckConstraint("ck_team_sheet_entries_designation", "designation in ('starter', 'substitute')");
                    table.CheckConstraint("ck_team_sheet_entries_role_override", "role_override is null or role_override in ('goalkeeper', 'centre_back', 'full_back', 'wing_back', 'defensive_midfielder', 'central_midfielder', 'attacking_midfielder', 'winger', 'striker')");
                    table.CheckConstraint("ck_team_sheet_entries_slot", "slot_number between 1 and 18");
                    table.ForeignKey(
                        name: "FK_team_sheet_entries_fixture_team_sheets_team_sheet_id",
                        column: x => x.team_sheet_id,
                        principalSchema: "squad",
                        principalTable: "fixture_team_sheets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_team_sheet_entries_players_player_id",
                        column: x => x.player_id,
                        principalSchema: "squad",
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fixture_team_sheets_club_id",
                schema: "squad",
                table: "fixture_team_sheets",
                column: "club_id");

            migrationBuilder.CreateIndex(
                name: "ix_fixture_team_sheets_tactical_plan_id",
                schema: "squad",
                table: "fixture_team_sheets",
                column: "tactical_plan_id");

            migrationBuilder.CreateIndex(
                name: "ux_fixture_team_sheets_fixture_club",
                schema: "squad",
                table: "fixture_team_sheets",
                columns: new[] { "fixture_id", "club_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_player_contracts_club_end_season",
                schema: "squad",
                table: "player_contracts",
                columns: new[] { "club_id", "end_season_number" });

            migrationBuilder.CreateIndex(
                name: "ux_player_contracts_active_player",
                schema: "squad",
                table: "player_contracts",
                column: "player_id",
                unique: true,
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_player_registrations_club_id",
                schema: "squad",
                table: "player_registrations",
                column: "club_id");

            migrationBuilder.CreateIndex(
                name: "ix_player_registrations_season_id",
                schema: "squad",
                table: "player_registrations",
                column: "effective_season_id");

            migrationBuilder.CreateIndex(
                name: "ux_player_registrations_active_player",
                schema: "squad",
                table: "player_registrations",
                column: "player_id",
                unique: true,
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_player_training_focus_club_id",
                schema: "squad",
                table: "player_training_focus",
                column: "club_id");

            migrationBuilder.CreateIndex(
                name: "ux_player_training_focus_player",
                schema: "squad",
                table: "player_training_focus",
                column: "player_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_player_unavailability_club_id",
                schema: "squad",
                table: "player_unavailability",
                column: "club_id");

            migrationBuilder.CreateIndex(
                name: "ix_player_unavailability_open_player",
                schema: "squad",
                table: "player_unavailability",
                column: "player_id",
                filter: "resolved_at is null");

            migrationBuilder.CreateIndex(
                name: "ix_players_world_position",
                schema: "squad",
                table: "players",
                columns: new[] { "world_id", "primary_position" });

            migrationBuilder.CreateIndex(
                name: "ux_tactical_plans_default_club",
                schema: "squad",
                table: "tactical_plans",
                column: "club_id",
                unique: true,
                filter: "is_default");

            migrationBuilder.CreateIndex(
                name: "ix_tactical_slots_assigned_player_id",
                schema: "squad",
                table: "tactical_slots",
                column: "assigned_player_id");

            migrationBuilder.CreateIndex(
                name: "ux_tactical_slots_plan_player",
                schema: "squad",
                table: "tactical_slots",
                columns: new[] { "plan_id", "assigned_player_id" },
                unique: true,
                filter: "assigned_player_id is not null");

            migrationBuilder.CreateIndex(
                name: "ux_tactical_slots_plan_slot",
                schema: "squad",
                table: "tactical_slots",
                columns: new[] { "plan_id", "slot_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_team_sheet_entries_player_id",
                schema: "squad",
                table: "team_sheet_entries",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ux_team_sheet_entries_sheet_player",
                schema: "squad",
                table: "team_sheet_entries",
                columns: new[] { "team_sheet_id", "player_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_team_sheet_entries_sheet_slot",
                schema: "squad",
                table: "team_sheet_entries",
                columns: new[] { "team_sheet_id", "slot_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_training_plans_club",
                schema: "squad",
                table: "training_plans",
                column: "club_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "player_attributes",
                schema: "squad");

            migrationBuilder.DropTable(
                name: "player_contracts",
                schema: "squad");

            migrationBuilder.DropTable(
                name: "player_registrations",
                schema: "squad");

            migrationBuilder.DropTable(
                name: "player_state",
                schema: "squad");

            migrationBuilder.DropTable(
                name: "player_training_focus",
                schema: "squad");

            migrationBuilder.DropTable(
                name: "player_unavailability",
                schema: "squad");

            migrationBuilder.DropTable(
                name: "tactical_slots",
                schema: "squad");

            migrationBuilder.DropTable(
                name: "team_sheet_entries",
                schema: "squad");

            migrationBuilder.DropTable(
                name: "training_plans",
                schema: "squad");

            migrationBuilder.DropTable(
                name: "fixture_team_sheets",
                schema: "squad");

            migrationBuilder.DropTable(
                name: "players",
                schema: "squad");

            migrationBuilder.DropTable(
                name: "tactical_plans",
                schema: "squad");
        }
    }
}
