# Data Model

> **Scope:** the logical model and the constraints that carry correctness. Exact EF Core
> mappings and generated SQL belong in migrations (Stage 2 onward); master plan §6 is the
> normative column-level reference and this document explains structure, keys, and policy.

---

## 1. Common conventions

| Convention | Rule |
|---|---|
| Primary keys | `id uuid primary key`, server-generated UUIDv7 (`ID-1`) |
| Tenancy | `world_id uuid` on every table where a world can be derived, so future shards/test worlds need no schema rewrite |
| Timestamps | `created_at`, `updated_at` as UTC `timestamptz not null` |
| Concurrency | `version bigint not null default 1` on mutable aggregates; exposed as a strong ETag |
| Names | `snake_case` in PostgreSQL, `PascalCase` in C# (`ID-2`) |
| Money | `bigint` minor units, single display currency. Never `float`/`double`/`numeric` money arithmetic in application code (`FIN-1`, `FIN-2`) |
| Deletes | Completed seasons, fixtures, financial records, bids, match snapshots, and audit rows are never deleted. Status fields and anonymization replace destructive deletes. |

**Referential integrity.** Restrictive foreign keys for history. `ON DELETE CASCADE` is not
used for business history. When there is genuinely no safe parent to delete, the operation is
a status change, not a delete.

---

## 2. Schema overview

```mermaid
flowchart LR
    subgraph auth[auth]
        users
        refresh_sessions
        email_tokens
        user_roles
        user_consents
    end
    subgraph world[world]
        game_worlds
        countries
        clubs
        managers
        club_tenures
        division_provisioning_requests
        generation_runs
    end
    subgraph squad[squad]
        players
        player_attributes
        player_state
        player_contracts
        player_registrations
        player_unavailability
        tactical_plans
        tactical_slots
        fixture_team_sheets
        team_sheet_entries
        training_plans
        player_training_focus
    end
    subgraph competition[competition]
        seasons
        divisions
        division_seasons
        club_season_entries
        matchdays
        fixtures
        standings
        player_season_stats
        club_season_stats
        discipline_records
        season_rollovers
    end
    subgraph match[match]
        input_snapshots
        matches
        lineup_participation
        events
        highlights
        simulation_attempts
    end
    subgraph market[market]
        shortlists
        transfer_listings
        transfer_bids
        transfer_outcomes
        ai_market_decisions
    end
    subgraph finance[finance]
        club_accounts
        ledger_entries
        club_season_finances
    end
    subgraph comms[comms]
        inbox_messages
        news_items
    end
    subgraph ops[ops]
        jobs
        outbox_messages
        idempotency_records
        audit_log
        feature_flags
        repair_actions
    end

    users --> managers
    managers --> club_tenures
    clubs --> club_tenures
    countries --> clubs
    game_worlds --> countries
    clubs --> players
    players --> player_contracts
    players --> player_attributes
    players --> player_state
    clubs --> tactical_plans
    tactical_plans --> tactical_slots
    seasons --> division_seasons
    divisions --> division_seasons
    division_seasons --> matchdays
    matchdays --> fixtures
    fixtures --> input_snapshots
    fixtures --> matches
    matches --> events
    events --> highlights
    matches --> lineup_participation
    division_seasons --> standings
    players --> player_season_stats
    clubs --> club_accounts
    club_accounts --> ledger_entries
    players --> transfer_listings
    transfer_listings --> transfer_bids
    transfer_listings --> transfer_outcomes
```

---

## 3. Entity relationships

### 3.1 Identity and world

