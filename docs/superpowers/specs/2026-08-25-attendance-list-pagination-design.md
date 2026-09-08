# Attendance list pagination — design

**Date:** 2026-08-25
**Scope:** `Hrms-Backend-v1` (API/Application/Infrastructure) + `Hrms--Web-application---front-end---v1` (Angular)

## Problem

The Time Tracking page (`/attendance/time-tracking`) has two tables that currently load and render every row for the selected filter, unbounded:

1. **My attendance history** — bounded loosely by the From/To date range, but a wide range still renders every day in one response.
2. **My correction requests** — no bound at all; every correction request the employee has ever submitted (optionally filtered by from/to/status) renders in one response.

Neither has pagination on the frontend or the backend.

## Goal

Add pagination to both tables, backend and frontend, following the pagination convention already established elsewhere in the codebase (Projects list, Employee list) rather than inventing a new one.

## Existing convention (reused, not reinvented)

**Backend:**
- `ONEVO.Application.Common.Models.PagedRequest` — `PageNumber` (default 1), `PageSize` (default 20, clamped 1–100), `SortBy`, `SortDirection`. Bound directly from query string via `[FromQuery] PagedRequest paging`.
- `ONEVO.Application.Common.Models.PagedResult<T>` — `Items`, `PageNumber`, `PageSize`, `TotalCount`, computed `TotalPages`/`HasNext`/`HasPrevious`.
- `ONEVO.Api.Contracts.Common.PagedResultViewModel<T>` — the wire DTO, same shape, camelCase over JSON.
- Repository methods that back a paged list return `(IReadOnlyList<T> Items, int TotalCount)` and accept `skip`/`take` (see `IProjectRepository.ListForMemberAsync`).

**Frontend:**
- `PagedResultDto<T>` (module-local interface, see `work/models/dto/project.dto.ts`) — `items`, `pageNumber`, `pageSize`, `totalCount`, `totalPages`.
- Store holds `<list>Page`, `<list>PageSize`, `<list>TotalCount` signals (see `PeopleState.employeesPage/employeesPageSize/employeesTotalCount`).
- UI: Previous/Next buttons + "Page X of Y" text, shown only when `totalCount > pageSize` (see `employee-list.component.html`).

This feature applies that exact pattern to two more lists. No new abstractions.

## Backend changes

### 1. Attendance history (`GET /api/v1/attendance/time-tracking/history`)

- `GetMyAttendanceHistoryQuery(DateOnly From, DateOnly To)` → add `PagedRequest Paging`.
- `GetCoveredAttendanceHistoryQuery(DateOnly From, DateOnly To, Guid? EmployeeId)` → add `PagedRequest Paging` (team view gets the same treatment for consistency, since it renders through the same table markup).
- Handler return type changes `Result<IReadOnlyList<AttendanceHistoryRow>>` → `Result<PagedResult<AttendanceHistoryRow>>` for both handlers in `AttendanceReadHandler`.
- `IAttendanceReadRepository.ListRecordsAsync` gains a paged overload: `ListRecordsPagedAsync(tenantId, employeeIds, from, to, skip, take, ct) → (IReadOnlyList<AttendanceRecord> Items, int TotalCount)`, implemented in `EfAttendanceReadRepository` by applying `.Skip(skip).Take(take)` after the existing `OrderByDescending(x => x.Date)`, with `TotalCount` from a separate `CountAsync` on the same filtered (unpaged) query. The existing unpaged `ListRecordsAsync` stays as-is — it's still used elsewhere for total-worked-time style aggregation, only the two history queries switch to the paged overload.
- `BuildRowsAsync` (breaks/leave enrichment) runs on the single page of records instead of the full range — cheaper, and unaffected because it already windows off `records.Min/Max(Date)` per call.
- Controller: `[FromQuery] PagedRequest paging` param added to `History` and `CoveredHistory` actions, forwarded into the query. Response wrapped as `PagedResultViewModel<AttendanceHistoryRow>` via a small mapper (matching `ProjectViewModelMapper.ToViewModel()`'s pattern).

### 2. My correction requests (`GET /api/v1/attendance/corrections/my`)

- `ListMyAttendanceCorrectionsQuery(DateOnly? From, DateOnly? To, string? Status)` → add `PagedRequest Paging`.
- `AttendanceCorrectionWorkflow.ListMyAsync` return type `Result<IReadOnlyList<AttendanceCorrectionResponse>>` → `Result<PagedResult<AttendanceCorrectionResponse>>`.
- `IAttendanceCorrectionRepository.ListMyAsync` gains `skip`/`take` params and returns `(IReadOnlyList<AttendanceCorrection> Items, int TotalCount)`, implemented in `EfAttendanceCorrectionRepository` the same way as above (existing `OrderByDescending(x => x.CreatedAt)` + `Skip/Take`, plus a `CountAsync`).
- Controller: `[FromQuery] PagedRequest paging` added to `My`, wrapped as `PagedResultViewModel<AttendanceCorrectionResponse>`.
- `ListAttendanceCorrectionApprovalsQuery` (the approvals inbox) is **not** touched — out of scope, different screen, not part of this request.

### Out of scope (explicitly)

- Covered/team history pagination is included (same table markup), but the **approvals inbox** and any other attendance list are not.
- No change to `PagedRequest`/`PagedResult` themselves — they already support everything needed.
- No sorting UI added; `SortBy`/`SortDirection` exist on `PagedRequest` but neither table currently offers sort controls, so they're left at their defaults (unset → repository's existing `OrderByDescending`).

