# Tray Employee Check-In Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** After the tray app connects via JWT, it can submit an employee check-in containing GPS location, face scan photo, and device serial number — giving the employer a verified attendance record per check-in event.

**Architecture:** Clean Architecture + CQRS. A new Bearer JWT auth scheme (`TrayDeviceScheme`) authenticates tray app requests separately from the existing cookie-based tenant/admin schemes. Check-in data (location, device serial) is stored in PostgreSQL via EF Core; face scan photos are stored in Cloudflare R2 with only metadata (file key, size, status) in the DB. Two endpoints: one for the check-in record, one for the face scan upload.

**Tech Stack:** ASP.NET Core 10, EF Core 10 / Npgsql, MediatR, FluentValidation, Microsoft.IdentityModel.Tokens (JWT Bearer), xUnit + Testcontainers (tests)

---

## File Map

### New files
| File | Responsibility |
|------|----------------|
| `src/ONEVO.Domain/Features/Monitoring/CheckIn/Entities/EmployeeCheckIn.cs` | Check-in aggregate root |
| `src/ONEVO.Domain/Features/Monitoring/CheckIn/Entities/MonitoringFaceScan.cs` | Face scan file metadata entity |
| `src/ONEVO.Application/Features/Monitoring/CheckIn/ServiceInterfaces/ITrayCurrentDevice.cs` | Reads device/user/tenant from tray JWT claims |
| `src/ONEVO.Application/Features/Monitoring/CheckIn/RepositoryInterfaces/ICheckInRepository.cs` | Data access contract |
| `src/ONEVO.Application/Features/Monitoring/CheckIn/Commands/SubmitCheckIn/SubmitCheckInCommand.cs` | Command record |
| `src/ONEVO.Application/Features/Monitoring/CheckIn/Commands/SubmitCheckIn/SubmitCheckInCommandValidator.cs` | FluentValidation |
| `src/ONEVO.Application/Features/Monitoring/CheckIn/Commands/SubmitCheckIn/SubmitCheckInCommandHandler.cs` | Handler |
| `src/ONEVO.Application/Features/Monitoring/CheckIn/Commands/UploadFaceScan/UploadFaceScanCommand.cs` | Command record |
| `src/ONEVO.Application/Features/Monitoring/CheckIn/Commands/UploadFaceScan/UploadFaceScanCommandValidator.cs` | FluentValidation |
| `src/ONEVO.Application/Features/Monitoring/CheckIn/Commands/UploadFaceScan/UploadFaceScanCommandHandler.cs` | Handler |
| `src/ONEVO.Application/Features/Monitoring/CheckIn/DTOs/Responses/CheckInResponseDto.cs` | Response shape |
| `src/ONEVO.Application/Features/Monitoring/CheckIn/DTOs/Responses/FaceScanUploadResponseDto.cs` | Response shape |
| `src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/CheckIn/EmployeeCheckInConfiguration.cs` | EF table mapping |
| `src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/CheckIn/MonitoringFaceScanConfiguration.cs` | EF table mapping |
| `src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/CheckIn/EfCheckInRepository.cs` | EF implementation |
| `src/ONEVO.Infrastructure/Services/Monitoring/CheckIn/TrayCurrentDeviceService.cs` | Reads tray JWT claims from HttpContext |
| `src/ONEVO.Api/Controllers/Tenant/Monitoring/CheckIn/MonitoringCheckInController.cs` | HTTP endpoints |
| `tests/ONEVO.Tests.Integration/Monitoring/CheckIn/CheckInTestFactory.cs` | WebApplicationFactory for tests |
| `tests/ONEVO.Tests.Integration/Monitoring/CheckIn/CheckInIntegrationTests.cs` | Full-stack tests |

### Modified files
| File | Change |
|------|--------|
| `src/ONEVO.Api/Extensions/AuthenticationExtensions.cs` | Add `TrayDeviceScheme` (JWT Bearer) |
| `src/ONEVO.Api/Extensions/AuthorizationExtensions.cs` | Add `TrayDevicePolicy` |
| `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` | Add `EmployeeCheckIns`, `MonitoringFaceScans` DbSets |
| `src/ONEVO.Infrastructure/DependencyInjection.cs` | Register `ICheckInRepository`, `ITrayCurrentDevice` |
| `src/ONEVO.Infrastructure/Migrations/` | New migration `AddMonitoringCheckIn` |

---

## Task 1: Domain Entities

**Files:**
- Create: `src/ONEVO.Domain/Features/Monitoring/CheckIn/Entities/EmployeeCheckIn.cs`
- Create: `src/ONEVO.Domain/Features/Monitoring/CheckIn/Entities/MonitoringFaceScan.cs`

- [ ] **Step 1: Create EmployeeCheckIn entity**

```csharp
// src/ONEVO.Domain/Features/Monitoring/CheckIn/Entities/EmployeeCheckIn.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.CheckIn.Entities;

public class EmployeeCheckIn : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid DeviceRegistrationId { get; set; }

    // Location
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? LocationAccuracy { get; set; }   // metres
    public string? LocationAddress { get; set; }

    // Device
    public string? DeviceSerialNumber { get; set; }

    // Face scan link
    public Guid? FaceScanId { get; set; }
    public MonitoringFaceScan? FaceScan { get; set; }

    public DateTimeOffset CheckedInAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

- [ ] **Step 2: Create MonitoringFaceScan entity**

```csharp
// src/ONEVO.Domain/Features/Monitoring/CheckIn/Entities/MonitoringFaceScan.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.CheckIn.Entities;

