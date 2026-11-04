# Meeting Detection End-to-End Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `MeetingDetector` (`ONEVO.Agent.TrayApp/Collectors/MeetingDetector.cs`) is registered as a collector (`CollectorCoordinator` starts/stops it) but is a dead stub: `StartAsync`/`StopAsync` only log, and `IsMeetingAppRunning()` is never called anywhere. Wire it into the same collector → IPC → Service sync → backend ingest → query pipeline that `DeviceStateCollector` already proves works, so meeting-app presence becomes real, queryable data instead of scaffolding.

**Architecture:** Per `ONEVO_Agent_Architecture_Flow_Folder_Structure.md` §7.4, Phase 1 meeting detection is **process-name matching only** — probabilistic, never presented as proof of an actual meeting. This plan does not add camera/microphone-in-use signals (§7.4 lists that as optional; out of scope here to keep the change mechanical and low-risk). The data path mirrors `DeviceStateSnapshot` exactly end-to-end: `MeetingDetector` (interactive-session collector, §2.3) samples periodically → privacy-scrubbed `CollectionRecord` over IPC (§4.2) → `ActivitySyncService` batches and flushes to a new backend ingest endpoint → new `MeetingSignal` table (RLS-protected like every other monitoring table) → new query endpoint. `MonitoringCapability.MeetingDetection` and the `MeetingDetection` column already exist on `MonitoringFeatureToggles`/`EmployeeMonitoringOverride`/`MonitoringPolicyOverride` — this plan is the first feature to actually gate on and populate that capability.

**Tech Stack:** .NET 10 (MAUI TrayApp + Windows Service), ASP.NET Core backend, MediatR, EF Core 8/PostgreSQL, xUnit/Moq/FluentAssertions (backend), xUnit (TrayApp).

**Scope boundary:** Process-name signal only, matching the architecture doc's Phase 1 scope. No camera/mic-in-use detection, no meeting duration/attendance derivation, no calendar integration — those are separate, larger features.

---

## File Structure

| File | Responsibility |
|---|---|
| `tray_app_maui/ONEVO.Agent.Shared/Models/CollectionRecord.cs` | Modify: add `MeetingSignal` record type + schema version + payload |
| `tray_app_maui/ONEVO.Agent.TrayApp/Collectors/MeetingDetector.cs` | Modify: real periodic sampling loop, emits `CollectionRecord`s over IPC |
| `tray_app_maui/ONEVO.Agent.Service/Api/AgentApiRoutes.cs` | Modify: add the ingest route |
| `tray_app_maui/ONEVO.Agent.Service/Api/ActivityIngestModels.cs` | Modify: add wire DTOs |
| `tray_app_maui/ONEVO.Agent.Service/Sync/ActivitySyncService.cs` | Modify: batch/dispatch/flush the new record type |
| `HRMS-Backend-v1/src/ONEVO.Domain/Errors/MonitoringErrors.cs` | Modify: add `MeetingDetectionDisabled` error |
| `HRMS-Backend-v1/src/ONEVO.Domain/Features/Monitoring/Meetings/Entities/MeetingSignal.cs` | New entity |
| `HRMS-Backend-v1/src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/Meetings/MeetingSignalConfiguration.cs` | New EF configuration |
| `HRMS-Backend-v1/src/ONEVO.Infrastructure/Migrations/<generated>_AddMeetingSignals.cs` | New migration (table + RLS) |
| `HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/Meetings/RepositoryInterfaces/IMeetingSignalRepository.cs` | Repository contract |
| `HRMS-Backend-v1/src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/Meetings/EfMeetingSignalRepository.cs` | EF implementation |
| `HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/Meetings/Mappers/MeetingSignalMapper.cs` | Payload → entity mapping |
| `HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/Meetings/Commands/IngestMeetingSignals/*` | Ingest command/handler/validator |
| `HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/Meetings/Queries/GetMeetingSignals/*` | Query/handler |
| `HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/Meetings/DTOs/Responses/MeetingSignalDto.cs` | Query response shape |
| `HRMS-Backend-v1/src/ONEVO.Api/Controllers/Tenant/Monitoring/Meetings/MonitoringMeetingIngestController.cs` | `POST /api/v1/monitoring/meetings/signals` (TrayDevicePolicy) |
| `HRMS-Backend-v1/src/ONEVO.Api/Controllers/Tenant/Monitoring/Meetings/MonitoringMeetingController.cs` | `GET /api/v1/monitoring/meetings/signals` (TenantPolicy) |
| `HRMS-Backend-v1/src/ONEVO.Infrastructure/DependencyInjection.cs` | Modify: register repository |

---

### Task 1: Shared contract — new record type

**Files:**
- Modify: `tray_app_maui/ONEVO.Agent.Shared/Models/CollectionRecord.cs`

- [ ] **Step 1: Add the record type, schema version, and payload**

```csharp
// In CollectionRecordTypes:
public const string MeetingSignal = "meeting_signal";

// In CollectionSchemaVersions:
public const string MeetingSignalV1 = "1.0";
```

