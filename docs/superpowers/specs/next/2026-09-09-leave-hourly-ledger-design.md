# Leave hourly ledger — Design

**Status:** Approved 2026-09-09 (chat brainstorm). Companion: `Hrms--Web-application---front-end---v1/docs/superpowers/specs/next/2026-09-09-leave-hourly-ledger-design.md` (same document). Amends `2026-08-21-leave-management-design.md` and `C:\HR\leave-management-complete.md` on unit of measure and request timing only — screens, permissions, and approval flow stay.

**Status (implementation):** next — no plan written yet.

## Problem

Leave is a year ledger of **days**, with request duration = working-date count and an optional AM/PM half-day (`0.5`). Afternoon and night shifts cannot be expressed. Night work windows (`22:00`–`06:00`) are rejected on General Settings because start must be before end on the same clock day.

## Decisions (locked)

1. Employee ledger (entitled / used / pending / remaining / request totals) is **decimal hours**, not days.
2. One leave day = that employee’s legal-entity **work start → work end minus break**. Different entities can have different hours for the same “14 days”.
3. A request is **From datetime → To datetime**. No AM/PM field.
4. Multi-day: start datetime to end datetime; in-between working shifts count as full `workDayHours`; weekends/holidays skipped; overnight hours belong to the **shift-start date**.
5. Storage is **decimal hours** (existing decimal-days pattern). Not integer minutes. Not a days-internal / hours-UI facade.
6. HR still authors types/policies in **days**. Conversion to hours happens at entitlement generate/recalculate and at request preview, using that employee’s current work window.

## Work-day hours

Legal entity General Settings already has `workStartTime`, `workEndTime`, `breakDurationMinutes`, `standardWorkingDays`, `timezone`.

```
shiftLength  = duration(workStart → workEnd)   // overnight if workEnd <= workStart
workDayHours = shiftLength − (breakDurationMinutes / 60)
```

- Both times set, or both empty. One-sided pair stays invalid.
- `workEnd <= workStart` is **valid** and means end is the next calendar day. Today’s `start_not_before_end` rule is removed.
- `workDayHours` must be `> 0`. Break ≥ shift length is a save error on General Settings.
- Times unset → entitlement generate **skips** that employee; request preview/submit **400** with “Set work start and end in General Settings first.” No silent 8h default.

Live line on Default work hours (same three fields, not replaced by a single hours box):

`{workDayHours} working hours / day · this is 1 leave day`  
plus `next day` on the end time when overnight.

Clock times stay. Leave overlap needs the window, not a bare hour count.

## Ledger vs config

| Stays days (HR config / calendar) | Becomes hours (employee ledger) |
|---|---|
| Leave type `defaultDaysPerYear` | Entitlement total / used / pending / carried forward |
| Policy `annualEntitlementDays`, `monthlyAccrualDays`, `minDaysPerRequest`, `maxConsecutiveDays` | Balance entitled / annual / used / pending / remaining |
| `minimumNoticeDays`, tenure months, year | Request `totalHours` / `paidHours` / `unpaidHours` |
| Working-day and holiday skip | Audit `hoursChanged` / `balanceAfter` |

`maxConsecutiveDays` and `documentRequiredAfterDays` stay **calendar working dates** in the request, not hours.

Generate:

```
hours = policyDays × workDayHours(employee.legalEntity)
```

`minDaysPerRequest` 0.5 on an 8h day → minimum **4.0** hours for that employee.

## Request model

Replace `StartDate` + `EndDate` + `HalfDayPeriod` with:

- `StartAt` `DateTimeOffset` (UTC)
- `EndAt` `DateTimeOffset` (UTC)

API JSON: `startAt`, `endAt` (ISO-8601 UTC). List views may derive local date/time in the legal-entity timezone for display.

`TotalDays` / `PaidDays` / `UnpaidDays` → `TotalHours` / `PaidHours` / `UnpaidHours`.

Wizard: From date + From time, To date + To time. Empty time defaults to work start on the from date and work end on the to date (overnight end = next calendar day @ work end). Existing `app-time-picker` is reused. Half-day dropdown is deleted.

## Hour calculator

