# Overtime forecast: backend summary

Date: 2026-11-08. Branch `feature/overtime-forecast-widgets` (local, not pushed).
Spec: `docs/superpowers/specs/next/2026-11-08-overtime-forecast-widgets-design.md`.
Plan: `docs/superpowers/plans/next/2026-11-08-overtime-forecast-backend.md`.

## What was built

- **`OvertimeForecast`** (`Features/WorkManagement/Monitoring/Services/OvertimeForecast.cs`) is the one shared, pure
  place for the maths: the 28-day window `[today−28, today−1]`, `rate = min(1, task/worked)` (null under 60 worked
  minutes), the predicted / required / overtime figures with their status, and the per-employee EDF windows. The
  spec suggested an `IOvertimeForecastService`; we used a static class because the rules must stay pure.
- **`IConversionRateReader`** (`ConversionRateReader.cs`) reads attendance worked minutes
  (`PlannerActuals.AttendanceMinutes`) and completed top-level task estimates (`ITeamCompletedTaskReader`) over the
  window in the legal entity's zone. These are the same sources as Work vs task hours. `ConversionRateSet.Pool`
  gives a group's pooled rate: Σtask / Σworked, plus the number of contributors. `Trend` gives 4 weekly points.
- **Engine.** `ProjectMonitorSnapshotLoader` reads the rates once per project. `ModuleOverCapacity`,
  `ModuleCapacityShortfall` (finish date at the rate) and `EmployeeDeadlineOverload` compare against
  `capacity × rateEff`. The hourly job, the Tree refresh and `CheckTaskLoad` all go through the loader.
  `CheckModuleCapacity` pools the form's people. Rule codes are unchanged. With no history the old behaviour and
  messages stay.
- **Endpoints.** `GET api/v1/work/forecast/modules` and `GET api/v1/work/forecast/employees`, in
  `WorkForecastController`.
- **Widgets.** `team-module-forecast` and `team-employee-forecast` are in `DashboardWidgetRegistry`: MyTeam, 2x2 to
  12x8, catalog only.

No migration and no new table.

## Overload and the max task rate (2026-11-09)

- **Status codes:** `on_track`, `raise_rate` (the UI says "Overtime risk": current rate < required ≤ max task rate),
  `overload` (required > max task rate, or no capacity left) and `no_history`. **`overload` replaces `overtime`**
  everywhere: the `status` values, the `status` filter and the counts (`totals.overload` instead of
  `totals.overtime`). The `sort=overtime` key stays: it sorts by `overtimeHours`. `overtimeHours` is still
  `max(0, remaining − predicted)`.
- **`totals.maxTaskRate`** (a fraction, e.g. 0.9) is on both endpoints: the viewer's Legal Entity limit, for the
  chart's threshold line. Each module item also has its own **`maxTaskRate`**: the limit of its project's Legal
  Entity, the one its status was judged by. The employee endpoint uses the viewer's Legal Entity limit.
- **Monitor alert details** add `maxTaskRate` and `overload` (true/false). An overloaded module alert reads
  "… h of work remain before Nov 20; overloaded: needs 125% task rate, above the 90% limit." The required rate is
  remaining ÷ capacity and reveals nobody's own rate, so it is printed for any pool size.
- **Settings table** `wm_task_rate_settings` holds the limit **per Legal Entity**:
  - Columns: tenant-owned `BaseEntity` columns, `legal_entity_id`, `max_task_rate numeric(5,4)` and
    `updated_by_employee_id`. Unique on (`tenant_id`, `legal_entity_id`). RLS coverage is in place.
  - With no row the code uses 0.90. Read values are clamped to 0.5–1.0.
  - Repository `ITaskRateSettingRepository`: `GetForLegalEntityAsync(tenantId, legalEntityId)`, plus
    `UpsertAsync(tenantId, legalEntityId, …)` for the Settings UI to come. There is no edit endpoint.
  - The engine reads the project's owning Legal Entity limit, the same one `WorkCalendar` comes from. It reads it
    once per evaluation in the snapshot loader, and in the module check.
- **Migrations (created, NOT applied — the developer applies them):**
  - `20261109043220_AddTaskRateSettings`
  - `20261109043340_AddTaskRateSettingsRlsPolicyCoverage` (the standard `tenant_isolation` policy)
  - An earlier per-tenant pair (`20261109042105_…`, `20261109042221_…`) existed briefly in commit `f172043f`. It
    was never applied anywhere and is replaced by the pair above.

## Deviations from the spec contract (frontend, please align)

1. **Who sees the module forecast.** The spec's call said "the modules the viewer can read". The table shows every
   member's own rate, so the population is now **only modules the viewer leads**: every module of a project they
   lead, or modules at or below one they own. Plain members get an empty list.
   The widget is gated on `isAvailable && leadsWork`, the same as team-progress. There is **no new capability
   flag**.
