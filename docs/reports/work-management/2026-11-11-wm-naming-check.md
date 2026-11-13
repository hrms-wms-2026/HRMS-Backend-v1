# WM naming check — 2026-11-11

Scope: Work Management module only, both repos. Meetings and Planner folders were scanned but are **not** in the fix list (teammate-owned, see bottom).

- Backend: 796 `.cs` files across Api / Application / Domain / Infrastructure WM folders
- Frontend: `src/app/modules/work` (excluding `meetings/`, `planner/`)

## Already clean (no action)

| Check | Result |
|---|---|
| BE namespace matches folder path | 0 mismatches |
| BE interfaces start with `I`, types PascalCase | 0 issues |
| BE controller actions | No `Async` suffix — matches whole repo (0 of 751 controller actions use it) |
| FE file + folder names kebab-case | 0 issues |
| FE class name matches file (`foo-bar.component.ts` → `FooBarComponent`) | 0 issues |
| FE selectors `app-<file-stem>` | 0 issues |
| FE Observables end with `$` | 0 issues |

## Findings to fix

### P1 — real convention breaks (small, safe)

| # | Repo | Where | Problem | Fix |
|---|---|---|---|---|
| 1 | BE | `Sprints/Services/SprintWriteService.cs:279` `Restore(...)` | Only non-controller `Task` method in WM without `Async` suffix | Rename to `RestoreAsync`; 1 caller in `SprintDeleteReverter.cs:42` |
| 2 | FE | `utils/sprint-date.utils.ts` (+ spec) | Repo uses `.util.ts` (72 files) vs `.utils.ts` (10) | Rename to `sprint-date.util.ts` |
| 3 | FE | `utils/tree-highlight.utils.ts` (+ spec) | Same as above | Rename to `tree-highlight.util.ts` (5 importers total for 2+3) |
| 4 | FE | `utils/module-task-selection.ts`, `task-kind.ts`, `task-status-change-set.ts` | No `.util` kind suffix, unlike every other file in `utils/` | Rename to `*.util.ts` (+ specs) |

### P2 — file holds many types but is named after none of them (BE, 25 files outside Meetings/Planner)

This is a style choice, not a bug. Two patterns exist:

- **Grouped DTO/contract files** (e.g. `TaskContracts.cs`, `SprintContracts.cs`, `TaskCommentResponses.cs`, `MonitorDtos.cs`, `SprintActionPayloads.cs`) — a plural group name for related records. Readable; recommend **keep**.
- **Command + validator + handler in one file** — `Tasks/Commands/BulkTaskActions/BulkTaskActionCommands.cs`, `Tasks/Commands/TaskDrafts/TaskDraftCommands.cs`, `Tasks/Queries/TaskDrafts/TaskDraftQueries.cs`. The rest of WM uses one file per command/handler, so these three stand out. Recommend **split** into `XCommand.cs` / `XCommandValidator.cs` / `XCommandHandler.cs`.
- `Monitoring/Services/ConversionRates.cs` holds `EmployeeConversionRate`, `PooledRate`, `RateTrendPoint`, `ConversionRateSet` — rename file to `ConversionRateSet.cs` or move the records to `Monitoring/DTOs/`.

### P3 — optional

- BE DTO suffix: top-level payloads use `Response` (71), nested pieces use `Dto` (15) e.g. `TaskCommentReactionDto` inside `TaskCommentResponse`. Consistent enough; no change.
- FE `feature/project-settings/` holds 3 child tab components (`project-general-settings`, `task-category-settings-tab`, `task-status-settings-tab`) beside the parent. Elsewhere each component has its own folder. Could move each into its own subfolder; low value.

## Teammate-owned (report only, not touching)

- `Meetings/` (BE + FE): grouped `*Commands.cs` / `*Queries.cs` files, 2 private fields named `Tenant`, 1 named `Settings` (should be `_tenant` / `_settings`).
- `Planner/Commands/PlannerCommands.cs` holds Query types; handlers named `PlannerRequestHandler` / `TeamPlannerRequestHandler`.

## Before fixing

FE branch `feature/overtime-forecast-widgets` has uncommitted dashboard forecast changes. Renames should go on their own branch (e.g. `chore/wm-naming`) after that work is committed, so the diffs don't mix.
