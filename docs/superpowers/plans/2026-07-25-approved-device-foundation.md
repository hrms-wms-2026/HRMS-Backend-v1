# Approved Device Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Correct employee identity during enrollment and enforce one approved Windows device per employee, with audited HR/Admin approval before a replacement device can log in, clock in, or monitor.

**Architecture:** Extend the existing Agent Gateway CQRS and repository boundaries. Browser confirmation resolves the authenticated user to the real employee row; enrollment activates the first device but records a later device as inactive with one pending replacement request. An asynchronous authorization requirement checks the database on every active-agent endpoint so revocation takes effect before JWT expiry.

**Tech Stack:** .NET 10, ASP.NET Core authentication/authorization, MediatR, EF Core 10, Npgsql/PostgreSQL, xUnit, Moq, existing ONEVO TenantScheme/AgentScheme/RLS infrastructure.

## Global Constraints

- Existing browser TenantScheme and AgentScheme JWT flows remain the only authentication systems.
- The Tray App never receives or stores an employee password.
- One employee has at most one `active` registered desktop.
- A candidate replacement stays `inactive` until a user with `agent:manage` approves it.
- Approval revokes the old device and activates the candidate in one `SaveChanges` transaction.
- Tenant and employee identity never come from client-supplied authority fields.
- Every new tenant-owned table has an EF tenant filter and PostgreSQL RLS.
- Existing untracked files under `docs/superpowers/plans/` and `src/ONEVO.Api/ONEVO.Api.slnx` are not staged or modified.
- Use TDD: failing focused test, minimal implementation, passing focused test, full relevant suite, commit.

## File Map

### New files

- `src/ONEVO.Domain/Features/AgentGateway/Entities/AgentDeviceChangeRequest.cs` — audited replacement aggregate.
- `src/ONEVO.Infrastructure/Persistence/Configurations/AgentGateway/AgentDeviceChangeRequestConfiguration.cs` — table, indexes, FKs, filtered unique pending index, xmin concurrency.
- `src/ONEVO.Application/Features/AgentGateway/Commands/ApproveDeviceChange/ApproveDeviceChangeCommand.cs`
- `src/ONEVO.Application/Features/AgentGateway/Commands/ApproveDeviceChange/ApproveDeviceChangeCommandHandler.cs`
- `src/ONEVO.Application/Features/AgentGateway/Commands/RejectDeviceChange/RejectDeviceChangeCommand.cs`
- `src/ONEVO.Application/Features/AgentGateway/Commands/RejectDeviceChange/RejectDeviceChangeCommandHandler.cs`
- `src/ONEVO.Application/Features/AgentGateway/Queries/GetDeviceChangeStatus/GetDeviceChangeStatusQuery.cs`
- `src/ONEVO.Application/Features/AgentGateway/Queries/GetDeviceChangeStatus/GetDeviceChangeStatusQueryHandler.cs`
- `src/ONEVO.Application/Features/AgentGateway/Queries/GetPendingDeviceChanges/GetPendingDeviceChangesQuery.cs`
- `src/ONEVO.Application/Features/AgentGateway/Queries/GetPendingDeviceChanges/GetPendingDeviceChangesQueryHandler.cs`
- `src/ONEVO.Api/Authorization/ActiveAgentRequirement.cs`
- `src/ONEVO.Api/Authorization/ActiveAgentAuthorizationHandler.cs`
- `tests/ONEVO.Tests.Unit/Features/AgentGateway/ConfirmEnrollmentCommandHandlerTests.cs`
- `tests/ONEVO.Tests.Unit/Features/AgentGateway/ApproveDeviceChangeCommandHandlerTests.cs`
- `tests/ONEVO.Tests.Unit/Features/AgentGateway/ActiveAgentAuthorizationHandlerTests.cs`
- generated EF migration `AddApprovedDeviceReplacement`.

### Modified files

