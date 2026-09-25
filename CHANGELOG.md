# Changelog

Notable changes by stage. The stage numbering follows
[`docs/product/master-plan.md`](docs/product/master-plan.md) §16.

## Stage 4 — Squads, contracts, tactics, and training foundations

A world you inherit a squad from. Every seeded club now owns a legal twenty-two-player senior squad,
generated from the same world seed as its identity, so a manager who takes a club over inherits players
rather than a name. This is the first of the stage's milestones: the schema and the generation the rest
of the stage writes and reads.

### Added

- **The `squad` schema** (shipped first, ahead of the rest of the stage): `players`, `player_attributes`,
  `player_state`, `player_contracts`, `player_registrations`, `player_unavailability`, `tactical_plans`,
  `tactical_slots`, `training_plans`, `player_training_focus`, `fixture_team_sheets`, and
  `team_sheet_entries`, in one migration applied against real PostgreSQL 17 before being committed. The
  range checks (`TRN-4` on all twenty-eight attributes, `TRN-5`…`TRN-7` on state, `TAC-9` on slot
  coordinates), the partial unique indexes (`SQ-6` one active contract and one active registration per
  player, `INS-11` one default plan per club), and the slot and team-sheet uniqueness constraints are all
  in the database rather than in a convention.
- **The player aggregates with the rules as behaviour**: `Player` (identity, physique, positions, and
  the two hidden C2 values), `PlayerAttributes` over a twenty-eight-attribute set with a canonical order
  and a checksum that makes an out-of-band edit visible, `PlayerState` (condition, fatigue, morale,
  sharpness in basis points, and the carried development remainder), `PlayerContract` (a 1–3 season term,
  `CON-1`), `PlayerRegistration` (eligibility from a fixture boundary, `SQ-7`), `PlayerUnavailability`
  (measured in fixtures, not days, `TRN-12`), and the tactics and training rows the later milestones
  write into: `TacticalPlan` with the six presets and the eight instructions, `TacticalSlot`,
  `FixtureTeamSheet`, `TeamSheetEntry`, `TrainingPlan`, `PlayerTrainingFocus`, and `SquadLegality`.
- **Deterministic squad generation.** `PlayerGenerator` and versioned `PlayerNamePools` and
  `PlayerAttributeProfiles`: three goalkeepers, seven defenders, seven midfielders, and five attackers
  per club, an age spread, per-position attribute emphasis, state, a wage, and one active contract and
  registration each. Every value is a pure function of the seed, the club's ordinal within its country,
  its tier, and the season's game year — keyed on the ordinal rather than the club id, because ids are
  UUIDv7 and differ per run while the logical squad must not.
- **Names that cannot duplicate themselves inside a squad**, by construction rather than by retry: the
  given-name and surname pools are coprime and larger than a squad, so a club's consecutive ordinals
  visit distinct pairs — the same argument the club name pools already make. A name that collides with
  the fictional-data blocklist advances deterministically to the next candidate for the same seed
  (`FIC-6`), which today's pools do not trigger.
- **The seeder generates the squads.** Running `npm run seed` now creates 2,376 players for the 108
  clubs, and `world.generation_runs` records the count alongside the clubs and accounts (`SQ-1`).
- 59 new domain tests (234 total) covering generation determinism, the golden squad, squad legality and
  composition, the attribute, state, contract, position, and tactics invariants, and the name pools'
  blocklist and injectivity; and 14 new infrastructure tests over real PostgreSQL covering the squad
  constraints, the team-sheet round trip, and the seeded squads.
- **ADR-0011**, on hidden player values as server-only columns rather than a restricted table, and on
  contract/registration agreement as an application invariant rather than a database constraint.
- **The squad, player, and contract reads** (second milestone; master plan §10.3): `GET
  /clubs/{clubId}/squad`, `GET /players/{playerId}`, and `GET /contracts`, each answering only for the
  club the caller actually holds. The squad response carries the state and contract each row needs and a
  legality summary, so the screen can warn about a squad below the minimum without recomputing `SQ-2`.
- **The ownership refusal, by name** (master plan §10.9, §15.4): `CLUB_NOT_MANAGED` for another club and
  `NO_CLUB` for an account that holds none, because "your view is stale" and "you have nothing yet" send a
  manager to two different screens and one forbidden response would leave the client guessing.
- **The `/squad` and `/players/:id` screens.** The squad table is PrimeNG's, the app's first PrimeNG
  component, lazy-loaded on its route; sorting is the table's own and announces its direction through
  `aria-sort`, and filtering is client-side over a response already bounded to 25. The player profile
  renders all twenty-eight attributes grouped by family beside the state, contract, and registration.
