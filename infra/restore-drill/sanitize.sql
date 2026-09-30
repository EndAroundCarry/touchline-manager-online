-- The adjustments a restored copy needs before it is opened (ADR-0008): a lease held by a worker that
-- no longer exists would block the job it belongs to, so every lease is cleared and the job returns to
-- the queue. Outgoing email is disabled in configuration, not here, and the procedure in
-- docs/operations/backup-and-restore.md says so.
\set ON_ERROR_STOP on

update ops.jobs
   set status = 'pending',
       lease_owner = null,
       lease_until = null,
       updated_at = now(),
       version = version + 1
 where status = 'leased';
