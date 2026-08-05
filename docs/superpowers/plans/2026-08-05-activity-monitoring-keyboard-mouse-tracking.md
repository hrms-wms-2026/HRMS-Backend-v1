# Activity Monitoring — Keyboard & Mouse Tracking
**Feature:** Monitoring → ActivityMonitoring  
**Date:** 2026-08-05  
**Auth Schemes:** TrayDevicePolicy (ingest), TenantPolicy (query)  
**Status:** Planning

---

## 1. Overview

The Tray App (Windows agent) periodically captures keyboard event counts and mouse event counts — **never keystroke content** — and sends them to the backend. The backend:

1. Lands raw payloads in `activity_raw_buffer` (append-only)
2. Normalizes into `activity_snapshots` (per capture interval)
3. A background job aggregates snapshots → `activity_daily_summary` nightly

HR and managers query daily summaries and snapshots through tenant APIs.

**Privacy rule:** Backend stores count only. Never log, store, or expose actual keystrokes or mouse positions.

---

## 2. Tables Involved (from phase1-table-inventory.md)

| Table | Purpose |
|-------|---------|
| `monitoring_feature_toggles` | Tenant-level ON/OFF for `activity_monitoring` |
| `monitoring_policy_overrides` | Role/position/dept scope overrides (nullable = inherit) |
| `employee_monitoring_overrides` | Per-employee override (nullable = inherit) |
| `activity_raw_buffer` | Append-only landing zone for raw Tray App payloads |
| `activity_snapshots` | Normalized keyboard/mouse counts per capture interval |
| `activity_daily_summary` | Pre-aggregated per-employee daily rollup |

**Toggle resolution chain (highest → lowest priority):**  
`employee_monitoring_overrides.activity_monitoring`  
→ `monitoring_policy_overrides.activity_monitoring` (role/position/dept)  
→ `monitoring_feature_toggles.activity_monitoring`  

If resolved value = false → reject snapshot ingest with 403.

---

## 3. Domain Entities

### 3.1 `ActivitySnapshot`
**Path:** `src/ONEVO.Domain/Features/Monitoring/ActivityMonitoring/Entities/ActivitySnapshot.cs`

```
Properties:
  Id                   : Guid (PK)
  TenantId             : Guid (FK -> tenants)
  EmployeeId           : Guid (FK -> employees)
  AgentDeviceId        : Guid (FK -> registered_agents)
  CapturedAt           : DateTimeOffset  ← agent capture time
  KeyboardEventsCount  : int             ← count only, never content
  MouseEventsCount     : int
  ActiveSeconds        : int
  IdleSeconds          : int
  IntensityScore       : decimal(5,2)    ← 0-100 computed by agent
  ForegroundProcessName: string?         ← e.g. "code.exe"
  CreatedAt            : DateTimeOffset

Implements: ITenantOwnedEntity
```

### 3.2 `ActivityRawBuffer`
**Path:** `src/ONEVO.Domain/Features/Monitoring/ActivityMonitoring/Entities/ActivityRawBuffer.cs`

```
Properties:
  Id            : Guid
  TenantId      : Guid
  AgentDeviceId : Guid (FK -> registered_agents)
  ReceivedAt    : DateTimeOffset  ← server receive time
  PayloadJson   : string          ← JSONB raw payload
```

### 3.3 `ActivityDailySummary`
**Path:** `src/ONEVO.Domain/Features/Monitoring/ActivityMonitoring/Entities/ActivityDailySummary.cs`

```
Properties:
  Id                       : Guid
  TenantId                 : Guid
  EmployeeId               : Guid
  Date                     : DateOnly
  TotalActiveMinutes       : int
  TotalIdleMinutes         : int
  TotalMeetingMinutes      : int
  ActivePercentage         : decimal(5,2)
  ProductiveAppMinutes     : int
  PersonalAppMinutes       : int
  UnknownAppMinutes        : int
  FocusMinutes             : int
  ActivityScore            : decimal(5,2)   ← 0-100
  DataCoveragePercentage   : decimal(5,2)
  TopAppsJson              : string         ← JSONB top 5 apps
  IntensityAvg             : decimal(5,2)
  KeyboardTotal            : int
  MouseTotal               : int
  DocumentTimeMinutes      : int
  DeepFocusSessionsCount   : int
  DataSource               : string         ← "agent_windows"

Implements: ITenantOwnedEntity
Unique constraint: (tenant_id, employee_id, date)
```