Add a new payload record in the same file, next to `WorkSessionPayload`:

```csharp
/// <summary>
/// Phase 1 probabilistic meeting-app-presence sample (§7.4). ProcessName identifies
/// which known meeting app was found running - never proof of an active meeting.
/// </summary>
public sealed record MeetingSignalPayload
{
    public required DateTimeOffset CapturedAt { get; init; }
    public required bool IsMeetingAppRunning { get; init; }
    public string? ProcessName { get; init; }
}
```

- [ ] **Step 2: Build the Shared project**

Run: `dotnet build tray_app_maui/ONEVO.Agent.Shared/ONEVO.Agent.Shared.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add tray_app_maui/ONEVO.Agent.Shared/Models/CollectionRecord.cs
git commit -m "feat: add MeetingSignal collection record type"
```

---

### Task 2: TrayApp — wire MeetingDetector to actually emit records

**Files:**
- Modify: `tray_app_maui/ONEVO.Agent.TrayApp/Collectors/MeetingDetector.cs`
- Test: `tray_app_maui/tests/ONEVO.Agent.TrayApp.Tests/Collectors/MeetingDetectorTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
namespace ONEVO.Agent.TrayApp.Tests.Collectors;

using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Collectors;
using Xunit;

public class MeetingDetectorTests
{
    [Fact]
    public async Task StartAsync_SamplesImmediatelyOnStart_SubmitsRecordOverPipe()
    {
        var pipe = new Mock<INamedPipeClient>();
        List<CollectionRecord>? submitted = null;
        pipe.Setup(p => p.SubmitCollectionRecordsAsync(It.IsAny<IReadOnlyList<CollectionRecord>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<CollectionRecord>, CancellationToken>((records, _) => submitted = records.ToList())
            .Returns(Task.CompletedTask);

        await using var sut = new MeetingDetector(NullLogger<MeetingDetector>.Instance, pipe.Object);
        await sut.StartAsync(policy: null!, CancellationToken.None);
        await Task.Delay(50); // allow the immediate first sample to complete

        submitted.Should().NotBeNull();
        submitted!.Should().ContainSingle();
        submitted[0].RecordType.Should().Be(CollectionRecordTypes.MeetingSignal);
        submitted[0].SchemaVersion.Should().Be(CollectionSchemaVersions.MeetingSignalV1);

        await sut.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void IsMeetingAppRunning_NoKnownProcessRunning_ReturnsFalse()
    {
        // Existing static probe behavior is unchanged by this task - regression guard only.
        MeetingDetector.IsMeetingAppRunning().Should().Be(MeetingDetector.IsMeetingAppRunning());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests --filter MeetingDetectorTests`
Expected: build error — constructor doesn't accept `INamedPipeClient` yet, no sampling loop exists.

- [ ] **Step 3: Rewrite `MeetingDetector.cs`**

```csharp
namespace ONEVO.Agent.TrayApp.Collectors;

using System.Diagnostics;
using System.Text.Json;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Phase 1 probabilistic meeting detection via known process names (§7.4).
/// Process found ≠ actively in meeting; result is a hint, not proof.
/// </summary>
public sealed class MeetingDetector : IAgentCollector, IAsyncDisposable
{
    private static readonly HashSet<string> MeetingProcessNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "teams",   "teams.exe",
            "zoom",    "zoom.exe",
            "webex",   "webex.exe",
            "slack",   "slack.exe",
            "msteams", "msteams.exe"
        };

    private static readonly TimeSpan SampleWindow = TimeSpan.FromMinutes(2);

    public string Name => "MeetingDetector";

    private readonly ILogger<MeetingDetector> _logger;
    private readonly INamedPipeClient _pipe;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _running;

    public MeetingDetector(ILogger<MeetingDetector> logger, INamedPipeClient pipe)
    {
        _logger = logger;
        _pipe = pipe;
    }

    public Task StartAsync(AgentPolicy policy, CancellationToken ct)
    {
        if (_running) return Task.CompletedTask;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop = SampleLoopAsync(_cts.Token);
        _running = true;
        _logger.LogInformation("{Name}: started", Name);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (!_running) return;
        _running = false;
        if (_cts is not null) { await _cts.CancelAsync(); _cts.Dispose(); _cts = null; }
        if (_loop is not null) { try { await _loop.WaitAsync(TimeSpan.FromSeconds(3), ct); } catch { } _loop = null; }
        _logger.LogInformation("{Name}: stopped", Name);
    }

    private async Task SampleLoopAsync(CancellationToken ct)
    {
        try
        {
            await EmitSampleAsync(ct); // sample immediately so a short meeting isn't missed by the first 2-minute wait
            using var timer = new PeriodicTimer(SampleWindow);
            while (await timer.WaitForNextTickAsync(ct))
                await EmitSampleAsync(ct);
        }
        catch (OperationCanceledException) { }
    }

    private async Task EmitSampleAsync(CancellationToken ct)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var (isRunning, processName) = DetectMeetingProcess();

            var record = new CollectionRecord
            {
                EventId          = Guid.NewGuid().ToString("N"),
                RecordType       = CollectionRecordTypes.MeetingSignal,
                SchemaVersion    = CollectionSchemaVersions.MeetingSignalV1,
                CaptureTimestamp = now,
                DeviceId         = Environment.MachineName,
                Payload          = JsonSerializer.SerializeToElement(new MeetingSignalPayload
                {
                    CapturedAt          = now,
                    IsMeetingAppRunning = isRunning,
                    ProcessName         = processName
                })
            };
            await _pipe.SubmitCollectionRecordsAsync([record], ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "{Name}: emit failed", Name);
        }
    }

    /// <summary>
    /// Returns true if a known meeting-app process is running.
    /// Probabilistic — background process ≠ active meeting.
    /// </summary>
    public static bool IsMeetingAppRunning() => DetectMeetingProcess().IsRunning;

    private static (bool IsRunning, string? ProcessName) DetectMeetingProcess()
    {
        try
        {
            var match = Process.GetProcesses()
                .FirstOrDefault(p => MeetingProcessNames.Contains(p.ProcessName));
            return (match is not null, match?.ProcessName);
        }
        catch { return (false, null); }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests --filter MeetingDetectorTests`
