# Agent Enrollment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow HR admins to generate one-time activation codes so employees can enroll their tray app (desktop agent) and receive a long-lived Device JWT for all subsequent API calls.

**Architecture:** HR generates a 48-hour one-time code stored as a SHA-256 hash; the tray app posts the code to a public `/api/v1/agent/enroll` endpoint (tenant context is resolved by host middleware before the handler runs); the backend validates atomically, creates an `AgentDevice` row, and issues a 90-day JWT signed with a separate `Jwt:AgentSecret` key validated by a dedicated `AgentScheme` JWT bearer. Revocation is real-time: agent endpoints check `AgentDevice.IsRevoked` on every call and return 401 if the device has been revoked by HR.

**Tech Stack:** ASP.NET Core 8, MediatR, EF Core 8 + Npgsql, Microsoft.AspNetCore.Authentication.JwtBearer, xUnit + Moq

---

## File Map

### New files
| File | Layer | Responsibility |
|------|-------|----------------|
| `src/ONEVO.Domain/Features/Agent/Enrollment/Entities/AgentActivationCode.cs` | Domain | Entity: one-time code row |
| `src/ONEVO.Domain/Features/Agent/Enrollment/Entities/AgentDevice.cs` | Domain | Entity: enrolled device row |
| `src/ONEVO.Application/Features/Agent/Enrollment/RepositoryInterfaces/IAgentActivationCodeRepository.cs` | Application | Read/write/atomic-mark-used |
| `src/ONEVO.Application/Features/Agent/Enrollment/RepositoryInterfaces/IAgentDeviceRepository.cs` | Application | Read/write/revoke |
| `src/ONEVO.Application/Features/Agent/Enrollment/Commands/GenerateActivationCode/GenerateActivationCodeCommand.cs` | Application | HR generates code |
| `src/ONEVO.Application/Features/Agent/Enrollment/Commands/GenerateActivationCode/GenerateActivationCodeCommandHandler.cs` | Application | |
| `src/ONEVO.Application/Features/Agent/Enrollment/Commands/EnrollAgent/EnrollAgentCommand.cs` | Application | Tray app enrolls |
| `src/ONEVO.Application/Features/Agent/Enrollment/Commands/EnrollAgent/EnrollAgentCommandHandler.cs` | Application | |
| `src/ONEVO.Application/Features/Agent/Enrollment/Commands/RevokeAgentDevice/RevokeAgentDeviceCommand.cs` | Application | HR revokes device |
| `src/ONEVO.Application/Features/Agent/Enrollment/Commands/RevokeAgentDevice/RevokeAgentDeviceCommandHandler.cs` | Application | |
| `src/ONEVO.Application/Features/Agent/Enrollment/DTOs/ActivationCodeDto.cs` | Application | HR response shape |
| `src/ONEVO.Application/Features/Agent/Enrollment/DTOs/AgentEnrollmentDto.cs` | Application | Tray app response shape |
| `src/ONEVO.Infrastructure/Persistence/Configurations/Agent/AgentActivationCodeConfiguration.cs` | Infra | EF table mapping |
| `src/ONEVO.Infrastructure/Persistence/Configurations/Agent/AgentDeviceConfiguration.cs` | Infra | EF table mapping |
| `src/ONEVO.Infrastructure/Persistence/Repositories/Agent/EfAgentRepository.cs` | Infra | Implements both repo interfaces |
| `src/ONEVO.Api/Controllers/Tenant/Hr/AgentActivationController.cs` | API | `POST /api/v1/activation-codes` (TenantPolicy) |
| `src/ONEVO.Api/Controllers/Agent/AgentEnrollController.cs` | API | `POST /api/v1/agent/enroll` (AllowAnonymous) |
| `src/ONEVO.Api/Controllers/Agent/AgentController.cs` | API | `GET /api/v1/agent/heartbeat`, `DELETE /api/v1/agent/devices/{id}` (AgentPolicy / TenantPolicy) |
| `tests/ONEVO.Tests.Unit/Features/Agent/GenerateActivationCodeCommandHandlerTests.cs` | Tests | Unit |
| `tests/ONEVO.Tests.Unit/Features/Agent/EnrollAgentCommandHandlerTests.cs` | Tests | Unit |

### Modified files
| File | What changes |
|------|-------------|
| `src/ONEVO.Application/Features/Auth/Login/ServiceInterfaces/IJwtTokenService.cs` | Rename method, add `agent_id` param |
| `src/ONEVO.Infrastructure/Identity/JwtTokenService.cs` | Separate `AgentSecret`, 90d expiry, updated claims |
| `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` | Add two DbSets |
| `src/ONEVO.Infrastructure/DependencyInjection.cs` | Register `EfAgentRepository` + repo interfaces |
| `src/ONEVO.Api/Extensions/AuthenticationExtensions.cs` | Add `AgentScheme` JWT bearer |
| `src/ONEVO.Api/Extensions/AuthorizationExtensions.cs` | Add `AgentPolicy` |
| `src/ONEVO.Api/appsettings.json` | Add `Jwt:AgentSecret` placeholder |

---

## Task 1: Update IJwtTokenService — separate key, 90-day expiry, correct claims

**Files:**
- Modify: `src/ONEVO.Application/Features/Auth/Login/ServiceInterfaces/IJwtTokenService.cs`
- Modify: `src/ONEVO.Infrastructure/Identity/JwtTokenService.cs`
- Modify: `src/ONEVO.Api/appsettings.json`

- [ ] **Step 1: Verify nothing else calls `GenerateDeviceToken`**

```bash
grep -r "GenerateDeviceToken" src/
```
Expected: only `IJwtTokenService.cs` (definition) and `JwtTokenService.cs` (implementation). No callers yet.

- [ ] **Step 2: Replace `IJwtTokenService.cs`**

Full file content:
```csharp
namespace ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

/// <summary>
/// Issues JWTs for device/agent APIs only.
/// Browser authentication uses opaque server-side cookies, not this service.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Issues a 90-day agent JWT.
    /// Claims: sub=agentId, agent_id=agentId, tenant_id, type="agent".
    /// Signed with Jwt:AgentSecret (separate from user cookie secret).
    /// </summary>
    string GenerateAgentToken(Guid agentId, Guid tenantId);
}
```

