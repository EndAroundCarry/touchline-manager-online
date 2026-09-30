# MVP Traceability

> Stage 0 exit criterion: **every public MVP feature maps to a module and a planned stage.**
> Source of truth for the feature list is master plan §2.2; §20 is the requirement checklist;
> §16 defines the stages.

---

## 1. Public MVP scope → module → stage

Every bullet of master plan §2.2, mapped. A feature is complete only when its stage closes
**and** the gate described in the last column has evidence.

| # | MVP feature (§2.2) | Module(s) | Stage | Completion evidence |
|---|---|---|---|---|
| F-01 | Account registration | auth | 2 | Integration + Playwright registration test |
| F-02 | Email verification | auth, comms | 2 | Verification token test; fake-email local workflow |
| F-03 | Login | auth | 2 | API integration test; lockout and rate-limit tests |
| F-04 | Session rotation | auth | 2 | Reuse-detection test revokes the family |
| F-05 | Password reset | auth | 2 | Single-use, expiry, enumeration-safe test |
| F-06 | Logout (single and all) | auth | 2, 13 | Revocation test; per-session revocation from the session list (`AccountManagementTests`, ADR-0036) |
| F-07 | Account settings | auth, web | 2, 13 | Settings screen acceptance; session list/revoke, machine-readable export, and locale/time-zone preferences (`AccountManagementTests`, ADR-0036) |
| F-08 | One active manager career per account | world, auth | 3 | Partial unique index test; `MANAGER_HAS_ACTIVE_CLUB` |
| F-09 | Country selection | world, web | 3 | Onboarding journey test |
| F-10 | Atomic takeover of an AI club | world | 3 | Concurrent-claim test: exactly one winner |
| F-11 | One persistent world, six national pyramids | world | 3 | Seeder test: 6 countries, 108 unique clubs |
| F-12 | One initial 18-club top division per country | world | 3 | Seeder validation |
| F-13 | Automatic lower-tier creation at 18/18 humans | world, competition, match | 3 (request), 11 (full) | Fill-tier-1 test creates exactly one tier 2; generic path test for tier 3 (`ProvisioningTests`, ADR-0030) |
| F-14 | AI control for vacant clubs | world, squad | 3, 8 | AI legal-side test with no privileged data |
| F-15 | Three weekly matchdays (Tue/Thu/Sun) | competition, ops | 6 | Schedule property tests; real-clock staging run |
| F-16 | Squad workflows | squad | 4 | Squad screen + legal-squad tests |
| F-17 | Player profiles and attributes | squad, web | 4 | Attribute display with non-colour indicators |
| F-18 | Lineup and team sheet | squad | 4 (default), 6 (fixture) | Validator accepts all presets, rejects invalid |
| F-19 | Tactics: formations, roles, instructions | squad | 4 | Validation + ETag conflict tests |
| F-20 | Training | squad | 4 (plan, focus, daily progression), 6 (match effects) | Deterministic progression test |
| F-21 | Availability: injuries and suspensions | squad, competition | 6 (apply), 8 (full) | Effect-on-correct-future-fixture-exactly-once test |
| F-22 | Contracts | squad | 4 (list/quote), 9 (full), 12 (rollover expiry) | Renewal determinism and rollover-expiry tests; the `squads` phase expires unrenewed contracts to free agency, renews unmanaged clubs (`AiContractPolicy`, `ai-contract-v1`), and repairs below-minimum clubs (`SQ-8`, ADR-0032) |
| F-23 | Finance workflows (cash, wages, income) | finance | 9 | Ledger replay reconstructs balances exactly |
| F-24 | League fixtures | competition | 6 | 34-fixture, one-home-one-away property tests |
| F-25 | League tables | competition | 6, 8 | All nine tie-break paths covered |
| F-26 | Results | competition, match, web | 6, 7 | Published matchday projection reconciliation |
| F-27 | Player and team statistics | competition, match | 8 | Rebuild equals live projection; the player profile reads a career derived by aggregating the same season lines across seasons (`STA-2`, `GetPlayer`) |
| F-28 | Promotion and relegation | competition, world | 12 | Three-up/three-down at every adjacent tier; `PromotionRelegation.Compute` (`promotion-relegation-v1`) plus the movement assertions in `SeasonRolloverTests` (ADR-0031); the rollover tells the manager of each moved attended club (`InboxTemplates.SeasonMovement`, `COM-1`) |
| F-29 | Season rollover | competition, finance, squad, world | 12 | Resumable checkpoint machine under the world advisory lock (`RunSeasonRollover`, ADR-0031, ADR-0032): a redelivered rollover is a no-op and an interrupted one resumes from its phase. The `squads` phase expires and renews contracts, retires announced players, and repairs below-minimum clubs (`CON-6`, `CON-9`, `CON-10`, `SQ-8`); finalize posts the position award (`FIN-5`) and writes season finance summaries (`FIN-19`); the real-PostgreSQL rollover test asserts each. The closed entries are read back as a club's season history and next-season placement (`GetClubSeasonHistory`, `PR-4`, `PR-6`). A non-production operator can preview what a rollover would do, run one on demand, and resume a failed one on a `Failed` checkpoint (`PreviewSeasonRollover`, `TriggerSeasonRollover`, `ResumeSeasonRollover`, `IJobQueue.RequeueAsync`, ADR-0034). Five consecutive seasons are rolled over by the worker alone, each asserted to reconcile and open a complete next season, and the whole run is checked for squad legality, ledger replay, movement counts, and history immutability (`StagingSeasonRunTests`, ADR-0035) |
| F-30 | Server-authoritative locking | ops, match | 6 | No public simulate endpoint; write-after-lock test |
| F-31 | Server-authoritative simulation | match, ops | 5, 6 | Golden hash determinism; publication atomicity |
| F-32 | Server-authoritative publication | competition, ops | 6 | Never 5-of-9 test |
| F-33 | Recovery from worker failure | ops | 6 | Kill/restart at lock, simulate, publish boundaries |
| F-34 | Text commentary | match, web | 5 (tokens), 7 (UI) | Variation and no-leakage tests |
| F-35 | Deterministic, replayable 2D highlights | match, web | 5 (director), 7 (viewer) | Payload budgets; replay determinism; the prepare-and-watch browser journey |
| F-36 | Search and scouting | market | 10 | Indexed search plan test; cursor pagination |
| F-37 | Shortlists | market | 10 | Private-visibility policy test |
| F-38 | Transfer listings | market | 10 | Eligibility and minimum-exposure tests |
| F-39 | Server-resolved timed auctions | market, finance | 10 | Deterministic winner with exact money movement |
| F-40 | AI market participation | market | 10 (deferred to the AI-market milestone) | AI uses no privileged finance; healthy market metrics |
| F-41 | Inbox / news notifications | comms | 6 (results), 8 (rollout), 11 (full) | Template + unread-sync tests; Stage 11 adds the news feed, notification preferences, deadline reminders, and outbox dispatch (ADR-0028, ADR-0029) |
| F-42 | Inactivity handling | world, comms, ops | 11 | Ladder tests: warn at 10 days, AI-assisted at 14, closed at 21; a suspended account is skipped (ADR-0027) |
| F-43 | Safe return-to-AI control | world, squad | 11 | A closure keeps every commitment; the freed club re-runs the capacity evaluation (ADR-0027) |
| F-44 | Responsive desktop/mobile/tablet UI | web | 1 (shell), 13 (complete) | Breakpoint E2E suite: the core journeys run at a desktop and a mobile project and a tablet project runs the `@responsive` spec, which asserts the shell's disclosure-versus-sidebar switch, the card-versus-table layouts, 44px touch targets, and no page-level horizontal overflow (`playwright.config.ts`, `support/navigation.ts`, `journeys/responsive.spec.ts`, ADR-0038) |
| F-45 | Installable PWA | web | 1 (shell), 7, 13 | Install/update/offline-read tests. Stage 13 adds the `SwUpdate` update prompt, offline mutation gating, the stale-read label, and a production-served PWA journey (`UpdateStore`, `SystemNotices`, `playwright.pwa.config.ts`, ADR-0037) |
| F-46 | Administration (jobs, matchdays, users, repairs) | ops, web | 14 | Stage 14 so far: roles are real and administered by the `access-admin` tool, `support`/`operator`/`admin` must complete a TOTP second factor, and the always-mapped `admin` route group exposes `GET /admin/health/game` (world/season/next kickoff and queue depth/dead letters) and the audited `POST /admin/users/{id}/suspend|restore`, every mutation requiring a fresh `X-MFA-Code`, a reason and an idempotency key behind `AdminRead`/`AdminMutate` (`tools/access-admin`, `AdminEndpoints`, `MfaStepUpFilter`, `MfaScenario`/`AdminEndpointsTests`, ADR-0042). The read console has since landed: the always-mapped group gains `GET /admin/jobs`, `GET /admin/matchdays/{id}`, and `GET /admin/audit`, keyset-paged with filters, behind `AdminRead` (`AdminQueries`, `AdminJobCursor`/`AdminAuditCursor`, `JobStatuses`, `AdminConsoleTests`, ADR-0043). The actions then landed: `POST /admin/jobs/{id}/retry`, `/admin/jobs/{id}/cancel`, and `/admin/matchdays/{id}/resume` return a dead letter, stop a stuck job, or requeue a stuck round's resolution or publication — behind `AdminMutate` and a fresh `X-MFA-Code`, adding the terminal `cancelled` job status and its migration (`RetryJob`/`CancelJob`, `ResumeMatchday`, `JobStatuses`, `Stage14JobCancellation`, `AdminRecoveryTests`, ADR-0044). The remaining §10.8 mutations and the web console follow |
| F-47 | Audit | ops | 2 (infra), 14 (complete) | Append-only, restricted, reason-required. Stage 14 adds the admin actions and their reasons (`admin.role.granted`, `admin.account.suspended|restored`, `auth.mfa.*`), asserted by `AdminEndpointsTests` against `ops.audit_log` (ADR-0042). The append-only trail now has its read: a keyset-paged, filterable search behind `AdminRead` that withholds the hashed IP and the repair metadata (`AdminConsoleTests`, ADR-0043). The recovery commands add `admin.job.retried`, `admin.job.cancelled`, and `admin.matchday.resumed`, each carrying its reason, asserted against `ops.audit_log` by `AdminRecoveryTests` (ADR-0044) |
| F-48 | Observability | ops, all | 1 (logging/traces), 14 (dashboards/SLO) | Metrics exist; alerts link to runbooks |
| F-49 | Backup and restore | ops | 14 | Restore drill meets integrity checks |
| F-50 | Deployment | ops | 14, 16 | Migration preflight, staged rollout, rollback drill |
| F-51 | Incident controls (read-only/maintenance) | ops, web | 14 | Read-only mode blocks writes, keeps reads |
| F-52 | Accessibility (WCAG 2.2 AA) | web | 13 | Axe gate over the core routes at desktop and mobile, plus the Canvas viewer on the matchday stack: zero WCAG 2.2 A/AA violations, a focus-ring guard, the squad roster named, scrollable table regions focusable, a keyboard path to move a tactics slot, and focus moved to `<main>` on navigation (`support/accessibility.ts`, `journeys/accessibility.spec.ts`, `matchday/matchday.spec.ts`, ADR-0039) |
| F-53 | Guided in-app help (rules, deadlines, tactics, market, season cadence) | web | 13 | A `/help` reference over the five promised subjects, each stating the settled rule and linking to the screen that owns it, with the copy asserted against the disclosure boundary; session-dismissed first-steps guidance on the dashboard; and deadlines that name their time zone (`features/help/`, `formatDeadline`, `journeys/help.spec.ts`, the `/help` entry in `journeys/accessibility.spec.ts`, ADR-0040) |
| F-54 | Product analytics (privacy-safe operational funnels) | ops | 13 | Onboarding and retention funnels read as counts over rows the game already writes, behind the first role-gated endpoint (`GET /ops/analytics/funnels`, `operator`/`admin`), with the same transitions counted on the OTel meter (`IOperationalAnalyticsQueries`, `IOperationalMetrics`, `OperationalAnalyticsQueriesTests`, `OperationalMetricsTests`, `AnalyticsTests`, ADR-0041) |

