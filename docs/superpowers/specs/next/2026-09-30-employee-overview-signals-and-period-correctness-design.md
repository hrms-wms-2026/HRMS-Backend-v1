# Employee Overview — Ranked Signals, Correct Working Days, Bounded Months, Real Compare

**Status:** Approved 2026-09-30 (brainstorm sign-off with user, all four sections approved).
**Repos:** `HRMS-Backend-v1` (backend) + `Hrms--Web-application---front-end---v1` (frontend). This one spec covers both; each repo gets its own plan folder.
**Screen:** People → Employee Detail → Overview tab.

## Problem

Observed on dev tenant `dapi`, September 2026:

1. **Working days contradict between employees.** Saif Ahamed (hired 2024-02-01) shows `Days attended 0/0`; Thivan Balasubramaniam (hired 2023-01-10) shows `1/1`. Both were employed all month, same legal entity, same General-settings working week. Root cause: `AttendancePeriodCalculator` counts `WorkingDays` only from existing `attendance_records` rows (`record.ExpectedWorkingDay`), and a row is created only on clock-in. No clock-in → no row → 0 expected days. The legal entity's `StandardWorkingDays` is only consulted at clock-in time via `AttendanceScheduleResolver`.
2. **KPI strip is fixed.** Four hardcoded cards (Days attended, Late arrivals, Missing clock-outs, Tasks completed) regardless of what matters. Over-break is never shown even though `/overview/attendance-discipline` already returns `OverBreakDays`/`OverBreakMinutes` (the chart component fetches it and ignores it).
3. **Needs Attention is fixed.** Four hardcoded rows in `overview-tab.component.ts`, always rendered, including green "No …" rows; the card never hides.
4. **Compare does nothing visible.** The button toggles `EmployeePeriodStore.compare`, but the visible KPI/chart component hardcodes `compare=false`; only `sr-only` hidden widgets read the flag.
5. **Month stepper is unbounded.** Arrows step to months before hire, which show zeros.

## Decisions (user-confirmed)

- All 12 violations below, in this rank order.
- Signals feed **both** the KPI strip and Needs Attention from **one** ranked list.
- Compare = option (b): user picks the compare month in a second picker.
- Severity order fixed in code (not tenant-configurable yet); thresholds come from existing policies (break allowance, schedule, required minutes).
- Signals whose module is off, or that the viewer cannot see, are **omitted**, never shown as zero.

## Section 1 — Expected working days & absences (backend bug fix)

New pure static helper `ExpectedWorkdayCalendar.Build(workingWeekdays, holidayDates, from, to, hireDate, terminationDate, today)`; `EmployeeAttendancePeriodReader` loads its inputs (employee row, tenant holiday events via new `ICalendarEventRepository.ListHolidayDatesAsync`) and exposes the result as `AttendancePeriodData.Workdays`. Holiday events are tenant-wide (that is how `SyncHolidayCalendarCommandHandler` stores them).

- Effective range = `[max(from, employee.HireDate), min(to, employee.TerminationDate ?? to, todayInEntityTz)]`. Empty if inverted.
- If the employee has no legal entity, or the entity's schedule is not configured (same `IsScheduleConfigured` rule clock-in uses: timezone + start < end), **no** date is expected — matching clock-in, and preventing a flood of false absences.
- A date is expected iff its weekday ∈ `LegalEntity.StandardWorkingDays` (same parsing as `AttendanceScheduleResolver.ParseWorkingDays`) AND it is not a synced holiday calendar event for that legal entity.
- Returns the ordered set of expected dates plus the set of holiday dates in range (with names).

`AttendancePeriodCalculator` is changed to take the expected-day set plus the period's records and approved-leave dates, and classify **every date in the effective range**:

| Date kind | Record | Leave | Classification |
|---|---|---|---|
| expected | none | none | `absent` |
| expected | none | yes | `leave` |
| expected | clocked in, on time | – | `present` |
| expected | late | – | `late` (counts as attended) |
| expected | `missing_clock_out` | – | `missing_clock_out` (counts as attended) |
| expected | worked < `RequiredWorkMinutes` | – | also counted `shortHours` |
| not expected / holiday | record exists | – | `worked_non_working_day` (counted) |
| not expected / holiday | none | – | `off` |
| expected | record exists | yes | `worked_during_time_off` (counted) |