- `src/ONEVO.Application/Features/Users/RepositoryInterfaces/IUserProfileRepository.cs`
- `src/ONEVO.Infrastructure/Persistence/Repositories/Users/EfUserProfileRepository.cs`
- `src/ONEVO.Application/Features/AgentGateway/Commands/ConfirmEnrollment/ConfirmEnrollmentCommandHandler.cs`
- `src/ONEVO.Application/Features/AgentGateway/Commands/CompleteEnrollment/CompleteEnrollmentCommandHandler.cs`
- `src/ONEVO.Application/Features/AgentGateway/DTOs/EnrollCompleteResponseDto.cs`
- `src/ONEVO.Application/Features/AgentGateway/RepositoryInterfaces/IAgentGatewayRepository.cs`
- `src/ONEVO.Infrastructure/Persistence/Repositories/AgentGateway/EfAgentGatewayRepository.cs`
- `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`
- `src/ONEVO.Api/Extensions/AuthorizationExtensions.cs`
- `src/ONEVO.Api/Controllers/AgentGateway/AgentGatewayController.cs`
- `src/ONEVO.Api/Controllers/AgentGateway/AgentFleetController.cs`
- `tests/ONEVO.Tests.Unit/Features/AgentGateway/CompleteEnrollmentCommandHandlerTests.cs`
- `tests/ONEVO.Tests.Architecture/TenantIsolationArchitectureTests.cs`

---

### Task 1: Resolve the Real Employee During Browser Confirmation

**Files:**

- Create: `tests/ONEVO.Tests.Unit/Features/AgentGateway/ConfirmEnrollmentCommandHandlerTests.cs`
- Modify: `src/ONEVO.Application/Features/AgentGateway/Commands/ConfirmEnrollment/ConfirmEnrollmentCommandHandler.cs`

**Interfaces:**

- Consumes: `IUserProfileRepository.GetEmployeeByUserIdAsync(Guid userId, CancellationToken ct)`.
- Produces: confirmed enrollment challenge whose `EmployeeId` is `Employee.Id`, never `User.Id`.

- [ ] **Step 1: Write the success-path failing test**

Construct the handler with mocks for `IAgentGatewayRepository`, `ITenantContext`, `ICurrentUser`, `IUserProfileRepository`, and `IUnitOfWork`. Return an employee with a different `Id` and `UserId`, then verify the repository call receives `employee.Id`:

```csharp
[Fact]
public async Task Handle_AuthenticatedEmployee_StoresEmployeeIdNotUserId()
{
    var userId = Guid.NewGuid();
    var employeeId = Guid.NewGuid();
    var tenantId = Guid.NewGuid();
    var enrollmentId = Guid.NewGuid();

    _tenant.SetupGet(x => x.IsResolved).Returns(true);
    _tenant.SetupGet(x => x.TenantId).Returns(tenantId);
    _current.SetupGet(x => x.UserId).Returns(userId);
    _profiles.Setup(x => x.GetEmployeeByUserIdAsync(userId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Employee { Id = employeeId, UserId = userId, TenantId = tenantId });
    _repo.Setup(x => x.GetChallengeByIdAsync(enrollmentId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(PendingChallenge(enrollmentId));
    _repo.Setup(x => x.TryMarkChallengeConfirmedAsync(
            enrollmentId, It.IsAny<string>(), tenantId, employeeId, userId,
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(true);

    var result = await CreateHandler().Handle(
        new ConfirmEnrollmentCommand(enrollmentId), CancellationToken.None);

    Assert.True(result.IsSuccess);
    _repo.VerifyAll();
}
```

- [ ] **Step 2: Write the no-employee failing test**

```csharp
[Fact]
public async Task Handle_UserWithoutEmployee_ReturnsForbiddenWithoutConfirming()
{
    _tenant.SetupGet(x => x.IsResolved).Returns(true);
    _current.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
    _profiles.Setup(x => x.GetEmployeeByUserIdAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((Employee?)null);

    var result = await CreateHandler().Handle(
        new ConfirmEnrollmentCommand(Guid.NewGuid()), CancellationToken.None);

    Assert.False(result.IsSuccess);
    Assert.Equal(403, result.StatusCode);
    _repo.Verify(x => x.TryMarkChallengeConfirmedAsync(
        It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
        It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
}
```