- **Attribute display with non-colour indicators** (F-17, master plan §11.3): an `AttributeValue`
  component renders every attribute as its number *and* the word for its band, and state values are shown
  on the 0–100 scale the API converts to (`TRN-8`). A unit test asserts both halves render, so a band that
  only changed the colour would fail.
- **The C2 guard `data-classification.md` §2.1 asks for**, now that a player response exists: a
  reflection test over the contracts assembly that fails if a squad DTO ever grows a `Potential` or
  `Reputation`, or exposes a basis-point field, plus an API test that reads the player payload as raw JSON
  and asserts neither hidden value crossed the wire.
- 8 new API integration tests (48 total), 3 data-classification tests, 20 new frontend tests (77 total),
  and 2 Playwright journeys (14 total) covering the squad screens and their refusals.

### Notes

- **The squad constants went into `WorldRuleSet`, and its version is now `world-rules-v2`.** `RULE-1`
  asks for one versioned rule set, and the file's own documentation says a stage's constants arrive with
  that stage. A world already stamped `world-rules-v1` keeps being read against v1, which is the
  versioning model working rather than a migration.
- **A `GenerationRun` records `world-gen-v2`.** The bootstrap now produces clubs *and* squads, so the
  version it records is the bootstrap's, and the sub-generators' versions are folded into the input hash.
  The Stage 3 test that pinned the club generator's version moved with it.
- **Shortlists are not here.** `modules.md` gives `market.shortlists` to the market module, and search
  and shortlisting land in Stage 10, so the squad schema is twelve tables rather than thirteen.
- **The team-sheet tables ship before fixtures do.** `competition.fixtures` arrives in Stage 6, so
  `fixture_team_sheets.fixture_id` and `player_unavailability.source_fixture_id` carry no foreign key
  yet. Stage 3 set the precedent by shipping the competition and finance shells with their stage, and the
  fixture-independent lineup work needs somewhere to land. ADR-0011 records it.
- **`training_plans` is one current row per club**, updated in place with a bumped version. The
  data-model phrase "partial unique … latest plan wins" is read as "one row per club", matching the
  entity definition, which has no supersede column; plan history is a Stage 12 concern.
- **No club is given a tactical or training plan yet.** A default plan appears when a manager first sets
  one (the tactics milestone) or when the AI does (Stage 8); Stage 12 requires "a default tactic" only
  when it rolls seasons.
- **The player generator reuses `Pcg32` and `DeterministicDigest` rather than a third copy.** The FNV-1a
  seeding that was private to `ClubIdentityGenerator` moved onto `DeterministicDigest` as `SeedOf`, and
  the Stage 3 golden name tests are what prove the extraction changed nothing.
- **The C2 test landed with the squad reads, as ADR-0011 predicted.** It cannot pass vacuously — a third
  assertion fails if the squad contracts are renamed or made internal — and it is scoped to the squad
  namespace rather than the whole assembly, because a club's public reputation is a legitimate
  `Reputation` property on a world DTO and is C0.
- **The squad reads are own-club only.** The squad list carries condition and the contract list carries
  wages, both C1 ("readable where the viewer is authorized — for example own club"), and §10.9 asks
  resource policies to enforce club ownership. The public, unattached player profile is Stage 10's
  scouting surface (`SCT-1`), which is why `GET /players/{playerId}` is scoped to the owner here.
- **State comes back on the user-facing scale, not in basis points** (`TRN-8`: the API converts; the
  database is authoritative). The conversion lives once, in the application mapper, and the response has
  no basis-point field — a rule the data-classification test now enforces across every squad DTO.
- **A squad row does not carry the attribute grid.** The table is about readiness — who is available,
  tired, or expiring — and the grid is the player profile's job. Attribute-based sorting is the Stage 10
  search surface, which will need indexed queries anyway.
- **The squad table needed PrimeNG's template-reference API, not `pTemplate`.** PrimeNG 22 reads its
  slots through `contentChild('header')` and friends, so an `ng-template pTemplate="header"` compiles and
  then renders nothing — a silent empty table. The header, body, and empty-message templates are declared
  as `#header`, `#body`, and `#emptymessage`. Worth knowing before the next dense screen.
- **The initial bundle budget moved from 500 kB to 550 kB.** Using PrimeNG puts its table styling into the
  global stylesheet, which is part of the initial payload; the table's JavaScript stays in the lazy
  `/squad` chunk. The initial total is 524 kB (123 kB transferred), so the new threshold is a deliberate
  26 kB of headroom rather than a blanket relaxation, and `maximumError` is unchanged at 1 MB.
- **`GET /clubs/{clubId}` is deliberately not in this milestone.** §10.3 lists it, but no screen needs it:
  the dashboard already composes the club, country, division, season, and finances.