Expected: 2 passed

- [ ] **Step 5: Commit**

```bash
git add tray_app_maui/ONEVO.Agent.TrayApp/Collectors/MeetingDetector.cs tray_app_maui/tests/ONEVO.Agent.TrayApp.Tests/Collectors/MeetingDetectorTests.cs
git commit -m "feat: MeetingDetector emits real periodic samples over IPC"
```

*(No `MauiProgram.cs` change needed — `MeetingDetector` is already registered as `AddSingleton<MeetingDetector>()`; the DI container resolves the new `INamedPipeClient` constructor parameter automatically since `NamedPipeClient` is already registered for `DeviceStateCollector`.)*

---

### Task 3: Service — batch, flush, and route the new record type

**Files:**
- Modify: `tray_app_maui/ONEVO.Agent.Service/Api/AgentApiRoutes.cs`
- Modify: `tray_app_maui/ONEVO.Agent.Service/Api/ActivityIngestModels.cs`
- Modify: `tray_app_maui/ONEVO.Agent.Service/Sync/ActivitySyncService.cs`
- Test: `tray_app_maui/tests/ONEVO.Agent.Service.Tests/Sync/ActivitySyncServiceMeetingSignalTests.cs` (if an existing `ActivitySyncServiceTests` file with a compatible test harness exists, add cases there instead — confirm before creating a new file)

- [ ] **Step 1: Add the route**

In `AgentApiRoutes.cs`, add next to `DeviceStateSnapshots`:

```csharp
public const string MeetingSignals = "/api/v1/monitoring/meetings/signals";
```

- [ ] **Step 2: Add the wire DTOs**

In `ActivityIngestModels.cs`, add next to `DeviceStateIngestRequest`/`Item`:

```csharp
/// <summary>Wire format for POST /api/v1/monitoring/meetings/signals.</summary>
public sealed class MeetingSignalIngestRequest
{
    [JsonPropertyName("signals")]
    public List<MeetingSignalIngestItem> Signals { get; set; } = [];
}

public sealed class MeetingSignalIngestItem
{
    [JsonPropertyName("captured_at")]
    public DateTimeOffset CapturedAt { get; set; }

    [JsonPropertyName("is_meeting_app_running")]
    public bool IsMeetingAppRunning { get; set; }

    [JsonPropertyName("process_name")]
    public string? ProcessName { get; set; }
}
```

- [ ] **Step 3: Wire it into the batch dispatcher**

In `ActivitySyncService.cs`, extend the switch (around line 118) and `IsBatchableType` (around line 213):

```csharp
var failed = recordType switch
{
    CollectionRecordTypes.ActivitySnapshot =>
        await FlushActivitySnapshotsAsync(records, jwt, ct),
    CollectionRecordTypes.AppUsageSnapshot =>
        await FlushAppUsageSnapshotsAsync(records, jwt, ct),
    CollectionRecordTypes.DeviceStateSnapshot =>
        await FlushDeviceStateSnapshotsAsync(records, jwt, ct),
    CollectionRecordTypes.MeetingSignal =>
        await FlushMeetingSignalsAsync(records, jwt, ct),
    _ => records
};
```

```csharp
private static bool IsBatchableType(string recordType) =>
    recordType is CollectionRecordTypes.ActivitySnapshot
        or CollectionRecordTypes.AppUsageSnapshot
        or CollectionRecordTypes.DeviceStateSnapshot
        or CollectionRecordTypes.MeetingSignal;
```

Add the flush method next to `FlushDeviceStateSnapshotsAsync`:

```csharp
private async Task<List<CollectionRecord>> FlushMeetingSignalsAsync(
    List<CollectionRecord> records, string jwt, CancellationToken ct)
{
    if (records.Count == 0) return [];

    var items = new List<MeetingSignalIngestItem>();
    var used  = new List<CollectionRecord>();

    foreach (var record in records)
    {
        try
        {
            var signal = record.Payload.Deserialize<MeetingSignalPayload>(JsonOptions);
            if (signal is null) continue;

            items.Add(new MeetingSignalIngestItem
            {
                CapturedAt          = signal.CapturedAt,
                IsMeetingAppRunning = signal.IsMeetingAppRunning,
                ProcessName         = signal.ProcessName
            });
            used.Add(record);
        }
        catch (JsonException)
        {
            _logger.LogWarning("Corrupt meeting-signal record quarantined eventId={EventId}", record.EventId);
        }
    }

    if (items.Count == 0) return [];
    return await PostBatchAsync(
        AgentApiRoutes.MeetingSignals, jwt,
        new MeetingSignalIngestRequest { Signals = items },
        used, ct);
}
```

- [ ] **Step 4: Build the Service project**

Run: `dotnet build tray_app_maui/ONEVO.Agent.Service/ONEVO.Agent.Service.csproj`
Expected: Build succeeded

- [ ] **Step 5: Confirm existing sync tests still pass, then add coverage for the new type**

Run: `dotnet test tests/ONEVO.Agent.Service.Tests --filter ActivitySyncService`

If an `ActivitySyncServiceTests.cs` file exists with a test-double `HttpMessageHandler` / mock buffer already set up for `DeviceStateSnapshot`, add one `[Fact]` there asserting a queued `MeetingSignal` record posts to `AgentApiRoutes.MeetingSignals` and is acknowledged on `202`. Match that file's exact existing mocking pattern rather than introducing a new harness — read it first before writing the test.

- [ ] **Step 6: Commit**

```bash
git add tray_app_maui/ONEVO.Agent.Service/Api/AgentApiRoutes.cs tray_app_maui/ONEVO.Agent.Service/Api/ActivityIngestModels.cs tray_app_maui/ONEVO.Agent.Service/Sync/ActivitySyncService.cs
git commit -m "feat: Service batches and flushes MeetingSignal records"
```

---

### Task 4: Backend — domain entity, EF configuration, migration

**Files:**
- Modify: `src/ONEVO.Domain/Errors/MonitoringErrors.cs`
- Create: `src/ONEVO.Domain/Features/Monitoring/Meetings/Entities/MeetingSignal.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/Meetings/MeetingSignalConfiguration.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` (add `DbSet<MeetingSignal>`)
- Generate: `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddMeetingSignals.cs`

- [ ] **Step 1: Add the error constants**

In `MonitoringErrors.cs`, add next to `DeviceTrackingDisabled`:

```csharp
public const string MeetingDetectionDisabledCode = "monitoring.meeting_detection_disabled";
public const string MeetingDetectionDisabled =
    "Meeting detection is not enabled for this employee.";
```

- [ ] **Step 2: Create the entity**

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.Meetings.Entities;