- [ ] **Step 3: Run the focused tests and verify RED**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter FullyQualifiedName~ConfirmEnrollmentCommandHandlerTests
```

Expected: compile failure because the handler constructor does not accept `IUserProfileRepository`, or assertion failure because `UserId` is still stored as `EmployeeId`.

- [ ] **Step 4: Inject the profile repository and resolve the employee before changing the challenge**

Use this constructor and guard:

```csharp
private readonly IUserProfileRepository _profiles;

public ConfirmEnrollmentCommandHandler(
    IAgentGatewayRepository repo,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IUserProfileRepository profiles,
    IUnitOfWork uow)
{
    _repo = repo;
    _tenantContext = tenantContext;
    _currentUser = currentUser;
    _profiles = profiles;
    _uow = uow;
}

var employee = await _profiles.GetEmployeeByUserIdAsync(
    _currentUser.UserId, cancellationToken);
if (employee is null || employee.TenantId != _tenantContext.TenantId)
    return Result<ConfirmEnrollmentResult>.Forbidden(
        "The signed-in user has no employee profile in this Company.");
```

Pass `employee.Id` as `employeeId` and `_currentUser.UserId` as `confirmedByUserId` to `TryMarkChallengeConfirmedAsync`.

- [ ] **Step 5: Run focused and Agent Gateway unit tests**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter FullyQualifiedName~AgentGateway
```

Expected: all Agent Gateway unit tests pass.

- [ ] **Step 6: Commit Task 1**

```powershell
git add src/ONEVO.Application/Features/AgentGateway/Commands/ConfirmEnrollment tests/ONEVO.Tests.Unit/Features/AgentGateway/ConfirmEnrollmentCommandHandlerTests.cs
git commit -m "fix(agent): resolve employee id during enrollment"
```

---

### Task 2: Add the Device Replacement Aggregate and Persistence

**Files:**

- Create: `src/ONEVO.Domain/Features/AgentGateway/Entities/AgentDeviceChangeRequest.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/AgentGateway/AgentDeviceChangeRequestConfiguration.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`
- Modify: `tests/ONEVO.Tests.Architecture/TenantIsolationArchitectureTests.cs`
- Generate: `src/ONEVO.Infrastructure/Migrations/*_AddApprovedDeviceReplacement.cs`

**Interfaces:**

- Produces: `AgentDeviceChangeRequest` and `ApplicationDbContext.AgentDeviceChangeRequests`.
- Status values: `pending`, `approved`, `rejected`, `cancelled`, `expired`.

- [ ] **Step 1: Write the tenant-isolation architecture test**

Add the new type to the explicit tenant-owned entity assertions used by `TenantIsolationArchitectureTests`:

```csharp
Assert.True(typeof(ITenantOwnedEntity).IsAssignableFrom(
    typeof(AgentDeviceChangeRequest)));
```

- [ ] **Step 2: Run the architecture test and verify RED**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --filter FullyQualifiedName~TenantIsolation
```

Expected: compile failure because `AgentDeviceChangeRequest` does not exist.

- [ ] **Step 3: Add the domain entity**

```csharp
public sealed class AgentDeviceChangeRequest : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid CurrentAgentId { get; set; }
    public Guid RequestedAgentId { get; set; }
    public string Status { get; set; } = "pending";
    public string? Reason { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public Guid? ReviewedById { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewComment { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public uint Version { get; set; }
}
```

- [ ] **Step 4: Add EF configuration and DbSet**

Configure:

```csharp
builder.ToTable("agent_device_change_requests");
builder.HasKey(x => x.Id);
builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
builder.Property(x => x.Reason).HasMaxLength(500);
builder.Property(x => x.ReviewComment).HasMaxLength(500);
builder.Property(x => x.Version).IsRowVersion();
builder.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Status });
builder.HasIndex(x => new { x.TenantId, x.EmployeeId })
    .IsUnique()
    .HasFilter("\"status\" = 'pending'");
builder.HasIndex(x => new { x.TenantId, x.RequestedAgentId });
builder.HasOne<Employee>().WithMany()
    .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
builder.HasOne<RegisteredAgent>().WithMany()
    .HasForeignKey(x => x.CurrentAgentId).OnDelete(DeleteBehavior.Restrict);
builder.HasOne<RegisteredAgent>().WithMany()
    .HasForeignKey(x => x.RequestedAgentId).OnDelete(DeleteBehavior.Restrict);