- **A world seeded by an earlier generator has no players, and the seeder will not add any.** It is
  idempotent, so it reports the world and stops. An environment carrying a `world-gen-v1` world needs a
  fresh database (or a reset) before the squad screens have anything to show; the end-to-end suite in this
  change was verified against a freshly seeded one.
- **Deferred to the rest of Stage 4:** the tactics presets, slots, validator, and ETag contract (delivered
  in the next milestone, below); the training endpoints and the deterministic daily progression job behind
  a feature flag; and the contract renewal quote. **Deferred beyond it:** fixtures, match effects, full
  finances, transfers, and the public scouting surface.

### Tactics

A club you can shape. A manager now saves a formation, a slot layout, the roles, the eight team
instructions, and the default eleven, on a plan whose `version` is the contract that stops two devices
overwriting each other. This is the second of the stage's milestones: the model, the validator, and the
API. The `/tactics` screen — the drag-and-drop board and its keyboard alternative — is the rest of it.

#### Added

- **The formation presets as data** (`TAC-1`…`TAC-6`): `FormationLayouts` holds each preset's eleven slots
  — family, role, and normalized coordinates — and checks its own table the first time it is used, so a
  preset that names not-eleven slots, repeats a number, puts a role in the wrong family, or strays off the
  pitch fails by name rather than deep inside a save. Slot 1 is the goalkeeper in every preset.
- **The tactics validator** (`TAC-7`…`TAC-10`, `INS-10`, `INS-12`): one pure function over plain slot
  values and two sets of facts — who is selectable and who is unavailable. It refuses the wrong slot
  count, duplicate slot numbers, coordinates off the pitch, two slots on the same point, a role that
  disagrees with its family, a repeated player, a player who is not a selectable member of the club, an
  unavailable player, and a half-filled lineup. An out-of-position player is deliberately **not** an
  issue: `INS-10` makes familiarity a penalty the engine applies, not a reason to refuse a side.
- **The tactics API** (master plan §10.4): `GET /tactics` — the club's plans, the squad they are picked
  from, and every preset's own arrangement — plus `POST /tactics`, `PUT /tactics/{planId}`, and
  `POST /tactics/{planId}/make-default`. The first plan a club saves becomes its default (`INS-11`), and
  making one default demotes the previous one in a single transaction with the demotion committed first,
  because the partial unique index is checked per statement.
- **The ETag contract** (`CONC-1`, ADR-0009): a plan's `version` is its strong entity tag. `GET` returns
  it in the body, both writes require it in `If-Match` (answered `428` without it), and a stale one is
  answered `412`. `squad.tactical_plans.version` is now a concurrency token, so a raced save is refused by
  the database and not only by the use case's own comparison — an empty migration records the token in the
  model snapshot.
- **The validation preview**: a refused plan answers `400 PLAN_VALIDATION_FAILED` with the issues as
  stable codes, each naming its slot and player, so the screen can draw them on the pitch rather than
  render a field message.
- 15 new domain tests (249 total) for the preset layouts and every validator rule; 2 new infrastructure
  tests (89 total), one of which is the only place the concurrency token itself can be observed; and 9 new
  API integration tests (57 total) covering the create/revise/default lifecycle, the `412`/`428`/`404`
  refusals, and the validation preview.

#### Notes

- **The preset layouts are the server's, not the client's.** `GET /tactics` returns every preset's default
  arrangement, so the screen renders a formation without reproducing eighteen coordinates, and the same
  numbers reach the renderer, the input snapshot hash, and — in Stage 5 — the engine (`TAC-9`).
- **Tactical zones are not modelled yet.** `TAC-7` speaks of dragging "within validated tactical zones";
  this milestone enforces the bounds and the unambiguous half of the overlap rule — two slots on one point
  — but there is no zone map, so a dragged slot is not constrained to its preset's region. Zone geometry
  arrives with the pitch model that gives it a consumer.
- **The lineup is all-or-nothing, and the domain decides it.** A request's lineup may name any number of
  slots; whether the eleven are full is `TAC-10`, and the domain answers it with `SELECTION_INCOMPLETE`, so
  the pitch's rules live in one place instead of being split between a field validator and the domain.
- **`TacticalSlot.Reshape` was added** so a formation change re-lays a plan's existing slots in place
  rather than deleting and reinserting them. Slot numbers stay stable, which is what keeps a team sheet
  prepared against a plan version referring to the same eleven positions (`data-model.md` §3.2).
- **`CurrentSeasonQuery` was extracted from the squad queries.** The tactics read resolves the same "season
  in progress", and a second copy would be where the two answers quietly diverged.
- **The migration is deliberately empty.** Marking the plan version a concurrency token changes the model,
  not the schema; the migration exists to record it in the model snapshot, and its `Up` says as much.
