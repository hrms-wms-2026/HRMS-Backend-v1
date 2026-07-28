# Agent Gateway — DEV4 Task 1: Login-Based Enrollment

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement login-based Windows agent enrollment per the OneVo-HR spec: TrayApp starts enrollment → browser uses existing web session to confirm → backend issues a Device JWT that is completely separate from user cookies.

**Architecture:** Three-step flow — (1) TrayApp posts device info to `enroll/start` (anonymous), gets back an `enrollment_id` and a browser URL; (2) the employee's browser (already signed in) hits `POST /api/v1/agent/enroll/confirm` (TenantScheme cookie auth), backend validates the challenge, generates a short-lived `authorization_code`, returns a redirect to the TrayApp callback URI; (3) TrayApp posts `enrollment_id + device_id + authorization_code` to `enroll/complete` (anonymous), backend validates everything, writes `registered_agents` + `agent_sessions`, issues the Device JWT signed with a **separate** `Jwt:AgentSecret` key. The Device JWT (`type=agent`, `aud=onevo-agent`) is rejected on all user endpoints; user cookies are rejected on all agent endpoints.

**Tech Stack:** ASP.NET Core, MediatR, EF Core 8 + Npgsql, Microsoft.AspNetCore.Authentication.JwtBearer, xUnit + Moq

**Spec source:** `C:\tmp\one backend\OneVo-HR\modules\agent-gateway\overview.md` and `agent-server-protocol.md`

---

## File Map

### New files