2. **Employee widget gate.** Same as `team-work-vs-task`: `isAvailable && canViewPeopleStatus`. The endpoint needs
   `attendance:read`.
3. **Extra optional query param.** `search` on `/modules` matches module title, path or project name.
4. **Paging.** `pageSize` is clamped to 1..500 (default 10; the CSV export may ask for 500). `page` is 1-based.
   Sort directions: `overtime` and `requiredRate` descending, `rate` ascending (no history last), `endDate` and
   `name` ascending.
5. **`rateTrend` week starts.** `rateTrend[i].weekStart = windowStart + 7i` (the window's own 7-day buckets,
   not ISO Mondays). There are always 4 points. `rate` is null for a week with under 60 worked minutes.
6. **Rounding.** Rates are fractions rounded to 4 decimals, hours to 2. `requiredRate` may be > 1. It is null when
   capacity is 0.
7. **Overtime hours (changed 2026-11-09 after the browser pass).** `overtimeHours = max(0, remaining − predicted)`:
   the task hours that won't get done in normal hours at the current rate, so it never exceeds `remainingHours`.
   It used to be divided by the rate, which exploded at low rates (27,035 h against 17,170 h remaining on real data).
   The same rule applies to items, totals, `projects[]` and the monitor alert details. Status is unchanged.
8. **Module totals.** `totals`, `projects[]` and `rateTrend` cover the whole population: `projectId`, `status`,
   `search` and paging narrow only `items` and `total`. Status counts cover every listed module. Hour sums, `teamRate`, `requiredRate` and `projects[]` use top-most listed modules
   only (no listed ancestor). `total` is the row count after the filters.
9. **Employee with no open estimated task due today or later.** `status: "on_track"`, `deadline: null`,
   hours 0, `requiredRate: 0`.
   Employee `status` is the most severe status across their EDF windows. Every figure (`deadline`, hours and
   `requiredRate`) comes from ONE binding window: the one with the highest required rate, ties broken by the larger
   shortfall. Totals `requiredRate` = Σ remaining ÷ Σ capacity of those same windows.
   `sort=rate` puts the lowest rate first and no-history last.
10. **Employee forecast scope.** It reads tasks and memberships only from projects in the **viewer's legal
    entity**, so project and module names from other legal entities never leak. The spec said "every project".
    The *rate* still counts completed tasks from every project.
11. **`ModuleCapacityCheckResponse`.** It gains the optional fields `conversionRate` and `predictedHours`.

## Privacy hardening (from the automated security review)

Individual task rates are performance data, so:

- **Task-overload alerts are visible to the whole module.** They never print the assignee's rate, predicted hours
  or overtime. Details keep `requiredRate` and `rateSource`. The `conversionRate`, `predictedHours` and
  `overtimeHours` details from spec §3 are therefore present on the module rules only.
- **`CheckTaskLoad`.** Only the caller's own rate is applied (and shown). Every other assignee is checked at 100%,
  because the caller controls the form's hours and due date and could otherwise probe a colleague's rate through
  the warning. The hourly monitor and the Tree still use each assignee's rate.
- **Module rules print the team rate only when it is pooled over 3 or more contributors.** A member who knows
  their own rate can't work out a single colleague's. Module alert details go only to Inbox recipients (the holder
  above), so they keep all the forecast fields.
- **`CheckModuleCapacity`.**
  - It pools only the project's active members and the caller.
  - Below 3 contributors it checks at 100% and returns `conversionRate`/`predictedHours` as null, so the warnings
    can't be used to probe a small team's rate.

## Endpoint examples

`GET api/v1/work/forecast/modules?page=1&pageSize=10`

