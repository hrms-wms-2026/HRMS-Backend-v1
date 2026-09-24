# "Milestone" → "Module" Display-Text Rename (Backend) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Change the seeded `module_catalog.name` display string for `module_key = 'objectives_milestones'` from `"Objectives & Milestones"` to `"Objectives & Modules"`, via a new EF Core migration, with zero code-identifier changes.

**Architecture:** Single data-only migration (`UpdateData`, no schema/model change) mirroring the exact pattern already used in `src/ONEVO.Infrastructure/Migrations/20260804024502_UpdateStarterPlanToCanonicalPhase1Modules.cs` for the same table.

**Tech Stack:** .NET 10, EF Core (Npgsql/PostgreSQL), xUnit.

## Global Constraints

- Scope is Work Management only. Do NOT rename `IMilestoneMembershipCoordinator`/`MilestoneMembershipCoordinator`, `GetMyProjectMilestonesQuery`/`Handler`, `MyProjectMilestoneResponse`/`ViewModel`, `TargetMilestonesPerProject`, or any XML doc-comment in `ObjectivesController.cs`. Do NOT rename the `objectives_milestones` module key itself — only its `name` (display) column value changes. See `docs/superpowers/specs/next/2026-08-12-milestone-to-module-display-rename-design.md` for full rationale.
- This is a single-row `UPDATE`, not a schema change — do not add `.Designer.cs` model-snapshot edits beyond what `dotnet ef migrations add` generates automatically.
- Do not touch `ModuleCatalogSeeder.cs` — it seeds a different, legacy key set (`work_management`, etc.) and does not include `objectives_milestones`; it is unrelated to this change and out of scope.
- One commit for this task.

---

### Task 1: Migration — rename the `objectives_milestones` module's display name

**Files:**
- Create: `src/ONEVO.Infrastructure/Migrations/<timestamp>_RenameObjectivesMilestonesModuleDisplayName.cs` (generated, then hand-edited)
- Create: `src/ONEVO.Infrastructure/Migrations/<timestamp>_RenameObjectivesMilestonesModuleDisplayName.Designer.cs` (generated, no manual edits)

**Interfaces:**
- Produces: nothing consumed by other code — this is a terminal, data-only change. No later task depends on it (this plan has only one task).

- [ ] **Step 1: Generate the empty migration scaffold**

Run: `dotnet ef migrations add RenameObjectivesMilestonesModuleDisplayName --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`

Expected: creates `src/ONEVO.Infrastructure/Migrations/<timestamp>_RenameObjectivesMilestonesModuleDisplayName.cs` and its `.Designer.cs`. Because this is a pure data change with no entity/schema difference, the generated `Up()`/`Down()` method bodies will be empty (or contain only a comment) — this matches how `20260804024502_UpdateStarterPlanToCanonicalPhase1Modules.cs` was originally created per its own header comment ("seeded imperatively... not via HasData"). `ApplicationDbContextModelSnapshot.cs` should show no diff (or a no-op diff) — do not hand-edit it.

- [ ] **Step 2: Fill in `Up()` and `Down()`**

Open the generated `<timestamp>_RenameObjectivesMilestonesModuleDisplayName.cs` and replace its (empty) `Up`/`Down` method bodies with:

```csharp
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "module_catalog",
                keyColumn: "module_key",
                keyValue: "objectives_milestones",
                column: "name",
                value: "Objectives & Modules");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "module_catalog",
                keyColumn: "module_key",
                keyValue: "objectives_milestones",
                column: "name",
                value: "Objectives & Milestones");
        }
```

This mirrors the working, no-`columnTypes`-needed `UpdateData` call already present in the same-purpose migration `20260804024502_UpdateStarterPlanToCanonicalPhase1Modules.cs:39-44` (its `subscription_plans.included_modules_json` update) — `module_key` and `name` are both simple `character varying` columns already mapped via `ModuleCatalogItemConfiguration.cs`, so no explicit `columnTypes` array is needed here (unlike that same file's `InsertData` call, which targets a table with no other `HasData`-tracked shape at all).

- [ ] **Step 3: Apply the migration**

Run: `dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`
Expected: applies cleanly, no errors.

- [ ] **Step 4: Verify the data change**

Run: `psql -h localhost -p 5432 -U postgres -d OnevoDb -c "SELECT module_key, name FROM module_catalog WHERE module_key = 'objectives_milestones';"`
(Password is in the repo's local `.env` under `ONEVO_DB_ADMIN_PASSWORD` — do not paste it into any command output or committed file; supply it at the interactive password prompt or via the `PGPASSWORD` environment variable for this one command only.)

Expected: 1 row, `name = "Objectives & Modules"`.

- [ ] **Step 5: Verify build**

Run: `dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj`
Expected: 0 errors.

- [ ] **Step 6: Run the regression suites**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --no-restore`
Expected: all tests pass (1933/1933 as of 2026-08-12 — see the backend test-run report from this session; this migration doesn't touch any code path they exercise, so the count should be unchanged).

Run: `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --no-restore`
Expected: all tests pass (555/555 as of 2026-08-12).

No test currently asserts on the `"Objectives & Milestones"` / `"Objectives & Modules"` string (confirmed by grep across `tests/` during design) — this step is a pure regression check, not a test of the new value itself (Step 4's `psql` query is that proof).

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Infrastructure/Migrations
git commit -m "feat(work-management): rename Objectives & Milestones module display name to Objectives & Modules"
```