---

## 2. Requirement checklist mapping (master plan §20)

| Requirement | Stages | Module(s) | Where it is specified |
|---|---|---|---|
| Old-school core management loop | 4, 8, 9, 10, 13 | squad, competition, finance, market, web | Master plan §2.4, product §2.2 F-16…F-29 |
| Online accounts and real managers | 2, 3 | auth, world | ADR-0002, game rules §4 |
| Persistent MMO / server authority | 3, 6, 11, 12, 14 | world, competition, ops | ADR-0001, ADR-0003, threat model §4.2 |
| Top five European countries plus Romania | 3 | world | `WORLD-2` |
| One initial division per country | 3 | world | `WORLD-5` |
| Add tier N+1 as the lowest tier fills | 11 | world, competition, match | ADR-0005, `PYR-1`…`PYR-14` |
| AI vacant clubs and inherited state | 3, 8, 11 | world, squad | `WORLD-9`, `INS-12` |
| Three matchdays weekly | 6 | competition, ops | `CAL-1`, `CAL-2` |
| Squad/player management | 4 | squad | `SQ-1`…`SQ-9` |
| Lineups, formations, roles, tactics | 4, 6 | squad | `TAC-*`, `INS-*` |
| Training, fatigue, condition, morale | 4, 6, 8 | squad | `TRN-*` |
| Injuries, cards, suspensions | 5, 6, 8 | squad, competition, match | `DIS-*` |
| Fixtures, tables, statistics | 6, 8 | competition | `CAL-*`, `TBL-*` |
| Deterministic server match engine | 5, 6 | match | ADR-0004, `MAT-*` |
| Text commentary | 5, 7 | match, web | `MAT-8`, master plan §8.6 |
| 2D highlights | 5, 7 | match, web | ADR-0006 |
| Scouting / search / shortlists | 10 | market | `SCT-1`…`SCT-3` |
| Timed transfer auctions | 10 | market, finance | `TRF-1`…`TRF-15` |
| Contracts and wages | 4, 9 | squad, finance | `CON-*`, `FIN-7` |
| Basic finances | 9 | finance | `FIN-1`…`FIN-18` |
| Inbox/news | 6, 8, 11 | comms | Master plan §6.9 |
| Inactivity handling | 11 | world, comms | `OCC-1`…`OCC-9` |
| Promotion/relegation/season rollover | 12 | competition, world, finance | `PR-*`, master plan §7.5, ADR-0031, ADR-0034 |
| Responsive installable PWA | 1, 7, 13 | web | ADR-0007 |
| Fully fictional data | 3, 4 | world, squad | §1 of content policy, `FIC-1`…`FIC-10` |
| Android and iOS after MVP | 20, 21 | web (Capacitor) | ADR-0007 |
| Staff / youth / cups / social later | 17, 18, 19 | — | Master plan §2.3, §16 |
| Security / admin / observability / backups | 2, 6, 14 | ops, auth, all | Threat model, data classification, ADR-0042, ADR-0043 |
| Accessibility (WCAG 2.2 AA) | 1, 13 | web | Master plan §11.3, §15.6, `F-52`, ADR-0039 |
| Guided in-app help | 13 | web | Master plan §16 Stage 13, §2.4, `F-53`, ADR-0040 |
| Product analytics (privacy-safe operational funnels) | 13 | ops | Master plan §16 Stage 13, §14.1, `LGL-5`, `F-54`, ADR-0041 |

