using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TouchlineManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorldPyramidAndOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "finance");

            migrationBuilder.EnsureSchema(
                name: "competition");

            migrationBuilder.EnsureSchema(
                name: "world");

            migrationBuilder.CreateTable(
                name: "game_worlds",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    rule_set_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    current_season_number = table.Column<int>(type: "integer", nullable: false),
                    kickoff_utc = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_worlds", x => x.id);
                    table.CheckConstraint("ck_game_worlds_season_number", "current_season_number >= 1");
                    table.CheckConstraint("ck_game_worlds_status", "status in ('active', 'frozen', 'retired')");
                });

            migrationBuilder.CreateTable(
                name: "generation_runs",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    seed = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    generator_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    input_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    countries_created = table.Column<int>(type: "integer", nullable: false),
                    clubs_created = table.Column<int>(type: "integer", nullable: false),
                    players_created = table.Column<int>(type: "integer", nullable: false),
                    accounts_created = table.Column<int>(type: "integer", nullable: false),
                    diagnostics = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_generation_runs", x => x.id);
                    table.CheckConstraint("ck_generation_runs_counts", "countries_created >= 0 and clubs_created >= 0 and players_created >= 0 and accounts_created >= 0");
                    table.CheckConstraint("ck_generation_runs_kind", "kind in ('world_bootstrap', 'division_provisioning')");
                    table.CheckConstraint("ck_generation_runs_status", "status in ('running', 'succeeded', 'failed')");
                });

            migrationBuilder.CreateTable(
                name: "managers",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reputation = table.Column<int>(type: "integer", nullable: false),
                    takeover_cooldown_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    locale = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_managers", x => x.id);
                    table.ForeignKey(
                        name: "FK_managers_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "auth",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "countries",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    world_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    display_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    locale = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name_pool_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_countries", x => x.id);
                    table.CheckConstraint("ck_countries_code", "char_length(code) = 3 and code = upper(code)");
                    table.ForeignKey(
                        name: "FK_countries_game_worlds_world_id",
                        column: x => x.world_id,
                        principalSchema: "world",
                        principalTable: "game_worlds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "seasons",
                schema: "competition",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    world_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_number = table.Column<int>(type: "integer", nullable: false),
                    display_label = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    game_year = table.Column<int>(type: "integer", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rollover_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    rule_set_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seasons", x => x.id);
                    table.CheckConstraint("ck_seasons_sequence_number", "sequence_number >= 1");
                    table.CheckConstraint("ck_seasons_status", "status in ('scheduled', 'active', 'rollover', 'completed')");
                    table.CheckConstraint("ck_seasons_window", "ends_at >= starts_at and rollover_ends_at >= ends_at");
                    table.ForeignKey(
                        name: "FK_seasons_game_worlds_world_id",
                        column: x => x.world_id,
                        principalSchema: "world",
                        principalTable: "game_worlds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "clubs",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    world_id = table.Column<Guid>(type: "uuid", nullable: false),
                    country_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    short_name = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    city = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    region = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    badge_seed = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    founding_game_year = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    stadium_baseline = table.Column<long>(type: "bigint", nullable: false),
                    reputation = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clubs", x => x.id);
                    table.CheckConstraint("ck_clubs_reputation", "reputation between 1 and 100");
                    table.CheckConstraint("ck_clubs_stadium_baseline", "stadium_baseline >= 0");
                    table.CheckConstraint("ck_clubs_status", "status in ('active', 'retired')");
                    table.ForeignKey(
                        name: "FK_clubs_countries_country_id",
                        column: x => x.country_id,
                        principalSchema: "world",
                        principalTable: "countries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_clubs_game_worlds_world_id",
                        column: x => x.world_id,
                        principalSchema: "world",
                        principalTable: "game_worlds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "division_provisioning_requests",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    country_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_tier = table.Column<int>(type: "integer", nullable: false),
                    target_season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    generation_seed = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_diagnostics = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_division_provisioning_requests", x => x.id);
                    table.CheckConstraint("ck_division_provisioning_requests_status", "status in ('requested', 'running', 'completed', 'failed')");
                    table.CheckConstraint("ck_division_provisioning_requests_target_tier", "target_tier >= 2");
                    table.ForeignKey(
                        name: "FK_division_provisioning_requests_countries_country_id",
                        column: x => x.country_id,
                        principalSchema: "world",
                        principalTable: "countries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "divisions",
                schema: "competition",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    country_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tier_number = table.Column<int>(type: "integer", nullable: false),
                    display_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_divisions", x => x.id);
                    table.CheckConstraint("ck_divisions_capacity", "capacity = 18");
                    table.CheckConstraint("ck_divisions_status", "status in ('provisioning', 'active', 'retired')");
                    table.CheckConstraint("ck_divisions_tier_number", "tier_number >= 1");
                    table.ForeignKey(
                        name: "FK_divisions_countries_country_id",
                        column: x => x.country_id,
                        principalSchema: "world",
                        principalTable: "countries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_divisions_seasons_created_season_id",
                        column: x => x.created_season_id,
                        principalSchema: "competition",
                        principalTable: "seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "club_accounts",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cash_minor = table.Column<long>(type: "bigint", nullable: false),
                    reserved_minor = table.Column<long>(type: "bigint", nullable: false),
                    last_ledger_sequence = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_club_accounts", x => x.id);
                    table.CheckConstraint("ck_club_accounts_cash_nonnegative", "cash_minor >= 0");
                    table.CheckConstraint("ck_club_accounts_ledger_sequence", "last_ledger_sequence >= 0");
                    table.CheckConstraint("ck_club_accounts_reserved_nonnegative", "reserved_minor >= 0");
                    table.CheckConstraint("ck_club_accounts_reserved_within_cash", "reserved_minor <= cash_minor");
                    table.ForeignKey(
                        name: "FK_club_accounts_clubs_club_id",
                        column: x => x.club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "club_tenures",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    manager_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    end_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    last_active_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    control_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    takeover_idempotency_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_club_tenures", x => x.id);
                    table.CheckConstraint("ck_club_tenures_control_status", "control_status in ('active', 'inactive', 'closed')");
                    table.CheckConstraint("ck_club_tenures_end_reason", "(ended_at is null and end_reason is null) or (ended_at is not null and end_reason is not null)");
                    table.CheckConstraint("ck_club_tenures_end_reason_value", "end_reason is null or end_reason in ('resigned', 'inactivity_closed', 'administrator_closed')");
                    table.ForeignKey(
                        name: "FK_club_tenures_clubs_club_id",
                        column: x => x.club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_club_tenures_managers_manager_id",
                        column: x => x.manager_id,
                        principalSchema: "world",
                        principalTable: "managers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "division_seasons",
                schema: "competition",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    division_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    schedule_seed = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tie_draw_seed = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tie_draw_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    standings_finalized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_division_seasons", x => x.id);
                    table.CheckConstraint("ck_division_seasons_status", "status in ('scheduled', 'active', 'completed')");
                    table.ForeignKey(
                        name: "FK_division_seasons_divisions_division_id",
                        column: x => x.division_id,
                        principalSchema: "competition",
                        principalTable: "divisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_division_seasons_seasons_season_id",
                        column: x => x.season_id,
                        principalSchema: "competition",
                        principalTable: "seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "club_season_entries",
                schema: "competition",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    division_season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    initial_control_type = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    final_rank = table.Column<int>(type: "integer", nullable: true),
                    is_promoted = table.Column<bool>(type: "boolean", nullable: false),
                    is_relegated = table.Column<bool>(type: "boolean", nullable: false),
                    closing_reputation = table.Column<int>(type: "integer", nullable: true),
                    closing_cash_minor = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_club_season_entries", x => x.id);
                    table.CheckConstraint("ck_club_season_entries_closing_cash", "closing_cash_minor is null or closing_cash_minor >= 0");
                    table.CheckConstraint("ck_club_season_entries_control_type", "initial_control_type in ('ai', 'human')");
                    table.CheckConstraint("ck_club_season_entries_final_rank", "final_rank is null or final_rank >= 1");
                    table.CheckConstraint("ck_club_season_entries_movement", "not (is_promoted and is_relegated)");
                    table.ForeignKey(
                        name: "FK_club_season_entries_clubs_club_id",
                        column: x => x.club_id,
                        principalSchema: "world",
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_club_season_entries_division_seasons_division_season_id",
                        column: x => x.division_season_id,
                        principalSchema: "competition",
                        principalTable: "division_seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_club_season_entries_seasons_season_id",
                        column: x => x.season_id,
                        principalSchema: "competition",
                        principalTable: "seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_club_accounts_club_id",
                schema: "finance",
                table: "club_accounts",
                column: "club_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_club_season_entries_club_id",
                schema: "competition",
                table: "club_season_entries",
                column: "club_id");

            migrationBuilder.CreateIndex(
                name: "ux_club_season_entries_division_season_id_club_id",
                schema: "competition",
                table: "club_season_entries",
                columns: new[] { "division_season_id", "club_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_club_season_entries_season_id_club_id",
                schema: "competition",
                table: "club_season_entries",
                columns: new[] { "season_id", "club_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_club_tenures_manager_id_control_status",
                schema: "world",
                table: "club_tenures",
                columns: new[] { "manager_id", "control_status" });

            migrationBuilder.CreateIndex(
                name: "ux_club_tenures_open_club",
                schema: "world",
                table: "club_tenures",
                column: "club_id",
                unique: true,
                filter: "control_status <> 'closed'");

            migrationBuilder.CreateIndex(
                name: "ux_club_tenures_open_manager",
                schema: "world",
                table: "club_tenures",
                column: "manager_id",
                unique: true,
                filter: "control_status <> 'closed'");

            migrationBuilder.CreateIndex(
                name: "ux_club_tenures_takeover_idempotency_key",
                schema: "world",
                table: "club_tenures",
                column: "takeover_idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_clubs_country_id",
                schema: "world",
                table: "clubs",
                column: "country_id");

            migrationBuilder.CreateIndex(
                name: "ux_clubs_world_id_normalized_name",
                schema: "world",
                table: "clubs",
                columns: new[] { "world_id", "normalized_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_clubs_world_id_slug",
                schema: "world",
                table: "clubs",
                columns: new[] { "world_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_countries_world_id_code",
                schema: "world",
                table: "countries",
                columns: new[] { "world_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_division_provisioning_requests_country_id_target_tier",
                schema: "world",
                table: "division_provisioning_requests",
                columns: new[] { "country_id", "target_tier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_division_seasons_season_id",
                schema: "competition",
                table: "division_seasons",
                column: "season_id");

            migrationBuilder.CreateIndex(
                name: "ux_division_seasons_division_id_season_id",
                schema: "competition",
                table: "division_seasons",
                columns: new[] { "division_id", "season_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_divisions_created_season_id",
                schema: "competition",
                table: "divisions",
                column: "created_season_id");

            migrationBuilder.CreateIndex(
                name: "ux_divisions_country_id_tier_number",
                schema: "competition",
                table: "divisions",
                columns: new[] { "country_id", "tier_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_game_worlds_name",
                schema: "world",
                table: "game_worlds",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_generation_runs_kind_started_at",
                schema: "world",
                table: "generation_runs",
                columns: new[] { "kind", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ux_managers_user_id",
                schema: "world",
                table: "managers",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_seasons_world_id_sequence_number",
                schema: "competition",
                table: "seasons",
                columns: new[] { "world_id", "sequence_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "club_accounts",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "club_season_entries",
                schema: "competition");

            migrationBuilder.DropTable(
                name: "club_tenures",
                schema: "world");

            migrationBuilder.DropTable(
                name: "division_provisioning_requests",
                schema: "world");

            migrationBuilder.DropTable(
                name: "generation_runs",
                schema: "world");

            migrationBuilder.DropTable(
                name: "division_seasons",
                schema: "competition");

            migrationBuilder.DropTable(
                name: "clubs",
                schema: "world");

            migrationBuilder.DropTable(
                name: "managers",
                schema: "world");

            migrationBuilder.DropTable(
                name: "divisions",
                schema: "competition");

            migrationBuilder.DropTable(
                name: "countries",
                schema: "world");

            migrationBuilder.DropTable(
                name: "seasons",
                schema: "competition");

            migrationBuilder.DropTable(
                name: "game_worlds",
                schema: "world");
        }
    }
}
