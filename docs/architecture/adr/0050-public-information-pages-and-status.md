# ADR-0050: Player-facing information pages and a public service status

- **Status:** Accepted
- **Date:** 2026-10-01
- **Stage:** 15
- **Related:** [ADR-0002](0002-auth-and-session-model.md), [ADR-0036](0036-account-sessions-export-and-preferences.md),
  [ADR-0040](0040-guided-help-and-first-steps.md), [ADR-0041](0041-privacy-safe-operational-funnels.md),
  [ADR-0047](0047-incident-read-only-mode.md), master plan §16 Stage 15, §12.4, `F-55`, `LGL-1`–`LGL-4`,
  `VOI-2`–`VOI-4`, `ACC-6`

## Context

Stage 15 asks for "player-facing rules/privacy/terms/status/support pages". None existed, and the gap was
concrete rather than cosmetic: the registration screen asks a manager to accept "the terms of service and the
privacy policy" and there was **no page to link to**, no footer link, and no public endpoint a signed-out
client could call. `docs/product/content-and-fictional-data-policy.md` §5 lists the missing gate directly —
"Terms, privacy, retention, and fictional-data statement published" — and `data-classification.md` §7 requires
that retention values "documented before launch" be fixed before Stage 15.

Three facts shaped the answer. The guided help (`F-53`, ADR-0040) had already set the pattern for
authored-as-data, server-independent copy that a spec test pins against the disclosure boundary. The server
records the terms and privacy versions a consent accepts (`LGL-1`, `Auth:TermsVersion`/`Auth:PrivacyVersion`)
but never exposed them, so a client could only guess them. And the one operator read of game health
(`GET /admin/health/game`) sits behind `AdminRead` and a completed second factor, so it is no use to a visitor.
There was no anonymous read in the product at all.

## Decision

**1. Five player-facing pages, four of them authored as data.**

`/rules`, `/privacy`, `/terms` and `/support` are one component (`features/info/`) driven by
`INFO_DOCUMENTS`, selected by the route's `data.document`. They read nothing from the server, following
`features/help/`, so a page can never render stale, empty or half-loaded, and a new page is a route plus a
document. The copy is asserted against the content policy by a spec test, as the help copy is (`F-53`).

**2. The pages are unguarded, so the register consent can reach them.**

They are shell-wrapped children of the root layout with no `canActivate`, like `/welcome`, because the whole
point is that a visitor who has not yet accepted anything can read what they are accepting. The shell footer —
always rendered, signed in or out — links all five.

**3. One anonymous read serves the live status and the versioned documents.**

`GET /api/v1/status` is the product's first anonymous endpoint. It carries the server's instant, whether the
game is in read-only maintenance and the operator's stated reason (`F-51`), the running season, the next
round's kickoff, and the terms and privacy versions. It adds no table and no repository: the read-only state
comes from the incident reader, the season from the world, and the next round from
`IMatchdayRepository.ListPendingLockingBetweenAsync`, the same reuse the stepped clock's status uses
(ADR-0049). One contract, one query, one test, one public surface.

**4. The version a page shows is the server's, not authored beside its copy.**

The terms and privacy pages display `Auth:TermsVersion`/`Auth:PrivacyVersion` from the status read, so the page
cannot disagree with the consent the server recorded (`LGL-1`). A version authored in the client would be a
second answer to one question and the first thing to drift.

**5. The retention values live in one place and are restated on the page.**

`data-classification.md` §3 fixes the two rows that were "documented before launch" — security/IP and device
hashes at 90 days, support correspondence at 24 months — and the privacy page states the same values, so the
spec test fails if the page and the policy ever disagree (`LGL-2`–`LGL-4`).

**6. Support is instructions, and the reference is the payload.**

There is no support address or messaging channel in the product, so the support page explains how to describe a
problem and tells a manager to quote the reference the footer already shows. The submission tool is the
separate feedback/report deliverable of this stage, not a contact address invented here.

**7. The anonymous read is not rate-limited, deliberately.**

It is a small read over a five-second-cached flag and existing tables, and it is the page a person opens when
something is already wrong; a limiter there would fail the one request that matters. This is recorded rather
than left implicit, and the read is `Cache-Control: no-store` because its whole value is being current.

## Consequences

**Positive**

- A signed-out visitor can read the rules and the legal documents, and the registration consent finally points
  at the pages it names. The content-policy gate is closed and executable.
- The version on the page is the version the consent records, by construction rather than by convention.
- The pages are cheap and safe: four are static data with no server dependency, and the one live page reads a
  single anonymous endpoint that carries only public game data and operational facts.
- The retention question is answered once, in the policy and on the page, and a test keeps them in step.

**Negative**

- **The status page's liveness depends on the client polling.** It reads once on open and on a visible-tab
  interval; it is not pushed. That is enough for a page a person opens to check, and it avoids a socket
  transport the product does not have.
- **The content policy is enforced by a spec test, not by the type system.** A forbidden token is caught when
  the spec runs, the same guard the help copy has (`F-53`).
- **The anonymous read is a new public surface.** It is bounded to counts, instants and document versions, and
  a test asserts no address can appear in it, but it does widen what an unauthenticated caller can reach —
  which is the point of a public status page.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Author the terms/privacy version in the client beside the copy | It is a second source of truth for the version a consent records; the server already owns the value (`LGL-1`), so the page reads it. |
| One endpoint per concern (status, legal versions) | Two public surfaces and two contracts for what one small read answers together; the page that needs a version is the same visitor the status page serves. |
| Gate the pages like `/help` (`requireAuthentication`) | The register consent is signed out, so the pages it links to must be reachable signed out; gating them recreates the original gap. |
| Serve the operator health read publicly | It is `AdminRead` + second factor by design, and it carries queue and dead-letter detail a visitor has no need of. |
| Render `docs/product/game-rules.md` on the rules page | It is the internal, reference-coded rule set, not player voice; the page restates the settled rules in the content policy's voice instead. |
| Put the retention periods only in the policy document | The privacy page has to state them, so they would then exist twice; fixing them in §3 and pinning the page to them keeps one answer. |
| Invent a support email address | No support channel exists; naming one that is not staffed is worse than explaining how to describe a problem and what to quote. |