public class MonitoringFaceScan : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CheckInId { get; set; }

    // R2 storage key: tenants/{tenantId}/monitoring/face-scans/{id}/{fileName}
    public string StorageKey { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string ContentType { get; set; } = string.Empty;

    // pending_scan | available | failed
    public string Status { get; set; } = "pending_scan";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

- [ ] **Step 3: Commit**

```bash
git add src/ONEVO.Domain/Features/Monitoring/CheckIn/
git commit -m "feat(monitoring): add EmployeeCheckIn and MonitoringFaceScan domain entities"
```

---

## Task 2: Application Interfaces & DTOs

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/CheckIn/ServiceInterfaces/ITrayCurrentDevice.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/CheckIn/RepositoryInterfaces/ICheckInRepository.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/CheckIn/DTOs/Responses/CheckInResponseDto.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/CheckIn/DTOs/Responses/FaceScanUploadResponseDto.cs`

- [ ] **Step 1: Create ITrayCurrentDevice**

```csharp
// src/ONEVO.Application/Features/Monitoring/CheckIn/ServiceInterfaces/ITrayCurrentDevice.cs
namespace ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;

public interface ITrayCurrentDevice
{
    Guid DeviceRegistrationId { get; }
    Guid UserId { get; }
    Guid TenantId { get; }
    bool IsAuthenticated { get; }
}
```

- [ ] **Step 2: Create ICheckInRepository**

```csharp
// src/ONEVO.Application/Features/Monitoring/CheckIn/RepositoryInterfaces/ICheckInRepository.cs
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;

namespace ONEVO.Application.Features.Monitoring.CheckIn.RepositoryInterfaces;

public interface ICheckInRepository
{
    Task AddCheckInAsync(EmployeeCheckIn checkIn, CancellationToken ct);
    Task<EmployeeCheckIn?> FindCheckInAsync(Guid checkInId, Guid tenantId, CancellationToken ct);
    Task AddFaceScanAsync(MonitoringFaceScan faceScan, CancellationToken ct);
    Task UpdateFaceScanStatusAsync(Guid faceScanId, string status, CancellationToken ct);
}
```

- [ ] **Step 3: Create response DTOs**

```csharp
// src/ONEVO.Application/Features/Monitoring/CheckIn/DTOs/Responses/CheckInResponseDto.cs
namespace ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;

public record CheckInResponseDto(
    Guid CheckInId,
    DateTimeOffset CheckedInAt,
    double? Latitude,
    double? Longitude,
    string? DeviceSerialNumber,
    bool FaceScanRequired);
```

```csharp
// src/ONEVO.Application/Features/Monitoring/CheckIn/DTOs/Responses/FaceScanUploadResponseDto.cs
namespace ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;

public record FaceScanUploadResponseDto(
    Guid FaceScanId,
    string Status,
    long FileSizeBytes);
```

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/CheckIn/
git commit -m "feat(monitoring): add check-in service/repo interfaces and response DTOs"
```

---

## Task 3: SubmitCheckIn Command

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/CheckIn/Commands/SubmitCheckIn/SubmitCheckInCommand.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/CheckIn/Commands/SubmitCheckIn/SubmitCheckInCommandValidator.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/CheckIn/Commands/SubmitCheckIn/SubmitCheckInCommandHandler.cs`

- [ ] **Step 1: Create command**

```csharp
// src/ONEVO.Application/Features/Monitoring/CheckIn/Commands/SubmitCheckIn/SubmitCheckInCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.CheckIn.Commands.SubmitCheckIn;

public record SubmitCheckInCommand(
    double? Latitude,
    double? Longitude,
    double? LocationAccuracy,
    string? LocationAddress,
    string? DeviceSerialNumber
) : IRequest<Result<CheckInResponseDto>>;
```

- [ ] **Step 2: Create validator**

```csharp
// src/ONEVO.Application/Features/Monitoring/CheckIn/Commands/SubmitCheckIn/SubmitCheckInCommandValidator.cs
using FluentValidation;

namespace ONEVO.Application.Features.Monitoring.CheckIn.Commands.SubmitCheckIn;

public class SubmitCheckInCommandValidator : AbstractValidator<SubmitCheckInCommand>
{
    public SubmitCheckInCommandValidator()
    {
        When(x => x.Latitude.HasValue, () =>
        {
            RuleFor(x => x.Latitude!.Value)
                .InclusiveBetween(-90, 90)
                .WithMessage("Latitude must be between -90 and 90.");
        });

        When(x => x.Longitude.HasValue, () =>
        {
            RuleFor(x => x.Longitude!.Value)
                .InclusiveBetween(-180, 180)
                .WithMessage("Longitude must be between -180 and 180.");
        });

        When(x => x.LocationAccuracy.HasValue, () =>
        {
            RuleFor(x => x.LocationAccuracy!.Value)
                .GreaterThanOrEqualTo(0)
                .WithMessage("Location accuracy cannot be negative.");
        });

        When(x => x.LocationAddress is not null, () =>
        {
            RuleFor(x => x.LocationAddress!)
                .MaximumLength(500)
                .WithMessage("Address must not exceed 500 characters.");
        });

        When(x => x.DeviceSerialNumber is not null, () =>
        {
            RuleFor(x => x.DeviceSerialNumber!)
                .MaximumLength(200)
                .WithMessage("Device serial number must not exceed 200 characters.");
        });
    }
}
```

- [ ] **Step 3: Create handler**

```csharp
// src/ONEVO.Application/Features/Monitoring/CheckIn/Commands/SubmitCheckIn/SubmitCheckInCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.Commands.SubmitCheckIn;
using ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.CheckIn.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;

namespace ONEVO.Application.Features.Monitoring.CheckIn.Commands.SubmitCheckIn;

public class SubmitCheckInCommandHandler
    : IRequestHandler<SubmitCheckInCommand, Result<CheckInResponseDto>>
{
    private readonly ICheckInRepository _repository;
    private readonly ITrayCurrentDevice _device;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public SubmitCheckInCommandHandler(
        ICheckInRepository repository,
        ITrayCurrentDevice device,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _device = device;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CheckInResponseDto>> Handle(
        SubmitCheckInCommand request,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var checkIn = new EmployeeCheckIn
        {
            Id = Guid.NewGuid(),
            TenantId = _device.TenantId,
            UserId = _device.UserId,
            DeviceRegistrationId = _device.DeviceRegistrationId,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            LocationAccuracy = request.LocationAccuracy,
            LocationAddress = request.LocationAddress,
            DeviceSerialNumber = request.DeviceSerialNumber,
            CheckedInAt = now,
            CreatedAt = now
        };

        await _repository.AddCheckInAsync(checkIn, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<CheckInResponseDto>.Success(new CheckInResponseDto(
            checkIn.Id,
            checkIn.CheckedInAt,
            checkIn.Latitude,
            checkIn.Longitude,
            checkIn.DeviceSerialNumber,
            FaceScanRequired: true));
    }
}
```

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/CheckIn/Commands/SubmitCheckIn/
git commit -m "feat(monitoring): add SubmitCheckIn command, validator, and handler"
```

---

## Task 4: UploadFaceScan Command

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/CheckIn/Commands/UploadFaceScan/UploadFaceScanCommand.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/CheckIn/Commands/UploadFaceScan/UploadFaceScanCommandValidator.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/CheckIn/Commands/UploadFaceScan/UploadFaceScanCommandHandler.cs`

- [ ] **Step 1: Create command**

```csharp
// src/ONEVO.Application/Features/Monitoring/CheckIn/Commands/UploadFaceScan/UploadFaceScanCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.CheckIn.Commands.UploadFaceScan;

public record UploadFaceScanCommand(
    Guid CheckInId,
    Stream ImageStream,
    string ContentType,
    long FileSizeBytes
) : IRequest<Result<FaceScanUploadResponseDto>>;
```

- [ ] **Step 2: Create validator**

```csharp
// src/ONEVO.Application/Features/Monitoring/CheckIn/Commands/UploadFaceScan/UploadFaceScanCommandValidator.cs
using FluentValidation;

namespace ONEVO.Application.Features.Monitoring.CheckIn.Commands.UploadFaceScan;

public class UploadFaceScanCommandValidator : AbstractValidator<UploadFaceScanCommand>
{
    private static readonly string[] AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"];
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    public UploadFaceScanCommandValidator()
    {
        RuleFor(x => x.CheckInId)
            .NotEmpty()
            .WithMessage("CheckInId is required.");

        RuleFor(x => x.ContentType)
            .Must(ct => AllowedContentTypes.Contains(ct))
            .WithMessage("Only JPEG, PNG, or WebP images are accepted.");

        RuleFor(x => x.FileSizeBytes)
            .InclusiveBetween(1, MaxFileSizeBytes)
            .WithMessage("Face scan image must be between 1 byte and 5 MB.");
    }
}
```

- [ ] **Step 3: Create handler**

```csharp
// src/ONEVO.Application/Features/Monitoring/CheckIn/Commands/UploadFaceScan/UploadFaceScanCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.CheckIn.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;

namespace ONEVO.Application.Features.Monitoring.CheckIn.Commands.UploadFaceScan;

public class UploadFaceScanCommandHandler
    : IRequestHandler<UploadFaceScanCommand, Result<FaceScanUploadResponseDto>>
{
    private readonly ICheckInRepository _repository;
    private readonly ITrayCurrentDevice _device;
    private readonly IFileStorageService _fileStorage;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public UploadFaceScanCommandHandler(
        ICheckInRepository repository,
        ITrayCurrentDevice device,
        IFileStorageService fileStorage,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _device = device;
        _fileStorage = fileStorage;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<FaceScanUploadResponseDto>> Handle(
        UploadFaceScanCommand request,
        CancellationToken cancellationToken)
    {
        var checkIn = await _repository.FindCheckInAsync(
            request.CheckInId, _device.TenantId, cancellationToken);

        if (checkIn is null)
            return Result<FaceScanUploadResponseDto>.NotFound("Check-in not found.");

        if (checkIn.UserId != _device.UserId)
            return Result<FaceScanUploadResponseDto>.Forbidden();

        var faceScanId = Guid.NewGuid();
        var ext = request.ContentType switch
        {
            "image/png" => "png",
            "image/webp" => "webp",
            _ => "jpg"
        };
        var storageKey = $"tenants/{_device.TenantId}/monitoring/face-scans/{faceScanId}/scan.{ext}";

        await _fileStorage.UploadAsync(storageKey, request.ImageStream, request.ContentType, cancellationToken);

        var now = _clock.UtcNow;
        var faceScan = new MonitoringFaceScan
        {
            Id = faceScanId,
            TenantId = _device.TenantId,
            CheckInId = request.CheckInId,
            StorageKey = storageKey,
            FileSizeBytes = request.FileSizeBytes,
            ContentType = request.ContentType,
            Status = "available",
            CreatedAt = now,
            UpdatedAt = now
        };

        await _repository.AddFaceScanAsync(faceScan, cancellationToken);

        checkIn.FaceScanId = faceScan.Id;
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<FaceScanUploadResponseDto>.Success(new FaceScanUploadResponseDto(
            faceScan.Id,
            faceScan.Status,
            faceScan.FileSizeBytes));
    }
}
```

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/CheckIn/Commands/UploadFaceScan/
git commit -m "feat(monitoring): add UploadFaceScan command, validator, and handler"
```

---

## Task 5: Infrastructure — EF Config + Repository

**Files:**
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/CheckIn/EmployeeCheckInConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/CheckIn/MonitoringFaceScanConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/CheckIn/EfCheckInRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`

- [ ] **Step 1: Create EmployeeCheckInConfiguration**

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/CheckIn/EmployeeCheckInConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.CheckIn;

public class EmployeeCheckInConfiguration : IEntityTypeConfiguration<EmployeeCheckIn>
{
    public void Configure(EntityTypeBuilder<EmployeeCheckIn> builder)
    {
        builder.ToTable("employee_check_ins");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.LocationAddress).HasMaxLength(500);
        builder.Property(e => e.DeviceSerialNumber).HasMaxLength(200);

        builder.HasOne(e => e.FaceScan)
               .WithOne()
               .HasForeignKey<EmployeeCheckIn>(e => e.FaceScanId)
               .IsRequired(false)
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => new { e.TenantId, e.UserId, e.CheckedInAt });
        builder.HasIndex(e => new { e.TenantId, e.DeviceRegistrationId });
    }
}
```

- [ ] **Step 2: Create MonitoringFaceScanConfiguration**

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/CheckIn/MonitoringFaceScanConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.CheckIn;

public class MonitoringFaceScanConfiguration : IEntityTypeConfiguration<MonitoringFaceScan>
{
    public void Configure(EntityTypeBuilder<MonitoringFaceScan> builder)
    {
        builder.ToTable("monitoring_face_scans");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.StorageKey).HasMaxLength(1000).IsRequired();
        builder.Property(f => f.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(f => f.Status).HasMaxLength(50).IsRequired();

        builder.HasIndex(f => new { f.TenantId, f.CheckInId }).IsUnique();
        builder.HasIndex(f => f.StorageKey).IsUnique();
    }
}
```

- [ ] **Step 3: Create EfCheckInRepository**

```csharp
// src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/CheckIn/EfCheckInRepository.cs
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Monitoring.CheckIn.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.CheckIn;

public class EfCheckInRepository : ICheckInRepository
{
    private readonly ApplicationDbContext _db;

    public EfCheckInRepository(ApplicationDbContext db) => _db = db;

    public async Task AddCheckInAsync(EmployeeCheckIn checkIn, CancellationToken ct)
        => await _db.EmployeeCheckIns.AddAsync(checkIn, ct);

    public async Task<EmployeeCheckIn?> FindCheckInAsync(Guid checkInId, Guid tenantId, CancellationToken ct)
        => await _db.EmployeeCheckIns
            .FirstOrDefaultAsync(c => c.Id == checkInId && c.TenantId == tenantId, ct);

    public async Task AddFaceScanAsync(MonitoringFaceScan faceScan, CancellationToken ct)
        => await _db.MonitoringFaceScans.AddAsync(faceScan, ct);

    public async Task UpdateFaceScanStatusAsync(Guid faceScanId, string status, CancellationToken ct)
    {
        await _db.MonitoringFaceScans
            .Where(f => f.Id == faceScanId)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.Status, status), ct);
    }
}
```

- [ ] **Step 4: Add DbSets to ApplicationDbContext**

In `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`, add after the existing tray activation DbSets:

```csharp
// Add usings at top:
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;

// Add DbSets (find the block of DbSet properties):
public DbSet<EmployeeCheckIn> EmployeeCheckIns => Set<EmployeeCheckIn>();
public DbSet<MonitoringFaceScan> MonitoringFaceScans => Set<MonitoringFaceScan>();
```

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/CheckIn/
git add src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/CheckIn/
git add src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs
git commit -m "feat(monitoring): add check-in EF config, repository, and DbContext DbSets"
```

---

## Task 6: TrayCurrentDeviceService + DI Registration

**Files:**
- Create: `src/ONEVO.Infrastructure/Services/Monitoring/CheckIn/TrayCurrentDeviceService.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Create TrayCurrentDeviceService**

```csharp
// src/ONEVO.Infrastructure/Services/Monitoring/CheckIn/TrayCurrentDeviceService.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.Monitoring.CheckIn;

public class TrayCurrentDeviceService : ITrayCurrentDevice
{
    private readonly IHttpContextAccessor _http;

    public TrayCurrentDeviceService(IHttpContextAccessor http) => _http = http;

    private ClaimsPrincipal? User => _http.HttpContext?.User;

    public bool IsAuthenticated =>
        User?.Identity?.IsAuthenticated == true
        && User.FindFirstValue("token_type") == "tray_device";

    public Guid DeviceRegistrationId =>
        Guid.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : Guid.Empty;

    public Guid UserId =>
        Guid.TryParse(User?.FindFirstValue("user_id"), out var id)
            ? id
            : Guid.Empty;

    public Guid TenantId =>
        Guid.TryParse(User?.FindFirstValue("tenant_id"), out var id)
            ? id
            : Guid.Empty;
}
```

- [ ] **Step 2: Register in DependencyInjection.cs**

In `src/ONEVO.Infrastructure/DependencyInjection.cs`, add inside `AddInfrastructure`:

```csharp
// Add usings:
using ONEVO.Application.Features.Monitoring.CheckIn.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.CheckIn;
using ONEVO.Infrastructure.Services.Monitoring.CheckIn;

// Add registrations:
services.AddScoped<ICheckInRepository, EfCheckInRepository>();
services.AddHttpContextAccessor(); // already registered if present — safe to call twice
services.AddScoped<ITrayCurrentDevice, TrayCurrentDeviceService>();
```

- [ ] **Step 3: Commit**

```bash
git add src/ONEVO.Infrastructure/Services/Monitoring/CheckIn/
git add src/ONEVO.Infrastructure/DependencyInjection.cs
git commit -m "feat(monitoring): add TrayCurrentDeviceService and DI registrations"
```

---

## Task 7: TrayDeviceScheme + TrayDevicePolicy

**Files:**
- Modify: `src/ONEVO.Api/Extensions/AuthenticationExtensions.cs`
- Modify: `src/ONEVO.Api/Extensions/AuthorizationExtensions.cs`

- [ ] **Step 1: Add TrayDeviceScheme to AuthenticationExtensions.cs**

Add the following using and chain `.AddJwtBearer` after the existing `.AddCookie("AdminScheme", ...)` call:

```csharp
// Add usings at top:
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

// Inside AddApiAuthentication, after .AddCookie("AdminScheme", ...) block, chain:
.AddJwtBearer("TrayDeviceScheme", options =>
{
    var section = services.BuildServiceProvider()
        .GetRequiredService<IConfiguration>()
        .GetSection("TrayApp:Jwt");

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = section["Issuer"] ?? "onevo-tray",
        ValidateAudience = true,
        ValidAudience = section["Audience"] ?? "onevo-tray-app",
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(section["Secret"]
                ?? throw new InvalidOperationException("TrayApp:Jwt:Secret is required."))),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30)
    };

    options.Events = new JwtBearerEvents
    {
        OnChallenge = context =>
        {
            context.HandleResponse();
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return context.Response.WriteAsJsonAsync(new
            {
                type = "https://onevo.com/errors/unauthorized",
                title = "Unauthorized",
                status = 401,
                detail = "A valid tray device token is required."
            });
        }
    };
})
```

> **Note:** `services.BuildServiceProvider()` inside `AddApiAuthentication` creates a service locator anti-pattern. A cleaner approach is to pass `IConfiguration` as a parameter: add `IConfiguration configuration` param to `AddApiAuthentication` and read from it directly. The existing callers in `Program.cs` already pass `builder.Configuration` context — update the call site accordingly. Either approach is acceptable for this codebase; choose the one that matches the existing style.

- [ ] **Step 2: Add TrayDevicePolicy to AuthorizationExtensions.cs**

```csharp
// Inside AddApiAuthorization, add after the existing AdminPolicy:
options.AddPolicy("TrayDevicePolicy", policy =>
    policy.AddAuthenticationSchemes("TrayDeviceScheme")
          .RequireAuthenticatedUser()
          .RequireClaim("token_type", "tray_device"));