---

## 3. Non-goals → guardrails

Every master plan §2.3 non-goal, with the mechanism that keeps it out of the MVP. These are
enforced rather than remembered.

| Non-goal | Guardrail |
|---|---|
| Real clubs/players/marks/badges/kits | `FIC-1`…`FIC-5`, name-pool blocklist test |
| Multiple world shards | One world in production; `world_id` present for test worlds only (`WORLD-1`) |
| User-created clubs, badge/kit uploads, renaming | No upload endpoint exists; club identity is generated and immutable |
| Cups, continental competitions, national teams, friendlies | Not in the domain model; a new competition type requires an ADR |
| Staff hiring and attributes | No tables; squad module has no staff aggregate |
| Youth academy and intake | Not in the domain model; Stage 17 |
| In-match tactical changes, WebSockets, synchronous PvP | No socket transport; team sheets lock before kickoff (`CAL-3`, `MAT-6`) |
| Private negotiations, agents, loans, swaps, clauses, installments, windows | `TRF-13`; only the auction path exists |
| Fog-of-war attributes, scouted network | `SCT-1`, `SCT-2`; attributes are exact and public |
| Stadium/facilities/sponsorship negotiation, merchandising, taxes, currencies, debt | `FIN-14`; one currency, fixed baselines only |
| Social chat, forums, PMs, associations, UGC | No such endpoints; `FIC-9` |
| Native Android/iOS packages | ADR-0007: Capacitor follows the PWA (Stage 20) |
| Offline mutations | ADR-0007: mutations disabled offline, no queue |
| Native AOT as a release gate | ADR-0004 note; no AOT gate exists in CI |
| Guaranteed zero-cost production | ADR-0008: cost model required, no `$0` claim |
