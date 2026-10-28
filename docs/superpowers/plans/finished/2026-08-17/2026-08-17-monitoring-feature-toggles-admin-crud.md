# MonitoringFeatureToggles Admin CRUD Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give tenant HR admins a working GET/PUT settings screen for `MonitoringFeatureToggles` so the tenant-level ON/OFF switch — currently write-only-missing — has a real admin path instead of permanently defaulting every monitoring capability to `false`.

**Architecture:** Standard ONEVO Clean Architecture + CQRS slice under `Features/Monitoring/Settings`. The `monitoring_feature_toggles` table, its EF configuration, and its PostgreSQL RLS policy (with `WITH CHECK`, confirmed in `20260805045300_AddActivityMonitoring.cs`) already exist — this plan adds only the Application/Infrastructure/Api layers for GET (current settings, defaulting to all-`false` when no row exists yet) and PUT (full-replace upsert). No migration is required. Follows the same `ICurrentUser`-driven, repository-owns-`SaveChangesAsync` pattern as `UpdateLegalEntityGeneralSettingsCommand` — not the tray-device `IUnitOfWork` pattern used by the ingest endpoints, because this is a human tenant-admin browser flow, not a device ingest flow.

**Tech Stack:** ASP.NET Core, MediatR, EF Core 8 / PostgreSQL, FluentAssertions + Moq + xUnit (unit), Testcontainers.PostgreSql (integration).

**Scope boundary:** Tenant-level `MonitoringFeatureToggles` only. `EmployeeMonitoringOverride` and `MonitoringPolicyOverride` (role/position/department overrides) are separate, larger admin-UI features and are explicitly out of scope for this plan.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/ONEVO.Application/Features/Monitoring/Settings/DTOs/Responses/MonitoringFeatureTogglesResponse.cs` | Response DTO (11 capability bools + `UpdatedAt`) |
| `src/ONEVO.Application/Features/Monitoring/Settings/Mappers/MonitoringFeatureTogglesMapper.cs` | Entity → response mapping, including the no-row-yet default |
| `src/ONEVO.Application/Features/Monitoring/Settings/RepositoryInterfaces/IMonitoringFeatureTogglesRepository.cs` | Repository contract |
| `src/ONEVO.Application/Features/Monitoring/Settings/Queries/GetMonitoringFeatureToggles/GetMonitoringFeatureTogglesQuery.cs` + `Handler.cs` | Read current tenant settings |
| `src/ONEVO.Application/Features/Monitoring/Settings/Commands/UpdateMonitoringFeatureToggles/UpdateMonitoringFeatureTogglesCommand.cs` + `Handler.cs` | Full-replace upsert of tenant settings |
| `src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/Settings/EfMonitoringFeatureTogglesRepository.cs` | EF implementation |
| `src/ONEVO.Infrastructure/DependencyInjection.cs` | Register the new repository (modify existing file) |
| `src/ONEVO.Api/Contracts/Monitoring/Settings/UpdateMonitoringFeatureTogglesRequest.cs` | HTTP request body |
| `src/ONEVO.Api/Controllers/Tenant/Monitoring/Settings/MonitoringSettingsController.cs` | `GET/PUT /api/v1/monitoring/settings` |
| `tests/ONEVO.Tests.Unit/Features/Monitoring/Settings/GetMonitoringFeatureTogglesQueryHandlerTests.cs` | Unit tests for the query |
| `tests/ONEVO.Tests.Unit/Features/Monitoring/Settings/UpdateMonitoringFeatureTogglesCommandHandlerTests.cs` | Unit tests for the command |
| `tests/ONEVO.Tests.Integration/Monitoring/Settings/MonitoringFeatureTogglesIntegrationTests.cs` | One real-HTTP, real-Postgres test proving the write actually unblocks the resolver |

No FluentValidation validator is added: every field on the command is a plain non-nullable `bool` bound by the model binder — there is no non-trivial input to validate (per architecture checklist step 5, validators are for non-trivial input only).

No EF migration task: the table, its unique index on `tenant_id`, and its RLS policy (`ALTER TABLE ... FORCE ROW LEVEL SECURITY` + `CREATE POLICY tenant_isolation ... USING (...) WITH CHECK (...)`) were created in `20260805045300_AddActivityMonitoring.cs`. The `WITH CHECK` clause means first-time `INSERT`s under `app.tenant_context_mode = 'tenant'` are already permitted — this is not the System-mode RLS gap documented elsewhere.

---

### Task 1: Response DTO and Mapper

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/Settings/DTOs/Responses/MonitoringFeatureTogglesResponse.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/Settings/Mappers/MonitoringFeatureTogglesMapper.cs`

- [ ] **Step 1: Create the response DTO**