```

- [ ] **Step 3: Commit**

```bash
git add src/ONEVO.Api/Extensions/AuthenticationExtensions.cs
git add src/ONEVO.Api/Extensions/AuthorizationExtensions.cs
git commit -m "feat(monitoring): add TrayDeviceScheme JWT bearer auth and TrayDevicePolicy"
```

---

## Task 8: EF Migration

- [ ] **Step 1: Generate migration**

```bash
dotnet ef migrations add AddMonitoringCheckIn \
  --project src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj \
  --startup-project src/ONEVO.Api/ONEVO.Api.csproj
```

Expected output: `Done. To undo this action, use 'ef migrations remove'`

- [ ] **Step 2: Review generated migration**

Open `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddMonitoringCheckIn.cs` and verify:
- `employee_check_ins` table created with all columns
- `monitoring_face_scans` table created with all columns
- All indexes present
- FK from `employee_check_ins.face_scan_id` → `monitoring_face_scans.id`

- [ ] **Step 3: Apply migration locally**

Run the setup script:
```powershell
.\ops\postgres\setup-local-db.ps1 -RunMigrations
```

Expected: `EF migrations completed successfully.`

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Infrastructure/Migrations/
git commit -m "feat(monitoring): add EF migration for employee_check_ins and monitoring_face_scans tables"
```