- **The API tests are tolerant of a re-used club.** The world is seeded once for the whole API collection
  and a released club keeps the plans its previous manager saved, so the create test reads whether a
  default already exists rather than assuming the club is fresh. The alternative was a test that passed
  only on its first run.
- **Deferred:** the `/tactics` screen (the board, the role and instruction pickers, the keyboard
  alternative, and the `412` reapply UX) closes this milestone; the training endpoints and the
  deterministic daily progression job follow; then the contract renewal quote.


## Stage 3 — World generation, six countries, clubs, and onboarding

A world you can onboard into. Six fictional national pyramids are generated from one seed, a manager
creates a profile, chooses a country, takes over an AI club in its lowest active tier, and inherits it
exactly as it stands. A country that fills with humans queues the generation of the next tier underneath
it.

### Added

- **The schema the rest of the stage writes into** (shipped first, in its own commit): the `world` schema
  (game worlds, countries, manager profiles, clubs, club tenures, division provisioning requests,
  generation runs), the `competition` shell (seasons, divisions, division-seasons, club season entries),
  and the `finance` shell (club accounts), in one migration applied against real PostgreSQL 17 before
  being committed. The versioned `WorldRuleSet` holds every constant from game rules §3, and its version is
  stamped onto the world and each season, so a historical season is interpreted against the rules that were
  actually in force when it was played.