```mermaid
erDiagram
    users ||--o| managers : "becomes"
    users ||--o{ refresh_sessions : "holds"
    users ||--o{ email_tokens : "requests"
    users ||--o{ user_roles : "granted"
    users ||--o{ user_consents : "accepts"
    managers ||--o{ club_tenures : "serves"
    game_worlds ||--o{ countries : "contains"
    game_worlds ||--o{ seasons : "runs"
    countries ||--o{ clubs : "hosts"
    countries ||--o{ divisions : "has"
    countries ||--o{ division_provisioning_requests : "requests"
    clubs ||--o{ club_tenures : "controlled by"

    users {
        uuid id PK
        text email
        text normalized_email UK
        text password_hash
        text display_name
        text normalized_display_name UK
        timestamptz email_verified_at
        text status "pending|active|suspended|deletion_pending|anonymized"
        timestamptz last_login_at
        text security_stamp
        int failed_login_count
        timestamptz lockout_until
    }
    club_tenures {
        uuid id PK
        uuid club_id FK
        uuid manager_id FK
        timestamptz started_at
        timestamptz ended_at
        text end_reason
        timestamptz last_active_at
        timestamptz inactivity_warning_at
        text control_status "active|inactive|closed"
        text takeover_idempotency_key
    }
    clubs {
        uuid id PK
        uuid world_id FK
        uuid country_id FK
        text name
        text short_name
        text slug UK
        text badge_seed
        int founding_game_year
        text status
        bigint stadium_baseline
        int reputation
        bigint version
    }
    countries {
        uuid id PK
        uuid world_id FK
        text code UK
        text display_name
        text name_pool_key
        int sort_order
        boolean is_active
    }
    division_provisioning_requests {
        uuid id PK
        uuid country_id FK
        int target_tier
        uuid target_season_id FK
        text status
        text generation_seed
        timestamptz requested_at
        timestamptz completed_at
        text failure_diagnostics
    }
```

**Critical indexes and constraints**

| Constraint | Table | Why |
|---|---|---|
| `unique (normalized_email)` | `users` | Account identity |
| `unique (normalized_display_name)` | `users` | Public identity uniqueness |
| `check (status in ...)`, `check (failed_login_count >= 0)` | `users` | The lifecycle cannot leave the states the code knows |
| `unique (token_hash)` | `refresh_sessions`, `email_tokens` | Tokens are looked up by hash; a duplicate could only mean a reused secret |
| `unique (user_id, role)` (primary key) | `user_roles` | A role is granted to an account once |
| `check (revoked_at is null) = (revocation_reason is null)` | `refresh_sessions` | A revoked session always says why |
| Restrictive FKs to `users` | `user_roles`, `refresh_sessions`, `email_tokens`, `user_consents` | Session and consent history cannot outlive its account; account deletion is a status change, never a delete (ADR-0002) |
| `unique (world_id, code)` | `countries` | Stable country identity |
| `unique (world_id, normalized_name)`, `unique (world_id, slug)` | `clubs` | No duplicate fictional clubs |
| **Partial unique** one `active`/`inactive` tenure per club | `club_tenures` | A club cannot have two managers |
| **Partial unique** one `active`/`inactive` tenure per manager | `club_tenures` | One club per manager (`OCC-9`) |
| `unique (country_id, target_tier)` | `division_provisioning_requests` | No duplicate tower creation (`PYR-3`) |
| Status check + transition check | `users` | Prevents illegal lifecycle jumps |

### 3.2 Squad, contracts, and tactics

```mermaid
erDiagram
    players ||--|| player_attributes : "has"
    players ||--|| player_state : "has"
    players ||--o{ player_contracts : "signs"
    players ||--o{ player_registrations : "registers"
    players ||--o{ player_unavailability : "may have"
    players ||--o{ player_training_focus : "may have"
    clubs ||--o{ tactical_plans : "owns"
    tactical_plans ||--|{ tactical_slots : "defines"
    clubs ||--o{ training_plans : "sets"
    fixtures ||--o{ fixture_team_sheets : "has"
    fixture_team_sheets ||--|{ team_sheet_entries : "contains"

    players {
        uuid id PK
        uuid world_id FK
        text full_name
        text short_name
        text nationality_code
        text name_seed
        int birth_game_year
        int birth_day_of_year
        text preferred_foot
        smallint height_cm
        smallint weight_kg
        text primary_position
        text secondary_positions
        text status "active|retired|free_agent|anonymized"
        int retirement_announced_season_number "null until announced (CON-10)"
    }
    player_attributes {
        uuid player_id PK
        smallint finishing
        smallint passing
        smallint crossing
        smallint dribbling
        smallint first_touch
        smallint tackling
        smallint marking
        smallint heading
        smallint technique
        smallint set_pieces
        smallint decisions
        smallint vision
        smallint positioning
        smallint composure
        smallint anticipation
        smallint work_rate
        smallint aggression
        smallint leadership
        smallint pace
        smallint acceleration
        smallint stamina
        smallint strength
        smallint agility
        smallint jumping_reach
        smallint handling
        smallint reflexes
        smallint one_on_ones
        smallint aerial_ability
        int schema_version
        text checksum
    }
    player_state {
        uuid player_id PK
        int condition_bp
        int fatigue_bp
        int morale_bp
        int match_sharpness_bp
        int development_remainder
        date last_progression_date
        bigint version
    }
    player_contracts {
        uuid id PK
        uuid player_id FK
        uuid club_id FK
        int start_season_number
        int end_season_number
        bigint weekly_wage_minor
        text squad_status
        text status
        text closed_reason
        timestamptz closed_at
    }
    player_registrations {
        uuid id PK
        uuid player_id FK
        uuid club_id FK
        int effective_fixture_boundary_round
        uuid effective_season_id FK
        text status
    }
    player_unavailability {
        uuid id PK
        uuid player_id FK
        uuid club_id FK
        text type "injury|suspension"
        uuid source_fixture_id FK
        timestamptz started_at
        int remaining_fixtures
        text severity
        timestamptz resolved_at
    }
    tactical_plans {
        uuid id PK
        uuid club_id FK
        text name
        text formation_preset
        text mentality
        text tempo
        text passing
        text width
        text pressing
        text defensive_line
        text tackling
        text time_wasting
        boolean is_default
        bigint version
    }
    tactical_slots {
        uuid id PK
        uuid plan_id FK
        smallint slot_number "1..11"
        text position_family
        text role
        int normalized_x "0..10000"
        int normalized_y "0..10000"
        uuid assigned_player_id FK
    }
    fixture_team_sheets {
        uuid id PK
        uuid fixture_id FK
        uuid club_id FK
        uuid tactical_plan_id FK
        bigint tactical_plan_version
        text status "draft|locked"
        bigint version
    }
```