## Frontend changes

All in `Hrms--Web-application---front-end---v1`, module `src/app/modules/attendance`.

### Models

- `time-tracking.model.ts`: add a local `PagedResultDto<T>` (same shape as the `work` module's), and change `TimeTrackingApiService.getMyHistory`/`getCoveredHistory` return types from `Observable<readonly AttendanceHistoryRow[]>` to `Observable<PagedResultDto<AttendanceHistoryRow>>`.
- `attendance-correction.model.ts`: same treatment for `getMyCorrections` → `Observable<PagedResultDto<AttendanceCorrectionListItem>>`.

### `TimeTrackingApiService`

- `getMyHistory`, `getCoveredHistory`, `getMyCorrections` accept `page: number, pageSize: number` and add `pageNumber`/`pageSize` query params alongside the existing `from`/`to`/`status` ones.

### `TimeTrackingStore`

New state: `historyPage`, `historyPageSize` (20), `historyTotalCount`; `coveredHistoryPage`, `coveredHistoryPageSize` (20), `coveredHistoryTotalCount`; `myCorrectionsPage`, `myCorrectionsPageSize` (20), `myCorrectionsTotalCount`.

- `loadMyHistory(range, page = 1)`, `loadCoveredHistory(range, employeeId?, page = 1)`, `loadMyCorrections(page = 1)` — each resets to page 1 when called with a new range/filter (i.e. from the existing "Apply" button and initial load), and patches the new page/total-count signals from the response envelope.

### `TimeTrackingComponent`

- `goToHistoryPage(page)`, `goToCoveredHistoryPage(page)`, `goToCorrectionsPage(page)` — call the store loaders with the current range/filter and the requested page.
- `applyHistoryRange()` and `selectViewMode()` continue to reset to page 1 (implicit via the loader defaults above).

### Templates (`time-tracking.component.html`)

For each of the three tables (my history, my corrections, team/covered history), add a Previous/Next block styled and gated the same way as `employee-list.component.html`:

```html
@if (store.historyTotalCount() > store.historyPageSize()) {
  <div class="tt-pagination">
    <span class="tt-pagination-info">
      Page {{ store.historyPage() }} of {{ Math.ceil(store.historyTotalCount() / store.historyPageSize()) }}
    </span>
    <div class="tt-pagination-buttons">
      <app-button variant="secondary" [disabled]="store.historyPage() <= 1" (pressed)="goToHistoryPage(store.historyPage() - 1)">Previous</app-button>
      <app-button variant="secondary" [disabled]="store.historyPage() * store.historyPageSize() >= store.historyTotalCount()" (pressed)="goToHistoryPage(store.historyPage() + 1)">Next</app-button>
    </div>
  </div>
}
```

`Math` needs to be exposed on the component the same way `employee-list.component.ts` does (`protected readonly Math = Math;` or equivalent), or the comparison can be pre-computed as a `computed()` signal instead — implementation detail for the plan step.

Page size is fixed at 20 for all three tables (not user-configurable), matching the backend default.

## Testing

- Backend: unit tests for the two repository paged methods (skip/take/count correctness) and handler tests asserting the `PagedResult` envelope; integration tests updated/extended for `GET history` and `GET corrections/my` to assert `pageNumber`/`pageSize`/`totalCount` in the response and correct page slicing across a multi-page fixture.
- Frontend: `time-tracking-api.service.spec.ts` updated for the new paged response shape; `time-tracking.store.spec.ts` for page-reset-on-range-change and page navigation; `time-tracking.component.spec.ts` for Previous/Next enabled/disabled states and the "Page X of Y" text, mirroring the existing `employee-list` pagination tests where useful as a reference.
- The 3 pre-existing failing tests from the in-progress MY/TEAM toggle work (unrelated, already broken before this feature) are not this feature's responsibility to fix, but should not be made worse.

## Risks / notes

- `AttendanceHistoryRow`'s enrichment (`BuildRowsAsync`) does per-call work (break windows, leave lookups) sized to whatever `records` it's given — paginating at the repository level before this runs is strictly cheaper than before, not a behavior change.
- Both `GetMyAttendanceHistoryQuery` and `GetCoveredAttendanceHistoryQuery` currently share `AttendanceReadHandler.BuildRowsAsync`; the paging change must keep both call sites correctly threading `skip`/`take` without duplicating logic.
- The frontend's `AttendanceCorrectionResponse` (submit/approve/reject results) is a different, non-paged use — only the **list** endpoint (`corrections/my`) and its consumer (`store.myCorrections`) change shape; `store.correctionPreview`, `submitCorrection`, etc. are untouched.