- **The world aggregates with the rules as behaviour**: `GameWorld` (freeze/resume), `Country`, `Manager`
  (the resignation cooldown), `Club` (tier-scaled baselines and a slug derived from the name rather than
  supplied beside it), `ClubTenure` (the whole of the ownership model), `DivisionProvisioningRequest`,
  `GenerationRun`, `SeasonCalendar` (a pure function from a first matchday to a season's whole window),
  and a `CountryCapacity` value object that answers "does this country have room, and should it grow?" in
  one place instead of in an endpoint.
- **Mappings that put the plan's constraints in the database rather than in a convention**: partial unique
  indexes for one open tenure per club and per manager (`OCC-9`), unique `(country_id, target_tier)` for
  provisioning (`PYR-3`), unique per-world club name and slug, one club per season, and the `FIN-13`
  checks that neither balance can go negative and reservations cannot exceed the cash behind them.
- **Deterministic world generation.** `Pcg32`, a pinned, explicitly tested PRNG, plus versioned fictional
  name pools for all six locales (`england`, `spain`, `germany`, `italy`, `france`, `romania`), a curated
  blocklist of real football identities, and `ClubIdentityGenerator`. Every generated value is a pure
  function of the seed, the country's pool, and the club's ordinal within its country, so the same seed
  reproduces the same pyramid and different seeds produce visibly different ones.
- **Club names that cannot collide, without a retry loop.** Places and suffixes are woven by ordinal rather
  than multiplied, so a division alternates both instead of naming eighteen clubs after one place; the
  combination cycle is their least common multiple, which makes the pairing injective and the names unique
  by construction. Past the cycle the generator adds a qualifier and then a numeral, so a deep pyramid runs
  out of names only by running out of integers (`PYR-11`).
- **The world seeder**, as a use case and as `tools/world-seeder`. `--seed` and `--first-matchday` override
  the configuration; everything else comes from `World:*`. It is idempotent — running it against an
  existing world reports that world and writes nothing (`WORLD-1`) — and it records the seed, generator
  version, and a digest of the non-seed inputs in `world.generation_runs` (`PYR-14`).
- **The onboarding commands**: `CreateManagerProfile`, `ClaimClub`, and `ResignClub`. The takeover validates
  the club is an active, AI-controlled member of the country's lowest active tier, disables a manager who
  already holds a club or is serving the resignation cooldown, and returns the inherited club.
- **`CapacityEvaluator`**, which answers "should this country grow?" in one place. It runs after every
  successful takeover and after every resignation, and creates the next tier's
  `DivisionProvisioningRequest` exactly once (`PYR-1`, `PYR-2`).
- **Advisory locks**: `IAdvisoryLock` and `AdvisoryLockKey`, implemented over
  `pg_advisory_xact_lock`. A takeover takes the manager's lock and then the country's, always in that
  order, which is what keeps the pair deadlock-free (`PYR-3`).
- **Explicit transactions** on `IUnitOfWork` (`BeginTransactionAsync`, `IDatabaseTransaction`,
  `TransactionIsolation`), so a workflow that must hold a lock across more than one save can.
- **The onboarding reads**: world, countries, per-country capacity, available clubs, onboarding state, and
  the inherited-club dashboard, each one query shaped for one screen.
- **The API** from master plan §10.2, plus `GET /clubs/{clubId}/dashboard`. Claim refusals carry the stable
  codes §7.6 requires — `CLUB_ALREADY_CLAIMED`, `MANAGER_HAS_ACTIVE_CLUB`, `MANAGER_PROFILE_REQUIRED`,
  `MANAGER_IN_COOLDOWN`, and `CAPACITY_PROVISIONING` — and the last two carry the moment the cooldown
  lapses and the provisioning request's state and polling hint (`PYR-10`).
- **The Angular onboarding screens** (`/onboarding/manager`, `/onboarding/country`, `/onboarding/club`) and
  a dashboard that routes on state: no profile, no club, or a club. `OnboardingStore` holds the reference
  data and the claim's idempotency key; `requireVerifiedEmail` keeps a manager who cannot claim away from
  the screen that would ask them to.
- 109 new domain tests (175 total) covering the PRNG's pinned sequences, the generator's determinism,
  uniqueness, deep-pyramid behaviour, and the blocklist; 38 world infrastructure tests over real
  PostgreSQL, including the concurrent-takeover and fill-a-tier races; 5 API onboarding tests; 14 new web
  tests (57 total); and a Playwright onboarding journey.
- **ADR-0010**, on why a takeover serialises with an advisory lock rather than with `SERIALIZABLE`.

### Fixed

- **The worker's composition root could not resolve `IRequestContext`.** The API registered an HTTP-backed
  implementation and nothing registered a default, so any use case that writes an audit row would have
  failed the moment the worker ran one. `ServiceRequestContext` is now the default, and the API's own
  registration overrides it — which is the arrangement `IRequestContext`'s own documentation described and
  nothing implemented.

### Notes

- **The onboarding endpoints sit at the version root, not under `/api/v1/world`.** Master plan §10.2
  addresses them as resources (`/countries`, `/club-claims`, `/club-tenure`), and the committed path is the
  contract. It is the same reasoning that puts `/me` at the root.
- **A takeover holds an advisory lock in a read-committed transaction rather than a serializable one.**
  §7.6 asks for serializable, but a snapshot-isolation transaction fixes its snapshot at its first
  statement — which in this workflow is the lock request — so it would still be reading pre-lock state
  after the lock was granted, and the losing manager would get a constraint violation instead of the
  documented refusal. ADR-0010 records the decision; the partial unique indexes are untouched and remain
  the backstop.
- **The deterministic PRNG in `Domain` will not be the match engine's.** The engine ships its own
  `Pcg32` in Stage 5, and that is deliberate: a generated world's reproducibility and a played match's
  reproducibility are separate versioned contracts, and the engine's output hashes are pinned per engine
  version (ADR-0004). The dependency rules settle the question anyway — `Domain` depends on nothing (DEP-1)
  and the engine may not depend on it (DEP-2).
- **Ordinals are per country and never restart per tier.** Tier 1 takes 0–17, tier 2 takes 18–35. If each
  tier restarted at zero, every provisioned tier would propose the same eighteen names and the unique name
  index would reject the second one. A test generates twelve tiers and asserts the names never repeat.
- **Name pools use invented places only, and never a real club's home town.** Generation is where licensed
  identity would enter the product, so the pools are the primary defence and the blocklist is the backstop
  (`FIC-5`). A test runs the blocklist check across every pool and every generated name.
- **A club's founding game year is the first season's game year.** A plausible older founding date would
  have to be invented from the seed, and inventing history is not what a reproducibility rule should be
  doing with its randomness.
- **The tier-1 money, stadium, and reputation baselines are provisional.** They halve per tier, which is the
  shape Stage 9 needs, and they are recorded in `game-rules.md` as balancing values rather than as rules.
  Calibrating them is Stage 9's multi-season simulation work, not a guess made now.
- **Provisioning is requested, not executed.** The takeover creates the `DivisionProvisioningRequest` and
  returns `CAPACITY_PROVISIONING` with its state; generating the tier, its squads, and its backfilled
  results is Stage 11. Until then a full country stays full, on purpose: a half-built tier that a manager
  could claim would be worse than a wait.
- **Only a club claim requires a verified address.** Master plan §12.1 lists club claims, bidding, listing,
  and display-name changes; creating a manager profile and resigning are authenticated but not
  verification-gated, because an unverified account can reach neither a club nor a bid.
- **The dashboard shows only what exists yet.** Identity, competition placement, control, and money. Squad,
  contracts, fixtures, and history join the response in the stages that create them rather than appearing
  now as permanently-empty fields.
- **The Playwright journey gives its club back.** It resigns at the end, which exercises the resignation
  path and returns the club to the pool. Without that, each run would consume one of the 108 clubs and the
  suite would start failing after eighteen runs for a reason that is not a defect.
- **`appsettings.json` had to travel with the seeder tool.** The generic host does not copy it the way the
  web SDK does, so the project declares it explicitly and the tool sets its content root to the binary's
  directory rather than the process's working directory.
- **`game_worlds` has no JSONB feature-flags column**, although master plan §6.3 lists one. Feature flags
  belong in `ops.feature_flags`, where they are queryable and versioned; a JSONB blob on the world row is
  precisely the unfinished modelling the JSONB policy forbids (`JSN-5`).
- **`club_season_entries` carries `season_id` as well as the division-season.** Without it, "one club
  appears in exactly one division per season" is not expressible as a database constraint: a promotion bug
  could enter one club into two divisions in the same season and every standings query would double-count
  it.
- **A failed provisioning request can be retried, and the retry reuses the recorded seed.**
  `unique (country_id, target_tier)` means a second request for the same tier cannot exist, so without that
  transition one failed run would block the tier permanently. Reusing the seed is what keeps `PYR-14` true
  across attempts. A domain test caught the gap.
- **Tier names are descriptive rather than evocative** — "England Top Division", "Spain Division 2" — so no
  generated name can drift towards a real competition's branding (`WORLD-3`).
- **A seeded division starts active and a provisioned one does not.** Stage 11's tier stays in
  `provisioning` until its generation, validation, and backfill all complete (`PYR-8`); the tier the seeder
  creates is claimable the moment it exists, because there is nothing left to backfill.

## Stage 2 — Identity and authenticated walking skeleton

The auth module: the account schema, the full credential lifecycle, session rotation and reuse
detection, the request security the rest of the product will build on, the Angular screens that drive
it, and the browser journeys that prove it works end to end.

### Added

- `auth` schema: `users`, `user_roles`, `refresh_sessions`, `email_tokens`, `user_consents`, with
  unique indexes on normalized email and display name, a unique index on each token hash, a check
  constraint on the status lifecycle, and restrictive foreign keys to `users` (ADR-0002).
- `ops.audit_log` and an `IAuditWriter` that stages audit rows **in the same unit of work** as the
  change they describe, so an action without an audit trail is impossible rather than merely
  discouraged. Auth actions (register, verify, login outcomes, rotation, reuse, logout, reset,
  profile change, deletion) are recorded with actor, target, correlation ID, and a hashed IP.
- Domain auth aggregates with the lifecycle as behaviour: `User` (register, verify, lockout,
  suspend/restore, change password, display name, deletion), `RefreshSession` (issue, rotate,
  revoke), `EmailToken` (issue, consume, supersede), `UserConsent`, `UserStatusRules`, and a pure
  `AccountLockoutPolicy` escalation ladder.
- Use cases for the whole lifecycle: `RegisterUser`, `VerifyEmail`, `ResendVerificationEmail`,
  `Login`, `RefreshAccessToken`, `Logout`, `LogoutAll`, `ForgotPassword`, `ResetPassword`,
  `GetProfile`, `UpdateProfile`, `DeleteAccount`, plus the shared `EmailTokenIssuer` and
  `SessionIssuer`. Each returns an explicit outcome rather than throwing for expected results.
- FluentValidation request validators with shared email, display-name, and password rules, returning
  per-field Problem Details that the client can render against individual form controls.
- Infrastructure providers: `IdentityPasswordHasher` over ASP.NET Core's password hasher with an
  explicit work factor and transparent rehash, `SecureTokenService` (CSPRNG tokens, SHA-256 storage
  hashes, HMAC client fingerprints), `JwtAccessTokenIssuer` (HS256 with the security stamp embedded),
  `SmtpEmailSender` (MailKit), and the EF Core repositories implementing the auth ports.
- The endpoints from master plan §10.1, including `GET/PATCH/DELETE /api/v1/me`.
- Bearer authentication that re-reads the account on every authenticated request and compares the
  security stamp, which is what makes a stateless token revocable. Policies for a verified manager
  and for the `admin`, `operator`, and `support` roles — the groundwork for the Stage 14 MFA
  requirement.
- Response security headers (`X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, a
  `default-src 'none'` CSP, and `Permissions-Policy`), plus rate limiting on the auth endpoints.
- The HttpOnly, `Secure`, `SameSite=Lax` refresh cookie, scoped to the refresh path, with `Secure`
  relaxed only in Development where the client is served over plain HTTP.
- A fake-email local workflow: `Email__Host`/`Email__Port` point at the Compose mail catcher, and the
  integration tests substitute a recording sender.
- The Angular auth screens — register, sign in, confirm email, forgotten password, reset password, and
  settings — as standalone components with reactive forms. Field messages are resolved in one place:
  the client's own rule wins while the manager is editing, and the server's per-field `errors` map
  takes over afterwards.
- The `SessionStore`, which holds the access token in memory and nowhere a template or another script
  can reach it, restores the session before the first route activates, and **shares one rotation
  between concurrent callers**. Without that sharing, a screen firing three requests on load would
  present an already-consumed refresh token and trip the server's reuse detection against its own
  legitimate user.
- The route guards in both directions, and a `returnUrl` that is honoured only when it is a
  same-origin absolute path — navigating to an arbitrary value would turn the sign-in screen into an
  open redirect.
- The token interceptor: attaches the bearer token, keeps auth endpoints out of both the token and the
  retry (a rejected sign-in is an answer, not an expiry), recovers from an expired token exactly once,
  and ends the session when the rotation fails instead of retrying forever.
- The Playwright suite in `tests/web-e2e`. It owns its stack — `globalSetup` brings up PostgreSQL and
  the mail catcher and applies migrations, then the config starts the API and the web client — and it
  reads the confirmation and reset links out of the mail catcher rather than being handed a token.
  Ten journeys cover registration and confirmation, the session surviving a reload, the unconfirmed
  account's restriction, account closure, renaming with the shell following, signing out everywhere,
  password replacement with a single-use link, and the guards including the external-`returnUrl` case.
- Frontend unit coverage for the session store, the guards, the interceptor, and the field-message
  precedence (43 web tests in total, up from 8).
- An `e2e` CI job that installs Chromium, typechecks the suite, runs the journeys, and uploads the
  report on failure.

### Notes

- **Access tokens carry a security stamp that is checked against the database on every authenticated
  request.** This costs one indexed primary-key lookup and is what makes suspension and
  "sign out everywhere" take effect immediately instead of after the token expires. A cache belongs
  here only when profiling shows it is needed.
- **`DELETE /api/v1/me` takes the confirming password in a JSON body.** ASP.NET Core refuses to infer
  a body parameter for `DELETE`, so the binding is explicit.
- **Only the auth endpoints are rate limited for now.** The broader per-route and per-user limits are
  Stage 14 work, where they can be tuned from load evidence instead of guessed at. The permit limit is
  configuration, so load and functional tests raise it rather than tripping it.
- **`refresh_sessions.revocation_reason`** is an addition to the column list in master plan §6.2:
  reuse detection has to distinguish "rotated" from "signed out" to know whether a presented token is
  a replay, and the reason is also what an operator needs when investigating a session incident.
- `Auth:SigningKey` is validated at startup in every environment. Development has a committed
  dev-only value, following the precedent of the local database password; every other environment
  must supply its own.
- **Playwright arrives in Stage 2, not Stage 7.** `docs/testing/test-strategy.md` originally deferred
  journeys until the match viewer, but master plan §16 makes the full auth lifecycle a Stage 2 exit
  criterion and `docs/product/mvp-traceability.md` asks for a registration journey at F-01. The test
  strategy has been corrected. The remaining master plan journeys still arrive with the screens they
  exercise, because a journey for a screen that does not exist tests nothing.
- **The journey stack raises `RateLimiting:AuthPermitLimit`.** The suite signs in far more often from
  one address than a person would, and a throttle that fired halfway through would look like a bug in
  the feature under test. The limiter is still tested — by the API integration tests, which build a
  host with a deliberately tiny limit and can therefore assert on it directly.
- **The suite is one worker, deliberately.** Every journey shares one database and one mail catcher,
  and a test that read another test's message out of the mailbox would fail for a reason that is not
  real.
- Anonymization after the deletion cooling period, session listing on the settings screen, and TOTP
  enrolment remain outstanding; they belong to the privacy workflow (Stage 14) and the settings
  screen (Stage 13).

### Fixed

- **The settings screen told managers the manager-name field was unavailable while leaving it
  editable.** The input carried both `formControlName` and a `[disabled]` binding. A reactive form
  owns the disabled property — `FormControlName` re-applies the control's own state on every change —
  so the binding was silently ignored. An unconfirmed manager could type a new name into a field the
  same screen said was locked, and only the save button stopped them. The control's own state is now
  set from the profile, and an end-to-end journey asserts the field really is disabled.
- The migration for the auth tables initially omitted the foreign keys to `auth.users`; the migration
  was regenerated rather than patched, since it had not been applied anywhere.

## Stage 1 — Monorepo scaffold and engineering guardrails

### Added

- Solution `TouchlineManager.slnx` with `Domain`, `Application`, `Infrastructure`, `Contracts`,
  `MatchEngine`, the `Api` and `Worker` composition roots, seven test projects, and two tool
  projects. Identifier naming uses the real product name throughout.
- Central package management (`Directory.Packages.props`), lockfiles, and a pinned SDK
  (`global.json`). `TreatWarningsAsErrors` with .NET analyzers at `Recommended`.
- Durable job queue on PostgreSQL: `ops.jobs` with enqueue idempotency on
  `(job_type, business_key)`, `FOR UPDATE SKIP LOCKED` claims, leases with expiry, exponential
  backoff with jitter, and dead-lettering for permanent domain failures (ADR-0003).
- Job poller as an Infrastructure hosted service, registered only by the worker composition root so
  the API can never execute a deadline.
- API composition root: validated options that fail startup when misconfigured, correlation
  middleware, RFC 9457 Problem Details, liveness/readiness/detailed health endpoints, CORS
  allowlist, OpenAPI in Development, and one route group per bounded module.
- Log redaction filter shared by both composition roots, enforcing the redaction list in
  `docs/security/data-classification.md` §4, with tests.
- Angular 22 PWA: standalone components, strict TypeScript and strict templates, PrimeNG 22 with
  Aura and Tailwind 4, documented CSS layer ordering (`tailwind-base, primeng, tailwind-utilities`),
  service worker and manifest, responsive shell with safe-area insets, skip link, reduced-motion
  handling, connectivity banner and correlation-ID footer.
- `IClock` seam with a fake clock in tests; no code path uses server local time.
- Docker Compose providing PostgreSQL 17 and a mail catcher, plus `npm run dev` as the single
  command that starts the backing services, the API, the worker and the web client.
- GitHub Actions pull-request pipeline: locked restore, `dotnet format` verification, Release build
  with warnings as errors, all test projects, generated migration SQL as a review artefact, and the
  Angular typecheck/build/test job.
- Architecture tests that fail the build when the dependency rules in
  `docs/architecture/modules.md` §2 are crossed.

### Fixed

- `global.json` pinned an SDK version the installed preview sorts below, so no SDK resolved.
- `EnqueueNoOpJob` and the job handler registry were registered as singletons while consuming the
  scoped `DbContext`. Use cases are now scoped, and the worker resolves handlers from each job's own
  scope. Caught by the API integration tests at startup.
- The log redactor missed `refresh_token`-style keys, because `_` is a word character and therefore
  provides no word boundary for a plain `\btoken\b` pattern.
- The private-field naming rule also applied to `const` and `static` fields, which are PascalCase by
  convention. Only private instance fields take the `_` prefix now.
- Local PostgreSQL publishes host port **55432**, not 5432. Docker Desktop will happily bind a second
  listener on an already-occupied host port, after which connections land unpredictably — observed
  here when 5433 was chosen and a second process bound it too. A port outside the standard and
  ephemeral ranges removes the class of problem.
- Both `package.json` files carry an `.npmrc` setting `include=dev`. A shell exporting
  `NODE_ENV=production` otherwise silently omits devDependencies, producing an install that cannot
  build.

### Notes

- EF Core is pinned to 10.0.4 to match the Npgsql provider's floor, so the graph contains exactly
  one EF Core version.
- FluentAssertions is pinned to 7.2.0. Version 8 changed to a licence that is not free for
  commercial use.
- Container images for the API, worker and web, and separate database roles for migrations and
  runtime, are deferred to Stage 14 where they are built and exercised for real rather than
  half-implemented now.
- `docs/product/match-engine.md` and the operations runbooks are intentionally absent; they are
  Stage 5 and Stage 14 deliverables.

## Stage 0 — Product rules, architecture, and executable specifications

### Added

- `docs/product/master-plan.md` adopting the approved plan.
- `docs/product/game-rules.md`: the normative rule set, with stable references for every
  configurable value, a consolidated constant table, and an explicit list of values deliberately
  left to balancing.
- ADR-0001 … ADR-0009 covering the modular monolith, authentication and sessions, the PostgreSQL
  job queue, the deterministic match engine, dynamic pyramid expansion, semantic highlights,
  PWA-first delivery, deployment topology, and time/identity/concurrency conventions.
- C4 context and container diagrams, the module map, and the first ER diagrams.
- Glossary; UI tone and fictional-data policy; disaster/recovery-relevant threat model and data
  classification; MVP traceability for 51 features plus a guardrail per non-goal.
- `docs/product/stage-0-review.md`: the consistency review, the resolved contradiction about where
  shortlists live, and the three decisions requiring a product owner's answer.

### Fixed

- Master plan §5.2 and §6.5 disagreed about the schema for shortlists. Resolved to
  `market.shortlists` and recorded, because placing it in `squad` while the market module writes it
  would violate the module-ownership rule.
- Closed two gaps that would otherwise have produced two implementations: what happens at the top of
  a pyramid (`PR-9`, `PR-10`) and whether an unserved suspension carries across rollover (`DIS-8`).