**Critical indexes and constraints**

| Constraint | Table | Why |
|---|---|---|
| `check (attribute between 1 and 20)` on every attribute column | `player_attributes` | `TRN-4`; the engine assumes this range |
| `check` on basis-point ranges | `player_state` | `TRN-5..7` |
| **Partial unique** one `active` contract per player | `player_contracts` | `SQ-6` |
| **Partial unique** one `active` registration per player | `player_registrations` | `SQ-6` |
| `unique (division_season_id, player_id, club_id)` | `player_season_stats` | One stat line per player per club per season |
| **Partial unique** one `is_default` plan per club | `tactical_plans` | `INS-11` |
| `version` exposed as a strong ETag and enforced as a concurrency token | `tactical_plans` | `CONC-1` (ADR-0009) |
| `unique (plan_id, slot_number)`, `unique (plan_id, assigned_player_id)` | `tactical_slots` | No duplicate shirt slots or players |
| `check (normalized_x between 0 and 10000)` etc. | `tactical_slots` | `TAC-9` |
| `unique (fixture_id, club_id)` | `fixture_team_sheets` | One sheet per club per fixture |
| **Partial unique** one active scheduling record per club | `training_plans` | Latest plan wins |
| Trigram/full-text index on player name | `players` | Scouting search |
| `unique (manager_id, player_id)` | `market.shortlists` | `SCT-3` (see §7 on the plan's internal conflict about this table's schema) |

> Contract/registration agreement (`SQ-6`) is enforced in the application transaction and by
> invariant integration tests, because a partial unique index alone cannot express "these two
> rows must agree on club and status".

### 3.3 Competition, matches, market, finance