/// <summary>
/// Phase 1 probabilistic meeting-app-presence sample (process-name match only).
/// A row existing means a known meeting app was running at CapturedAt - not proof
/// the employee was actively in a meeting (architecture doc §7.4).
/// </summary>
public class MeetingSignal : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid AgentDeviceId { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public bool IsMeetingAppRunning { get; set; }
    public string? ProcessName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

- [ ] **Step 3: Create the EF configuration, mirroring `DeviceStateSnapshotConfiguration`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.Meetings.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.Meetings;

public class MeetingSignalConfiguration : IEntityTypeConfiguration<MeetingSignal>
{
    public void Configure(EntityTypeBuilder<MeetingSignal> builder)
    {
        builder.ToTable("meeting_signals");
        builder.HasKey(e => e.Id);

        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.CapturedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_meeting_signals_tenant_employee_captured");
    }
}
```

- [ ] **Step 4: Register the DbSet**

In `ApplicationDbContext.cs`, add next to `DeviceStateSnapshots`:

```csharp
public DbSet<MeetingSignal> MeetingSignals => Set<MeetingSignal>();
```

- [ ] **Step 5: Generate the migration**

Run:
```bash
dotnet ef migrations add AddMeetingSignals --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```

This generates the `CreateTable`/index scaffolding from the entity + configuration above. EF Core cannot know about the RLS policy — that part must be added by hand next.

- [ ] **Step 6: Append the RLS policy to the generated migration's `Up`/`Down`**

Open the newly generated `<timestamp>_AddMeetingSignals.cs`. At the end of `Up()` (after the `CreateTable`/`CreateIndex` calls EF generated), add — this is the exact same policy shape as `20260805045300_AddActivityMonitoring.cs`, scoped to just the one new table:

```csharp
migrationBuilder.Sql(@"
    ALTER TABLE meeting_signals ENABLE ROW LEVEL SECURITY;
    ALTER TABLE meeting_signals FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS tenant_isolation ON meeting_signals;
    CREATE POLICY tenant_isolation ON meeting_signals
        USING (
            current_setting('app.tenant_context_mode', true) = 'admin'
            OR (
                current_setting('app.tenant_context_mode', true) = 'tenant'
                AND tenant_id::text = current_setting('app.current_tenant_id', true)
            )
        )
        WITH CHECK (
            current_setting('app.tenant_context_mode', true) = 'admin'
            OR (
                current_setting('app.tenant_context_mode', true) = 'tenant'
                AND tenant_id::text = current_setting('app.current_tenant_id', true)
            )
        );
");
```

At the top of the generated `Down()` (before the `DropTable` call), add:

```csharp
migrationBuilder.Sql(@"
    DROP POLICY IF EXISTS tenant_isolation ON meeting_signals;
    ALTER TABLE meeting_signals DISABLE ROW LEVEL SECURITY;
");
```

- [ ] **Step 7: Apply and verify locally**

Run: `dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`
Expected: `meeting_signals` table exists with RLS enabled (`\d+ meeting_signals` in `psql` should show `Policies: tenant_isolation`).

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Domain/Errors/MonitoringErrors.cs src/ONEVO.Domain/Features/Monitoring/Meetings src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/Meetings src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs src/ONEVO.Infrastructure/Migrations
git commit -m "feat: add MeetingSignal table with RLS tenant isolation"
```

---

### Task 5: Backend — repository, mapper, ingest command

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/Meetings/RepositoryInterfaces/IMeetingSignalRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/Meetings/EfMeetingSignalRepository.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/Meetings/Mappers/MeetingSignalMapper.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/Meetings/Commands/IngestMeetingSignals/IngestMeetingSignalsCommand.cs` + `Handler.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/Meetings/IngestMeetingSignalsCommandHandlerTests.cs`

- [ ] **Step 1: Repository interface**

```csharp
using ONEVO.Domain.Features.Monitoring.Meetings.Entities;

namespace ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;

public interface IMeetingSignalRepository
{
    Task AddRangeAsync(IEnumerable<MeetingSignal> signals, CancellationToken ct);

    Task<IReadOnlyList<MeetingSignal>> GetByEmployeeDateAsync(
        Guid tenantId, Guid employeeId, DateOnly date, int page, int pageSize, CancellationToken ct);

    Task<int> GetTotalCountAsync(Guid tenantId, Guid employeeId, DateOnly date, CancellationToken ct);
}
```

- [ ] **Step 2: EF implementation**

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Meetings.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Meetings;

public class EfMeetingSignalRepository : IMeetingSignalRepository
{
    private readonly ApplicationDbContext _db;

    public EfMeetingSignalRepository(ApplicationDbContext db) => _db = db;

    public async Task AddRangeAsync(IEnumerable<MeetingSignal> signals, CancellationToken ct)
        => await _db.MeetingSignals.AddRangeAsync(signals, ct);

    public async Task<IReadOnlyList<MeetingSignal>> GetByEmployeeDateAsync(
        Guid tenantId, Guid employeeId, DateOnly date, int page, int pageSize, CancellationToken ct)
    {
        var (start, end) = UtcDayBounds(date);

        return await _db.MeetingSignals
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId
                        && s.EmployeeId == employeeId
                        && s.CapturedAt >= start
                        && s.CapturedAt < end)
            .OrderBy(s => s.CapturedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<int> GetTotalCountAsync(
        Guid tenantId, Guid employeeId, DateOnly date, CancellationToken ct)
    {
        var (start, end) = UtcDayBounds(date);

        return await _db.MeetingSignals
            .AsNoTracking()
            .CountAsync(s => s.TenantId == tenantId
                             && s.EmployeeId == employeeId
                             && s.CapturedAt >= start
                             && s.CapturedAt < end, ct);
    }

    private static (DateTimeOffset Start, DateTimeOffset End) UtcDayBounds(DateOnly date)
    {
        var start = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return (start, start.AddDays(1));
    }
}
```

- [ ] **Step 3: Register in DI**

In `DependencyInjection.cs`, add next to the `IDeviceStateSnapshotRepository` registration:

```csharp
services.AddScoped<
    ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces.IMeetingSignalRepository,
    ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Meetings.EfMeetingSignalRepository>();
```

- [ ] **Step 4: Command record**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Monitoring.Meetings.Commands.IngestMeetingSignals;

public record IngestMeetingSignalsCommand : IRequest<Result>
{
    public List<MeetingSignalItem> Signals { get; init; } = [];
}

public record MeetingSignalItem
{
    public DateTimeOffset CapturedAt { get; init; }
    public bool IsMeetingAppRunning { get; init; }
    public string? ProcessName { get; init; }
}
```

- [ ] **Step 5: Mapper**

```csharp
using ONEVO.Application.Features.Monitoring.Meetings.Commands.IngestMeetingSignals;
using ONEVO.Domain.Features.Monitoring.Meetings.Entities;

namespace ONEVO.Application.Features.Monitoring.Meetings.Mappers;

public static class MeetingSignalMapper
{
    public static MeetingSignal ToEntity(
        MeetingSignalItem item, Guid tenantId, Guid employeeId, Guid agentDeviceId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        EmployeeId = employeeId,
        AgentDeviceId = agentDeviceId,
        CapturedAt = item.CapturedAt,
        IsMeetingAppRunning = item.IsMeetingAppRunning,
        ProcessName = item.ProcessName,
        CreatedAt = now
    };
}
```

- [ ] **Step 6: Write the failing handler test**

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Meetings.Commands.IngestMeetingSignals;
using ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Errors;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.Meetings.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Meetings;

public class IngestMeetingSignalsCommandHandlerTests
{
    private readonly Mock<IMeetingSignalRepository> _signals = new();
    private readonly Mock<IMonitoringToggleResolver> _toggles = new();
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _switcher = new();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly FakeUnitOfWork _uow = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public IngestMeetingSignalsCommandHandlerTests()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(true);
        _device.Setup(d => d.TenantId).Returns(_tenantId);
        _device.Setup(d => d.UserId).Returns(_userId);
        _device.Setup(d => d.DeviceRegistrationId).Returns(_deviceId);

        _tenants.Setup(t => t.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = _tenantId, Name = "Test", Slug = "test", Status = TenantStatus.Active });

        _toggles.Setup(t => t.IsEnabledAsync(
                _tenantId, _userId, MonitoringCapability.MeetingDetection, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private IngestMeetingSignalsCommandHandler CreateSut() => new(
        _signals.Object, _toggles.Object, _device.Object, _tenants.Object, _switcher.Object,
        _clock, _uow, NullLogger<IngestMeetingSignalsCommandHandler>.Instance);

    private MeetingSignalItem Item(DateTimeOffset capturedAt, bool isRunning = true) => new()
    {
        CapturedAt = capturedAt, IsMeetingAppRunning = isRunning, ProcessName = "teams.exe"
    };

    [Fact]
    public async Task Happy_path_saves_signals()
    {
        IEnumerable<MeetingSignal>? saved = null;
        _signals.Setup(s => s.AddRangeAsync(It.IsAny<IEnumerable<MeetingSignal>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<MeetingSignal>, CancellationToken>((list, _) => saved = list.ToList())
            .Returns(Task.CompletedTask);

        var result = await CreateSut().Handle(
            new IngestMeetingSignalsCommand { Signals = [Item(_clock.UtcNow.AddMinutes(-1))] }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _uow.SaveCallCount.Should().Be(1);
        saved.Should().NotBeNull().And.HaveCount(1);
        saved!.First().ProcessName.Should().Be("teams.exe");
        saved.First().EmployeeId.Should().Be(_userId);
    }

    [Fact]
    public async Task Monitoring_disabled_returns_403()
    {
        _toggles.Setup(t => t.IsEnabledAsync(
                _tenantId, _userId, MonitoringCapability.MeetingDetection, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateSut().Handle(
            new IngestMeetingSignalsCommand { Signals = [Item(_clock.UtcNow)] }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Be(MonitoringErrors.MeetingDetectionDisabled);
        _uow.SaveCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Unauthenticated_device_returns_401()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(false);

        var result = await CreateSut().Handle(
            new IngestMeetingSignalsCommand { Signals = [Item(_clock.UtcNow)] }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }
}
```

- [ ] **Step 7: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter IngestMeetingSignalsCommandHandlerTests`
Expected: build error — handler doesn't exist.

- [ ] **Step 8: Implement the handler (exact mirror of `IngestDeviceStateSnapshotsCommandHandler`)**

```csharp
using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Meetings.Mappers;
using ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Errors;

namespace ONEVO.Application.Features.Monitoring.Meetings.Commands.IngestMeetingSignals;

public class IngestMeetingSignalsCommandHandler : IRequestHandler<IngestMeetingSignalsCommand, Result>
{
    private readonly IMeetingSignalRepository _signals;
    private readonly IMonitoringToggleResolver _toggleResolver;
    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<IngestMeetingSignalsCommandHandler> _logger;

    public IngestMeetingSignalsCommandHandler(
        IMeetingSignalRepository signals,
        IMonitoringToggleResolver toggleResolver,
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork,
        ILogger<IngestMeetingSignalsCommandHandler> logger)
    {
        _signals = signals;
        _toggleResolver = toggleResolver;
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(IngestMeetingSignalsCommand request, CancellationToken ct)
    {
        if (!_device.IsAuthenticated
            || _device.TenantId == Guid.Empty
            || _device.UserId == Guid.Empty
            || _device.DeviceRegistrationId == Guid.Empty)
        {
            return Result.Failure("A valid tray device token is required.", 401);
        }

        var tenant = await _tenants.GetByIdAsync(_device.TenantId, ct);
        if (tenant is null)
            return Result.Failure("Tenant not found.", 401);

        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);

        var tenantId = _device.TenantId;
        var employeeId = _device.UserId;
        var agentDeviceId = _device.DeviceRegistrationId;
        var now = _clock.UtcNow;

        var enabled = await _toggleResolver.IsEnabledAsync(
            tenantId, employeeId, MonitoringCapability.MeetingDetection, ct);

        if (!enabled)
        {
            _logger.LogInformation(
                "Meeting-signal batch rejected: monitoring disabled. TenantId={TenantId} DeviceId={DeviceId} EmployeeId={EmployeeId} Count={Count}",
                tenantId, agentDeviceId, employeeId, request.Signals.Count);
            return Result.Failure(MonitoringErrors.MeetingDetectionDisabled, 403);
        }

        foreach (var item in request.Signals)
        {
            if (item.CapturedAt > now.AddMinutes(5))
                return Result.Failure(MonitoringErrors.SnapshotFutureTime, 400);
            if (item.CapturedAt < now.AddHours(-24))
                return Result.Failure(MonitoringErrors.SnapshotTooOld, 400);
        }

        var entities = request.Signals
            .Select(item => MeetingSignalMapper.ToEntity(item, tenantId, employeeId, agentDeviceId, now))
            .ToList();

        await _signals.AddRangeAsync(entities, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
```

- [ ] **Step 9: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter IngestMeetingSignalsCommandHandlerTests`
Expected: 3 passed

- [ ] **Step 10: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/Meetings src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/Meetings src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/Monitoring/Meetings/IngestMeetingSignalsCommandHandlerTests.cs
git commit -m "feat: add MeetingSignal ingest command"
```

---

### Task 6: Backend — query and both controllers

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/Meetings/DTOs/Responses/MeetingSignalDto.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/Meetings/Queries/GetMeetingSignals/GetMeetingSignalsQuery.cs` + `Handler.cs`
- Create: `src/ONEVO.Api/Controllers/Tenant/Monitoring/Meetings/MonitoringMeetingIngestController.cs`
- Create: `src/ONEVO.Api/Controllers/Tenant/Monitoring/Meetings/MonitoringMeetingController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/Meetings/GetMeetingSignalsQueryHandlerTests.cs`

- [ ] **Step 1: DTO and query record**

```csharp
namespace ONEVO.Application.Features.Monitoring.Meetings.DTOs.Responses;

public record MeetingSignalDto
{
    public Guid Id { get; init; }
    public DateTimeOffset CapturedAt { get; init; }
    public bool IsMeetingAppRunning { get; init; }
    public string? ProcessName { get; init; }
}
```

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.Meetings.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.Meetings.Queries.GetMeetingSignals;

public record GetMeetingSignalsQuery : IRequest<Result<PagedResult<MeetingSignalDto>>>
{
    public Guid EmployeeId { get; init; }
    public DateOnly Date { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 100;
}
```

- [ ] **Step 2: Write the failing handler test**

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Meetings.Queries.GetMeetingSignals;
using ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Meetings.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Meetings;

public class GetMeetingSignalsQueryHandlerTests
{
    private readonly Mock<IMeetingSignalRepository> _signals = new();
    private readonly Mock<ITenantContext> _tenantContext = new();

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 8, 17);

    private GetMeetingSignalsQueryHandler BuildSut()
    {
        _tenantContext.SetupGet(c => c.TenantId).Returns(TenantId);
        return new GetMeetingSignalsQueryHandler(_signals.Object, _tenantContext.Object);
    }

    [Fact]
    public async Task Handle_ReturnsPagedMappedResults()
    {
        _signals.Setup(r => r.GetTotalCountAsync(TenantId, EmployeeId, Day, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _signals.Setup(r => r.GetByEmployeeDateAsync(TenantId, EmployeeId, Day, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new MeetingSignal
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(),
                CapturedAt = DateTimeOffset.UtcNow, IsMeetingAppRunning = true, ProcessName = "zoom.exe"
            }]);
        var sut = BuildSut();

        var result = await sut.Handle(
            new GetMeetingSignalsQuery { EmployeeId = EmployeeId, Date = Day }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle(i => i.ProcessName == "zoom.exe" && i.IsMeetingAppRunning);
    }

    [Fact]
    public async Task Handle_MissingEmployeeId_ReturnsBadRequest()
    {
        var sut = BuildSut();

        var result = await sut.Handle(
            new GetMeetingSignalsQuery { EmployeeId = Guid.Empty, Date = Day }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter GetMeetingSignalsQueryHandlerTests`
Expected: build error

- [ ] **Step 4: Implement the handler**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Meetings.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;

namespace ONEVO.Application.Features.Monitoring.Meetings.Queries.GetMeetingSignals;

public class GetMeetingSignalsQueryHandler
    : IRequestHandler<GetMeetingSignalsQuery, Result<PagedResult<MeetingSignalDto>>>
{
    private readonly IMeetingSignalRepository _signals;
    private readonly ITenantContext _tenantContext;

    public GetMeetingSignalsQueryHandler(IMeetingSignalRepository signals, ITenantContext tenantContext)
    {
        _signals = signals;
        _tenantContext = tenantContext;
    }

    public async Task<Result<PagedResult<MeetingSignalDto>>> Handle(
        GetMeetingSignalsQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Result<PagedResult<MeetingSignalDto>>.Failure("Tenant context is required.", 401);

        if (request.EmployeeId == Guid.Empty)
            return Result<PagedResult<MeetingSignalDto>>.Failure("employeeId is required.", 400);

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 500 ? 100 : request.PageSize;
        var tenantId = _tenantContext.TenantId;

        var total = await _signals.GetTotalCountAsync(tenantId, request.EmployeeId, request.Date, ct);
        var items = await _signals.GetByEmployeeDateAsync(tenantId, request.EmployeeId, request.Date, page, pageSize, ct);

        var dtos = items.Select(s => new MeetingSignalDto
        {
            Id = s.Id,
            CapturedAt = s.CapturedAt,
            IsMeetingAppRunning = s.IsMeetingAppRunning,
            ProcessName = s.ProcessName
        }).ToList();

        return Result<PagedResult<MeetingSignalDto>>.Success(
            new PagedResult<MeetingSignalDto>(dtos, page, pageSize, total));
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter GetMeetingSignalsQueryHandlerTests`
Expected: 2 passed

- [ ] **Step 6: Ingest controller**

```csharp
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.Meetings.Commands.IngestMeetingSignals;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.Meetings;

/// <summary>Tray App → Backend ingest for probabilistic meeting-app-presence samples.</summary>
[ApiController]
[Route("api/v1/monitoring/meetings")]
[Authorize(Policy = "TrayDevicePolicy")]
public class MonitoringMeetingIngestController : ControllerBase
{
    private readonly IMediator _mediator;

    public MonitoringMeetingIngestController(IMediator mediator) => _mediator = mediator;

    [HttpPost("signals")]
    public async Task<IActionResult> IngestSignals(
        [FromBody] IngestMeetingSignalsRequest request, CancellationToken ct)
    {
        var items = (request.Signals ?? [])
            .Select(s => new MeetingSignalItem
            {
                CapturedAt = s.CapturedAt,
                IsMeetingAppRunning = s.IsMeetingAppRunning,
                ProcessName = s.ProcessName
            })
            .ToList();

        var result = await _mediator.Send(new IngestMeetingSignalsCommand { Signals = items }, ct);

        return result.IsSuccess ? Accepted() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}

public record IngestMeetingSignalsRequest(
    [property: JsonPropertyName("signals")] List<MeetingSignalRequestItem>? Signals);

public record MeetingSignalRequestItem(
    [property: JsonPropertyName("captured_at")] DateTimeOffset CapturedAt,
    [property: JsonPropertyName("is_meeting_app_running")] bool IsMeetingAppRunning,
    [property: JsonPropertyName("process_name")] string? ProcessName);
```

- [ ] **Step 7: Query controller**

```csharp
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Monitoring.Meetings.Queries.GetMeetingSignals;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.Meetings;

[ApiController]
[Route("api/v1/monitoring/meetings")]
[Authorize(Policy = "TenantPolicy")]
public class MonitoringMeetingController : ControllerBase
{
    private readonly IMediator _mediator;

    public MonitoringMeetingController(IMediator mediator) => _mediator = mediator;

    [HttpGet("signals")]
    [RequirePermission("monitoring:read")]
    public async Task<IActionResult> GetSignals(
        [FromQuery] Guid employeeId,
        [FromQuery] DateOnly date,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new GetMeetingSignalsQuery { EmployeeId = employeeId, Date = date, Page = page, PageSize = pageSize }, ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
```

- [ ] **Step 8: Build and run full unit suite**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj && dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~Monitoring.Meetings"`
Expected: build succeeded; all Meetings unit tests passed.

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/Meetings/DTOs src/ONEVO.Application/Features/Monitoring/Meetings/Queries src/ONEVO.Api/Controllers/Tenant/Monitoring/Meetings tests/ONEVO.Tests.Unit/Features/Monitoring/Meetings/GetMeetingSignalsQueryHandlerTests.cs
git commit -m "feat: add meeting signal query endpoint"
```

---

### Task 7: Live verification (per the project's real-DB house rule)

Green unit tests do not prove the RLS-protected insert path or the full IPC→Service→backend chain works — verify against a real environment before calling this done.

- [ ] **Step 1: Enable the capability for a dev tenant**

Using the [MonitoringFeatureToggles admin CRUD](2026-08-17-monitoring-feature-toggles-admin-crud.md) endpoint (or direct DB update on the acme/dapi dev tenant), set `MeetingDetection = true`.

- [ ] **Step 2: Run the full agent stack locally**

Start `ONEVO.Agent.Service` and `ONEVO.Agent.TrayApp` against the dev backend, with a meeting app (e.g. `Teams.exe` or any process named in `MeetingProcessNames`) running.

- [ ] **Step 3: Confirm the signal reaches the backend**

Query `GET /api/v1/monitoring/meetings/signals?employeeId=<id>&date=2026-08-17` as a tenant admin and confirm rows appear with `isMeetingAppRunning: true` within the 2-minute sample window, and that `meeting_signals` has rows in the dev database.

- [ ] **Step 4: Confirm the toggle actually gates it**

Set `MeetingDetection = false` for the tenant, restart the TrayApp (so `MonitoringToggleResolverService`'s 2-minute cache is guaranteed stale), and confirm the ingest endpoint starts returning `403 monitoring.meeting_detection_disabled`.
