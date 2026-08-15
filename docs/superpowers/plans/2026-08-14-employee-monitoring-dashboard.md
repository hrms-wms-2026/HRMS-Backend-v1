# Employee Monitoring Dashboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the backend APIs and metrics needed for the Employee Monitoring Dashboard MVP.

**Status:** Implemented and verified on 2026-08-14 with the monitoring-focused unit test suite.

**Architecture:** Keep collection in the Tray App and Agent Service. Extend backend aggregation and query APIs so a future web dashboard can read employee status, productivity metrics, top apps, and alerts without adding new collection behavior.

**Tech Stack:** .NET, ASP.NET Core controllers, MediatR, EF Core repositories, xUnit, FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-08-14-employee-monitoring-dashboard-design.md`

## Global Constraints

- Backend-only slice; no HR web frontend exists in this workspace.
- Do not store keystroke content, mouse coordinates, raw window titles, clipboard content, or full browser URLs.
- Dashboard endpoints require `monitoring:read`.
- Employee visibility must use existing `EmployeeVisibilityScope`.
- Status freshness window is 5 minutes.
- Default shift start is `09:00`, shift end is `18:00`, grace is `10` minutes.
- Default long idle threshold is `120` minutes.
- Default low activity score threshold is `50`.
- Default low data coverage threshold is `60`.

---

### Task 1: App Categorization In Daily Summary

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Services/AppUsageCategorizer.cs`
- Modify: `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Services/ActivityDailySummaryAggregator.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/AppUsageCategorizerTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/ActivityDailySummaryAggregatorTests.cs`

**Interfaces:**
- Produces: `AppUsageCategorizer.Categorize(string? processName): AppUsageCategory`
- Produces: `AppUsageCategory` enum values `Productive`, `Meeting`, `Personal`, `Unknown`
- Produces: `TopAppsJson` serialized as a list of objects with `appName`, `totalSeconds`, and `category`

- [ ] Step 1: Write tests for process categorization.
- [ ] Step 2: Run categorization tests and verify they fail because `AppUsageCategorizer` does not exist.
- [ ] Step 3: Implement `AppUsageCategorizer` with deterministic process-name matching.
- [ ] Step 4: Run categorization tests and verify they pass.
- [ ] Step 5: Write aggregation tests proving productive, meeting, personal, unknown, and top-app metrics populate from snapshots.
- [ ] Step 6: Run aggregation tests and verify they fail on current zero/empty summary behavior.
- [ ] Step 7: Update `ActivityDailySummaryAggregator.Aggregate` to compute app category minutes and top apps.
- [ ] Step 8: Run focused aggregation tests and verify they pass.

### Task 2: Dashboard DTOs And Status Rollup

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/Dashboard/DTOs/MonitoringDashboardDto.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/Dashboard/Services/MonitoringDashboardStatusService.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/Dashboard/MonitoringDashboardStatusServiceTests.cs`

**Interfaces:**
- Produces: `MonitoringEmployeeStatus` enum values `Active`, `Idle`, `Offline`
- Produces: `MonitoringDashboardStatusService.ResolveStatus(DateTimeOffset? latestCapturedAt, bool? isIdle, DateTimeOffset now): MonitoringEmployeeStatus`
- Produces: `MonitoringDashboardStatusService.Summarize(IEnumerable<MonitoringEmployeeDashboardItemDto>): MonitoringDashboardSummaryDto`

- [ ] Step 1: Write status freshness tests for active, idle, and offline.
- [ ] Step 2: Run status tests and verify they fail because service does not exist.
- [ ] Step 3: Implement dashboard DTOs and status service.
- [ ] Step 4: Run status tests and verify they pass.

### Task 3: Latest Device State Repository Support

**Files:**
- Modify: `src/ONEVO.Application/Features/Monitoring/DeviceState/RepositoryInterfaces/IDeviceStateSnapshotRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/DeviceState/EfDeviceStateSnapshotRepository.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/Dashboard/MonitoringDashboardQueryHandlerTests.cs`

**Interfaces:**
- Produces: `GetLatestForEmployeesAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct): Task<IReadOnlyDictionary<Guid, DeviceStateSnapshot>>`

- [ ] Step 1: Add a handler-level test using a fake device-state repository with latest snapshots.
- [ ] Step 2: Run the handler test and verify it fails because the repository contract is missing.
- [ ] Step 3: Add the repository method to the interface and EF implementation.
- [ ] Step 4: Run the handler test again once Task 4 exists.

### Task 4: Manager Dashboard Query And API

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/Dashboard/Queries/GetMonitoringDashboard/GetMonitoringDashboardQuery.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/Dashboard/Queries/GetMonitoringDashboard/GetMonitoringDashboardQueryHandler.cs`
- Create: `src/ONEVO.Api/Controllers/Tenant/Monitoring/Dashboard/MonitoringDashboardController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/Dashboard/MonitoringDashboardQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `IEmployeeRepository.ListVisibleAsync`
- Consumes: `IActivityDailySummaryRepository.GetAsync`
- Consumes: `IDeviceStateSnapshotRepository.GetLatestForEmployeesAsync`
- Produces: `GET /api/v1/monitoring/dashboard`

- [ ] Step 1: Write handler tests for empty tenant, visible employee mapping, and status rollup.
- [ ] Step 2: Run handler tests and verify they fail because the query handler does not exist.
- [ ] Step 3: Implement query, handler, and controller.
- [ ] Step 4: Run handler tests and verify they pass.

### Task 5: Dashboard Alert Evaluation

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/Dashboard/Services/MonitoringAlertEvaluator.cs`
- Modify: `src/ONEVO.Application/Features/Monitoring/Dashboard/DTOs/MonitoringDashboardDto.cs`
- Modify: `src/ONEVO.Application/Features/Monitoring/Dashboard/Queries/GetMonitoringDashboard/GetMonitoringDashboardQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/Dashboard/MonitoringAlertEvaluatorTests.cs`

**Interfaces:**
- Produces: `MonitoringAlertEvaluator.Evaluate(ActivityDailySummaryDto? summary, IReadOnlyList<WorkSessionReportDto> sessions): IReadOnlyList<MonitoringDashboardAlertDto>`
- Produces alert codes `late_login`, `early_logout`, `long_idle`, `low_activity_score`, and `low_data_coverage`

- [ ] Step 1: Write alert tests for every alert code.
- [ ] Step 2: Run alert tests and verify they fail because evaluator does not exist.
- [ ] Step 3: Implement evaluator and wire it into dashboard employee items.
- [ ] Step 4: Run alert and dashboard handler tests and verify they pass.

### Task 6: Focused Verification

**Files:**
- No production file changes.

**Interfaces:**
- Consumes: all tests added in Tasks 1-5.

- [ ] Step 1: Run `dotnet test .\tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --filter Monitoring`
- [ ] Step 2: Fix failures caused by this implementation.
- [ ] Step 3: Re-run the same command and record the result.