### 3.4 `MonitoringFeatureToggles`
**Path:** `src/ONEVO.Domain/Features/Monitoring/Settings/Entities/MonitoringFeatureToggles.cs`

```
Properties:
  Id                      : Guid
  TenantId                : Guid (UNIQUE)
  ActivityMonitoring      : bool
  ApplicationTracking     : bool
  DocumentTracking        : bool
  CommunicationTracking   : bool
  ScreenshotCapture       : bool
  AutoScreenshotCapture   : bool
  MeetingDetection        : bool
  DeviceTracking          : bool
  WorkLocationVerification: bool
  IdentityVerification    : bool
  Biometric               : bool
  CreatedAt               : DateTimeOffset
  UpdatedAt               : DateTimeOffset

Implements: ITenantOwnedEntity
```

### 3.5 `EmployeeMonitoringOverride`
**Path:** `src/ONEVO.Domain/Features/Monitoring/Settings/Entities/EmployeeMonitoringOverride.cs`

```
Properties:
  Id                      : Guid
  TenantId                : Guid
  EmployeeId              : Guid (UNIQUE per tenant)
  ActivityMonitoring      : bool?   ← null = inherit
  ApplicationTracking     : bool?
  DocumentTracking        : bool?
  CommunicationTracking   : bool?
  ScreenshotCapture       : bool?
  AutoScreenshotCapture   : bool?
  MeetingDetection        : bool?
  DeviceTracking          : bool?
  WorkLocationVerification: bool?
  IdentityVerification    : bool?
  Biometric               : bool?
  OverrideReason          : string
  SetById                 : Guid
  CreatedAt               : DateTimeOffset
  UpdatedAt               : DateTimeOffset

Implements: ITenantOwnedEntity
```

### 3.6 `MonitoringPolicyOverride`
**Path:** `src/ONEVO.Domain/Features/Monitoring/Settings/Entities/MonitoringPolicyOverride.cs`

```
Properties:
  Id                      : Guid
  TenantId                : Guid
  ScopeType               : string   ← "role" | "position" | "department"
  ScopeId                 : Guid
  ActivityMonitoring      : bool?
  ApplicationTracking     : bool?
  ... (same nullable fields as employee override)
  OverrideReason          : string
  SetById                 : Guid
  CreatedAt               : DateTimeOffset
  UpdatedAt               : DateTimeOffset

Implements: ITenantOwnedEntity
Unique: (tenant_id, scope_type, scope_id)
```

---

## 4. Application Layer

### 4.1 Commands

#### `IngestActivitySnapshotsCommand`
**Path:** `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Commands/IngestActivitySnapshots/`

**Files:**
- `IngestActivitySnapshotsCommand.cs`
- `IngestActivitySnapshotsCommandHandler.cs`
- `IngestActivitySnapshotsCommandValidator.cs`

**Command shape (sent by Tray App):**
```csharp
public record IngestActivitySnapshotsCommand : IRequest<Result>
{
    public List<ActivitySnapshotItem> Snapshots { get; init; }
}

public record ActivitySnapshotItem
{
    public DateTimeOffset CapturedAt       { get; init; }
    public int KeyboardEventsCount         { get; init; }   // count only
    public int MouseEventsCount            { get; init; }
    public int ActiveSeconds               { get; init; }
    public int IdleSeconds                 { get; init; }
    public decimal IntensityScore          { get; init; }
    public string? ForegroundProcessName   { get; init; }
}
```