builder.HasOne<User>().WithMany()
    .HasForeignKey(x => x.ReviewedById).OnDelete(DeleteBehavior.Restrict);
```

Add:

```csharp
public DbSet<AgentDeviceChangeRequest> AgentDeviceChangeRequests
    => Set<AgentDeviceChangeRequest>();
```

- [ ] **Step 5: Generate the migration**

Run from `HRMS-Backend-v1`:

```powershell
dotnet ef migrations add AddApprovedDeviceReplacement --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```

Expected: migration, designer, and model snapshot updates are generated.

- [ ] **Step 6: Add RLS SQL to the generated migration**

The migration `Up` must include:

```sql
ALTER TABLE agent_device_change_requests ENABLE ROW LEVEL SECURITY;
ALTER TABLE agent_device_change_requests FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON agent_device_change_requests
USING (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid);
```

The migration `Down` drops the policy before dropping the table.

- [ ] **Step 7: Verify migration and architecture tests**

Run:

```powershell
dotnet build src/ONEVO.Api/ONEVO.Api.csproj
dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --filter FullyQualifiedName~TenantIsolation
```

Expected: build and focused architecture tests pass.

- [ ] **Step 8: Commit Task 2**

Stage only the entity, configuration, DbContext, migration files, snapshot, and architecture test:

```powershell
git commit -m "feat(agent): persist device replacement requests"
```

---

### Task 3: Make Enrollment Activate Only the First Employee Device

**Files:**

- Modify: `src/ONEVO.Application/Features/Users/RepositoryInterfaces/IUserProfileRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/Users/EfUserProfileRepository.cs`
- Modify: `src/ONEVO.Application/Features/AgentGateway/RepositoryInterfaces/IAgentGatewayRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/AgentGateway/EfAgentGatewayRepository.cs`
- Modify: `src/ONEVO.Application/Features/AgentGateway/DTOs/EnrollCompleteResponseDto.cs`
- Modify: `src/ONEVO.Application/Features/AgentGateway/Commands/CompleteEnrollment/CompleteEnrollmentCommandHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/AgentGateway/AgentGatewayController.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/AgentGateway/CompleteEnrollmentCommandHandlerTests.cs`

**Interfaces:**

- Adds `GetEmployeeByIdAsync(Guid employeeId, CancellationToken ct)`.
- Adds `GetActiveAgentByEmployeeIdAsync`, `GetPendingDeviceChangeByEmployeeIdAsync`, and `AddDeviceChangeRequestAsync`.
- Extends enrollment response with `DeviceApprovalStatus` and `DeviceChangeRequestId`.

- [ ] **Step 1: Add failing first-device and replacement-device tests**

The first-device test asserts the new agent is active, a session is created, and no replacement request is created.

The replacement test uses an existing active agent and asserts:

```csharp
Assert.True(result.IsSuccess);
Assert.Equal("pending", result.Value!.DeviceApprovalStatus);
Assert.NotNull(result.Value.DeviceChangeRequestId);
_repo.Verify(x => x.AddDeviceChangeRequestAsync(
    It.Is<AgentDeviceChangeRequest>(r =>
        r.CurrentAgentId == currentAgent.Id &&
        r.RequestedAgentId == capturedCandidate.Id &&
        r.Status == "pending"),
    It.IsAny<CancellationToken>()), Times.Once);
_repo.Verify(x => x.AddSessionAsync(
    It.IsAny<AgentSession>(), It.IsAny<CancellationToken>()), Times.Never);
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter FullyQualifiedName~CompleteEnrollmentCommandHandlerTests
```

Expected: compile failures for the new DTO members and repository methods.

- [ ] **Step 3: Add repository/profile interfaces and EF implementations**

Use exact signatures:

```csharp
Task<Employee?> GetEmployeeByIdAsync(Guid employeeId, CancellationToken ct);
Task<RegisteredAgent?> GetActiveAgentByEmployeeIdAsync(Guid employeeId, CancellationToken ct);
Task<AgentDeviceChangeRequest?> GetPendingDeviceChangeByEmployeeIdAsync(
    Guid employeeId, CancellationToken ct);
