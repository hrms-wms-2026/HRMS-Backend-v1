# Attendance history redesign — backend design

**Date:** 2026-08-27
**Status:** Approved (chat brainstorm)
**Scope:** `HRMS-Backend-v1` only. Companion: `Hrms--Web-application---front-end---v1/docs/superpowers/specs/next/2026-08-27-attendance-history-redesign-frontend-design.md`

## Problem

The "My attendance history" table (Time Tracking, `/attendance/time-tracking`) is being trimmed down and given a per-day detail drawer on the frontend (see companion doc). The drawer needs one aggregated call that returns: the day's attendance summary, a clock/break event timeline, and the employee's daily activity (idle time, app usage, activity score) from the already-built TrayApp monitoring pipeline — which today has no consumer anywhere in the frontend.

The existing `GetMyAttendanceHistoryQuery` / `GetCoveredAttendanceHistoryQuery` (`TimeTrackingController`, handled in `AttendanceReadHandler`, see `AttendanceReadHandlers.cs`) already return paginated `AttendanceHistoryRow` lists (paging added in `2026-08-25-attendance-list-pagination-design.md`) but only cover the row-level summary fields — no timeline, no activity data.

## Goal

Add one new endpoint that returns everything the detail drawer needs for a single employee-day in one response, with permission rules that let an employee always see their own data, but require `monitoring:read` (on top of existing `attendance:read`/authority-resolver visibility) for anyone viewing someone else's activity data.

## Non-goals

- No changes to `ActivityDailySummary`, its aggregation job (`ActivityDailySummaryAggregator`/`ActivityDailySummaryJob`), or the TrayApp ingest pipeline — this only reads existing data.
- No changes to the correction/work-area-change workflow endpoints.
- No changes to the existing `history`/`covered-history` list endpoints beyond what `2026-08-25-attendance-list-pagination-design.md` already covers.

## Design

### New endpoint

`GET /api/v1/attendance/time-tracking/history/{employeeId:guid}/{date}/detail`

Added to `TimeTrackingController` (`C:\onevoNew\HRMS-Backend-v1\src\ONEVO.Api\Controllers\Tenant\Attendance\TimeTrackingController.cs`), alongside `History`/`CoveredHistory`. `date` bound as `DateOnly` (route constraint, same convention as elsewhere).

### Query and handler

- New query `GetAttendanceDayDetailQuery(Guid EmployeeId, DateOnly Date)` in `ONEVO.Application.Features.TimeAttendance.Queries`.
- Handled in `AttendanceReadHandler` (same class as the existing two queries — the day-detail case is a natural third method there, not a new handler class), so it can call the existing private helpers instead of duplicating join logic:
  - Summary + timeline: reuse the per-day slice of `BuildRowsAsync`'s join logic (attendance record + approved leave + breaks + legal-entity timezone/break policy), called for a single `(employeeId, date)` instead of a range. `BuildRowsAsync` already windows by `records.Min/Max(Date)` per call, so this is a call with a single-day record set, not new logic.
  - Timeline events: derive from the same break records already joined in `BuildRowsAsync` (break start/end) plus the attendance record's own clock-in/clock-out timestamps — no new repository call.
  - Daily activity: call into whatever service/query object `MonitoringActivityController`'s `daily-summary` action currently uses internally to look up `ActivityDailySummaryDto` for `(employeeId, date)` (reuse that service/query directly — do not re-implement the `ActivityDailySummary` → DTO mapping a second time).

### Permission rule

- `employeeId == currentUser.EmployeeId` → summary/timeline/activity all allowed unconditionally, no permission check.
- `employeeId != currentUser.EmployeeId`:
  - Summary/timeline require `attendance:read` **and** the same `IEmployeeAuthorityResolver` visibility check (`EmployeeAuthorityPurpose.TimeTrackingRead`) that `covered-history` already applies. Fails → `403` for the whole request (no summary without visibility, matching `covered-history`'s existing behavior).
  - Daily activity additionally requires `monitoring:read`. If attendance visibility passes but `monitoring:read` is absent, the request still succeeds (`200`) with `dailyActivity: null` — the drawer's activity section renders its own "not visible to you" state rather than the whole drawer erroring. This is a deliberate asymmetry: attendance visibility and monitoring visibility are different grants today (`attendance:read` vs `monitoring:read`), and a manager with one but not the other shouldn't be blocked from seeing attendance just because they lack monitoring access.

### Response DTO

`AttendanceDayDetailResponse` (new record, `ONEVO.Application.Features.TimeAttendance.DTOs.Responses`):

```csharp
public record AttendanceDayDetailResponse(
    AttendanceHistoryRow Summary,
    IReadOnlyList<TimelineEvent> TimelineEvents,
    ActivityDailySummaryDto? DailyActivity
);

public record TimelineEvent(
    string EventType,   // "ClockIn" | "ClockOut" | "BreakStart" | "BreakEnd"
    DateTimeOffset Timestamp,
    string Source       // e.g. "Web", "TrayApp" — same source values AttendanceHistoryRow already uses
);
```

Wrapped in the standard `Result<T>` envelope, not paginated (single day, bounded event count).

### Testing

- Handler unit tests for `GetAttendanceDayDetailQuery` (`AttendanceReadHandler`):
  - Self access: full response regardless of `monitoring:read`/`attendance:read`.
  - Team access with `attendance:read` + `monitoring:read`: full response.
  - Team access with `attendance:read` only: `dailyActivity` is `null`, summary/timeline still populated.
  - Team access with neither: `403`.
  - No `ActivityDailySummary` row for that employee+date: `dailyActivity` is `null` (not an error), summary/timeline still populated from attendance data.
  - Timeline ordering: events sorted chronologically, break start/end pairs correctly attributed.
- Integration test for the new route covering the same permission matrix end-to-end (mirroring the integration-test style already used for `history`/`covered-history`).

### Risks / notes

- Reusing `BuildRowsAsync` for a single day must not regress its existing range behavior for `History`/`CoveredHistory` — the day-detail path should call it with a one-day range, not fork a parallel implementation.
- The activity-lookup reuse depends on whatever internal service backs `MonitoringActivityController`'s `daily-summary` action being callable outside the controller (i.e. it should already be a handler/service, not logic embedded directly in the controller action) — confirm this during planning; if the logic is controller-embedded, extracting it to a shared service is in-scope as a small prerequisite refactor, not a redesign of the monitoring feature.