**Handler logic:**
1. Resolve `EmployeeId` and `AgentDeviceId` from `ITrayCurrentDevice` (already exists)
2. Call `IMonitoringToggleResolver.IsEnabledAsync(tenantId, employeeId, MonitoringCapability.ActivityMonitoring)`
3. If disabled → return `Result.Failure(MonitoringErrors.ActivityMonitoringDisabled)`
4. Validate each snapshot item (see validator)
5. Save raw payload to `activity_raw_buffer` via `IActivityRawBufferRepository`
6. Map + save normalized snapshots to `activity_snapshots` via `IActivitySnapshotRepository`
7. `IUnitOfWork.SaveChangesAsync()`

**Validator rules:**
- Snapshots list: not empty, max 200 items per batch
- CapturedAt: not in future, not older than 24h
- KeyboardEventsCount: 0–100,000
- MouseEventsCount: 0–100,000
- ActiveSeconds + IdleSeconds <= 300 (capture interval max 5 min)
- IntensityScore: 0–100
- ForegroundProcessName: max 100 chars, no path separators

---

### 4.2 Queries

#### `GetActivitySnapshotsQuery`
**Path:** `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Queries/GetActivitySnapshots/`

**Files:**
- `GetActivitySnapshotsQuery.cs`
- `GetActivitySnapshotsQueryHandler.cs`

**Query shape:**
```csharp
public record GetActivitySnapshotsQuery : IRequest<Result<PagedList<ActivitySnapshotDto>>>
{
    public Guid EmployeeId  { get; init; }
    public DateOnly Date    { get; init; }
    public int Page         { get; init; } = 1;
    public int PageSize     { get; init; } = 100;
}
```

**Handler logic:**
1. Check permission: `monitoring:read`
2. Query `activity_snapshots` filtered by `(tenant_id, employee_id, date)`
3. Order by `captured_at` ASC
4. Return paged list of `ActivitySnapshotDto`

**Permission required:** `monitoring:read`

---

#### `GetActivityDailySummaryQuery`
**Path:** `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Queries/GetActivityDailySummary/`

**Files:**
- `GetActivityDailySummaryQuery.cs`
- `GetActivityDailySummaryQueryHandler.cs`

**Query shape:**
```csharp
public record GetActivityDailySummaryQuery : IRequest<Result<ActivityDailySummaryDto?>>
{
    public Guid EmployeeId { get; init; }
    public DateOnly Date   { get; init; }
}
```

**Handler logic:**
1. Check permission: `monitoring:read`
2. Query `activity_daily_summary` for `(tenant_id, employee_id, date)`
3. Return `ActivityDailySummaryDto?` (null if not yet aggregated)

**Permission required:** `monitoring:read`

---

#### `GetActivityDailyRangeQuery` *(optional — for dashboard)*
**Path:** `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Queries/GetActivityDailyRange/`

**Query shape:**
```csharp
public record GetActivityDailyRangeQuery : IRequest<Result<List<ActivityDailySummaryDto>>>
{
    public Guid EmployeeId  { get; init; }
    public DateOnly From    { get; init; }
    public DateOnly To      { get; init; }   // max 31-day window
}
```

---

### 4.3 DTOs

**Path:** `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/DTOs/Responses/`

#### `ActivitySnapshotDto.cs`
```csharp
public record ActivitySnapshotDto
{
    public Guid Id                     { get; init; }
    public DateTimeOffset CapturedAt   { get; init; }
    public int KeyboardEventsCount     { get; init; }
    public int MouseEventsCount        { get; init; }
    public int ActiveSeconds           { get; init; }
    public int IdleSeconds             { get; init; }
    public decimal IntensityScore      { get; init; }
    public string? ForegroundProcess   { get; init; }
}
```

#### `ActivityDailySummaryDto.cs`
```csharp
public record ActivityDailySummaryDto
{
    public Guid EmployeeId             { get; init; }
    public DateOnly Date               { get; init; }
    public int TotalActiveMinutes      { get; init; }
    public int TotalIdleMinutes        { get; init; }
    public int TotalMeetingMinutes     { get; init; }
    public decimal ActivePercentage    { get; init; }
    public decimal ActivityScore       { get; init; }
    public int KeyboardTotal           { get; init; }
    public int MouseTotal              { get; init; }
    public int FocusMinutes            { get; init; }
    public int DeepFocusSessionsCount  { get; init; }
    public decimal IntensityAvg        { get; init; }
    public decimal DataCoveragePercentage { get; init; }
    public List<AppUsageSummary> TopApps  { get; init; } = [];
}

public record AppUsageSummary
{
    public string AppName     { get; init; } = string.Empty;
    public int TotalSeconds   { get; init; }
    public string Category    { get; init; } = string.Empty;
}
```