```mermaid
erDiagram
    seasons ||--o{ division_seasons : "spans"
    divisions ||--o{ division_seasons : "plays"
    division_seasons ||--o{ club_season_entries : "contains"
    division_seasons ||--o{ matchdays : "schedules"
    division_seasons ||--o{ standings : "tabulates"
    seasons ||--o{ season_rollovers : "closed by"
    matchdays ||--|{ fixtures : "comprises"
    fixtures ||--o| input_snapshots : "freezes"
    fixtures ||--o| matches : "produces"
    matches ||--o{ events : "emits"
    matches ||--o{ lineup_participation : "records"
    events ||--o{ highlights : "yields"
    fixtures ||--o{ simulation_attempts : "retries"
    players ||--o{ transfer_listings : "listed"
    transfer_listings ||--o{ transfer_bids : "receives"
    transfer_listings ||--o| transfer_outcomes : "resolves"
    clubs ||--|| club_accounts : "banked"
    club_accounts ||--o{ ledger_entries : "records"
    clubs ||--o{ club_season_finances : "summarised"
    club_season_finances ||--|{ club_season_finance_lines : "totals"

    seasons {
        uuid id PK
        uuid world_id FK
        int sequence_number UK
        text display_label
        int game_year
        timestamptz starts_at
        timestamptz ends_at
        timestamptz rollover_ends_at
        text status
        text rule_set_version
    }
    divisions {
        uuid id PK
        uuid country_id FK
        int tier_number
        text display_name
        text status
        uuid created_season_id FK
        smallint capacity "18"
    }
    division_seasons {
        uuid id PK
        uuid division_id FK
        uuid season_id FK
        text status
        text schedule_seed
        text tie_draw_seed
        text tie_draw_hash
        timestamptz standings_finalized_at
    }
    matchdays {
        uuid id PK
        uuid division_season_id FK
        smallint round_number "1..34"
        timestamptz lock_at
        timestamptz kickoff_at
        text publication_status
    }
    fixtures {
        uuid id PK
        uuid matchday_id FK
        uuid home_club_id FK
        uuid away_club_id FK
        timestamptz kickoff_at
        text status "scheduled|locked|simulating|staged|published|void"
        smallint home_score
        smallint away_score
        uuid match_id FK
        timestamptz published_at
        boolean is_bootstrap "generated history (PYR-7)"
        bigint version
    }
    standings {
        uuid id PK
        uuid division_season_id FK
        uuid club_id FK
        smallint played
        smallint won
        smallint drawn
        smallint lost
        smallint goals_for
        smallint goals_against
        smallint points
        smallint yellow_cards
        smallint red_cards
        smallint current_rank
        bigint version
    }
    discipline_records {
        uuid id PK
        uuid division_season_id FK
        uuid player_id FK
        int yellow_cards
        int red_cards
        bigint version
    }
    season_rollovers {
        uuid id PK
        uuid world_id FK
        uuid season_id FK
        uuid next_season_id FK
        text phase "started|frozen|finalized|moved|completed|failed"
        text failure_reason
        timestamptz started_at
        timestamptz completed_at
        bigint version
    }
    input_snapshots {
        uuid id PK
        uuid fixture_id FK
        text engine_version
        text rule_set_version
        bytea seed_ciphertext
        text seed_commitment_hash
        jsonb snapshot
        text snapshot_hash
        timestamptz created_at
    }
    matches {
        uuid id PK
        uuid fixture_id FK
        text engine_version
        text presentation_version
        text seed_commitment
        timestamptz started_at
        timestamptz completed_at
        smallint home_score
        smallint away_score
        jsonb aggregate_statistics
        text input_hash
        text output_hash
        uuid simulation_attempt_id
    }
    events {
        uuid id PK
        uuid match_id FK
        int sequence UK
        smallint minute
        smallint second
        text event_type
        uuid acting_club_id FK
        uuid acting_player_id FK
        uuid secondary_player_id FK
        smallint x "0..10000"
        smallint y "0..10000"
        jsonb detail
        int detail_schema_version
    }
    transfer_listings {
        uuid id PK
        uuid player_id FK
        uuid seller_club_id FK
        bigint minimum_fee_minor
        bigint generated_buyer_wage_minor
        int generated_contract_seasons
        timestamptz opens_at
        timestamptz ends_at
        text status
        uuid resolution_job_id FK
        bigint version
    }
    transfer_bids {
        uuid id PK
        uuid listing_id FK
        uuid bidder_club_id FK
        bigint amount_minor
        bigint bid_sequence
        timestamptz placed_at
        text status "leading|outbid|won|released|invalid"
        text reservation_correlation_id
        bigint version
    }
    club_accounts {
        uuid id PK
        uuid club_id FK
        bigint cash_minor
        bigint reserved_minor
        bigint last_ledger_sequence
        bigint version
    }
    ledger_entries {
        uuid id PK
        uuid club_id FK
        bigint sequence UK
        text category
        bigint cash_delta_minor
        bigint reserved_delta_minor
        bigint resulting_cash_minor
        bigint resulting_reserved_minor
        text source_type
        uuid source_id
        text correlation_id
        text description_template
        jsonb description_parameters
        timestamptz created_at
    }
    club_season_finances {
        uuid id PK
        uuid club_id FK
        uuid season_id FK
        bigint opening_cash_minor
        bigint closing_cash_minor
        bigint version
    }
    club_season_finance_lines {
        uuid id PK
        uuid club_season_finance_id FK
        text category
        bigint cash_delta_minor
        bigint version
    }
```

**Critical indexes and constraints**

