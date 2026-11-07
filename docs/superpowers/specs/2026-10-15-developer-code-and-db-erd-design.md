# Developer ERD: code model and applied database snapshot

Date: 2026-10-15

Status: Design approved in conversation; implementation not started
Audience: ONEXSO developers maintaining the backend schema

## Intent and success criteria

Developers need one browser page to understand the ONEXSO schema while continuing development. After they pull a schema-changing commit, its **Code schema** view must already reflect that commit. They also need an **Applied DB snapshot** view showing what a particular PostgreSQL database actually contains. Schema-related work should run only when a developer changes schema, not on unrelated pushes. The page is a developer artifact, not a tenant/admin product route. It must expose no database credential or row data.

Success means a developer can open the committed page locally, select either view, search for a table/column, navigate domain-scoped relationships and cross-domain FKs, and see provenance. A schema change without a regenerated Code view must fail a schema-scoped CI check. An ordinary application-only PR must not run ERD generation or its CI check. A DB snapshot must identify the applied migration and generation time so it cannot be mistaken for the current code model.

## Existing system and decision

The backend already has `schema-snapshots/generate.mjs`, a PostgreSQL catalog extraction query, Markdown per-domain reports, `viewer.html`, `viewer-mockup.html`, and `SPEC.md`. That generator reads `information_schema`/`pg_catalog` using `psql`, captures the latest `__EFMigrationsHistory` row, and produces a **point-in-time database snapshot**. Its README and `SPEC.md` §9.6 intentionally regenerate only after migration reaches the shared/main database, not per PR. Keep that contract for the Applied DB view. Its existing HTML uses a Mermaid CDN, so offline developer viewing needs a pinned local renderer asset or an explicitly documented internet requirement; the implementation plan should prefer local assets.

Add a second, code-derived source. Use EF Core's *relational model metadata* for the checked-out backend revision rather than scraping C# migrations with regex. Extract table and column names/types/nullability, PK/unique/index markers, FKs with target columns/cardinality/delete action, and table grouping. Include a deterministic source-model fingerprint; optionally show a Git revision only when the generation process can prove which committed revision it represents. A timestamp belongs in non-compared provenance metadata, not the deterministic schema payload. If model construction requires a connection string, use a nonconnecting design-time configuration; the generator must never call `Migrate`, open a production connection, or read `.env` secrets to build the Code view. If a raw-SQL migration creates physical objects absent from EF metadata, mark the Code view as an EF-model projection and explain that the Applied DB view is authoritative for such objects. Do not claim code-view equality with a deployed database.

## User experience

Extend the existing viewer layout rather than create a second design system. The header has two explicit choices: **Code schema** (model fingerprint, optional proven revision) and **Applied DB snapshot** (database label, migration ID, UTC captured time). A stale/missing DB snapshot displays a visible state and never silently substitutes Code data. Each view offers domain navigation with table count, table/column search, a relationship diagram for the selected domain, collapsible table details, and cross-domain references with delete rule. Show PK/FK/unique keys in text and icon/badge, not color alone. For large domains, render only the selected domain and its immediate cross-domain reference list; do not draw the entire database as one unreadable graph. Keyboard navigation and local file opening must work. No row values, employee records, or secrets enter generated assets.

The existing Applied DB generator can keep its versioned `raw/<migration_id>` and `<migration_id>-<date>` outputs. A stable committed developer entry point under `schema-snapshots/` should select the latest committed Code output and latest available DB snapshot. Missing DB data is an honest empty state. Distinguish **generated from EF model fingerprint X** from **observed in database Y at migration Z** on every screen; a difference is expected when migration has not yet been applied.

## Generation and team workflow

1. A developer changes EF schema/entity configuration or adds a migration. They run one documented ERD generation command locally. The Code schema payload is deterministic for a given model; optional time/revision provenance is kept separately so a freshness check does not churn. They commit generator changes and Code HTML/data in the same schema PR.
2. A GitHub Actions check triggers on `pull_request` **only for schema-relevant paths**: migration files/snapshot, persistence configurations, `ApplicationDbContext`, domain entities that affect EF mapping, and ERD generator/assets. It regenerates Code output without connecting to a database and compares the generated artifact with the commit. It does not auto-push to PRs. Unrelated pushes/PRs do not run this workflow. A small always-run status check may be needed only if branch protection requires a check name; that status check must not regenerate the ERD.
3. Once a migration is applied to an approved development/shared PostgreSQL database, an operator runs the existing DB snapshot generator against a **read-only schema-inspection account** where possible and commits the new Applied DB snapshot after review. This operation is separate from every push, consistent with `SPEC.md` §9.6. It must not execute migrations. Production credentials must never be used in CI for this page.
4. Teammates pull the merged schema commit and open the committed entry-point HTML. They need no post-pull Git hook: hooks are local and cannot guarantee team-wide refresh. For their own uncommitted schema edits they rerun the command manually (or optionally use a local watcher); no background job runs on every push.

## Alternatives considered

- **DB-only viewer:** the existing generator provides accurate applied tables/FKs and needs little new code, but a pulled migration is invisible until applied to that database. It fails the requested code-pull behaviour.
- **Code-only viewer:** fulfils pull-time visibility and avoids DB credentials, but cannot prove that migrations were applied, RLS/raw SQL objects exist, or a target DB has drifted. It fails the requested actual-state comparison.
- **Two labelled views (selected):** reuses the existing DB generator and adds a code-derived view; provenance prevents confusion. More implementation and CI work, but each view answers a different maintenance question.
- **Truly live browser query:** would require a secured service and DB connectivity. Out of scope for a developer-only static page; the Applied DB view is a timestamped snapshot, not an open browser connection to PostgreSQL.

## Failure handling and boundaries

Generation fails with a clear error if EF model loading, unsupported mapping, or diagram serialization fails; it must not write a partial replacement. Schema CI checks both model extraction and deterministic output. Existing DB generator errors must not silently reuse an old capture as new. If a domain has no tables, label it empty; if an FK crosses domains, list it rather than drawing a misleading within-domain edge. Keep a documented source boundary: EF model versus catalog snapshot. Neither view includes data rows, RLS policy details, triggers or raw SQL-only constructs unless separately added and labelled.

Do not modify product APIs, tenant UI, database schema, migrations, or data as part of this viewer. Do not add a post-pull hook or run a generator on every push. Keep the existing DB snapshot cadence and no-migration-write guarantee.

## Verification and acceptance

- On a fixture model, adding/removing a table, column or FK changes Code output; unchanged model produces byte-identical schema data. Rendering and search remain usable for large domains and offline if local assets are chosen.
- A schema PR without updated generated artifacts fails the scoped check; the same PR passes after generation. A PR touching only an unrelated application file does not start ERD generation.
- DB extraction against a disposable PostgreSQL fixture matches `pg_catalog` for table/column/FK/unique facts and labels its applied migration/time. Missing or older DB snapshot is visibly distinct from the Code view.
- No SQL migration or write is executed; no secret or row data appears in HTML, JSON, logs, or committed files. Existing `schema-snapshots` generated reports remain readable.

## Open implementation choices for the plan

Choose the exact EF design-time model loader, deterministic asset format, and local Mermaid packaging after testing against this repository's existing .NET/Node toolchain. These are engineering choices that must preserve the above source boundaries and conditional workflow. The product owner need not decide them.
