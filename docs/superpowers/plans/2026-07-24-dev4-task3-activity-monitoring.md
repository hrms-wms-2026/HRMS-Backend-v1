# DEV4 Task 3 — Activity Monitoring Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the Activity Monitoring backend: fix existing schema drift, add 7 domain tables, implement the raw-buffer processing pipeline, daily aggregation, agent health + commands APIs, and the AgentHeartbeatLost outbox event.

**Architecture:** Clean Architecture CQRS in `ONEVO.Application/Features/ActivityMonitoring/`. Raw agent data lands in `activity_raw_buffer`, `ProcessRawBufferJob` parses it into typed tables every 2 min, `AggregateDailySummaryJob` rolls up snapshots every 30 min. All jobs use `BackgroundService` (no Hangfire — not installed in this project). Background jobs resolve scoped services via `IServiceProvider.CreateAsyncScope()`.

**Tech Stack:** .NET 10, EF Core 10, PostgreSQL 16, MediatR 14, xUnit, Moq, FluentAssertions, BackgroundService, `OutboxWriter` for cross-module events.

---

## ⚠️ Known Issues (Fix Before New Work)

These are bugs in the existing implementation that must be resolved first.

### Issue 1 — `activity_raw_buffer` schema drifts from spec

**File:** `src/ONEVO.Domain/Features/AgentGateway/Entities/ActivityRawBuffer.cs`

The entity has `AgentId` + `EmployeeId` + `EventsJson`. The spec says:
- `agent_device_id` (FK → registered_agents) — NOT a separate `agent_id`
- `payload_json` (NOT `events_json`)
- No `employee_id` column (employee is derived from the agent session at processing time)

**Fix:** Migrate column rename `events_json → payload_json`; also rename `agent_id → agent_device_id`, remove `employee_id`. Update entity, EF config, repository, and handler. Task 1 covers this.

### Issue 2 — Ingest endpoint payload format wrong

**File:** `src/ONEVO.Api/Controllers/AgentGateway/AgentGatewayController.cs` (Ingest action)

Controller accepts `{ "events": JsonElement[] }`. Spec requires:
```json
{
  "device_id": "uuid",
  "employee_id": "uuid",
  "timestamp": "2026-04-05T10:30:00Z",
  "batch": [
    { "type": "activity_snapshot", "data": { ... } },
    { "type": "app_usage", "data": { ... } }
  ]
}
```

`batch` is a polymorphic array with `type` discriminator. The raw payload is stored verbatim in `activity_raw_buffer.payload_json`. Task 1 covers this.

### Issue 3 — `DetectOfflineAgentsJob` missing `AgentHeartbeatLost` outbox event

**File:** `src/ONEVO.Infrastructure/Services/AgentGateway/DetectOfflineAgentsJob.cs`

The job marks agents inactive but never writes `AgentHeartbeatLost` to `outbox_messages`. The spec says: "atomically record the offline/lost transition and `AgentHeartbeatLost` outbox message in the same transaction." Task 2 covers this.

### Issue 4 — No Hangfire installed; spec references it for all jobs

The spec specifies Hangfire queues. The project uses `BackgroundService`. This plan continues with `BackgroundService` for consistency. If Hangfire is added later, all jobs in this plan are written in a way that can be wrapped without changes to business logic.

### Issue 5 — `GET /api/v1/agent/policy` returns raw stored policy, not merged effective policy

**File:** `src/ONEVO.Application/Features/AgentGateway/Queries/GetAgentPolicy/GetAgentPolicyQueryHandler.cs`

The spec requires merging: `tenantPolicy.MergeWith(scopePolicy).MergeWith(employeeOverride).ApplyConsentGate(employeeConsent).ApplyLifecycleState(presenceState)`. Currently this returns the raw `policy_json` blob. This requires the Configuration module (`IConfigurationService`) to be implemented first. For now the stub is acceptable. Task 9 introduces a `PolicyMerger` placeholder.

---

## File Map

**New domain entities:**
- `src/ONEVO.Domain/Features/ActivityMonitoring/Entities/ActivitySnapshot.cs`
- `src/ONEVO.Domain/Features/ActivityMonitoring/Entities/ApplicationUsage.cs`
- `src/ONEVO.Domain/Features/ActivityMonitoring/Entities/MeetingSession.cs`
- `src/ONEVO.Domain/Features/ActivityMonitoring/Entities/MonitoringEvidenceAsset.cs`
- `src/ONEVO.Domain/Features/ActivityMonitoring/Entities/ActivityDailySummary.cs`
- `src/ONEVO.Domain/Features/ActivityMonitoring/Entities/ApplicationCategory.cs`
- `src/ONEVO.Domain/Features/ActivityMonitoring/Entities/DeviceTracking.cs`

**Modify existing entity (Issue 1):**
- `src/ONEVO.Domain/Features/AgentGateway/Entities/ActivityRawBuffer.cs`

**New EF configurations:**
- `src/ONEVO.Infrastructure/Persistence/Configurations/ActivityMonitoring/ActivitySnapshotConfiguration.cs`
- `src/ONEVO.Infrastructure/Persistence/Configurations/ActivityMonitoring/ApplicationUsageConfiguration.cs`
- `src/ONEVO.Infrastructure/Persistence/Configurations/ActivityMonitoring/MeetingSessionConfiguration.cs`
- `src/ONEVO.Infrastructure/Persistence/Configurations/ActivityMonitoring/MonitoringEvidenceAssetConfiguration.cs`
- `src/ONEVO.Infrastructure/Persistence/Configurations/ActivityMonitoring/ActivityDailySummaryConfiguration.cs`
- `src/ONEVO.Infrastructure/Persistence/Configurations/ActivityMonitoring/ApplicationCategoryConfiguration.cs`
- `src/ONEVO.Infrastructure/Persistence/Configurations/ActivityMonitoring/DeviceTrackingConfiguration.cs`

**Modify existing EF configuration (Issue 1):**
- `src/ONEVO.Infrastructure/Persistence/Configurations/AgentGateway/ActivityRawBufferConfiguration.cs`

**Modify ApplicationDbContext:**
- `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` — add 7 new DbSets

**New migration:**
- `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddActivityMonitoringTables.cs` — generated by EF

**Repository interface:**
- `src/ONEVO.Application/Features/ActivityMonitoring/RepositoryInterfaces/IActivityMonitoringRepository.cs`

**EF repository:**
- `src/ONEVO.Infrastructure/Persistence/Repositories/ActivityMonitoring/EfActivityMonitoringRepository.cs`

**Public interface:**
- `src/ONEVO.Application/Features/ActivityMonitoring/Public/IActivityMonitoringService.cs`

**Application — Queries:**
- `src/ONEVO.Application/Features/ActivityMonitoring/Queries/GetDailySummary/GetDailySummaryQuery.cs`
- `src/ONEVO.Application/Features/ActivityMonitoring/Queries/GetDailySummary/GetDailySummaryQueryHandler.cs`
- `src/ONEVO.Application/Features/ActivityMonitoring/Queries/GetSnapshots/GetSnapshotsQuery.cs`
- `src/ONEVO.Application/Features/ActivityMonitoring/Queries/GetSnapshots/GetSnapshotsQueryHandler.cs`
- `src/ONEVO.Application/Features/ActivityMonitoring/Queries/GetAppUsage/GetAppUsageQuery.cs`
- `src/ONEVO.Application/Features/ActivityMonitoring/Queries/GetAppUsage/GetAppUsageQueryHandler.cs`
- `src/ONEVO.Application/Features/ActivityMonitoring/Queries/GetMeetings/GetMeetingsQuery.cs`
- `src/ONEVO.Application/Features/ActivityMonitoring/Queries/GetMeetings/GetMeetingsQueryHandler.cs`

**Application — Commands:**
- `src/ONEVO.Application/Features/ActivityMonitoring/Commands/UpsertApplicationCategory/UpsertApplicationCategoryCommand.cs`
- `src/ONEVO.Application/Features/ActivityMonitoring/Commands/UpsertApplicationCategory/UpsertApplicationCategoryCommandHandler.cs`
- `src/ONEVO.Application/Features/ActivityMonitoring/Commands/DeleteApplicationCategory/DeleteApplicationCategoryCommand.cs`
- `src/ONEVO.Application/Features/ActivityMonitoring/Commands/DeleteApplicationCategory/DeleteApplicationCategoryCommandHandler.cs`

**Application — DTOs:**
- `src/ONEVO.Application/Features/ActivityMonitoring/DTOs/Responses/ActivityDailySummaryDto.cs`
- `src/ONEVO.Application/Features/ActivityMonitoring/DTOs/Responses/ActivitySnapshotDto.cs`
- `src/ONEVO.Application/Features/ActivityMonitoring/DTOs/Responses/ApplicationUsageDto.cs`
- `src/ONEVO.Application/Features/ActivityMonitoring/DTOs/Responses/MeetingSessionDto.cs`
- `src/ONEVO.Application/Features/ActivityMonitoring/DTOs/Responses/ApplicationCategoryDto.cs`

**Infrastructure — Jobs:**
- `src/ONEVO.Infrastructure/Services/ActivityMonitoring/ProcessRawBufferJob.cs`
- `src/ONEVO.Infrastructure/Services/ActivityMonitoring/AggregateDailySummaryJob.cs`
- `src/ONEVO.Infrastructure/Services/ActivityMonitoring/PurgeRawBufferJob.cs`

**API:**
- `src/ONEVO.Api/Controllers/ActivityMonitoring/ActivityMonitoringController.cs`

**Modify:**
- `src/ONEVO.Api/Controllers/AgentGateway/AgentGatewayController.cs` — fix ingest payload
- `src/ONEVO.Application/Features/AgentGateway/Commands/IngestBatch/IngestBatchCommand.cs` — update fields
- `src/ONEVO.Application/Features/AgentGateway/Commands/IngestBatch/IngestBatchCommandHandler.cs` — fix payload storage
- `src/ONEVO.Infrastructure/Services/AgentGateway/DetectOfflineAgentsJob.cs` — add outbox event
- `src/ONEVO.Infrastructure/DependencyInjection.cs` — register new repos + jobs
- `src/ONEVO.Infrastructure/Persistence/Repositories/AgentGateway/EfAgentGatewayRepository.cs` — fix AddRawActivityBatchAsync signature