| Constraint | Table | Why |
|---|---|---|
| `unique (world_id, sequence_number)` | `seasons` | One season number per world |
| `unique (world_id, season_id)`, `check (phase in (...))` | `season_rollovers` | One rollover per closing season, and its checkpoint is a known phase (`PR-4`, ADR-0031) |
| `unique (country_id, tier_number)`, `check (tier_number >= 1)` | `divisions` | `WORLD-4`, no tier 0 |
| `unique (division_id, season_id)` | `division_seasons` | One instance per division per season |
| `unique (division_season_id, club_id)` | `club_season_entries`, `standings` | One entry and one standing per club |
| `unique (division_season_id, player_id)`, `check (yellow_cards >= 0 and red_cards >= 0)` | `discipline_records` | One accumulation per player per season (`DIS-2`) |
| `unique (division_season_id, round_number)` | `matchdays` | 34 rounds |
| `check (home_club_id <> away_club_id)` | `fixtures` | A club cannot play itself |
| `check (home_score >= 0 and away_score >= 0)` | `fixtures` | Nonnegative scores |
| Score required only when `status in (staged, published)` | `fixtures` | No half-resolved fixtures |
| Unique home/away pair per leg + one fixture per club per matchday | `fixtures` | Backed by generation validation plus database support where practical |
| Index `(club_id, kickoff_at, status)`, `(matchday_id, status)` | `fixtures` | Fixture lists, dashboard, matchday resolution |
| `unique (fixture_id)` | `input_snapshots`, `matches` | One snapshot, one match |
| `unique (match_id, sequence)` | `events` | Ordered event stream; `MAT-9` |
| Index `(match_id, minute)`, `(player_id, event_type)` | `events` | Commentary and stats |
| `unique (source_event_id)` where one highlight per event | `highlights` | Determinism |
| `check (minimum_fee_minor > 0)`, `check (ends_at > opens_at + minimum exposure)` | `transfer_listings` | `TRF-1`, `TRF-2` |
| **Partial unique** one active listing per player | `transfer_listings` | `TRF-14` |
| **Partial unique** one active bid per listing per club | `transfer_bids` | `TRF-6` |
| Index `(listing_id, status, amount_minor, bid_sequence)` | `transfer_bids` | `TRF-8` ordering |
| `unique (listing_id)` | `transfer_outcomes` | Resolution happens once |
| `check` the action is a known code and names exactly one of the listing or the bid; `check (length(inputs_hash) = 64)`; index `(club_id, evaluated_at)` | `ai_market_decisions` | `TRF-12`: a decision always states what it produced and what it read |
| `check (cash_minor >= 0 and reserved_minor >= 0)` | `club_accounts` | `FIN-13` |
| `unique (club_id, sequence)`, `unique (correlation_id, category)`, `check (resulting_cash_minor >= 0 and resulting_reserved_minor >= 0 and resulting_reserved_minor <= resulting_cash_minor)` | `ledger_entries` | `FIN-11`, `FIN-13`, `FIN-17` |
| `unique (club_id, season_id)` | `club_season_finances` | One finance summary per club per season (`FIN-19`) |
| `unique (club_season_finance_id, category)`, `check (category in (...))` | `club_season_finance_lines` | One total per category, and a known category (`FIN-19`) |

> **Stage 10 status.** The market tables are implemented as specified above, with one tidy-up:
> `transfer_listings` has no `resolution_job_id` column, because the resolution job's business key
> `listing:{id}:resolve` already identifies it durably, and a column naming it would be a second
> statement of the same fact (ADR-0003, ADR-0024). `shortlists` is owned by the `market` module, per §7.

> **Stage 12 status (rollover).** `competition.season_rollovers` is the checkpoint row for the season
> rollover state machine (ADR-0031): one row per closing season, guarded by a world-scoped advisory lock,
> advanced `started → frozen → finalized → squads → moved → completed`. `club_season_entries.final_rank`,
> `is_promoted`, `is_relegated`, `closing_reputation`, and `closing_cash_minor` are now written once, at
> rollover (`PR-4`), and never rewritten; the next season's entries, division-seasons, schedule, and opening
> table are new rows, so prior seasons remain immutable (`PR-6`). The rollover's continuity work landed too
> (ADR-0032): a `squads` phase between `finalized` and `moved` resolves contracts (`CON-6`, `CON-9`),
> retirements (`CON-10`, announced on `players.retirement_announced_season_number`), and emergency
> replacements (`SQ-8`); the finalize phase posts the position award (`FIN-5`) and writes a season finance
> summary (`FIN-19`). The retirement and contract-continuity policies are pure and versioned
> (`retirement-v1`, `ai-contract-v1`), and the rule set advanced to `world-rules-v9`.
>
> **Stage 12 status (season history).** No schema change: the closed entries are read back as a club's
> season history and next-season placement (`PR-4`, `PR-6`), and the retained `player_season_stats` rows are
> aggregated across seasons as a player's career (`STA-2`). The club-identity seed fix (ADR-0033) changes
> which names a provisioned tier generates, not the shape of any table; it advances the provisioning
> generator to `division-gen-v2`.

