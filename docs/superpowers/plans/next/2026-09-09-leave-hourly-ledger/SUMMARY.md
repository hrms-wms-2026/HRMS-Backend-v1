# 2026-09-09-leave-hourly-ledger (backend)

**Status:** implemented on `feat/leave-hourly-ledger`. Design: `docs/superpowers/specs/next/2026-09-09-leave-hourly-ledger-design.md`. Companion frontend plan: `Hrms--Web-application---front-end---v1/docs/superpowers/plans/next/2026-09-09-leave-hourly-ledger/`.

Execute in order. Each part is independently testable; later parts fail compile until earlier ones land.

| Part | What ships |
|---|---|
| `part-1-work-day-hours-and-overnight.md` | `WorkDayHoursCalculator` + overnight General Settings validator |
| `part-2-leave-request-hour-calculator.md` | `LeaveRequestHourCalculator` unit tests (not wired to HTTP yet) |
| `part-3-ledger-hours-schema-api-and-migration.md` | Hours columns, `StartAt`/`EndAt`, generate × workDayHours, EF backfill |

Frontend Part 1 can start after backend Part 1. Frontend Part 2 needs backend Part 3.