---

## Task 9: API Controller

**Files:**
- Create: `src/ONEVO.Api/Controllers/Tenant/Monitoring/CheckIn/MonitoringCheckInController.cs`

- [ ] **Step 1: Create controller**

```csharp
// src/ONEVO.Api/Controllers/Tenant/Monitoring/CheckIn/MonitoringCheckInController.cs
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.CheckIn.Commands.SubmitCheckIn;
using ONEVO.Application.Features.Monitoring.CheckIn.Commands.UploadFaceScan;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.CheckIn;

[ApiController]
[Route("api/v1/monitoring/check-in")]
[Authorize(Policy = "TrayDevicePolicy")]
public class MonitoringCheckInController : ControllerBase
{
    private readonly IMediator _mediator;

    public MonitoringCheckInController(IMediator mediator)
        => _mediator = mediator;

    /// <summary>
    /// Submit a check-in with location and device serial number.
    /// Called by the tray app immediately on check-in action.
    /// Authorization: Bearer {tray_access_token}
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> SubmitCheckIn(
        [FromBody] SubmitCheckInRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new SubmitCheckInCommand(
            request.Latitude,
            request.Longitude,
            request.LocationAccuracy,
            request.LocationAddress,
            request.DeviceSerialNumber), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }

    /// <summary>
    /// Upload a face scan photo for a previously submitted check-in.
    /// Accepts multipart/form-data with a single "face_scan" file field.
    /// Authorization: Bearer {tray_access_token}
    /// </summary>
    [HttpPost("{checkInId:guid}/face-scan")]
    [RequestSizeLimit(6 * 1024 * 1024)] // 6 MB limit (5 MB image + overhead)
    public async Task<IActionResult> UploadFaceScan(
        Guid checkInId,
        IFormFile faceScan,
        CancellationToken ct)
    {
        if (faceScan is null || faceScan.Length == 0)
            return Problem("face_scan file is required.", statusCode: 400);

        var result = await _mediator.Send(new UploadFaceScanCommand(
            checkInId,
            faceScan.OpenReadStream(),
            faceScan.ContentType,
            faceScan.Length), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }
}

public record SubmitCheckInRequest(
    double? Latitude,
    double? Longitude,
    double? LocationAccuracy,
    string? LocationAddress,
    string? DeviceSerialNumber);
```

