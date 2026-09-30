# Backup and restore

How the game's data is backed up, how a restore is proven, and what a restored copy must not do. This
is the `F-49` deliverable of Stage 14; the drill itself is
[`infra/restore-drill/`](../../infra/restore-drill/drill.mjs), and the decision is recorded in
[ADR-0046](../architecture/adr/0046-load-supply-chain-and-restore-drills.md).

## The strategy

Production runs **managed PostgreSQL with automated backups and point-in-time recovery** in the same
region as the API and worker ([ADR-0008](../architecture/adr/0008-deployment-topology.md)). The provider
keeps a rolling window of base backups and continuous WAL, so the database can be recovered to any
instant inside that window. There is no self-managed backup script in the repository, because there is
no self-managed database.

Two properties the product depends on shape the requirement:

- **No accepted bid, club claim, published fixture, or finance posting may be lost** (threat model §5).
  The target is a recovery point of **five minutes** — the smallest window that keeps a matchday's worth
  of results inside one backup interval.
- **Deadlines are durable rows**, so a restore that is minutes old simply re-runs the jobs that had not
  completed; the recovery *time* objective is **one hour** to a serving API.

## The drill

`npm run drill:restore` exercises the whole procedure locally, on real PostgreSQL 17, because a backup
nobody has restored is not a backup. It:

1. stands up an archive-enabled primary and seeds a world;
2. writes a `before` marker into `ops.restore_drill_sentinel`;
3. takes a base backup with `pg_basebackup --wal-method=stream`;
4. records the recovery target instant `T` and writes an `after` marker;
5. forces the WAL past `T` to archive;
6. recovers a fresh cluster from the base backup, replaying the archive to `T` and promoting;
7. clears job leases (the restored-environment rule below) and runs the integrity checks.

It tears its containers and volume down when it is done, on success and on failure alike. It is local
and manual; nothing runs it in CI yet.

### The integrity checks

`infra/restore-drill/checks.sql` fails the drill, naming every broken check, unless all of these hold on
the recovered cluster:

| Check | Invariant |
|---|---|
| Ledger replay | `finance.club_accounts` cash and reserved are exactly the sum of their `finance.ledger_entries` (`FIN-18`) |
| Last entry balance | The last entry's stored resulting balances equal the account's (`FIN-11`, `FIN-12`) |
| Publication atomicity | A published round has nine published fixtures — never five of nine (`CAL-10`, `MAT-7`) |
| Job queue integrity | No duplicate `(job_type, business_key)`, no lease without an owner, no unknown status |
| No leases after sanitize | The restored copy is safe to open (ADR-0008) |
| Point-in-time target | The `before` marker is present and the `after` marker is absent |
| World shape | Six countries and at least the seeded tier of clubs |

The point-in-time check is the one that proves the restore stopped where it was told to, rather than
replaying the whole archive.

## Recovering a real environment

The provider's procedure is the same shape as the drill; only the command names change.

1. **Choose the target instant.** For a bad deploy, it is the moment before it; for corruption, the
   last known-good instant. Record why.
2. **Recover a new instance, never the live one.** Restore into a fresh database so the original stays
   available for diagnosis.
3. **Apply the restored-environment rules** (ADR-0008), or the copy will act on the live game:
   - disable outgoing email, so no reminder or notification is sent from a restored world;
   - clear every job lease (the drill's `sanitize.sql` does this);
   - point the DNS/connection string at the restored instance only once the next steps pass.
4. **Run the integrity checks** against the restored instance — the same `checks.sql`, or its queries
   adapted to the provider's connection. A restore that fails a check is not a recovery.
5. **Reconcile before serving.** Publication, rollover, auction, and finance workflows are idempotent
   on their business keys, so the worker re-runs what the restore lost rather than duplicating it.
   Confirm the queue drains and the projected tables reconcile before reopening to managers.
6. **Record the evidence.** The recovery instant, the checks' output, and the reconciliation result.

## Retention

The provider's backup window is the retention policy; it is sized so the closed-beta and launch worlds
are covered by at least a week of point-in-time recovery. The retention window and its cost are tracked
in the cost model ([ADR-0008](../architecture/adr/0008-deployment-topology.md) requires one; it is a
Stage 14 follow-up).
