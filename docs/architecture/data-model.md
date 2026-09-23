# Data Model

First ER diagram + conventions for the relational model (plan §6). Exact EF mappings and migration-era detail are appended here as each stage lands them; the logical minimum below is normative for Stage 0.

## Conventions (plan §6.1, §4.6)

- Every mutable business table: `id uuid PK` (UUIDv7, server-generated), `world_id uuid` where applicable, `created_at`/`updated_at timestamptz` (UTC), `version bigint NOT NULL DEFAULT 1`.
- `snake_case` columns, C# PascalCase. Instants are `timestamptz`; API exposes ISO 8601.
- History is never cascade-deleted: restrictive FKs + status fields + anonymization instead (seasons, fixtures, ledger, bids, snapshots, audit).
- Money is `bigint` minor units. Condition/fatigue/morale/sharpness are integer basis points 0–10,000.
- JSONB only for: versioned match snapshots/config, event detail payloads, highlight keyframes, notification template params, audit metadata — each with a schema/version discriminator and golden deserialization tests (plan §4.5).
- Attributes used in scouting filters and tactics used in validation are **columns**, not JSONB.

## ER diagram — auth + world

```mermaid
erDiagram
    users ||--o| managers : "profile 1:1"
    users ||--o{ refresh_sessions : ""
    users ||--o{ email_tokens : ""
    users ||--o{ user_roles : ""
    users ||--o| user_consents : ""
    users ||--o{ audit_log : "actor"
    game_worlds ||--o{ countries : ""
    game_worlds ||--o{ seasons : ""
    countries ||--o{ clubs : ""
    countries ||--o{ divisions : ""
    countries ||--o{ division_provisioning_requests : ""
    managers ||--o{ club_tenures : ""
    clubs ||--o{ club_tenures : ""

    users {
        uuid id PK
        text normalized_email UK
        text password_hash
        timestamptz email_verified_at
        text status
        int failed_login_count
        bigint version
    }
    refresh_sessions {
        uuid id PK
        uuid user_id FK
        text token_hash UK
        uuid family_id
        timestamptz expires_at
        timestamptz revoked_at
        uuid replaced_by_session_id
    }
    email_tokens {
        uuid id PK
        uuid user_id FK
        text purpose
        text token_hash
        timestamptz expires_at
    }
    user_roles {
        uuid user_id PK_FK
        text role PK
    }
    user_consents {
        uuid user_id PK_FK
        text terms_version
        timestamptz accepted_at
    }
    game_worlds {
        uuid id PK
        text name
        text status
        text rule_set_version
        int game_season_number
        time default_kickoff_utc
    }
    countries {
        uuid id PK
        uuid world_id FK
        text code UK
        text display_name
        int sort_order
    }
    managers {
        uuid id PK
        uuid user_id FK_UK
        timestamptz takeover_cooldown_until
        text timezone
    }
    clubs {
        uuid id PK
        uuid country_id FK
        text name UK
        text slug UK
        bigint badge_seed
        int stadium_baseline
        int reputation
    }
    club_tenures {
        uuid id PK
        uuid club_id FK
        uuid manager_id FK
        timestamptz started_at
        timestamptz ended_at
        text end_reason
        text control_status
        uuid takeover_idempotency_key UK
    }
    division_provisioning_requests {
        uuid id PK
        uuid country_id FK
        int target_tier
        text status
        bigint generation_seed
    }
    generation_runs {
        uuid id PK
        text kind
        bigint seed
        text generator_version
        text status
    }
    audit_log {
        uuid id PK
        uuid actor_user_id FK
        text action
        text target
        uuid correlation_id
        timestamptz occurred_at
    }
```

> `club_tenures` enforces **one active tenure per club** and **one active tenure per manager** via partial unique indexes; `users.normalized_email` and normalized display name are unique.

## ER diagram — competition + squad