- [ ] **Step 3: Replace `JwtTokenService.cs`**

Full file content:
```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

namespace ONEVO.Infrastructure.Identity;

/// <summary>
/// Issues 90-day JWTs for enrolled desktop agents.
/// Uses Jwt:AgentSecret — a key that is separate from any user-session secret.
/// </summary>
public class JwtTokenService : IJwtTokenService
{
    private readonly string _agentSecret;
    private readonly string _issuer;

    public JwtTokenService(IConfiguration configuration)
    {
        _agentSecret = configuration["Jwt:AgentSecret"]
            ?? throw new InvalidOperationException("Jwt:AgentSecret is required.");
        _issuer = configuration["Jwt:TenantIssuer"] ?? "onevo-api";
    }

    public string GenerateAgentToken(Guid agentId, Guid tenantId)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, agentId.ToString()),
            new("agent_id", agentId.ToString()),
            new("tenant_id", tenantId.ToString()),
            new("type", "agent")
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_agentSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: "onevo-agent",
            claims: claims,
            expires: DateTime.UtcNow.AddDays(90),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

- [ ] **Step 4: Add `Jwt:AgentSecret` to `appsettings.json`**

In the `"Jwt"` section, add one line:
```json
"Jwt": {
  "TenantIssuer": "onevo-api",
  "TenantAudience": "onevo-api",
  "Secret": "CHANGE_ME_tenant_secret_min_32_chars_!!",
  "AgentSecret": "CHANGE_ME_agent_secret_min_32_chars_!!"
}
```

- [ ] **Step 5: Add `ONEVO_JWT_AGENT_SECRET` to `.env.example`**

Open `.env.example`, add after the existing JWT secret line:
```
ONEVO_JWT_AGENT_SECRET=<local-agent-jwt-secret-min-32-chars>
```

- [ ] **Step 6: Build to confirm no compilation errors**

```bash
dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/Auth/Login/ServiceInterfaces/IJwtTokenService.cs
git add src/ONEVO.Infrastructure/Identity/JwtTokenService.cs
git add src/ONEVO.Api/appsettings.json
git add .env.example
git commit -m "feat(agent): separate AgentSecret JWT key, 90-day expiry, rename to GenerateAgentToken"
```

---

## Task 2: Register AgentScheme JWT Bearer + AgentPolicy

**Files:**
- Modify: `src/ONEVO.Api/Extensions/AuthenticationExtensions.cs`
- Modify: `src/ONEVO.Api/Extensions/AuthorizationExtensions.cs`

- [ ] **Step 1: Add `Microsoft.AspNetCore.Authentication.JwtBearer` NuGet package**

```bash
dotnet add src/ONEVO.Api/ONEVO.Api.csproj package Microsoft.AspNetCore.Authentication.JwtBearer
```
Expected: package added successfully.

- [ ] **Step 2: Add `AgentScheme` JWT bearer to `AuthenticationExtensions.cs`**

Add these two `using` statements at the top of the file:
```csharp
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
```

Then, inside `AddApiAuthentication`, change the method signature to accept `IConfiguration`:
```csharp
internal static IServiceCollection AddApiAuthentication(
    this IServiceCollection services,
    IWebHostEnvironment env,
    IConfiguration configuration)
```

After the existing `.AddCookie("AdminScheme", ...)` block, add:
```csharp
            .AddJwtBearer("AgentScheme", options =>
            {
                var secret = configuration["Jwt:AgentSecret"]
                    ?? throw new InvalidOperationException("Jwt:AgentSecret is required.");
                var issuer = configuration["Jwt:TenantIssuer"] ?? "onevo-api";

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = "onevo-agent",
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
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
                            detail = "A valid agent token is required."
                        });
                    }
                };
            });
```

- [ ] **Step 3: Fix the call site in `Program.cs`**

Open `src/ONEVO.Api/Program.cs`, find the line calling `AddApiAuthentication` and update it to pass `builder.Configuration`:
```csharp
builder.Services.AddApiAuthentication(builder.Environment, builder.Configuration);
```

- [ ] **Step 4: Add `AgentPolicy` to `AuthorizationExtensions.cs`**

Inside `AddApiAuthorization`, after the existing `AdminPolicy` block:
```csharp
            options.AddPolicy("AgentPolicy", policy =>
                policy.AddAuthenticationSchemes("AgentScheme")
                      .RequireAuthenticatedUser()
                      .RequireClaim("type", "agent"));
```

- [ ] **Step 5: Build**

```bash
dotnet build src/ONEVO.Api/ONEVO.Api.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Api/Extensions/AuthenticationExtensions.cs
git add src/ONEVO.Api/Extensions/AuthorizationExtensions.cs
git add src/ONEVO.Api/Program.cs
git add src/ONEVO.Api/ONEVO.Api.csproj
git commit -m "feat(agent): register AgentScheme JWT bearer and AgentPolicy"
```

---

## Task 3: Domain Entities — AgentActivationCode + AgentDevice

**Files:**
- Create: `src/ONEVO.Domain/Features/Agent/Enrollment/Entities/AgentActivationCode.cs`
- Create: `src/ONEVO.Domain/Features/Agent/Enrollment/Entities/AgentDevice.cs`

- [ ] **Step 1: Create `AgentActivationCode.cs`**

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Agent.Enrollment.Entities;

/// <summary>
/// A one-time activation code generated by HR to enroll a tray app.
/// Only the SHA-256 hash is stored; the plaintext is returned once and never persisted.
/// </summary>
public class AgentActivationCode : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid UserId { get; set; }

    /// <summary>SHA-256 hex hash of the plaintext code.</summary>
    public string CodeHash { get; set; } = string.Empty;

    /// <summary>pending | used | revoked</summary>
    public string Status { get; set; } = "pending";

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid CreatedById { get; set; }

    public bool IsPending(DateTimeOffset now) =>
        Status == "pending" && ExpiresAt > now;
}
```

