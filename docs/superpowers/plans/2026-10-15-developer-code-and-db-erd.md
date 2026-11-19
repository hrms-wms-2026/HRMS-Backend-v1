# Developer Code and Database ERD Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give ONEXSO developers one searchable, domain-scoped HTML ERD with separately labelled EF code model and applied PostgreSQL snapshot, updating the committed code view only when schema work changes.

**Architecture:** A no-connection .NET extractor reads `ApplicationDbContext.Model.GetRelationalModel()` and emits deterministic JSON. The existing `schema-snapshots` DB generator continues to capture applied schema on demand; a shared static viewer builder reads both JSON shapes and writes the committed developer page. A schema-path-filtered PR workflow checks code-view freshness without database access or per-push runs.

**Tech Stack:** .NET 10 / EF Core 10 / Npgsql already in backend, Node.js built-ins, existing schema-snapshots HTML/CSS/Mermaid viewer, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-10-15-developer-code-and-db-erd-design.md`

## Global Constraints

- Developer-only static artifact; no tenant/admin route, product API or DB migration.
- Code extraction never opens a DB connection, reads `.env`, or calls `Migrate`.
- DB capture remains after an approved environment migration, not per PR/push; never write to the target DB.
- CI regeneration/check runs only for schema-relevant pull requests; unrelated pushes do not run ERD work.
- No secrets, row data, private employee records or real DB connection strings in generated files.
- Preserve `schema-snapshots/SPEC.md`'s domain grouping and versioned DB snapshot layout; update its workflow section only to distinguish the new code view.
- Generated schema payload is byte-stable for the same EF model. Timestamp/revision metadata cannot make freshness checks nondeterministic.
- Keep cross-domain FKs in reference lists; do not draw them as same-domain edges.

## Review Focus

1. A raw-SQL migration creates an object absent from EF: Code view explicitly says “EF model only”; DB view can show the physical object after capture (Task 1/3 tests).
2. A composite FK or nullable FK crosses domains: serialize all column pairs and delete action once, and show it in references rather than a false same-domain edge (Task 1/3 tests).
3. The DB capture is missing or older than Code: viewer labels missing/older state without silently showing Code under DB (Task 3 test).
4. A schema model is unchanged but a new commit/time exists: deterministic payload and CI comparison still pass (Task 1/4 tests).
5. An unrelated PR changes no schema path: ERD workflow does not run generator; a schema PR with stale output fails (Task 4 test/review).

## File map

| File | Responsibility |
|---|---|
| `tools/ONEVO.SchemaErd/ONEVO.SchemaErd.csproj`, `Program.cs`, `EfModelExtractor.cs` | Construct nonconnecting EF context, serialize relational tables/columns/FKs/indexes as deterministic JSON. |
| `tests/ONEVO.Tests.Unit/SchemaErd/EfModelExtractorTests.cs` | Pin source extraction, nullability, composite FK, sorting and no-DB contract. |
| `schema-snapshots/model-schema.mjs` | Normalize EF JSON and DB JSON to one viewer contract, map existing domains, compute fingerprint. |
| `schema-snapshots/render-viewer.mjs` | Render the stable `schema-snapshots/index.html` from current Code JSON plus latest committed DB capture; `--check` compares without writes. |
| `schema-snapshots/generate.mjs` | Preserve DB capture, additionally save normalized `db-model.json`; delegate HTML rendering without changing SQL semantics. |
| `schema-snapshots/assets/mermaid.min.js` and license notice | Pinned local renderer for offline viewing; no network load. |
| `schema-snapshots/code-model.json`, `schema-snapshots/index.html` | Committed generated code model and browser entry point. |
| `schema-snapshots/README.md`, `schema-snapshots/SPEC.md` | Developer commands and explicit code-vs-applied-DB cadence/provenance. |
| `.github/workflows/schema-erd.yml` | Schema-path-filtered PR freshness check; no DB secret or push event. |

## Task 1: Read the EF relational model without connecting to PostgreSQL

**Files:** Create `tools/ONEVO.SchemaErd/{ONEVO.SchemaErd.csproj,Program.cs,EfModelExtractor.cs}`; create `tests/ONEVO.Tests.Unit/SchemaErd/EfModelExtractorTests.cs`; modify `tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj` to reference the tool project.

**Interfaces:** Produce `EfModelExtractor.Extract(IRelationalModel model, string? latestCodeMigration): SchemaDocument`, with records `SchemaDocument`, `SchemaTable`, `SchemaColumn`, `SchemaForeignKey`, `SchemaIndex` declared in `EfModelExtractor.cs`. `latestCodeMigration` comes from `IMigrationsAssembly.Migrations.Keys.Max(StringComparer.Ordinal)`, or `null` when there are no migrations; reading the assembly must not contact PostgreSQL. CLI: `dotnet run --project tools/ONEVO.SchemaErd -- --output schema-snapshots/code-model.json`. All collection order is ordinal by table, column ordinal/name, FK name, index name. No timestamp or commit SHA in JSON.

- [ ] **Step 1: Write failing tests.** Build a tiny Npgsql-backed EF test context with `Parent` and `Child`, a nullable/composite FK, unique index and snake-case table names, but never call `Database.OpenConnection`. Assert exact sorted table/column/FK/index projections. Add a test that compares two extractions byte-for-byte and checks no connection call is made. Add a test for a raw SQL-only table name being absent and the output source label `ef-relational-model`. Assert `latest_code_migration` comes from the highest migration ID without a DB query.
- [ ] **Step 2: Run the focused tests and confirm red.** `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter FullyQualifiedName~EfModelExtractorTests --no-restore`; expected compile failure because the new extractor/interface is missing.
- [ ] **Step 3: Implement the extractor and CLI.** Use `model.Tables`, each `ITable.Columns`, `ITable.ForeignKeyConstraints`, and `ITable.Indexes`/unique constraints; include all composite column pairs and `OnDeleteAction` as strings. Build `ApplicationDbContext` with the same interceptors/tenant context pattern as `ApplicationDbContextFactory`, but `UseNpgsql("Host=127.0.0.1;Database=erd_metadata;Username=erd_metadata").UseSnakeCaseNamingConvention()` and no configuration loading. Read only `context.Model.GetRelationalModel()`. Serialize via `System.Text.Json` with stable ordering; write to a temporary sibling and replace output only after serialization succeeds. Reject CLI arguments other than `--output <path>` and `--check`.
- [ ] **Step 4: Run focused tests and CLI twice.** Tests must pass. Hash the two CLI outputs and confirm identical bytes. Confirm no PostgreSQL connection is attempted (run with no PostgreSQL service/credentials); inspect JSON for calendar/task tables and no credentials.
- [ ] **Step 5: Commit only this task's files.** Message: `feat(schema): extract deterministic EF relational ERD model`.

## Task 2: Normalize the existing applied-DB capture

**Files:** Create `schema-snapshots/model-schema.mjs` and `schema-snapshots/model-schema.test.mjs`; modify `schema-snapshots/generate.mjs` to save `db-model.json` beside the existing raw/Markdown/viewer outputs. Do not change `schema-dump.sql` or its read-only `psql` call.

**Interfaces:** Export `normalizeCodeModel(raw)`, `normalizeDbModel(raw)`, `bucketFor(tableName)`, `fingerprint(model)` from `model-schema.mjs`. Both normalization functions return `{source, meta, domains:[{name,tables,relationships,externalReferences}]}`. The DB source includes `database`, `last_migration`, `generated_at`; the Code source includes `fingerprint`, `latest_code_migration`, and no DB label.

- [ ] **Step 1: Write failing `node:test` fixtures.** Use a three-table fixture spanning Calendar and Work Management with composite FK, nullable column, unique key and raw-SQL-only DB table. Assert normalized names/types/FK delete rule and deterministic fingerprint despite input order or generated timestamp. Assert the raw-SQL-only table appears only in DB normalization.
- [ ] **Step 2: Run red.** `node --test schema-snapshots/model-schema.test.mjs`; expected missing-module/export failure.
- [ ] **Step 3: Implement normalizers.** Move the existing `DOMAINS`/`bucketFor` rule without changing its ordering, map current generator's `meta/columns/constraints` shape and Task 1's code JSON to one view model. Preserve composite FK as one relationship with all column pairs. Add `db-model.json` write after successful read/normalization; do not write if `psql` fails. Keep existing generated Markdown and `viewer.html` until Task 3 replaces/redirects the entry point.
- [ ] **Step 4: Run green and inspect fixture output.** `node --test schema-snapshots/model-schema.test.mjs`; verify no row values or connection settings appear.
- [ ] **Step 5: Commit.** Message: `feat(schema): normalize code and applied database ERD sources`.

## Task 3: Build one local, searchable HTML viewer

**Files:** Create `schema-snapshots/render-viewer.mjs`, `schema-snapshots/render-viewer.test.mjs`, `schema-snapshots/assets/mermaid.min.js` with license notice; modify `schema-snapshots/generate.mjs`, `README.md`, `SPEC.md`; generate `schema-snapshots/index.html`. Retain historical versioned DB snapshot folders.

**Interfaces:** `renderViewer({codeModel,dbModel|null}): string` and CLI `node schema-snapshots/render-viewer.mjs [--check]`. `--check` exits nonzero if `index.html` differs; it never writes. Code-model generation command is `dotnet run --project tools/ONEVO.SchemaErd -- --output schema-snapshots/code-model.json`, then `node schema-snapshots/render-viewer.mjs`.

- [ ] **Step 1: Write failing HTML tests.** Fixture expects Code/Applied DB tabs, source fingerprint, DB migration/time, missing/older DB badge, domain filter, table/column search, `details` cards, PK/FK/UK labels and cross-domain delete rule. Compare DB `last_migration` to Code `latest_code_migration` for the older badge; if either value is absent, show “version unknown” instead of asserting staleness. Assert fixture labels containing `<script>` are escaped and no CDN URL appears. Assert `--check` fails on stale HTML but does not rewrite it.
- [ ] **Step 2: Run red.** `node --test schema-snapshots/render-viewer.test.mjs`; expected missing renderer failure.
- [ ] **Step 3: Implement viewer.** Reuse current `viewer-mockup.html`/`generate.mjs` palette and layout; render only selected domain's Mermaid diagram and immediate cross-domain reference tables. Add tabs, honest provenance/empty state, accessible search and keyboard focus. Pin a local Mermaid browser bundle and license (review size/license); load `assets/mermaid.min.js` by relative path. Serialize data into the HTML safely; escape labels/JSON. Integrate DB generator so its versioned viewer remains available and `index.html` incorporates latest committed DB capture. Do not call `psql` from the Code rendering path.
- [ ] **Step 4: Verify.** Run Node tests, build code-model JSON, render HTML, and run `--check`. Open `schema-snapshots/index.html` in a browser with network disabled; inspect Calendar and Work Management domains, search, FK focus, 1280px and mobile widths. Confirm a missing DB snapshot is labelled absent rather than live.
- [ ] **Step 5: Commit.** Message: `feat(schema): add offline code and database ERD viewer`.

## Task 4: Run freshness checks only for schema PRs

**Files:** Create `.github/workflows/schema-erd.yml`; create `schema-snapshots/check-scope.mjs` and `check-scope.test.mjs` only if path-filter verification needs an executable fixture; update `schema-snapshots/README.md` with exact developer steps.

**Interfaces:** PR-only workflow path filters include `src/ONEVO.Infrastructure/Migrations/**`, `src/ONEVO.Infrastructure/Persistence/Configurations/**`, `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`, mapped domain entity files, `tools/ONEVO.SchemaErd/**`, `schema-snapshots/**`, and the workflow itself. Job runs extractor `--check` and `render-viewer.mjs --check`; it has no DB secret, no `psql`, no `push` trigger. Consider GitHub path-filter branch-protection pending-status behaviour before making the check required.

- [ ] **Step 1: Write failing scope tests.** Given changed paths in migration/configuration/entity/ERD tool, assert ERD required; given only `src/ONEVO.Api/Controllers/...` or docs unrelated to schema, assert skipped. Test that stale `code-model.json`/`index.html` returns nonzero and does not alter files.
- [ ] **Step 2: Run red.** `node --test schema-snapshots/check-scope.test.mjs` plus fresh `--check`; expect failed stale-artifact/scope assertions.
- [ ] **Step 3: Add workflow and docs.** Use `pull_request.paths` to avoid ERD work on unrelated PRs; only checkout, setup .NET/Node, restore/build extractor, run two checks. Document that DB snapshot generation is manual after approved migration, while the code artifact is committed with schema PR. If a required check must report success on unrelated PRs, add a lightweight always-running sentinel that skips the generator rather than removing path filters from the heavy job.
- [ ] **Step 4: Verify locally and review YAML.** Run `node --test schema-snapshots/*.test.mjs`, focused .NET extractor tests, both freshness checks, `git diff --check`, and `git status`. Inspect workflow events and path list to prove no push trigger and no DB access. Use a test PR only if the user authorizes publishing one; do not create a PR automatically from this plan.
- [ ] **Step 5: Commit.** Message: `ci(schema): check ERD freshness only on schema pull requests`.

## Final verification before integration

Run the full focused test commands fresh, inspect `index.html` in the browser, verify generated files contain no secrets/row data, and compare final diff against the spec. Check the current Git worktree before/after so unrelated local changes remain untouched. Report the exact passing counts and any blocked real-DB validation. Do not regenerate DB snapshots against a shared database or run migrations as part of this implementation.
