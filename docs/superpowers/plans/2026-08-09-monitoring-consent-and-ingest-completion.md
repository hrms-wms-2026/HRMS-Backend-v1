# Monitoring Consent Accuracy & Missing Ingest Endpoints — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every item the Tray App's "Allow Required Policies" consent screen implies is being collected actually reach `agent_activity.db` and the backend PostgreSQL database, and make the screen itself stop lying about what's on/off.

**Architecture:** Two of the six consent items (App Usage, Device State/idle) already collect real data on the Tray App side and queue it in the local SQLite buffer via `ActivitySyncService`, but the backend has no controller listening on their POST routes, so every batch 404s and re-queues forever. This plan adds the missing backend ingest features (mirroring the already-shipped `ActivitySnapshot` feature exactly — same CQRS shape, same tenant/device auth, same raw-buffer-then-normalize pattern). Separately, the consent screen's six switches currently show a mix of hardcoded values and live policy — this plan makes all of them reflect the real `AgentPolicy` from the backend, removes the fake "Location Access" item (no collector, no policy field, nothing sent — Phase 1 already covers location via the one-time office-pin at clock-in), and fixes `NotificationService` from being a logger-only stub into a real Windows notification.

**Tech Stack:** ASP.NET Core 10 + MediatR + FluentValidation + EF Core/PostgreSQL (backend), .NET MAUI Windows + CommunityToolkit.Mvvm (Tray App), xUnit + Moq + FluentAssertions (backend tests), xUnit (Tray App tests).

**Decisions already made with the user (do not re-litigate):**
1. Location Access is **removed** from the consent screen — no new collector, no backend work for it.
2. All six switches become **locked, display-only**, reflecting the real `AgentPolicy` from the backend (matching the footer text "Permissions are managed according to your company policy").

**Known out-of-scope gap (noted, not fixed here):** `DeviceStateCollector.StartAsync` ([DeviceStateCollector.cs:30-38](C:/HR/tray_app_maui/ONEVO.Agent.TrayApp/Collectors/DeviceStateCollector.cs)) never checks an `AgentPolicy` field before starting — there is no `DeviceStateEnabled` field in `AgentPolicy` at all. Adding one touches the tray activation/policy-sync path on both repos and is a separate, bigger change. Flagged here so it isn't forgotten; not a task in this plan.

---

## File Structure

### Backend (`C:\HR\HRMS-Backend-v1`) — two new features, both mirroring the existing `Monitoring/ActivityMonitoring` feature

```
src/ONEVO.Domain/Features/Monitoring/AppUsage/Entities/
  AppUsageSnapshot.cs                          ← Create
src/ONEVO.Domain/Features/Monitoring/DeviceState/Entities/
  DeviceStateSnapshot.cs                       ← Create

src/ONEVO.Application/Features/Monitoring/AppUsage/
  Commands/IngestAppUsageSnapshots/
    IngestAppUsageSnapshotsCommand.cs          ← Create
    IngestAppUsageSnapshotsCommandHandler.cs   ← Create
    IngestAppUsageSnapshotsCommandValidator.cs ← Create
  Mappers/AppUsageSnapshotMapper.cs             ← Create
  RepositoryInterfaces/IAppUsageSnapshotRepository.cs ← Create
src/ONEVO.Application/Features/Monitoring/DeviceState/
  Commands/IngestDeviceStateSnapshots/
    IngestDeviceStateSnapshotsCommand.cs          ← Create
    IngestDeviceStateSnapshotsCommandHandler.cs   ← Create
    IngestDeviceStateSnapshotsCommandValidator.cs ← Create
  Mappers/DeviceStateSnapshotMapper.cs             ← Create
  RepositoryInterfaces/IDeviceStateSnapshotRepository.cs ← Create

src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/AppUsage/
  AppUsageSnapshotConfiguration.cs             ← Create
src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/DeviceState/
  DeviceStateSnapshotConfiguration.cs          ← Create
src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/AppUsage/
  EfAppUsageSnapshotRepository.cs              ← Create
src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/DeviceState/
  EfDeviceStateSnapshotRepository.cs           ← Create
src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs   ← Modify (add 2 DbSets)
src/ONEVO.Infrastructure/DependencyInjection.cs                ← Modify (register 2 repos)
src/ONEVO.Infrastructure/Migrations/                            ← Generated (1 migration)

src/ONEVO.Domain/Errors/MonitoringErrors.cs   ← Modify (add 2 disabled-capability messages)

src/ONEVO.Api/Controllers/Tenant/Monitoring/AppUsage/
  MonitoringAppUsageIngestController.cs        ← Create
src/ONEVO.Api/Controllers/Tenant/Monitoring/DeviceState/
  MonitoringDeviceStateIngestController.cs     ← Create

tests/ONEVO.Tests.Unit/Features/Monitoring/AppUsage/
  IngestAppUsageSnapshotsCommandHandlerTests.cs    ← Create
  IngestAppUsageSnapshotsCommandValidatorTests.cs  ← Create
tests/ONEVO.Tests.Unit/Features/Monitoring/DeviceState/
  IngestDeviceStateSnapshotsCommandHandlerTests.cs    ← Create
  IngestDeviceStateSnapshotsCommandValidatorTests.cs  ← Create
```

### Tray App (`C:\HR\tray_app_maui`)

```
ONEVO.Agent.TrayApp/Views/PrivacyConsentPage.xaml            ← Modify (remove Location row, lock all switches)
ONEVO.Agent.TrayApp/ViewModels/PrivacyConsentViewModel.cs     ← Modify (accurate policy binding, drop Location)
ONEVO.Agent.TrayApp/Services/NotificationService.cs           ← Modify (real toast, not just logging)
ONEVO.Agent.TrayApp/Platforms/Windows/App.xaml.cs              ← Modify (register AppNotificationManager)
tests/ONEVO.Agent.TrayApp.Tests/ViewModels/PrivacyConsentViewModelTests.cs ← Modify
```

---

## Task 1: Backend — App Usage domain entity + EF configuration

**Files:**
- Create: `src/ONEVO.Domain/Features/Monitoring/AppUsage/Entities/AppUsageSnapshot.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/AppUsage/AppUsageSnapshotConfiguration.cs`

- [ ] **Step 1: Create the domain entity**

```csharp
// src/ONEVO.Domain/Features/Monitoring/AppUsage/Entities/AppUsageSnapshot.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.AppUsage.Entities;

/// <summary>
/// Foreground-application usage sample. Window title is stored as a hash only —
/// raw title text is never sent by the tray agent or persisted here (§8.3).
/// </summary>
public class AppUsageSnapshot : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid AgentDeviceId { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public string? ProcessName { get; set; }
    public string? WindowTitleHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

- [ ] **Step 2: Create the EF configuration**

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/AppUsage/AppUsageSnapshotConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.AppUsage.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.AppUsage;

public class AppUsageSnapshotConfiguration : IEntityTypeConfiguration<AppUsageSnapshot>
{
    public void Configure(EntityTypeBuilder<AppUsageSnapshot> builder)
    {
        builder.ToTable("app_usage_snapshots");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ProcessName).HasMaxLength(100);
        builder.Property(e => e.WindowTitleHash).HasMaxLength(128);

        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.CapturedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_app_usage_snapshots_tenant_employee_captured");

        builder.HasIndex(e => new { e.TenantId, e.AgentDeviceId, e.CapturedAt })
            .HasDatabaseName("ix_app_usage_snapshots_tenant_device_captured");
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add src/ONEVO.Domain/Features/Monitoring/AppUsage src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/AppUsage
git commit -m "feat: add AppUsageSnapshot domain entity and EF configuration"
```

---

