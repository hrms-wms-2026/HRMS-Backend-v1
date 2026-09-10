# Schema Snapshots

Point-in-time, per-domain documentation of the PostgreSQL schema, built from
`information_schema` + `pg_catalog`. This is a **static snapshot**, not a live
diagram &mdash; see [`SPEC.md`](./SPEC.md) for the full rules, and
[`viewer-mockup.html`](./viewer-mockup.html) for the design the viewer follows.

## Layout

```
schema-snapshots/
  SPEC.md                     the rules (change rarely, PR + review like code)
  viewer-mockup.html          reference design for the viewer
  schema-dump.sql             the extraction query (SPEC.md section 4)
  generate.mjs                runs the query, writes everything below
  generate.ps1 / generate.sh  one-line wrappers

  raw/<migration_id>/
    meta.json  columns.json  constraints.json     raw extract (SPEC.md section 9.1)

  <migration_id>-<YYYY-MM-DD>/
    <domain>.md   x13          one Markdown doc per domain (SPEC.md section 3-7, 11)
    viewer.html                the browsable page (SPEC.md section 12)
```

## Regenerate

**When:** after an EF migration lands on the shared / main environment &mdash;
**not per PR** (SPEC.md section 9.6).

```bash
node schema-snapshots/generate.mjs        # any OS - needs node + psql
```
```powershell
pwsh schema-snapshots/generate.ps1        # Windows
```
```bash
bash schema-snapshots/generate.sh         # macOS / Linux
```

Connection comes from the repo-root `.env` (`ONEVO_DB_*`, same values
`ops/postgres/setup-local-db.ps1` uses). Point at another database with
`ONEVO_DB_NAME=OnevoDb_other node schema-snapshots/generate.mjs`.

The run prints a per-domain table count and compares it to SPEC.md section 8;
the total must be **163**. A mismatch means the scope filter or the domain
buckets in `generate.mjs` need attention.

## Review checklist (SPEC.md section 9.4)

- [ ] Documented base-table count == 163 (generator prints it).
- [ ] No cross-domain edges drawn inside any `erDiagram` &mdash; only the two
      per-table reference tables.
- [ ] Every cross-domain reference row has an `On Delete` value.
- [ ] Header (`DB` / `Last migration` / `Snapshot generated`) present on each `.md`.

## Commit

Commit the new `<migration_id>-<YYYY-MM-DD>/` folder (13 `.md` + `viewer.html`)
and its `raw/<migration_id>/`. Keep only the last few snapshots &mdash; older ones
are superseded by newer migration ids (SPEC.md section 10).

## Domain buckets

The 13 domains are assigned by the ordered regex rules in the `DOMAINS` array
near the top of `generate.mjs` (rules, not a hand-kept list &mdash; SPEC.md
section 1). A table whose name matches nothing lands in **Other**; add or widen a
rule and rerun. `EXPECTED` in the same file holds SPEC.md section 8's counts for
the sanity check.

## Not covered

Per SPEC.md section 2: views, functions, triggers, non-unique indexes, CHECK /
EXCLUSION constraints, RLS policies, sequences, generated columns, enum value
lists, app-level-only associations, row data, external systems, and migration
diffs. Composite FKs are collapsed to one relationship.