```mermaid
erDiagram
    game_worlds ||--o{ seasons : ""
    countries ||--o{ divisions : ""
    divisions ||--o{ division_seasons : ""
    seasons ||--o{ division_seasons : ""
    division_seasons ||--o{ club_season_entries : ""
    clubs ||--o{ club_season_entries : ""
    division_seasons ||--o{ matchdays : ""
    division_seasons ||--o{ standings : ""
    division_seasons ||--o{ player_season_stats : ""
    division_seasons ||--o{ discipline_records : ""
    matchdays ||--o{ fixtures : ""
    clubs ||--o{ fixtures : "home"
    clubs ||--o{ fixtures : "away"
    players ||--o| player_attributes : ""
    players ||--o| player_state : ""
    players ||--o{ player_contracts : ""
    clubs ||--o{ player_contracts : ""
    players ||--o{ player_registrations : ""
    players ||--o{ player_unavailability : ""
    players ||--o{ shortlists : ""
    managers ||--o{ shortlists : ""
    clubs ||--o{ tactical_plans : ""
    tactical_plans ||--o{ tactical_slots : ""
    fixtures ||--o{ fixture_team_sheets : ""
    fixture_team_sheets ||--o{ team_sheet_entries : ""
    players ||--o{ team_sheet_entries : ""
    clubs ||--o{ training_plans : ""
    players ||--o{ player_training_focus : ""

    seasons {
        uuid id PK
        uuid world_id FK
        int sequence_number
        int game_year
        text status
        text rule_set_version
    }
    divisions {
        uuid id PK
        uuid country_id FK
        int tier_number
        text status
        int capacity
    }
    division_seasons {
        uuid id PK
        uuid division_id FK
        uuid season_id FK
        bigint schedule_seed
        bigint tie_draw_seed
        text status
    }
    club_season_entries {
        uuid id PK
        uuid division_season_id FK
        uuid club_id FK
        int final_rank
        bool promoted
        bool relegated
    }
    matchdays {
        uuid id PK
        uuid division_season_id FK
        int round_number
        timestamptz lock_at
        timestamptz kickoff_at
        text publication_status
    }
    fixtures {
        uuid id PK
        uuid matchday_id FK
        uuid home_club_id FK
        uuid away_club_id FK
        text status
        smallint home_score
        smallint away_score
        uuid match_id
        bigint version
    }
    standings {
        uuid id PK
        uuid division_season_id FK
        uuid club_id FK
        int points
        int goal_difference
        int rank
        bigint version
    }
    player_season_stats {
        uuid id PK
        uuid division_season_id FK
        uuid player_id FK
        int appearances
        int goals
        int assists
        numeric average_rating
    }
    discipline_records {
        uuid id PK
        uuid division_season_id FK
        uuid player_id FK
        int yellow_count
        int red_count
        int pending_suspension_fixtures
    }
    players {
        uuid id PK
        text full_name
        text nationality
        int birth_game_year
        text primary_position
        uuid name_seed
    }
    player_attributes {
        uuid player_id PK_FK
        smallint finishing
        smallint passing
        smallint tackling
        smallint decisions
        smallint pace
        smallint stamina
        smallint handling
        smallint attribute_schema_version
    }
    player_state {
        uuid player_id PK_FK
        int condition_bp
        int fatigue_bp
        int morale_bp
        int sharpness_bp
        timestamptz last_progression_date
        bigint version
    }
    player_contracts {
        uuid id PK
        uuid player_id FK
        uuid club_id FK
        int end_season
        bigint weekly_wage_minor
        text status
    }
    player_registrations {
        uuid id PK
        uuid player_id FK
        uuid club_id FK
        uuid effective_fixture_id
        text status
    }
    player_unavailability {
        uuid id PK
        uuid player_id FK
        text type
        int remaining_fixtures
        text severity
    }
    tactical_plans {
        uuid id PK
        uuid club_id FK
        text formation
        text mentality
        text tempo
        bool is_default
        bigint version
    }
    tactical_slots {
        uuid id PK
        uuid plan_id FK
        int slot_number
        text position_family
        int x_scaled
        int y_scaled
        uuid default_player_id
    }
    fixture_team_sheets {
        uuid id PK
        uuid fixture_id FK
        uuid club_id FK
        text status
        bigint version
    }
    team_sheet_entries {
        uuid id PK
        uuid team_sheet_id FK
        uuid player_id FK
        text designation
        int slot_order
    }
    training_plans {
        uuid id PK
        uuid club_id FK
        text team_focus
        text intensity
        bigint version
    }
    player_training_focus {
        uuid id PK
        uuid player_id FK
        text focus_family
    }
    shortlists {
        uuid id PK
        uuid manager_id FK
        uuid player_id FK
        text notes
    }
```