Replace `LeaveRequestDayCalculator` with `LeaveRequestHourCalculator`. Interpret `StartAt`/`EndAt` in the legal-entity timezone.

For each shift whose **start date** lies on a working, non-holiday date in `[fromDate, toDate]`:

1. Shift interval: same-day `D@start → D@end`, or overnight `D@start → (D+1)@end`.
2. Overlap = `[StartAt, EndAt] ∩ shift interval`.
3. Full overlap → charge `workDayHours` (break already removed).
4. Partial overlap → charge overlap hours, **do not** subtract break.
5. Sum, round to **2 decimal hours**.

Overnight hours attach to the shift-start date (Fri 22:00–Sat 06:00 counts as Friday even if Saturday is a weekend).

| Example (09:00–18:00, 60 min break → 8.0h) | Hours |
|---|---|
| 9 Sep 09:00 → 18:00 | 8.0 |
| 9 Sep 14:00 → 18:00 | 4.0 |
| 9 Sep 22:00 → 10 Sep 06:00 with night window 22:00–06:00 | 8.0 |
| 9 Sep 09:00 → 11 Sep 18:00 (Mon–Wed working) | 24.0 |
| 9 Sep 14:00 → 11 Sep 13:00 | 16.0 |
| Fri 09:00 → Mon 18:00 | 16.0 |

Paid/unpaid split uses the same remaining-hours math as today’s remaining-days split.

## UI

**My Balances / Team / All Balances:** year filter unchanged. Copy and columns use hours. Secondary label allowed: `14 days at 8.0h/day`. History: `9 Sep 14:00 → 9 Sep 18:00 · 4h`; night `12 Sep 22:00 → 13 Sep 06:00 · 8h`.

**New Request:** leave type shows `92 hours remaining`. Preview: `Total 4.0 hours · 92h → 88h after submit`.

**Types / Policies:** still day fields. Entitlements generate preview and All Balances show hours.

**Approvals / Team Calendar:** same screens; duration and chip text in hours; calendar block uses `StartAt`–`EndAt`.

## Errors

Preview/submit 400:

- Work window unset
- `EndAt` ≤ `StartAt`
- Total hours `0` (range is only weekend/holiday)
- Total hours below `minDaysPerRequest × workDayHours`
- Remaining hours insufficient (same unpaid-split rules as today when unpaid is allowed)

General Settings save 400: one-sided work times; `workDayHours <= 0`.

## Migration (one EF migration, in place)

Existing decimal day amounts × that employee’s `workDayHours` at migration (if window unset, use `8.0` **only for this backfill**, then still require a window going forward).

- Full-day request: `StartAt` = start date @ workStart, `EndAt` = end date @ workEnd (overnight: end next day @ workEnd).
- AM: `StartAt` = date @ workStart, `EndAt` = `StartAt` plus `workDayHours / 2` (clock time). Example 09:00–18:00 − 1h → AM 09:00–13:00 = 4.0h.
- PM: `EndAt` = date’s shift end, `StartAt` = `EndAt` minus `workDayHours / 2`. Example → PM 14:00–18:00 = 4.0h.
- `HalfDayPeriod` column dropped after backfill.

Approved/pending stored totals are rewritten to hours; they are not recalculated from the new overlap rules beyond the mapping above.

Work-window edits after generate do **not** rewrite existing entitlements or requests. Recalculate / next generate uses the new window.

## Out of scope

- Per-employee or per-roster shift times (legal entity default only)
- Calendar hour-grid leave painting
- Changing notice/tenure/year to hours
- Attendance redesign — a day with any overlapping leave hours stays leave-aware; the AM/PM half-day attendance flag is no longer fed
- Integer-minute canonical storage

## Tests (must exist before claiming done)

- Calculator: full day, afternoon partial, overnight full, multi-day, weekend skip, break only on full overlap, unset window fails
- General Settings: overnight pair saves; live hours line; break ≥ shift blocked
- Wizard: no AM/PM; remaining hours; empty times default to window
- Generate: 14 days × 8.0h = 112.0h; skip when window unset
- Migration: 14 days → 112.0h; AM/PM → half-shift datetimes