`AttendancePeriodCounts` gains `Absent`, `ShortHours`, `WorkedOnNonWorkingDay`, `WorkedDuringTimeOff`. `WorkingDays` = count of expected dates in effective range. `Present` means attended (on time + late + missing clock-out), matching the KPI "Days attended x / WorkingDays".

Consumers fixed together: `GET /employees/{id}/overview/attendance` (response gains `Absent`, `ShortHours`, `WorkedOnNonWorkingDay`, `WorkedDuringTimeOff`, `HireDate`, `TerminationDate`) and the monthly attendance summary that already delegates to the calculator. Days list now contains every date in the range (future/pre-hire dates = `none`).

## Section 2 — Month bounds

- Bounds = `hireMonth .. min(currentMonth, terminationMonth)`. The Overview reads `hireDate`/`terminationDate` from the employee record already loaded by the detail page and passes them to `EmployeePeriodStore.setBounds(min, max)`.
- Store clamps `month` into bounds on set; `canGoPrevious`/`canGoNext` computed signals disable the arrows at the edges. The default month is `max` (current month, or termination month for leavers).
- In-bounds months with no data (long leave etc.) remain selectable; they show `0 / N` with leave days counted.

## Section 3 — Compare (user-picked month)

- Store: replace `compare: 'none' | 'previous'` with `compareMonth: string | null` (+ derived `compareFrom`/`compareTo`). Compare button toggles it on (default = month before selected, clamped to bounds) / off. When on, a second month picker renders next to the stepper; options = in-bounds months excluding the selected month.
- **Approach:** each widget re-requests its own existing endpoint with the compare month's `from/to` (no new backend compare params). A shared pure helper `delta(current, previous, direction)` returns `{ diff, pct, trend: 'up'|'down'|'flat', good: boolean }` for a `<app-delta-badge>` (component already exists at `people/ui/delta-badge`).
- Applies to: KPI strip cards, trend chart (second dashed series), Work, Attendance, Approvals, Needs Attention rows (count delta), Time Off (leave days taken in the month — from the attendance endpoint's `leaveDays`; the balance rings are year-based and get no delta).

## Plans

- Backend: `docs/superpowers/plans/next/2026-09-30-employee-overview-signals-backend/part-1-expected-working-days.md`, `part-2-signals-endpoint.md`.
- Frontend (`Hrms--Web-application---front-end---v1`): `docs/superpowers/plans/next/2026-09-30-employee-overview-signals-frontend/part-1-month-bounds-and-compare-month.md`, `part-2-signals-kpi-strip-and-needs-attention.md`, `part-3-compare-everywhere.md`.
- Order: backend 1 → backend 2 → frontend 1 → 2 → 3 (frontend 1 can start in parallel with backend).
- The legacy `comparePrevious` backend flags stay for compatibility; the frontend stops sending `true`.

## Section 4 — Ranked violation signals

New endpoint `GET /api/v1/employees/{id}/overview/signals?from&to`, guarded by the existing `IEmployeeReadAccessGuard` / `EmployeeOverviewAccess`.

Response:

```
EmployeeOverviewSignalsResponse(DateOnly From, DateOnly To, IReadOnlyList<EmployeeSignal> Signals)
EmployeeSignal(string Key, int Rank, string Severity /* critical|warning|info */, string Category /* attendance|work|approvals|monitoring */, decimal Value, string Unit /* days|minutes|count|percent */, decimal? Denominator, string Label, string Detail)
```

Only **active** signals (value > 0 / threshold crossed) are returned, pre-sorted.

### Catalogue (rank = user order)

| Rank | Key | Severity | Source | Active when | Module / visibility gate |
|---|---|---|---|---|---|
| 1 | `absent_days` | critical | Section 1 calculator `Absent` | ≥ 1 | attendance |
| 2 | `missing_clock_outs` | critical | calculator | ≥ 1 | attendance |
| 3 | `over_break` | warning | discipline query `OverBreakDays`/`OverBreakMinutes` (value = minutes, denominator = days) | minutes > 0 | attendance |
| 4 | `late_clock_ins` | warning | calculator `Late` (denominator = attended) | ≥ 1 | attendance |
| 5 | `early_clock_outs` | warning | discipline query | ≥ 1 | attendance |
| 6 | `short_hours_days` | warning | calculator `ShortHours` | ≥ 1 | attendance |
| 7 | `location_violations` | warning | discipline `LocationViolations` | not null and ≥ 1 | location tracking enabled |
| 8 | `overdue_tasks` | critical | work overview `Overdue` (denominator = assigned) | ≥ 1 | work management |
| 9 | `pending_approvals` | info | approvals overview `Pending` | ≥ 1 | approvals visible to viewer |
| 10 | `off_schedule_work` | info | calculator `WorkedOnNonWorkingDay + WorkedDuringTimeOff` | ≥ 1 | attendance |
| 11 | `idle_activity_alerts` | warning | Monitoring `INotificationRepository.CountByTypeAsync(LongIdleAlert, LowActivityAlert)` | ≥ 1 | monitoring enabled for employee |
| 12 | `monitoring_exceptions` | critical | Monitoring `Exception` rows in range (`SustainedLowActivity`, `AttendanceIrregularity`, `UnusualActivityPattern`, `IdentityAnomaly`) | ≥ 1 | monitoring enabled for employee |

Ordering: strictly by `Rank` (the user's confirmed importance order; ranks are unique so no tiebreak is needed). `Severity` drives colour/chip only, never order.

Each provider is a small `IEmployeeSignalProvider` (one per source group: attendance, work, approvals, monitoring) returning zero or more `EmployeeSignal`s; the query handler runs them, gates by module/visibility, sorts. A provider that throws is logged and skipped — one failing source never blanks the whole list.

### Frontend

- `OverviewSignalsStore`-style loader (via `createWidgetLoader`) fetches `/overview/signals` once per period (and once for the compare month when on).
- **KPI strip** (`overview-productivity-discipline-chart` card row): always 4 cards. Fill with the top active signals; remaining slots are filled with baseline KPIs in order: Days attended (`present / workingDays`), Tasks completed (`completed / assigned`), On-time delivery (`onTimeRatePercent`), Leave days (`leaveDays`). (Hours worked was dropped at plan time: no Overview endpoint returns worked hours.) A signal card uses its severity colour; a baseline card uses the neutral/positive style.
- **Needs Attention**: renders all active signals in returned order, one row each (icon by category, label, detail, severity chip). If the list is empty the card is **not rendered** (`@if`). The four hardcoded rows and their `@let` plumbing are deleted.

## Error handling

- Signals endpoint: 404 employee not found, 403 via read guard; provider failure → that provider's signals omitted, others returned.
- Frontend: signals load error → KPI strip falls back to the 4 baseline cards; Needs Attention hidden.
- Compare month out of bounds or equal to selected → store rejects the set.

## Testing

- Backend unit: `ExpectedWorkdayCalendar` (weekends, holidays, mid-month hire, termination, today cut-off, inverted range); calculator classification table above row by row; each signal provider (active / inactive / gated); handler ordering + provider-failure isolation.
- Backend integration: `/overview/signals` 200 shape, 403 for unrelated viewer; `/overview/attendance` for an employee with zero records returns `WorkingDays > 0`, `Absent == WorkingDays`.
- Frontend (Vitest): store bounds/clamp/compare month; delta helper; KPI strip fill order (0, 2, 5 active signals); Needs Attention hidden when empty, ordered when not.

## Out of scope

- Tenant-configurable severity/thresholds.
- Per-employee schedules / shift rosters (expected days use the legal entity week only, same as clock-in today).
- Half-day holidays.

## Coordination note

At spec time (2026-09-30) another session has uncommitted frontend work on exactly these files (`overview-tab.component.ts`, its specs, the attendance/time-off/work widgets, `widget-card`, and the untracked `overview-productivity-discipline-chart/`). Frontend implementation must start only after that work is committed.