```csharp
namespace ONEVO.Application.Features.Monitoring.Settings.DTOs.Responses;

public record MonitoringFeatureTogglesResponse(
    bool ActivityMonitoring,
    bool ApplicationTracking,
    bool DocumentTracking,
    bool CommunicationTracking,
    bool ScreenshotCapture,
    bool AutoScreenshotCapture,
    bool MeetingDetection,
    bool DeviceTracking,
    bool WorkLocationVerification,
    bool IdentityVerification,
    bool Biometric,
    DateTimeOffset? UpdatedAt);
```

- [ ] **Step 2: Create the mapper**

```csharp
using ONEVO.Application.Features.Monitoring.Settings.DTOs.Responses;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;

namespace ONEVO.Application.Features.Monitoring.Settings.Mappers;

public static class MonitoringFeatureTogglesMapper
{
    /// <summary>
    /// Null entity (no row yet) maps to all-false defaults with UpdatedAt = null,
    /// mirroring MonitoringToggleResolverService's own null-row-means-false semantics.
    /// </summary>
    public static MonitoringFeatureTogglesResponse ToResponse(MonitoringFeatureToggles? entity) =>
        entity is null
            ? new MonitoringFeatureTogglesResponse(
                false, false, false, false, false, false, false, false, false, false, false, null)
            : new MonitoringFeatureTogglesResponse(
                entity.ActivityMonitoring,
                entity.ApplicationTracking,
                entity.DocumentTracking,
                entity.CommunicationTracking,
                entity.ScreenshotCapture,
                entity.AutoScreenshotCapture,
                entity.MeetingDetection,
                entity.DeviceTracking,
                entity.WorkLocationVerification,
                entity.IdentityVerification,
                entity.Biometric,
                entity.UpdatedAt);
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/ONEVO.Application/ONEVO.Application.csproj`
Expected: Build succeeded (no test yet — this is a pure data-shape step; behavior is tested via the handlers in Tasks 3–4).

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/Settings/DTOs src/ONEVO.Application/Features/Monitoring/Settings/Mappers
git commit -m "feat: add MonitoringFeatureToggles response DTO and mapper"
```

---

### Task 2: Repository interface and EF implementation

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/Settings/RepositoryInterfaces/IMonitoringFeatureTogglesRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/Settings/EfMonitoringFeatureTogglesRepository.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs:332` (immediately after the existing `services.AddHostedService<ActivityDailySummaryJob>();` line, inside the `// Monitoring - Activity (keyboard/mouse tracking)` DI block)

- [ ] **Step 1: Create the repository interface**

```csharp
using ONEVO.Domain.Features.Monitoring.Settings.Entities;

namespace ONEVO.Application.Features.Monitoring.Settings.RepositoryInterfaces;

public interface IMonitoringFeatureTogglesRepository
{
    Task<MonitoringFeatureToggles?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);

    Task AddAsync(MonitoringFeatureToggles toggles, CancellationToken ct = default);

    void Update(MonitoringFeatureToggles toggles);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: Create the EF implementation**

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Monitoring.Settings.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Settings;

public class EfMonitoringFeatureTogglesRepository : IMonitoringFeatureTogglesRepository
{
    private readonly ApplicationDbContext _db;

    public EfMonitoringFeatureTogglesRepository(ApplicationDbContext db) => _db = db;

    public async Task<MonitoringFeatureToggles?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        await _db.MonitoringFeatureToggles
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);

    public async Task AddAsync(MonitoringFeatureToggles toggles, CancellationToken ct = default) =>
        await _db.MonitoringFeatureToggles.AddAsync(toggles, ct);

    public void Update(MonitoringFeatureToggles toggles) => _db.MonitoringFeatureToggles.Update(toggles);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default) => await _db.SaveChangesAsync(ct);
}
```

- [ ] **Step 3: Register in DI**

In `src/ONEVO.Infrastructure/DependencyInjection.cs`, change:

```csharp
        services.AddScoped<IMonitoringToggleResolver, MonitoringToggleResolverService>();
        services.AddHostedService<ActivityDailySummaryJob>();
```

to:

```csharp
        services.AddScoped<IMonitoringToggleResolver, MonitoringToggleResolverService>();
        services.AddHostedService<ActivityDailySummaryJob>();

        // Monitoring - Settings (tenant-level feature toggles admin CRUD)
        services.AddScoped<
            ONEVO.Application.Features.Monitoring.Settings.RepositoryInterfaces.IMonitoringFeatureTogglesRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Settings.EfMonitoringFeatureTogglesRepository>();
```

- [ ] **Step 4: Build**

Run: `dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/Settings/RepositoryInterfaces src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/Settings src/ONEVO.Infrastructure/DependencyInjection.cs
git commit -m "feat: add MonitoringFeatureToggles repository"
```

---

### Task 3: Get query — test first, then handler

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/Settings/Queries/GetMonitoringFeatureToggles/GetMonitoringFeatureTogglesQuery.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/Settings/Queries/GetMonitoringFeatureToggles/GetMonitoringFeatureTogglesQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/Settings/GetMonitoringFeatureTogglesQueryHandlerTests.cs`

- [ ] **Step 1: Define the query record (needed for the test to compile)**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.Settings.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.Settings.Queries.GetMonitoringFeatureToggles;

public record GetMonitoringFeatureTogglesQuery : IRequest<Result<MonitoringFeatureTogglesResponse>>;
```