---

### 4.4 Repository & Service Interfaces

**Path:** `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/RepositoryInterfaces/`

#### `IActivitySnapshotRepository.cs`
```csharp
public interface IActivitySnapshotRepository
{
    Task AddRangeAsync(IEnumerable<ActivitySnapshot> snapshots, CancellationToken ct);
    Task<IReadOnlyList<ActivitySnapshot>> GetByEmployeeDateAsync(Guid tenantId, Guid employeeId, DateOnly date, int page, int pageSize, CancellationToken ct);
    Task<int> GetTotalCountAsync(Guid tenantId, Guid employeeId, DateOnly date, CancellationToken ct);
}
```

#### `IActivityRawBufferRepository.cs`
```csharp
public interface IActivityRawBufferRepository
{
    Task AddAsync(ActivityRawBuffer buffer, CancellationToken ct);
}
```

#### `IActivityDailySummaryRepository.cs`
```csharp
public interface IActivityDailySummaryRepository
{
    Task<ActivityDailySummary?> GetAsync(Guid tenantId, Guid employeeId, DateOnly date, CancellationToken ct);
    Task<IReadOnlyList<ActivityDailySummary>> GetRangeAsync(Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct);
    Task UpsertAsync(ActivityDailySummary summary, CancellationToken ct);
}
```

**Path:** `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/ServiceInterfaces/`

#### `IMonitoringToggleResolver.cs`
```csharp
public enum MonitoringCapability
{
    ActivityMonitoring,
    ApplicationTracking,
    DocumentTracking,
    CommunicationTracking,
    ScreenshotCapture,
    AutoScreenshotCapture,
    MeetingDetection,
    DeviceTracking,
    WorkLocationVerification,
    IdentityVerification,
    Biometric
}

public interface IMonitoringToggleResolver
{
    Task<bool> IsEnabledAsync(Guid tenantId, Guid employeeId, MonitoringCapability capability, CancellationToken ct = default);
}
```

---

### 4.5 Mappers

**Path:** `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Mappers/ActivitySnapshotMapper.cs`

Maps `ActivitySnapshotItem` → `ActivitySnapshot` entity (sets TenantId, EmployeeId, AgentDeviceId from handler context).

---

## 5. Infrastructure Layer

### 5.1 Repository Implementations
**Path:** `src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/ActivityMonitoring/`

- `EfActivitySnapshotRepository.cs`
- `EfActivityRawBufferRepository.cs`
- `EfActivityDailySummaryRepository.cs`

### 5.2 Toggle Resolver Service
**Path:** `src/ONEVO.Infrastructure/Services/Monitoring/ActivityMonitoring/MonitoringToggleResolverService.cs`

**Resolution logic:**
```
1. Load employee_monitoring_overrides for (tenantId, employeeId)
   → if row exists AND capability column is NOT NULL → return that value

2. Load employee's role_id, position_id, department_id
   Load monitoring_policy_overrides matching those scope_ids
   → Apply priority: employee_override > role_scope > position_scope > dept_scope
   → If a non-null match found → return that value

3. Load monitoring_feature_toggles for tenantId
   → Return tenant-level value

4. If no monitoring_feature_toggles row → return false (safe default)
```

**Caching:** Cache result with key:
```
tenant:{tenantId}:monitoring-toggle:employee:{employeeId}:{capability}
TTL: 2 min (toggles can change)
```

Invalidate on: toggle update, override update.

### 5.3 EF Configurations
**Path:** `src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/ActivityMonitoring/`