- [ ] **Step 2: Create `AgentDevice.cs`**

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Agent.Enrollment.Entities;

/// <summary>
/// A tray app instance enrolled via an activation code.
/// IsRevoked=true causes the next heartbeat/API call to return 401.
/// </summary>
public class AgentDevice : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid UserId { get; set; }
    public Guid ActivationCodeId { get; set; }

    public string MachineId { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public string Arch { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = string.Empty;

    public bool IsRevoked { get; set; } = false;
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedById { get; set; }

    public DateTimeOffset EnrolledAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeenAt { get; set; }
}
```

- [ ] **Step 3: Build Domain project**

```bash
dotnet build src/ONEVO.Domain/ONEVO.Domain.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Domain/Features/Agent/
git commit -m "feat(agent): add AgentActivationCode and AgentDevice domain entities"
```

---

## Task 4: EF Configurations + DbContext DbSets + Migration

**Files:**
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/Agent/AgentActivationCodeConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/Agent/AgentDeviceConfiguration.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`

- [ ] **Step 1: Create `AgentActivationCodeConfiguration.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Agent.Enrollment.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Agent;

public class AgentActivationCodeConfiguration : IEntityTypeConfiguration<AgentActivationCode>
{
    public void Configure(EntityTypeBuilder<AgentActivationCode> builder)
    {
        builder.ToTable("agent_activation_codes");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.CodeHash).HasMaxLength(128).IsRequired();
        builder.Property(c => c.Status).HasMaxLength(20).IsRequired();

        builder.HasIndex(c => c.CodeHash).IsUnique();
        builder.HasIndex(c => new { c.TenantId, c.EmployeeId });
        builder.HasIndex(c => c.ExpiresAt);
    }
}
```

- [ ] **Step 2: Create `AgentDeviceConfiguration.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Agent.Enrollment.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Agent;

public class AgentDeviceConfiguration : IEntityTypeConfiguration<AgentDevice>
{
    public void Configure(EntityTypeBuilder<AgentDevice> builder)
    {
        builder.ToTable("agent_devices");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.MachineId).HasMaxLength(255).IsRequired();
        builder.Property(d => d.Hostname).HasMaxLength(255).IsRequired();
        builder.Property(d => d.OsVersion).HasMaxLength(100).IsRequired();
        builder.Property(d => d.Arch).HasMaxLength(20).IsRequired();
        builder.Property(d => d.AgentVersion).HasMaxLength(50).IsRequired();

        builder.HasIndex(d => new { d.TenantId, d.EmployeeId });
        builder.HasIndex(d => d.ActivationCodeId).IsUnique();
    }
}
```

- [ ] **Step 3: Add DbSets to `ApplicationDbContext.cs`**

After `// CoreHR` section (around line 138), add:
```csharp
    // Agent Enrollment
    public DbSet<AgentActivationCode> AgentActivationCodes => Set<AgentActivationCode>();
    public DbSet<AgentDevice> AgentDevices => Set<AgentDevice>();
```

Also add the using at the top of `ApplicationDbContext.cs`:
```csharp
using ONEVO.Domain.Features.Agent.Enrollment.Entities;
```

- [ ] **Step 4: Build Infrastructure project**

```bash
dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 5: Generate EF migration**

```bash
dotnet ef migrations add AddAgentEnrollment \
  --project src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj \
  --startup-project src/ONEVO.Api/ONEVO.Api.csproj \
  --context ApplicationDbContext
```
Expected: `Done. To undo this action, use 'ef migrations remove'`

- [ ] **Step 6: Review the generated migration file**

Open the new file in `src/ONEVO.Infrastructure/Migrations/`. Verify it contains:
- `CREATE TABLE agent_activation_codes` with columns: `id`, `tenant_id`, `employee_id`, `user_id`, `code_hash`, `status`, `expires_at`, `used_at`, `revoked_at`, `revoked_by_id`, `created_at`, `created_by_id`
- `CREATE TABLE agent_devices` with columns: `id`, `tenant_id`, `employee_id`, `user_id`, `activation_code_id`, `machine_id`, `hostname`, `os_version`, `arch`, `agent_version`, `is_revoked`, `revoked_at`, `revoked_by_id`, `enrolled_at`, `last_seen_at`
- Unique index on `agent_activation_codes.code_hash`
- Unique index on `agent_devices.activation_code_id`

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Configurations/Agent/
git add src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs
git add src/ONEVO.Infrastructure/Migrations/
git commit -m "feat(agent): EF configurations and migration for agent_activation_codes + agent_devices"
```

---

## Task 5: Repository Interfaces + EfAgentRepository + DI Registration

**Files:**
- Create: `src/ONEVO.Application/Features/Agent/Enrollment/RepositoryInterfaces/IAgentActivationCodeRepository.cs`
- Create: `src/ONEVO.Application/Features/Agent/Enrollment/RepositoryInterfaces/IAgentDeviceRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/Agent/EfAgentRepository.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Create `IAgentActivationCodeRepository.cs`**

```csharp
using ONEVO.Domain.Features.Agent.Enrollment.Entities;

namespace ONEVO.Application.Features.Agent.Enrollment.RepositoryInterfaces;

public interface IAgentActivationCodeRepository
{
    Task AddAsync(AgentActivationCode code, CancellationToken ct);
    Task<AgentActivationCode?> GetByCodeHashAsync(string codeHash, CancellationToken ct);

    /// <summary>
    /// Atomically flips Status from "pending" to "used".
    /// Returns false if the row was already used or does not exist (concurrent enroll guard).
    /// </summary>
    Task<bool> TryMarkUsedAsync(Guid codeId, DateTimeOffset usedAt, CancellationToken ct);
}
```

- [ ] **Step 2: Create `IAgentDeviceRepository.cs`**

```csharp
using ONEVO.Domain.Features.Agent.Enrollment.Entities;

namespace ONEVO.Application.Features.Agent.Enrollment.RepositoryInterfaces;

public interface IAgentDeviceRepository
{
    Task AddAsync(AgentDevice device, CancellationToken ct);
    Task<AgentDevice?> GetByIdAsync(Guid agentId, CancellationToken ct);

    /// <summary>
    /// Sets IsRevoked=true and RevokedAt/RevokedById. Returns false if not found.
    /// </summary>
    Task<bool> RevokeAsync(Guid agentId, Guid revokedById, DateTimeOffset revokedAt, CancellationToken ct);

    /// <summary>Updates LastSeenAt to now. No-op if device not found.</summary>
    Task TouchLastSeenAsync(Guid agentId, DateTimeOffset now, CancellationToken ct);
}
```

- [ ] **Step 3: Create `EfAgentRepository.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Agent.Enrollment.RepositoryInterfaces;
using ONEVO.Domain.Features.Agent.Enrollment.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.Agent;

public sealed class EfAgentRepository : IAgentActivationCodeRepository, IAgentDeviceRepository
{
    private readonly ApplicationDbContext _db;

    public EfAgentRepository(ApplicationDbContext db) => _db = db;

    // ── IAgentActivationCodeRepository ────────────────────────────────────────

    public async Task AddAsync(AgentActivationCode code, CancellationToken ct) =>
        await _db.AgentActivationCodes.AddAsync(code, ct);

    public Task<AgentActivationCode?> GetByCodeHashAsync(string codeHash, CancellationToken ct) =>
        _db.AgentActivationCodes.FirstOrDefaultAsync(c => c.CodeHash == codeHash, ct);

    public async Task<bool> TryMarkUsedAsync(Guid codeId, DateTimeOffset usedAt, CancellationToken ct)
    {
        var affected = await _db.AgentActivationCodes
            .Where(c => c.Id == codeId && c.Status == "pending")
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, "used")
                .SetProperty(c => c.UsedAt, usedAt), ct);
        return affected > 0;
    }

    // ── IAgentDeviceRepository ────────────────────────────────────────────────

    public async Task AddAsync(AgentDevice device, CancellationToken ct) =>
        await _db.AgentDevices.AddAsync(device, ct);

    public Task<AgentDevice?> GetByIdAsync(Guid agentId, CancellationToken ct) =>
        _db.AgentDevices.FirstOrDefaultAsync(d => d.Id == agentId, ct);

    public async Task<bool> RevokeAsync(
        Guid agentId, Guid revokedById, DateTimeOffset revokedAt, CancellationToken ct)
    {
        var affected = await _db.AgentDevices
            .Where(d => d.Id == agentId && !d.IsRevoked)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.IsRevoked, true)
                .SetProperty(d => d.RevokedAt, revokedAt)
                .SetProperty(d => d.RevokedById, revokedById), ct);
        return affected > 0;
    }

    public async Task TouchLastSeenAsync(Guid agentId, DateTimeOffset now, CancellationToken ct) =>
        await _db.AgentDevices
            .Where(d => d.Id == agentId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.LastSeenAt, now), ct);
}
```

- [ ] **Step 4: Register in `DependencyInjection.cs`**

Find the `// Auth: invitation tokens` comment block in `DependencyInjection.cs` and add after it:
```csharp
        // Agent enrollment
        services.AddScoped<EfAgentRepository>();
        services.AddScoped<IAgentActivationCodeRepository>(sp => sp.GetRequiredService<EfAgentRepository>());
        services.AddScoped<IAgentDeviceRepository>(sp => sp.GetRequiredService<EfAgentRepository>());
```