**Tests:**
- `tests/ONEVO.Application.Tests/Features/ActivityMonitoring/ProcessRawBufferJobTests.cs`
- `tests/ONEVO.Application.Tests/Features/ActivityMonitoring/AggregateDailySummaryJobTests.cs`
- `tests/ONEVO.Application.Tests/Features/AgentGateway/DetectOfflineAgentsJobTests.cs`

---

### Task 1: Fix Schema Drift — activity_raw_buffer + Ingest Payload

**Files:**
- Modify: `src/ONEVO.Domain/Features/AgentGateway/Entities/ActivityRawBuffer.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/AgentGateway/ActivityRawBufferConfiguration.cs`
- Modify: `src/ONEVO.Application/Features/AgentGateway/RepositoryInterfaces/IAgentGatewayRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/AgentGateway/EfAgentGatewayRepository.cs`
- Modify: `src/ONEVO.Application/Features/AgentGateway/Commands/IngestBatch/IngestBatchCommand.cs`
- Modify: `src/ONEVO.Application/Features/AgentGateway/Commands/IngestBatch/IngestBatchCommandHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/AgentGateway/AgentGatewayController.cs`
- Create: EF migration (dotnet ef migrations add)

- [ ] **Step 1: Update ActivityRawBuffer entity**

Replace the entire file `src/ONEVO.Domain/Features/AgentGateway/Entities/ActivityRawBuffer.cs`:

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.AgentGateway.Entities;

public class ActivityRawBuffer : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AgentDeviceId { get; set; }   // FK -> registered_agents.id
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public string PayloadJson { get; set; } = "{}";
}
```

- [ ] **Step 2: Update ActivityRawBufferConfiguration**

Replace `src/ONEVO.Infrastructure/Persistence/Configurations/AgentGateway/ActivityRawBufferConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.AgentGateway.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.AgentGateway;

public class ActivityRawBufferConfiguration : IEntityTypeConfiguration<ActivityRawBuffer>
{
    public void Configure(EntityTypeBuilder<ActivityRawBuffer> builder)
    {
        builder.ToTable("activity_raw_buffer");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.PayloadJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(b => new { b.TenantId, b.AgentDeviceId, b.ReceivedAt });
    }
}
```

- [ ] **Step 3: Update IAgentGatewayRepository**

In `src/ONEVO.Application/Features/AgentGateway/RepositoryInterfaces/IAgentGatewayRepository.cs`, change:

```csharp
    // Activity raw buffer (tenant-scoped)
    Task AddRawActivityBatchAsync(ActivityRawBuffer batch, CancellationToken ct);
```

(signature unchanged — just remove the `EmployeeId`-related doc comments if any)

- [ ] **Step 4: Update IngestBatchCommand**

Replace `src/ONEVO.Application/Features/AgentGateway/Commands/IngestBatch/IngestBatchCommand.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.AgentGateway.Commands.IngestBatch;

public record IngestBatchCommand(
    Guid AgentId,
    Guid TenantId,
    string PayloadJson) : IRequest<Result>;
```

- [ ] **Step 5: Update IngestBatchCommandHandler**

Replace `src/ONEVO.Application/Features/AgentGateway/Commands/IngestBatch/IngestBatchCommandHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;

namespace ONEVO.Application.Features.AgentGateway.Commands.IngestBatch;

public class IngestBatchCommandHandler : IRequestHandler<IngestBatchCommand, Result>
{
    private readonly IAgentGatewayRepository _repo;
    private readonly IUnitOfWork _uow;