- [ ] **Step 2: Commit**

```bash
git add src/ONEVO.Api/Controllers/Tenant/Monitoring/CheckIn/
git commit -m "feat(monitoring): add MonitoringCheckInController with submit and face-scan endpoints"
```

---

## Task 10: Integration Tests

**Files:**
- Create: `tests/ONEVO.Tests.Integration/Monitoring/CheckIn/CheckInTestFactory.cs`
- Create: `tests/ONEVO.Tests.Integration/Monitoring/CheckIn/CheckInIntegrationTests.cs`

- [ ] **Step 1: Create CheckInTestFactory**

```csharp
// tests/ONEVO.Tests.Integration/Monitoring/CheckIn/CheckInTestFactory.cs
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Tests.Integration.Monitoring.CheckIn;

public sealed class CheckInTestFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public CheckInTestFactory(string connectionString) => _connectionString = connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((ctx, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["Jwt:Secret"] = "checkin-test-jwt-secret-min-32-chars-long!!",
                ["Jwt:TenantIssuer"] = "onevo-api",
                ["Jwt:TenantAudience"] = "onevo-api",
                ["TrayApp:Jwt:Secret"] = "checkin-test-tray-jwt-secret-min-32chars!!",
                ["TrayApp:Jwt:Issuer"] = "onevo-tray",
                ["TrayApp:Jwt:Audience"] = "onevo-tray-app",
                ["Tenancy:RootDomain"] = "localhost",
                ["Urls:AppBaseUrl"] = "https://localhost",
                ["Encryption:MasterKey"] = "checkin-test-master-key-32-chars-xxxxxx!",
                ["PlatformBootstrap:SuperAdminEmail"] = "test_admin@onevo.dev",
                ["PlatformBootstrap:SuperAdminFullName"] = "CheckIn Test Admin"
            });
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (descriptor != null) services.Remove(descriptor);

            services.AddDbContext<ApplicationDbContext>((sp, options) =>
                options.UseNpgsql(_connectionString).UseSnakeCaseNamingConvention());

            services.RemoveAll<ITotpService>();
            services.AddSingleton<ITotpService, AlwaysValidTotpService>();
            services.RemoveAll<IGoogleIdTokenValidator>();
            services.AddSingleton<IGoogleIdTokenValidator, EmailAsGoogleTokenValidator>();
            services.RemoveAll<IPlatformOAuthAppResolver>();
            services.AddSingleton<IPlatformOAuthAppResolver, TestPlatformOAuthAppResolver>();

            // Stub out R2 file storage for tests
            services.RemoveAll<IFileStorageService>();
            services.AddSingleton<IFileStorageService, NoOpFileStorageService>();
        });
    }

    private sealed class AlwaysValidTotpService : ITotpService
    {
        public bool Verify(string base32Secret, string code) => code == "123456";
    }

    private sealed class EmailAsGoogleTokenValidator : IGoogleIdTokenValidator
    {
        public Task<GoogleIdTokenPayload?> ValidateAsync(string idToken, string expectedAudience, CancellationToken ct = default)
            => Task.FromResult<GoogleIdTokenPayload?>(new GoogleIdTokenPayload(idToken, idToken, true, "Test User"));
    }

    private sealed class TestPlatformOAuthAppResolver : IPlatformOAuthAppResolver
    {
        public Task<ResolvedPlatformOAuthApp?> GetActiveAppForProviderAsync(string provider, CancellationToken ct = default)
            => Task.FromResult<ResolvedPlatformOAuthApp?>(new ResolvedPlatformOAuthApp(provider, "test-client", "https://auth.example.com/authorize", "https://auth.example.com/token", []));

        public Task<ResolvedPlatformOAuthAppCredential?> GetActiveCredentialForProviderAsync(string provider, CancellationToken ct = default)
            => Task.FromResult<ResolvedPlatformOAuthAppCredential?>(null);
    }

    // No-op storage: tests don't need real R2
    private sealed class NoOpFileStorageService : IFileStorageService
    {
        public Task UploadAsync(string key, Stream stream, string contentType, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeleteAsync(string key, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<Stream> DownloadAsync(string key, CancellationToken ct = default)
            => Task.FromResult<Stream>(Stream.Null);

        public Task<string> GetSignedUrlAsync(string key, TimeSpan expiry, CancellationToken ct = default)
            => Task.FromResult($"https://r2.example.com/{key}?signed=test");
    }
}
```