Also add using:
```csharp
using ONEVO.Application.Features.Agent.Enrollment.RepositoryInterfaces;
using ONEVO.Infrastructure.Persistence.Repositories.Agent;
```

- [ ] **Step 5: Build**

```bash
dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/Agent/
git add src/ONEVO.Infrastructure/Persistence/Repositories/Agent/
git add src/ONEVO.Infrastructure/DependencyInjection.cs
git commit -m "feat(agent): repository interfaces and EfAgentRepository"
```

---

## Task 6: GenerateActivationCodeCommand (HR generates code)

**Files:**
- Create: `src/ONEVO.Application/Features/Agent/Enrollment/DTOs/ActivationCodeDto.cs`
- Create: `src/ONEVO.Application/Features/Agent/Enrollment/Commands/GenerateActivationCode/GenerateActivationCodeCommand.cs`
- Create: `src/ONEVO.Application/Features/Agent/Enrollment/Commands/GenerateActivationCode/GenerateActivationCodeCommandHandler.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Agent/GenerateActivationCodeCommandHandlerTests.cs`

- [ ] **Step 1: Create `ActivationCodeDto.cs`**

```csharp
namespace ONEVO.Application.Features.Agent.Enrollment.DTOs;

public record ActivationCodeDto(
    string Code,
    Guid EmployeeId,
    DateTimeOffset ExpiresAt
);
```

- [ ] **Step 2: Create `GenerateActivationCodeCommand.cs`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Agent.Enrollment.DTOs;

namespace ONEVO.Application.Features.Agent.Enrollment.Commands.GenerateActivationCode;

public record GenerateActivationCodeCommand(Guid EmployeeId) : IRequest<Result<ActivationCodeDto>>;
```

- [ ] **Step 3: Write the failing unit test**

```csharp
// tests/ONEVO.Tests.Unit/Features/Agent/GenerateActivationCodeCommandHandlerTests.cs
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Agent.Enrollment.Commands.GenerateActivationCode;
using ONEVO.Application.Features.Agent.Enrollment.RepositoryInterfaces;
using ONEVO.Domain.Features.Agent.Enrollment.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Agent;

public class GenerateActivationCodeCommandHandlerTests
{
    private readonly Mock<IAgentActivationCodeRepository> _codeRepo = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private GenerateActivationCodeCommandHandler CreateHandler() =>
        new(_codeRepo.Object, _users.Object, _uow.Object, _tenantContext.Object, _currentUser.Object);

    [Fact]
    public async Task Handle_ValidEmployee_ReturnsFormattedCode()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var hrUserId = Guid.NewGuid();

