# "Milestone" → "Module" Display-Text Rename (Backend) — Design

**Status:** Approved 2026-08-12 (scoped through direct back-and-forth with the user). No plan written yet.

## Problem

The user's manager doesn't like the product-facing term "Milestone" and asked for it to be changed to "Module". Scoped down to **display text only** — code identifiers, the `objectives_milestones` module permission key, and the `Objective` domain entity all stay exactly as they are; only a user/admin-visible display string changes.

**Why identifiers stay:** The backend's real domain entity for this concept is already `Objective` (`Objective.cs`, `objective_change_requests` table, `/api/v1/work/objectives/...` routes, `GetObjectiveTree`, etc.) — "Milestone" is a thin product-layer label on top, present in only a handful of places: `IMilestoneMembershipCoordinator`/`MilestoneMembershipCoordinator`, `GetMyProjectMilestonesQuery`/`Handler`, `MyProjectMilestoneResponse`/`ViewModel`, a seeder constant, and ~15 XML doc-comments in `ObjectivesController.cs`. The user confirmed these should NOT be renamed — only the one place where "Milestone" appears as an actual **display string** shown to a human.

## Investigation: where "Milestone" actually appears as display text

Grepped `src/ONEVO.Api/Controllers/Tenant/WorkManagement/*.cs` for route/URL segments — **none** contain "milestone"; all routes already say `objectives`. So no API contract/URL changes are needed at all.

The only literal display-name string is a single seeded database row:

```csharp
// src/ONEVO.Infrastructure/Migrations/20260804024502_UpdateStarterPlanToCanonicalPhase1Modules.cs:69
{ "objectives_milestones", "Objectives & Milestones", "worksync", "phase_1", "flat_rate", "[]", "[]", "[]", true, ... }
```

This is an `InsertData` row in the `module_catalog` table: `module_key = "objectives_milestones"`, `name = "Objectives & Milestones"`. Per the migration's own comment, `module_catalog` "has no `.Designer.cs` target-model diff... it is seeded imperatively by `ModuleCatalogSeeder` / earlier data-only migrations, not via `HasData`" — so a new migration is required to change already-seeded rows; editing this historical migration file is not an option (already applied to the DB).

Checked for duplicate/conflicting seed sources:
- `ModuleCatalogSeeder.cs` (`IHostedService`, runs on every startup, idempotent upsert-by-key) — seeds a **different, legacy** key set (`work_management`, `auth`, `configuration`, etc., from the pre-2026-08-04-taxonomy-rename vocabulary). It does not include `objectives_milestones` at all, so it will not overwrite or conflict with this migration's row.
- `SubscriptionPlanConfiguration.cs:35` and ~18 `*.Designer.cs` migration snapshots also contain the string `objectives_milestones` — but only as an entry inside a `subscription_plans.included_modules_json` array (a list of module *keys* a plan includes), never as the display name. The key stays unchanged, so none of these need editing.

Confirmed: **exactly one row, one column** needs to change.

## Scope

### In scope

One new EF Core migration:
- `UpdateData` on table `module_catalog`, `keyColumn: "module_key"`, `keyValue: "objectives_milestones"`, `column: "name"`, value `"Objectives & Milestones"` → `"Objectives & Modules"`.
- `Down()` reverts the same row back to `"Objectives & Milestones"`.
- No `.Designer.cs`/snapshot diff needed for this table (consistent with how `20260804024502` itself was written — imperative `UpdateData`/`InsertData`, not `HasData`).

### Explicitly out of scope

- `IMilestoneMembershipCoordinator`/`MilestoneMembershipCoordinator`, `GetMyProjectMilestonesQuery`/`Handler`, `MyProjectMilestoneResponse`/`ViewModel`, `TargetMilestonesPerProject` — all class/interface/type names stay as-is.
- The `objectives_milestones` module key itself (used for permission/subscription gating on both backend `[RequirePermission]`-style checks and the frontend's `nav-items.config.ts` `requiredModules` array) — renaming the key would require coordinated backend+frontend deploys and touches the permission system; the user explicitly chose to leave it alone.
- ~15 XML doc-comments in `ObjectivesController.cs` ("Edits a milestone...", "Marks a milestone Achieved...") — these surface into Swagger UI for API consumers/developers, not end users. User explicitly said don't touch backend beyond the one display-name fix.
- The `Objective` domain entity, `objective_change_requests` table, and all `/api/v1/work/objectives/...` routes — unaffected, already didn't say "milestone".
- Any module outside Work Management.

## Testing

This is a single-row data migration with no logic change. Verification: apply the migration, `SELECT name FROM module_catalog WHERE module_key = 'objectives_milestones'` should return `"Objectives & Modules"`. No existing test asserts on this string (confirmed by grep across `tests/`) — no test changes required. Run the full `dotnet test tests/ONEVO.Tests.Unit` + `tests/ONEVO.Tests.Architecture` suites afterward as a regression check (both were 100% green as of 2026-08-12, see the corresponding backend test-run report).

## Companion change

Frontend: `Hrms--Web-application---front-end---v1/docs/superpowers/specs/next/2026-08-12-milestone-to-module-display-rename-design.md` — 6 literal UI strings. Independent of this migration; can ship in either order.