- [ ] **Step 2: Create CheckInIntegrationTests**

```csharp
// tests/ONEVO.Tests.Integration/Monitoring/CheckIn/CheckInIntegrationTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;

namespace ONEVO.Tests.Integration.Monitoring.CheckIn;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class CheckInIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_checkin_integration_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private CheckInTestFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var cs = _postgres.GetConnectionString();
        await IntegrationDatabaseBootstrap.InitializeAsync(cs);
        _environmentScope = new IntegrationTestEnvironmentScope(cs);
        _factory = new CheckInTestFactory(cs);
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

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<string> GetTrayJwtAsync()
    {
        // Seed a tenant user, activate a device via generate→exchange, return the access_token
        var tenantSlug = $"checkin-{Guid.NewGuid():N}"[..20];
        var user = await SeedActiveUserWithTenantAsync(tenantSlug, $"{tenantSlug}@test.dev", "TestPass1!");
        var session = await LoginAndGetSessionAsync(user, tenantSlug);

        var genResp = await PostWithSessionAsync("/api/v1/monitoring/activation/generate", null, session);
        genResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var genBody = await genResp.Content.ReadFromJsonAsync<JsonElement>();
        var code = genBody.GetProperty("code").GetString()!;

        var exchResp = await _client.PostAsJsonAsync("/api/v1/monitoring/activation/exchange", new
        {
            code,
            device_name = "Test Device",
            device_os = "Windows",
            device_fingerprint = "test-fp-001"
        });
        exchResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var exchBody = await exchResp.Content.ReadFromJsonAsync<JsonElement>();
        return exchBody.GetProperty("access_token").GetString()!;
    }

    private HttpRequestMessage TrayRequest(HttpMethod method, string path, object? body = null)
    {
        // JWT is set per test — caller sets Authorization header
        var req = new HttpRequestMessage(method, path);
        if (body is not null)
            req.Content = JsonContent.Create(body);
        return req;
    }

    // ── Migrations ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Migrations_ApplyCleanly_AndLeaveNoPendingMigrations()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pending = await db.Database.GetPendingMigrationsAsync();
        pending.Should().BeEmpty();
    }

    // ── SubmitCheckIn ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitCheckIn_WithValidTrayJwt_Returns200AndPersistsRecord()
    {
        var jwt = await GetTrayJwtAsync();
        var req = TrayRequest(HttpMethod.Post, "/api/v1/monitoring/check-in", new
        {
            latitude = 6.9271,
            longitude = 79.8612,
            location_accuracy = 15.0,
            location_address = "Colombo, Sri Lanka",
            device_serial_number = "SN-TEST-001"
        });
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var resp = await _client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("check_in_id").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("latitude").GetDouble().Should().BeApproximately(6.9271, 0.0001);
        body.GetProperty("device_serial_number").GetString().Should().Be("SN-TEST-001");

        // Verify DB record
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var checkInId = Guid.Parse(body.GetProperty("check_in_id").GetString()!);
        var record = await db.EmployeeCheckIns.FindAsync(checkInId);
        record.Should().NotBeNull();
        record!.Latitude.Should().BeApproximately(6.9271, 0.0001);
        record.DeviceSerialNumber.Should().Be("SN-TEST-001");
    }

    [Fact]
    public async Task SubmitCheckIn_WithoutJwt_Returns401()
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/monitoring/check-in", new
        {
            latitude = 6.9271,
            longitude = 79.8612
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SubmitCheckIn_WithInvalidLatitude_Returns400()
    {
        var jwt = await GetTrayJwtAsync();
        var req = TrayRequest(HttpMethod.Post, "/api/v1/monitoring/check-in", new
        {
            latitude = 999.0,  // invalid
            longitude = 79.8612
        });
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var resp = await _client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SubmitCheckIn_WithNoLocationOrDevice_Returns200()
    {
        // All fields optional — minimal check-in is valid
        var jwt = await GetTrayJwtAsync();
        var req = TrayRequest(HttpMethod.Post, "/api/v1/monitoring/check-in", new { });
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var resp = await _client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── UploadFaceScan ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UploadFaceScan_AfterCheckIn_Returns200AndPersistsMetadata()
    {
        var jwt = await GetTrayJwtAsync();

        // Submit check-in first
        var checkInReq = TrayRequest(HttpMethod.Post, "/api/v1/monitoring/check-in", new
        {
            latitude = 6.9271,
            longitude = 79.8612
        });
        checkInReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var checkInResp = await _client.SendAsync(checkInReq);
        checkInResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var checkInBody = await checkInResp.Content.ReadFromJsonAsync<JsonElement>();
        var checkInId = checkInBody.GetProperty("check_in_id").GetString()!;

        // Upload face scan (1x1 JPEG)
        var fakeJpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x01, 0xFF, 0xD9 };
        using var form = new MultipartFormDataContent();
        var imageContent = new ByteArrayContent(fakeJpeg);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(imageContent, "face_scan", "scan.jpg");

        var scanReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/monitoring/check-in/{checkInId}/face-scan")
        {
            Content = form
        };
        scanReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var scanResp = await _client.SendAsync(scanReq);

        scanResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var scanBody = await scanResp.Content.ReadFromJsonAsync<JsonElement>();
        scanBody.GetProperty("face_scan_id").GetString().Should().NotBeNullOrEmpty();
        scanBody.GetProperty("status").GetString().Should().Be("available");

        // Verify DB
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var faceScan = await db.MonitoringFaceScans
            .FirstOrDefaultAsync(f => f.CheckInId == Guid.Parse(checkInId));
        faceScan.Should().NotBeNull();
        faceScan!.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task UploadFaceScan_WithWrongContentType_Returns400()
    {
        var jwt = await GetTrayJwtAsync();

        var checkInReq = TrayRequest(HttpMethod.Post, "/api/v1/monitoring/check-in", new { });
        checkInReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var checkInResp = await _client.SendAsync(checkInReq);
        var checkInId = (await checkInResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("check_in_id").GetString()!;

        using var form = new MultipartFormDataContent();
        var pdfContent = new ByteArrayContent(new byte[] { 0x25, 0x50, 0x44, 0x46 });
        pdfContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(pdfContent, "face_scan", "scan.pdf");

        var scanReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/monitoring/check-in/{checkInId}/face-scan")
        {
            Content = form
        };
        scanReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var resp = await _client.SendAsync(scanReq);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadFaceScan_ForAnotherUsersCheckIn_Returns403()
    {
        var jwt1 = await GetTrayJwtAsync();
        var jwt2 = await GetTrayJwtAsync(); // different user/device

        // User1 submits check-in
        var checkInReq = TrayRequest(HttpMethod.Post, "/api/v1/monitoring/check-in", new { });
        checkInReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt1);
        var checkInResp = await _client.SendAsync(checkInReq);
        var checkInId = (await checkInResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("check_in_id").GetString()!;

        // User2 tries to upload scan for User1's check-in
        var fakeJpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x01, 0xFF, 0xD9 };
        using var form = new MultipartFormDataContent();
        var img = new ByteArrayContent(fakeJpeg);
        img.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(img, "face_scan", "scan.jpg");

        var scanReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/monitoring/check-in/{checkInId}/face-scan")
        {
            Content = form
        };
        scanReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt2);

        var resp = await _client.SendAsync(scanReq);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Helpers (seed/login) — match TrayActivationIntegrationTests pattern ───

    private Task<(Guid UserId, Guid TenantId, string Email)> SeedActiveUserWithTenantAsync(
        string slug, string email, string password)
    {
        // Use IntegrationTestEnvironmentScope to seed a tenant + user directly in DB
        return _environmentScope.SeedTenantUserAsync(slug, email, password);
    }

    private Task<string> LoginAndGetSessionAsync(
        (Guid UserId, Guid TenantId, string Email) user, string slug)
    {
        return _environmentScope.LoginAndGetSessionCookieAsync(_client, user.Email, "TestPass1!", slug);
    }

    private Task<HttpResponseMessage> PostWithSessionAsync(string path, object? body, string sessionCookie)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path);
        req.Headers.Add("Cookie", sessionCookie);
        if (body is not null) req.Content = JsonContent.Create(body);
        return _client.SendAsync(req);
    }
}
```