        _tenantContext.Setup(t => t.IsResolved).Returns(true);
        _tenantContext.Setup(t => t.TenantId).Returns(tenantId);
        _tenantContext.Setup(t => t.Status).Returns(TenantStatus.Active);
        _currentUser.Setup(u => u.UserId).Returns(hrUserId);

        _users.Setup(u => u.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new User { Id = userId, TenantId = tenantId, IsActive = true });

        // EmployeeId lookup is handled by the handler resolving UserId from employee
        // For this test we pass a UserId directly via the command (see handler impl)
        AgentActivationCode? saved = null;
        _codeRepo.Setup(r => r.AddAsync(It.IsAny<AgentActivationCode>(), It.IsAny<CancellationToken>()))
                 .Callback<AgentActivationCode, CancellationToken>((c, _) => saved = c)
                 .Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = CreateHandler();
        var result = await handler.Handle(new GenerateActivationCodeCommand(employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        // Code format: XXXX-XXXX-XXXX-XXXX (4 groups of 4 uppercase hex, separated by dashes)
        Assert.Matches(@"^[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}$", result.Value!.Code);
        Assert.NotNull(saved);
        Assert.NotEmpty(saved!.CodeHash);
        Assert.NotEqual(result.Value.Code.Replace("-", ""), saved.CodeHash); // hash != plaintext hex
    }

    [Fact]
    public async Task Handle_TenantNotResolved_ReturnsFailure()
    {
        _tenantContext.Setup(t => t.IsResolved).Returns(false);

        var handler = CreateHandler();
        var result = await handler.Handle(new GenerateActivationCodeCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }
}
```

- [ ] **Step 4: Run failing test**

```bash
dotnet test tests/ONEVO.Tests.Unit/ --filter "GenerateActivationCodeCommandHandlerTests" --no-build 2>&1 | tail -5
```
Expected: FAIL — handler class not found.

- [ ] **Step 5: Create `GenerateActivationCodeCommandHandler.cs`**

```csharp
using System.Security.Cryptography;
using System.Text;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Agent.Enrollment.DTOs;
using ONEVO.Application.Features.Agent.Enrollment.RepositoryInterfaces;
using ONEVO.Domain.Features.Agent.Enrollment.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Application.Features.Agent.Enrollment.Commands.GenerateActivationCode;

public class GenerateActivationCodeCommandHandler
    : IRequestHandler<GenerateActivationCodeCommand, Result<ActivationCodeDto>>
{
    private readonly IAgentActivationCodeRepository _codes;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;

    public GenerateActivationCodeCommandHandler(
        IAgentActivationCodeRepository codes,
        IUserRepository users,
        IUnitOfWork uow,
        ITenantContext tenantContext,
        ICurrentUser currentUser)
    {
        _codes = codes;
        _users = users;
        _uow = uow;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
    }

    public async Task<Result<ActivationCodeDto>> Handle(
        GenerateActivationCodeCommand request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.IsResolved)
            return Result<ActivationCodeDto>.Failure("Tenant context is not resolved.", 400);

        if (_tenantContext.Status is not (TenantStatus.Active or TenantStatus.Trial))
            return Result<ActivationCodeDto>.Failure("This tenant is not available.", 403);

        // Validate the employee's user record belongs to this tenant
        var user = await _users.GetByIdAsync(request.EmployeeId, cancellationToken);
        if (user is null || user.TenantId != _tenantContext.TenantId || !user.IsActive)
            return Result<ActivationCodeDto>.NotFound("Employee not found or inactive.");

        var (plainCode, codeHash) = GenerateCode();
        var expiresAt = DateTimeOffset.UtcNow.AddHours(48);

        var code = new AgentActivationCode
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            EmployeeId = request.EmployeeId,
            UserId = user.Id,
            CodeHash = codeHash,
            Status = "pending",
            ExpiresAt = expiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedById = _currentUser.UserId
        };

        await _codes.AddAsync(code, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return Result<ActivationCodeDto>.Success(new ActivationCodeDto(plainCode, request.EmployeeId, expiresAt));
    }

    private static (string Code, string Hash) GenerateCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(8); // 8 bytes = 16 uppercase hex chars
        var hex = Convert.ToHexString(bytes);          // e.g. "A3F91B2CD4E56F78"
        var code = $"{hex[0..4]}-{hex[4..8]}-{hex[8..12]}-{hex[12..16]}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();
        return (code, hash);
    }
}
```

- [ ] **Step 6: Run tests — expect pass**

```bash
dotnet test tests/ONEVO.Tests.Unit/ --filter "GenerateActivationCodeCommandHandlerTests" -v
```
Expected: all green.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/Agent/
git add tests/ONEVO.Tests.Unit/Features/Agent/GenerateActivationCodeCommandHandlerTests.cs
git commit -m "feat(agent): GenerateActivationCodeCommand + handler + tests"
```

---

## Task 7: EnrollAgentCommand (tray app enrolls with code)

**Files:**
- Create: `src/ONEVO.Application/Features/Agent/Enrollment/DTOs/AgentEnrollmentDto.cs`
- Create: `src/ONEVO.Application/Features/Agent/Enrollment/Commands/EnrollAgent/EnrollAgentCommand.cs`
- Create: `src/ONEVO.Application/Features/Agent/Enrollment/Commands/EnrollAgent/EnrollAgentCommandHandler.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Agent/EnrollAgentCommandHandlerTests.cs`

- [ ] **Step 1: Create `AgentEnrollmentDto.cs`**

```csharp
namespace ONEVO.Application.Features.Agent.Enrollment.DTOs;

public record AgentEnrollmentDto(
    string DeviceToken,
    Guid AgentId,
    Guid EmployeeId,
    Guid TenantId,
    DateTimeOffset ExpiresAt
);
```

- [ ] **Step 2: Create `EnrollAgentCommand.cs`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Agent.Enrollment.DTOs;

namespace ONEVO.Application.Features.Agent.Enrollment.Commands.EnrollAgent;