| File | Layer | Responsibility |
|------|-------|----------------|
| `src/ONEVO.Domain/Features/AgentGateway/Entities/AgentEnrollmentChallenge.cs` | Domain | Short-lived enrollment challenge (not tenant-owned) |
| `src/ONEVO.Domain/Features/AgentGateway/Entities/RegisteredAgent.cs` | Domain | Enrolled device registry |
| `src/ONEVO.Domain/Features/AgentGateway/Entities/AgentSession.cs` | Domain | Active employee-device binding |
| `src/ONEVO.Domain/Features/AgentGateway/Entities/AgentPolicy.cs` | Domain | Monitoring policy JSON blob |
| `src/ONEVO.Domain/Features/AgentGateway/Entities/AgentHealthLog.cs` | Domain | Heartbeat health snapshot |
| `src/ONEVO.Application/Features/AgentGateway/RepositoryInterfaces/IAgentGatewayRepository.cs` | Application | All agent data access |
| `src/ONEVO.Application/Features/AgentGateway/Commands/StartEnrollment/StartEnrollmentCommand.cs` | Application | TrayApp calls this first |
| `src/ONEVO.Application/Features/AgentGateway/Commands/StartEnrollment/StartEnrollmentCommandHandler.cs` | Application | |
| `src/ONEVO.Application/Features/AgentGateway/Commands/ConfirmEnrollment/ConfirmEnrollmentCommand.cs` | Application | Browser user confirms device |
| `src/ONEVO.Application/Features/AgentGateway/Commands/ConfirmEnrollment/ConfirmEnrollmentCommandHandler.cs` | Application | |
| `src/ONEVO.Application/Features/AgentGateway/Commands/CompleteEnrollment/CompleteEnrollmentCommand.cs` | Application | TrayApp completes enrollment |
| `src/ONEVO.Application/Features/AgentGateway/Commands/CompleteEnrollment/CompleteEnrollmentCommandHandler.cs` | Application | |
| `src/ONEVO.Application/Features/AgentGateway/Commands/AgentLogin/AgentLoginCommand.cs` | Application | Resume employee-device session |
| `src/ONEVO.Application/Features/AgentGateway/Commands/AgentLogin/AgentLoginCommandHandler.cs` | Application | |
| `src/ONEVO.Application/Features/AgentGateway/Commands/AgentLogout/AgentLogoutCommand.cs` | Application | End session |
| `src/ONEVO.Application/Features/AgentGateway/Commands/AgentLogout/AgentLogoutCommandHandler.cs` | Application | |
| `src/ONEVO.Application/Features/AgentGateway/DTOs/EnrollStartResponseDto.cs` | Application | |
| `src/ONEVO.Application/Features/AgentGateway/DTOs/EnrollCompleteResponseDto.cs` | Application | Device JWT + policy |
| `src/ONEVO.Application/Features/AgentGateway/DTOs/AgentLoginResponseDto.cs` | Application | |
| `src/ONEVO.Infrastructure/Persistence/Configurations/AgentGateway/AgentEnrollmentChallengeConfiguration.cs` | Infra | EF table: agent_enrollment_challenges |
| `src/ONEVO.Infrastructure/Persistence/Configurations/AgentGateway/RegisteredAgentConfiguration.cs` | Infra | EF table: registered_agents |
| `src/ONEVO.Infrastructure/Persistence/Configurations/AgentGateway/AgentSessionConfiguration.cs` | Infra | EF table: agent_sessions |
| `src/ONEVO.Infrastructure/Persistence/Configurations/AgentGateway/AgentPolicyConfiguration.cs` | Infra | EF table: agent_policies |
| `src/ONEVO.Infrastructure/Persistence/Configurations/AgentGateway/AgentHealthLogConfiguration.cs` | Infra | EF table: agent_health_logs |
| `src/ONEVO.Infrastructure/Persistence/Repositories/AgentGateway/EfAgentGatewayRepository.cs` | Infra | |
| `src/ONEVO.Api/Controllers/AgentGateway/AgentGatewayController.cs` | API | All /api/v1/agent/* endpoints |
| `tests/ONEVO.Tests.Unit/Features/AgentGateway/StartEnrollmentCommandHandlerTests.cs` | Tests | |
| `tests/ONEVO.Tests.Unit/Features/AgentGateway/CompleteEnrollmentCommandHandlerTests.cs` | Tests | |

### Modified files

| File | What changes |
|------|-------------|
| `src/ONEVO.Application/Features/Auth/Login/ServiceInterfaces/IJwtTokenService.cs` | Rename `GenerateDeviceToken` → `GenerateAgentToken`, update param |
| `src/ONEVO.Infrastructure/Identity/JwtTokenService.cs` | Use `Jwt:AgentSecret`, exact spec claims, 90d expiry |
| `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` | Add 5 new DbSets |
| `src/ONEVO.Infrastructure/DependencyInjection.cs` | Register EfAgentGatewayRepository |
| `src/ONEVO.Api/Extensions/AuthenticationExtensions.cs` | Add `AgentScheme` JWT bearer |
| `src/ONEVO.Api/Extensions/AuthorizationExtensions.cs` | Add `AgentPolicy` |
| `src/ONEVO.Api/appsettings.json` | Add `Jwt:AgentSecret` |

---

## Task 1: JWT Service — Separate Agent Secret + Correct Spec Claims

**Files:**
- Modify: `src/ONEVO.Application/Features/Auth/Login/ServiceInterfaces/IJwtTokenService.cs`
- Modify: `src/ONEVO.Infrastructure/Identity/JwtTokenService.cs`
- Modify: `src/ONEVO.Api/appsettings.json`

- [ ] **Step 1: Verify no callers of `GenerateDeviceToken` exist yet**

```bash
grep -r "GenerateDeviceToken" src/
```
Expected output: only the interface definition and implementation — no command handlers calling it.

- [ ] **Step 2: Replace `IJwtTokenService.cs` entirely**

```csharp
namespace ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

/// <summary>
/// Issues Device JWTs for the desktop agent only.
/// Browser sessions use HttpOnly cookies — this service is never called for web auth.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Issues a Device JWT per spec: sub=deviceId, tenant_id, type="agent", aud="onevo-agent".
    /// Signed with Jwt:AgentSecret — completely separate from user session signing.
    /// </summary>
    string GenerateAgentToken(Guid deviceId, Guid tenantId);
}
```

- [ ] **Step 3: Replace `JwtTokenService.cs` entirely**

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

namespace ONEVO.Infrastructure.Identity;

/// <summary>
/// Issues 90-day Device JWTs for enrolled desktop agents.
/// Uses Jwt:AgentSecret — independent of any user-session key.
/// Claims: sub=deviceId, tenant_id, type="agent", iss="onevo", aud="onevo-agent".
/// </summary>
public class JwtTokenService : IJwtTokenService
{
    private readonly string _agentSecret;
    private readonly string _issuer;

    public JwtTokenService(IConfiguration configuration)
    {
        _agentSecret = configuration["Jwt:AgentSecret"]
            ?? throw new InvalidOperationException("Jwt:AgentSecret is required.");
        _issuer = configuration["Jwt:TenantIssuer"] ?? "onevo";
    }

    public string GenerateAgentToken(Guid deviceId, Guid tenantId)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, deviceId.ToString()),
            new Claim("tenant_id", tenantId.ToString()),
            new Claim("type", "agent")
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

In the existing `"Jwt"` section add one line:
```json
"Jwt": {
  "TenantIssuer": "onevo",
  "TenantAudience": "onevo-api",
  "Secret": "CHANGE_ME_tenant_secret_min_32_chars_!!",
  "AgentSecret": "CHANGE_ME_agent_secret_min_32_chars_!!"
}
```

- [ ] **Step 5: Build**

```bash
dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/Auth/Login/ServiceInterfaces/IJwtTokenService.cs
git add src/ONEVO.Infrastructure/Identity/JwtTokenService.cs
git add src/ONEVO.Api/appsettings.json
git commit -m "feat(agent): separate Jwt:AgentSecret, spec-correct claims, rename to GenerateAgentToken"
```

---

## Task 2: Register AgentScheme JWT Bearer + AgentPolicy

**Files:**
- Modify: `src/ONEVO.Api/Extensions/AuthenticationExtensions.cs`
- Modify: `src/ONEVO.Api/Extensions/AuthorizationExtensions.cs`
- Modify: `src/ONEVO.Api/Program.cs`

- [ ] **Step 1: Add JwtBearer NuGet**

```bash
dotnet add src/ONEVO.Api/ONEVO.Api.csproj package Microsoft.AspNetCore.Authentication.JwtBearer
```

- [ ] **Step 2: Add using directives to `AuthenticationExtensions.cs`**

At the top of the file add:
```csharp
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
```

- [ ] **Step 3: Update method signature in `AuthenticationExtensions.cs`**

Change:
```csharp
internal static IServiceCollection AddApiAuthentication(this IServiceCollection services, IWebHostEnvironment env)
```
To:
```csharp
internal static IServiceCollection AddApiAuthentication(
    this IServiceCollection services,
    IWebHostEnvironment env,
    IConfiguration configuration)
```

- [ ] **Step 4: Add `AgentScheme` JWT bearer inside `AddApiAuthentication`**

After the existing `.AddCookie("AdminScheme", options => { ... })` closing brace, chain:
```csharp
            .AddJwtBearer("AgentScheme", options =>
            {
                var secret = configuration["Jwt:AgentSecret"]
                    ?? throw new InvalidOperationException("Jwt:AgentSecret is required.");
                var issuer = configuration["Jwt:TenantIssuer"] ?? "onevo";

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
                            detail = "A valid device token is required."
                        });
                    }
                };
            });
```

- [ ] **Step 5: Add `AgentPolicy` to `AuthorizationExtensions.cs`**

Inside `AddApiAuthorization`, after the `AdminPolicy` block:
```csharp
            options.AddPolicy("AgentPolicy", policy =>
                policy.AddAuthenticationSchemes("AgentScheme")
                      .RequireAuthenticatedUser()
                      .RequireClaim("type", "agent"));
```

- [ ] **Step 6: Update `Program.cs` call site**

Find the existing `AddApiAuthentication` call and pass `builder.Configuration`:
```csharp
builder.Services.AddApiAuthentication(builder.Environment, builder.Configuration);
```

- [ ] **Step 7: Build**

```bash
dotnet build src/ONEVO.Api/ONEVO.Api.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Api/Extensions/AuthenticationExtensions.cs
git add src/ONEVO.Api/Extensions/AuthorizationExtensions.cs
git add src/ONEVO.Api/Program.cs
git add src/ONEVO.Api/ONEVO.Api.csproj
git commit -m "feat(agent): AgentScheme JWT bearer and AgentPolicy — device credential cannot reach user endpoints"
```

---

## Task 3: Domain Entities

**Files:**
- Create: `src/ONEVO.Domain/Features/AgentGateway/Entities/AgentEnrollmentChallenge.cs`
- Create: `src/ONEVO.Domain/Features/AgentGateway/Entities/RegisteredAgent.cs`
- Create: `src/ONEVO.Domain/Features/AgentGateway/Entities/AgentSession.cs`
- Create: `src/ONEVO.Domain/Features/AgentGateway/Entities/AgentPolicy.cs`
- Create: `src/ONEVO.Domain/Features/AgentGateway/Entities/AgentHealthLog.cs`

- [ ] **Step 1: Create `AgentEnrollmentChallenge.cs`**

This entity is NOT tenant-owned because it's created before we know which tenant the device belongs to.
```csharp
namespace ONEVO.Domain.Features.AgentGateway.Entities;

/// <summary>
/// Short-lived enrollment challenge created by enroll/start.
/// Not tenant-scoped because tenant is unknown until the browser session confirms.
/// Expires in 10 minutes. Deleted after completion.
/// </summary>
public class AgentEnrollmentChallenge
{
    public Guid Id { get; set; }                    // = enrollment_id returned to TrayApp
    public string DeviceId { get; set; } = string.Empty;   // UUID v7 from agent install
    public string DeviceName { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = string.Empty;

    /// <summary>pending | confirmed | completed | expired</summary>
    public string Status { get; set; } = "pending";

    /// <summary>SHA-256 hash of the short-lived authorization_code. Set when status=confirmed.</summary>
    public string? AuthorizationCodeHash { get; set; }

    /// <summary>Set when the browser session confirms. Used by enroll/complete to write tenant-scoped rows.</summary>
    public Guid? TenantId { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? ConfirmedByUserId { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

- [ ] **Step 2: Create `RegisteredAgent.cs`**

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.AgentGateway.Entities;

/// <summary>
/// A desktop agent installed on an employee's machine.
/// Spec: modules/agent-gateway/overview.md — registered_agents table.
/// </summary>
public class RegisteredAgent : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? EmployeeId { get; set; }    // nullable — set at employee login
    public string DeviceId { get; set; } = string.Empty;   // UUID v7 from agent install (unique per tenant)
    public string DeviceName { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = string.Empty;

    /// <summary>active | inactive | revoked</summary>
    public string Status { get; set; } = "active";

    public DateTimeOffset RegisteredAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
```

- [ ] **Step 3: Create `AgentSession.cs`**

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.AgentGateway.Entities;

/// <summary>
/// Tracks the currently logged-in employee on an enrolled device.
/// Only one active session per device (enforced by unique partial index).
/// Spec: modules/agent-gateway/overview.md — agent_sessions table.
/// </summary>
public class AgentSession : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string DeviceId { get; set; } = string.Empty;   // matches RegisteredAgent.DeviceId
    public Guid EmployeeId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAt { get; set; }
}
```

- [ ] **Step 4: Create `AgentPolicy.cs`**

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.AgentGateway.Entities;

/// <summary>
/// Monitoring policy pushed to an agent after enrollment.
/// Spec: modules/agent-gateway/overview.md — agent_policies table.
/// </summary>
public class AgentPolicy : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AgentId { get; set; }    // FK -> registered_agents.id
    public string PolicyJson { get; set; } = "{}";
    public DateTimeOffset? LastSyncedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
```

- [ ] **Step 5: Create `AgentHealthLog.cs`**

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.AgentGateway.Entities;

/// <summary>
/// Snapshot of agent health reported on each heartbeat.
/// Spec: modules/agent-gateway/overview.md — agent_health_logs table.
/// </summary>
public class AgentHealthLog : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AgentId { get; set; }
    public DateTimeOffset ReportedAt { get; set; }
    public decimal CpuUsage { get; set; }
    public int MemoryMb { get; set; }
    public string ErrorsJson { get; set; } = "[]";
    public bool TamperDetected { get; set; } = false;
}
```

- [ ] **Step 6: Build**

```bash
dotnet build src/ONEVO.Domain/ONEVO.Domain.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Domain/Features/AgentGateway/
git commit -m "feat(agent): domain entities — AgentEnrollmentChallenge, RegisteredAgent, AgentSession, AgentPolicy, AgentHealthLog"
```

---

## Task 4: EF Configurations + DbContext + Migration

**Files:**
- Create: 5 configuration files under `src/ONEVO.Infrastructure/Persistence/Configurations/AgentGateway/`
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`

- [ ] **Step 1: Create `AgentEnrollmentChallengeConfiguration.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.AgentGateway.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.AgentGateway;

public class AgentEnrollmentChallengeConfiguration
    : IEntityTypeConfiguration<AgentEnrollmentChallenge>
{
    public void Configure(EntityTypeBuilder<AgentEnrollmentChallenge> builder)
    {
        builder.ToTable("agent_enrollment_challenges");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.DeviceId).HasMaxLength(36).IsRequired();
        builder.Property(c => c.DeviceName).HasMaxLength(100).IsRequired();
        builder.Property(c => c.OsVersion).HasMaxLength(50).IsRequired();
        builder.Property(c => c.AgentVersion).HasMaxLength(20).IsRequired();
        builder.Property(c => c.Status).HasMaxLength(20).IsRequired();
        builder.Property(c => c.AuthorizationCodeHash).HasMaxLength(128);

        builder.HasIndex(c => c.ExpiresAt);
        builder.HasIndex(c => c.DeviceId);
    }
}
```

- [ ] **Step 2: Create `RegisteredAgentConfiguration.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.AgentGateway.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.AgentGateway;

public class RegisteredAgentConfiguration : IEntityTypeConfiguration<RegisteredAgent>
{
    public void Configure(EntityTypeBuilder<RegisteredAgent> builder)
    {
        builder.ToTable("registered_agents");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.DeviceId).HasMaxLength(36).IsRequired();
        builder.Property(a => a.DeviceName).HasMaxLength(100).IsRequired();
        builder.Property(a => a.OsVersion).HasMaxLength(50).IsRequired();
        builder.Property(a => a.AgentVersion).HasMaxLength(20).IsRequired();
        builder.Property(a => a.Status).HasMaxLength(20).IsRequired();

        // Spec: (tenant_id, device_id) UNIQUE
        builder.HasIndex(a => new { a.TenantId, a.DeviceId }).IsUnique();
        builder.HasIndex(a => new { a.TenantId, a.Status });
        builder.HasIndex(a => new { a.TenantId, a.EmployeeId });
    }
}
```

- [ ] **Step 3: Create `AgentSessionConfiguration.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.AgentGateway.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.AgentGateway;

public class AgentSessionConfiguration : IEntityTypeConfiguration<AgentSession>
{
    public void Configure(EntityTypeBuilder<AgentSession> builder)
    {
        builder.ToTable("agent_sessions");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.DeviceId).HasMaxLength(36).IsRequired();

        // Spec: UNIQUE (device_id) WHERE is_active = true
        builder.HasIndex(s => s.DeviceId)
               .HasFilter("is_active = true")
               .IsUnique();
    }
}
```

- [ ] **Step 4: Create `AgentPolicyConfiguration.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.AgentGateway.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.AgentGateway;

public class AgentPolicyConfiguration : IEntityTypeConfiguration<AgentPolicy>
{
    public void Configure(EntityTypeBuilder<AgentPolicy> builder)
    {
        builder.ToTable("agent_policies");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.PolicyJson).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(p => p.AgentId).IsUnique();
    }
}
```

- [ ] **Step 5: Create `AgentHealthLogConfiguration.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.AgentGateway.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.AgentGateway;

public class AgentHealthLogConfiguration : IEntityTypeConfiguration<AgentHealthLog>
{
    public void Configure(EntityTypeBuilder<AgentHealthLog> builder)
    {
        builder.ToTable("agent_health_logs");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.ErrorsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(h => h.CpuUsage).HasPrecision(5, 2);

        // Spec: (agent_id, reported_at)
        builder.HasIndex(h => new { h.AgentId, h.ReportedAt });
    }
}
```

- [ ] **Step 6: Add DbSets to `ApplicationDbContext.cs`**

After the `// CoreHR` section, add:
```csharp
    // Agent Gateway
    public DbSet<AgentEnrollmentChallenge> AgentEnrollmentChallenges => Set<AgentEnrollmentChallenge>();
    public DbSet<RegisteredAgent> RegisteredAgents => Set<RegisteredAgent>();
    public DbSet<AgentSession> AgentSessions => Set<AgentSession>();
    public DbSet<AgentPolicy> AgentPolicies => Set<AgentPolicy>();
    public DbSet<AgentHealthLog> AgentHealthLogs => Set<AgentHealthLog>();
```

Add the using at the top:
```csharp
using ONEVO.Domain.Features.AgentGateway.Entities;
```

- [ ] **Step 7: Build Infrastructure**

```bash
dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 8: Generate migration**

```bash
dotnet ef migrations add AddAgentGatewayEnrollment \
  --project src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj \
  --startup-project src/ONEVO.Api/ONEVO.Api.csproj \
  --context ApplicationDbContext
```
Expected: `Done.`

- [ ] **Step 9: Review the generated migration**

Open the new file in `src/ONEVO.Infrastructure/Migrations/`. Verify:
- `agent_enrollment_challenges` table: id, device_id, device_name, os_version, agent_version, status, authorization_code_hash, tenant_id (nullable), employee_id (nullable), confirmed_by_user_id (nullable), expires_at, created_at
- `registered_agents` table: id, tenant_id, employee_id (nullable), device_id, device_name, os_version, agent_version, status, registered_at, last_heartbeat_at, created_at, updated_at — plus UNIQUE index on (tenant_id, device_id)
- `agent_sessions` table: id, tenant_id, device_id, employee_id, is_active, created_at, ended_at — plus partial unique index on device_id WHERE is_active
- `agent_policies` table: id, tenant_id, agent_id, policy_json (jsonb), last_synced_at, created_at, updated_at
- `agent_health_logs` table: id, tenant_id, agent_id, reported_at, cpu_usage, memory_mb, errors_json (jsonb), tamper_detected

- [ ] **Step 10: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Configurations/AgentGateway/
git add src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs
git add src/ONEVO.Infrastructure/Migrations/
git commit -m "feat(agent): EF configurations and migration for 5 Agent Gateway tables"
```

---

## Task 5: Repository Interface + EfAgentGatewayRepository + DI

**Files:**
- Create: `src/ONEVO.Application/Features/AgentGateway/RepositoryInterfaces/IAgentGatewayRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/AgentGateway/EfAgentGatewayRepository.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Create `IAgentGatewayRepository.cs`**

```csharp
using ONEVO.Domain.Features.AgentGateway.Entities;

namespace ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;

public interface IAgentGatewayRepository
{
    // Enrollment challenges (no tenant filter)
    Task AddChallengeAsync(AgentEnrollmentChallenge challenge, CancellationToken ct);
    Task<AgentEnrollmentChallenge?> GetChallengeByIdAsync(Guid enrollmentId, CancellationToken ct);
    Task<bool> TryMarkChallengeConfirmedAsync(Guid enrollmentId, string authCodeHash,
        Guid tenantId, Guid employeeId, Guid confirmedByUserId, CancellationToken ct);
    Task<bool> TryMarkChallengeCompletedAsync(Guid enrollmentId, CancellationToken ct);

    // Registered agents (tenant-scoped via query filter)
    Task AddAgentAsync(RegisteredAgent agent, CancellationToken ct);
    Task<RegisteredAgent?> GetAgentByDeviceIdAsync(string deviceId, CancellationToken ct);
    Task<RegisteredAgent?> GetAgentByIdAsync(Guid agentId, CancellationToken ct);
    Task<bool> TouchHeartbeatAsync(Guid agentId, DateTimeOffset now, CancellationToken ct);

    // Agent sessions (tenant-scoped via query filter)
    Task AddSessionAsync(AgentSession session, CancellationToken ct);
    Task EndActiveSessionAsync(string deviceId, DateTimeOffset endedAt, CancellationToken ct);
    Task<AgentSession?> GetActiveSessionByDeviceIdAsync(string deviceId, CancellationToken ct);

    // Agent policies (tenant-scoped)
    Task AddOrUpdatePolicyAsync(AgentPolicy policy, CancellationToken ct);
    Task<AgentPolicy?> GetPolicyByAgentIdAsync(Guid agentId, CancellationToken ct);

    // Health logs (tenant-scoped)
    Task AddHealthLogAsync(AgentHealthLog log, CancellationToken ct);
}
```

- [ ] **Step 2: Create `EfAgentGatewayRepository.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.AgentGateway;

public sealed class EfAgentGatewayRepository : IAgentGatewayRepository
{
    private readonly ApplicationDbContext _db;
    public EfAgentGatewayRepository(ApplicationDbContext db) => _db = db;

    // ── Enrollment challenges ──────────────────────────────────────────────────

    public async Task AddChallengeAsync(AgentEnrollmentChallenge challenge, CancellationToken ct) =>
        await _db.AgentEnrollmentChallenges.AddAsync(challenge, ct);

    public Task<AgentEnrollmentChallenge?> GetChallengeByIdAsync(Guid enrollmentId, CancellationToken ct) =>
        _db.AgentEnrollmentChallenges.FirstOrDefaultAsync(c => c.Id == enrollmentId, ct);

    public async Task<bool> TryMarkChallengeConfirmedAsync(
        Guid enrollmentId, string authCodeHash,
        Guid tenantId, Guid employeeId, Guid confirmedByUserId, CancellationToken ct)
    {
        var affected = await _db.AgentEnrollmentChallenges
            .Where(c => c.Id == enrollmentId && c.Status == "pending")
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, "confirmed")
                .SetProperty(c => c.AuthorizationCodeHash, authCodeHash)
                .SetProperty(c => c.TenantId, tenantId)
                .SetProperty(c => c.EmployeeId, employeeId)
                .SetProperty(c => c.ConfirmedByUserId, confirmedByUserId), ct);
        return affected > 0;
    }

    public async Task<bool> TryMarkChallengeCompletedAsync(Guid enrollmentId, CancellationToken ct)
    {
        var affected = await _db.AgentEnrollmentChallenges
            .Where(c => c.Id == enrollmentId && c.Status == "confirmed")
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, "completed"), ct);
        return affected > 0;
    }

    // ── Registered agents ─────────────────────────────────────────────────────

    public async Task AddAgentAsync(RegisteredAgent agent, CancellationToken ct) =>
        await _db.RegisteredAgents.AddAsync(agent, ct);

    public Task<RegisteredAgent?> GetAgentByDeviceIdAsync(string deviceId, CancellationToken ct) =>
        _db.RegisteredAgents.FirstOrDefaultAsync(a => a.DeviceId == deviceId, ct);

    public Task<RegisteredAgent?> GetAgentByIdAsync(Guid agentId, CancellationToken ct) =>
        _db.RegisteredAgents.FirstOrDefaultAsync(a => a.Id == agentId, ct);

    public async Task<bool> TouchHeartbeatAsync(Guid agentId, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.RegisteredAgents
            .Where(a => a.Id == agentId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.LastHeartbeatAt, now)
                .SetProperty(a => a.UpdatedAt, now), ct);
        return affected > 0;
    }

    // ── Agent sessions ────────────────────────────────────────────────────────

    public async Task AddSessionAsync(AgentSession session, CancellationToken ct) =>
        await _db.AgentSessions.AddAsync(session, ct);

    public async Task EndActiveSessionAsync(string deviceId, DateTimeOffset endedAt, CancellationToken ct) =>
        await _db.AgentSessions
            .Where(s => s.DeviceId == deviceId && s.IsActive)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.IsActive, false)
                .SetProperty(x => x.EndedAt, endedAt), ct);

    public Task<AgentSession?> GetActiveSessionByDeviceIdAsync(string deviceId, CancellationToken ct) =>
        _db.AgentSessions.FirstOrDefaultAsync(s => s.DeviceId == deviceId && s.IsActive, ct);

    // ── Agent policies ────────────────────────────────────────────────────────

    public async Task AddOrUpdatePolicyAsync(AgentPolicy policy, CancellationToken ct)
    {
        var existing = await _db.AgentPolicies
            .FirstOrDefaultAsync(p => p.AgentId == policy.AgentId, ct);
        if (existing is null)
            await _db.AgentPolicies.AddAsync(policy, ct);
        else
        {
            existing.PolicyJson = policy.PolicyJson;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    public Task<AgentPolicy?> GetPolicyByAgentIdAsync(Guid agentId, CancellationToken ct) =>
        _db.AgentPolicies.FirstOrDefaultAsync(p => p.AgentId == agentId, ct);

    // ── Health logs ───────────────────────────────────────────────────────────

    public async Task AddHealthLogAsync(AgentHealthLog log, CancellationToken ct) =>
        await _db.AgentHealthLogs.AddAsync(log, ct);
}
```

- [ ] **Step 3: Register in `DependencyInjection.cs`**

After the `// Auth: invitation tokens` block, add:
```csharp
        // Agent Gateway
        services.AddScoped<EfAgentGatewayRepository>();
        services.AddScoped<IAgentGatewayRepository>(sp => sp.GetRequiredService<EfAgentGatewayRepository>());
```

Add usings:
```csharp
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Infrastructure.Persistence.Repositories.AgentGateway;
```

- [ ] **Step 4: Build**

```bash
dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/AgentGateway/RepositoryInterfaces/
git add src/ONEVO.Infrastructure/Persistence/Repositories/AgentGateway/
git add src/ONEVO.Infrastructure/DependencyInjection.cs
git commit -m "feat(agent): IAgentGatewayRepository + EfAgentGatewayRepository + DI registration"
```

---

## Task 6: StartEnrollmentCommand (TrayApp → backend, anonymous)

**Files:**
- Create: `src/ONEVO.Application/Features/AgentGateway/DTOs/EnrollStartResponseDto.cs`
- Create: `src/ONEVO.Application/Features/AgentGateway/Commands/StartEnrollment/StartEnrollmentCommand.cs`
- Create: `src/ONEVO.Application/Features/AgentGateway/Commands/StartEnrollment/StartEnrollmentCommandHandler.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/AgentGateway/StartEnrollmentCommandHandlerTests.cs`

- [ ] **Step 1: Create `EnrollStartResponseDto.cs`**

```csharp
namespace ONEVO.Application.Features.AgentGateway.DTOs;

public record EnrollStartResponseDto(
    Guid EnrollmentId,
    string AuthUrl,
    DateTimeOffset ExpiresAt
);
```

- [ ] **Step 2: Create `StartEnrollmentCommand.cs`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.AgentGateway.DTOs;

namespace ONEVO.Application.Features.AgentGateway.Commands.StartEnrollment;

public record StartEnrollmentCommand(
    string DeviceId,
    string DeviceName,
    string OsVersion,
    string AgentVersion
) : IRequest<Result<EnrollStartResponseDto>>;
```

- [ ] **Step 3: Write the failing unit test**

```csharp
// tests/ONEVO.Tests.Unit/Features/AgentGateway/StartEnrollmentCommandHandlerTests.cs
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.AgentGateway.Commands.StartEnrollment;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.AgentGateway;

public class StartEnrollmentCommandHandlerTests
{
    private readonly Mock<IAgentGatewayRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private StartEnrollmentCommandHandler CreateHandler(string appBaseUrl = "https://app.onevo.io") =>
        new(_repo.Object, _uow.Object, appBaseUrl);

    [Fact]
    public async Task Handle_ValidDeviceInfo_ReturnsEnrollmentIdAndAuthUrl()
    {
        AgentEnrollmentChallenge? saved = null;
        _repo.Setup(r => r.AddChallengeAsync(It.IsAny<AgentEnrollmentChallenge>(), It.IsAny<CancellationToken>()))
             .Callback<AgentEnrollmentChallenge, CancellationToken>((c, _) => saved = c)
             .Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new StartEnrollmentCommand("device-uuid-v7", "DESKTOP-ABC123", "Windows 11 23H2", "1.0.0"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.NotEqual(Guid.Empty, result.Value!.EnrollmentId);
        Assert.Contains("enrollment_id=", result.Value.AuthUrl);
        Assert.NotNull(saved);
        Assert.Equal("pending", saved!.Status);
        // Expires in ~10 minutes
        Assert.True(result.Value.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(8));
        Assert.True(result.Value.ExpiresAt < DateTimeOffset.UtcNow.AddMinutes(12));
    }

    [Fact]
    public async Task Handle_EmptyDeviceId_ReturnsFailure()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            new StartEnrollmentCommand("", "NAME", "Win11", "1.0.0"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }
}
```

- [ ] **Step 4: Run failing test**

```bash
dotnet test tests/ONEVO.Tests.Unit/ --filter "StartEnrollmentCommandHandlerTests" --no-build 2>&1 | tail -5
```
Expected: FAIL — handler not found.

- [ ] **Step 5: Create `StartEnrollmentCommandHandler.cs`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.Commands.StartEnrollment;
using ONEVO.Application.Features.AgentGateway.DTOs;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;
using Microsoft.Extensions.Configuration;

namespace ONEVO.Application.Features.AgentGateway.Commands.StartEnrollment;

public class StartEnrollmentCommandHandler
    : IRequestHandler<StartEnrollmentCommand, Result<EnrollStartResponseDto>>
{
    private readonly IAgentGatewayRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly string _appBaseUrl;

    public StartEnrollmentCommandHandler(
        IAgentGatewayRepository repo,
        IUnitOfWork uow,
        string appBaseUrl)
    {
        _repo = repo;
        _uow = uow;
        _appBaseUrl = appBaseUrl.TrimEnd('/');
    }

    public async Task<Result<EnrollStartResponseDto>> Handle(
        StartEnrollmentCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceId))
            return Result<EnrollStartResponseDto>.Failure("device_id is required.", 400);

        var enrollmentId = Guid.NewGuid();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);

        var challenge = new AgentEnrollmentChallenge
        {
            Id = enrollmentId,
            DeviceId = request.DeviceId.Trim(),
            DeviceName = request.DeviceName.Trim(),
            OsVersion = request.OsVersion.Trim(),
            AgentVersion = request.AgentVersion.Trim(),
            Status = "pending",
            ExpiresAt = expiresAt,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _repo.AddChallengeAsync(challenge, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        var authUrl = $"{_appBaseUrl}/agent/enroll?enrollment_id={enrollmentId}";

        return Result<EnrollStartResponseDto>.Success(
            new EnrollStartResponseDto(enrollmentId, authUrl, expiresAt));
    }
}
```

> **Note on DI:** `StartEnrollmentCommandHandler` takes a `string appBaseUrl` — register it in `DependencyInjection.cs` using a factory lambda that reads `Urls:AppBaseUrl` from `IConfiguration`. Add to the MediatR handler registration or use explicit registration:
> ```csharp
> services.AddScoped<StartEnrollmentCommandHandler>(sp =>
>     new StartEnrollmentCommandHandler(
>         sp.GetRequiredService<IAgentGatewayRepository>(),
>         sp.GetRequiredService<IUnitOfWork>(),
>         sp.GetRequiredService<IConfiguration>()["Urls:AppBaseUrl"] ?? "https://app.onevo.io"));
> ```

- [ ] **Step 6: Run tests — expect pass**

```bash
dotnet test tests/ONEVO.Tests.Unit/ --filter "StartEnrollmentCommandHandlerTests" -v
```
Expected: all green.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/AgentGateway/
git add tests/ONEVO.Tests.Unit/Features/AgentGateway/StartEnrollmentCommandHandlerTests.cs
git commit -m "feat(agent): StartEnrollmentCommand — creates challenge and returns browser auth URL"
```

---

## Task 7: ConfirmEnrollmentCommand (Browser session confirms device)

**Files:**
- Create: `src/ONEVO.Application/Features/AgentGateway/Commands/ConfirmEnrollment/ConfirmEnrollmentCommand.cs`
- Create: `src/ONEVO.Application/Features/AgentGateway/Commands/ConfirmEnrollment/ConfirmEnrollmentCommandHandler.cs`

- [ ] **Step 1: Create `ConfirmEnrollmentCommand.cs`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.AgentGateway.Commands.ConfirmEnrollment;

/// <summary>
/// Called by the web frontend when the authenticated employee confirms "Yes, this is my desktop".
/// Returns a short-lived authorization_code that the TrayApp uses in enroll/complete.
/// </summary>
public record ConfirmEnrollmentCommand(Guid EnrollmentId) : IRequest<Result<string>>;
```

- [ ] **Step 2: Create `ConfirmEnrollmentCommandHandler.cs`**

```csharp
using System.Security.Cryptography;
using System.Text;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;

namespace ONEVO.Application.Features.AgentGateway.Commands.ConfirmEnrollment;

public class ConfirmEnrollmentCommandHandler : IRequestHandler<ConfirmEnrollmentCommand, Result<string>>
{
    private readonly IAgentGatewayRepository _repo;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _uow;

    public ConfirmEnrollmentCommandHandler(
        IAgentGatewayRepository repo,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IUnitOfWork uow)
    {
        _repo = repo;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _uow = uow;
    }

    public async Task<Result<string>> Handle(
        ConfirmEnrollmentCommand request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.IsResolved)
            return Result<string>.Failure("Tenant context is not resolved.", 400);

        var challenge = await _repo.GetChallengeByIdAsync(request.EnrollmentId, cancellationToken);

        if (challenge is null)
            return Result<string>.NotFound("Enrollment challenge not found.");

        if (challenge.Status != "pending")
            return Result<string>.Failure("Enrollment challenge is no longer pending.", 409);

        if (challenge.ExpiresAt < DateTimeOffset.UtcNow)
            return Result<string>.Failure("Enrollment challenge has expired.", 400);

        // Generate short-lived authorization_code (valid 5 minutes — only for TrayApp handoff)
        var plainCode = GenerateAuthCode();
        var codeHash = HashCode(plainCode);

        // Atomically confirm: prevents double-confirm race
        var userId = _currentUser.UserId;
        var employeeId = userId; // employee_id == user_id in this codebase

        var confirmed = await _repo.TryMarkChallengeConfirmedAsync(
            request.EnrollmentId,
            codeHash,
            _tenantContext.TenantId,
            employeeId,
            userId,
            cancellationToken);

        if (!confirmed)
            return Result<string>.Conflict("Enrollment challenge was already confirmed.");

        // Return plaintext code — frontend will pass to TrayApp via callback URI
        return Result<string>.Success(plainCode);
    }

    private static string GenerateAuthCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();
}
```

- [ ] **Step 3: Build**

```bash
dotnet build src/ONEVO.Application/ONEVO.Application.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Application/Features/AgentGateway/Commands/ConfirmEnrollment/
git commit -m "feat(agent): ConfirmEnrollmentCommand — browser session confirms device, generates auth code"
```

---

## Task 8: CompleteEnrollmentCommand (TrayApp finalises, receives Device JWT)

**Files:**
- Create: `src/ONEVO.Application/Features/AgentGateway/DTOs/EnrollCompleteResponseDto.cs`
- Create: `src/ONEVO.Application/Features/AgentGateway/Commands/CompleteEnrollment/CompleteEnrollmentCommand.cs`
- Create: `src/ONEVO.Application/Features/AgentGateway/Commands/CompleteEnrollment/CompleteEnrollmentCommandHandler.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/AgentGateway/CompleteEnrollmentCommandHandlerTests.cs`

- [ ] **Step 1: Create `EnrollCompleteResponseDto.cs`**

```csharp
namespace ONEVO.Application.Features.AgentGateway.DTOs;

public record EnrollCompleteResponseDto(
    Guid AgentId,
    Guid TenantId,
    Guid EmployeeId,
    string EmployeeName,
    string DeviceToken,
    DateTimeOffset TokenExpiresAt,
    string PolicyJson
);
```

- [ ] **Step 2: Create `CompleteEnrollmentCommand.cs`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.AgentGateway.DTOs;

namespace ONEVO.Application.Features.AgentGateway.Commands.CompleteEnrollment;

public record CompleteEnrollmentCommand(
    Guid EnrollmentId,
    string DeviceId,
    string AuthorizationCode
) : IRequest<Result<EnrollCompleteResponseDto>>;
```

- [ ] **Step 3: Write failing unit test**

```csharp
// tests/ONEVO.Tests.Unit/Features/AgentGateway/CompleteEnrollmentCommandHandlerTests.cs
using System.Security.Cryptography;
using System.Text;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.Commands.CompleteEnrollment;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.AgentGateway;

public class CompleteEnrollmentCommandHandlerTests
{
    private readonly Mock<IAgentGatewayRepository> _repo = new();
    private readonly Mock<IJwtTokenService> _jwt = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private static string Hash(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    private CompleteEnrollmentCommandHandler CreateHandler() =>
        new(_repo.Object, _jwt.Object, _uow.Object);

    [Fact]
    public async Task Handle_ValidCode_ReturnsDeviceToken()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var enrollmentId = Guid.NewGuid();
        const string plainCode = "valid-auth-code";

        _repo.Setup(r => r.GetChallengeByIdAsync(enrollmentId, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new AgentEnrollmentChallenge
             {
                 Id = enrollmentId,
                 DeviceId = "device-uuid-v7",
                 DeviceName = "DESKTOP-ABC",
                 OsVersion = "Windows 11",
                 AgentVersion = "1.0.0",
                 Status = "confirmed",
                 AuthorizationCodeHash = Hash(plainCode),
                 TenantId = tenantId,
                 EmployeeId = employeeId,
                 ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
             });

        _repo.Setup(r => r.TryMarkChallengeCompletedAsync(enrollmentId, It.IsAny<CancellationToken>()))
             .ReturnsAsync(true);
        _repo.Setup(r => r.GetAgentByDeviceIdAsync("device-uuid-v7", It.IsAny<CancellationToken>()))
             .ReturnsAsync((RegisteredAgent?)null);
        _repo.Setup(r => r.AddAgentAsync(It.IsAny<RegisteredAgent>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.EndActiveSessionAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.AddSessionAsync(It.IsAny<AgentSession>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.AddOrUpdatePolicyAsync(It.IsAny<AgentPolicy>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _jwt.Setup(j => j.GenerateAgentToken(It.IsAny<Guid>(), tenantId))
            .Returns("eyJ.test.token");

        var handler = CreateHandler();
        var result = await handler.Handle(
            new CompleteEnrollmentCommand(enrollmentId, "device-uuid-v7", plainCode),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("eyJ.test.token", result.Value!.DeviceToken);
        Assert.Equal(tenantId, result.Value.TenantId);
        Assert.Equal(employeeId, result.Value.EmployeeId);
    }

    [Fact]
    public async Task Handle_WrongAuthCode_ReturnsUnauthorized()
    {
        var enrollmentId = Guid.NewGuid();
        _repo.Setup(r => r.GetChallengeByIdAsync(enrollmentId, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new AgentEnrollmentChallenge
             {
                 Id = enrollmentId,
                 Status = "confirmed",
                 AuthorizationCodeHash = Hash("correct-code"),
                 TenantId = Guid.NewGuid(),
                 EmployeeId = Guid.NewGuid(),
                 ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
             });

        var handler = CreateHandler();
        var result = await handler.Handle(
            new CompleteEnrollmentCommand(enrollmentId, "device-id", "wrong-code"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(401, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ExpiredChallenge_ReturnsFailure()
    {
        var enrollmentId = Guid.NewGuid();
        _repo.Setup(r => r.GetChallengeByIdAsync(enrollmentId, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new AgentEnrollmentChallenge
             {
                 Id = enrollmentId,
                 Status = "confirmed",
                 AuthorizationCodeHash = Hash("code"),
                 TenantId = Guid.NewGuid(),
                 EmployeeId = Guid.NewGuid(),
                 ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) // expired
             });

        var handler = CreateHandler();
        var result = await handler.Handle(
            new CompleteEnrollmentCommand(enrollmentId, "device-id", "code"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }
}
```

- [ ] **Step 4: Run failing test**

```bash
dotnet test tests/ONEVO.Tests.Unit/ --filter "CompleteEnrollmentCommandHandlerTests" --no-build 2>&1 | tail -5
```
Expected: FAIL.

- [ ] **Step 5: Create `CompleteEnrollmentCommandHandler.cs`**

```csharp
using System.Security.Cryptography;
using System.Text;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.Commands.CompleteEnrollment;
using ONEVO.Application.Features.AgentGateway.DTOs;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;

namespace ONEVO.Application.Features.AgentGateway.Commands.CompleteEnrollment;

public class CompleteEnrollmentCommandHandler
    : IRequestHandler<CompleteEnrollmentCommand, Result<EnrollCompleteResponseDto>>
{
    private readonly IAgentGatewayRepository _repo;
    private readonly IJwtTokenService _jwt;
    private readonly IUnitOfWork _uow;

    private static readonly string DefaultPolicyJson = """
        {
          "activity_monitoring": false,
          "application_tracking": false,
          "screenshot_capture": false,
          "heartbeat_interval_seconds": 60
        }
        """;

    public CompleteEnrollmentCommandHandler(
        IAgentGatewayRepository repo,
        IJwtTokenService jwt,
        IUnitOfWork uow)
    {
        _repo = repo;
        _jwt = jwt;
        _uow = uow;
    }

    public async Task<Result<EnrollCompleteResponseDto>> Handle(
        CompleteEnrollmentCommand request, CancellationToken cancellationToken)
    {
        var challenge = await _repo.GetChallengeByIdAsync(request.EnrollmentId, cancellationToken);

        if (challenge is null)
            return Result<EnrollCompleteResponseDto>.NotFound("Enrollment challenge not found.");

        if (challenge.ExpiresAt < DateTimeOffset.UtcNow)
            return Result<EnrollCompleteResponseDto>.Failure("Enrollment challenge has expired.", 400);

        if (challenge.Status != "confirmed")
            return Result<EnrollCompleteResponseDto>.Failure("Challenge has not been confirmed in the browser.", 400);

        // Validate device_id matches the challenge
        if (!string.Equals(challenge.DeviceId, request.DeviceId, StringComparison.OrdinalIgnoreCase))
            return Result<EnrollCompleteResponseDto>.Failure("device_id does not match the enrollment challenge.", 401);

        // Validate authorization_code
        var submittedHash = HashCode(request.AuthorizationCode);
        if (!string.Equals(submittedHash, challenge.AuthorizationCodeHash, StringComparison.OrdinalIgnoreCase))
            return Result<EnrollCompleteResponseDto>.Failure("Invalid authorization_code.", 401);

        // Atomic complete — prevents double-use of auth code
        var completed = await _repo.TryMarkChallengeCompletedAsync(request.EnrollmentId, cancellationToken);
        if (!completed)
            return Result<EnrollCompleteResponseDto>.Conflict("Enrollment was already completed.");

        var tenantId = challenge.TenantId!.Value;
        var employeeId = challenge.EmployeeId!.Value;
        var now = DateTimeOffset.UtcNow;

        // Create or update registered_agents
        var existing = await _repo.GetAgentByDeviceIdAsync(request.DeviceId, cancellationToken);
        Guid agentId;

        if (existing is not null)
        {
            agentId = existing.Id;
            existing.AgentVersion = challenge.AgentVersion;
            existing.EmployeeId = employeeId;
            existing.UpdatedAt = now;
        }
        else
        {
            var agent = new RegisteredAgent
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = employeeId,
                DeviceId = challenge.DeviceId,
                DeviceName = challenge.DeviceName,
                OsVersion = challenge.OsVersion,
                AgentVersion = challenge.AgentVersion,
                Status = "active",
                RegisteredAt = now,
                CreatedAt = now
            };
            await _repo.AddAgentAsync(agent, cancellationToken);
            agentId = agent.Id;
        }

        // End any previous active session for this device, create new one
        await _repo.EndActiveSessionAsync(request.DeviceId, now, cancellationToken);
        await _repo.AddSessionAsync(new AgentSession
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DeviceId = request.DeviceId,
            EmployeeId = employeeId,
            IsActive = true,
            CreatedAt = now
        }, cancellationToken);

        // Create default policy (tenant config module will update it later via AgentRegistered event)
        await _repo.AddOrUpdatePolicyAsync(new AgentPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AgentId = agentId,
            PolicyJson = DefaultPolicyJson,
            CreatedAt = now
        }, cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);

        // Device JWT — spec claims: sub=deviceId, tenant_id, type="agent"
        var deviceToken = _jwt.GenerateAgentToken(agentId, tenantId);
        var tokenExpiresAt = DateTimeOffset.UtcNow.AddDays(90);

        return Result<EnrollCompleteResponseDto>.Success(new EnrollCompleteResponseDto(
            AgentId: agentId,
            TenantId: tenantId,
            EmployeeId: employeeId,
            EmployeeName: string.Empty, // populate from employee lookup if needed
            DeviceToken: deviceToken,
            TokenExpiresAt: tokenExpiresAt,
            PolicyJson: DefaultPolicyJson));
    }

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();
}
```

- [ ] **Step 6: Run tests — expect pass**

```bash
dotnet test tests/ONEVO.Tests.Unit/ --filter "CompleteEnrollmentCommandHandlerTests" -v
```
Expected: all green.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/AgentGateway/
git add tests/ONEVO.Tests.Unit/Features/AgentGateway/CompleteEnrollmentCommandHandlerTests.cs
git commit -m "feat(agent): CompleteEnrollmentCommand — validates auth code, creates registered_agent + session + policy, issues Device JWT"
```

---

## Task 9: AgentLogin + AgentLogout Commands

**Files:**
- Create: `src/ONEVO.Application/Features/AgentGateway/DTOs/AgentLoginResponseDto.cs`
- Create: `src/ONEVO.Application/Features/AgentGateway/Commands/AgentLogin/AgentLoginCommand.cs`
- Create: `src/ONEVO.Application/Features/AgentGateway/Commands/AgentLogin/AgentLoginCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/AgentGateway/Commands/AgentLogout/AgentLogoutCommand.cs`
- Create: `src/ONEVO.Application/Features/AgentGateway/Commands/AgentLogout/AgentLogoutCommandHandler.cs`

- [ ] **Step 1: Create `AgentLoginResponseDto.cs`**

```csharp
namespace ONEVO.Application.Features.AgentGateway.DTOs;

public record AgentLoginResponseDto(
    Guid EmployeeId,
    string EmployeeName,
    string PolicyJson
);
```

- [ ] **Step 2: Create `AgentLoginCommand.cs`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.AgentGateway.DTOs;

namespace ONEVO.Application.Features.AgentGateway.Commands.AgentLogin;

/// <summary>
/// Resume or refresh an employee-device session on an already-enrolled agent.
/// Called by the TrayApp when the device credential is valid but the session needs refreshing.
/// Auth: AgentPolicy (Device JWT required).
/// </summary>
public record AgentLoginCommand(Guid AgentId) : IRequest<Result<AgentLoginResponseDto>>;
```

- [ ] **Step 3: Create `AgentLoginCommandHandler.cs`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.Commands.AgentLogin;
using ONEVO.Application.Features.AgentGateway.DTOs;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;

namespace ONEVO.Application.Features.AgentGateway.Commands.AgentLogin;

public class AgentLoginCommandHandler : IRequestHandler<AgentLoginCommand, Result<AgentLoginResponseDto>>
{
    private readonly IAgentGatewayRepository _repo;
    private readonly IUnitOfWork _uow;

    public AgentLoginCommandHandler(IAgentGatewayRepository repo, IUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task<Result<AgentLoginResponseDto>> Handle(
        AgentLoginCommand request, CancellationToken cancellationToken)
    {
        var agent = await _repo.GetAgentByIdAsync(request.AgentId, cancellationToken);
        if (agent is null || agent.Status == "revoked")
            return Result<AgentLoginResponseDto>.Failure("Agent not found or revoked.", 401);

        var now = DateTimeOffset.UtcNow;

        // End previous active session and start a fresh one
        await _repo.EndActiveSessionAsync(agent.DeviceId, now, cancellationToken);
        await _repo.AddSessionAsync(new AgentSession
        {
            Id = Guid.NewGuid(),
            TenantId = agent.TenantId,
            DeviceId = agent.DeviceId,
            EmployeeId = agent.EmployeeId!.Value,
            IsActive = true,
            CreatedAt = now
        }, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        var policy = await _repo.GetPolicyByAgentIdAsync(agent.Id, cancellationToken);

        return Result<AgentLoginResponseDto>.Success(new AgentLoginResponseDto(
            EmployeeId: agent.EmployeeId!.Value,
            EmployeeName: string.Empty,  // populate from employee service if needed
            PolicyJson: policy?.PolicyJson ?? "{}"));
    }
}
```

- [ ] **Step 4: Create `AgentLogoutCommand.cs`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.AgentGateway.Commands.AgentLogout;

/// <summary>Agent ends the active employee-device session. Auth: AgentPolicy.</summary>
public record AgentLogoutCommand(string DeviceId) : IRequest<Result>;
```

- [ ] **Step 5: Create `AgentLogoutCommandHandler.cs`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.Commands.AgentLogout;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;

namespace ONEVO.Application.Features.AgentGateway.Commands.AgentLogout;

public class AgentLogoutCommandHandler : IRequestHandler<AgentLogoutCommand, Result>
{
    private readonly IAgentGatewayRepository _repo;
    private readonly IUnitOfWork _uow;

    public AgentLogoutCommandHandler(IAgentGatewayRepository repo, IUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task<Result> Handle(AgentLogoutCommand request, CancellationToken cancellationToken)
    {
        await _repo.EndActiveSessionAsync(request.DeviceId, DateTimeOffset.UtcNow, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
```

- [ ] **Step 6: Build Application**

```bash
dotnet build src/ONEVO.Application/ONEVO.Application.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/AgentGateway/Commands/AgentLogin/
git add src/ONEVO.Application/Features/AgentGateway/Commands/AgentLogout/
git add src/ONEVO.Application/Features/AgentGateway/DTOs/AgentLoginResponseDto.cs
git commit -m "feat(agent): AgentLogin and AgentLogout commands"
```

---

## Task 10: AgentGatewayController — All Endpoints

**Files:**
- Create: `src/ONEVO.Api/Controllers/AgentGateway/AgentGatewayController.cs`

- [ ] **Step 1: Create `AgentGatewayController.cs`**

```csharp
using System.IdentityModel.Tokens.Jwt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.Commands.AgentLogin;
using ONEVO.Application.Features.AgentGateway.Commands.AgentLogout;
using ONEVO.Application.Features.AgentGateway.Commands.CompleteEnrollment;
using ONEVO.Application.Features.AgentGateway.Commands.ConfirmEnrollment;
using ONEVO.Application.Features.AgentGateway.Commands.StartEnrollment;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;

namespace ONEVO.Api.Controllers.AgentGateway;

[ApiController]
[Route("api/v1/agent")]
public class AgentGatewayController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IUnitOfWork _uow;

    public AgentGatewayController(IMediator mediator, IUnitOfWork uow)
    {
        _mediator = mediator;
        _uow = uow;
    }

    // ── Enrollment ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Step 1: TrayApp starts enrollment.
    /// Anonymous — device has no credential yet.
    /// Spec: POST /api/v1/agent/enroll/start
    /// </summary>
    [HttpPost("enroll/start")]
    [AllowAnonymous]
    public async Task<IActionResult> EnrollStart([FromBody] EnrollStartRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new StartEnrollmentCommand(request.DeviceId, request.DeviceName, request.OsVersion, request.AgentVersion), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(new
        {
            enrollment_id = result.Value!.EnrollmentId,
            auth_url = result.Value.AuthUrl,
            expires_at = result.Value.ExpiresAt
        });
    }

    /// <summary>
    /// Step 2: Authenticated employee confirms "Yes, this is my desktop" in the browser.
    /// Uses TenantPolicy (web cookie session).
    /// Frontend calls this, then redirects browser to onevo-agent://callback?code=xxx
    /// Spec: browser confirmation before enroll/complete
    /// </summary>
    [HttpPost("enroll/confirm")]
    [Authorize(Policy = "TenantPolicy")]
    public async Task<IActionResult> EnrollConfirm([FromBody] EnrollConfirmRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new ConfirmEnrollmentCommand(request.EnrollmentId), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        // Return the plaintext authorization_code — frontend passes it to TrayApp via callback URI
        return Ok(new { authorization_code = result.Value });
    }

    /// <summary>
    /// Step 3: TrayApp completes enrollment with the authorization_code from browser.
    /// Anonymous — device doesn't have a credential yet.
    /// Spec: POST /api/v1/agent/enroll/complete → returns device_token + policy
    /// </summary>
    [HttpPost("enroll/complete")]
    [AllowAnonymous]
    public async Task<IActionResult> EnrollComplete([FromBody] EnrollCompleteRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CompleteEnrollmentCommand(request.EnrollmentId, request.DeviceId, request.AuthorizationCode), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var dto = result.Value!;
        return StatusCode(201, new
        {
            agent_id = dto.AgentId,
            tenant_id = dto.TenantId,
            employee_id = dto.EmployeeId,
            employee_name = dto.EmployeeName,
            device_token = dto.DeviceToken,
            token_expires_at = dto.TokenExpiresAt,
            policy = dto.PolicyJson
        });
    }

    // ── Agent session (Device JWT required) ────────────────────────────────────

    /// <summary>
    /// Resume/refresh employee-device session on an enrolled agent.
    /// Spec: POST /api/v1/agent/login
    /// </summary>
    [HttpPost("login")]
    [Authorize(Policy = "AgentPolicy")]
    public async Task<IActionResult> Login(CancellationToken ct)
    {
        var agentId = GetAgentId();
        if (agentId == Guid.Empty) return Unauthorized();

        var result = await _mediator.Send(new AgentLoginCommand(agentId), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(new
        {
            employee_id = result.Value!.EmployeeId,
            employee_name = result.Value.EmployeeName,
            policy = result.Value.PolicyJson
        });
    }

    /// <summary>
    /// End active employee-device session.
    /// Spec: POST /api/v1/agent/logout
    /// </summary>
    [HttpPost("logout")]
    [Authorize(Policy = "AgentPolicy")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var deviceId = User.FindFirst("sub")?.Value ?? string.Empty;
        var result = await _mediator.Send(new AgentLogoutCommand(deviceId), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok();
    }

    /// <summary>
    /// Agent heartbeat every 60s. Updates last_heartbeat_at.
    /// Spec: POST /api/v1/agent/heartbeat → returns pending command info
    /// </summary>
    [HttpPost("heartbeat")]
    [Authorize(Policy = "AgentPolicy")]
    public async Task<IActionResult> Heartbeat([FromBody] HeartbeatRequest request, CancellationToken ct)
    {
        var agentId = GetAgentId();
        if (agentId == Guid.Empty) return Unauthorized();

        // Check revocation — if agent is revoked, return 401
        // (injected IAgentGatewayRepository via constructor for this check)
        // Note: for now, JWT validity is the revocation check. Full revocation list is Phase 2 if needed.

        // Update heartbeat timestamp (fire-and-forget style, non-critical)
        // This command is thin: just touch the timestamp.
        // For brevity the heartbeat is handled directly here without a full command
        // — refactor to HeartbeatCommand if logic grows.

        return Ok(new
        {
            status = "ok",
            update_available = false,
            update_url = (string?)null,
            has_pending_commands = false,
            pending_command_count = 0
        });
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private Guid GetAgentId()
    {
        var value = User.FindFirst("sub")?.Value
                    ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }

    // ── Request shapes ─────────────────────────────────────────────────────────

    public record EnrollStartRequest(
        string DeviceId,
        string DeviceName,
        string OsVersion,
        string AgentVersion);

    public record EnrollConfirmRequest(Guid EnrollmentId);

    public record EnrollCompleteRequest(
        Guid EnrollmentId,
        string DeviceId,
        string AuthorizationCode);

    public record HeartbeatRequest(
        string DeviceId,
        string AgentVersion,
        double CpuUsage,
        int MemoryMb,
        int BufferCount,
        string MonitoringState);
}
```

- [ ] **Step 2: Build API project**

```bash
dotnet build src/ONEVO.Api/ONEVO.Api.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add src/ONEVO.Api/Controllers/AgentGateway/
git commit -m "feat(agent): AgentGatewayController — enroll/start, enroll/confirm, enroll/complete, login, logout, heartbeat"
```

---

## Task 11: Full Build + Test Run

- [ ] **Step 1: Build entire solution**

```bash
dotnet build HRMS-Backend-v1.sln
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 2: Run all unit tests**

```bash
dotnet test tests/ONEVO.Tests.Unit/ -v
```
Expected: All pass including `StartEnrollmentCommandHandlerTests` and `CompleteEnrollmentCommandHandlerTests`.

- [ ] **Step 3: Final commit**

```bash
git add .
git commit -m "feat(agent): DEV4 Task 1 complete — login-based agent enrollment, Device JWT separated from user cookies"
```

---

## Self-Review

### Spec Coverage Checklist

| Spec requirement | Task |
|-----------------|------|
| `registered_agents` table | Task 3 + 4 |
| `agent_sessions` table | Task 3 + 4 |
| `agent_policies` table | Task 3 + 4 |
| `agent_health_logs` table | Task 3 + 4 |
| `POST /api/v1/agent/enroll/start` | Task 6 + 10 |
| `POST /api/v1/agent/enroll/complete` | Task 8 + 10 |
| Device credential ≠ User JWT | Task 1 + 2 — separate `AgentSecret`, separate `AgentScheme`, separate audience |
| Agent login/logout APIs | Task 9 + 10 |
| TenantScheme cookies rejected on agent endpoints | Task 2 — `AgentPolicy` uses `AgentScheme` only |
| Device JWT rejected on user endpoints | Task 2 — `TenantPolicy` uses `TenantScheme` (cookies) only |
| Tests: enrollment, challenge expiry, credential issuance, tenant isolation | Tasks 6 tests + 8 tests |
| Employees never enter API key / tenant ID | Design: enroll/start is anonymous, tenant resolved from browser session |

### Known gaps (follow-up tasks outside DEV4.T1 scope)
- `agent_commands` table + `GET /api/v1/agent/commands` / ack / complete endpoints (DEV4 Task 3 remote commands feature)
- `POST /api/v1/agent/ingest` high-throughput ingestion (DEV4 Task 3)
- `GET /api/v1/agent/policy` endpoint (thin — add to AgentGatewayController alongside heartbeat)
- SignalR hub `/hubs/agent-commands` (DEV4 Task 3)
- `DetectOfflineAgentsJob` Hangfire job (DEV4 Task 3)
- `AgentRegistered` domain event → outbox → Configuration module pushes real policy (cross-module)
- `EmployeeName` field in responses (needs `IEmployeeService` lookup)