- [ ] **Step 3: Run tests (expect failures until implementation is complete)**

```bash
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj \
  --filter "FullyQualifiedName~CheckIn" -v normal
```

Expected: Tests compile and run. Failures will show which pieces are still missing.

- [ ] **Step 4: Fix any compile errors, then run until green**

Run:
```bash
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj \
  --filter "FullyQualifiedName~CheckIn" -v normal
```

Expected: All 7 tests pass. ✅

- [ ] **Step 5: Commit**

```bash
git add tests/ONEVO.Tests.Integration/Monitoring/CheckIn/
git commit -m "test(monitoring): add check-in integration tests for submit, face scan, and auth"
```

---

## Task 11: Postman Collection Update

Update `ONEVO-HRMS.postman_collection.json` — add 2 new requests under `🏢 Tenant API → Monitoring — Tray Activation`:

**New request 1 — Submit Check-In:**
- Method: `POST`
- URL: `{{baseUrl}}/api/v1/monitoring/check-in`
- Headers: `Authorization: Bearer {{trayAccessToken}}`, `Content-Type: application/json`
- Body:
```json
{
  "latitude": 6.9271,
  "longitude": 79.8612,
  "location_accuracy": 15.0,
  "location_address": "Colombo, Sri Lanka",
  "device_serial_number": "SN-001"
}
```

