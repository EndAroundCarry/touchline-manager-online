-- The integrity checks the restore drill runs against the recovered cluster (master plan §16 Stage 14;
-- `F-49`). Each check proves an invariant that must survive a point-in-time restore; the script fails
-- loudly, naming every broken one, rather than reporting a number.
--
-- Run it after sanitize.sql, against the restored database, with `psql -v ON_ERROR_STOP=1`.
\set ON_ERROR_STOP on

begin;

create temp table drill_check (
  name text primary key,
  failed_rows bigint not null,
  detail text not null
) on commit drop;

-- FIN-18: the append-only ledger replays to the stored balances exactly.
insert into drill_check (name, failed_rows, detail)
select
  'FIN-18 ledger replay',
  count(*),
  coalesce(string_agg(drift.club_id::text, ', '), 'every balance replays')
from (
  select a.club_id
  from finance.club_accounts a
  left join (
    select club_id,
           sum(cash_delta_minor) as cash,
           sum(reserved_delta_minor) as reserved,
           max(sequence) as last_sequence
    from finance.ledger_entries
    group by club_id
  ) l on l.club_id = a.club_id
  where a.cash_minor <> coalesce(l.cash, 0)
     or a.reserved_minor <> coalesce(l.reserved, 0)
     or a.last_ledger_sequence <> coalesce(l.last_sequence, 0)
) drift;

-- FIN-11/FIN-12: the last entry states the balance the account actually holds.
insert into drill_check (name, failed_rows, detail)
select
  'FIN-11 last entry balance',
  count(*),
  coalesce(string_agg(a.club_id::text, ', '), 'last entries agree')
from finance.club_accounts a
join lateral (
  select resulting_cash_minor, resulting_reserved_minor
  from finance.ledger_entries e
  where e.club_id = a.club_id
  order by e.sequence desc
  limit 1
) last on true
where last.resulting_cash_minor <> a.cash_minor
   or last.resulting_reserved_minor <> a.reserved_minor;

-- CAL-10/MAT-7: never five of nine — a published round has nine published fixtures.
insert into drill_check (name, failed_rows, detail)
select
  'Publication atomicity',
  count(*),
  coalesce(string_agg(m.id::text, ', '), 'every published round is complete')
from competition.matchdays m
where m.publication_status = 'published'
  and (select count(*)
       from competition.fixtures f
       where f.matchday_id = m.id and f.status = 'published') <> 9;

-- The job queue is sound: no duplicate business key, no leased job without a lease, no unknown status.
insert into drill_check (name, failed_rows, detail)
select
  'Job queue integrity',
  count(*),
  coalesce(string_agg(job_type, ', '), 'sound')
from (
  select job_type from ops.jobs group by job_type, business_key having count(*) > 1
  union all
  select job_type from ops.jobs where status = 'leased' and (lease_owner is null or lease_until is null)
  union all
  select job_type from ops.jobs
   where status not in ('pending', 'leased', 'completed', 'dead_letter', 'cancelled')
) broken;

-- ADR-0008: a restored copy is opened with no lease still held.
insert into drill_check (name, failed_rows, detail)
select 'No leases after sanitize', count(*), 'none leased'
from ops.jobs where status = 'leased';

-- Point-in-time correctness: the marker written before the target is present and the one written after
-- it is not. This is what proves the restore stopped where it was told to.
insert into drill_check (name, failed_rows, detail)
select
  'Point-in-time target',
  case when (select count(*) from ops.restore_drill_sentinel where label = 'before') = 1 then 0 else 1 end
    + case when (select count(*) from ops.restore_drill_sentinel where label = 'after') = 0 then 0 else 1 end,
  'the pre-target marker is present and the post-target one is absent';

-- The world is intact: six countries and at least the seeded tier of clubs.
insert into drill_check (name, failed_rows, detail)
select
  'World shape',
  case when (select count(*) from world.countries) = 6 then 0 else 1 end
    + case when (select count(*) from world.clubs) >= 108 then 0 else 1 end,
  'six countries and at least 108 clubs';

do $$
declare
  failures text;
begin
  select string_agg(name || ' (' || failed_rows || ') — ' || detail, E'\n' order by name)
    into failures
    from drill_check
   where failed_rows > 0;

  if failures is not null then
    raise exception E'restore integrity checks failed:\n%', failures;
  end if;
end $$;

select name, failed_rows, detail from drill_check order by name;

commit;