Task AddDeviceChangeRequestAsync(AgentDeviceChangeRequest request, CancellationToken ct);
```

Every EF query remains tenant-filtered. `GetActiveAgentByEmployeeIdAsync` filters `EmployeeId == employeeId && Status == "active"`.

- [ ] **Step 4: Extend the enrollment response**

```csharp
public sealed record EnrollCompleteResponseDto(
    Guid AgentId,
    Guid TenantId,
    Guid EmployeeId,
    string EmployeeName,
    string DeviceToken,
    DateTimeOffset TokenExpiresAt,
    string PolicyJson,
    string DeviceApprovalStatus,
    Guid? DeviceChangeRequestId);
```

Expose `device_approval_status` and `device_change_request_id` from the controller.

- [ ] **Step 5: Implement first-device versus candidate behavior**

The handler algorithm is:

```csharp
var currentApproved = await _repo.GetActiveAgentByEmployeeIdAsync(employeeId, ct);
var sameApprovedDevice = currentApproved is not null &&
    string.Equals(currentApproved.DeviceId, request.DeviceId,
        StringComparison.OrdinalIgnoreCase);

if (currentApproved is null || sameApprovedDevice)
{
    agent.Status = "active";
    await CreateActiveSessionAsync(agent, employeeId, now, ct);
    approvalStatus = "approved";
}
else
{
    agent.Status = "inactive";
    var pending = await _repo.GetPendingDeviceChangeByEmployeeIdAsync(employeeId, ct);
    if (pending is null)
    {
        pending = new AgentDeviceChangeRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            CurrentAgentId = currentApproved.Id,
            RequestedAgentId = agent.Id,
            Status = "pending",
            RequestedAt = now,
            CreatedAt = now
        };
        await _repo.AddDeviceChangeRequestAsync(pending, ct);
    }
    approvalStatus = "pending";
    requestId = pending.Id;
}
```

Do not create an active `AgentSession` or monitoring policy for an inactive candidate. Generate its Agent JWT only for the pending-status endpoint.

Resolve the employee display name through `GetEmployeeByIdAsync`; remove the incorrect `_users.GetByIdAsync(employeeId)` lookup.

- [ ] **Step 6: Run focused and full unit tests**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter FullyQualifiedName~CompleteEnrollmentCommandHandlerTests
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj
```

Expected: all tests pass.

- [ ] **Step 7: Commit Task 3**

```powershell
git commit -m "feat(agent): require approval for replacement devices"
```

---

### Task 4: Add HR/Admin Approve and Reject Use Cases

**Files:**

- Create command/query files listed in the File Map.
- Modify: `src/ONEVO.Application/Features/AgentGateway/RepositoryInterfaces/IAgentGatewayRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/AgentGateway/EfAgentGatewayRepository.cs`
- Modify: `src/ONEVO.Api/Controllers/AgentGateway/AgentFleetController.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/AgentGateway/ApproveDeviceChangeCommandHandlerTests.cs`

**Interfaces:**

- `ApproveDeviceChangeCommand(Guid RequestId, string? ReviewComment, Guid ReviewedById)`.
- `RejectDeviceChangeCommand(Guid RequestId, string? ReviewComment, Guid ReviewedById)`.
- `GetDeviceChangeStatusQuery(Guid AgentId)`.
- `GetPendingDeviceChangesQuery(int Page, int PageSize)`.

- [ ] **Step 1: Write approval state-transition tests**

Test success and stale/non-pending conflicts. Success assertions:

```csharp
Assert.Equal("revoked", current.Status);
Assert.Equal("active", candidate.Status);
Assert.Equal("approved", request.Status);
Assert.Equal(reviewerId, request.ReviewedById);
Assert.NotNull(request.ReviewedAt);
_repo.Verify(x => x.EndActiveSessionAsync(
    current.DeviceId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
    Times.Once);
_uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
```

- [ ] **Step 2: Run the test and verify RED**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter FullyQualifiedName~ApproveDeviceChange
```

Expected: compile failure because command and handler do not exist.

- [ ] **Step 3: Add repository methods**

```csharp
Task<AgentDeviceChangeRequest?> GetDeviceChangeRequestByIdAsync(
    Guid requestId, CancellationToken ct);