- `ActivitySnapshotConfiguration.cs`
  - Table: `activity_snapshots`
  - Index: `(tenant_id, employee_id, captured_at DESC)` — for range queries
  - Index: `(tenant_id, employee_id, date(captured_at))` — for daily grouping
  - No FK-level cascade delete (append-only)

- `ActivityRawBufferConfiguration.cs`
  - Table: `activity_raw_buffer`
  - Index: `(agent_device_id, received_at DESC)`

- `ActivityDailySummaryConfiguration.cs`
  - Table: `activity_daily_summary`
  - Unique: `(tenant_id, employee_id, date)`
  - Index: `(tenant_id, employee_id, date DESC)`

**Path:** `src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/Settings/`

- `MonitoringFeatureTogglesConfiguration.cs`
  - Table: `monitoring_feature_toggles`
  - Unique: `(tenant_id)`

- `EmployeeMonitoringOverrideConfiguration.cs`
  - Table: `employee_monitoring_overrides`
  - Unique: `(tenant_id, employee_id)`

- `MonitoringPolicyOverrideConfiguration.cs`
  - Table: `monitoring_policy_overrides`
  - Unique: `(tenant_id, scope_type, scope_id)`

### 5.4 DbContext Additions
**File:** `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`

```csharp
public DbSet<ActivitySnapshot> ActivitySnapshots { get; set; }
public DbSet<ActivityRawBuffer> ActivityRawBuffers { get; set; }
public DbSet<ActivityDailySummary> ActivityDailySummaries { get; set; }
public DbSet<MonitoringFeatureToggles> MonitoringFeatureToggles { get; set; }
public DbSet<EmployeeMonitoringOverride> EmployeeMonitoringOverrides { get; set; }
public DbSet<MonitoringPolicyOverride> MonitoringPolicyOverrides { get; set; }
```

### 5.5 Migration
**Name:** `AddActivityMonitoring`
**Command:** `dotnet ef migrations add AddActivityMonitoring --project ONEVO.Infrastructure --startup-project ONEVO.Api`

Creates:
- `activity_snapshots`
- `activity_raw_buffer`
- `activity_daily_summary`
- `monitoring_feature_toggles`
- `employee_monitoring_overrides`
- `monitoring_policy_overrides`

### 5.6 Background Job — Daily Summary Aggregation
**Path:** `src/ONEVO.Infrastructure/Services/Monitoring/ActivityMonitoring/ActivityDailySummaryJob.cs`

**Schedule:** Daily at 11:00 PM tenant timezone (or UTC 11 PM for Phase 1)

**Logic per tenant + employee:**
1. Query all `activity_snapshots` for the day
2. Compute:
   - `TotalActiveMinutes` = sum of `active_seconds` / 60
   - `TotalIdleMinutes` = sum of `idle_seconds` / 60
   - `KeyboardTotal` = sum of `keyboard_events_count`
   - `MouseTotal` = sum of `mouse_events_count`
   - `ActivityScore` = weighted formula (active % × intensity avg × data coverage)
   - `IntensityAvg` = avg of `intensity_score` where `active_seconds > 0`
   - `DataCoveragePercentage` = (snapshot_covered_minutes / expected_work_minutes) × 100
   - `FocusMinutes` = contiguous active windows ≥ 30 min in same foreground process
   - `DeepFocusSessionsCount` = count of above sessions
3. Upsert `activity_daily_summary` row

**Phase 1 note:** `TotalMeetingMinutes`, `ProductiveAppMinutes`, `PersonalAppMinutes` are populated by Application Tracking job (separate feature). This job sets them to 0 if not yet available.

### 5.7 DI Registration
**File:** `src/ONEVO.Infrastructure/DependencyInjection.cs`

```csharp
services.AddScoped<IActivitySnapshotRepository, EfActivitySnapshotRepository>();
services.AddScoped<IActivityRawBufferRepository, EfActivityRawBufferRepository>();
services.AddScoped<IActivityDailySummaryRepository, EfActivityDailySummaryRepository>();
services.AddScoped<IMonitoringToggleResolver, MonitoringToggleResolverService>();
// Background job registration (IHostedService or Hangfire/Quartz)
services.AddHostedService<ActivityDailySummaryJob>();
```

