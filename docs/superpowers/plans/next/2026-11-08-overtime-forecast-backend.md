# Overtime Forecast (backend) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Project Monitoring Engine rate-aware (4-week conversion rate) and add `GET work/forecast/modules` and `GET work/forecast/employees` plus their My Team widget registrations.

**Architecture:** One pure static class `OvertimeForecast` holds every formula (rate, figures, status, EDF windows). `IConversionRateReader` turns attendance + completed tasks into a `ConversionRateSet` for the 28-day window. The snapshot loader, the two live checks, the hourly job (via the loader) and both new query handlers all go through these two pieces.

**Tech Stack:** .NET 10, MediatR, EF Core, xUnit + Moq.

**Spec:** `docs/superpowers/specs/next/2026-11-08-overtime-forecast-widgets-design.md` (Parts 1, 2, 4c).

## Global Constraints

- Window = `[today − 28, today − 1]` in the legal entity's zone; today excluded.
- Rate = `min(1, taskMinutes / workedMinutes)`; `null` when workedMinutes < 60. `rateEff = rate ?? 1`.
- Status strings: `on_track`, `raise_rate`, `overtime`, `no_history`. Rule codes unchanged.
- No migration, no new table. Rules stay pure.
- JSON camelCase, field names exactly as spec §4a/§4b.
- Permission checks first in every handler. No `[AllowAnonymous]`.

## Review Focus

- Rate 0 (worked but completed nothing): `overtimeHours` must not divide by zero → reported as the shortfall `remaining − predicted`. Test in Task 1.
- Capacity 0 (deadline today on a weekend / start after end): status `overtime` when remaining > 0, `requiredRate` null. Test in Task 1.
- Employee with no open estimated tasks: `on_track` with zeros even with no history. Test in Task 1.
- Parent and child module both readable: hour totals count only the top-most listed module. Test in Task 4.
- Viewer with no projects / no covered team: empty page, zero totals, 4 trend points with null rates. Tests in Tasks 4/5.

---

### Task 1: `OvertimeForecast` pure maths

**Files:** Create `src/ONEVO.Application/Features/WorkManagement/Monitoring/Services/OvertimeForecast.cs`; Test `tests/ONEVO.Tests.Unit/Features/WorkManagement/Monitoring/OvertimeForecastTests.cs`

**Produces:**
- `OvertimeForecast.WindowDays = 28`, `MinWorkedMinutes = 60`, `(DateOnly Start, DateOnly End) Window(DateOnly today)`
- `decimal? Rate(int taskMinutes, int workedMinutes)`
- `ForecastFigures Compute(decimal remaining, decimal capacity, decimal? rate)` → `record ForecastFigures(decimal Remaining, decimal Capacity, decimal? Rate, decimal Predicted, decimal? RequiredRate, decimal OvertimeHours, string Status)`
- `EmployeeWindowForecast? EmployeeWindows(IEnumerable<(DateOnly Due, decimal Remaining)> items, WorkCalendar calendar, DateOnly today, decimal? rate)` → worst window (max overtime, then max requiredRate), `RequiredRate` = max over windows, status = most severe window status (`no_history` when rate null); null when no items.
- `ForecastStatuses` constants; `Severity(string status)`.

Tests: on_track, raise_rate (rate 0.5, remaining 60, capacity 100 → predicted 50, required 0.6, overtime 20), overtime (required > 1), capacity 0, rate 0, rate null → no_history with rateEff 1, Rate capped at 1, Rate null under 60 min, Window(2026-11-08) = 10-11..11-07, EDF worst window picks largest overtime.

### Task 2: Conversion rates (set + reader)

**Files:** Create `Monitoring/Services/ConversionRates.cs` (records + pooling), `Monitoring/Services/ConversionRateReader.cs` (`IConversionRateReader` + implementation); modify `WorkCalendarResolver.cs` (add `Task<LegalEntity?> LegalEntityForProjectAsync`), DI in `src/ONEVO.Infrastructure/DependencyInjection.cs`. Test `ConversionRatesTests.cs`, `ConversionRateReaderTests.cs`.

**Produces:**
- `record EmployeeConversionRate(Guid EmployeeId, int WorkedMinutes, int TaskMinutes, decimal? Rate, IReadOnlyList<int> WeekWorked, IReadOnlyList<int> WeekTask)`
- `record ConversionRateSet(DateOnly Today, DateOnly WindowStart, DateOnly WindowEnd, IReadOnlyDictionary<Guid, EmployeeConversionRate> ByEmployee)` with `PooledRate Pool(IEnumerable<Guid>)` (`record PooledRate(int WorkedMinutes, int TaskMinutes, decimal? Rate)`), `IReadOnlyList<RateTrendPoint> Trend(IEnumerable<Guid>)` (4 points, week i starts `WindowStart + 7i`), `decimal? RateOf(Guid)`, `static Empty(DateOnly today)`.
- `IConversionRateReader.ReadAsync(Guid tenantId, LegalEntity? legalEntity, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct) : Task<ConversionRateSet>` — sources exactly as `GetTeamWorkTaskHoursQueryHandler` (`ListRecordsAsync` + `PlannerActuals.AttendanceMinutes`, `ITeamCompletedTaskReader` + `TeamWorkTaskMath.EstimateMinutes`), zone/today from `AttendanceScheduleResolver` (UTC + `clock.Today` when no legal entity).