> One active contract and one active registration per player are partial unique indexes; one default tactical plan per club is a partial unique index; `players_club` in the diagram denotes current squad membership derived from the active contract.

## ER diagram — match + market + finance + comms + ops

```mermaid
erDiagram
    fixtures ||--o| input_snapshots : ""
    fixtures ||--o| matches : ""
    fixtures ||--o{ simulation_attempts : ""
    matches ||--o{ lineup_participation : ""
    matches ||--o{ events : ""
    matches ||--o{ highlights : ""
    events ||--o| highlights : "source"
    players ||--o{ transfer_listings : ""
    clubs ||--o{ transfer_listings : "seller"
    transfer_listings ||--o{ transfer_bids : ""
    transfer_listings ||--o| transfer_outcomes : ""
    clubs ||--o| club_accounts : ""
    clubs ||--o{ ledger_entries : ""
    managers ||--o{ inbox_messages : ""
    game_worlds ||--o{ news_items : ""

    input_snapshots {
        uuid id PK
        uuid fixture_id FK_UK
        int engine_version
        text seed_commitment_hash
        jsonb snapshot
        text snapshot_hash
    }
    matches {
        uuid id PK
        uuid fixture_id FK_UK
        smallint home_score
        smallint away_score
        text input_hash
        text output_hash
        timestamptz completed_at
    }
    lineup_participation {
        uuid id PK
        uuid match_id FK
        uuid player_id FK
        text designation
        int entered_minute
        numeric rating
    }
    events {
        uuid id PK
        uuid match_id FK
        int sequence
        int minute
        text event_type
        uuid club_id
        uuid primary_player_id
        jsonb detail
    }
    highlights {
        uuid id PK
        uuid match_id FK
        uuid source_event_id
        int order
        int duration_ms
        jsonb keyframes
        text presentation_hash
    }
    simulation_attempts {
        uuid id PK
        uuid fixture_id FK
        int attempt_number
        text status
        text error_category
    }
    transfer_listings {
        uuid id PK
        uuid player_id FK
        uuid seller_club_id FK
        bigint minimum_fee_minor
        timestamptz opens_at
        timestamptz ends_at
        text status
        bigint version
    }
    transfer_bids {
        uuid id PK
        uuid listing_id FK
        uuid bidder_club_id FK
        bigint amount_minor
        bigint bid_sequence
        text status
    }
    transfer_outcomes {
        uuid id PK
        uuid listing_id FK_UK
        uuid winning_bid_id
        bigint fee_minor
        uuid correlation_id
    }
    club_accounts {
        uuid id PK
        uuid club_id FK_UK
        bigint cash_balance_minor
        bigint reserved_balance_minor
        bigint version
    }
    ledger_entries {
        uuid id PK
        uuid club_id FK
        bigint sequence
        text category
        bigint cash_delta
        bigint reserved_delta
        uuid correlation_id
    }
    inbox_messages {
        uuid id PK
        uuid manager_id FK
        text category
        text title_template_key
        jsonb parameters
        timestamptz read_at
    }
    news_items {
        uuid id PK
        uuid world_id FK
        text scope
        text category
        jsonb parameters
    }
    jobs {
        uuid id PK
        text job_type
        text business_key
        jsonb payload
        timestamptz due_at
        text status
        int attempt_count
        uuid lease_owner
        timestamptz lease_until
    }
    outbox_messages {
        uuid id PK
        text type
        uuid correlation_id
        jsonb payload
        timestamptz published_at
    }
    idempotency_records {
        uuid id PK
        uuid user_id FK
        text operation
        text idempotency_key
        text request_hash
        int response_status
    }
    feature_flags {
        uuid id PK
        text key
        jsonb value
        bigint version
    }
    repair_actions {
        uuid id PK
        text repair_type
        text target
        text execution_state
    }
```

> `jobs` enforces `unique (job_type, business_key)`; `ledger_entries` enforces `unique (club_id, sequence)` and is append-only (corrections are compensating entries); `transfer_bids` enforces one active bid per `(listing_id, bidder_club_id)`.