Task<AgentDeviceChangeRequest?> GetDeviceChangeRequestByRequestedAgentIdAsync(
    Guid requestedAgentId, CancellationToken ct);
Task<IReadOnlyList<AgentDeviceChangeRequest>> GetPendingDeviceChangesAsync(
    int skip, int take, CancellationToken ct);
```

Cap `PageSize` at 100 and order pending requests by `RequestedAt`.

- [ ] **Step 4: Implement approve/reject handlers**

Approve validates:

```csharp
if (request is null)
    return Result.NotFound("Device change request not found.");
if (request.Status != "pending")
    return Result.Conflict("Device change request is no longer pending.");
if (current is null || candidate is null ||
    current.EmployeeId != request.EmployeeId ||
    candidate.EmployeeId != request.EmployeeId)
    return Result.Conflict("Device bindings changed; refresh and retry.");
```

Then revoke current, activate candidate, end the old session, create the candidate active session, set reviewer fields, and call `SaveChangesAsync` once.

Reject sets request status/reviewer fields and leaves old device active/candidate inactive.

- [ ] **Step 5: Add protected controller endpoints**

Use browser `TenantPolicy` plus existing permission filter:

```csharp
[HttpGet("device-change-requests")]
[Authorize(Policy = "TenantPolicy")]
[RequirePermission("agent:manage")]

[HttpPut("device-change-requests/{id:guid}/approve")]
[Authorize(Policy = "TenantPolicy")]
[RequirePermission("agent:manage")]

[HttpPut("device-change-requests/{id:guid}/reject")]
[Authorize(Policy = "TenantPolicy")]
[RequirePermission("agent:manage")]
```

Never accept reviewer id from the request body; use `ICurrentUser.UserId`.

- [ ] **Step 6: Run unit and controller architecture tests**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter FullyQualifiedName~DeviceChange
dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --filter FullyQualifiedName~Controller
```

Expected: all focused tests pass.

- [ ] **Step 7: Commit Task 4**

```powershell
git commit -m "feat(agent): approve employee device replacements"
```

---

### Task 5: Enforce Active Approved Device on Agent APIs

**Files:**

- Create: `src/ONEVO.Api/Authorization/ActiveAgentRequirement.cs`
- Create: `src/ONEVO.Api/Authorization/ActiveAgentAuthorizationHandler.cs`
- Modify: `src/ONEVO.Api/Extensions/AuthorizationExtensions.cs`
- Modify: `src/ONEVO.Api/Controllers/AgentGateway/AgentGatewayController.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/AgentGateway/ActiveAgentAuthorizationHandlerTests.cs`

**Interfaces:**

- Produces authorization policy `ActiveAgentPolicy`.
- Basic `AgentPolicy` remains available only for candidate device-change status polling.

- [ ] **Step 1: Write authorization-handler tests**

Cover active, inactive, revoked, wrong tenant, and missing employee binding. The active test succeeds only for:

```csharp
new RegisteredAgent
{
    Id = agentId,
    TenantId = tenantId,
    EmployeeId = Guid.NewGuid(),
    Status = "active"
};
```

The inactive and revoked cases must leave the authorization requirement unsatisfied.

- [ ] **Step 2: Run tests and verify RED**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter FullyQualifiedName~ActiveAgentAuthorization
```

Expected: compile failure because the authorization types do not exist.

- [ ] **Step 3: Implement the requirement and handler**

```csharp
public sealed class ActiveAgentRequirement : IAuthorizationRequirement;

public sealed class ActiveAgentAuthorizationHandler
    : AuthorizationHandler<ActiveAgentRequirement>
{
    private readonly IAgentGatewayRepository _repo;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActiveAgentRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirstValue("sub"), out var agentId) ||
            !Guid.TryParse(context.User.FindFirstValue("tenant_id"), out var tenantId))
            return;

        var agent = await _repo.GetAgentByIdAsync(agentId, CancellationToken.None);
        if (agent is not null &&
            agent.TenantId == tenantId &&
            agent.EmployeeId.HasValue &&
            agent.Status == "active")
            context.Succeed(requirement);
    }
}
```

Register the handler as scoped and define:

```csharp
options.AddPolicy("ActiveAgentPolicy", policy =>
    policy.AddAuthenticationSchemes("AgentScheme")
        .RequireAuthenticatedUser()
        .RequireClaim("type", "agent")
        .AddRequirements(new ActiveAgentRequirement()));
