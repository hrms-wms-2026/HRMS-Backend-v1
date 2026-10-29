# 10. AI-Agent Fixable Task Backlog

## Table of Contents

1. [How to Use This Backlog](#1-how-to-use-this-backlog)
2. [Before UAT (P1)](#2-before-uat-p1)
3. [Before or During UAT (P2)](#3-before-or-during-uat-p2)
4. [Clean-Up (P3)](#4-clean-up-p3)
5. [Tasks That Need a Human](#5-tasks-that-need-a-human)

## 1. How to Use This Backlog

Each task can be handed to a coding agent as written. Each one names the repository, the files, the done condition and the check to run. Follow the repository's `CLAUDE.md`: no suppressions, no `--no-verify`, no migrations applied to shared databases.

## 2. Before UAT (P1)

| No. | Task | Repo | Files | Done when |
|---|---|---|---|---|
| A1 (UAT-001) | Open a PR to merge `feature/planner-redesign-backend` into `development`, unless the planner is declared out of scope. Run the Release build, unit tests and architecture tests. | BE | branch merge; migration `20261028233312_AddTaskCategoryKind` | PR green. `GET /planner/me/range` returns 200 when the planner is on. |
| A2 (UAT-002) | Add frontend CI: `.github/workflows/ci.yml` with `npm ci`, `npm run lint`, `npm run check:warnings`, `npx ng test --watch=false` on push and PR to `Development` and `main`. Pin actions by SHA, set `permissions: contents: read`. | FE | `.github/workflows/ci.yml` | The workflow runs on a PR. It fails on the current lint errors until A4 lands. |
| A3 (UAT-003) | Draft `docs/deployment/uat-environment.md`, listing every required variable with its purpose and no values. Add `appsettings.Staging.json` with non-secret defaults: `Swagger:Enabled=false`, a `Tenancy:RootDomain` placeholder that fails startup validation when empty outside Development, and `onexso.com` origins. | BE | `docs/deployment/`, `src/ONEVO.Api/appsettings.Staging.json`, `ConfigurationStartupValidator` | The API refuses to start as Staging without a root domain. Unit test added. |
| A4 (UAT-013) | Fix the 5 ESLint errors. | FE | `calendar-page.component.html:108-109`, `meeting-form-modal.component.ts:18` | `npm run lint`: 0 errors, 0 warnings. Specs still pass. |

## 3. Before or During UAT (P2)

| No. | Task | Repo | Files | Done when |
|---|---|---|---|---|
| B1 (UAT-006) | Build `GET /api/v1/org/legal-entities/{legalEntityId}/positions/coverage/by-target` with query `coveredTargetType`, `coveredPositionId`, `coveredDepartmentId`, `excludingRecordId`, returning `ManagementCoverageRecordResponse[]`. Same permission as `{positionId}/coverage`. | BE | `PositionsController.cs`, new query and handler, unit tests | The FE coverage modal shows existing owners. Handler unit tests pass. |
| B2 (UAT-007) | Add authorization-matrix integration tests for CoreHr, Leave, Attendance and Inbox handler-authorized endpoints: other-tenant id → 404, outside visibility → 403, missing permission → 403. | BE | `tests/ONEVO.Tests.Integration/Authorization/*` | Tests pass. Removing a handler's check makes one fail. |
| B3 (UAT-008) | Add PostgreSQL to `/health/ready`. | BE | health-check registration | Readiness returns 503 when the database is down. |
| B4 (UAT-010) | Wrap each recurring job's run in `IJobLeaseService` (the pattern in `MeetingOccurrenceGeneratorJob`). | BE | the 14 jobs listed in report 2, section 3 | Unit tests show the second instance skips the run. |
| B5 (UAT-011) | Validate checklist template task descriptions with `RichTextRules.HasActiveContent` in the create and update validators. | BE | checklist template validators | Unit test with `<img src=x onerror=…>` is rejected. |
| B6 (UAT-012) | Harden `ci.yml`: SHA pins, `permissions:`, `persist-credentials: false`. | BE | `.github/workflows/ci.yml` | actionlint and zizmor are clean. CI is green. |
| B7 | Add an API contract check: export the backend route list and compare it with frontend call sites in CI. Start from the method in report 7. | BE+FE | new script | CI fails on a frontend call with no backend route. |

## 4. Clean-Up (P3)

| No. | Task | Files |
|---|---|---|
| C1 (UAT-014) | Make the landing-header spec deterministic (stub or flush `requestAnimationFrame`). | `src/app/modules/public/ui/landing-header/landing-header.component.spec.ts` |
| C2 (UAT-015) | Show dashboard times in tenant time and add a fixed-time-zone spec. | dashboard hero and activity widgets |
| C3 (UAT-017) | Remove `setTrayAppAccess()` (or build the endpoint, if the product wants it). | `people-api.service.ts:105` |
| C4 (UAT-018) | Fix the 6 nullable warnings. | `MeetingEditService.cs`, `OnboardingDraftWriteService.cs`, `TaskCreateApplier.cs` |
| C5 (UAT-019) | Require `https` in the CORS subdomain match outside Development, with a unit test. | `CorsExtensions.cs` |

## 5. Tasks That Need a Human

- Decide whether the planner is in UAT scope (UAT-001).
- Provide UAT secrets and hosting, and choose single or multiple API instances (UAT-003, UAT-010).
- Run the integration suite and the upgrade rehearsal with Docker and a database backup (UAT-004, UAT-020).
- Accept or mitigate the malware-scanning risk (UAT-009).
- Run the Tray App end to end on a real device (UAT-021).
- Turn on branch protection that requires the new frontend CI (UAT-002).