### 3.4 Communications and operations

```mermaid
erDiagram
    managers ||--o{ inbox_messages : "receives"
    managers ||--o| notification_preferences : "sets"
    jobs ||--o{ simulation_attempts : "drives"

    inbox_messages {
        uuid id PK
        uuid recipient_manager_id FK
        text category
        text template_key
        jsonb parameters
        uuid related_entity_id
        timestamptz created_at
        timestamptz read_at
        timestamptz updated_at
        bigint version
    }
    news_items {
        uuid id PK
        uuid world_id FK
        uuid country_id FK
        uuid division_id FK
        text category
        text template_key
        jsonb parameters
        timestamptz published_at
        timestamptz expires_at
    }
    notification_preferences {
        uuid id PK
        uuid manager_id FK UK
        boolean email_deadline_reminders
        boolean email_inactivity_warnings
        boolean email_market_messages
        boolean email_news_digest
        bigint version
    }
    jobs {
        uuid id PK
        text job_type
        text business_key UK
        jsonb payload
        timestamptz due_at
        smallint priority
        text status
        int attempt_count
        int max_attempts
        text lease_owner
        timestamptz lease_until
        text last_error
        timestamptz completed_at
    }
    outbox_messages {
        uuid id PK
        text type
        text aggregate_type
        uuid aggregate_id
        text correlation_id
        jsonb payload
        timestamptz occurred_at
        timestamptz published_at
        int attempt_count
        text lease_owner
        timestamptz lease_until
    }
    idempotency_records {
        uuid id PK
        uuid user_id FK
        text operation
        text idempotency_key UK
        text request_hash
        int response_status
        text response_body_hash
        text resource_reference
        timestamptz expires_at
    }
    audit_log {
        uuid id PK
        text actor_type
        uuid actor_user_id
        text action
        text target_type
        uuid target_id
        text correlation_id
        timestamptz occurred_at
        text ip_hash
        jsonb before_metadata
        jsonb after_metadata
        text reason
    }
    feature_flags {
        uuid id PK
        text scope
        text key UK
        jsonb value
        jsonb rollout_metadata
        bigint version
    }
    repair_actions {
        uuid id PK
        text incident_reference
        uuid approved_by_user_id FK
        text repair_type
        text target_type
        uuid target_id
        jsonb dry_run_result
        text execution_state
        jsonb compensating_action
    }
```

**Critical indexes and constraints**

| Constraint | Table | Why |
|---|---|---|
| `unique (job_type, business_key)` | `jobs` | Enqueue idempotency (`ADR-0003`) |
| Index `(status, due_at, priority)` | `jobs` | Ready-job claim path |
| Index `(status, due_at)` on unpublished outbox | `outbox_messages` | Dispatch path |
| `check (status in ('pending','published','dead_letter'))`, `check (attempt_count >= 0 and max_attempts >= 1)` | `outbox_messages` | An outbox row is in a known state with a real attempt budget (`MOD-4`) |
| Index `(division_id, published_at desc)` and `(country_id, published_at desc)` | `news_items` | The feed's two scopes, newest first (`COM-1`) |
| `check (expires_at is null or expires_at > published_at)` | `news_items` | A news item cannot expire before it is published |
| `unique (manager_id)` | `notification_preferences` | One preferences row per manager (`COM-8`) |
| `unique (user_id, operation, idempotency_key)` | `idempotency_records` | Reject key reuse with a different request hash |
| `check (category in (...))`, `check (length(template_key) > 0)` | `inbox_messages` | A stored shelf and template are codes, not ordinals |
| Partial index `recipient_manager_id where read_at is null` | `inbox_messages` | Unread badge and sync counter |
| Index `(recipient_manager_id, created_at, id)` | `inbox_messages` | Keyset page order, newest first |
| Append-only, restricted access | `audit_log` | Tamper evidence |