- [ ] **Step 2: Write the failing test**

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Settings.Queries.GetMonitoringFeatureToggles;
using ONEVO.Application.Features.Monitoring.Settings.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Settings;

public class GetMonitoringFeatureTogglesQueryHandlerTests
{
    private readonly Mock<IMonitoringFeatureTogglesRepository> _toggles = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private GetMonitoringFeatureTogglesQueryHandler BuildSut()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        return new GetMonitoringFeatureTogglesQueryHandler(_toggles.Object, _currentUser.Object);
    }

    [Fact]
    public async Task Handle_NoExistingRow_ReturnsAllFalseDefaults()
    {
        _toggles.Setup(r => r.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MonitoringFeatureToggles?)null);
        var sut = BuildSut();

        var result = await sut.Handle(new GetMonitoringFeatureTogglesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ActivityMonitoring.Should().BeFalse();
        result.Value.Biometric.Should().BeFalse();
        result.Value.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ExistingRow_ReturnsMappedValues()
    {
        var updatedAt = new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero);
        _toggles.Setup(r => r.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonitoringFeatureToggles
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ActivityMonitoring = true,
                ScreenshotCapture = true,
                UpdatedAt = updatedAt
            });
        var sut = BuildSut();

        var result = await sut.Handle(new GetMonitoringFeatureTogglesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ActivityMonitoring.Should().BeTrue();
        result.Value.ScreenshotCapture.Should().BeTrue();
        result.Value.ApplicationTracking.Should().BeFalse();
        result.Value.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public async Task Handle_Unauthenticated_ReturnsForbidden()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(false);
        var sut = new GetMonitoringFeatureTogglesQueryHandler(_toggles.Object, _currentUser.Object);

        var result = await sut.Handle(new GetMonitoringFeatureTogglesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter GetMonitoringFeatureTogglesQueryHandlerTests`
Expected: Build error — `GetMonitoringFeatureTogglesQueryHandler` does not exist yet.

- [ ] **Step 4: Implement the handler**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Settings.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Settings.Mappers;
using ONEVO.Application.Features.Monitoring.Settings.RepositoryInterfaces;

namespace ONEVO.Application.Features.Monitoring.Settings.Queries.GetMonitoringFeatureToggles;

public class GetMonitoringFeatureTogglesQueryHandler
    : IRequestHandler<GetMonitoringFeatureTogglesQuery, Result<MonitoringFeatureTogglesResponse>>
{
    private readonly IMonitoringFeatureTogglesRepository _toggles;
    private readonly ICurrentUser _currentUser;

    public GetMonitoringFeatureTogglesQueryHandler(
        IMonitoringFeatureTogglesRepository toggles, ICurrentUser currentUser)
    {
        _toggles = toggles;
        _currentUser = currentUser;
    }

    public async Task<Result<MonitoringFeatureTogglesResponse>> Handle(
        GetMonitoringFeatureTogglesQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<MonitoringFeatureTogglesResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<MonitoringFeatureTogglesResponse>.Forbidden("Tenant context missing.");

        var entity = await _toggles.GetByTenantIdAsync(tenantId, ct);
        return Result<MonitoringFeatureTogglesResponse>.Success(MonitoringFeatureTogglesMapper.ToResponse(entity));
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter GetMonitoringFeatureTogglesQueryHandlerTests`
Expected: 3 passed

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/Settings/Queries tests/ONEVO.Tests.Unit/Features/Monitoring/Settings/GetMonitoringFeatureTogglesQueryHandlerTests.cs
git commit -m "feat: add GetMonitoringFeatureTogglesQuery handler"
```

---

### Task 4: Update command — test first, then handler

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/Settings/Commands/UpdateMonitoringFeatureToggles/UpdateMonitoringFeatureTogglesCommand.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/Settings/Commands/UpdateMonitoringFeatureToggles/UpdateMonitoringFeatureTogglesCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/Settings/UpdateMonitoringFeatureTogglesCommandHandlerTests.cs`

- [ ] **Step 1: Define the command record**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.Settings.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.Settings.Commands.UpdateMonitoringFeatureToggles;

// Full-replace PUT: every capability must be supplied on every call. There is no
// nullable-means-preserve semantics here - the admin settings screen always
// submits the complete current state of all 11 switches.
public record UpdateMonitoringFeatureTogglesCommand(
    bool ActivityMonitoring,
    bool ApplicationTracking,
    bool DocumentTracking,
    bool CommunicationTracking,
    bool ScreenshotCapture,
    bool AutoScreenshotCapture,
    bool MeetingDetection,
    bool DeviceTracking,
    bool WorkLocationVerification,
    bool IdentityVerification,
    bool Biometric) : IRequest<Result<MonitoringFeatureTogglesResponse>>;
```

- [ ] **Step 2: Write the failing test**

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Settings.Commands.UpdateMonitoringFeatureToggles;
using ONEVO.Application.Features.Monitoring.Settings.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Settings;

public class UpdateMonitoringFeatureTogglesCommandHandlerTests
{
    private readonly Mock<IMonitoringFeatureTogglesRepository> _toggles = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _dateTimeProvider = new();
    private readonly Mock<ICacheService> _cache = new();

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    private UpdateMonitoringFeatureTogglesCommandHandler BuildSut(bool hasPermission = true)
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        _currentUser.Setup(c => c.HasPermission("monitoring:configure")).Returns(hasPermission);
        _dateTimeProvider.SetupGet(d => d.UtcNow).Returns(FixedNow);
        return new UpdateMonitoringFeatureTogglesCommandHandler(
            _toggles.Object, _currentUser.Object, _dateTimeProvider.Object, _cache.Object);
    }

    private static UpdateMonitoringFeatureTogglesCommand ValidCommand(bool activityMonitoring = true) => new(
        ActivityMonitoring: activityMonitoring,
        ApplicationTracking: true,
        DocumentTracking: false,
        CommunicationTracking: false,
        ScreenshotCapture: true,
        AutoScreenshotCapture: false,
        MeetingDetection: false,
        DeviceTracking: true,
        WorkLocationVerification: false,
        IdentityVerification: false,
        Biometric: false);

    [Fact]
    public async Task Handle_NoExistingRow_CreatesNewRow()
    {
        _toggles.Setup(r => r.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MonitoringFeatureToggles?)null);
        MonitoringFeatureToggles? added = null;
        _toggles.Setup(r => r.AddAsync(It.IsAny<MonitoringFeatureToggles>(), It.IsAny<CancellationToken>()))
            .Callback<MonitoringFeatureToggles, CancellationToken>((t, _) => added = t)
            .Returns(Task.CompletedTask);
        var sut = BuildSut();

        var result = await sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added.Should().NotBeNull();
        added!.TenantId.Should().Be(TenantId);
        added.ActivityMonitoring.Should().BeTrue();
        added.CreatedAt.Should().Be(FixedNow);
        added.UpdatedAt.Should().Be(FixedNow);
        _toggles.Verify(r => r.Update(It.IsAny<MonitoringFeatureToggles>()), Times.Never);
        _toggles.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ExistingRow_UpdatesInPlace()
    {
        var existing = new MonitoringFeatureToggles
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            ActivityMonitoring = false,
            CreatedAt = FixedNow.AddDays(-10),
            UpdatedAt = FixedNow.AddDays(-10)
        };
        _toggles.Setup(r => r.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        var sut = BuildSut();

        var result = await sut.Handle(ValidCommand(activityMonitoring: true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existing.ActivityMonitoring.Should().BeTrue();
        existing.UpdatedAt.Should().Be(FixedNow);
        _toggles.Verify(r => r.Update(existing), Times.Once);
        _toggles.Verify(r => r.AddAsync(It.IsAny<MonitoringFeatureToggles>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ValidRequest_InvalidatesTenantToggleCachePrefix()
    {
        _toggles.Setup(r => r.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MonitoringFeatureToggles?)null);
        var sut = BuildSut();

        await sut.Handle(ValidCommand(), CancellationToken.None);

        _cache.Verify(c => c.RemoveByPrefixAsync(
            $"tenant:{TenantId}:monitoring-toggle:", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Unauthenticated_ReturnsForbidden()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(false);
        var sut = new UpdateMonitoringFeatureTogglesCommandHandler(
            _toggles.Object, _currentUser.Object, _dateTimeProvider.Object, _cache.Object);

        var result = await sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        _toggles.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_MissingConfigurePermission_ReturnsForbidden()
    {
        var sut = BuildSut(hasPermission: false);

        var result = await sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        _toggles.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter UpdateMonitoringFeatureTogglesCommandHandlerTests`
Expected: Build error — `UpdateMonitoringFeatureTogglesCommandHandler` does not exist yet.

- [ ] **Step 4: Implement the handler**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Settings.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Settings.Mappers;
using ONEVO.Application.Features.Monitoring.Settings.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;

namespace ONEVO.Application.Features.Monitoring.Settings.Commands.UpdateMonitoringFeatureToggles;

public class UpdateMonitoringFeatureTogglesCommandHandler
    : IRequestHandler<UpdateMonitoringFeatureTogglesCommand, Result<MonitoringFeatureTogglesResponse>>
{
    private readonly IMonitoringFeatureTogglesRepository _toggles;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ICacheService _cache;

    public UpdateMonitoringFeatureTogglesCommandHandler(
        IMonitoringFeatureTogglesRepository toggles,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ICacheService cache)
    {
        _toggles = toggles;
        _currentUser = currentUser;
        _clock = clock;
        _cache = cache;
    }

    public async Task<Result<MonitoringFeatureTogglesResponse>> Handle(
        UpdateMonitoringFeatureTogglesCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<MonitoringFeatureTogglesResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<MonitoringFeatureTogglesResponse>.Forbidden("Tenant context missing.");

        if (!_currentUser.HasPermission("monitoring:configure"))
            return Result<MonitoringFeatureTogglesResponse>.Forbidden(
                "You do not have permission to configure monitoring settings.");

        var now = _clock.UtcNow;
        var existing = await _toggles.GetByTenantIdAsync(tenantId, ct);

        if (existing is not null)
        {
            existing.ActivityMonitoring = request.ActivityMonitoring;
            existing.ApplicationTracking = request.ApplicationTracking;
            existing.DocumentTracking = request.DocumentTracking;
            existing.CommunicationTracking = request.CommunicationTracking;
            existing.ScreenshotCapture = request.ScreenshotCapture;
            existing.AutoScreenshotCapture = request.AutoScreenshotCapture;
            existing.MeetingDetection = request.MeetingDetection;
            existing.DeviceTracking = request.DeviceTracking;
            existing.WorkLocationVerification = request.WorkLocationVerification;
            existing.IdentityVerification = request.IdentityVerification;
            existing.Biometric = request.Biometric;
            existing.UpdatedAt = now;
            _toggles.Update(existing);
        }
        else
        {
            existing = new MonitoringFeatureToggles
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ActivityMonitoring = request.ActivityMonitoring,
                ApplicationTracking = request.ApplicationTracking,
                DocumentTracking = request.DocumentTracking,
                CommunicationTracking = request.CommunicationTracking,
                ScreenshotCapture = request.ScreenshotCapture,
                AutoScreenshotCapture = request.AutoScreenshotCapture,
                MeetingDetection = request.MeetingDetection,
                DeviceTracking = request.DeviceTracking,
                WorkLocationVerification = request.WorkLocationVerification,
                IdentityVerification = request.IdentityVerification,
                Biometric = request.Biometric,
                CreatedAt = now,
                UpdatedAt = now
            };
            await _toggles.AddAsync(existing, ct);
        }

        await _toggles.SaveChangesAsync(ct);

        // Resolver caches per (tenant, employee, capability) under this prefix (2 min TTL,
        // see MonitoringToggleResolverService). This clears the local in-memory cache only -
        // acceptable convergence bound is "up to 2 minutes", not instant.
        await _cache.RemoveByPrefixAsync($"tenant:{tenantId}:monitoring-toggle:", ct);

        return Result<MonitoringFeatureTogglesResponse>.Success(MonitoringFeatureTogglesMapper.ToResponse(existing));
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter UpdateMonitoringFeatureTogglesCommandHandlerTests`
Expected: 5 passed

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/Settings/Commands tests/ONEVO.Tests.Unit/Features/Monitoring/Settings/UpdateMonitoringFeatureTogglesCommandHandlerTests.cs
git commit -m "feat: add UpdateMonitoringFeatureTogglesCommand handler"
```

---

### Task 5: Controller and request DTO

**Files:**
- Create: `src/ONEVO.Api/Contracts/Monitoring/Settings/UpdateMonitoringFeatureTogglesRequest.cs`
- Create: `src/ONEVO.Api/Controllers/Tenant/Monitoring/Settings/MonitoringSettingsController.cs`

- [ ] **Step 1: Create the request DTO**

```csharp
namespace ONEVO.Api.Contracts.Monitoring.Settings;

public record UpdateMonitoringFeatureTogglesRequest(
    bool ActivityMonitoring,
    bool ApplicationTracking,
    bool DocumentTracking,
    bool CommunicationTracking,
    bool ScreenshotCapture,
    bool AutoScreenshotCapture,
    bool MeetingDetection,
    bool DeviceTracking,
    bool WorkLocationVerification,
    bool IdentityVerification,
    bool Biometric);
```

- [ ] **Step 2: Create the controller**

```csharp
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Monitoring.Settings;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Monitoring.Settings.Commands.UpdateMonitoringFeatureToggles;
using ONEVO.Application.Features.Monitoring.Settings.Queries.GetMonitoringFeatureToggles;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.Settings;

/// <summary>
/// Tenant admin CRUD for tenant-level monitoring capability toggles.
/// Without this endpoint, MonitoringFeatureToggles has no write path and every
/// monitoring capability permanently resolves to false in production tenants
/// (see MonitoringToggleResolverService and DevSmokeTestTenantSeeder's dev-only workaround).
/// </summary>
[ApiController]
[Route("api/v1/monitoring/settings")]
[Authorize(Policy = "TenantPolicy")]
public class MonitoringSettingsController : ControllerBase
{
    private readonly IMediator _mediator;

    public MonitoringSettingsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequirePermission("monitoring:read")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetMonitoringFeatureTogglesQuery(), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPut]
    [RequirePermission("monitoring:configure")]
    public async Task<IActionResult> Update(
        [FromBody] UpdateMonitoringFeatureTogglesRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new UpdateMonitoringFeatureTogglesCommand(
                request.ActivityMonitoring,
                request.ApplicationTracking,
                request.DocumentTracking,
                request.CommunicationTracking,
                request.ScreenshotCapture,
                request.AutoScreenshotCapture,
                request.MeetingDetection,
                request.DeviceTracking,
                request.WorkLocationVerification,
                request.IdentityVerification,
                request.Biometric),
            ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
```

- [ ] **Step 3: Build the API project**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Api/Contracts/Monitoring/Settings src/ONEVO.Api/Controllers/Tenant/Monitoring/Settings
git commit -m "feat: add GET/PUT /api/v1/monitoring/settings controller"
```

---

### Task 6: Integration test — the one that proves the blocker is actually fixed

Rather than replicating the full tenant-provisioning-via-admin-API flow (`ProvisionAndLoginOwnerAsync` in `LegalEntitiesIntegrationTests.cs`, ~360 lines, built for legal-entity business-rule testing), this reuses the lighter direct-DB-seed + real-login pattern already proven in `tests/ONEVO.Tests.Integration/Monitoring/Policy/TrayMonitoringPolicyIntegrationTests.cs` (`SeedActiveUserAsync` + `LoginAndGetSessionAsync` + its private helpers `CompleteLegalAcceptanceAsync`, `CompleteTenantSessionExchangeAsync`, `ExtractCookieValue`), extended with a Role/Permission grant since our endpoints (unlike the tray JWT endpoints) go through `[RequirePermission]`.

**Files:**
- Create: `tests/ONEVO.Tests.Integration/Monitoring/Settings/MonitoringFeatureTogglesIntegrationTests.cs`

- [ ] **Step 1: Write the integration test**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.Monitoring.Settings;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MonitoringFeatureTogglesIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_monitoring_settings_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private E2ETestFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        await IntegrationDatabaseBootstrap.InitializeAsync(connectionString);
        _environmentScope = new IntegrationTestEnvironmentScope(connectionString);

        _factory = new E2ETestFactory(connectionString, new CapturingEmailService());
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
        await _environmentScope.DisposeAsync();
    }

    [Fact]
    public async Task Get_Unauthenticated_Returns401()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/monitoring/settings");
        req.Headers.Host = "localhost";

        var resp = await _client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_NoRowYet_ReturnsAllFalseDefaults()
    {
        var session = await SeedAdminUserAndLoginAsync("mft-get");

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/monitoring/settings");
        req.Headers.Host = session.TenantHost;
        req.Headers.Add("Cookie", session.CookieHeader);

        var resp = await _client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("activityMonitoring").GetBoolean().Should().BeFalse();
        body.GetProperty("updatedAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Put_MissingConfigurePermission_Returns403()
    {
        var session = await SeedUserWithPermissionsAsync("mft-noperm", ["monitoring:read"]);

        using var req = new HttpRequestMessage(HttpMethod.Put, "/api/v1/monitoring/settings");
        req.Headers.Host = session.TenantHost;
        req.Headers.Add("Cookie", session.CookieHeader);
        req.Headers.Add("X-CSRF-Token", session.CsrfHeader);
        req.Content = JsonContent.Create(ToggleBody(activityMonitoring: true));

        var resp = await _client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// The actual product claim this feature exists to satisfy: after PUT, the
    /// resolver that every ingest endpoint calls (MonitoringToggleResolverService)
    /// sees the new value - not just that the row changed in the database.
    /// </summary>
    [Fact]
    public async Task Put_ActivityMonitoringTrue_ResolverReflectsChange()
    {
        var session = await SeedAdminUserAndLoginAsync("mft-resolver");
        var employeeId = Guid.NewGuid(); // resolver falls back to tenant toggle when no employee override exists

        using var putReq = new HttpRequestMessage(HttpMethod.Put, "/api/v1/monitoring/settings");
        putReq.Headers.Host = session.TenantHost;
        putReq.Headers.Add("Cookie", session.CookieHeader);
        putReq.Headers.Add("X-CSRF-Token", session.CsrfHeader);
        putReq.Content = JsonContent.Create(ToggleBody(activityMonitoring: true));

        var putResp = await _client.SendAsync(putReq);
        putResp.StatusCode.Should().Be(HttpStatusCode.OK, await putResp.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IMonitoringToggleResolver>();
        var enabled = await resolver.IsEnabledAsync(
            session.TenantId, employeeId, MonitoringCapability.ActivityMonitoring);

        enabled.Should().BeTrue();
    }

    private static object ToggleBody(bool activityMonitoring) => new
    {
        activityMonitoring,
        applicationTracking = false,
        documentTracking = false,
        communicationTracking = false,
        screenshotCapture = false,
        autoScreenshotCapture = false,
        meetingDetection = false,
        deviceTracking = false,
        workLocationVerification = false,
        identityVerification = false,
        biometric = false
    };

    private Task<SessionInfo> SeedAdminUserAndLoginAsync(string slug) =>
        SeedUserWithPermissionsAsync(slug, ["monitoring:read", "monitoring:configure"]);

    private async Task<SessionInfo> SeedUserWithPermissionsAsync(string slug, IReadOnlyList<string> permissionCodes)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tenant = new ONEVO.Domain.Features.InfrastructureModule.Entities.Tenant
        {
            Id = Guid.NewGuid(),
            Name = slug,
            Slug = slug,
            CompanySizeRange = "1-10",
            Status = ONEVO.Domain.Features.InfrastructureModule.Entities.TenantStatus.Active
        };
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            TenantId = tenant.Id,
            Email = $"{slug}@test.dev",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("TestPass1!", 12),
            FirstName = "Test",
            LastName = "Admin",
            IsActive = true
        };
        db.Tenants.Add(tenant);
        db.Users.Add(user);

        var roleId = Guid.NewGuid();
        db.Add(new Role
        {
            Id = roleId,
            TenantId = tenant.Id,
            Name = $"{slug}-role",
            Description = "Monitoring settings fixture role",
            IsSystem = false,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedById = userId
        });
        foreach (var code in permissionCodes)
        {
            var permission = await db.Permissions.SingleAsync(p => p.Code == code);
            db.Add(new RolePermission { TenantId = tenant.Id, RoleId = roleId, PermissionId = permission.Id });
        }
        db.Add(new UserRole
        {
            TenantId = tenant.Id, UserId = userId, RoleId = roleId,
            AssignedAt = DateTimeOffset.UtcNow, AssignedBy = userId
        });

        await db.SaveChangesAsync();

        var sessionInfo = await LoginAndGetSessionAsync(userId, $"{slug}@test.dev", "TestPass1!", slug);
        return sessionInfo with { TenantId = tenant.Id };
    }

    private async Task<SessionInfo> LoginAndGetSessionAsync(Guid userId, string email, string password, string tenantSlug)
    {
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login");
        loginRequest.Headers.Host = "localhost";
        loginRequest.Content = JsonContent.Create(new { email, password });
        var loginResponse = await _client.SendAsync(loginRequest);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.Accepted, await loginResponse.Content.ReadAsStringAsync());

        var legalResponse = await CompleteLegalAcceptanceAsync(loginResponse);
        legalResponse.StatusCode.Should().Be(HttpStatusCode.Accepted, await legalResponse.Content.ReadAsStringAsync());

        var exchangeResponse = await CompleteTenantSessionExchangeAsync(legalResponse);
        exchangeResponse.StatusCode.Should().Be(HttpStatusCode.OK, await exchangeResponse.Content.ReadAsStringAsync());

        var sessionValue = ExtractCookieValue(exchangeResponse, "onevo_session");
        var csrfCookieValue = ExtractCookieValue(exchangeResponse, "onevo_csrf");
        var csrfHeader = Uri.UnescapeDataString(csrfCookieValue);

        return new SessionInfo(
            $"onevo_session={sessionValue}; onevo_csrf={csrfCookieValue}",
            csrfHeader,
            $"{tenantSlug}.localhost",
            Guid.Empty); // overwritten with `with { TenantId = ... }` by the caller, which already knows it
    }

    private async Task<HttpResponseMessage> CompleteLegalAcceptanceAsync(HttpResponseMessage priorResponse)
    {
        var legalPending = ExtractCookieValue(priorResponse, "onevo_legal_pending");
        var legalCsrf = ExtractCookieValue(priorResponse, "onevo_legal_csrf");
        var priorBody = await priorResponse.Content.ReadAsStringAsync();
        using var priorDocument = JsonDocument.Parse(priorBody);
        var continueUrl = new Uri(priorDocument.RootElement.GetProperty("continue_url").GetString()!, UriKind.Absolute);

        using var request = new HttpRequestMessage(HttpMethod.Post, continueUrl.PathAndQuery);
        request.Headers.Host = continueUrl.Host;
        request.Headers.Add("Cookie", $"onevo_legal_pending={legalPending}; onevo_legal_csrf={legalCsrf}");
        request.Headers.Add("X-CSRF-Token", legalCsrf);
        request.Content = JsonContent.Create(new
        {
            acceptances = new[]
            {
                new { document_type = "terms", version = "1.0", decision = "accepted" },
                new { document_type = "privacy_notice", version = "1.0", decision = "acknowledged" }
            }
        });

        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> CompleteTenantSessionExchangeAsync(HttpResponseMessage priorResponse)
    {
        var priorBody = await priorResponse.Content.ReadAsStringAsync();
        using var priorDocument = JsonDocument.Parse(priorBody);
        var continueUrl = new Uri(priorDocument.RootElement.GetProperty("continue_url").GetString()!, UriKind.Absolute);
        var code = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(continueUrl.Query)["code"].ToString();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/session-exchange");
        request.Headers.Host = continueUrl.Host;
        request.Content = JsonContent.Create(new { code });
        return await _client.SendAsync(request);
    }

    private static string ExtractCookieValue(HttpResponseMessage response, string cookieName)
    {
        var setCookies = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values : Enumerable.Empty<string>();
        foreach (var cookie in setCookies)
        {
            var pair = cookie.Split(';')[0];
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == cookieName)
                return parts[1];
        }
        throw new InvalidOperationException($"Cookie '{cookieName}' not found in response.");
    }

    private sealed record SessionInfo(string CookieHeader, string CsrfHeader, string TenantHost, Guid TenantId);
}
```

- [ ] **Step 2: Confirm the required test-support types exist with these exact names**

Before running, verify (read-only, no edits) that `IntegrationDatabaseBootstrap`, `IntegrationTestEnvironmentScope`, `E2ETestFactory`, `CapturingEmailService`, and `WebApplicationFactoryCollection` in `tests/ONEVO.Tests.Integration/Support/` and `tests/ONEVO.Tests.Integration/E2E/` match the usages in `TrayMonitoringPolicyIntegrationTests.cs` and `LegalEntitiesIntegrationTests.cs` referenced above — this test file was assembled from those two files' proven patterns, not written against the support classes directly.

- [ ] **Step 3: Run the integration tests**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter MonitoringFeatureTogglesIntegrationTests`
Expected: 4 passed (requires Docker for Testcontainers, unless `ONEVO_TEST_DB` is set per the convention in `LegalEntitiesIntegrationTests.cs`)

- [ ] **Step 4: Commit**

```bash
git add tests/ONEVO.Tests.Integration/Monitoring/Settings
git commit -m "test: add MonitoringFeatureToggles integration tests"
```

---

### Task 7: Swagger check and live dev-DB verification

Per the project's own house rule (learned from `ExchangeActivationCodeCommandHandler` shipping 15/15 green while returning null against the real dev DB — Testcontainers connects as the Postgres table owner, which bypasses `FORCE ROW LEVEL SECURITY` entirely): green tests are not sufficient proof for anything that writes to an RLS-protected tenant table. This task is the real-DB check.

- [ ] **Step 1: Confirm Swagger picks up the new endpoints**

Run: `dotnet run --project src/ONEVO.Api` (dev environment), then check `https://localhost:<port>/swagger` shows `GET /api/v1/monitoring/settings` and `PUT /api/v1/monitoring/settings` under the Monitoring group.

- [ ] **Step 2: Exercise the create path against the real dev DB**

`DevSmokeTestTenantSeeder.SeedMonitoringFeatureTogglesAsync` already seeds a `monitoring_feature_toggles` row for the acme/dapi dev tenants, so testing against them as-is only exercises the **update** branch. To prove the **create** branch (the one that matters most — it's the one a brand-new production tenant will hit), delete the seeded row first:

```sql
DELETE FROM monitoring_feature_toggles WHERE tenant_id = '<acme-tenant-id>';
```

- [ ] **Step 3: Log in as the acme tenant admin, call the endpoints**

```bash
curl -i -c cookies.txt -X POST https://acme.localhost:<port>/api/v1/auth/login -H "Content-Type: application/json" -d "{\"email\":\"<acme-admin-email>\",\"password\":\"<acme-admin-password>\"}"
```

Complete whatever login step the response asks for (legal acceptance / session exchange, same as the integration test's flow), then:

```bash
curl -i -b cookies.txt https://acme.localhost:<port>/api/v1/monitoring/settings
```

Expected: `200 OK`, all 11 capabilities `false`, `updatedAt: null` (row was just deleted in Step 2).

```bash
curl -i -b cookies.txt -X PUT https://acme.localhost:<port>/api/v1/monitoring/settings \
  -H "Content-Type: application/json" -H "X-CSRF-Token: <csrf-from-cookie>" \
  -d "{\"activityMonitoring\":true,\"applicationTracking\":false,\"documentTracking\":false,\"communicationTracking\":false,\"screenshotCapture\":false,\"autoScreenshotCapture\":false,\"meetingDetection\":false,\"deviceTracking\":false,\"workLocationVerification\":false,\"identityVerification\":false,\"biometric\":false}"
```

Expected: `200 OK`, `activityMonitoring: true`, `updatedAt` populated. Row now exists in `monitoring_feature_toggles` for acme.

- [ ] **Step 4: Confirm an ingest endpoint that was previously blocked now accepts**

Using the acme tray device/employee credentials, call an activity-ingest endpoint (`POST /api/v1/monitoring/activity/ingest` or equivalent per `MonitoringActivityIngestController`) and confirm it no longer returns `403 monitoring.activity_monitoring_disabled` — this is the end-to-end proof that the whole point of this feature (unblocking the already-built ingest pipelines) actually works, not just that a row got written.

- [ ] **Step 5: Re-seed acme's original toggles row (leave dev environment as found)**

Re-run `DevSmokeTestTenantSeeder` (restart the app, or re-insert the row it originally seeded) so other developers' local ingest testing isn't left in a partially-toggled state from this manual check.