public record EnrollAgentCommand(
    string ActivationCode,
    string MachineId,
    string Hostname,
    string OsVersion,
    string Arch,
    string AgentVersion
) : IRequest<Result<AgentEnrollmentDto>>;
```

- [ ] **Step 3: Write the failing unit test**

```csharp
// tests/ONEVO.Tests.Unit/Features/Agent/EnrollAgentCommandHandlerTests.cs
using System.Security.Cryptography;
using System.Text;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Agent.Enrollment.Commands.EnrollAgent;
using ONEVO.Application.Features.Agent.Enrollment.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.Agent.Enrollment.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Agent;

public class EnrollAgentCommandHandlerTests
{
    private readonly Mock<IAgentActivationCodeRepository> _codeRepo = new();
    private readonly Mock<IAgentDeviceRepository> _deviceRepo = new();
    private readonly Mock<IJwtTokenService> _jwt = new();
    private readonly Mock<ITenantContext> _tenantContext = new();

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();

    private EnrollAgentCommandHandler CreateHandler() =>
        new(_codeRepo.Object, _deviceRepo.Object, _jwt.Object, _tenantContext.Object);

    [Fact]
    public async Task Handle_ValidCode_ReturnsDeviceToken()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var codeId = Guid.NewGuid();
        const string plainCode = "A3F9-1B2C-D4E5-6F78";

        _tenantContext.Setup(t => t.IsResolved).Returns(true);
        _tenantContext.Setup(t => t.TenantId).Returns(tenantId);