## Task 2: Backend — App Usage application layer (command, validator, mapper, repository interface)

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/AppUsage/Commands/IngestAppUsageSnapshots/IngestAppUsageSnapshotsCommand.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/AppUsage/Commands/IngestAppUsageSnapshots/IngestAppUsageSnapshotsCommandValidator.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/AppUsage/Commands/IngestAppUsageSnapshots/IngestAppUsageSnapshotsCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/AppUsage/Mappers/AppUsageSnapshotMapper.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/AppUsage/RepositoryInterfaces/IAppUsageSnapshotRepository.cs`
- Modify: `src/ONEVO.Domain/Errors/MonitoringErrors.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/AppUsage/IngestAppUsageSnapshotsCommandValidatorTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/AppUsage/IngestAppUsageSnapshotsCommandHandlerTests.cs`

- [ ] **Step 1: Add the two error messages this feature needs**

Add to `src/ONEVO.Domain/Errors/MonitoringErrors.cs`, inside the existing `MonitoringErrors` static class (after `ScreenshotCapabilityDisabled`, line 27):

```csharp
    public const string AppTrackingDisabledCode = "monitoring.app_tracking_disabled";
    public const string AppTrackingDisabled =
        "Application tracking is not enabled for this employee.";

    public const string DeviceTrackingDisabledCode = "monitoring.device_tracking_disabled";
    public const string DeviceTrackingDisabled =
        "Device state tracking is not enabled for this employee.";
```

- [ ] **Step 2: Write the command + validator (failing — no handler yet)**

```csharp
// src/ONEVO.Application/Features/Monitoring/AppUsage/Commands/IngestAppUsageSnapshots/IngestAppUsageSnapshotsCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Monitoring.AppUsage.Commands.IngestAppUsageSnapshots;

public record IngestAppUsageSnapshotsCommand : IRequest<Result>
{
    public List<AppUsageSnapshotItem> Snapshots { get; init; } = [];
}

public record AppUsageSnapshotItem
{
    public DateTimeOffset CapturedAt { get; init; }
    public string? ProcessName { get; init; }
    public string? WindowTitleHash { get; init; }
}
```

```csharp
// src/ONEVO.Application/Features/Monitoring/AppUsage/Commands/IngestAppUsageSnapshots/IngestAppUsageSnapshotsCommandValidator.cs
using FluentValidation;

namespace ONEVO.Application.Features.Monitoring.AppUsage.Commands.IngestAppUsageSnapshots;

public class IngestAppUsageSnapshotsCommandValidator : AbstractValidator<IngestAppUsageSnapshotsCommand>
{
    public const int MaxBatchSize = 200;
    public const int MaxProcessNameLength = 100;
    public const int MaxHashLength = 128;

    public IngestAppUsageSnapshotsCommandValidator()
    {
        RuleFor(x => x.Snapshots)
            .NotEmpty()
            .WithMessage("At least one snapshot is required.")
            .Must(s => s.Count <= MaxBatchSize)
            .WithMessage($"Batch cannot exceed {MaxBatchSize} snapshots.");

        RuleForEach(x => x.Snapshots).ChildRules(item =>
        {
            item.RuleFor(s => s.ProcessName)
                .MaximumLength(MaxProcessNameLength)
                .When(s => s.ProcessName is not null)
                .WithMessage($"ProcessName must not exceed {MaxProcessNameLength} characters.");

            item.RuleFor(s => s.ProcessName)
                .Must(name => name is null || (!name.Contains('\\') && !name.Contains('/') && !name.Contains(':')))
                .WithMessage("ProcessName must not contain path separators.");

            item.RuleFor(s => s.WindowTitleHash)
                .MaximumLength(MaxHashLength)
                .When(s => s.WindowTitleHash is not null)
                .WithMessage($"WindowTitleHash must not exceed {MaxHashLength} characters.");
        });
    }
}
```

```csharp
// tests/ONEVO.Tests.Unit/Features/Monitoring/AppUsage/IngestAppUsageSnapshotsCommandValidatorTests.cs
using FluentAssertions;
using ONEVO.Application.Features.Monitoring.AppUsage.Commands.IngestAppUsageSnapshots;

namespace ONEVO.Tests.Unit.Features.Monitoring.AppUsage;

public class IngestAppUsageSnapshotsCommandValidatorTests
{
    private readonly IngestAppUsageSnapshotsCommandValidator _sut = new();

    private static AppUsageSnapshotItem Item() => new()
    {
        CapturedAt = DateTimeOffset.UtcNow,
        ProcessName = "code.exe",
        WindowTitleHash = "abc123"
    };