**New request 2 — Upload Face Scan:**
- Method: `POST`
- URL: `{{baseUrl}}/api/v1/monitoring/check-in/{{checkInId}}/face-scan`
- Headers: `Authorization: Bearer {{trayAccessToken}}`
- Body: `form-data` → key: `face_scan`, type: `File`

Add `trayAccessToken` and `checkInId` to collection variables.

Add a test script on "Exchange Activation Code" to extract and store `access_token`:
```javascript
const body = pm.response.json();
if (body.access_token) pm.collectionVariables.set('trayAccessToken', body.access_token);
```

- [ ] **Commit:**

```bash
git add ONEVO-HRMS.postman_collection.json
git commit -m "chore(postman): add check-in and face-scan endpoints to collection"
```

---

## Self-Review

**Spec coverage:**
| Requirement | Covered by |
|-------------|------------|
| Tray app authenticated via JWT | Task 7 — TrayDeviceScheme + TrayDevicePolicy |
| Employee location submission | Task 3 — SubmitCheckInCommand (Latitude, Longitude, Accuracy, Address) |
| Face scan upload | Task 4 — UploadFaceScanCommand (multipart, R2 key stored) |
| Device serial number | Task 3 — SubmitCheckInCommand (DeviceSerialNumber) |
| Data persisted in DB | Task 5 — EF Config + Repository |
| File stored in R2 (metadata in DB) | Task 4 — IFileStorageService.UploadAsync + MonitoringFaceScan entity |
| Integration tests | Task 10 — 7 tests covering happy path + auth + validation |
| Postman testable | Task 11 |

**Placeholder scan:** None found. All code blocks are complete.

**Type consistency:** `CheckInResponseDto`, `FaceScanUploadResponseDto`, `SubmitCheckInCommand`, `UploadFaceScanCommand` names consistent across Tasks 2–4 and 9.