Tests: pooled rate is Σtask/Σworked (not mean), trend buckets, reader ignores records outside window, open-record today never read.

### Task 3: Rate-aware engine

**Files:** Modify `ProjectMonitorSnapshot.cs` (`MonitorModule` gains `decimal? ConversionRate = null`; snapshot gains `ConversionRateSet? Rates = null`), `ModuleManpower.cs` (`People(...)` set; `Count` uses it), `WorkCapacityCalculator.cs` (`FinishDate(int people, DateOnly from, decimal hours, decimal rate = 1m)`), `ProjectMonitorRules.cs`, `ProjectMonitorSnapshotLoader.cs`, `CheckModuleCapacityQueryHandler.cs`, `CheckTaskLoadQueryHandler.cs`, `MonitorDtos.cs` (`ModuleCapacityCheckResponse` gains `decimal? ConversionRate = null, decimal? PredictedHours = null`). Tests extend `ProjectMonitorRulesTests.cs`, `MonitorCheckQueryHandlerTests.cs`.

- `ModuleOverCapacity`: allocated > `capacity × rateEff`. `ModuleCapacityShortfall`: remaining > `predicted`; finish date at `rateEff`. `EmployeeDeadlineOverload(..., decimal? rate = null)`: demand > `capacity(1,today,d) × rateEff`.
- With a rate the message reads "68 h of work remain; at the team's 42% task rate (last 4 weeks) they will complete about 50 h before Nov 20." Without a rate the old messages stay. Details add `conversionRate`, `predictedHours`, `requiredRate`, `overtimeHours`, `rateSource` (`history`/`none`).
- Loader: rates for every member/owner/assignee via `LegalEntityForProjectAsync` + reader; module rate = pool over `ModuleManpower.People`.

Tests: rate 0.5 makes 300 h allocation over 400 h capacity flag; rate null keeps old result; EDF with rate 0.5 flags 30 h demand vs 40 h capacity; messages contain "task rate (last 4 weeks)"; module check returns ConversionRate.

### Task 4: `GET work/forecast/modules`

**Files:** Create `src/ONEVO.Application/Features/WorkManagement/Forecast/DTOs/ForecastResponses.cs`, `Forecast/Queries/GetModuleForecast/{Query,Handler}.cs`, `Forecast/Services/ForecastPaging.cs`; Controller `src/ONEVO.Api/Controllers/Tenant/WorkManagement/WorkForecastController.cs`. Test `tests/.../WorkManagement/Forecast/GetModuleForecastQueryHandlerTests.cs`.

Population: projects from the viewer's active memberships ∪ owned modules, active, not achieved, not system, in the viewer's legal entity. Readable modules: all if project lead, else at/below a membership or owned module. Not achieved, end ≥ today. Hours totals and project bars over top-most listed modules; status counts over all listed modules (before the status filter). Query `status, projectId, search, sort, page, pageSize (1..100, default 10)`. Members for page items only.

Tests: unauthenticated → 403; parent+child counted once in totals; status filter; sort overtime desc; non-member module hidden.

### Task 5: `GET work/forecast/employees`

**Files:** Create `Forecast/Queries/GetEmployeeForecast/{Query,Handler}.cs`, `Forecast/Services/IForecastTaskReader.cs`, `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfForecastTaskReader.cs`, DI. Test `GetEmployeeForecastQueryHandlerTests.cs`.

- `IForecastTaskReader.ListOpenAssignedAsync(tenantId, employeeIds)` → `ForecastOpenTask(TaskId, EmployeeId, ProjectId, ProjectName, ModuleId, ModuleTitle, ModuleEndDate, DueDate?, EstimatedHours?, CompletedHours, AssigneeCount)`; `ListMemberModulesAsync(tenantId, employeeIds)` → `ForecastMemberModule(EmployeeId, ModuleId, ModuleTitle, ProjectId, ProjectName, ModuleEndDate)`.
- Gate `attendance:read` (handler + `[RequirePermission]`), population `CoveredTeam`. Query `status, sort (overtime|requiredRate|rate|name), search, page, pageSize`.

Tests: no permission → 403; shared task split; no tasks → on_track zeros; search by name.

### Task 6: Widget registration

Modify `DashboardWidgetRegistry.cs` (two catalog entries, 2,2,12,8, `false`), `DashboardWidgetAccess.cs` (`TeamEmployeeForecast` with TeamWorkVsTask; `TeamModuleForecast` when `CanViewWorkForecast`), `MyTeamCapabilitiesResponse` (+ `bool CanViewWorkForecast = false`), capabilities handler (work module on and leads work or holds any active project membership). Update registry/access tests.

### Task 7: Gate + report

`pwsh ../dev-env/scripts/verify.ps1 backend` green; `find-unauthorized-endpoints.ps1 backend` = 0; report `docs/reports/work-management/2026-11-08-overtime-forecast-backend.md`.