---

## 6. API Controller

### 6.1 Ingest Controller (Tray App → Backend)
**Path:** `src/ONEVO.Api/Controllers/Tenant/Monitoring/ActivityMonitoring/MonitoringActivityIngestController.cs`

```csharp
[ApiController]
[Route("api/v1/monitoring/activity")]
[Authorize(Policy = "TrayDevicePolicy")]
public class MonitoringActivityIngestController : ControllerBase
{
    // POST api/v1/monitoring/activity/snapshots
    // Body: { snapshots: [...] }
    // Returns: 202 Accepted
    [HttpPost("snapshots")]
    public async Task<IActionResult> IngestSnapshots(
        [FromBody] IngestActivitySnapshotsRequest request,
        ISender sender)
    { ... }
}
```

**Response codes:**
- `202 Accepted` — snapshots accepted and queued
- `400 Bad Request` — validation failure
- `403 Forbidden` — activity monitoring disabled for this employee
- `401 Unauthorized` — invalid/expired tray token

### 6.2 Query Controller (HR/Manager)
**Path:** `src/ONEVO.Api/Controllers/Tenant/Monitoring/ActivityMonitoring/MonitoringActivityController.cs`

```csharp
[ApiController]
[Route("api/v1/monitoring/activity")]
[Authorize(Policy = "TenantPolicy")]
public class MonitoringActivityController : ControllerBase
{
    // GET api/v1/monitoring/activity/snapshots?employeeId=...&date=2026-08-05
    [HttpGet("snapshots")]
    [RequirePermission("monitoring:read")]
    public async Task<IActionResult> GetSnapshots([FromQuery] GetActivitySnapshotsQuery query, ISender sender)
    { ... }

    // GET api/v1/monitoring/activity/daily-summary?employeeId=...&date=2026-08-05
    [HttpGet("daily-summary")]
    [RequirePermission("monitoring:read")]
    public async Task<IActionResult> GetDailySummary([FromQuery] GetActivityDailySummaryQuery query, ISender sender)
    { ... }

    // GET api/v1/monitoring/activity/daily-range?employeeId=...&from=...&to=...
    [HttpGet("daily-range")]
    [RequirePermission("monitoring:read")]
    public async Task<IActionResult> GetDailyRange([FromQuery] GetActivityDailyRangeQuery query, ISender sender)
    { ... }
}
```

---

## 7. Permission Codes

Add to permissions catalog seed:

| Code | Description |
|------|-------------|
| `monitoring:read` | View activity snapshots and daily summaries |
| `monitoring:settings:write` | Update monitoring feature toggles and overrides |

---

## 8. Error Codes

**Path:** `src/ONEVO.Domain/Errors/MonitoringErrors.cs` *(create or extend)*

```csharp
public static class MonitoringErrors
{
    public static readonly Error ActivityMonitoringDisabled =
        Error.Forbidden("monitoring.activity_monitoring_disabled",
            "Activity monitoring is not enabled for this employee.");

    public static readonly Error SnapshotBatchTooLarge =
        Error.Validation("monitoring.snapshot_batch_too_large",
            "Batch cannot exceed 200 snapshots.");

    public static readonly Error SnapshotTooOld =
        Error.Validation("monitoring.snapshot_too_old",
            "Snapshot captured_at cannot be older than 24 hours.");

    public static readonly Error SnapshotFutureTime =
        Error.Validation("monitoring.snapshot_future_time",
            "Snapshot captured_at cannot be in the future.");
}
```

---

## 9. Logging Rules

**Log (with correlation ID + tenant ID):**
- Snapshot batch received (count, device ID, employee ID)
- Toggle disabled rejection
- Daily summary job start/end (tenant count, employee count processed)
- Performance warnings (batch > 50 items, p95 > 400ms)

**Never log:**
- keyboard_events_count value (log only that it was received)
- ForegroundProcessName in production logs (privacy)
- Raw payload from activity_raw_buffer

---

## 10. Tests

### Unit Tests
**Path:** `tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/`