---

## 4. JSONB policy

Relational columns are used for identities, relationships, money, state, attributes used in
filters, tactics used in validation, table data, and anything used for concurrency. JSONB is
permitted **only** for:

| Allowed use | Table.column | Versioned by |
|---|---|---|
| Immutable match input snapshot | `match.input_snapshots.snapshot` | `engine_version` + `snapshot_hash` |
| Engine configuration snapshot/hash | `match.input_snapshots.snapshot` | `rule_set_version` |
| Typed match-event detail | `match.events.detail` | `detail_schema_version` |
| Semantic highlight keyframes | `match.highlights.keyframe_payload` | `presentation_version` |
| Notification/news template parameters | `comms.inbox_messages.parameters`, `comms.news_items.parameters` | template key |
| Audit before/after metadata where relational querying is not needed | `ops.audit_log.before_metadata`, `.after_metadata` | — |
| Job payloads | `ops.jobs.payload` | `job_type` |
| Aggregate match statistics and the player lines | `match.matches.aggregate_statistics` | `match-statistics-v3`, `engine_version` |

Binding rules:

| Rule | Statement |
|---|---|
| JSN-1 | Every JSONB document includes a schema or version discriminator. |
| JSN-2 | Every document type has application-level validation on write. |
| JSN-3 | Every document type has a golden deserialization test, including a rejected malformed case. |
| JSN-4 | None of the above may be promoted into a hot filter path; if a field must be filtered or sorted, it becomes a relational column. |
| JSN-5 | JSONB is never used as a shortcut for unfinished modelling. |

---

## 5. Projections and reconciliation

| Projection | Source of truth | Rebuild path |
|---|---|---|
| `competition.standings` | Published fixtures | `competition.rebuild-division-projections`, which recomputes it and asserts equality with the live projection (`TBL-13`, `TBL-14`) |
| `competition.player_season_stats` | Published match results and events | The same job, which recomputes every line and removes one no result supports (`STA-6`) |
| `competition.discipline_records` | Published cards and suspension service | Deferred to the rollover that owns the accumulation's reset (`DIS-3`): its consequence is a suspension already served, so replaying the count alone would create a new inconsistency |
| `finance.club_accounts.cash_minor` / `reserved_minor` | Append-only `finance.ledger_entries` | Ledger replay must reproduce both balances exactly (`FIN-18`) |
| `squad.player_state` | Published match participation plus daily progression | Replay of progression inputs for a bounded window |

A projection is treated as a cache of a derivable fact. Any repair that cannot be expressed
as a rebuild is an audited `ops.repair_actions` entry with a compensating action.

---

## 6. Migration policy

| Rule | Statement |
|---|---|
| MIG-1 | EF Core migrations live in Infrastructure and are reviewed as SQL in CI. |
| MIG-2 | Every migration documents forward validation and rollback/compensation notes. |
| MIG-3 | Breaking changes use expand → migrate → contract. No deployment requires a destructive same-step migration. |
| MIG-4 | Migrations seed only stable reference and rule data. World generation is an explicit idempotent application tool/job, never a migration. |
| MIG-5 | Production migrations run as a controlled pre-deploy job using the migration database role, never at application startup. |
| MIG-6 | A verified backup is taken before high-risk migrations and before season-engine data changes. |
| MIG-7 | Partial unique indexes, check constraints, and exclusion constraints are covered by integration tests against real PostgreSQL — never an in-memory provider. |

---

## 7. Resolved conflict in the master plan: `shortlists`

The master plan contradicts itself about the home of the shortlist table:

- §5.2 (bounded modules) assigns **scouting shortlists** to the `market` module and schema.
- §6.5 (relational data model) documents the table as **`squad.shortlists`**.

**Resolution:** the table is `market.shortlists`, owned by the `market` module.

**Reasoning:** module ownership is the stronger constraint, because `MOD-1` states that a
module's tables are written only by that module's code. Placing the table in `squad` while a
different module writes it would create exactly the cross-module write that ADR-0001 forbids,
and would force `squad` to absorb scouting concerns that the master plan assigns to the
market surface (§10.6 `GET/POST/DELETE /api/v1/shortlist`).

This is a schema-placement decision, not a behaviour change: the columns, uniqueness
(`unique (manager_id, player_id)`), privacy, and API surface are unchanged. It is recorded
here rather than silently applied because the master plan's §6.5 heading is normative text.