```

- [ ] **Step 4: Apply policies to endpoints**

Change login, logout, heartbeat, policy, and ingest endpoints to `ActiveAgentPolicy`.

Add a candidate-safe endpoint under basic `AgentPolicy`:

```csharp
[HttpGet("device-change/status")]
[Authorize(Policy = "AgentPolicy")]
public async Task<IActionResult> GetDeviceChangeStatus(CancellationToken ct)
```

The query resolves the requested agent id from `sub`; it does not accept agent, employee, or tenant ids from query/body.

- [ ] **Step 5: Run focused, API, and full backend tests**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter FullyQualifiedName~AgentGateway
dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --no-restore
```

Expected: all available tests pass; integration tests that require unavailable external infrastructure may be reported separately with their exact skip/failure reason.

- [ ] **Step 6: Commit Task 5**

```powershell
git commit -m "feat(agent): enforce approved device authorization"
```

---

### Task 6: Phase Verification and Handoff

**Files:**

- Modify only tests or implementation files required to fix failures introduced by Tasks 1-5.

**Interfaces:**

- Produces the stable foundation consumed by the location/face/attendance plan:
  - trustworthy employee id;
  - one active device;
  - pending replacement state;
  - approval/rejection APIs;
  - immediate revoked-device denial.

- [ ] **Step 1: Verify formatting and repository scope**

Run:

```powershell
dotnet format src/ONEVO.Api/ONEVO.Api.csproj --verify-no-changes
git status --short --untracked-files=all
```

Expected: formatting passes. Only known user-owned untracked files remain; no unrelated file is staged.

- [ ] **Step 2: Run the complete backend build and test suites**

Run:

```powershell
dotnet build src/ONEVO.Api/ONEVO.Api.csproj
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj
dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --no-build
```

Expected: build, unit tests, architecture tests, and available integration tests pass.

- [ ] **Step 3: Inspect the migration SQL**

Run:

```powershell
dotnet ef migrations script --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api --idempotent
```

Expected SQL contains the new table, filtered unique pending index, RLS enable/force/policy statements, and no destructive change to existing Agent Gateway data.

- [ ] **Step 4: Review the branch diff**

Run:

```powershell
git diff HEAD~5..HEAD --stat
git diff HEAD~5..HEAD --check
```

Expected: only approved-device foundation code, tests, migration, and this plan are present; whitespace check passes.

- [ ] **Step 5: Record completion**

Mark Tasks 1-6 complete in this plan only after the commands above pass. Then create the next detailed plan from the approved master spec:

`docs/superpowers/plans/2026-07-25-location-face-attendance-backend.md`

The next plan must consume `ActiveAgentPolicy` and the approved device context rather than reimplementing device validation.

## Self-Review

### Spec coverage

- Real employee id correction: Task 1.
- One approved device and pending replacement: Tasks 2-3.
- HR/Admin approval using `agent:manage`: Task 4.
- Old device revoked and new device activated atomically: Task 4.
- Revocation enforced before JWT expiry: Task 5.
- Candidate can poll status but cannot use active agent operations: Tasks 3 and 5.
- Tenant filters, RLS, concurrency, tests, and migration review: Tasks 2 and 6.

### Placeholder scan

The plan contains no unresolved markers, generic test instructions, or undefined implementation steps. Generated migration filenames are intentionally timestamp-generated by EF; the stable migration name is `AddApprovedDeviceReplacement`.

### Type consistency

- `AgentDeviceChangeRequest` is the only replacement aggregate name.
- `DeviceApprovalStatus` values are `approved` and `pending` in enrollment responses.
- Persistent request status values are `pending`, `approved`, `rejected`, `cancelled`, and `expired`.
- `ActiveAgentPolicy` is the only policy that authorizes active agent operations.
- `AgentPolicy` remains JWT-only for the device-change status endpoint.