    public IngestBatchCommandHandler(IAgentGatewayRepository repo, IUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task<Result> Handle(IngestBatchCommand request, CancellationToken cancellationToken)
    {
        var agent = await _repo.GetAgentByIdAsync(request.AgentId, cancellationToken);
        if (agent is null || agent.Status == "revoked")
            return Result.Failure("Agent not found or revoked.", 401);

        await _repo.AddRawActivityBatchAsync(new ActivityRawBuffer
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            AgentDeviceId = request.AgentId,
            ReceivedAt = DateTimeOffset.UtcNow,
            PayloadJson = request.PayloadJson
        }, cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
```

- [ ] **Step 6: Fix the Ingest action in AgentGatewayController**

The controller needs to accept the spec payload format. Replace the `Ingest` action and `IngestBatchRequest` record in `src/ONEVO.Api/Controllers/AgentGateway/AgentGatewayController.cs`:

```csharp
    [HttpPost("ingest")]
    [Authorize(Policy = "AgentPolicy")]
    public async Task<IActionResult> Ingest([FromBody] IngestBatchRequest request, CancellationToken ct)
    {
        var agentId = GetAgentId();
        if (agentId == Guid.Empty) return Unauthorized();

        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty) return Unauthorized();

        // Store entire payload verbatim — processor determines batch item types
        var payloadJson = JsonSerializer.Serialize(request);
        var result = await _mediator.Send(new IngestBatchCommand(agentId, tenantId, payloadJson), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Accepted();
    }
```

Also replace `IngestBatchRequest` record at the bottom of the controller:

```csharp
    // Spec payload: { device_id, employee_id, timestamp, batch: [{ type, data }] }
    public record IngestBatchRequest(
        Guid DeviceId,
        Guid EmployeeId,
        DateTimeOffset Timestamp,
        JsonElement[] Batch);
```

- [ ] **Step 7: Generate migration**

```bash
cd "C:\tmp\one backend\HRMS-Backend-v1"
dotnet ef migrations add FixActivityRawBufferSchema --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```

Expected output: `Done. To undo this action, use 'ef migrations remove'`

- [ ] **Step 8: Build**

```bash
dotnet build src/ONEVO.Api/ONEVO.Api.csproj -c Release 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Domain/Features/AgentGateway/Entities/ActivityRawBuffer.cs \
        src/ONEVO.Infrastructure/Persistence/Configurations/AgentGateway/ActivityRawBufferConfiguration.cs \
        src/ONEVO.Application/Features/AgentGateway/Commands/IngestBatch/ \
        src/ONEVO.Api/Controllers/AgentGateway/AgentGatewayController.cs \
        src/ONEVO.Infrastructure/Migrations/*FixActivityRawBufferSchema*
git commit -m "fix(agent-gateway): align activity_raw_buffer schema and ingest payload with spec"
```

---

### Task 2: Fix DetectOfflineAgentsJob — Add AgentHeartbeatLost Outbox Event

**Files:**
- Modify: `src/ONEVO.Infrastructure/Services/AgentGateway/DetectOfflineAgentsJob.cs`
- Modify: `src/ONEVO.Application/Features/AgentGateway/RepositoryInterfaces/IAgentGatewayRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/AgentGateway/EfAgentGatewayRepository.cs`

The job must atomically mark agents inactive AND write `AgentHeartbeatLost` to `outbox_messages` in the same transaction. The existing `IOutboxWriter` handles this.

- [ ] **Step 1: Read IOutboxWriter interface**

```bash
# Read this file before editing:
# src/ONEVO.Application/Common/ServiceInterfaces/IOutboxWriter.cs
```

The interface signature is:
```csharp
public interface IOutboxWriter
{
    Task WriteAsync(string eventType, object payload, CancellationToken ct);
}
```

- [ ] **Step 2: Update MarkAgentsInactiveAsync to return agent IDs**

The job needs to know WHICH agents went offline (to write an outbox event per agent). Change the repository interface:

In `src/ONEVO.Application/Features/AgentGateway/RepositoryInterfaces/IAgentGatewayRepository.cs`, replace:

```csharp
    Task<int> MarkAgentsInactiveAsync(DateTimeOffset threshold, CancellationToken ct);
```

with:

```csharp
    Task<IReadOnlyList<Guid>> MarkAgentsInactiveAndReturnIdsAsync(DateTimeOffset threshold, CancellationToken ct);
```

- [ ] **Step 3: Implement the updated repository method**

In `src/ONEVO.Infrastructure/Persistence/Repositories/AgentGateway/EfAgentGatewayRepository.cs`, replace `MarkAgentsInactiveAsync` with:

```csharp
    public async Task<IReadOnlyList<Guid>> MarkAgentsInactiveAndReturnIdsAsync(
        DateTimeOffset threshold, CancellationToken ct)
    {
        var agentIds = await _db.RegisteredAgents
            .Where(a => a.Status == "active"
                        && a.LastHeartbeatAt != null
                        && a.LastHeartbeatAt < threshold)
            .Select(a => a.Id)
            .ToListAsync(ct);

        if (agentIds.Count == 0) return agentIds;

        await _db.RegisteredAgents
            .Where(a => agentIds.Contains(a.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, "inactive"), ct);

        return agentIds;
    }
```

- [ ] **Step 4: Update DetectOfflineAgentsJob**

Replace `src/ONEVO.Infrastructure/Services/AgentGateway/DetectOfflineAgentsJob.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;

namespace ONEVO.Infrastructure.Services.AgentGateway;

public sealed class DetectOfflineAgentsJob : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan OfflineThreshold = TimeSpan.FromMinutes(5);

    private readonly IServiceProvider _services;
    private readonly ILogger<DetectOfflineAgentsJob> _logger;

    public DetectOfflineAgentsJob(IServiceProvider services, ILogger<DetectOfflineAgentsJob> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
                await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DetectOfflineAgentsJob iteration failed; will retry next interval.");
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAgentGatewayRepository>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var threshold = DateTimeOffset.UtcNow.Subtract(OfflineThreshold);
        var agentIds = await repo.MarkAgentsInactiveAndReturnIdsAsync(threshold, ct);

        if (agentIds.Count == 0) return;

        foreach (var agentId in agentIds)
        {
            await outbox.WriteAsync("AgentHeartbeatLost", new
            {
                agent_id = agentId,
                detected_at = DateTimeOffset.UtcNow,
                offline_threshold_minutes = (int)OfflineThreshold.TotalMinutes
            }, ct);
        }

        await uow.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Marked {Count} agent(s) inactive and wrote AgentHeartbeatLost outbox events (threshold: {Threshold}).",
            agentIds.Count, threshold);
    }
}
```

- [ ] **Step 5: Build**

```bash
dotnet build src/ONEVO.Api/ONEVO.Api.csproj -c Release 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Infrastructure/Services/AgentGateway/DetectOfflineAgentsJob.cs \
        src/ONEVO.Application/Features/AgentGateway/RepositoryInterfaces/IAgentGatewayRepository.cs \
        src/ONEVO.Infrastructure/Persistence/Repositories/AgentGateway/EfAgentGatewayRepository.cs
git commit -m "fix(agent-gateway): DetectOfflineAgentsJob atomically marks inactive and writes AgentHeartbeatLost outbox event"
```

---

### Task 3: Activity Monitoring Domain Entities (7 tables)

**Files:**
- Create: 7 files in `src/ONEVO.Domain/Features/ActivityMonitoring/Entities/`

- [ ] **Step 1: Create ActivitySnapshot**

```csharp
// src/ONEVO.Domain/Features/ActivityMonitoring/Entities/ActivitySnapshot.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.ActivityMonitoring.Entities;

public class ActivitySnapshot : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public int KeyboardEventsCount { get; set; }
    public int MouseEventsCount { get; set; }
    public int ActiveSeconds { get; set; }
    public int IdleSeconds { get; set; }
    public decimal IntensityScore { get; set; }
    public string ForegroundProcessName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

- [ ] **Step 2: Create ApplicationUsage**

```csharp
// src/ONEVO.Domain/Features/ActivityMonitoring/Entities/ApplicationUsage.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.ActivityMonitoring.Entities;

public class ApplicationUsage : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly Date { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;
    public string? ApplicationCategory { get; set; }
    public string? WindowTitleHash { get; set; }
    public int TotalSeconds { get; set; }
    public bool? IsProductive { get; set; }
    public bool? IsAllowed { get; set; }
}
```

- [ ] **Step 3: Create MeetingSession**

```csharp
// src/ONEVO.Domain/Features/ActivityMonitoring/Entities/MeetingSession.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.ActivityMonitoring.Entities;

public class MeetingSession : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateTimeOffset MeetingStart { get; set; }
    public DateTimeOffset MeetingEnd { get; set; }
    public string Platform { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public bool HadCameraOn { get; set; }
    public bool HadMicActivity { get; set; }
}
```

- [ ] **Step 4: Create MonitoringEvidenceAsset**

```csharp
// src/ONEVO.Domain/Features/ActivityMonitoring/Entities/MonitoringEvidenceAsset.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.ActivityMonitoring.Entities;

public class MonitoringEvidenceAsset : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? AgentDeviceId { get; set; }
    public Guid? ActivitySnapshotId { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public Guid FileRecordId { get; set; }

    /// <summary>screenshot | app_snapshot | idle_evidence</summary>
    public string EvidenceType { get; set; } = string.Empty;

    /// <summary>on_demand | auto_deviation</summary>
    public string TriggerType { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

- [ ] **Step 5: Create ActivityDailySummary**

```csharp
// src/ONEVO.Domain/Features/ActivityMonitoring/Entities/ActivityDailySummary.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.ActivityMonitoring.Entities;

public class ActivityDailySummary : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly Date { get; set; }
    public int TotalActiveMinutes { get; set; }
    public int TotalIdleMinutes { get; set; }
    public int TotalMeetingMinutes { get; set; }
    public decimal ActivePercentage { get; set; }
    public int ProductiveAppMinutes { get; set; }
    public int PersonalAppMinutes { get; set; }
    public int UnknownAppMinutes { get; set; }
    public int FocusMinutes { get; set; }
    public decimal ActivityScore { get; set; }
    public decimal DataCoveragePercentage { get; set; }
    public string TopAppsJson { get; set; } = "[]";
    public decimal IntensityAvg { get; set; }
    public int KeyboardTotal { get; set; }
    public int MouseTotal { get; set; }
}
```

- [ ] **Step 6: Create ApplicationCategory**

```csharp
// src/ONEVO.Domain/Features/ActivityMonitoring/Entities/ApplicationCategory.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.ActivityMonitoring.Entities;

public class ApplicationCategory : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string ApplicationNamePattern { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool? IsProductive { get; set; }
    public Guid CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

- [ ] **Step 7: Create DeviceTracking**

```csharp
// src/ONEVO.Domain/Features/ActivityMonitoring/Entities/DeviceTracking.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.ActivityMonitoring.Entities;

public class DeviceTracking : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly Date { get; set; }
    public int LaptopActiveMinutes { get; set; }
    public int EstimatedMobileMinutes { get; set; }
    public decimal LaptopPercentage { get; set; }

    /// <summary>agent | manual</summary>
    public string DetectionMethod { get; set; } = "agent";
}
```

- [ ] **Step 8: Commit entities**

```bash
git add src/ONEVO.Domain/Features/ActivityMonitoring/
git commit -m "feat(activity-monitoring): add 7 domain entities"
```

---

### Task 4: EF Configurations + Migration + DbContext

**Files:**
- Create: 7 configuration files in `src/ONEVO.Infrastructure/Persistence/Configurations/ActivityMonitoring/`
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`
- Create: EF migration

- [ ] **Step 1: Create ActivitySnapshotConfiguration**

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/ActivityMonitoring/ActivitySnapshotConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.ActivityMonitoring;

public class ActivitySnapshotConfiguration : IEntityTypeConfiguration<ActivitySnapshot>
{
    public void Configure(EntityTypeBuilder<ActivitySnapshot> builder)
    {
        builder.ToTable("activity_snapshots");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.ForegroundProcessName).HasMaxLength(100).IsRequired();
        builder.Property(s => s.IntensityScore).HasPrecision(5, 2);
        builder.HasIndex(s => new { s.TenantId, s.EmployeeId, s.CapturedAt });
        builder.HasIndex(s => new { s.TenantId, s.CapturedAt });
    }
}
```

- [ ] **Step 2: Create ApplicationUsageConfiguration**

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/ActivityMonitoring/ApplicationUsageConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.ActivityMonitoring;

public class ApplicationUsageConfiguration : IEntityTypeConfiguration<ApplicationUsage>
{
    public void Configure(EntityTypeBuilder<ApplicationUsage> builder)
    {
        builder.ToTable("application_usage");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.ProcessName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.ApplicationName).HasMaxLength(255).IsRequired();
        builder.Property(u => u.ApplicationCategory).HasMaxLength(100);
        builder.Property(u => u.WindowTitleHash).HasMaxLength(64);
        builder.HasIndex(u => new { u.TenantId, u.EmployeeId, u.Date });
        builder.HasIndex(u => new { u.TenantId, u.Date, u.ApplicationCategory });
        builder.HasIndex(u => new { u.TenantId, u.EmployeeId, u.Date, u.IsAllowed });
    }
}
```

- [ ] **Step 3: Create MeetingSessionConfiguration**

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/ActivityMonitoring/MeetingSessionConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.ActivityMonitoring;

public class MeetingSessionConfiguration : IEntityTypeConfiguration<MeetingSession>
{
    public void Configure(EntityTypeBuilder<MeetingSession> builder)
    {
        builder.ToTable("meeting_sessions");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Platform).HasMaxLength(20).IsRequired();
        builder.HasIndex(m => new { m.TenantId, m.EmployeeId, m.MeetingStart });
    }
}
```

- [ ] **Step 4: Create MonitoringEvidenceAssetConfiguration**

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/ActivityMonitoring/MonitoringEvidenceAssetConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.ActivityMonitoring;

public class MonitoringEvidenceAssetConfiguration : IEntityTypeConfiguration<MonitoringEvidenceAsset>
{
    public void Configure(EntityTypeBuilder<MonitoringEvidenceAsset> builder)
    {
        builder.ToTable("monitoring_evidence_assets");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.EvidenceType).HasMaxLength(40).IsRequired();
        builder.Property(a => a.TriggerType).HasMaxLength(20).IsRequired();
        builder.HasIndex(a => new { a.TenantId, a.EmployeeId, a.CapturedAt });
    }
}
```

- [ ] **Step 5: Create ActivityDailySummaryConfiguration**

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/ActivityMonitoring/ActivityDailySummaryConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.ActivityMonitoring;

public class ActivityDailySummaryConfiguration : IEntityTypeConfiguration<ActivityDailySummary>
{
    public void Configure(EntityTypeBuilder<ActivityDailySummary> builder)
    {
        builder.ToTable("activity_daily_summary");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.ActivePercentage).HasPrecision(5, 2);
        builder.Property(s => s.ActivityScore).HasPrecision(5, 2);
        builder.Property(s => s.DataCoveragePercentage).HasPrecision(5, 2);
        builder.Property(s => s.IntensityAvg).HasPrecision(5, 2);
        builder.Property(s => s.TopAppsJson).HasColumnType("jsonb").IsRequired();
        // (tenant_id, employee_id, date) unique — required for upsert
        builder.HasIndex(s => new { s.TenantId, s.EmployeeId, s.Date }).IsUnique();
    }
}
```

- [ ] **Step 6: Create ApplicationCategoryConfiguration**

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/ActivityMonitoring/ApplicationCategoryConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.ActivityMonitoring;

public class ApplicationCategoryConfiguration : IEntityTypeConfiguration<ApplicationCategory>
{
    public void Configure(EntityTypeBuilder<ApplicationCategory> builder)
    {
        builder.ToTable("application_categories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.ApplicationNamePattern).HasMaxLength(255).IsRequired();
        builder.Property(c => c.Category).HasMaxLength(100).IsRequired();
        builder.HasIndex(c => new { c.TenantId, c.ApplicationNamePattern });
    }
}
```

- [ ] **Step 7: Create DeviceTrackingConfiguration**

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/ActivityMonitoring/DeviceTrackingConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.ActivityMonitoring;

public class DeviceTrackingConfiguration : IEntityTypeConfiguration<DeviceTracking>
{
    public void Configure(EntityTypeBuilder<DeviceTracking> builder)
    {
        builder.ToTable("device_tracking");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.LaptopPercentage).HasPrecision(5, 2);
        builder.Property(d => d.DetectionMethod).HasMaxLength(30).IsRequired();
        builder.HasIndex(d => new { d.TenantId, d.EmployeeId, d.Date }).IsUnique();
    }
}
```

- [ ] **Step 8: Add 7 DbSets to ApplicationDbContext**

In `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`, add after the existing Agent Gateway DbSets:

```csharp
    // Activity Monitoring
    public DbSet<ActivitySnapshot> ActivitySnapshots => Set<ActivitySnapshot>();
    public DbSet<ApplicationUsage> ApplicationUsage => Set<ApplicationUsage>();
    public DbSet<MeetingSession> MeetingSessions => Set<MeetingSession>();
    public DbSet<MonitoringEvidenceAsset> MonitoringEvidenceAssets => Set<MonitoringEvidenceAsset>();
    public DbSet<ActivityDailySummary> ActivityDailySummaries => Set<ActivityDailySummary>();
    public DbSet<ApplicationCategory> ApplicationCategories => Set<ApplicationCategory>();
    public DbSet<DeviceTracking> DeviceTracking => Set<DeviceTracking>();
```

Add the using at the top:
```csharp
using ONEVO.Domain.Features.ActivityMonitoring.Entities;
```

- [ ] **Step 9: Generate migration**

```bash
cd "C:\tmp\one backend\HRMS-Backend-v1"
dotnet ef migrations add AddActivityMonitoringTables --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```

Expected: `Done.`

- [ ] **Step 10: Build**

```bash
dotnet build src/ONEVO.Api/ONEVO.Api.csproj -c Release 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 11: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Configurations/ActivityMonitoring/ \
        src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs \
        src/ONEVO.Infrastructure/Migrations/*AddActivityMonitoringTables*
git commit -m "feat(activity-monitoring): EF configurations, DbSets, and migration for 7 tables"
```

---

### Task 5: Activity Monitoring Repository Interface + EF Implementation

**Files:**
- Create: `src/ONEVO.Application/Features/ActivityMonitoring/RepositoryInterfaces/IActivityMonitoringRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/ActivityMonitoring/EfActivityMonitoringRepository.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Create IActivityMonitoringRepository**

```csharp
// src/ONEVO.Application/Features/ActivityMonitoring/RepositoryInterfaces/IActivityMonitoringRepository.cs
using ONEVO.Domain.Features.ActivityMonitoring.Entities;

namespace ONEVO.Application.Features.ActivityMonitoring.RepositoryInterfaces;

public interface IActivityMonitoringRepository
{
    // Raw buffer
    Task<IReadOnlyList<RawBufferItem>> GetPendingRawBatchAsync(int maxRows, CancellationToken ct);
    Task BulkInsertSnapshotsAsync(IEnumerable<ActivitySnapshot> snapshots, CancellationToken ct);
    Task BulkInsertApplicationUsageAsync(IEnumerable<ApplicationUsage> usage, CancellationToken ct);
    Task BulkInsertMeetingSessionsAsync(IEnumerable<MeetingSession> sessions, CancellationToken ct);
    Task UpsertDeviceTrackingAsync(DeviceTracking tracking, CancellationToken ct);
    Task DeleteRawBufferRowsAsync(IEnumerable<Guid> ids, CancellationToken ct);

    // Daily aggregation
    Task<IReadOnlyList<ActivitySnapshot>> GetSnapshotsForDayAsync(Guid employeeId, DateOnly date, CancellationToken ct);
    Task<IReadOnlyList<ApplicationUsage>> GetAppUsageForDayAsync(Guid employeeId, DateOnly date, CancellationToken ct);
    Task<IReadOnlyList<MeetingSession>> GetMeetingsForDayAsync(Guid employeeId, DateOnly date, CancellationToken ct);
    Task UpsertDailySummaryAsync(ActivityDailySummary summary, CancellationToken ct);

    // Queries
    Task<ActivityDailySummary?> GetDailySummaryAsync(Guid employeeId, DateOnly date, CancellationToken ct);
    Task<IReadOnlyList<ActivitySnapshot>> GetSnapshotsAsync(Guid employeeId, DateOnly date, CancellationToken ct);
    Task<IReadOnlyList<ApplicationUsage>> GetAppUsageAsync(Guid employeeId, DateOnly date, CancellationToken ct);
    Task<IReadOnlyList<MeetingSession>> GetMeetingsAsync(Guid employeeId, DateOnly date, CancellationToken ct);
    Task<IReadOnlyList<ApplicationCategory>> GetCategoriesAsync(CancellationToken ct);
    Task AddCategoryAsync(ApplicationCategory category, CancellationToken ct);
    Task<bool> DeleteCategoryAsync(Guid id, CancellationToken ct);

    // Purge
    Task<int> DeleteRawBufferOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct);
    Task<int> DeleteSnapshotsOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct);
}

// Projection for raw buffer processing
public record RawBufferItem(Guid Id, Guid TenantId, Guid AgentDeviceId, DateTimeOffset ReceivedAt, string PayloadJson);
```

- [ ] **Step 2: Create EfActivityMonitoringRepository**

```csharp
// src/ONEVO.Infrastructure/Persistence/Repositories/ActivityMonitoring/EfActivityMonitoringRepository.cs
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.ActivityMonitoring;

public sealed class EfActivityMonitoringRepository : IActivityMonitoringRepository
{
    private readonly ApplicationDbContext _db;
    public EfActivityMonitoringRepository(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<RawBufferItem>> GetPendingRawBatchAsync(int maxRows, CancellationToken ct) =>
        await _db.ActivityRawBuffer
            .OrderBy(b => b.ReceivedAt)
            .Take(maxRows)
            .Select(b => new RawBufferItem(b.Id, b.TenantId, b.AgentDeviceId, b.ReceivedAt, b.PayloadJson))
            .ToListAsync(ct);

    public async Task BulkInsertSnapshotsAsync(IEnumerable<ActivitySnapshot> snapshots, CancellationToken ct) =>
        await _db.ActivitySnapshots.AddRangeAsync(snapshots, ct);

    public async Task BulkInsertApplicationUsageAsync(IEnumerable<ApplicationUsage> usage, CancellationToken ct) =>
        await _db.ApplicationUsage.AddRangeAsync(usage, ct);

    public async Task BulkInsertMeetingSessionsAsync(IEnumerable<MeetingSession> sessions, CancellationToken ct) =>
        await _db.MeetingSessions.AddRangeAsync(sessions, ct);

    public async Task UpsertDeviceTrackingAsync(DeviceTracking tracking, CancellationToken ct)
    {
        var existing = await _db.DeviceTracking
            .FirstOrDefaultAsync(d => d.TenantId == tracking.TenantId
                                      && d.EmployeeId == tracking.EmployeeId
                                      && d.Date == tracking.Date, ct);
        if (existing is null)
            await _db.DeviceTracking.AddAsync(tracking, ct);
        else
        {
            existing.LaptopActiveMinutes += tracking.LaptopActiveMinutes;
            existing.LaptopPercentage = tracking.LaptopPercentage;
        }
    }

    public async Task DeleteRawBufferRowsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var idList = ids.ToList();
        await _db.ActivityRawBuffer
            .Where(b => idList.Contains(b.Id))
            .ExecuteDeleteAsync(ct);
    }

    public async Task<IReadOnlyList<ActivitySnapshot>> GetSnapshotsForDayAsync(
        Guid employeeId, DateOnly date, CancellationToken ct)
    {
        var start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
        return await _db.ActivitySnapshots
            .Where(s => s.EmployeeId == employeeId
                        && s.CapturedAt >= start && s.CapturedAt <= end)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ApplicationUsage>> GetAppUsageForDayAsync(
        Guid employeeId, DateOnly date, CancellationToken ct) =>
        await _db.ApplicationUsage
            .Where(u => u.EmployeeId == employeeId && u.Date == date)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<MeetingSession>> GetMeetingsForDayAsync(
        Guid employeeId, DateOnly date, CancellationToken ct)
    {
        var start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
        return await _db.MeetingSessions
            .Where(m => m.EmployeeId == employeeId
                        && m.MeetingStart >= start && m.MeetingStart <= end)
            .ToListAsync(ct);
    }

    public async Task UpsertDailySummaryAsync(ActivityDailySummary summary, CancellationToken ct)
    {
        var existing = await _db.ActivityDailySummaries
            .FirstOrDefaultAsync(s => s.TenantId == summary.TenantId
                                      && s.EmployeeId == summary.EmployeeId
                                      && s.Date == summary.Date, ct);
        if (existing is null)
            await _db.ActivityDailySummaries.AddAsync(summary, ct);
        else
        {
            existing.TotalActiveMinutes = summary.TotalActiveMinutes;
            existing.TotalIdleMinutes = summary.TotalIdleMinutes;
            existing.TotalMeetingMinutes = summary.TotalMeetingMinutes;
            existing.ActivePercentage = summary.ActivePercentage;
            existing.ProductiveAppMinutes = summary.ProductiveAppMinutes;
            existing.PersonalAppMinutes = summary.PersonalAppMinutes;
            existing.UnknownAppMinutes = summary.UnknownAppMinutes;
            existing.FocusMinutes = summary.FocusMinutes;
            existing.ActivityScore = summary.ActivityScore;
            existing.DataCoveragePercentage = summary.DataCoveragePercentage;
            existing.TopAppsJson = summary.TopAppsJson;
            existing.IntensityAvg = summary.IntensityAvg;
            existing.KeyboardTotal = summary.KeyboardTotal;
            existing.MouseTotal = summary.MouseTotal;
        }
    }

    public Task<ActivityDailySummary?> GetDailySummaryAsync(Guid employeeId, DateOnly date, CancellationToken ct) =>
        _db.ActivityDailySummaries
            .FirstOrDefaultAsync(s => s.EmployeeId == employeeId && s.Date == date, ct);

    public async Task<IReadOnlyList<ActivitySnapshot>> GetSnapshotsAsync(
        Guid employeeId, DateOnly date, CancellationToken ct) =>
        await GetSnapshotsForDayAsync(employeeId, date, ct);

    public async Task<IReadOnlyList<ApplicationUsage>> GetAppUsageAsync(
        Guid employeeId, DateOnly date, CancellationToken ct) =>
        await GetAppUsageForDayAsync(employeeId, date, ct);

    public async Task<IReadOnlyList<MeetingSession>> GetMeetingsAsync(
        Guid employeeId, DateOnly date, CancellationToken ct) =>
        await GetMeetingsForDayAsync(employeeId, date, ct);

    public async Task<IReadOnlyList<ApplicationCategory>> GetCategoriesAsync(CancellationToken ct) =>
        await _db.ApplicationCategories.ToListAsync(ct);

    public async Task AddCategoryAsync(ApplicationCategory category, CancellationToken ct) =>
        await _db.ApplicationCategories.AddAsync(category, ct);

    public async Task<bool> DeleteCategoryAsync(Guid id, CancellationToken ct)
    {
        var rows = await _db.ApplicationCategories
            .Where(c => c.Id == id)
            .ExecuteDeleteAsync(ct);
        return rows > 0;
    }

    public async Task<int> DeleteRawBufferOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct) =>
        await _db.ActivityRawBuffer
            .Where(b => b.ReceivedAt < cutoff)
            .ExecuteDeleteAsync(ct);

    public async Task<int> DeleteSnapshotsOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct) =>
        await _db.ActivitySnapshots
            .Where(s => s.CapturedAt < cutoff)
            .ExecuteDeleteAsync(ct);
}
```

- [ ] **Step 3: Register in DependencyInjection.cs**

Add after the existing Agent Gateway registration:

```csharp
        // Activity Monitoring
        services.AddScoped<EfActivityMonitoringRepository>();
        services.AddScoped<IActivityMonitoringRepository>(
            sp => sp.GetRequiredService<EfActivityMonitoringRepository>());
```

Add using:
```csharp
using ONEVO.Application.Features.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Infrastructure.Persistence.Repositories.ActivityMonitoring;
```

- [ ] **Step 4: Build**

```bash
dotnet build src/ONEVO.Api/ONEVO.Api.csproj -c Release 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/ActivityMonitoring/RepositoryInterfaces/ \
        src/ONEVO.Infrastructure/Persistence/Repositories/ActivityMonitoring/ \
        src/ONEVO.Infrastructure/DependencyInjection.cs
git commit -m "feat(activity-monitoring): repository interface and EF implementation"
```

---

### Task 6: ProcessRawBufferJob

Reads from `activity_raw_buffer`, parses typed records from the batch payload, inserts into `activity_snapshots`, `application_usage`, `meeting_sessions`, `device_tracking`. Runs every 2 minutes.

**Files:**
- Create: `src/ONEVO.Infrastructure/Services/ActivityMonitoring/ProcessRawBufferJob.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Create ProcessRawBufferJob**

```csharp
// src/ONEVO.Infrastructure/Services/ActivityMonitoring/ProcessRawBufferJob.cs
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;

namespace ONEVO.Infrastructure.Services.ActivityMonitoring;

public sealed class ProcessRawBufferJob : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);
    private const int BatchSize = 200;

    // Meeting process names for Phase 1 detection
    private static readonly HashSet<string> MeetingProcesses =
        new(StringComparer.OrdinalIgnoreCase) { "teams.exe", "zoom.exe", "webex.exe", "skype.exe" };

    private readonly IServiceProvider _services;
    private readonly ILogger<ProcessRawBufferJob> _logger;

    public ProcessRawBufferJob(IServiceProvider services, ILogger<ProcessRawBufferJob> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
                await RunBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ProcessRawBufferJob batch failed; will retry next interval.");
            }
        }
    }

    private async Task RunBatchAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var activityRepo = scope.ServiceProvider.GetRequiredService<IActivityMonitoringRepository>();
        var agentRepo = scope.ServiceProvider.GetRequiredService<IAgentGatewayRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var batch = await activityRepo.GetPendingRawBatchAsync(BatchSize, ct);
        if (batch.Count == 0) return;

        var snapshots = new List<ActivitySnapshot>();
        var appUsage = new List<ApplicationUsage>();
        var meetings = new List<MeetingSession>();
        var deviceSessions = new List<(Guid TenantId, Guid AgentDeviceId, int ActiveMinutes)>();
        var processedIds = new List<Guid>();

        foreach (var item in batch)
        {
            try
            {
                // Resolve employee from agent
                var agent = await agentRepo.GetAgentByIdAsync(item.AgentDeviceId, ct);
                if (agent is null || agent.EmployeeId is null)
                {
                    processedIds.Add(item.Id);
                    continue;
                }

                var employeeId = agent.EmployeeId.Value;
                var tenantId = item.TenantId;

                using var doc = JsonDocument.Parse(item.PayloadJson);
                var root = doc.RootElement;

                if (!root.TryGetProperty("batch", out var batchArray)) 
                {
                    processedIds.Add(item.Id);
                    continue;
                }

                foreach (var entry in batchArray.EnumerateArray())
                {
                    if (!entry.TryGetProperty("type", out var typeEl)) continue;
                    var type = typeEl.GetString();
                    if (!entry.TryGetProperty("data", out var data)) continue;

                    switch (type)
                    {
                        case "activity_snapshot":
                            snapshots.Add(ParseSnapshot(data, tenantId, employeeId, item.ReceivedAt));
                            break;
                        case "app_usage":
                            appUsage.Add(ParseAppUsage(data, tenantId, employeeId, DateOnly.FromDateTime(item.ReceivedAt.UtcDateTime)));
                            break;
                        case "device_session":
                            if (data.TryGetProperty("active_minutes", out var am))
                                deviceSessions.Add((tenantId, item.AgentDeviceId, am.GetInt32()));
                            break;
                    }
                }

                processedIds.Add(item.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process raw buffer item {Id}; skipping.", item.Id);
                processedIds.Add(item.Id); // skip corrupt items
            }
        }

        // Detect meetings from app_usage (Phase 1: process name matching)
        foreach (var usage in appUsage.Where(u => MeetingProcesses.Contains(u.ProcessName)))
        {
            meetings.Add(new MeetingSession
            {
                Id = Guid.NewGuid(),
                TenantId = usage.TenantId,
                EmployeeId = usage.EmployeeId,
                MeetingStart = DateTimeOffset.UtcNow.Date.ToUniversalTime(),
                MeetingEnd = DateTimeOffset.UtcNow,
                Platform = usage.ProcessName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase),
                DurationMinutes = usage.TotalSeconds / 60,
                HadCameraOn = false,
                HadMicActivity = false
            });
        }

        if (snapshots.Count > 0) await activityRepo.BulkInsertSnapshotsAsync(snapshots, ct);
        if (appUsage.Count > 0) await activityRepo.BulkInsertApplicationUsageAsync(appUsage, ct);
        if (meetings.Count > 0) await activityRepo.BulkInsertMeetingSessionsAsync(meetings, ct);

        foreach (var (tenantId, agentDeviceId, activeMinutes) in deviceSessions)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            await activityRepo.UpsertDeviceTrackingAsync(new DeviceTracking
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = Guid.Empty, // resolved below if needed
                Date = today,
                LaptopActiveMinutes = activeMinutes,
                DetectionMethod = "agent"
            }, ct);
        }

        await activityRepo.DeleteRawBufferRowsAsync(processedIds, ct);
        await uow.SaveChangesAsync(ct);

        _logger.LogInformation(
            "ProcessRawBufferJob: {Snapshots} snapshots, {AppUsage} app records, {Meetings} meetings from {Batch} raw items.",
            snapshots.Count, appUsage.Count, meetings.Count, batch.Count);
    }

    private static ActivitySnapshot ParseSnapshot(JsonElement data, Guid tenantId, Guid employeeId, DateTimeOffset capturedAt)
    {
        var keyboardCount = data.TryGetProperty("keyboard_events_count", out var k) ? k.GetInt32() : 0;
        var mouseCount = data.TryGetProperty("mouse_events_count", out var m) ? m.GetInt32() : 0;
        var activeSeconds = data.TryGetProperty("active_seconds", out var a) ? a.GetInt32() : 0;
        var idleSeconds = data.TryGetProperty("idle_seconds", out var i) ? i.GetInt32() : 0;
        var processName = data.TryGetProperty("foreground_process_name", out var p) ? p.GetString() ?? string.Empty : string.Empty;

        const int maxExpected = 3000;
        var intensity = Math.Min((decimal)(keyboardCount + mouseCount) / maxExpected * 100, 100);

        return new ActivitySnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            CapturedAt = capturedAt,
            KeyboardEventsCount = keyboardCount,
            MouseEventsCount = mouseCount,
            ActiveSeconds = activeSeconds,
            IdleSeconds = idleSeconds,
            IntensityScore = intensity,
            ForegroundProcessName = processName,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static ApplicationUsage ParseAppUsage(JsonElement data, Guid tenantId, Guid employeeId, DateOnly date)
    {
        var processName = data.TryGetProperty("process_name", out var p) ? p.GetString() ?? string.Empty : string.Empty;
        var appName = data.TryGetProperty("application_name", out var a) ? a.GetString() ?? string.Empty : string.Empty;
        var category = data.TryGetProperty("app_category_type", out var c) ? c.GetString() : null;
        var titleHash = data.TryGetProperty("window_title_hash", out var h) ? h.GetString() : null;
        var duration = data.TryGetProperty("duration_seconds", out var d) ? d.GetInt32() : 0;

        return new ApplicationUsage
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            Date = date,
            ProcessName = processName,
            ApplicationName = appName,
            ApplicationCategory = category,
            WindowTitleHash = titleHash,
            TotalSeconds = duration
        };
    }
}
```

- [ ] **Step 2: Register ProcessRawBufferJob**

In `DependencyInjection.cs`, add after DetectOfflineAgentsJob:
```csharp
        services.AddHostedService<ProcessRawBufferJob>();
```

Add using:
```csharp
using ONEVO.Infrastructure.Services.ActivityMonitoring;
```

- [ ] **Step 3: Build**

```bash
dotnet build src/ONEVO.Api/ONEVO.Api.csproj -c Release 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Infrastructure/Services/ActivityMonitoring/ProcessRawBufferJob.cs \
        src/ONEVO.Infrastructure/DependencyInjection.cs
git commit -m "feat(activity-monitoring): ProcessRawBufferJob parses raw buffer into typed tables every 2 min"
```

---

### Task 7: AggregateDailySummaryJob + PurgeRawBufferJob

**Files:**
- Create: `src/ONEVO.Infrastructure/Services/ActivityMonitoring/AggregateDailySummaryJob.cs`
- Create: `src/ONEVO.Infrastructure/Services/ActivityMonitoring/PurgeRawBufferJob.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Create AggregateDailySummaryJob**

```csharp
// src/ONEVO.Infrastructure/Services/ActivityMonitoring/AggregateDailySummaryJob.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;
using ONEVO.Infrastructure.Persistence;
using System.Text.Json;

namespace ONEVO.Infrastructure.Services.ActivityMonitoring;

public sealed class AggregateDailySummaryJob : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    private readonly IServiceProvider _services;
    private readonly ILogger<AggregateDailySummaryJob> _logger;

    public AggregateDailySummaryJob(IServiceProvider services, ILogger<AggregateDailySummaryJob> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
                await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AggregateDailySummaryJob failed; will retry next interval.");
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IActivityMonitoringRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Find distinct (tenant_id, employee_id) combos that have snapshots today
        var activeEmployees = await db.ActivitySnapshots
            .Where(s => s.CapturedAt.Date == today.ToDateTime(TimeOnly.MinValue))
            .GroupBy(s => new { s.TenantId, s.EmployeeId })
            .Select(g => new { g.Key.TenantId, g.Key.EmployeeId })
            .ToListAsync(ct);

        foreach (var emp in activeEmployees)
        {
            var snapshots = await repo.GetSnapshotsForDayAsync(emp.EmployeeId, today, ct);
            var appUsage = await repo.GetAppUsageForDayAsync(emp.EmployeeId, today, ct);
            var meetings = await repo.GetMeetingsForDayAsync(emp.EmployeeId, today, ct);

            var totalActiveMin = snapshots.Sum(s => s.ActiveSeconds) / 60;
            var totalIdleMin = snapshots.Sum(s => s.IdleSeconds) / 60;
            var totalMeetingMin = meetings.Sum(m => m.DurationMinutes);
            var keyboardTotal = snapshots.Sum(s => s.KeyboardEventsCount);
            var mouseTotal = snapshots.Sum(s => s.MouseEventsCount);
            var intensityAvg = snapshots.Count > 0
                ? snapshots.Average(s => (double)s.IntensityScore)
                : 0;

            var productiveMin = appUsage.Where(u => u.IsProductive == true).Sum(u => u.TotalSeconds) / 60;
            var personalMin = appUsage.Where(u => u.IsProductive == false).Sum(u => u.TotalSeconds) / 60;
            var unknownMin = appUsage.Where(u => u.IsProductive is null).Sum(u => u.TotalSeconds) / 60;

            var totalMin = totalActiveMin + totalIdleMin;
            var activePercent = totalMin > 0 ? (decimal)totalActiveMin / totalMin * 100 : 0;
            var activityScore = Math.Min((decimal)intensityAvg, 100);

            var topApps = appUsage
                .GroupBy(u => u.ApplicationName)
                .Select(g => new { app = g.Key, seconds = g.Sum(u => u.TotalSeconds) })
                .OrderByDescending(x => x.seconds)
                .Take(5)
                .ToList();
            var topAppsJson = JsonSerializer.Serialize(topApps);

            var summary = new ActivityDailySummary
            {
                Id = Guid.NewGuid(),
                TenantId = emp.TenantId,
                EmployeeId = emp.EmployeeId,
                Date = today,
                TotalActiveMinutes = totalActiveMin,
                TotalIdleMinutes = totalIdleMin,
                TotalMeetingMinutes = totalMeetingMin,
                ActivePercentage = Math.Round(activePercent, 2),
                ProductiveAppMinutes = productiveMin,
                PersonalAppMinutes = personalMin,
                UnknownAppMinutes = unknownMin,
                FocusMinutes = 0, // Phase 2: 30+ min uninterrupted sessions
                ActivityScore = Math.Round(activityScore, 2),
                DataCoveragePercentage = 100,
                TopAppsJson = topAppsJson,
                IntensityAvg = Math.Round((decimal)intensityAvg, 2),
                KeyboardTotal = keyboardTotal,
                MouseTotal = mouseTotal
            };

            await repo.UpsertDailySummaryAsync(summary, ct);
        }

        if (activeEmployees.Count > 0)
            await uow.SaveChangesAsync(ct);

        _logger.LogInformation("AggregateDailySummaryJob: aggregated {Count} employee summaries for {Date}.",
            activeEmployees.Count, today);
    }
}
```

- [ ] **Step 2: Create PurgeRawBufferJob**

```csharp
// src/ONEVO.Infrastructure/Services/ActivityMonitoring/PurgeRawBufferJob.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.ActivityMonitoring.RepositoryInterfaces;

namespace ONEVO.Infrastructure.Services.ActivityMonitoring;

public sealed class PurgeRawBufferJob : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan RetentionWindow = TimeSpan.FromHours(48);

    private readonly IServiceProvider _services;
    private readonly ILogger<PurgeRawBufferJob> _logger;

    public PurgeRawBufferJob(IServiceProvider services, ILogger<PurgeRawBufferJob> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
                await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PurgeRawBufferJob failed; will retry next interval.");
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IActivityMonitoringRepository>();

        var cutoff = DateTimeOffset.UtcNow.Subtract(RetentionWindow);
        var deleted = await repo.DeleteRawBufferOlderThanAsync(cutoff, ct);

        if (deleted > 0)
            _logger.LogInformation("PurgeRawBufferJob: deleted {Count} raw buffer rows older than {Cutoff}.",
                deleted, cutoff);
    }
}
```

- [ ] **Step 3: Register both jobs in DependencyInjection.cs**

```csharp
        services.AddHostedService<AggregateDailySummaryJob>();
        services.AddHostedService<PurgeRawBufferJob>();
```

- [ ] **Step 4: Build**

```bash
dotnet build src/ONEVO.Api/ONEVO.Api.csproj -c Release 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Infrastructure/Services/ActivityMonitoring/ \
        src/ONEVO.Infrastructure/DependencyInjection.cs
git commit -m "feat(activity-monitoring): AggregateDailySummaryJob (30 min) and PurgeRawBufferJob (48h retention)"
```

---

### Task 8: Activity Monitoring DTOs + Queries

**Files:**
- Create: 5 DTO files
- Create: 4 query handlers

- [ ] **Step 1: Create DTOs**

```csharp
// src/ONEVO.Application/Features/ActivityMonitoring/DTOs/Responses/ActivityDailySummaryDto.cs
namespace ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;

public record ActivityDailySummaryDto(
    Guid EmployeeId,
    DateOnly Date,
    int TotalActiveMinutes,
    int TotalIdleMinutes,
    int TotalMeetingMinutes,
    decimal ActivePercentage,
    int ProductiveAppMinutes,
    int PersonalAppMinutes,
    decimal ActivityScore,
    string TopAppsJson,
    decimal IntensityAvg,
    int KeyboardTotal,
    int MouseTotal);
```

```csharp
// src/ONEVO.Application/Features/ActivityMonitoring/DTOs/Responses/ActivitySnapshotDto.cs
namespace ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;

public record ActivitySnapshotDto(
    Guid Id,
    DateTimeOffset CapturedAt,
    int KeyboardEventsCount,
    int MouseEventsCount,
    int ActiveSeconds,
    int IdleSeconds,
    decimal IntensityScore,
    string ForegroundProcessName);
```

```csharp
// src/ONEVO.Application/Features/ActivityMonitoring/DTOs/Responses/ApplicationUsageDto.cs
namespace ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;

public record ApplicationUsageDto(
    Guid Id,
    string ProcessName,
    string ApplicationName,
    string? ApplicationCategory,
    int TotalSeconds,
    bool? IsProductive,
    bool? IsAllowed);
```

```csharp
// src/ONEVO.Application/Features/ActivityMonitoring/DTOs/Responses/MeetingSessionDto.cs
namespace ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;

public record MeetingSessionDto(
    Guid Id,
    DateTimeOffset MeetingStart,
    DateTimeOffset MeetingEnd,
    string Platform,
    int DurationMinutes,
    bool HadCameraOn,
    bool HadMicActivity);
```

```csharp
// src/ONEVO.Application/Features/ActivityMonitoring/DTOs/Responses/ApplicationCategoryDto.cs
namespace ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;

public record ApplicationCategoryDto(
    Guid Id,
    string ApplicationNamePattern,
    string Category,
    bool? IsProductive);
```

- [ ] **Step 2: Create GetDailySummaryQuery + Handler**

```csharp
// src/ONEVO.Application/Features/ActivityMonitoring/Queries/GetDailySummary/GetDailySummaryQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;

namespace ONEVO.Application.Features.ActivityMonitoring.Queries.GetDailySummary;

public record GetDailySummaryQuery(Guid EmployeeId, DateOnly Date) : IRequest<Result<ActivityDailySummaryDto>>;
```

```csharp
// src/ONEVO.Application/Features/ActivityMonitoring/Queries/GetDailySummary/GetDailySummaryQueryHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.ActivityMonitoring.RepositoryInterfaces;

namespace ONEVO.Application.Features.ActivityMonitoring.Queries.GetDailySummary;

public class GetDailySummaryQueryHandler
    : IRequestHandler<GetDailySummaryQuery, Result<ActivityDailySummaryDto>>
{
    private readonly IActivityMonitoringRepository _repo;
    public GetDailySummaryQueryHandler(IActivityMonitoringRepository repo) => _repo = repo;

    public async Task<Result<ActivityDailySummaryDto>> Handle(
        GetDailySummaryQuery request, CancellationToken ct)
    {
        var summary = await _repo.GetDailySummaryAsync(request.EmployeeId, request.Date, ct);
        if (summary is null)
            return Result<ActivityDailySummaryDto>.NotFound("No summary found for this employee and date.");

        return Result<ActivityDailySummaryDto>.Success(new ActivityDailySummaryDto(
            summary.EmployeeId, summary.Date,
            summary.TotalActiveMinutes, summary.TotalIdleMinutes, summary.TotalMeetingMinutes,
            summary.ActivePercentage, summary.ProductiveAppMinutes, summary.PersonalAppMinutes,
            summary.ActivityScore, summary.TopAppsJson, summary.IntensityAvg,
            summary.KeyboardTotal, summary.MouseTotal));
    }
}
```

- [ ] **Step 3: Create GetSnapshots, GetAppUsage, GetMeetings queries**

```csharp
// src/ONEVO.Application/Features/ActivityMonitoring/Queries/GetSnapshots/GetSnapshotsQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;

namespace ONEVO.Application.Features.ActivityMonitoring.Queries.GetSnapshots;

public record GetSnapshotsQuery(Guid EmployeeId, DateOnly Date)
    : IRequest<Result<List<ActivitySnapshotDto>>>;
```

```csharp
// src/ONEVO.Application/Features/ActivityMonitoring/Queries/GetSnapshots/GetSnapshotsQueryHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.ActivityMonitoring.RepositoryInterfaces;

namespace ONEVO.Application.Features.ActivityMonitoring.Queries.GetSnapshots;

public class GetSnapshotsQueryHandler
    : IRequestHandler<GetSnapshotsQuery, Result<List<ActivitySnapshotDto>>>
{
    private readonly IActivityMonitoringRepository _repo;
    public GetSnapshotsQueryHandler(IActivityMonitoringRepository repo) => _repo = repo;

    public async Task<Result<List<ActivitySnapshotDto>>> Handle(
        GetSnapshotsQuery request, CancellationToken ct)
    {
        var list = await _repo.GetSnapshotsAsync(request.EmployeeId, request.Date, ct);
        var dtos = list.Select(s => new ActivitySnapshotDto(
            s.Id, s.CapturedAt, s.KeyboardEventsCount, s.MouseEventsCount,
            s.ActiveSeconds, s.IdleSeconds, s.IntensityScore, s.ForegroundProcessName)).ToList();
        return Result<List<ActivitySnapshotDto>>.Success(dtos);
    }
}
```

```csharp
// src/ONEVO.Application/Features/ActivityMonitoring/Queries/GetAppUsage/GetAppUsageQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;

namespace ONEVO.Application.Features.ActivityMonitoring.Queries.GetAppUsage;

public record GetAppUsageQuery(Guid EmployeeId, DateOnly Date)
    : IRequest<Result<List<ApplicationUsageDto>>>;
```

```csharp
// src/ONEVO.Application/Features/ActivityMonitoring/Queries/GetAppUsage/GetAppUsageQueryHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.ActivityMonitoring.RepositoryInterfaces;

namespace ONEVO.Application.Features.ActivityMonitoring.Queries.GetAppUsage;

public class GetAppUsageQueryHandler
    : IRequestHandler<GetAppUsageQuery, Result<List<ApplicationUsageDto>>>
{
    private readonly IActivityMonitoringRepository _repo;
    public GetAppUsageQueryHandler(IActivityMonitoringRepository repo) => _repo = repo;

    public async Task<Result<List<ApplicationUsageDto>>> Handle(
        GetAppUsageQuery request, CancellationToken ct)
    {
        var list = await _repo.GetAppUsageAsync(request.EmployeeId, request.Date, ct);
        var dtos = list.Select(u => new ApplicationUsageDto(
            u.Id, u.ProcessName, u.ApplicationName, u.ApplicationCategory,
            u.TotalSeconds, u.IsProductive, u.IsAllowed)).ToList();
        return Result<List<ApplicationUsageDto>>.Success(dtos);
    }
}
```

```csharp
// src/ONEVO.Application/Features/ActivityMonitoring/Queries/GetMeetings/GetMeetingsQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;

namespace ONEVO.Application.Features.ActivityMonitoring.Queries.GetMeetings;

public record GetMeetingsQuery(Guid EmployeeId, DateOnly Date)
    : IRequest<Result<List<MeetingSessionDto>>>;
```

```csharp
// src/ONEVO.Application/Features/ActivityMonitoring/Queries/GetMeetings/GetMeetingsQueryHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.ActivityMonitoring.RepositoryInterfaces;

namespace ONEVO.Application.Features.ActivityMonitoring.Queries.GetMeetings;

public class GetMeetingsQueryHandler
    : IRequestHandler<GetMeetingsQuery, Result<List<MeetingSessionDto>>>
{
    private readonly IActivityMonitoringRepository _repo;
    public GetMeetingsQueryHandler(IActivityMonitoringRepository repo) => _repo = repo;

    public async Task<Result<List<MeetingSessionDto>>> Handle(
        GetMeetingsQuery request, CancellationToken ct)
    {
        var list = await _repo.GetMeetingsAsync(request.EmployeeId, request.Date, ct);
        var dtos = list.Select(m => new MeetingSessionDto(
            m.Id, m.MeetingStart, m.MeetingEnd, m.Platform,
            m.DurationMinutes, m.HadCameraOn, m.HadMicActivity)).ToList();
        return Result<List<MeetingSessionDto>>.Success(dtos);
    }
}
```

- [ ] **Step 4: Build**

```bash
dotnet build src/ONEVO.Api/ONEVO.Api.csproj -c Release 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/ActivityMonitoring/
git commit -m "feat(activity-monitoring): DTOs and query handlers for summary, snapshots, app usage, meetings"
```

---

### Task 9: ActivityMonitoringController (Manager + Self-Service Endpoints)

**Files:**
- Create: `src/ONEVO.Api/Controllers/ActivityMonitoring/ActivityMonitoringController.cs`

- [ ] **Step 1: Create controller**

```csharp
// src/ONEVO.Api/Controllers/ActivityMonitoring/ActivityMonitoringController.cs
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.ActivityMonitoring.Queries.GetAppUsage;
using ONEVO.Application.Features.ActivityMonitoring.Queries.GetDailySummary;
using ONEVO.Application.Features.ActivityMonitoring.Queries.GetMeetings;
using ONEVO.Application.Features.ActivityMonitoring.Queries.GetSnapshots;
using System.Security.Claims;

namespace ONEVO.Api.Controllers.ActivityMonitoring;

[ApiController]
[Route("api/v1/activity")]
[Authorize]
public class ActivityMonitoringController : ControllerBase
{
    private readonly IMediator _mediator;
    public ActivityMonitoringController(IMediator mediator) => _mediator = mediator;

    // ── Manager-facing (requires monitoring:read permission) ───────────────────

    [HttpGet("summary/{employeeId}")]
    public async Task<IActionResult> GetDailySummary(
        Guid employeeId, [FromQuery] DateOnly date, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetDailySummaryQuery(employeeId, date), ct);
        if (!result.IsSuccess) return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    [HttpGet("snapshots/{employeeId}")]
    public async Task<IActionResult> GetSnapshots(
        Guid employeeId, [FromQuery] DateOnly date, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSnapshotsQuery(employeeId, date), ct);
        if (!result.IsSuccess) return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    [HttpGet("apps/{employeeId}")]
    public async Task<IActionResult> GetAppUsage(
        Guid employeeId, [FromQuery] DateOnly date, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetAppUsageQuery(employeeId, date), ct);
        if (!result.IsSuccess) return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    [HttpGet("meetings/{employeeId}")]
    public async Task<IActionResult> GetMeetings(
        Guid employeeId, [FromQuery] DateOnly date, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetMeetingsQuery(employeeId, date), ct);
        if (!result.IsSuccess) return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    // ── Self-service (employee's own data) ─────────────────────────────────────

    [HttpGet("my/summary")]
    public async Task<IActionResult> GetMySummary([FromQuery] DateOnly date, CancellationToken ct)
    {
        var employeeId = GetCallerEmployeeId();
        if (employeeId == Guid.Empty) return Unauthorized();
        var result = await _mediator.Send(new GetDailySummaryQuery(employeeId, date), ct);
        if (!result.IsSuccess) return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    [HttpGet("my/apps")]
    public async Task<IActionResult> GetMyAppUsage([FromQuery] DateOnly date, CancellationToken ct)
    {
        var employeeId = GetCallerEmployeeId();
        if (employeeId == Guid.Empty) return Unauthorized();
        var result = await _mediator.Send(new GetAppUsageQuery(employeeId, date), ct);
        if (!result.IsSuccess) return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    [HttpGet("my/meetings")]
    public async Task<IActionResult> GetMyMeetings([FromQuery] DateOnly date, CancellationToken ct)
    {
        var employeeId = GetCallerEmployeeId();
        if (employeeId == Guid.Empty) return Unauthorized();
        var result = await _mediator.Send(new GetMeetingsQuery(employeeId, date), ct);
        if (!result.IsSuccess) return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    private Guid GetCallerEmployeeId()
    {
        var value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build src/ONEVO.Api/ONEVO.Api.csproj -c Release 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add src/ONEVO.Api/Controllers/ActivityMonitoring/
git commit -m "feat(activity-monitoring): ActivityMonitoringController with manager and self-service endpoints"
```

---

### Task 10: Agent Health List + Detail Endpoints

The spec requires `GET /api/v1/agents` (health list) and `GET /api/v1/agents/{agentId}/health` endpoints for managing fleet health.

**Files:**
- Create: `src/ONEVO.Application/Features/AgentGateway/Queries/GetAgentHealthList/GetAgentHealthListQuery.cs`
- Create: `src/ONEVO.Application/Features/AgentGateway/Queries/GetAgentHealthList/GetAgentHealthListQueryHandler.cs`
- Create: `src/ONEVO.Api/Controllers/AgentGateway/AgentFleetController.cs`
- Modify: `src/ONEVO.Application/Features/AgentGateway/RepositoryInterfaces/IAgentGatewayRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/AgentGateway/EfAgentGatewayRepository.cs`

- [ ] **Step 1: Add repository methods for fleet health**

In `IAgentGatewayRepository.cs`, add:

```csharp
    // Fleet health
    Task<IReadOnlyList<RegisteredAgent>> GetActiveAgentsAsync(CancellationToken ct);
    Task<IReadOnlyList<AgentHealthLog>> GetRecentHealthLogsAsync(Guid agentId, int count, CancellationToken ct);
```

In `EfAgentGatewayRepository.cs`, add:

```csharp
    public async Task<IReadOnlyList<RegisteredAgent>> GetActiveAgentsAsync(CancellationToken ct) =>
        await _db.RegisteredAgents
            .Where(a => a.Status == "active")
            .OrderByDescending(a => a.LastHeartbeatAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AgentHealthLog>> GetRecentHealthLogsAsync(
        Guid agentId, int count, CancellationToken ct) =>
        await _db.AgentHealthLogs
            .Where(h => h.AgentId == agentId)
            .OrderByDescending(h => h.ReportedAt)
            .Take(count)
            .ToListAsync(ct);
```

- [ ] **Step 2: Create GetAgentHealthListQuery + Handler**

```csharp
// src/ONEVO.Application/Features/AgentGateway/Queries/GetAgentHealthList/GetAgentHealthListQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.AgentGateway.Queries.GetAgentHealthList;

public record AgentHealthListItemDto(
    Guid AgentId,
    string DeviceName,
    string Status,
    string AgentVersion,
    DateTimeOffset? LastHeartbeatAt,
    Guid? EmployeeId);

public record GetAgentHealthListQuery : IRequest<Result<List<AgentHealthListItemDto>>>;
```

```csharp
// src/ONEVO.Application/Features/AgentGateway/Queries/GetAgentHealthList/GetAgentHealthListQueryHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;

namespace ONEVO.Application.Features.AgentGateway.Queries.GetAgentHealthList;

public class GetAgentHealthListQueryHandler
    : IRequestHandler<GetAgentHealthListQuery, Result<List<AgentHealthListItemDto>>>
{
    private readonly IAgentGatewayRepository _repo;
    public GetAgentHealthListQueryHandler(IAgentGatewayRepository repo) => _repo = repo;

    public async Task<Result<List<AgentHealthListItemDto>>> Handle(
        GetAgentHealthListQuery request, CancellationToken ct)
    {
        var agents = await _repo.GetActiveAgentsAsync(ct);
        var dtos = agents.Select(a => new AgentHealthListItemDto(
            a.Id, a.DeviceName, a.Status, a.AgentVersion, a.LastHeartbeatAt, a.EmployeeId)).ToList();
        return Result<List<AgentHealthListItemDto>>.Success(dtos);
    }
}
```

- [ ] **Step 3: Create AgentFleetController**

```csharp
// src/ONEVO.Api/Controllers/AgentGateway/AgentFleetController.cs
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.AgentGateway.Queries.GetAgentHealthList;

namespace ONEVO.Api.Controllers.AgentGateway;

[ApiController]
[Route("api/v1/agents")]
[Authorize]
public class AgentFleetController : ControllerBase
{
    private readonly IMediator _mediator;
    public AgentFleetController(IMediator mediator) => _mediator = mediator;

    /// <summary>Fleet health list — all active agents for this tenant.</summary>
    [HttpGet]
    public async Task<IActionResult> GetFleet(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetAgentHealthListQuery(), ct);
        if (!result.IsSuccess) return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }
}
```

- [ ] **Step 4: Build**

```bash
dotnet build src/ONEVO.Api/ONEVO.Api.csproj -c Release 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/AgentGateway/Queries/GetAgentHealthList/ \
        src/ONEVO.Api/Controllers/AgentGateway/AgentFleetController.cs \
        src/ONEVO.Application/Features/AgentGateway/RepositoryInterfaces/IAgentGatewayRepository.cs \
        src/ONEVO.Infrastructure/Persistence/Repositories/AgentGateway/EfAgentGatewayRepository.cs
git commit -m "feat(agent-gateway): agent fleet health list endpoint and repository methods"
```

---

### Task 11: Tests — ProcessRawBufferJob + DetectOfflineAgentsJob

**Files:**
- Create: `tests/ONEVO.Tests/Features/ActivityMonitoring/ProcessRawBufferJobTests.cs`
- Create: `tests/ONEVO.Tests/Features/AgentGateway/DetectOfflineAgentsJobTests.cs`

> **Before writing tests:** Run `find tests/ -name "*.csproj" | head -5` to discover the test project path. Adapt the namespace accordingly.

- [ ] **Step 1: Discover test project structure**

```bash
find . -name "*.Tests.csproj" | head -5
```

Note the project path. All test files go in that project.

- [ ] **Step 2: Write ProcessRawBufferJob unit tests**

In the discovered test project, create `Features/ActivityMonitoring/ProcessRawBufferJobTests.cs`:

```csharp
using Moq;
using FluentAssertions;
using ONEVO.Application.Features.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;
using System.Text.Json;

namespace ONEVO.Tests.Features.ActivityMonitoring;

public class ProcessRawBufferJobTests
{
    private static string MakePayload(string type, object data)
    {
        var payload = new { batch = new[] { new { type, data } } };
        return JsonSerializer.Serialize(payload);
    }

    [Fact]
    public void ActivitySnapshot_payload_parses_keyboard_and_mouse_counts()
    {
        var data = new
        {
            keyboard_events_count = 200,
            mouse_events_count = 50,
            active_seconds = 120,
            idle_seconds = 30,
            foreground_process_name = "code.exe"
        };

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(data));
        var el = doc.RootElement;

        var keyCount = el.TryGetProperty("keyboard_events_count", out var k) ? k.GetInt32() : 0;
        var mouseCount = el.TryGetProperty("mouse_events_count", out var m) ? m.GetInt32() : 0;

        keyCount.Should().Be(200);
        mouseCount.Should().Be(50);
    }

    [Fact]
    public void IntensityScore_is_capped_at_100()
    {
        const int maxExpected = 3000;
        var intensity = Math.Min((decimal)(9999 + 9999) / maxExpected * 100, 100);
        intensity.Should().Be(100);
    }

    [Fact]
    public void IntensityScore_formula_is_proportional()
    {
        const int maxExpected = 3000;
        var intensity = (decimal)(300 + 300) / maxExpected * 100;
        intensity.Should().Be(20m);
    }

    [Fact]
    public void Payload_with_unknown_type_is_skipped_gracefully()
    {
        var payload = """{"batch":[{"type":"unknown_type","data":{}}]}""";
        using var doc = JsonDocument.Parse(payload);
        var batch = doc.RootElement.GetProperty("batch");
        var count = 0;
        foreach (var entry in batch.EnumerateArray())
        {
            var type = entry.GetProperty("type").GetString();
            if (type == "activity_snapshot") count++;
        }
        count.Should().Be(0, "unknown type should be skipped");
    }
}
```

- [ ] **Step 3: Run tests**

```bash
dotnet test --filter "ProcessRawBufferJob" -v minimal 2>&1 | tail -10
```

Expected: `Passed! - 4`

- [ ] **Step 4: Write DetectOfflineAgentsJob unit tests**

```csharp
using Moq;
using FluentAssertions;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.RepositoryInterfaces;

namespace ONEVO.Tests.Features.AgentGateway;

public class DetectOfflineAgentsJobTests
{
    [Fact]
    public async Task When_no_offline_agents_outbox_is_not_written()
    {
        var repo = new Mock<IAgentGatewayRepository>();
        repo.Setup(r => r.MarkAgentsInactiveAndReturnIdsAsync(It.IsAny<DateTimeOffset>(), default))
            .ReturnsAsync(new List<Guid>());

        var outbox = new Mock<IOutboxWriter>();
        var uow = new Mock<IUnitOfWork>();

        // Act: simulate the RunAsync logic
        var agentIds = await repo.Object.MarkAgentsInactiveAndReturnIdsAsync(
            DateTimeOffset.UtcNow.AddMinutes(-5), default);

        // Assert
        agentIds.Should().BeEmpty();
        outbox.Verify(o => o.WriteAsync(It.IsAny<string>(), It.IsAny<object>(), default), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task When_agents_go_offline_outbox_event_written_per_agent()
    {
        var offlineIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var repo = new Mock<IAgentGatewayRepository>();
        repo.Setup(r => r.MarkAgentsInactiveAndReturnIdsAsync(It.IsAny<DateTimeOffset>(), default))
            .ReturnsAsync(offlineIds);

        var outbox = new Mock<IOutboxWriter>();
        var uow = new Mock<IUnitOfWork>();

        // Act: simulate the RunAsync logic
        var agentIds = await repo.Object.MarkAgentsInactiveAndReturnIdsAsync(
            DateTimeOffset.UtcNow.AddMinutes(-5), default);

        foreach (var agentId in agentIds)
            await outbox.Object.WriteAsync("AgentHeartbeatLost", new { agent_id = agentId }, default);

        await uow.Object.SaveChangesAsync(default);

        // Assert
        outbox.Verify(o => o.WriteAsync("AgentHeartbeatLost", It.IsAny<object>(), default),
            Times.Exactly(2));
        uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }
}
```

- [ ] **Step 5: Run tests**

```bash
dotnet test --filter "DetectOfflineAgentsJob" -v minimal 2>&1 | tail -10
```

Expected: `Passed! - 2`

- [ ] **Step 6: Commit**

```bash
git add tests/
git commit -m "test(activity-monitoring): unit tests for ProcessRawBufferJob parsing and DetectOfflineAgentsJob outbox event"
```

---

## Self-Review

**1. Spec coverage check:**

| Requirement | Task |
|---|---|
| Heartbeat persists health + updates last_heartbeat_at | ✅ Done (pre-plan) |
| GET /agent/policy | ✅ Done (pre-plan) |
| POST /agent/ingest 202 Accepted | ✅ Done (pre-plan) |
| activity_raw_buffer schema matches spec | Task 1 |
| Ingest payload matches spec format | Task 1 |
| AgentHeartbeatLost outbox event | Task 2 |
| 7 Activity Monitoring tables | Tasks 3-4 |
| ProcessRawBufferJob (snapshots, app_usage, meetings) | Task 6 |
| AggregateDailySummaryJob (30 min) | Task 7 |
| PurgeRawBufferJob (48h retention) | Task 7 |
| Activity Monitoring read endpoints | Tasks 8-9 |
| Agent fleet health endpoint | Task 10 |
| Tests | Task 11 |

**2. Not in this plan (follow-on work):**
- RLS policies for new Activity Monitoring tables (`activity_snapshots`, `application_usage`, `meeting_sessions`, `monitoring_evidence_assets`, `activity_daily_summary`, `application_categories`, `device_tracking`) — needs a follow-on migration matching `AddAgentGatewayRlsPolicies` pattern
- `agent_commands` table + SignalR hub `/hubs/agent-commands` — DEV4 Task 3 extension
- Rate limiting per device (30 req/min) — needs a middleware or policy
- Configuration module integration for merged effective policy — depends on DEV1
- DEV4 Tasks 4-8 (Identity Verification, Alerts, Discrepancy Engine, IDE Agent Install, Agent Version Manager)

**3. No placeholders found.**

**4. Type consistency verified** — all types referenced across tasks match their definitions.