```json
{
  "asOf": "2026-11-09", "windowStart": "2026-10-12", "windowEnd": "2026-11-08",
  "totals": { "modules": 2, "onTrack": 0, "raiseRate": 0, "overload": 2, "noHistory": 0, "maxTaskRate": 0.9,
              "allocatedHours": 200, "completedHours": 0, "remainingHours": 200, "capacityHours": 160,
              "predictedHours": 80, "overtimeHours": 120, "teamRate": 0.5, "requiredRate": 1.25 },
  "rateTrend": [ { "weekStart": "2026-10-12", "rate": 0.5 }, { "weekStart": "2026-10-19", "rate": null },
                 { "weekStart": "2026-10-26", "rate": null }, { "weekStart": "2026-11-02", "rate": 0.5 } ],
  "projects": [ { "projectId": "…", "name": "Payments", "allocatedHours": 200, "remainingHours": 200,
                  "predictedHours": 80, "capacityHours": 160, "overtimeHours": 120, "modulesAtRisk": 2 } ],
  "items": [
    { "moduleId": "…", "projectId": "…", "projectName": "Payments", "title": "Checkout", "path": "Checkout",
      "startDate": "2026-10-26", "endDate": "2026-11-20", "workingDaysLeft": 10,
      "allocatedHours": 200, "completedHours": 0, "remainingHours": 200, "capacityHours": 160,
      "conversionRate": 0.5, "predictedHours": 80, "requiredRate": 1.25, "overtimeHours": 120,
      "status": "overload", "memberCount": 2, "maxTaskRate": 0.9,
      "members": [ { "employeeId": "…", "name": "Kaja", "avatarFileId": null, "conversionRate": 0.5, "workedMinutes": 600, "taskMinutes": 300 },
                   { "employeeId": "…", "name": "Nila", "avatarFileId": null, "conversionRate": 0.5, "workedMinutes": 600, "taskMinutes": 300 } ] },
    { "moduleId": "…", "projectId": "…", "projectName": "Payments", "title": "Card form", "path": "Checkout › Card form",
      "startDate": "2026-10-26", "endDate": "2026-11-13", "workingDaysLeft": 5,
      "allocatedHours": 80, "completedHours": 0, "remainingHours": 80, "capacityHours": 40,
      "conversionRate": 0.5, "predictedHours": 20, "requiredRate": 2, "overtimeHours": 60,
      "status": "overload", "memberCount": 1, "maxTaskRate": 0.9, "members": [ … ] }
  ],
  "total": 2, "page": 1, "pageSize": 10
}
```

`GET api/v1/work/forecast/employees?page=1&pageSize=10`

```json
{
  "asOf": "2026-11-09", "windowStart": "2026-10-12", "windowEnd": "2026-11-08",
  "totals": { "people": 3, "onTrack": 2, "raiseRate": 1, "overload": 0, "noHistory": 0, "maxTaskRate": 0.9,
              "remainingHours": 30, "capacityHours": 40, "predictedHours": 20, "overtimeHours": 10,
              "teamRate": 0.7317, "requiredRate": 0.75 },
  "rateTrend": [ { "weekStart": "2026-10-12", "rate": 0.7317 }, { "weekStart": "2026-10-19", "rate": null },
                 { "weekStart": "2026-10-26", "rate": null }, { "weekStart": "2026-11-02", "rate": null } ],
  "items": [
    { "employeeId": "…", "name": "Arun", "avatarFileId": null, "department": "Eng",
      "workedMinutes": 600, "taskMinutes": 300, "conversionRate": 0.5,
      "deadline": "2026-11-13", "remainingHours": 30, "capacityHours": 40, "predictedHours": 20,
      "requiredRate": 0.75, "overtimeHours": 10, "status": "raise_rate",
      "modules": [ { "moduleId": "…", "projectId": "…", "projectName": "Payments", "title": "Checkout",
                     "endDate": "2026-11-20", "remainingHours": 141, "openTasks": 4 } ] },
    { "employeeId": "…", "name": "Bala", "avatarFileId": null, "department": "Eng",
      "workedMinutes": 600, "taskMinutes": 600, "conversionRate": 1,
      "deadline": null, "remainingHours": 0, "capacityHours": 0, "predictedHours": 0,
      "requiredRate": 0, "overtimeHours": 0, "status": "on_track",
      "modules": [ { "moduleId": "…", "projectId": "…", "projectName": "Payments", "title": "Refunds",
                     "endDate": "2026-11-20", "remainingHours": 0, "openTasks": 0 } ] }
  ],
  "total": 3, "page": 1, "pageSize": 10
}
```

The figures above come from the unit-test scenarios (`GetModuleForecastQueryHandlerTests`,
`GetTeamEmployeeForecastQueryHandlerTests`).

## Verification

- `verify.ps1 backend`: the Release build passes. Unit tests: 7751 pass and 3 fail. All 3 failures are in
  `MemoryPlatformMfaChallengeStoreTests` (Auth). They are pre-existing and unrelated to this branch, which touches
  no auth code: the test pins its clock to 2026-11-05, but `MemoryCache` expires on the real clock, so the
  challenges have already expired today. All WorkManagement / Dashboard / TimeAttendance tests pass (2395).
- Architecture tests: the gate stops after the unit step fails, so they were run directly with
  `dotnet test tests/ONEVO.Tests.Architecture -c Release`. 755 pass, 0 fail.
- `find-unauthorized-endpoints.ps1 backend`: 0 actions without an authorization attribute.
- Not done: the Testcontainers integration suite (`-Full`) and a live browser or API pass.

## Follow-ups

- Fix the time-bombed `MemoryPlatformMfaChallengeStoreTests` (inject a time provider into the `MemoryCache`, or
  use a relative clock).
- If evaluation is slow, cache the rates per (tenant, employee, day). Today the hourly job reads 28 days of
  attendance per project.