- `IngestActivitySnapshotsCommandHandlerTests.cs`
  - Happy path: valid batch → saved to buffer + snapshots
  - Monitoring disabled → returns 403 error
  - Future timestamp → validation error
  - Batch > 200 → validation error
  - Snapshot > 24h old → validation error

- `IngestActivitySnapshotsCommandValidatorTests.cs`
  - All validator rules

- `MonitoringToggleResolverTests.cs`
  - Employee override wins over policy override
  - Policy override wins over tenant toggle
  - Tenant toggle fallback
  - No toggle row → returns false

- `ActivityDailySummaryJobTests.cs`
  - Correct aggregation of active/idle minutes
  - IntensityAvg calculation (only active windows)
  - FocusMinutes + DeepFocusSessions computation

### Integration Tests
**Path:** `tests/ONEVO.Tests.Integration/Monitoring/ActivityMonitoring/`

- `ActivityIngestIntegrationTests.cs`
  - POST /api/v1/monitoring/activity/snapshots with valid tray token → 202
  - POST with activity_monitoring = false for tenant → 403
  - POST with expired tray token → 401
  - Data lands in both activity_raw_buffer and activity_snapshots
  - Tenant isolation: device from tenant A cannot ingest for tenant B

- `ActivityQueryIntegrationTests.cs`
  - GET snapshots by employee + date → correct filtered results
  - GET without monitoring:read permission → 403
  - GET daily-summary → correct aggregated values
  - Tenant isolation: tenant A cannot query tenant B employee data

---

## 11. File Creation Order (Build Sequence)

```
Step 1 — Domain entities (6 files)
Step 2 — Application interfaces (IActivitySnapshotRepository, IActivityRawBufferRepository, IActivityDailySummaryRepository, IMonitoringToggleResolver)
Step 3 — Application DTOs (ActivitySnapshotDto, ActivityDailySummaryDto)
Step 4 — Command: IngestActivitySnapshots (command + validator + handler)
Step 5 — Queries: GetActivitySnapshots, GetActivityDailySummary, GetActivityDailyRange
Step 6 — Mapper: ActivitySnapshotMapper
Step 7 — EF Configurations (6 files)
Step 8 — DbContext DbSet additions
Step 9 — Infrastructure repositories (3 files)
Step 10 — MonitoringToggleResolverService
Step 11 — ActivityDailySummaryJob
Step 12 — DI registrations
Step 13 — EF Migration: AddActivityMonitoring
Step 14 — API Controllers (ingest + query)
Step 15 — Permission catalog seed
Step 16 — MonitoringErrors
Step 17 — Unit tests
Step 18 — Integration tests
Step 19 — Postman collection update
```

---

## 12. Permissions Catalog Seed

In `src/ONEVO.Infrastructure/Persistence/Seeders/PermissionCatalogSeeder.cs`, add:

```csharp
new Permission { Code = "monitoring:read",            Feature = "Monitoring" },
new Permission { Code = "monitoring:settings:write",  Feature = "Monitoring" },
```

---

## 13. NFR Checklist

- [ ] Ingest endpoint p95 ≤ 400ms for batch of 100 snapshots
- [ ] Query endpoints p95 ≤ 400ms
- [ ] No N+1 on snapshot list queries
- [ ] `EXPLAIN ANALYZE` run on `activity_snapshots` date range query before release
- [ ] Indexes verified: `(tenant_id, employee_id, captured_at DESC)`
- [ ] Append-only tables: no DELETE/UPDATE migrations on snapshots
- [ ] Never log ForegroundProcessName in production
- [ ] Cache invalidation tested for toggle resolver
- [ ] RLS verified: tenant A cannot access tenant B snapshots
- [ ] Rate limit on ingest endpoint: tray device = 60 req/min

---

## 14. What is NOT in this plan (Phase 2 / later)

- Application tracking (app usage per window title)
- Screenshot capture + auto screenshot
- Meeting detection
- Discrepancy engine
- Productivity score composite (needs work management data)
- Idle evidence files
- Alert routing for anomaly detection
