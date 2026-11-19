# Employee Detail Phase 2 — Timezone & Shared Project Memberships (Backend)

**Date:** 2026-09-30
**Status:** implemented on `feature/employee-detail-phase2`. **Merge gate:** the repository integration test (Task 3) is still open.
**Companion:** `Hrms--Web-application---front-end---v1/docs/superpowers/plans/next/2026-09-30-employee-detail-tabs.md` (Phase 2)

## Goal

Give the redesigned Employment tab its Work Arrangement time zone and its "Project Memberships" card. The product owner
chose project memberships over a new Teams module on 2026-09-30.

## Decisions

- The change is purely additive, so no API version bump. `EmployeeDetailJobInformation.Timezone` and
  `EmployeeDetailResponse.ProjectMemberships` are optional trailing record parameters.
- Timezone uses `Employee.DisplayTimezone` and falls back to `LegalEntity.Timezone`, the same order as
  `CalendarTimezoneResolver`. If neither is set it returns `null`, not a fabricated `"UTC"`.
- Project memberships are **relationship-scoped**. `ListProjectsQueryHandler` refuses to list another employee's
  projects, so the detail screen only reveals projects where the viewer is also an active member (or the viewer is
  the employee). A viewer with no employee row gets an empty list.
- The viewer is resolved with `IEmployeeRepository.GetDefaultForUserAsync`, which is deterministic for users with
  several employee rows. Work Management's `CallerIdentityResolver` uses an unordered `GetByUserIdAsync`. That gap is
  tracked separately.

## Tasks

1. [x] `IProjectMemberRepository.ListSharedProjectMembershipsAsync` + `EmployeeProjectMembershipSummary`. This collapses
   the per-objective rows per project: `MemberSince` is the earliest `JoinedAt`, `IsActive` is true if any row is active,
   and only active projects are included.
2. [x] `GetEmployeeDetailQueryHandler`: add timezone resolution and shared memberships. Unit tests cover the employee
   timezone, the legal-entity fallback, a null timezone, shared memberships, and a viewer with no employee row.
3. [ ] Integration test for `ListSharedProjectMembershipsAsync` against PostgreSQL under a NOBYPASSRLS role, following
   `TaskDraftRepositoryIntegrationTests`. Cover: only projects shared with the viewer are returned, the viewer's own
   projects, inactive projects are excluded, and the other tenant is invisible. **Blocked: Docker was not running.**

## Verification

- Unit: 4759/4759. Architecture: 695/695. The integration project builds.