    [Fact]
    public void Empty_snapshots_fails()
    {
        var result = _sut.Validate(new IngestAppUsageSnapshotsCommand { Snapshots = [] });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Valid_single_snapshot_passes()
    {
        var result = _sut.Validate(new IngestAppUsageSnapshotsCommand { Snapshots = [Item()] });
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ProcessName_with_path_separator_fails()
    {
        var item = Item() with { ProcessName = "C:\\evil.exe" };
        var result = _sut.Validate(new IngestAppUsageSnapshotsCommand { Snapshots = [item] });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Batch_over_200_fails()
    {
        var items = Enumerable.Range(0, 201).Select(_ => Item()).ToList();
        var result = _sut.Validate(new IngestAppUsageSnapshotsCommand { Snapshots = items });
        result.IsValid.Should().BeFalse();
    }
}
```

- [ ] **Step 3: Run the validator tests to confirm they pass on their own**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~IngestAppUsageSnapshotsCommandValidatorTests"`
Expected: 4 passed (validator has no dependency on the handler, so this compiles and passes immediately).

- [ ] **Step 4: Write the mapper and repository interface**

```csharp
// src/ONEVO.Application/Features/Monitoring/AppUsage/Mappers/AppUsageSnapshotMapper.cs
using ONEVO.Application.Features.Monitoring.AppUsage.Commands.IngestAppUsageSnapshots;
using ONEVO.Domain.Features.Monitoring.AppUsage.Entities;

namespace ONEVO.Application.Features.Monitoring.AppUsage.Mappers;

public static class AppUsageSnapshotMapper
{
    public static AppUsageSnapshot ToEntity(
        AppUsageSnapshotItem item,
        Guid tenantId,
        Guid employeeId,
        Guid agentDeviceId,
        DateTimeOffset createdAt)
    {
        return new AppUsageSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            AgentDeviceId = agentDeviceId,
            CapturedAt = item.CapturedAt,
            ProcessName = item.ProcessName,
            WindowTitleHash = item.WindowTitleHash,
            CreatedAt = createdAt
        };
    }
}
```

```csharp
// src/ONEVO.Application/Features/Monitoring/AppUsage/RepositoryInterfaces/IAppUsageSnapshotRepository.cs
using ONEVO.Domain.Features.Monitoring.AppUsage.Entities;

namespace ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;

public interface IAppUsageSnapshotRepository
{
    Task AddRangeAsync(IEnumerable<AppUsageSnapshot> snapshots, CancellationToken ct);
}
```

- [ ] **Step 5: Write the handler (mirrors `IngestActivitySnapshotsCommandHandler` exactly, capability = `ApplicationTracking`, no raw buffer)**

```csharp
// src/ONEVO.Application/Features/Monitoring/AppUsage/Commands/IngestAppUsageSnapshots/IngestAppUsageSnapshotsCommandHandler.cs
using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.AppUsage.Mappers;
using ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Domain.Errors;

namespace ONEVO.Application.Features.Monitoring.AppUsage.Commands.IngestAppUsageSnapshots;

public class IngestAppUsageSnapshotsCommandHandler
    : IRequestHandler<IngestAppUsageSnapshotsCommand, Result>
{
    private readonly IAppUsageSnapshotRepository _snapshots;
    private readonly IMonitoringToggleResolver _toggleResolver;
    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<IngestAppUsageSnapshotsCommandHandler> _logger;

    public IngestAppUsageSnapshotsCommandHandler(
        IAppUsageSnapshotRepository snapshots,
        IMonitoringToggleResolver toggleResolver,
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork,
        ILogger<IngestAppUsageSnapshotsCommandHandler> logger)
    {
        _snapshots = snapshots;
        _toggleResolver = toggleResolver;
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(
        IngestAppUsageSnapshotsCommand request,
        CancellationToken cancellationToken)
    {
        if (!_device.IsAuthenticated
            || _device.TenantId == Guid.Empty
            || _device.UserId == Guid.Empty
            || _device.DeviceRegistrationId == Guid.Empty)
        {
            return Result.Failure("A valid tray device token is required.", 401);
        }

        var tenant = await _tenants.GetByIdAsync(_device.TenantId, cancellationToken);
        if (tenant is null)
            return Result.Failure("Tenant not found.", 401);

        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null),
            cancellationToken);

        var tenantId = _device.TenantId;
        var employeeId = _device.UserId;
        var agentDeviceId = _device.DeviceRegistrationId;
        var now = _clock.UtcNow;

        var enabled = await _toggleResolver.IsEnabledAsync(
            tenantId, employeeId, MonitoringCapability.ApplicationTracking, cancellationToken);

        if (!enabled)
        {
            _logger.LogInformation(
                "App-usage snapshot batch rejected: monitoring disabled. TenantId={TenantId} DeviceId={DeviceId} EmployeeId={EmployeeId} Count={Count}",
                tenantId, agentDeviceId, employeeId, request.Snapshots.Count);
            return Result.Failure(MonitoringErrors.AppTrackingDisabled, 403);
        }

        foreach (var item in request.Snapshots)
        {
            if (item.CapturedAt > now.AddMinutes(5))
                return Result.Failure(MonitoringErrors.SnapshotFutureTime, 400);

            if (item.CapturedAt < now.AddHours(-24))
                return Result.Failure(MonitoringErrors.SnapshotTooOld, 400);
        }

        _logger.LogInformation(
            "App-usage snapshot batch received. TenantId={TenantId} DeviceId={DeviceId} EmployeeId={EmployeeId} Count={Count}",
            tenantId, agentDeviceId, employeeId, request.Snapshots.Count);

        var entities = request.Snapshots
            .Select(item => AppUsageSnapshotMapper.ToEntity(item, tenantId, employeeId, agentDeviceId, now))
            .ToList();

        await _snapshots.AddRangeAsync(entities, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
```

- [ ] **Step 6: Write the handler tests (mirrors `IngestActivitySnapshotsCommandHandlerTests`, no raw-buffer mock needed)**

```csharp
// tests/ONEVO.Tests.Unit/Features/Monitoring/AppUsage/IngestAppUsageSnapshotsCommandHandlerTests.cs
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.AppUsage.Commands.IngestAppUsageSnapshots;
using ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Domain.Errors;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.AppUsage.Entities;
using ONEVO.Tests.Unit.Fakes;

namespace ONEVO.Tests.Unit.Features.Monitoring.AppUsage;

public class IngestAppUsageSnapshotsCommandHandlerTests
{
    private readonly Mock<IAppUsageSnapshotRepository> _snapshots = new();
    private readonly Mock<IMonitoringToggleResolver> _toggles = new();
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _switcher = new();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly FakeUnitOfWork _uow = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public IngestAppUsageSnapshotsCommandHandlerTests()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(true);
        _device.Setup(d => d.TenantId).Returns(_tenantId);
        _device.Setup(d => d.UserId).Returns(_userId);
        _device.Setup(d => d.DeviceRegistrationId).Returns(_deviceId);

        _tenants.Setup(t => t.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant
            {
                Id = _tenantId,
                Name = "Test",
                Slug = "test",
                Status = TenantStatus.Active
            });

        _toggles.Setup(t => t.IsEnabledAsync(
                _tenantId, _userId, MonitoringCapability.ApplicationTracking, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private IngestAppUsageSnapshotsCommandHandler CreateSut() => new(
        _snapshots.Object,
        _toggles.Object,
        _device.Object,
        _tenants.Object,
        _switcher.Object,
        _clock,
        _uow,
        NullLogger<IngestAppUsageSnapshotsCommandHandler>.Instance);

    private static AppUsageSnapshotItem Item(DateTimeOffset capturedAt) => new()
    {
        CapturedAt = capturedAt,
        ProcessName = "code.exe",
        WindowTitleHash = "abc123"
    };

    [Fact]
    public async Task Happy_path_saves_snapshots()
    {
        IEnumerable<AppUsageSnapshot>? saved = null;
        _snapshots.Setup(s => s.AddRangeAsync(It.IsAny<IEnumerable<AppUsageSnapshot>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<AppUsageSnapshot>, CancellationToken>((list, _) => saved = list.ToList())
            .Returns(Task.CompletedTask);

        var cmd = new IngestAppUsageSnapshotsCommand { Snapshots = [Item(_clock.UtcNow.AddMinutes(-1))] };
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _uow.SaveCallCount.Should().Be(1);
        saved.Should().NotBeNull().And.HaveCount(1);
        saved!.First().EmployeeId.Should().Be(_userId);
        saved.First().ProcessName.Should().Be("code.exe");
    }

    [Fact]
    public async Task Monitoring_disabled_returns_403()
    {
        _toggles.Setup(t => t.IsEnabledAsync(
                _tenantId, _userId, MonitoringCapability.ApplicationTracking, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var cmd = new IngestAppUsageSnapshotsCommand { Snapshots = [Item(_clock.UtcNow)] };
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Be(MonitoringErrors.AppTrackingDisabled);
        _uow.SaveCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Unauthenticated_device_returns_401()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(false);

        var result = await CreateSut().Handle(
            new IngestAppUsageSnapshotsCommand { Snapshots = [Item(_clock.UtcNow)] },
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }
}
```

- [ ] **Step 7: Run all new tests, confirm pass**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~AppUsage"`
Expected: 7 passed (4 validator + 3 handler).

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/AppUsage src/ONEVO.Domain/Errors/MonitoringErrors.cs tests/ONEVO.Tests.Unit/Features/Monitoring/AppUsage
git commit -m "feat: add App Usage ingest command, validator, handler and tests"
```

---

## Task 3: Backend — App Usage infrastructure (EF repository, DbSet, DI, migration)

**Files:**
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/AppUsage/EfAppUsageSnapshotRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs:87` (after the `ActivityDailySummaries` line)
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs:254` (after the `IActivityRawBufferRepository` line)

- [ ] **Step 1: Write the EF repository**

```csharp
// src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/AppUsage/EfAppUsageSnapshotRepository.cs
using ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.AppUsage.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.AppUsage;

public class EfAppUsageSnapshotRepository : IAppUsageSnapshotRepository
{
    private readonly ApplicationDbContext _db;

    public EfAppUsageSnapshotRepository(ApplicationDbContext db) => _db = db;

    public async Task AddRangeAsync(IEnumerable<AppUsageSnapshot> snapshots, CancellationToken ct)
        => await _db.AppUsageSnapshots.AddRangeAsync(snapshots, ct);
}
```

- [ ] **Step 2: Add the DbSet**

In `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`, immediately after line 87 (`public DbSet<ActivityDailySummary> ActivityDailySummaries => Set<ActivityDailySummary>();`), add:

```csharp
    public DbSet<ONEVO.Domain.Features.Monitoring.AppUsage.Entities.AppUsageSnapshot> AppUsageSnapshots => Set<ONEVO.Domain.Features.Monitoring.AppUsage.Entities.AppUsageSnapshot>();
```

- [ ] **Step 3: Register the repository in DI**

In `src/ONEVO.Infrastructure/DependencyInjection.cs`, immediately after line 254 (`services.AddScoped<IActivityRawBufferRepository, EfActivityRawBufferRepository>();`), add:

```csharp
        services.AddScoped<IAppUsageSnapshotRepository, EfAppUsageSnapshotRepository>();
```

Add the two `using` statements this line needs at the top of the file (mirror the existing `using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;` / `using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.ActivityMonitoring;` pair):

```csharp
using ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.AppUsage;
```

- [ ] **Step 4: Generate the EF migration**

Run:
```bash
dotnet ef migrations add AddAppUsageSnapshots --project src\ONEVO.Infrastructure\ONEVO.Infrastructure.csproj --startup-project src\ONEVO.Api\ONEVO.Api.csproj -o Migrations
```
Expected: a new migration file appears under `src/ONEVO.Infrastructure/Migrations/` containing `CreateTable("app_usage_snapshots", ...)` and the two indexes from Task 1. Open the generated file and confirm both indexes are present before continuing — if a fluent `HasIndex` call was missed in `AppUsageSnapshotConfiguration.cs`, the migration will silently omit it.

- [ ] **Step 5: Apply the migration to your local dev database**

Run:
```bash
dotnet ef database update --project src\ONEVO.Infrastructure\ONEVO.Infrastructure.csproj --startup-project src\ONEVO.Api\ONEVO.Api.csproj
```
Expected: `Applying migration 'XXXXXXXX_AddAppUsageSnapshots'.` then `Done.`

- [ ] **Step 6: Build to confirm everything wires up**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/AppUsage src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs src/ONEVO.Infrastructure/DependencyInjection.cs src/ONEVO.Infrastructure/Migrations
git commit -m "feat: wire App Usage snapshot repository, DbSet and EF migration"
```

---

## Task 4: Backend — App Usage controller (closes the 404 gap)

**Files:**
- Create: `src/ONEVO.Api/Controllers/Tenant/Monitoring/AppUsage/MonitoringAppUsageIngestController.cs`

- [ ] **Step 1: Write the controller (mirrors `MonitoringActivityIngestController` exactly — same `TrayDevicePolicy`, same `snake_case` wire format matching `AppUsageIngestRequest` in the Tray App's [ActivityIngestModels.cs:37-53](C:/HR/tray_app_maui/ONEVO.Agent.Service/Api/ActivityIngestModels.cs:37))**

```csharp
// src/ONEVO.Api/Controllers/Tenant/Monitoring/AppUsage/MonitoringAppUsageIngestController.cs
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.AppUsage.Commands.IngestAppUsageSnapshots;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.AppUsage;

/// <summary>
/// Tray App → Backend ingest for foreground application usage.
/// Window titles arrive pre-hashed — this endpoint never receives raw title text.
/// </summary>
[ApiController]
[Route("api/v1/monitoring/app-usage")]
[Authorize(Policy = "TrayDevicePolicy")]
public class MonitoringAppUsageIngestController : ControllerBase
{
    private readonly IMediator _mediator;

    public MonitoringAppUsageIngestController(IMediator mediator)
        => _mediator = mediator;

    /// <summary>
    /// Accept a batch of app-usage snapshots from the tray agent.
    /// Authorization: Bearer {tray_access_token}
    /// </summary>
    [HttpPost("snapshots")]
    public async Task<IActionResult> IngestSnapshots(
        [FromBody] IngestAppUsageSnapshotsRequest request,
        CancellationToken ct)
    {
        var items = (request.Snapshots ?? [])
            .Select(s => new AppUsageSnapshotItem
            {
                CapturedAt = s.CapturedAt,
                ProcessName = s.ProcessName,
                WindowTitleHash = s.WindowTitleHash
            })
            .ToList();

        var result = await _mediator.Send(
            new IngestAppUsageSnapshotsCommand { Snapshots = items },
            ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Accepted();
    }
}

public record IngestAppUsageSnapshotsRequest(
    [property: JsonPropertyName("snapshots")] List<AppUsageSnapshotRequestItem>? Snapshots);

public record AppUsageSnapshotRequestItem(
    [property: JsonPropertyName("captured_at")] DateTimeOffset CapturedAt,
    [property: JsonPropertyName("process_name")] string? ProcessName,
    [property: JsonPropertyName("window_title_hash")] string? WindowTitleHash);
```

- [ ] **Step 2: Build and confirm route registration**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Manual smoke test against a running API + real tray device token**

Run the API (`dotnet run --project src\ONEVO.Api\ONEVO.Api.csproj`), then with a valid tray access token (obtained via `/api/v1/monitoring/activation/exchange`):
```bash
curl -X POST https://localhost:7229/api/v1/monitoring/app-usage/snapshots \
  -H "Authorization: Bearer <tray_access_token>" \
  -H "Content-Type: application/json" \
  -d "{\"snapshots\":[{\"captured_at\":\"2026-08-09T10:00:00Z\",\"process_name\":\"code.exe\",\"window_title_hash\":\"abc123\"}]}"
```
Expected: `202 Accepted`.

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Api/Controllers/Tenant/Monitoring/AppUsage
git commit -m "feat: add App Usage ingest controller"
```

---

## Task 5: Backend — Device State domain entity + EF configuration

**Files:**
- Create: `src/ONEVO.Domain/Features/Monitoring/DeviceState/Entities/DeviceStateSnapshot.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/DeviceState/DeviceStateSnapshotConfiguration.cs`

- [ ] **Step 1: Create the domain entity**

```csharp
// src/ONEVO.Domain/Features/Monitoring/DeviceState/Entities/DeviceStateSnapshot.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.DeviceState.Entities;

/// <summary>
/// Device idle/active state sample (seconds since last keyboard/mouse input).
/// </summary>
public class DeviceStateSnapshot : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid AgentDeviceId { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public int IdleSeconds { get; set; }
    public bool IsIdle { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

- [ ] **Step 2: Create the EF configuration**

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/DeviceState/DeviceStateSnapshotConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.DeviceState.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.DeviceState;

public class DeviceStateSnapshotConfiguration : IEntityTypeConfiguration<DeviceStateSnapshot>
{
    public void Configure(EntityTypeBuilder<DeviceStateSnapshot> builder)
    {
        builder.ToTable("device_state_snapshots");
        builder.HasKey(e => e.Id);

        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.CapturedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_device_state_snapshots_tenant_employee_captured");

        builder.HasIndex(e => new { e.TenantId, e.AgentDeviceId, e.CapturedAt })
            .HasDatabaseName("ix_device_state_snapshots_tenant_device_captured");
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add src/ONEVO.Domain/Features/Monitoring/DeviceState src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/DeviceState
git commit -m "feat: add DeviceStateSnapshot domain entity and EF configuration"
```

---

## Task 6: Backend — Device State application layer

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/DeviceState/Commands/IngestDeviceStateSnapshots/IngestDeviceStateSnapshotsCommand.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/DeviceState/Commands/IngestDeviceStateSnapshots/IngestDeviceStateSnapshotsCommandValidator.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/DeviceState/Commands/IngestDeviceStateSnapshots/IngestDeviceStateSnapshotsCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/DeviceState/Mappers/DeviceStateSnapshotMapper.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/DeviceState/RepositoryInterfaces/IDeviceStateSnapshotRepository.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/DeviceState/IngestDeviceStateSnapshotsCommandValidatorTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/DeviceState/IngestDeviceStateSnapshotsCommandHandlerTests.cs`

- [ ] **Step 1: Write the command + validator**

```csharp
// src/ONEVO.Application/Features/Monitoring/DeviceState/Commands/IngestDeviceStateSnapshots/IngestDeviceStateSnapshotsCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Monitoring.DeviceState.Commands.IngestDeviceStateSnapshots;

public record IngestDeviceStateSnapshotsCommand : IRequest<Result>
{
    public List<DeviceStateSnapshotItem> Snapshots { get; init; } = [];
}

public record DeviceStateSnapshotItem
{
    public DateTimeOffset CapturedAt { get; init; }
    public int IdleSeconds { get; init; }
    public bool IsIdle { get; init; }
}
```

```csharp
// src/ONEVO.Application/Features/Monitoring/DeviceState/Commands/IngestDeviceStateSnapshots/IngestDeviceStateSnapshotsCommandValidator.cs
using FluentValidation;

namespace ONEVO.Application.Features.Monitoring.DeviceState.Commands.IngestDeviceStateSnapshots;

public class IngestDeviceStateSnapshotsCommandValidator : AbstractValidator<IngestDeviceStateSnapshotsCommand>
{
    public const int MaxBatchSize = 200;
    public const int MaxIdleSeconds = 86_400; // 24h ceiling — a stuck/unlocked machine, not a legitimate sample

    public IngestDeviceStateSnapshotsCommandValidator()
    {
        RuleFor(x => x.Snapshots)
            .NotEmpty()
            .WithMessage("At least one snapshot is required.")
            .Must(s => s.Count <= MaxBatchSize)
            .WithMessage($"Batch cannot exceed {MaxBatchSize} snapshots.");

        RuleForEach(x => x.Snapshots).ChildRules(item =>
        {
            item.RuleFor(s => s.IdleSeconds)
                .InclusiveBetween(0, MaxIdleSeconds)
                .WithMessage($"IdleSeconds must be between 0 and {MaxIdleSeconds}.");
        });
    }
}
```

```csharp
// tests/ONEVO.Tests.Unit/Features/Monitoring/DeviceState/IngestDeviceStateSnapshotsCommandValidatorTests.cs
using FluentAssertions;
using ONEVO.Application.Features.Monitoring.DeviceState.Commands.IngestDeviceStateSnapshots;

namespace ONEVO.Tests.Unit.Features.Monitoring.DeviceState;

public class IngestDeviceStateSnapshotsCommandValidatorTests
{
    private readonly IngestDeviceStateSnapshotsCommandValidator _sut = new();

    private static DeviceStateSnapshotItem Item() => new()
    {
        CapturedAt = DateTimeOffset.UtcNow,
        IdleSeconds = 30,
        IsIdle = false
    };

    [Fact]
    public void Empty_snapshots_fails()
    {
        var result = _sut.Validate(new IngestDeviceStateSnapshotsCommand { Snapshots = [] });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Valid_single_snapshot_passes()
    {
        var result = _sut.Validate(new IngestDeviceStateSnapshotsCommand { Snapshots = [Item()] });
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Negative_idle_seconds_fails()
    {
        var item = Item() with { IdleSeconds = -1 };
        var result = _sut.Validate(new IngestDeviceStateSnapshotsCommand { Snapshots = [item] });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Idle_seconds_over_ceiling_fails()
    {
        var item = Item() with { IdleSeconds = 90_000 };
        var result = _sut.Validate(new IngestDeviceStateSnapshotsCommand { Snapshots = [item] });
        result.IsValid.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run the validator tests**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~IngestDeviceStateSnapshotsCommandValidatorTests"`
Expected: 4 passed.

- [ ] **Step 3: Write the mapper and repository interface**

```csharp
// src/ONEVO.Application/Features/Monitoring/DeviceState/Mappers/DeviceStateSnapshotMapper.cs
using ONEVO.Application.Features.Monitoring.DeviceState.Commands.IngestDeviceStateSnapshots;
using ONEVO.Domain.Features.Monitoring.DeviceState.Entities;

namespace ONEVO.Application.Features.Monitoring.DeviceState.Mappers;

public static class DeviceStateSnapshotMapper
{
    public static DeviceStateSnapshot ToEntity(
        DeviceStateSnapshotItem item,
        Guid tenantId,
        Guid employeeId,
        Guid agentDeviceId,
        DateTimeOffset createdAt)
    {
        return new DeviceStateSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            AgentDeviceId = agentDeviceId,
            CapturedAt = item.CapturedAt,
            IdleSeconds = item.IdleSeconds,
            IsIdle = item.IsIdle,
            CreatedAt = createdAt
        };
    }
}
```

```csharp
// src/ONEVO.Application/Features/Monitoring/DeviceState/RepositoryInterfaces/IDeviceStateSnapshotRepository.cs
using ONEVO.Domain.Features.Monitoring.DeviceState.Entities;

namespace ONEVO.Application.Features.Monitoring.DeviceState.RepositoryInterfaces;

public interface IDeviceStateSnapshotRepository
{
    Task AddRangeAsync(IEnumerable<DeviceStateSnapshot> snapshots, CancellationToken ct);
}
```

- [ ] **Step 4: Write the handler (capability = `DeviceTracking`)**

```csharp
// src/ONEVO.Application/Features/Monitoring/DeviceState/Commands/IngestDeviceStateSnapshots/IngestDeviceStateSnapshotsCommandHandler.cs
using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.DeviceState.Mappers;
using ONEVO.Application.Features.Monitoring.DeviceState.RepositoryInterfaces;
using ONEVO.Domain.Errors;

namespace ONEVO.Application.Features.Monitoring.DeviceState.Commands.IngestDeviceStateSnapshots;

public class IngestDeviceStateSnapshotsCommandHandler
    : IRequestHandler<IngestDeviceStateSnapshotsCommand, Result>
{
    private readonly IDeviceStateSnapshotRepository _snapshots;
    private readonly IMonitoringToggleResolver _toggleResolver;
    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<IngestDeviceStateSnapshotsCommandHandler> _logger;

    public IngestDeviceStateSnapshotsCommandHandler(
        IDeviceStateSnapshotRepository snapshots,
        IMonitoringToggleResolver toggleResolver,
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork,
        ILogger<IngestDeviceStateSnapshotsCommandHandler> logger)
    {
        _snapshots = snapshots;
        _toggleResolver = toggleResolver;
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(
        IngestDeviceStateSnapshotsCommand request,
        CancellationToken cancellationToken)
    {
        if (!_device.IsAuthenticated
            || _device.TenantId == Guid.Empty
            || _device.UserId == Guid.Empty
            || _device.DeviceRegistrationId == Guid.Empty)
        {
            return Result.Failure("A valid tray device token is required.", 401);
        }

        var tenant = await _tenants.GetByIdAsync(_device.TenantId, cancellationToken);
        if (tenant is null)
            return Result.Failure("Tenant not found.", 401);

        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null),
            cancellationToken);

        var tenantId = _device.TenantId;
        var employeeId = _device.UserId;
        var agentDeviceId = _device.DeviceRegistrationId;
        var now = _clock.UtcNow;

        var enabled = await _toggleResolver.IsEnabledAsync(
            tenantId, employeeId, MonitoringCapability.DeviceTracking, cancellationToken);

        if (!enabled)
        {
            _logger.LogInformation(
                "Device-state snapshot batch rejected: monitoring disabled. TenantId={TenantId} DeviceId={DeviceId} EmployeeId={EmployeeId} Count={Count}",
                tenantId, agentDeviceId, employeeId, request.Snapshots.Count);
            return Result.Failure(MonitoringErrors.DeviceTrackingDisabled, 403);
        }

        foreach (var item in request.Snapshots)
        {
            if (item.CapturedAt > now.AddMinutes(5))
                return Result.Failure(MonitoringErrors.SnapshotFutureTime, 400);

            if (item.CapturedAt < now.AddHours(-24))
                return Result.Failure(MonitoringErrors.SnapshotTooOld, 400);
        }

        _logger.LogInformation(
            "Device-state snapshot batch received. TenantId={TenantId} DeviceId={DeviceId} EmployeeId={EmployeeId} Count={Count}",
            tenantId, agentDeviceId, employeeId, request.Snapshots.Count);

        var entities = request.Snapshots
            .Select(item => DeviceStateSnapshotMapper.ToEntity(item, tenantId, employeeId, agentDeviceId, now))
            .ToList();

        await _snapshots.AddRangeAsync(entities, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
```

- [ ] **Step 5: Write the handler tests**

```csharp
// tests/ONEVO.Tests.Unit/Features/Monitoring/DeviceState/IngestDeviceStateSnapshotsCommandHandlerTests.cs
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.DeviceState.Commands.IngestDeviceStateSnapshots;
using ONEVO.Application.Features.Monitoring.DeviceState.RepositoryInterfaces;
using ONEVO.Domain.Errors;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.DeviceState.Entities;
using ONEVO.Tests.Unit.Fakes;

namespace ONEVO.Tests.Unit.Features.Monitoring.DeviceState;

public class IngestDeviceStateSnapshotsCommandHandlerTests
{
    private readonly Mock<IDeviceStateSnapshotRepository> _snapshots = new();
    private readonly Mock<IMonitoringToggleResolver> _toggles = new();
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _switcher = new();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly FakeUnitOfWork _uow = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public IngestDeviceStateSnapshotsCommandHandlerTests()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(true);
        _device.Setup(d => d.TenantId).Returns(_tenantId);
        _device.Setup(d => d.UserId).Returns(_userId);
        _device.Setup(d => d.DeviceRegistrationId).Returns(_deviceId);

        _tenants.Setup(t => t.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant
            {
                Id = _tenantId,
                Name = "Test",
                Slug = "test",
                Status = TenantStatus.Active
            });

        _toggles.Setup(t => t.IsEnabledAsync(
                _tenantId, _userId, MonitoringCapability.DeviceTracking, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private IngestDeviceStateSnapshotsCommandHandler CreateSut() => new(
        _snapshots.Object,
        _toggles.Object,
        _device.Object,
        _tenants.Object,
        _switcher.Object,
        _clock,
        _uow,
        NullLogger<IngestDeviceStateSnapshotsCommandHandler>.Instance);

    private static DeviceStateSnapshotItem Item(DateTimeOffset capturedAt) => new()
    {
        CapturedAt = capturedAt,
        IdleSeconds = 15,
        IsIdle = false
    };

    [Fact]
    public async Task Happy_path_saves_snapshots()
    {
        IEnumerable<DeviceStateSnapshot>? saved = null;
        _snapshots.Setup(s => s.AddRangeAsync(It.IsAny<IEnumerable<DeviceStateSnapshot>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<DeviceStateSnapshot>, CancellationToken>((list, _) => saved = list.ToList())
            .Returns(Task.CompletedTask);

        var cmd = new IngestDeviceStateSnapshotsCommand { Snapshots = [Item(_clock.UtcNow.AddMinutes(-1))] };
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _uow.SaveCallCount.Should().Be(1);
        saved.Should().NotBeNull().And.HaveCount(1);
        saved!.First().EmployeeId.Should().Be(_userId);
        saved.First().IdleSeconds.Should().Be(15);
    }

    [Fact]
    public async Task Monitoring_disabled_returns_403()
    {
        _toggles.Setup(t => t.IsEnabledAsync(
                _tenantId, _userId, MonitoringCapability.DeviceTracking, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var cmd = new IngestDeviceStateSnapshotsCommand { Snapshots = [Item(_clock.UtcNow)] };
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Be(MonitoringErrors.DeviceTrackingDisabled);
        _uow.SaveCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Unauthenticated_device_returns_401()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(false);

        var result = await CreateSut().Handle(
            new IngestDeviceStateSnapshotsCommand { Snapshots = [Item(_clock.UtcNow)] },
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }
}
```

- [ ] **Step 6: Run all new tests**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~DeviceState"`
Expected: 7 passed (4 validator + 3 handler).

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/DeviceState tests/ONEVO.Tests.Unit/Features/Monitoring/DeviceState
git commit -m "feat: add Device State ingest command, validator, handler and tests"
```

---

## Task 7: Backend — Device State infrastructure (EF repository, DbSet, DI, migration)

**Files:**
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/DeviceState/EfDeviceStateSnapshotRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` (after the `AppUsageSnapshots` DbSet added in Task 3)
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` (after the `IAppUsageSnapshotRepository` line added in Task 3)

- [ ] **Step 1: Write the EF repository**

```csharp
// src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/DeviceState/EfDeviceStateSnapshotRepository.cs
using ONEVO.Application.Features.Monitoring.DeviceState.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.DeviceState.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.DeviceState;

public class EfDeviceStateSnapshotRepository : IDeviceStateSnapshotRepository
{
    private readonly ApplicationDbContext _db;

    public EfDeviceStateSnapshotRepository(ApplicationDbContext db) => _db = db;

    public async Task AddRangeAsync(IEnumerable<DeviceStateSnapshot> snapshots, CancellationToken ct)
        => await _db.DeviceStateSnapshots.AddRangeAsync(snapshots, ct);
}
```

- [ ] **Step 2: Add the DbSet**

In `ApplicationDbContext.cs`, immediately after the `AppUsageSnapshots` DbSet line from Task 3, add:

```csharp
    public DbSet<ONEVO.Domain.Features.Monitoring.DeviceState.Entities.DeviceStateSnapshot> DeviceStateSnapshots => Set<ONEVO.Domain.Features.Monitoring.DeviceState.Entities.DeviceStateSnapshot>();
```

- [ ] **Step 3: Register the repository in DI**

In `DependencyInjection.cs`, immediately after the `IAppUsageSnapshotRepository` line from Task 3, add:

```csharp
        services.AddScoped<IDeviceStateSnapshotRepository, EfDeviceStateSnapshotRepository>();
```

Add the two matching `using` statements:

```csharp
using ONEVO.Application.Features.Monitoring.DeviceState.RepositoryInterfaces;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.DeviceState;
```

- [ ] **Step 4: Generate and apply the EF migration**

```bash
dotnet ef migrations add AddDeviceStateSnapshots --project src\ONEVO.Infrastructure\ONEVO.Infrastructure.csproj --startup-project src\ONEVO.Api\ONEVO.Api.csproj -o Migrations
dotnet ef database update --project src\ONEVO.Infrastructure\ONEVO.Infrastructure.csproj --startup-project src\ONEVO.Api\ONEVO.Api.csproj
```
Expected: migration creates `device_state_snapshots` with both indexes; `Done.` on update.

- [ ] **Step 5: Build to confirm**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/DeviceState src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs src/ONEVO.Infrastructure/DependencyInjection.cs src/ONEVO.Infrastructure/Migrations
git commit -m "feat: wire Device State snapshot repository, DbSet and EF migration"
```

---

## Task 8: Backend — Device State controller

**Files:**
- Create: `src/ONEVO.Api/Controllers/Tenant/Monitoring/DeviceState/MonitoringDeviceStateIngestController.cs`

- [ ] **Step 1: Write the controller (wire format matches `DeviceStateIngestRequest` in [ActivityIngestModels.cs:56-72](C:/HR/tray_app_maui/ONEVO.Agent.Service/Api/ActivityIngestModels.cs:56))**

```csharp
// src/ONEVO.Api/Controllers/Tenant/Monitoring/DeviceState/MonitoringDeviceStateIngestController.cs
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.DeviceState.Commands.IngestDeviceStateSnapshots;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.DeviceState;

/// <summary>
/// Tray App → Backend ingest for device idle/active state.
/// </summary>
[ApiController]
[Route("api/v1/monitoring/device-state")]
[Authorize(Policy = "TrayDevicePolicy")]
public class MonitoringDeviceStateIngestController : ControllerBase
{
    private readonly IMediator _mediator;

    public MonitoringDeviceStateIngestController(IMediator mediator)
        => _mediator = mediator;

    /// <summary>
    /// Accept a batch of device-state snapshots from the tray agent.
    /// Authorization: Bearer {tray_access_token}
    /// </summary>
    [HttpPost("snapshots")]
    public async Task<IActionResult> IngestSnapshots(
        [FromBody] IngestDeviceStateSnapshotsRequest request,
        CancellationToken ct)
    {
        var items = (request.Snapshots ?? [])
            .Select(s => new DeviceStateSnapshotItem
            {
                CapturedAt = s.CapturedAt,
                IdleSeconds = s.IdleSeconds,
                IsIdle = s.IsIdle
            })
            .ToList();

        var result = await _mediator.Send(
            new IngestDeviceStateSnapshotsCommand { Snapshots = items },
            ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Accepted();
    }
}

public record IngestDeviceStateSnapshotsRequest(
    [property: JsonPropertyName("snapshots")] List<DeviceStateSnapshotRequestItem>? Snapshots);

public record DeviceStateSnapshotRequestItem(
    [property: JsonPropertyName("captured_at")] DateTimeOffset CapturedAt,
    [property: JsonPropertyName("idle_seconds")] int IdleSeconds,
    [property: JsonPropertyName("is_idle")] bool IsIdle);
```

- [ ] **Step 2: Build**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Run the full backend unit test suite to confirm nothing else broke**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj`
Expected: all tests pass, including the 14 new ones from Tasks 2 and 6.

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Api/Controllers/Tenant/Monitoring/DeviceState
git commit -m "feat: add Device State ingest controller"
```

**At this point:** both `AgentApiRoutes.AppUsageSnapshots` and `AgentApiRoutes.DeviceStateSnapshots` resolve to real endpoints. Once a Tray App has a Device JWT (real activation against this backend), `ActivitySyncService` will stop re-queuing App Usage and Device State batches and they will land in `app_usage_snapshots` / `device_state_snapshots` — no Tray App code changes needed for this part, since `ActivitySyncService.FlushAppUsageSnapshotsAsync` / `FlushDeviceStateSnapshotsAsync` ([ActivitySyncService.cs:243-313](C:/HR/tray_app_maui/ONEVO.Agent.Service/Sync/ActivitySyncService.cs:243)) already exist and already POST the correct shape.

---

## Task 9: Tray App — Consent screen reflects real policy, Location Access removed

**Files:**
- Modify: `ONEVO.Agent.TrayApp/ViewModels/PrivacyConsentViewModel.cs`
- Modify: `ONEVO.Agent.TrayApp/Views/PrivacyConsentPage.xaml`
- Modify: `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/PrivacyConsentViewModelTests.cs`

- [ ] **Step 1: Update the failing/changing tests first**

Replace the full content of `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/PrivacyConsentViewModelTests.cs`:

```csharp
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class PrivacyConsentViewModelTests
{
    [Fact]
    public void ScreenMonitoringEnabled_DefaultsTrue()
    {
        var vm = new PrivacyConsentViewModel(new ONEVO.Agent.TrayApp.Tests.Fakes.FakeNamedPipeClient());
        Assert.True(vm.ScreenMonitoringEnabled);
    }

    [Fact]
    public void AppTrackingEnabled_DefaultsTrue()
    {
        var vm = new PrivacyConsentViewModel(new ONEVO.Agent.TrayApp.Tests.Fakes.FakeNamedPipeClient());
        Assert.True(vm.AppTrackingEnabled);
    }

    [Fact]
    public void CameraAccessEnabled_DefaultsFalse()
    {
        var vm = new PrivacyConsentViewModel(new ONEVO.Agent.TrayApp.Tests.Fakes.FakeNamedPipeClient());
        Assert.False(vm.CameraAccessEnabled);
    }

    [Fact]
    public void KeyboardMouseEnabled_DefaultsTrue()
    {
        var vm = new PrivacyConsentViewModel(new ONEVO.Agent.TrayApp.Tests.Fakes.FakeNamedPipeClient());
        Assert.True(vm.KeyboardMouseEnabled);
    }

    [Fact]
    public void AllowAndContinueCommand_AlwaysEnabled()
    {
        var vm = new PrivacyConsentViewModel(new ONEVO.Agent.TrayApp.Tests.Fakes.FakeNamedPipeClient());
        Assert.True(vm.AllowAndContinueCommand.CanExecute(null));
    }

    [Fact]
    public void ApplyPolicy_SetsAppTracking()
    {
        var vm     = new PrivacyConsentViewModel(new ONEVO.Agent.TrayApp.Tests.Fakes.FakeNamedPipeClient());
        var policy = new AgentPolicy { Version = "1", AppUsageEnabled = false };
        vm.ApplyPolicy(policy);
        Assert.False(vm.AppTrackingEnabled);
    }

    [Fact]
    public void ApplyPolicy_SetsCameraAccess()
    {
        var vm     = new PrivacyConsentViewModel(new ONEVO.Agent.TrayApp.Tests.Fakes.FakeNamedPipeClient());
        var policy = new AgentPolicy { Version = "1", CameraVerificationEnabled = true };
        vm.ApplyPolicy(policy);
        Assert.True(vm.CameraAccessEnabled);
    }

    [Fact]
    public void ApplyPolicy_SetsScreenMonitoring()
    {
        var vm     = new PrivacyConsentViewModel(new ONEVO.Agent.TrayApp.Tests.Fakes.FakeNamedPipeClient());
        var policy = new AgentPolicy { Version = "1", ScreenshotEnabled = false };
        vm.ApplyPolicy(policy);
        Assert.False(vm.ScreenMonitoringEnabled);
    }

    [Fact]
    public void ApplyPolicy_SetsKeyboardMouse()
    {
        var vm     = new PrivacyConsentViewModel(new ONEVO.Agent.TrayApp.Tests.Fakes.FakeNamedPipeClient());
        var policy = new AgentPolicy { Version = "1", ActivitySignalEnabled = false };
        vm.ApplyPolicy(policy);
        Assert.False(vm.KeyboardMouseEnabled);
    }
}
```

Note: `LocationAccessEnabled_DefaultsTrue` is deleted — the property no longer exists after Step 2.

- [ ] **Step 2: Run the tests to confirm they fail to compile (property doesn't reflect policy yet, `LocationAccessEnabled` still exists)**

Run: `dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~PrivacyConsentViewModelTests"`
Expected: `ApplyPolicy_SetsScreenMonitoring` and `ApplyPolicy_SetsKeyboardMouse` FAIL (values stay hardcoded `true`).

- [ ] **Step 3: Update the ViewModel — drop `LocationAccessEnabled`, make every switch reflect real policy**

Replace `ONEVO.Agent.TrayApp/ViewModels/PrivacyConsentViewModel.cs` in full:

```csharp
namespace ONEVO.Agent.TrayApp.ViewModels;

using ONEVO.Agent.TrayApp.Services;

public sealed partial class PrivacyConsentViewModel : BaseViewModel
{
    private readonly INamedPipeClient _pipe;

    [ObservableProperty] private bool _screenMonitoringEnabled = true;
    [ObservableProperty] private bool _appTrackingEnabled      = true;
    [ObservableProperty] private bool _cameraAccessEnabled     = false;
    [ObservableProperty] private bool _notificationsEnabled    = true;
    [ObservableProperty] private bool _keyboardMouseEnabled    = true;

    public PrivacyConsentViewModel(INamedPipeClient pipe)
    {
        Title = "Allow Required Policies";
        _pipe = pipe;
    }

    public void OnAppearing()
    {
        if (_pipe.LastKnownPolicy is { } policy)
            ApplyPolicy(policy);
    }

    /// <summary>
    /// All switches on this screen are display-only — they mirror the tenant-configured
    /// AgentPolicy, they are never a per-employee opt-out (see the footer copy in
    /// PrivacyConsentPage.xaml). Notifications has no AgentPolicy field because it isn't a
    /// monitoring capability, so it stays at its default.
    /// </summary>
    public void ApplyPolicy(AgentPolicy policy)
    {
        ScreenMonitoringEnabled = policy.ScreenshotEnabled;
        AppTrackingEnabled      = policy.AppUsageEnabled;
        CameraAccessEnabled     = policy.CameraVerificationEnabled;
        KeyboardMouseEnabled    = policy.ActivitySignalEnabled;
    }

    [RelayCommand]
    private async Task AllowAndContinue()
    {
        try { await Shell.Current.GoToAsync("//clockin"); }
        catch { /* unit tests */ }
    }
}
```

- [ ] **Step 4: Run the tests again, confirm all pass**

Run: `dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~PrivacyConsentViewModelTests"`
Expected: 8 passed.

- [ ] **Step 5: Update the XAML — remove the Location Access row, lock every switch**

In `ONEVO.Agent.TrayApp/Views/PrivacyConsentPage.xaml`:

Delete the entire Location Access block (currently lines 68-86 — the `<!-- Location Access -->` `<Grid>` and the `<BoxView>` separator immediately after it).

Add `IsEnabled="False"` to the three switches that don't already have it — App Tracking, Location (deleted, skip), Camera Access, Notifications. Change:

```xml
            <Switch Grid.Column="2" IsToggled="{Binding AppTrackingEnabled}"
                    VerticalOptions="Center" />
```
to:
```xml
            <Switch Grid.Column="2" IsToggled="{Binding AppTrackingEnabled}"
                    IsEnabled="False" VerticalOptions="Center" />
```

Change:
```xml
            <Switch Grid.Column="2" IsToggled="{Binding CameraAccessEnabled}"
                    VerticalOptions="Center" />
```
to:
```xml
            <Switch Grid.Column="2" IsToggled="{Binding CameraAccessEnabled}"
                    IsEnabled="False" VerticalOptions="Center" />
```

Change:
```xml
            <Switch Grid.Column="2" IsToggled="{Binding NotificationsEnabled}"
                    VerticalOptions="Center" />
```
to:
```xml
            <Switch Grid.Column="2" IsToggled="{Binding NotificationsEnabled}"
                    IsEnabled="False" VerticalOptions="Center" />
```

(`ScreenMonitoringEnabled` and `KeyboardMouseEnabled` switches already have `IsEnabled="False"` — leave them as-is.)

- [ ] **Step 6: Build the Tray App**

Run: `dotnet build ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj -f net10.0-windows10.0.19041.0`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Run the full Tray App test suite**

Run: `dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj`
Expected: all tests pass.

- [ ] **Step 8: Commit**

```bash
git add ONEVO.Agent.TrayApp/ViewModels/PrivacyConsentViewModel.cs ONEVO.Agent.TrayApp/Views/PrivacyConsentPage.xaml tests/ONEVO.Agent.TrayApp.Tests/ViewModels/PrivacyConsentViewModelTests.cs
git commit -m "fix: consent screen switches reflect real AgentPolicy, remove fake Location Access item"
```

---

## Task 10: Tray App — Real Windows notifications (replace the logger-only stub)

**Files:**
- Modify: `ONEVO.Agent.TrayApp/Services/NotificationService.cs`
- Modify: `ONEVO.Agent.TrayApp/Platforms/Windows/App.xaml.cs`

The `Microsoft.Windows.AppNotifications` assemblies are already present in the build output (transitively via the Windows App SDK reference that MAUI Windows pulls in) — no new NuGet package is needed.

- [ ] **Step 1: Register the notification manager at app startup**

In `ONEVO.Agent.TrayApp/Platforms/Windows/App.xaml.cs`, add the using and registration/unregistration calls. Read the file first to find the existing constructor and any `OnLaunched`/exit handling, then add:

```csharp
using Microsoft.Windows.AppNotifications;
```

At the top of the constructor (before any other startup logic runs):

```csharp
        AppNotificationManager.Default.Register();
```

Find where the app currently handles process exit / window close-to-tray (search for `Environment.Exit` or the app's shutdown path) and add, right before actual process termination (not on hide-to-tray, since the tray icon keeps running):

```csharp
        AppNotificationManager.Default.Unregister();
```

- [ ] **Step 2: Replace `NotificationService` with a real implementation**

```csharp
// ONEVO.Agent.TrayApp/Services/NotificationService.cs
namespace ONEVO.Agent.TrayApp.Services;

using Microsoft.Windows.AppNotifications.Builder;

public sealed class NotificationService
{
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger;
    }

    public void ShowInfo(string title, string message) => Show(title, message);

    public void ShowWarning(string title, string message) => Show(title, message);

    private void Show(string title, string message)
    {
        _logger.LogInformation("Notification: {Title} — {Message}", title, message);

        try
        {
            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message)
                .BuildNotification();

            Microsoft.Windows.AppNotifications.AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to show Windows notification: {Title}", title);
        }
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj -f net10.0-windows10.0.19041.0`
Expected: Build succeeded, 0 errors. If `AppNotificationManager` fails to resolve, confirm the project's `.csproj` targets `net10.0-windows10.0.19041.0` with `<UseWinUI>true</UseWinUI>` (it already does — this is the same SDK that makes `Microsoft.Windows.AppNotifications` available) and re-check the exact namespace via `Object Browser` / IntelliSense rather than guessing further.

- [ ] **Step 4: Manual smoke test**

Run the Tray App (`dotnet run --project ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj -f net10.0-windows10.0.19041.0`). Trigger any existing call site that logs via `NotificationService` (there are currently none wired — for this manual test only, temporarily call `notificationService.ShowInfo("Test", "Hello from ONEVO")` from `MainPage`/`AppShell` constructor, confirm a real Windows toast appears in the Action Center, then remove the temporary call before committing).
Expected: a native Windows toast notification appears, not just a log line.

- [ ] **Step 5: Commit**

```bash
git add ONEVO.Agent.TrayApp/Services/NotificationService.cs ONEVO.Agent.TrayApp/Platforms/Windows/App.xaml.cs
git commit -m "feat: NotificationService shows real Windows toast notifications"
```

**Note:** this task makes `NotificationService` capable of showing real notifications — it does not add new call sites (e.g. break reminders, sync-failure alerts). Deciding when the app should actually notify the user is a product decision outside this plan's scope; wire specific call sites as separate, small follow-ups once that's decided.

---

## Self-Review

**Spec coverage:**
- App Usage data reaching the database → Tasks 1-4. ✅
- Device State data reaching the database → Tasks 5-8. ✅
- Consent screen accuracy (locked switches reflecting real policy) → Task 9. ✅
- Location Access removed per decision 1 → Task 9, Step 5. ✅
- System Notifications stub fixed → Task 10. ✅
- Camera Access, Screen Monitoring, Keyboard & Mouse already worked end-to-end before this plan — no task needed, confirmed via the earlier verification pass.

**Placeholder scan:** No TBD/TODO markers; every step has complete, runnable code or an exact command with expected output.

**Type consistency:** `AppUsageSnapshotItem`/`DeviceStateSnapshotItem` field names match across Command → Handler → Mapper → Tests in each task. Controller request DTOs' `JsonPropertyName` values match exactly what `ActivityIngestModels.cs` on the Tray App side already sends (`captured_at`, `process_name`, `window_title_hash`, `idle_seconds`, `is_idle`) — verified against the existing Tray App source, not assumed.

---

**Plan complete and saved to `docs/superpowers/plans/2026-08-09-monitoring-consent-and-ingest-completion.md`. Two execution options:**

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints

**Which approach?**