        _codeRepo.Setup(r => r.GetByCodeHashAsync(HashCode(plainCode), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new AgentActivationCode
                 {
                     Id = codeId,
                     TenantId = tenantId,
                     EmployeeId = employeeId,
                     UserId = userId,
                     Status = "pending",
                     ExpiresAt = DateTimeOffset.UtcNow.AddHours(24)
                 });

        _codeRepo.Setup(r => r.TryMarkUsedAsync(codeId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);

        _deviceRepo.Setup(r => r.AddAsync(It.IsAny<AgentDevice>(), It.IsAny<CancellationToken>()))
                   .Returns(Task.CompletedTask);

        _jwt.Setup(j => j.GenerateAgentToken(It.IsAny<Guid>(), tenantId))
            .Returns("eyJhbGci.test.token");

        var handler = CreateHandler();
        var result = await handler.Handle(
            new EnrollAgentCommand(plainCode, "WIN-ABC123", "PIRAKI-LAPTOP", "Windows 11", "x64", "1.0.0"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("eyJhbGci.test.token", result.Value!.DeviceToken);
        Assert.Equal(employeeId, result.Value.EmployeeId);
        Assert.Equal(tenantId, result.Value.TenantId);
    }

    [Fact]
    public async Task Handle_AlreadyUsedCode_ReturnsFailure()
    {
        var tenantId = Guid.NewGuid();
        const string plainCode = "A3F9-1B2C-D4E5-6F78";

        _tenantContext.Setup(t => t.IsResolved).Returns(true);
        _tenantContext.Setup(t => t.TenantId).Returns(tenantId);

        _codeRepo.Setup(r => r.GetByCodeHashAsync(HashCode(plainCode), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new AgentActivationCode
                 {
                     Id = Guid.NewGuid(),
                     TenantId = tenantId,
                     Status = "pending",
                     ExpiresAt = DateTimeOffset.UtcNow.AddHours(24)
                 });

        // Simulate race: another request used the code first
        _codeRepo.Setup(r => r.TryMarkUsedAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(false);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new EnrollAgentCommand(plainCode, "WIN-ABC123", "HOST", "Win11", "x64", "1.0.0"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ExpiredCode_ReturnsFailure()
    {
        var tenantId = Guid.NewGuid();
        const string plainCode = "A3F9-1B2C-D4E5-6F78";

        _tenantContext.Setup(t => t.IsResolved).Returns(true);
        _tenantContext.Setup(t => t.TenantId).Returns(tenantId);

        _codeRepo.Setup(r => r.GetByCodeHashAsync(HashCode(plainCode), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new AgentActivationCode
                 {
                     Id = Guid.NewGuid(),
                     TenantId = tenantId,
                     Status = "pending",
                     ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1) // expired
                 });

        var handler = CreateHandler();
        var result = await handler.Handle(
            new EnrollAgentCommand(plainCode, "WIN-ABC123", "HOST", "Win11", "x64", "1.0.0"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }
}
```

- [ ] **Step 4: Run failing test**

```bash
dotnet test tests/ONEVO.Tests.Unit/ --filter "EnrollAgentCommandHandlerTests" --no-build 2>&1 | tail -5
```
Expected: FAIL — handler not found.

- [ ] **Step 5: Create `EnrollAgentCommandHandler.cs`**

```csharp
using System.Security.Cryptography;
using System.Text;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Agent.Enrollment.DTOs;
using ONEVO.Application.Features.Agent.Enrollment.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.Agent.Enrollment.Entities;

namespace ONEVO.Application.Features.Agent.Enrollment.Commands.EnrollAgent;

public class EnrollAgentCommandHandler
    : IRequestHandler<EnrollAgentCommand, Result<AgentEnrollmentDto>>
{
    private readonly IAgentActivationCodeRepository _codes;
    private readonly IAgentDeviceRepository _devices;
    private readonly IJwtTokenService _jwt;
    private readonly ITenantContext _tenantContext;

    public EnrollAgentCommandHandler(
        IAgentActivationCodeRepository codes,
        IAgentDeviceRepository devices,
        IJwtTokenService jwt,
        ITenantContext tenantContext)
    {
        _codes = codes;
        _devices = devices;
        _jwt = jwt;
        _tenantContext = tenantContext;
    }

    public async Task<Result<AgentEnrollmentDto>> Handle(
        EnrollAgentCommand request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.IsResolved)
            return Result<AgentEnrollmentDto>.Failure("Tenant context is not resolved.", 400);

        var codeHash = HashCode(request.ActivationCode.Trim().ToUpperInvariant());
        var code = await _codes.GetByCodeHashAsync(codeHash, cancellationToken);

        if (code is null || !code.IsPending(DateTimeOffset.UtcNow))
            return Result<AgentEnrollmentDto>.Failure("Invalid or expired activation code.", 400);

        // Atomic mark-as-used — guards against concurrent enroll with the same code
        var marked = await _codes.TryMarkUsedAsync(code.Id, DateTimeOffset.UtcNow, cancellationToken);
        if (!marked)
            return Result<AgentEnrollmentDto>.Conflict("Activation code was already used.");

        var device = new AgentDevice
        {
            Id = Guid.NewGuid(),
            TenantId = code.TenantId,
            EmployeeId = code.EmployeeId,
            UserId = code.UserId,
            ActivationCodeId = code.Id,
            MachineId = request.MachineId,
            Hostname = request.Hostname,
            OsVersion = request.OsVersion,
            Arch = request.Arch,
            AgentVersion = request.AgentVersion,
            EnrolledAt = DateTimeOffset.UtcNow
        };

        await _devices.AddAsync(device, cancellationToken);

        var expiresAt = DateTimeOffset.UtcNow.AddDays(90);
        var token = _jwt.GenerateAgentToken(device.Id, device.TenantId);

        return Result<AgentEnrollmentDto>.Success(new AgentEnrollmentDto(
            DeviceToken: token,
            AgentId: device.Id,
            EmployeeId: device.EmployeeId,
            TenantId: device.TenantId,
            ExpiresAt: expiresAt));
    }

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();
}
```

> **Note on `AddAsync` + no `SaveChangesAsync` here:** `TryMarkUsedAsync` uses `ExecuteUpdateAsync` which hits the DB immediately. `AddAsync` stages the device insert in EF change tracking. The controller calls `IUnitOfWork.SaveChangesAsync()` after the command returns, committing the insert. If you want to keep it inside the handler, inject `IUnitOfWork` and call it before issuing the JWT.

- [ ] **Step 6: Run tests — expect pass**

```bash
dotnet test tests/ONEVO.Tests.Unit/ --filter "EnrollAgentCommandHandlerTests" -v
```
Expected: all green.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/Agent/
git add tests/ONEVO.Tests.Unit/Features/Agent/EnrollAgentCommandHandlerTests.cs
git commit -m "feat(agent): EnrollAgentCommand + handler + tests (atomic code use, race guard)"
```

---

## Task 8: RevokeAgentDeviceCommand (HR revokes a device)

**Files:**
- Create: `src/ONEVO.Application/Features/Agent/Enrollment/Commands/RevokeAgentDevice/RevokeAgentDeviceCommand.cs`
- Create: `src/ONEVO.Application/Features/Agent/Enrollment/Commands/RevokeAgentDevice/RevokeAgentDeviceCommandHandler.cs`

- [ ] **Step 1: Create `RevokeAgentDeviceCommand.cs`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Agent.Enrollment.Commands.RevokeAgentDevice;

public record RevokeAgentDeviceCommand(Guid AgentId) : IRequest<Result>;
```

- [ ] **Step 2: Create `RevokeAgentDeviceCommandHandler.cs`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Agent.Enrollment.RepositoryInterfaces;

namespace ONEVO.Application.Features.Agent.Enrollment.Commands.RevokeAgentDevice;

public class RevokeAgentDeviceCommandHandler : IRequestHandler<RevokeAgentDeviceCommand, Result>
{
    private readonly IAgentDeviceRepository _devices;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;

    public RevokeAgentDeviceCommandHandler(
        IAgentDeviceRepository devices,
        ITenantContext tenantContext,
        ICurrentUser currentUser)
    {
        _devices = devices;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(RevokeAgentDeviceCommand request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var revoked = await _devices.RevokeAsync(
            request.AgentId,
            _currentUser.UserId,
            DateTimeOffset.UtcNow,
            cancellationToken);

        return revoked
            ? Result.Success()
            : Result.NotFound("Device not found or already revoked.");
    }
}
```

- [ ] **Step 3: Build**

```bash
dotnet build src/ONEVO.Application/ONEVO.Application.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Application/Features/Agent/Enrollment/Commands/RevokeAgentDevice/
git commit -m "feat(agent): RevokeAgentDeviceCommand + handler"
```

---

## Task 9: Controllers

**Files:**
- Create: `src/ONEVO.Api/Controllers/Tenant/Hr/AgentActivationController.cs`
- Create: `src/ONEVO.Api/Controllers/Agent/AgentEnrollController.cs`
- Create: `src/ONEVO.Api/Controllers/Agent/AgentController.cs`

- [ ] **Step 1: Create HR activation code controller**

```csharp
// src/ONEVO.Api/Controllers/Tenant/Hr/AgentActivationController.cs
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Agent.Enrollment.Commands.GenerateActivationCode;
using ONEVO.Application.Features.Agent.Enrollment.Commands.RevokeAgentDevice;

namespace ONEVO.Api.Controllers.Tenant.Hr;

[ApiController]
[Route("api/v1/activation-codes")]
[Authorize(Policy = "TenantPolicy")]
public class AgentActivationController : ControllerBase
{
    private readonly IMediator _mediator;
    public AgentActivationController(IMediator mediator) => _mediator = mediator;

    /// <summary>HR generates a one-time activation code for an employee's tray app.</summary>
    [HttpPost]
    public async Task<IActionResult> Generate(
        [FromBody] GenerateRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new GenerateActivationCodeCommand(request.EmployeeId), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    /// <summary>HR revokes a registered device immediately.</summary>
    [HttpDelete("devices/{agentId:guid}")]
    public async Task<IActionResult> Revoke(Guid agentId, CancellationToken ct)
    {
        var result = await _mediator.Send(new RevokeAgentDeviceCommand(agentId), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return NoContent();
    }

    public record GenerateRequest(Guid EmployeeId);
}
```

- [ ] **Step 2: Create public enroll controller**

```csharp
// src/ONEVO.Api/Controllers/Agent/AgentEnrollController.cs
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Agent.Enrollment.Commands.EnrollAgent;

namespace ONEVO.Api.Controllers.Agent;

[ApiController]
[Route("api/v1/agent")]
public class AgentEnrollController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IUnitOfWork _uow;

    public AgentEnrollController(IMediator mediator, IUnitOfWork uow)
    {
        _mediator = mediator;
        _uow = uow;
    }

    /// <summary>
    /// Public endpoint — no auth header needed.
    /// Tray app posts the activation code + device info to receive a Device JWT.
    /// Tenant context is resolved from the request host by HostTenantResolutionMiddleware
    /// before this action runs.
    /// </summary>
    [HttpPost("enroll")]
    [AllowAnonymous]
    public async Task<IActionResult> Enroll([FromBody] EnrollRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new EnrollAgentCommand(
            request.ActivationCode,
            request.MachineId,
            request.Hostname,
            request.OsVersion,
            request.Arch,
            request.AgentVersion), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        await _uow.SaveChangesAsync(ct);
        return Ok(result.Value);
    }

    public record EnrollRequest(
        string ActivationCode,
        string MachineId,
        string Hostname,
        string OsVersion,
        string Arch,
        string AgentVersion);
}
```

- [ ] **Step 3: Create agent heartbeat + status controller**

```csharp
// src/ONEVO.Api/Controllers/Agent/AgentController.cs
using System.IdentityModel.Tokens.Jwt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Agent.Enrollment.RepositoryInterfaces;

namespace ONEVO.Api.Controllers.Agent;

[ApiController]
[Route("api/v1/agent")]
[Authorize(Policy = "AgentPolicy")]
public class AgentController : ControllerBase
{
    private readonly IAgentDeviceRepository _devices;

    public AgentController(IAgentDeviceRepository devices) => _devices = devices;

    /// <summary>
    /// Tray app calls this periodically (e.g. every 60s).
    /// Returns 401 immediately if the device has been revoked by HR.
    /// </summary>
    [HttpGet("heartbeat")]
    public async Task<IActionResult> Heartbeat(CancellationToken ct)
    {
        var agentId = GetAgentId();
        if (agentId == Guid.Empty)
            return Unauthorized();

        var device = await _devices.GetByIdAsync(agentId, ct);
        if (device is null || device.IsRevoked)
            return Unauthorized(new { code = "device_revoked", message = "Device has been revoked. Re-enroll using a new activation code." });

        await _devices.TouchLastSeenAsync(agentId, DateTimeOffset.UtcNow, ct);
        return Ok(new { status = "ok", agent_id = agentId });
    }

    private Guid GetAgentId()
    {
        var value = User.FindFirst("agent_id")?.Value
                    ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }
}
```

- [ ] **Step 4: Build API project**

```bash
dotnet build src/ONEVO.Api/ONEVO.Api.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Api/Controllers/Tenant/Hr/AgentActivationController.cs
git add src/ONEVO.Api/Controllers/Agent/
git commit -m "feat(agent): controllers — HR activation, public enroll, heartbeat with revocation check"
```

---

## Task 10: Full Build + Unit Test Run

- [ ] **Step 1: Build the entire solution**

```bash
dotnet build HRMS-Backend-v1.sln
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 2: Run all unit tests**

```bash
dotnet test tests/ONEVO.Tests.Unit/ -v
```
Expected: All tests pass, including the two new agent test classes.

- [ ] **Step 3: Run integration tests (if DB is available)**

```bash
dotnet test tests/ONEVO.Tests.Integration/ -v
```
Expected: All pass. If DB is not set up locally, this can be deferred.

- [ ] **Step 4: Final commit**

```bash
git add .
git commit -m "feat(agent): complete agent enrollment — activation code generate, tray app enroll, heartbeat, revocation"
```

---

## Self-Review

### 1. Spec Coverage

| Spec requirement | Task covering it |
|-----------------|-----------------|
| HR generates code → `POST /api/v1/activation-codes` | Task 6 + Task 9 |
| Code format `XXXX-XXXX-XXXX-XXXX`, 48h expiry | Task 6 handler `GenerateCode()` |
| Code stored as hash only | Task 3 entity + Task 6 handler |
| Tray app `POST /api/v1/agent/enroll` AllowAnonymous | Task 7 + Task 9 |
| Machine metadata stored | Task 3 `AgentDevice` + Task 7 handler |
| Code mark USED atomically (race guard) | Task 5 `TryMarkUsedAsync` + Task 7 test |
| Device JWT returned (90d, separate key) | Task 1 + Task 7 handler |
| JWT claims: `sub`, `agent_id`, `tenant_id`, `type=agent` | Task 1 `JwtTokenService` |
| Separate `Jwt:AgentSecret` key | Task 1 + Task 2 |
| `AgentScheme` rejects user cookies | Task 2 — `AgentPolicy` uses `AgentScheme` only |
| `TenantPolicy` unaffected (cookie scheme) | Task 2 — untouched |
| `GET /api/v1/agent/heartbeat` → revocation check | Task 9 `AgentController` |
| HR revoke device `DELETE /api/v1/activation-codes/devices/{id}` | Task 8 + Task 9 |
| `AgentDevice.IsRevoked` → heartbeat returns 401 | Task 9 `AgentController.Heartbeat` |

### 2. Known gaps / follow-ups (out of scope for this plan)
- `EnrollAgentCommandHandler` does not call `IUnitOfWork.SaveChangesAsync` directly; the controller does. This is deliberate — if you prefer the handler to own the save, inject `IUnitOfWork` into the handler and call it before returning the DTO.
- The `GenerateActivationCodeCommand` uses `EmployeeId` but the handler validates via `IUserRepository.GetByIdAsync(request.EmployeeId)` — this assumes `EmployeeId == UserId` for now. If the domain later separates `Employee.Id` from `Employee.UserId`, update the lookup to go through an `IEmployeeRepository` first.
- No permission guard on `POST /api/v1/activation-codes` — add `[RequirePermission("hr.agent.enroll")]` once the permission catalog is extended.
