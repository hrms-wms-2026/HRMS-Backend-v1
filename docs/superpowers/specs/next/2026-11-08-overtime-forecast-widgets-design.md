# Overtime Forecast: rate-aware monitor engine + two My Team widgets

Date: 2026-11-08. Both repos. Decided with the user in chat. Items marked **(call)** are Claude's choices; the user can override them.

## 1. Why

Every dashboard widget so far reports the past. This one **predicts**. The owner or manager wants two answers:

1. To finish this module (or this person's work) before the deadline, **does the team need overtime?**
2. Is the team's current **work-hour → task-hour conversion rate** good enough, or does it have to rise, and by how much?

A small project can live with a low rate, so nobody needs to be pushed. A heavy one needs a higher rate, or overtime.

## 2. The three numbers

- **Allocated hours**: the project's hours, split down to modules and tasks (already stored: `MonitorModule.AllocatedHours`, task `EstimatedHours`).
- **Conversion rate (task rate)**: the share of an employee's working time (attendance: clocked time minus breaks) that turns into completed task hours (the estimates of the top-level tasks they completed). It is the "task share" the **Work vs task hours** widget already computes.
- **Predicted task hours before the deadline** = max work hours from today to the deadline × conversion rate.

### Rate rules (user)

- **Only the last 4 weeks count.** People and modules start slow and speed up, so old history must not drag down today's prediction.
  **(call)** The window is the 28 days `[today − 28, today − 1]` in the legal entity's zone. Today is excluded because it is partial.
- **Employee rate** `r_e = taskMinutes / workedMinutes` over the window. It uses the same sources as `GetTeamWorkTaskHoursQueryHandler`: `IAttendanceReadRepository` + `PlannerActuals.AttendanceMinutes`, and `ITeamCompletedTaskReader`. Tasks from every project count.
- **Module rate** `r_m` is pooled over the module's manpower (`ModuleManpower`: members and owners of the module and all sub-modules): Σ their task minutes ÷ Σ their worked minutes. It is not an average of the individual rates.
  **(call)** Attendance can't be split by module, so the module rate is the module team's overall rate. Their task minutes from other projects also count.
- **(call)** The rate is capped at **100%** (`min(1, …)`), so a prediction never exceeds the physical max work hours.
- **(call)** **No history:** when workedMinutes < 60 in the window, the rate is `null`. The status is `no_history` and the engine falls back to the old behaviour (rate = 1).
- **(call)** The rates are computed fresh on every evaluation. There is no new table and no migration. If timing shows it's too slow, the builder may cache them per (tenant, employee, day) in memory.

### Derived figures (per module or employee)

```
capacity      = max work hours from max(today, start) to deadline   (existing WorkCalendar.Capacity)
remaining     = hours still to do (module: Allocated − Completed; employee: EDF window demand, see §3)
rateEff       = rate ?? 1
predicted     = capacity × rateEff
requiredRate  = remaining / capacity          (may be > 1; null when capacity = 0)
overtimeHours = max(0, remaining − predicted)     (TASK hours that won't get done in normal hours at the current rate; never > remaining)
maxTaskRate   = the tenant's limit (wm_task_rate_settings.max_task_rate; no row = 0.90; clamped to 0.5–1.0)
status        = on_track    if predicted ≥ remaining
                raise_rate  if predicted < remaining and requiredRate ≤ maxTaskRate   (UI: "Overtime risk")
                overload    if requiredRate > maxTaskRate or capacity = 0           (even pushing hard won't finish it)
                no_history  if rate is null (figures still filled using rateEff = 1)
overload flag = requiredRate > maxTaskRate or capacity = 0, whatever the history (alert details `overload`)
```

**Max task rate (2026-11-09, user).** A team realistically converts at most ~90–95% of work hours into task hours.
Work that needs more is **overload**, which is different from "raise the rate or do overtime". The limit is stored
per tenant in the tenant-owned table `wm_task_rate_settings` (one row per tenant: `max_task_rate numeric(5,4)`,
`updated_by_employee_id`). A teammate builds the Settings edit UI later, so there is no edit endpoint yet. The
`overload` status replaces the earlier `overtime` status code.

## 3. Part 1, backend engine change (Project Monitoring Engine)

Files: `Features/WorkManagement/Monitoring/Services/*`.

- Add an `IConversionRateReader` (Application interface + Infrastructure implementation, or a service over the existing readers). It returns `rate`, `workedMinutes` and `taskMinutes` per employee for the window. `ProjectMonitorSnapshotLoader` loads it for every person in the snapshot, adds each module's pooled rate to `MonitorModule` (new fields `ConversionRate decimal?`, plus the member ids so the pooled figure can be computed), and adds `EmployeeRates` to the snapshot.
- `ProjectMonitorRules`:
  - `ModuleOverCapacity`: the allocation must fit `capacity × rateEff` (over the module's whole start..end span).
  - `ModuleCapacityShortfall`: the remaining hours must fit `capacity × rateEff`. `FinishDate` uses `people × DailyHours × rateEff` per day.
  - `EmployeeDeadlineOverload`: EDF, each window's demand must fit `capacity(1, today, d) × r_e,eff`.
  - Messages name the rate, e.g. "68 h of work remain; at the team's 42% task rate (last 4 weeks) they will complete about 50 h before Nov 20." Details add `conversionRate`, `predictedHours`, `requiredRate`, `overtimeHours`, `rateSource` (`"history"` or `"none"`).
- The live form checks (`CheckModuleCapacity`, `CheckTaskLoad`), the hourly job and the Tree-load refresh all go through the same rules, so the Tree red dots follow the new prediction automatically.
- Rule codes don't change, so existing open alerts keep their identity.
- Keep the rules pure. Unit-test every branch: no history, rate 0, rate capped, on_track / raise_rate / overload (exactly at the limit, above it, capacity 0, missing row → 0.90, clamp), the EDF windows with a rate.

## 4. Part 2, backend widget endpoints

Put the forecast maths in one shared service (e.g. `IOvertimeForecastService`). The engine rules and both endpoints must use it, so the widget, the Tree dots and the form checks never disagree.

### 4a. `GET work/forecast/modules`, widget key `team-module-forecast`

Population **(call)**: modules the viewer can read under the existing WM read access (`IModuleReadAccess` / project lead / owner), in active projects of the viewer's legal entity, **not achieved**, with **end date ≥ today**. The table lists every such module, with its path. **(call)** Totals, the pie and the project bars use only modules whose parent is not also in the list, because a parent's allocation already contains its children's.

Query: `status?` (`on_track|raise_rate|overload|no_history`), `projectId?`, `sort` (`overtime` default | `requiredRate` | `endDate` | `name`), `page`, `pageSize` (card asks for 10, popup pages 25).

```jsonc
{
  "asOf": "2026-11-08", "windowStart": "2026-10-11", "windowEnd": "2026-11-07",
  "totals": { "modules": 14, "onTrack": 6, "raiseRate": 4, "overload": 3, "noHistory": 1, "maxTaskRate": 0.9,
              "allocatedHours": 2400, "completedHours": 900, "remainingHours": 1500,
              "capacityHours": 2600, "predictedHours": 1100, "overtimeHours": 950,
              "teamRate": 0.42, "requiredRate": 0.58 },
  "rateTrend": [ { "weekStart": "2026-10-12", "rate": 0.31 }, … 4 points ],   // pooled over all module people
  "projects": [ { "projectId": "…", "name": "…", "allocatedHours": 0, "remainingHours": 0,
                  "predictedHours": 0, "capacityHours": 0, "overtimeHours": 0, "modulesAtRisk": 0 } ],
  "items": [ { "moduleId": "…", "projectId": "…", "projectName": "…", "title": "…", "path": "Parent › Child",
               "startDate": "…", "endDate": "…", "workingDaysLeft": 8,
               "allocatedHours": 0, "completedHours": 0, "remainingHours": 0, "capacityHours": 0,
               "conversionRate": 0.42, "predictedHours": 0, "requiredRate": 0.6, "overtimeHours": 0,
               "status": "raise_rate", "memberCount": 4,
               "members": [ { "employeeId": "…", "name": "…", "avatarFileId": null,
                              "conversionRate": 0.5, "workedMinutes": 0, "taskMinutes": 0 } ] } ],
  "total": 14, "page": 1, "pageSize": 10
}
```

### 4b. `GET work/forecast/employees`, widget key `team-employee-forecast`

Population: `CoveredTeam`, the same as Team Status and Work vs task hours.

Per employee, take the open, estimated tasks that are due on or after today. Each shared task's remaining hours are split evenly between its assignees, exactly as the current EDF rule does. **(call)** For every distinct deadline d: `demand_d` vs `capacity(1, today, d) × r_e,eff`.
- The **binding window** (highest `requiredRate`; ties go to the larger shortfall; no capacity with work due counts as highest) supplies every figure: `remainingHours`, `capacityHours`, `predictedHours`, `requiredRate`, `overtimeHours` and `deadline`. The status is the most severe across all windows.
- With no open estimated tasks, the employee is `on_track` with zeros.

Query: `status?`, `sort` (`overtime` default | `requiredRate` | `rate` | `name`), `search?`, `page`, `pageSize`.

```jsonc
{
  "asOf": "…", "windowStart": "…", "windowEnd": "…",
  "totals": { "people": 12, "onTrack": 7, "raiseRate": 2, "overload": 2, "noHistory": 1, "maxTaskRate": 0.9,
              "remainingHours": 0, "capacityHours": 0, "predictedHours": 0, "overtimeHours": 0,
              "teamRate": 0.4, "requiredRate": 0.55 },
  "rateTrend": [ { "weekStart": "…", "rate": 0.3 } ],
  "items": [ { "employeeId": "…", "name": "…", "avatarFileId": null, "department": "…",
               "workedMinutes": 0, "taskMinutes": 0, "conversionRate": 0.38,
               "deadline": "2026-11-20", "remainingHours": 0, "capacityHours": 0, "predictedHours": 0,
               "requiredRate": 0.7, "overtimeHours": 12.5, "status": "raise_rate",
               "modules": [ { "moduleId": "…", "projectId": "…", "projectName": "…", "title": "…",
                              "endDate": "…", "remainingHours": 0, "openTasks": 3 } ] } ],
  "total": 12, "page": 1, "pageSize": 10
}
```

`modules` = the modules where the employee has open assigned tasks or is a member. The user asked for this explicitly: the detail view shows which modules the person is connected to.

### 4c. Registration

- Both go in `DashboardWidgetRegistry` under `DashboardKeys.MyTeam`, min 2x2, max 12x8, **catalog only** (not in the default layout).
  - Titles: "Module overtime forecast" and "Employee overtime forecast".
- Access in `DashboardWidgetAccess`:
  - Employee widget: gated like `team-work-vs-task`.
  - Module widget: the viewer must have WM read access to at least one project. **(call)**
- Permission checks first. Unit tests for the handlers and the forecast service. Architecture tests stay green. No migration expected.

## 5. Part 3, frontend: two widgets

**Follow `Hrms--Web-application---front-end---v1/docs/design/WIDGET_CREATION_RULES.md` exactly. Read it first.** The reference is `team-work-task-card` + its dialog. Each widget gets its own model, API service, store, layout/format/summary-cards utils, card, dialog and specs, plus a registry entry.

### Shared visual language **(call)**

- **Donut, "where the remaining hours go":**
  - green `#10b981` **Covered at current rate** = min(predicted, remaining)
  - amber `#fbbf24` **Needs a higher rate** = min(capacity, remaining) − covered
  - red `#f87171` **Needs overtime** = max(0, remaining − capacity)
  - The centre shows the **team rate %**, with "task rate · last 4 wk" under it.
- **Status chips:** On track (green), Overtime risk (amber, `raise_rate`), Overload (red, `overload`), No history (slate).
- **Hero figure:** the overtime hours ("~950 h overtime"), or "On track" when 0. The caption reads "Needs 58% task rate · now 42%".

### Module widget (`team-module-forecast`)

- **Mini:** summary strip.
- **Standard grows by:**
  1. KPI tiles: Allocated, Predicted, Task rate, Overtime h.
  2. The **project-wise bar chart**: `app-dashboard-bar-chart` with `pair` stacks remaining vs predicted per project and the capacity as `marker`, in a new `ui/` adapter.
  3. A ranking of the modules at risk: rate bar, overtime chip, status.
- **Summary cards:**
  - Task rate (spark, 4 weekly points)
  - Overtime needed (figure + bars per project)
  - Modules at risk (`rank`, overtime chips)
  - By status (`chips`)
- **Popup:**
  - KPI tiles: Allocated / Remaining / Predicted / Task rate vs required / Overtime.
  - The overall pie with an explanation, and the project bar chart.
  - The **module breakdown table**: #, module (path), project, deadline, days left, allocated, remaining, predicted, rate, required rate, overtime, status. Sortable, searchable, status filter, paged, CSV.
  - **Expanding a module row shows its team members table** (name, rate, worked, task hours). The user asked for this: for modules that need overtime, show the team members' names in the breakdown.

### Employee widget (`team-employee-forecast`)

- The same ladder. Tiles: Remaining, Predicted, Task rate, Overtime h.
- The chart is the per-employee rate vs required rate (`pair` bars, top N).
- The ranking is the people who need overtime.
- **Summary cards:**
  - Team task rate (spark)
  - Overtime needed (bars)
  - People at risk (`rank`)
  - By status (`chips`)
- **Popup:**
  - KPI tiles.
  - The overall pie.
  - The rate-vs-required chart.
  - The **employee breakdown table**: #, avatar + name, department, rate (4 wk), required rate, nearest deadline, remaining, predicted, overtime, status. Sortable, searchable, filterable, paged, CSV.
  - **Expanding a row shows the modules the employee is connected to** (module, project, deadline, remaining, open tasks).

## 6. Non-goals and anti-goals

- No new data entry, no editing of rates, no notifications beyond the existing monitor alerts.
- Don't count history older than 4 weeks. Don't scale donuts. Cards don't scroll. Don't hand-draw charts.
- The widget, the Tree dots and the form checks must never disagree. That's why there is one shared forecast service.

## 7. Done

- **BE:** unit and architecture tests green, Release build clean.
- **FE:** `npm run check:warnings` clean, new specs green, full suite has no new failures.
- **Browser pass by the user:** 2x2 → 12x8, popup, CSV.
- Work goes on a local branch `feature/overtime-forecast-widgets` in each repo, committed and **not pushed**.
