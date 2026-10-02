# Module Member Add/Remove Through the Approval Engine — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Route Module member add/remove through the existing WM Approval + Notification Engine, the same way module.edit/delete/transfer/achieve/etc. already work: the parent module's owner (or anyone above) acts directly and the change is notified; the module's own head or any plain member below sends a request to the parent owner, and the add/remove only happens once approved.

**Architecture:** Two new `WorkActionTypes` (`module.member_add`, `module.member_remove`) flow through the existing `IModuleActionSubmitter` → `IWorkApprovalEngine` → (direct now, or `IApprovalActionApplier` later) pipeline that `ModuleEdit`/`ModuleTransfer`/etc. already use (Plan 3, `2026-09-28-wm-approval-engine-plan-3-modules-and-sprints.md`). The actual mutation (create the invitation / deactivate the membership) is extracted out of the two command handlers into two new `IMilestoneMembershipCoordinator` methods (`ApplyMemberAddAsync`, `ApplyMemberRemoveAsync`), so the same mutation code runs for both the direct-apply callback and the approved-request applier — mirroring how `IModuleWriteService.ApplyEditAsync` already serves both paths for Edit.

**Tech Stack:** .NET 8 / C#, MediatR, EF Core (ONEVO.Infrastructure), xUnit + Moq + FluentAssertions (backend); Angular (standalone components, signals, NgRx SignalStore) (frontend).

**Spec:** `HRMS-Backend-v1/docs/superpowers/specs/next/2026-09-28-wm-hierarchy-approval-notification-engine-design.md` (the master engine design — this plan is a direct, same-shaped extension of its §5 Module-action rule, applied to the two Module actions Plan 3 left out). Also read Plan 3 itself, `HRMS-Backend-v1/docs/superpowers/plans/next/2026-09-28-wm-approval-engine-plan-3-modules-and-sprints.md`, for the pattern this plan copies.

## Global Constraints

- **Work Management only.** Never touch CoreHr, Leave, TimeAttendance, People, Calendar, or shared notification/outbox infrastructure code.
- **Always build and test with `-c Release`.** A running Debug API locks `bin/Debug`. Never kill the user's processes.
- **Never stash, reset or discard user work.** Never junction `node_modules` into a temporary worktree.
- Commit messages end with: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`
- **Useless-test rule:** delete a test only if it tests deleted code, duplicates another test, or asserts nothing behavioural. List every deleted test in its commit message. The two existing handler test files (`AddObjectiveMemberCommandHandlerTests.cs`, `RemoveObjectiveMemberCommandHandlerTests.cs`) get **rewritten**, not deleted — their behavioural intent (already-member no-op, pending-invite conflict, achieved-milestone block, caller-not-a-member forbidden, not-found) carries over to the new shape.
- **Known limitation to carry forward, not fix:** `ModuleActionSubmitter` always keys the approval-conflict check (`IWorkApprovalRequestRepository.HasPendingAsync`) by `(TenantId, TargetType=Module, TargetId=module.Id, ActionType)` — one pending slot per module **per action type**, not per employee. This already applies to every other Module action (two concurrent Edit proposals on the same module collide the same way) and is accepted there; `module.member_add`/`module.member_remove` inherit the same trade-off: only one pending add (and separately, one pending remove) per module at a time, regardless of which employee it names. Do not build a per-employee conflict key — that would require changing the shared conflict check for every action type.

---

## Part 1 — Backend

### Task 1: Action types and payload DTOs

**Files:**
- Modify: `src/ONEVO.Domain/Features/WorkManagement/Approvals/Entities/WorkApprovalRequest.cs:33-53` (`WorkActionTypes`)
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/ModuleActionPayloads.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Objectives/ModuleAppliersTests.cs` (payload round-trip is exercised by Task 3's applier tests; no standalone DTO test needed — these are plain records)

**Interfaces:**
- Produces: `WorkActionTypes.ModuleMemberAdd` = `"module.member_add"`, `WorkActionTypes.ModuleMemberRemove` = `"module.member_remove"`; `ModuleMemberAddInput(Guid EmployeeId, Guid RequestedByEmployeeId)`, `ModuleMemberRemoveInput(Guid EmployeeId)`.

- [ ] **Step 1: Add the two action type constants**

In `src/ONEVO.Domain/Features/WorkManagement/Approvals/Entities/WorkApprovalRequest.cs`, inside `WorkActionTypes`, add after `ModuleAllocationExtend`:

```csharp
    public const string ModuleAllocationExtend = "module.allocation_extend";
    public const string ModuleMemberAdd = "module.member_add";
    public const string ModuleMemberRemove = "module.member_remove";
```

- [ ] **Step 2: Add the two payload DTOs**

In `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/ModuleActionPayloads.cs`, add after `ModuleAllocationExtendInput`:

```csharp
/// <summary>payload_json of module.member_add. RequestedByEmployeeId travels in the payload (not just
/// the WorkApprovalRequest column) so the Applier - which only sees payloadJson - knows who to credit
/// as "invited you" on the invitation it creates once approved.</summary>
public sealed record ModuleMemberAddInput(Guid EmployeeId, Guid RequestedByEmployeeId);

/// <summary>payload_json of module.member_remove.</summary>
public sealed record ModuleMemberRemoveInput(Guid EmployeeId);
```

- [ ] **Step 3: Build**

Run: `dotnet build src/ONEVO.Domain src/ONEVO.Application -c Release`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Domain/Features/WorkManagement/Approvals/Entities/WorkApprovalRequest.cs src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/ModuleActionPayloads.cs
git commit -m "feat(work): add module.member_add/remove action types and payloads

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: `IMilestoneMembershipCoordinator.ApplyMemberAddAsync` / `ApplyMemberRemoveAsync`

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Services/IMilestoneMembershipCoordinator.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Services/MilestoneMembershipCoordinator.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/MilestoneMembershipCoordinatorTests.cs`

**Interfaces:**
- Consumes: `IProjectMemberInvitationRepository.AddAsync`, `.GetPendingForObjectiveAndEmployeeAsync`, `.GetTrackedPendingForObjectiveAndEmployeeAsync`, `.Update` (existing); `ICallerIdentityResolver.ResolveDisplayNamesByEmployeeIdAsync` (existing); `IOutboxWriter.EnqueueAsync` (existing); `Result<T>` / `Result` from `ONEVO.Application.Common.Models` (existing).
- Produces: `Task<Result<ProjectMemberInvitation>> ApplyMemberAddAsync(Guid tenantId, Objective module, Guid requestedByEmployeeId, Guid employeeId, CancellationToken ct = default)`; `Task<Result> ApplyMemberRemoveAsync(Guid tenantId, Objective module, Guid employeeId, CancellationToken ct = default)`. Both re-validate from scratch (active employee / not-already-member / no-pending-invite for Add; active-membership-or-pending-invite exists for Remove) so they're safe to call straight from an approved request, days after the original check.

First, read the current interface and implementation so the diff is exact:

- [ ] **Step 1: Read the current files**

Open `src/ONEVO.Application/Features/WorkManagement/Objectives/Services/IMilestoneMembershipCoordinator.cs` and `MilestoneMembershipCoordinator.cs` (already shown above in this plan's research — the interface lists `GetActiveAssigneeAsync`, `UpsertMembershipAsync`, `DeactivateMembershipAsync`, `HasOtherActiveAccessAsync`, `HasActiveMembershipAsync`, `IsEffectiveManagerAsync`, `IsEffectiveOwnerAsync`).

- [ ] **Step 2: Write the failing coordinator test for Add**

In `tests/ONEVO.Tests.Unit/Features/WorkManagement/MilestoneMembershipCoordinatorTests.cs`, the existing `BuildCoordinator` helper only wires `IEmployeeRepository`, `IProjectMemberRepository`, `IObjectiveRepository`. Extend it and add new tests. Replace the two `BuildCoordinator` overloads with:

```csharp
    private (MilestoneMembershipCoordinator Coordinator, Mock<IProjectMemberRepository> Members, Mock<IProjectMemberInvitationRepository> Invitations, Mock<IOutboxWriter> Outbox) BuildCoordinator(Employee? employee)
    {
        var (coordinator, members, invitations, outbox, _) = BuildCoordinator(employee, new Mock<IObjectiveRepository>());
        return (coordinator, members, invitations, outbox);
    }

    private (MilestoneMembershipCoordinator Coordinator, Mock<IProjectMemberRepository> Members, Mock<IProjectMemberInvitationRepository> Invitations, Mock<IOutboxWriter> Outbox, Mock<IObjectiveRepository> Objectives) BuildCoordinator(Employee? employee, Mock<IObjectiveRepository> objectives)
    {
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(x => x.GetByIdAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(employee);

        var members = new Mock<IProjectMemberRepository>();
        var invitations = new Mock<IProjectMemberInvitationRepository>();
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [RequestedById] = "Inviter" });
        var outbox = new Mock<IOutboxWriter>();

        var coordinator = new MilestoneMembershipCoordinator(employees.Object, members.Object, objectives.Object, invitations.Object, identity.Object, outbox.Object);
        return (coordinator, members, invitations, outbox, objectives);
    }
```

Add `RequestedById` next to the other `private static readonly Guid` fields at the top of the class:

```csharp
    private static readonly Guid RequestedById = Guid.NewGuid();
```

Add `using ONEVO.Application.Common.ServiceInterfaces;`, `using ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;`, `using ONEVO.Application.Features.WorkManagement.Common.Services;`, `using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;`, and `using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;` to the file's usings.

Then add a `Module` test fixture and the new tests at the end of the class, before the closing brace:

```csharp
    private static Objective Module() => new()
    {
        Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, Title = "Sub", OwnerId = Guid.NewGuid(),
        IsActive = true, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1)
    };

    [Fact]
    public async Task ApplyMemberAddAsync_ActiveEmployeeNotAlreadyMember_CreatesInvitationAndNotifies()
    {
        var (coordinator, members, invitations, outbox) = BuildCoordinator(ActiveEmployee());
        members.Setup(x => x.GetTrackedForObjectiveAsync(TenantId, ProjectId, ObjectiveId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProjectMember?)null);
        invitations.Setup(x => x.GetPendingForObjectiveAndEmployeeAsync(TenantId, ObjectiveId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProjectMemberInvitation?)null);

        var result = await coordinator.ApplyMemberAddAsync(TenantId, Module(), RequestedById, EmployeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(EmployeeId, result.Value!.InvitedEmployeeId);
        Assert.Equal(RequestedById, result.Value.InvitedById);
        Assert.Equal(ProjectInvitationStatuses.Pending, result.Value.Status);
        invitations.Verify(x => x.AddAsync(It.IsAny<ProjectMemberInvitation>(), It.IsAny<CancellationToken>()), Times.Once);
        outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.WorkNotification,
            It.Is<WorkNotificationPayload>(p => p.TemplateCode == "work_objective_invitation_created" && p.UserId == UserId),
            TenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyMemberAddAsync_EmployeeNotActive_Fails()
    {
        var (coordinator, _, _, _) = BuildCoordinator(InactiveEmployee());

        var result = await coordinator.ApplyMemberAddAsync(TenantId, Module(), RequestedById, EmployeeId, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ApplyMemberAddAsync_AlreadyActiveMember_Fails()
    {
        var (coordinator, members, _, _) = BuildCoordinator(ActiveEmployee());
        members.Setup(x => x.GetTrackedForObjectiveAsync(TenantId, ProjectId, ObjectiveId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectMember { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, EmployeeId = EmployeeId, IsActive = true });

        var result = await coordinator.ApplyMemberAddAsync(TenantId, Module(), RequestedById, EmployeeId, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ApplyMemberAddAsync_PendingInviteAlreadyExists_Fails()
    {
        var (coordinator, members, invitations, _) = BuildCoordinator(ActiveEmployee());
        members.Setup(x => x.GetTrackedForObjectiveAsync(TenantId, ProjectId, ObjectiveId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProjectMember?)null);
        invitations.Setup(x => x.GetPendingForObjectiveAndEmployeeAsync(TenantId, ObjectiveId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectMemberInvitation { Id = Guid.NewGuid(), TenantId = TenantId, ObjectiveId = ObjectiveId, InvitedEmployeeId = EmployeeId });

        var result = await coordinator.ApplyMemberAddAsync(TenantId, Module(), RequestedById, EmployeeId, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ApplyMemberRemoveAsync_ActiveMembership_Deactivates()
    {
        var (coordinator, members, _, _) = BuildCoordinator(ActiveEmployee());
        var membership = new ProjectMember { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, EmployeeId = EmployeeId, IsActive = true };
        members.Setup(x => x.GetTrackedForObjectiveAsync(TenantId, ProjectId, ObjectiveId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(membership);

        var result = await coordinator.ApplyMemberRemoveAsync(TenantId, Module(), EmployeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(membership.IsActive);
    }

    [Fact]
    public async Task ApplyMemberRemoveAsync_OnlyPendingInvite_CancelsIt()
    {
        var (coordinator, members, invitations, _) = BuildCoordinator(ActiveEmployee());
        members.Setup(x => x.GetTrackedForObjectiveAsync(TenantId, ProjectId, ObjectiveId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProjectMember?)null);
        var pending = new ProjectMemberInvitation { Id = Guid.NewGuid(), TenantId = TenantId, ObjectiveId = ObjectiveId, InvitedEmployeeId = EmployeeId, Status = ProjectInvitationStatuses.Pending };
        invitations.Setup(x => x.GetTrackedPendingForObjectiveAndEmployeeAsync(TenantId, ObjectiveId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pending);

        var result = await coordinator.ApplyMemberRemoveAsync(TenantId, Module(), EmployeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ProjectInvitationStatuses.Cancelled, pending.Status);
        invitations.Verify(x => x.Update(pending), Times.Once);
    }

    [Fact]
    public async Task ApplyMemberRemoveAsync_NeitherMembershipNorInvite_Fails()
    {
        var (coordinator, members, invitations, _) = BuildCoordinator(ActiveEmployee());
        members.Setup(x => x.GetTrackedForObjectiveAsync(TenantId, ProjectId, ObjectiveId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProjectMember?)null);
        invitations.Setup(x => x.GetTrackedPendingForObjectiveAndEmployeeAsync(TenantId, ObjectiveId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProjectMemberInvitation?)null);

        var result = await coordinator.ApplyMemberRemoveAsync(TenantId, Module(), EmployeeId, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }
```

Note `UserId` must already exist in this file as `Employee.UserId` for the active employee fixture (it does — see the existing `ActiveEmployee()`/`InactiveEmployee()` helpers at the top of the file).

- [ ] **Step 3: Run to verify it fails (won't even compile yet)**

Run: `dotnet build tests/ONEVO.Tests.Unit -c Release`
Expected: FAIL — `MilestoneMembershipCoordinator` has no constructor taking 6 args, no `ApplyMemberAddAsync`/`ApplyMemberRemoveAsync`.

- [ ] **Step 4: Add the two methods to the interface**

In `src/ONEVO.Application/Features/WorkManagement/Objectives/Services/IMilestoneMembershipCoordinator.cs`, add (check its current `using`s - it will need `ONEVO.Application.Common.Models` for `Result` and `ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities` for `ProjectMemberInvitation`):

```csharp
    /// <summary>The module.member_add mutation: re-validates (active employee, not already a member, no
    /// pending invite) and creates the invitation, notifying the invitee. Used by both the direct-apply
    /// path and the approved-request applier, so it must be safe to call long after any earlier check.</summary>
    Task<Result<ProjectMemberInvitation>> ApplyMemberAddAsync(Guid tenantId, Objective module, Guid requestedByEmployeeId, Guid employeeId, CancellationToken ct = default);

    /// <summary>The module.member_remove mutation: deactivates the active membership, or cancels the
    /// pending invite if there's no active membership. Fails if there's neither.</summary>
    Task<Result> ApplyMemberRemoveAsync(Guid tenantId, Objective module, Guid employeeId, CancellationToken ct = default);
```

- [ ] **Step 5: Implement in `MilestoneMembershipCoordinator`**

Add three new constructor dependencies and the two methods. Replace the constructor and add the usings:

```csharp
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
```

```csharp
public class MilestoneMembershipCoordinator : IMilestoneMembershipCoordinator
{
    private readonly IEmployeeRepository _employees;
    private readonly IProjectMemberRepository _members;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectMemberInvitationRepository _invitations;
    private readonly ICallerIdentityResolver _identity;
    private readonly IOutboxWriter _outboxWriter;

    public MilestoneMembershipCoordinator(
        IEmployeeRepository employees, IProjectMemberRepository members, IObjectiveRepository objectives,
        IProjectMemberInvitationRepository invitations, ICallerIdentityResolver identity, IOutboxWriter outboxWriter)
    {
        _employees = employees;
        _members = members;
        _objectives = objectives;
        _invitations = invitations;
        _identity = identity;
        _outboxWriter = outboxWriter;
    }
```

(Leave every existing method body unchanged - only the constructor signature grows.) Then add at the end of the class, before the closing brace:

```csharp
    public async Task<Result<ProjectMemberInvitation>> ApplyMemberAddAsync(
        Guid tenantId, Objective module, Guid requestedByEmployeeId, Guid employeeId, CancellationToken ct = default)
    {
        var assignee = await GetActiveAssigneeAsync(tenantId, employeeId, ct);
        if (assignee is null)
            return Result<ProjectMemberInvitation>.Failure("The member must be an active employee in this tenant.");

        if (await HasActiveMembershipAsync(tenantId, module.ProjectId, module.Id, assignee.Id, ct))
            return Result<ProjectMemberInvitation>.Conflict("This employee is already a member.");

        if (await _invitations.GetPendingForObjectiveAndEmployeeAsync(tenantId, module.Id, assignee.Id, ct) is not null)
            return Result<ProjectMemberInvitation>.Conflict("An invitation is already pending for this employee on this milestone.");

        var invitation = new ProjectMemberInvitation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProjectId = module.ProjectId,
            ObjectiveId = module.Id,
            InvitedEmployeeId = assignee.Id,
            InviteType = ProjectInvitationTypes.Member,
            Status = ProjectInvitationStatuses.Pending,
            InvitedById = requestedByEmployeeId
        };
        await _invitations.AddAsync(invitation, ct);

        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, [requestedByEmployeeId], ct);
        var inviterDisplayName = names.GetValueOrDefault(requestedByEmployeeId) ?? "A teammate";
        await _outboxWriter.EnqueueAsync(
            OutboxMessageTypes.WorkNotification,
            new WorkNotificationPayload(
                tenantId,
                assignee.UserId,
                "work_objective_invitation_created",
                new Dictionary<string, string>
                {
                    ["inviterName"] = inviterDisplayName,
                    ["objectiveName"] = module.Title,
                    ["inviteType"] = ProjectInvitationTypes.Member
                },
                "project_member_invitation",
                invitation.Id),
            tenantId,
            ct);

        return Result<ProjectMemberInvitation>.Success(invitation);
    }

    public async Task<Result> ApplyMemberRemoveAsync(Guid tenantId, Objective module, Guid employeeId, CancellationToken ct = default)
    {
        if (await HasActiveMembershipAsync(tenantId, module.ProjectId, module.Id, employeeId, ct))
        {
            await DeactivateMembershipAsync(tenantId, module.ProjectId, module.Id, employeeId, ct);
            return Result.Success();
        }

        var pendingInvite = await _invitations.GetTrackedPendingForObjectiveAndEmployeeAsync(tenantId, module.Id, employeeId, ct);
        if (pendingInvite is null)
            return Result.NotFound("This employee has no active membership or pending invitation on this milestone.");

        pendingInvite.Status = ProjectInvitationStatuses.Cancelled;
        pendingInvite.DecidedAt = DateTimeOffset.UtcNow;
        _invitations.Update(pendingInvite);
        return Result.Success();
    }
```

Note `CreatedById`/`CreatedAt` are intentionally left unset on the new `ProjectMemberInvitation` - `AuditableEntityInterceptor` (`src/ONEVO.Infrastructure/Persistence/Interceptors/AuditableEntityInterceptor.cs:44-48`) stamps both from the ambient `ICurrentUser` on insert regardless, so for the approved-request path they correctly reflect the approver (the one actually triggering the DB write), while `InvitedById` (the "who invited you" field shown to the invitee) correctly stays the original requester.

- [ ] **Step 6: Run the coordinator tests**

Run: `dotnet test tests/ONEVO.Tests.Unit -c Release --filter MilestoneMembershipCoordinatorTests`
Expected: all pass, including the 6 new tests and every pre-existing test in the file (they only needed the `BuildCoordinator` helper's constructor call updated, not their own bodies).

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/Services/IMilestoneMembershipCoordinator.cs src/ONEVO.Application/Features/WorkManagement/Objectives/Services/MilestoneMembershipCoordinator.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/MilestoneMembershipCoordinatorTests.cs
git commit -m "feat(work): add ApplyMemberAddAsync/ApplyMemberRemoveAsync to MilestoneMembershipCoordinator

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: `ModuleMemberAddApplier` / `ModuleMemberRemoveApplier`

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Appliers/ModuleMemberAddApplier.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Appliers/ModuleMemberRemoveApplier.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs:451` (register both, alongside the other Module appliers)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Objectives/ModuleAppliersTests.cs`

**Interfaces:**
- Consumes: `ModuleApplierBase` (existing, `src/.../Objectives/Appliers/ModuleApplierBase.cs`), `IMilestoneMembershipCoordinator.ApplyMemberAddAsync`/`ApplyMemberRemoveAsync` (Task 2), `WorkActionTypes.ModuleMemberAdd`/`ModuleMemberRemove`, `ModuleMemberAddInput`/`ModuleMemberRemoveInput` (Task 1).
- Produces: `ModuleMemberAddApplier`, `ModuleMemberRemoveApplier` (both `IApprovalActionApplier` via `ModuleApplierBase`).

- [ ] **Step 1: Write the failing applier tests**

In `tests/ONEVO.Tests.Unit/Features/WorkManagement/Objectives/ModuleAppliersTests.cs`, add two private factory methods near `Edit()`:

```csharp
    private ModuleMemberAddApplier MemberAdd() => new(_kit.Objectives.Object, _kit.Writes(), _kit.Membership.Object);
    private ModuleMemberRemoveApplier MemberRemove() => new(_kit.Objectives.Object, _kit.Writes(), _kit.Membership.Object);
```

And add tests at the end of the class, before the closing brace:

```csharp
    [Fact]
    public async Task MemberAdd_Success_Applied()
    {
        var employeeId = Guid.NewGuid();
        _kit.Membership.Setup(x => x.ApplyMemberAddAsync(K.TenantId, _kit.Module, K.Head, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectMemberInvitation>.Success(new ProjectMemberInvitation { Id = Guid.NewGuid(), InvitedEmployeeId = employeeId }));
        var request = Request(WorkActionTypes.ModuleMemberAdd,
            "{\"employeeId\":\"" + employeeId + "\",\"requestedByEmployeeId\":\"" + K.Head + "\"}");

        var outcome = await Apply(MemberAdd(), request);

        outcome.Kind.Should().Be(ApplyOutcomeKind.Applied);
    }

    [Fact]
    public async Task MemberAdd_CoordinatorRejects_Invalid()
    {
        var employeeId = Guid.NewGuid();
        _kit.Membership.Setup(x => x.ApplyMemberAddAsync(K.TenantId, _kit.Module, K.Head, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectMemberInvitation>.Conflict("This employee is already a member."));
        var request = Request(WorkActionTypes.ModuleMemberAdd,
            "{\"employeeId\":\"" + employeeId + "\",\"requestedByEmployeeId\":\"" + K.Head + "\"}");

        var outcome = await Apply(MemberAdd(), request);

        outcome.Kind.Should().Be(ApplyOutcomeKind.Invalid);
        outcome.Error.Should().Be("This employee is already a member.");
    }

    [Fact]
    public async Task MemberAdd_ModuleAchieved_Stale()
    {
        _kit.Module.IsAchieved = true;
        var employeeId = Guid.NewGuid();
        var request = Request(WorkActionTypes.ModuleMemberAdd,
            "{\"employeeId\":\"" + employeeId + "\",\"requestedByEmployeeId\":\"" + K.Head + "\"}");

        var outcome = await Apply(MemberAdd(), request);

        outcome.Kind.Should().Be(ApplyOutcomeKind.Stale);
    }

    [Fact]
    public async Task MemberRemove_Success_Applied()
    {
        var employeeId = Guid.NewGuid();
        _kit.Membership.Setup(x => x.ApplyMemberRemoveAsync(K.TenantId, _kit.Module, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        var request = Request(WorkActionTypes.ModuleMemberRemove, "{\"employeeId\":\"" + employeeId + "\"}");

        var outcome = await Apply(MemberRemove(), request);

        outcome.Kind.Should().Be(ApplyOutcomeKind.Applied);
    }

    [Fact]
    public async Task MemberRemove_CoordinatorRejects_Invalid()
    {
        var employeeId = Guid.NewGuid();
        _kit.Membership.Setup(x => x.ApplyMemberRemoveAsync(K.TenantId, _kit.Module, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.NotFound("This employee has no active membership or pending invitation on this milestone."));
        var request = Request(WorkActionTypes.ModuleMemberRemove, "{\"employeeId\":\"" + employeeId + "\"}");

        var outcome = await Apply(MemberRemove(), request);

        outcome.Kind.Should().Be(ApplyOutcomeKind.Invalid);
    }
```

Add `using ONEVO.Application.Common.Models;` and `using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;` to this file's usings if not already present.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet build tests/ONEVO.Tests.Unit -c Release`
Expected: FAIL - `ModuleMemberAddApplier`/`ModuleMemberRemoveApplier` don't exist yet, and `IMilestoneMembershipCoordinator` mock has no `ApplyMemberAddAsync`/`ApplyMemberRemoveAsync` (those exist now from Task 2, so this step mainly confirms the two new applier classes are missing).

- [ ] **Step 3: Create `ModuleMemberAddApplier`**

```csharp
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Appliers;

/// <summary>Applies an approved module.member_add: re-validates and creates the invitation via the
/// coordinator, which is also what the direct-apply path calls.</summary>
public sealed class ModuleMemberAddApplier : ModuleApplierBase
{
    private readonly IMilestoneMembershipCoordinator _membership;

    public ModuleMemberAddApplier(IObjectiveRepository objectives, IModuleWriteService modules, IMilestoneMembershipCoordinator membership)
        : base(objectives, modules) => _membership = membership;

    public override string ActionType => WorkActionTypes.ModuleMemberAdd;

    protected override bool IsStale(Objective module) => module.IsAchieved;

    protected override async Task<Result> ApplyAsync(Guid tenantId, Objective module, string payloadJson, CancellationToken ct)
    {
        var input = Read<ModuleMemberAddInput>(payloadJson);
        if (input is null || input.EmployeeId == Guid.Empty)
            return Result.Failure("The member-add request has no employee.");

        var applied = await _membership.ApplyMemberAddAsync(tenantId, module, input.RequestedByEmployeeId, input.EmployeeId, ct);
        return applied.IsSuccess ? Result.Success() : Result.Failure(applied.Error!, applied.StatusCode ?? 400);
    }
}
```

- [ ] **Step 4: Create `ModuleMemberRemoveApplier`**

```csharp
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Appliers;

/// <summary>Applies an approved module.member_remove via the coordinator, which is also what the
/// direct-apply path calls.</summary>
public sealed class ModuleMemberRemoveApplier : ModuleApplierBase
{
    private readonly IMilestoneMembershipCoordinator _membership;

    public ModuleMemberRemoveApplier(IObjectiveRepository objectives, IModuleWriteService modules, IMilestoneMembershipCoordinator membership)
        : base(objectives, modules) => _membership = membership;

    public override string ActionType => WorkActionTypes.ModuleMemberRemove;

    protected override bool IsStale(Objective module) => module.IsAchieved;

    protected override Task<Result> ApplyAsync(Guid tenantId, Objective module, string payloadJson, CancellationToken ct)
    {
        var input = Read<ModuleMemberRemoveInput>(payloadJson);
        if (input is null || input.EmployeeId == Guid.Empty)
            return Task.FromResult(Result.Failure("The member-remove request has no employee."));

        return _membership.ApplyMemberRemoveAsync(tenantId, module, input.EmployeeId, ct);
    }
}
```

- [ ] **Step 5: Register both in DI**

In `src/ONEVO.Infrastructure/DependencyInjection.cs`, add two lines next to the other Module appliers (after line 451, `ModuleAllocationExtendApplier`):

```csharp
        services.AddScoped<IApprovalActionApplier, ONEVO.Application.Features.WorkManagement.Objectives.Appliers.ModuleAllocationExtendApplier>();
        services.AddScoped<IApprovalActionApplier, ONEVO.Application.Features.WorkManagement.Objectives.Appliers.ModuleMemberAddApplier>();
        services.AddScoped<IApprovalActionApplier, ONEVO.Application.Features.WorkManagement.Objectives.Appliers.ModuleMemberRemoveApplier>();
```

- [ ] **Step 6: Run the applier tests**

Run: `dotnet test tests/ONEVO.Tests.Unit -c Release --filter ModuleAppliersTests`
Expected: all pass (existing + 5 new).

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/Appliers/ModuleMemberAddApplier.cs src/ONEVO.Application/Features/WorkManagement/Objectives/Appliers/ModuleMemberRemoveApplier.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Objectives/ModuleAppliersTests.cs
git commit -m "feat(work): add ModuleMemberAddApplier and ModuleMemberRemoveApplier

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Response DTOs and view models

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/AddObjectiveMemberOutcomeResponse.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/RemoveObjectiveMemberOutcomeResponse.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/AddObjectiveMemberOutcomeViewModel.cs`
- Create: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/RemoveObjectiveMemberOutcomeViewModel.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveViewModelMapper.cs:41-45`

**Interfaces:**
- Produces: `AddObjectiveMemberOutcomeResponse(bool Applied, bool AlreadyMember, Guid? ApprovalRequestId, ProjectMemberInvitationResponse? Invitation)`; `RemoveObjectiveMemberOutcomeResponse(bool Applied, Guid? ApprovalRequestId)`.

- [ ] **Step 1: Extend `AddObjectiveMemberOutcomeResponse`**

Replace the file's content:

```csharp
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

/// <summary>Applied - the invitation was created now (caller was the parent owner or above, or AlreadyMember
/// is true and there's nothing to do). Otherwise ApprovalRequestId is set and Invitation is null.</summary>
public sealed record AddObjectiveMemberOutcomeResponse(bool Applied, bool AlreadyMember, Guid? ApprovalRequestId, ProjectMemberInvitationResponse? Invitation);
```

- [ ] **Step 2: Create `RemoveObjectiveMemberOutcomeResponse`**

```csharp
namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

/// <summary>Applied - the member was removed (or their invite cancelled) now. Otherwise ApprovalRequestId is set.</summary>
public sealed record RemoveObjectiveMemberOutcomeResponse(bool Applied, Guid? ApprovalRequestId);
```

- [ ] **Step 3: Extend `AddObjectiveMemberOutcomeViewModel`**

```csharp
using ONEVO.Api.Contracts.WorkManagement.ProjectInvitations;

namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public class AddObjectiveMemberOutcomeViewModel
{
    public bool Applied { get; set; }
    public bool AlreadyMember { get; set; }
    public Guid? ApprovalRequestId { get; set; }
    public ProjectMemberInvitationViewModel? Invitation { get; set; }
}
```

- [ ] **Step 4: Create `RemoveObjectiveMemberOutcomeViewModel`**

```csharp
namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public class RemoveObjectiveMemberOutcomeViewModel
{
    public bool Applied { get; set; }
    public Guid? ApprovalRequestId { get; set; }
}
```

- [ ] **Step 5: Update the mapper**

In `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveViewModelMapper.cs`, replace lines 41-45 with:

```csharp
    public static AddObjectiveMemberOutcomeViewModel ToViewModel(this AddObjectiveMemberOutcomeResponse dto) => new()
    {
        Applied = dto.Applied,
        AlreadyMember = dto.AlreadyMember,
        ApprovalRequestId = dto.ApprovalRequestId,
        Invitation = dto.Invitation?.ToViewModel()
    };

    public static RemoveObjectiveMemberOutcomeViewModel ToViewModel(this RemoveObjectiveMemberOutcomeResponse dto) => new()
    {
        Applied = dto.Applied,
        ApprovalRequestId = dto.ApprovalRequestId
    };
```

- [ ] **Step 6: Build**

Run: `dotnet build src/ONEVO.Application src/ONEVO.Api -c Release`
Expected: FAIL at this point - `AddObjectiveMemberCommandHandler` still constructs the old 2-arg record. That's expected; Task 5 fixes it next. If you prefer a clean build at every step, do Task 4 and Task 5 as one combined commit instead of separately - either is fine, just don't leave the build red across a commit boundary that isn't immediately followed by the fix in the same task.

- [ ] **Step 7: Commit together with Task 5** (see Task 5's commit step - these two are naturally one unit since the DTO change forces the handler change).

---

### Task 5: Rewrite `AddObjectiveMemberCommandHandler`

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/AddObjectiveMember/AddObjectiveMemberCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Services/ModuleHandlerTestKit.cs` → actually `tests/ONEVO.Tests.Unit/Features/WorkManagement/Objectives/ModuleHandlerTestKit.cs`
- Modify (full rewrite): `tests/ONEVO.Tests.Unit/Features/WorkManagement/AddObjectiveMemberCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IModuleActionSubmitter.SubmitAsync` (existing), `IMilestoneMembershipCoordinator.ApplyMemberAddAsync` (Task 2), `ProjectMemberInvitationMapper.ToResponse` (existing, `src/ONEVO.Application/Features/WorkManagement/ProjectInvitations/Mappers/`).
- Produces: `AddObjectiveMemberCommandHandler` now depends on `(ICurrentUser, ICallerIdentityResolver, IObjectiveRepository, IMilestoneMembershipCoordinator, IProjectMemberInvitationRepository, IModuleActionSubmitter)` - six params, down from seven (drops `IUnitOfWork` and `IOutboxWriter`, which moved into the coordinator).

- [ ] **Step 1: Add `AddMember()` to `ModuleHandlerTestKit`**

In `tests/ONEVO.Tests.Unit/Features/WorkManagement/Objectives/ModuleHandlerTestKit.cs`:

Add these two setups in the constructor, right after the existing `Membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, NewHead, ...))` block (so "Other" - the non-owner caller - and "NewHead" - the member being added/removed in tests - have sane defaults):

```csharp
        Membership.Setup(x => x.HasActiveMembershipAsync(TenantId, ProjectId, ModuleId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        Membership.Setup(x => x.ApplyMemberAddAsync(TenantId, It.IsAny<Objective>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectMemberInvitation>.Success(new ProjectMemberInvitation
            {
                Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ModuleId,
                InvitedEmployeeId = NewHead, InviteType = ProjectInvitationTypes.Member, Status = ProjectInvitationStatuses.Pending
            }));
        Membership.Setup(x => x.ApplyMemberRemoveAsync(TenantId, It.IsAny<Objective>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        Invitations.Setup(x => x.GetPendingForObjectiveAndEmployeeAsync(TenantId, ModuleId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProjectMemberInvitation?)null);
```

Add two factory methods next to `Transfer()`:

```csharp
    public AddObjectiveMemberCommandHandler AddMember()
        => new(CurrentUser.Object, Identity.Object, Objectives.Object, Membership.Object, Invitations.Object, Submitter());

    public RemoveObjectiveMemberCommandHandler RemoveMember()
        => new(CurrentUser.Object, Identity.Object, Objectives.Object, Membership.Object, Submitter());
```

Add `using ONEVO.Application.Features.WorkManagement.Objectives.Commands.AddObjectiveMember;`, `using ONEVO.Application.Features.WorkManagement.Objectives.Commands.RemoveObjectiveMember;` to this file's usings.

- [ ] **Step 2: Rewrite `AddObjectiveMemberCommandHandlerTests.cs` to use the kit (failing first)**

Replace the whole file:

```csharp
using Moq;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.AddObjectiveMember;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;
using K = ONEVO.Tests.Unit.Features.WorkManagement.Objectives.ModuleHandlerTestKit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class AddObjectiveMemberCommandHandlerTests
{
    private static AddObjectiveMemberCommand Command() => new(K.ModuleId, K.NewHead);

    [Fact]
    public async Task Handle_AddByHeadBelowPosition_EnginePending_ReturnsApprovalRequestId()
    {
        var kit = new K();
        kit.EnginePending();

        var result = await kit.AddMember().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Applied);
        Assert.False(result.Value.AlreadyMember);
        Assert.Null(result.Value.Invitation);
        Assert.Equal(K.RequestId, result.Value.ApprovalRequestId);
    }

    [Fact]
    public async Task Handle_EngineDirect_CreatesInvitationAndReturnsIt()
    {
        var kit = new K();
        kit.CallAs(K.ParentOwnerUser);

        var result = await kit.AddMember().Handle(Command(), CancellationToken.None);

        Assert.True(result.Value!.Applied);
        Assert.False(result.Value.AlreadyMember);
        Assert.NotNull(result.Value.Invitation);
        Assert.Null(result.Value.ApprovalRequestId);
        kit.Membership.Verify(x => x.ApplyMemberAddAsync(K.TenantId, kit.Module, K.ParentOwner, K.NewHead, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AlreadyActiveMember_ShortCircuitsBeforeTheEngine()
    {
        var kit = new K();
        kit.Membership.Setup(x => x.HasActiveMembershipAsync(K.TenantId, K.ProjectId, K.ModuleId, K.NewHead, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await kit.AddMember().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.AlreadyMember);
        Assert.Null(result.Value.Invitation);
        kit.VerifyEngineNeverCalled();
    }

    [Fact]
    public async Task Handle_AlreadyPendingInvite_ReturnsConflict_BeforeTheEngine()
    {
        var kit = new K();
        kit.Invitations.Setup(x => x.GetPendingForObjectiveAndEmployeeAsync(K.TenantId, K.ModuleId, K.NewHead, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities.ProjectMemberInvitation
            {
                Id = Guid.NewGuid(), TenantId = K.TenantId, ObjectiveId = K.ModuleId, InvitedEmployeeId = K.NewHead
            });

        var result = await kit.AddMember().Handle(Command(), CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
        kit.VerifyEngineNeverCalled();
    }

    [Fact]
    public async Task Handle_MemberNotActiveEmployee_ReturnsBadRequest_BeforeTheEngine()
    {
        var kit = new K();
        kit.Membership.Setup(x => x.GetActiveAssigneeAsync(K.TenantId, K.NewHead, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.Employee?)null);

        var result = await kit.AddMember().Handle(Command(), CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        kit.VerifyEngineNeverCalled();
    }

    [Fact]
    public async Task Handle_CallerNotMember_ReturnsForbidden()
    {
        var kit = new K { OtherIsMember = false };
        kit.CallAs(K.OtherUser);

        var result = await kit.AddMember().Handle(Command(), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        kit.VerifyEngineNeverCalled();
    }

    [Fact]
    public async Task Handle_ObjectiveAchieved_ReturnsBadRequest()
    {
        var module = K.NewModule();
        module.IsAchieved = true;
        var kit = new K(module);

        var result = await kit.AddMember().Handle(Command(), CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        kit.VerifyEngineNeverCalled();
    }

    [Fact]
    public async Task Handle_ObjectiveNotFound_ReturnsNotFound()
    {
        var kit = new K();
        kit.Objectives.Setup(x => x.GetByIdForTenantAsync(K.TenantId, K.ModuleId, It.IsAny<CancellationToken>())).ReturnsAsync((Objective?)null);

        var result = await kit.AddMember().Handle(Command(), CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit -c Release --filter AddObjectiveMemberCommandHandlerTests`
Expected: FAIL to build - `AddObjectiveMemberCommandHandler`'s constructor doesn't match yet.

- [ ] **Step 4: Rewrite `AddObjectiveMemberCommandHandler`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.Mappers;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.AddObjectiveMember;

/// <summary>
/// Adds a milestone member through the approval engine: the parent's owner (or anyone above) invites
/// them now; the milestone's own head or members below file a module.member_add request to the
/// parent's owner. Either way, "added" means a pending ProjectMemberInvitation the invitee must accept.
/// </summary>
public class AddObjectiveMemberCommandHandler : IRequestHandler<AddObjectiveMemberCommand, Result<AddObjectiveMemberOutcomeResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IProjectMemberInvitationRepository _invitations;
    private readonly IModuleActionSubmitter _submitter;

    public AddObjectiveMemberCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IMilestoneMembershipCoordinator membership, IProjectMemberInvitationRepository invitations, IModuleActionSubmitter submitter)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _membership = membership;
        _invitations = invitations;
        _submitter = submitter;
    }

    public async Task<Result<AddObjectiveMemberOutcomeResponse>> Handle(AddObjectiveMemberCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<AddObjectiveMemberOutcomeResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<AddObjectiveMemberOutcomeResponse>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<AddObjectiveMemberOutcomeResponse>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<AddObjectiveMemberOutcomeResponse>.NotFound("Objective not found.");

        if (objective.IsAchieved)
            return Result<AddObjectiveMemberOutcomeResponse>.Failure("Cannot add members to an achieved milestone.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result<AddObjectiveMemberOutcomeResponse>.Forbidden("Only this milestone's head or an active member can add members.");

        var assignee = await _membership.GetActiveAssigneeAsync(tenantId, request.EmployeeId, ct);
        if (assignee is null)
            return Result<AddObjectiveMemberOutcomeResponse>.Failure("The member must be an active employee in this tenant.");

        if (await _membership.HasActiveMembershipAsync(tenantId, objective.ProjectId, objective.Id, assignee.Id, ct))
            return Result<AddObjectiveMemberOutcomeResponse>.Success(new AddObjectiveMemberOutcomeResponse(true, true, null, null));

        if (await _invitations.GetPendingForObjectiveAndEmployeeAsync(tenantId, objective.Id, assignee.Id, ct) is not null)
            return Result<AddObjectiveMemberOutcomeResponse>.Conflict("An invitation is already pending for this employee on this milestone.");

        var input = new ModuleMemberAddInput(assignee.Id, callerEmployeeId.Value);
        ProjectMemberInvitation? createdInvitation = null;
        var outcome = await _submitter.SubmitAsync(tenantId, callerEmployeeId.Value, objective, WorkActionTypes.ModuleMemberAdd, input,
            async (tracked, innerCt) =>
            {
                var applied = await _membership.ApplyMemberAddAsync(tenantId, tracked, callerEmployeeId.Value, assignee.Id, innerCt);
                if (!applied.IsSuccess)
                    return Result.Failure(applied.Error!, applied.StatusCode ?? 400);
                createdInvitation = applied.Value;
                return Result.Success();
            }, ct: ct);

        if (!outcome.IsSuccess)
            return Result<AddObjectiveMemberOutcomeResponse>.Failure(outcome.Error!, outcome.StatusCode ?? 400);

        return Result<AddObjectiveMemberOutcomeResponse>.Success(outcome.Value!.Applied
            ? new AddObjectiveMemberOutcomeResponse(true, false, null, createdInvitation is null ? null : ProjectMemberInvitationMapper.ToResponse(createdInvitation))
            : new AddObjectiveMemberOutcomeResponse(false, false, outcome.Value.ApprovalRequestId, null));
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/ONEVO.Tests.Unit -c Release --filter "AddObjectiveMemberCommandHandlerTests|ModuleAppliersTests|MilestoneMembershipCoordinatorTests"`
Expected: all pass.

- [ ] **Step 6: Commit (Tasks 4 and 5 together)**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/AddObjectiveMemberOutcomeResponse.cs src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/RemoveObjectiveMemberOutcomeResponse.cs src/ONEVO.Api/Contracts/WorkManagement/Objectives/AddObjectiveMemberOutcomeViewModel.cs src/ONEVO.Api/Contracts/WorkManagement/Objectives/RemoveObjectiveMemberOutcomeViewModel.cs src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveViewModelMapper.cs src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/AddObjectiveMember/AddObjectiveMemberCommandHandler.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Objectives/ModuleHandlerTestKit.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/AddObjectiveMemberCommandHandlerTests.cs
git commit -m "feat(work): route AddObjectiveMemberCommandHandler through the approval engine

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Rewrite `RemoveObjectiveMemberCommandHandler`

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/RemoveObjectiveMember/RemoveObjectiveMemberCommand.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/RemoveObjectiveMember/RemoveObjectiveMemberCommandHandler.cs`
- Modify (full rewrite): `tests/ONEVO.Tests.Unit/Features/WorkManagement/RemoveObjectiveMemberCommandHandlerTests.cs`

**Interfaces:**
- Produces: `RemoveObjectiveMemberCommand : IRequest<Result<RemoveObjectiveMemberOutcomeResponse>>` (was `IRequest<Result>`). `RemoveObjectiveMemberCommandHandler` now depends on `(ICurrentUser, ICallerIdentityResolver, IObjectiveRepository, IMilestoneMembershipCoordinator, IModuleActionSubmitter)` - five params, down from six (drops `IProjectMemberInvitationRepository` and `IUnitOfWork`, both now only needed inside the coordinator).

- [ ] **Step 1: Update the command's return type**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.RemoveObjectiveMember;

public sealed record RemoveObjectiveMemberCommand(Guid ObjectiveId, Guid EmployeeId) : IRequest<Result<RemoveObjectiveMemberOutcomeResponse>>;
```

- [ ] **Step 2: Rewrite the test file (failing first)**

```csharp
using Moq;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.RemoveObjectiveMember;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;
using K = ONEVO.Tests.Unit.Features.WorkManagement.Objectives.ModuleHandlerTestKit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class RemoveObjectiveMemberCommandHandlerTests
{
    private static RemoveObjectiveMemberCommand Command() => new(K.ModuleId, K.NewHead);

    [Fact]
    public async Task Handle_RemoveByHeadBelowPosition_EnginePending_ReturnsApprovalRequestId()
    {
        var kit = new K();
        kit.EnginePending();

        var result = await kit.RemoveMember().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Applied);
        Assert.Equal(K.RequestId, result.Value.ApprovalRequestId);
    }

    [Fact]
    public async Task Handle_EngineDirect_RemovesMember()
    {
        var kit = new K();
        kit.CallAs(K.ParentOwnerUser);

        var result = await kit.RemoveMember().Handle(Command(), CancellationToken.None);

        Assert.True(result.Value!.Applied);
        Assert.Null(result.Value.ApprovalRequestId);
        kit.Membership.Verify(x => x.ApplyMemberRemoveAsync(K.TenantId, kit.Module, K.NewHead, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TargetIsCurrentHead_ReturnsBadRequest_BeforeTheEngine()
    {
        var kit = new K();

        var result = await kit.RemoveMember().Handle(new RemoveObjectiveMemberCommand(K.ModuleId, K.Head), CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        kit.VerifyEngineNeverCalled();
    }

    [Fact]
    public async Task Handle_CallerNotMember_ReturnsForbidden()
    {
        var kit = new K { OtherIsMember = false };
        kit.CallAs(K.OtherUser);

        var result = await kit.RemoveMember().Handle(Command(), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        kit.VerifyEngineNeverCalled();
    }

    [Fact]
    public async Task Handle_ObjectiveAchieved_ReturnsBadRequest()
    {
        var module = K.NewModule();
        module.IsAchieved = true;
        var kit = new K(module);

        var result = await kit.RemoveMember().Handle(Command(), CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        kit.VerifyEngineNeverCalled();
    }

    [Fact]
    public async Task Handle_ObjectiveNotFound_ReturnsNotFound()
    {
        var kit = new K();
        kit.Objectives.Setup(x => x.GetByIdForTenantAsync(K.TenantId, K.ModuleId, It.IsAny<CancellationToken>())).ReturnsAsync((Objective?)null);

        var result = await kit.RemoveMember().Handle(Command(), CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit -c Release --filter RemoveObjectiveMemberCommandHandlerTests`
Expected: FAIL to build.

- [ ] **Step 4: Rewrite `RemoveObjectiveMemberCommandHandler`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.RemoveObjectiveMember;

/// <summary>
/// Removes a milestone member through the approval engine: the parent's owner (or anyone above) removes
/// them now; the milestone's own head or members below file a module.member_remove request to the
/// parent's owner.
/// </summary>
public class RemoveObjectiveMemberCommandHandler : IRequestHandler<RemoveObjectiveMemberCommand, Result<RemoveObjectiveMemberOutcomeResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IModuleActionSubmitter _submitter;

    public RemoveObjectiveMemberCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IMilestoneMembershipCoordinator membership, IModuleActionSubmitter submitter)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _membership = membership;
        _submitter = submitter;
    }

    public async Task<Result<RemoveObjectiveMemberOutcomeResponse>> Handle(RemoveObjectiveMemberCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<RemoveObjectiveMemberOutcomeResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<RemoveObjectiveMemberOutcomeResponse>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<RemoveObjectiveMemberOutcomeResponse>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<RemoveObjectiveMemberOutcomeResponse>.NotFound("Objective not found.");

        if (objective.IsAchieved)
            return Result<RemoveObjectiveMemberOutcomeResponse>.Failure("Cannot remove members from an achieved milestone.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result<RemoveObjectiveMemberOutcomeResponse>.Forbidden("Only this milestone's head or an active member can remove members.");

        if (request.EmployeeId == objective.OwnerId)
            return Result<RemoveObjectiveMemberOutcomeResponse>.Failure("Cannot remove the milestone's head as a member - use Transfer instead.");

        var input = new ModuleMemberRemoveInput(request.EmployeeId);
        var outcome = await _submitter.SubmitAsync(tenantId, callerEmployeeId.Value, objective, WorkActionTypes.ModuleMemberRemove, input,
            (tracked, innerCt) => _membership.ApplyMemberRemoveAsync(tenantId, tracked, request.EmployeeId, innerCt),
            extraRecipients: [request.EmployeeId], ct: ct);

        return outcome.IsSuccess
            ? Result<RemoveObjectiveMemberOutcomeResponse>.Success(new RemoveObjectiveMemberOutcomeResponse(outcome.Value!.Applied, outcome.Value.ApprovalRequestId))
            : Result<RemoveObjectiveMemberOutcomeResponse>.Failure(outcome.Error!, outcome.StatusCode ?? 400);
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/ONEVO.Tests.Unit -c Release --filter RemoveObjectiveMemberCommandHandlerTests`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/RemoveObjectiveMember/RemoveObjectiveMemberCommand.cs src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/RemoveObjectiveMember/RemoveObjectiveMemberCommandHandler.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/RemoveObjectiveMemberCommandHandlerTests.cs
git commit -m "feat(work): route RemoveObjectiveMemberCommandHandler through the approval engine

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Controller endpoints

**Files:**
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs:124-146`

**Interfaces:**
- Consumes: `AddObjectiveMemberOutcomeResponse`/`RemoveObjectiveMemberOutcomeResponse` (Task 4), both handlers' new return shapes (Tasks 5-6).

No new unit tests for this step - the controller is a thin pass-through already covered by the handler tests above and by Task 8's integration/build check. (If the repo's convention is to also run a controller-level integration test suite, run it in Task 9's final verification; this plan doesn't add new controller tests because none exist today for `AddMember`/`RemoveMember`.)

- [ ] **Step 1: Replace the two endpoints**

In `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs`, replace lines 124-135 (`AddMember`) and 137-146 (`RemoveMember`):

```csharp
    /// <summary>Adds a member through the approval engine: 204 if already a member, 202 with the invitation if the caller is the parent's owner or above, otherwise 202 with { approvalRequestId }.</summary>
    [HttpPost("{id:guid}/members")]
    public async Task<IActionResult> AddMember(Guid id, [FromBody] AddObjectiveMemberRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new AddObjectiveMemberCommand(id, request.EmployeeId), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var outcome = result.Value!;
        if (outcome.AlreadyMember)
            return StatusCode(204, outcome.ToViewModel());

        return outcome.Applied
            ? StatusCode(202, outcome.ToViewModel())
            : StatusCode(202, new { approvalRequestId = outcome.ApprovalRequestId });
    }

    /// <summary>Removes a member through the approval engine: 204 when the caller is the parent's owner or above, otherwise 202 { approvalRequestId }. Rejects removing the current head - use Transfer instead.</summary>
    [HttpDelete("{id:guid}/members/{employeeId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid id, Guid employeeId, CancellationToken ct)
    {
        var result = await _mediator.Send(new RemoveObjectiveMemberCommand(id, employeeId), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return result.Value!.Applied
            ? NoContent()
            : StatusCode(202, new { approvalRequestId = result.Value.ApprovalRequestId });
    }
```

Note the two 202 shapes from `AddMember` are deliberately different: `outcome.ToViewModel()` (has an `invitation` field) when the add was applied immediately, vs the bare `{ approvalRequestId }` object when it's pending approval. The frontend tells them apart by which field is present - same technique `TransferOutcomeViewModel` already uses for its own three-way split.

- [ ] **Step 2: Build**

Run: `dotnet build src/ONEVO.Api -c Release`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs
git commit -m "feat(work): AddMember/RemoveMember endpoints return approval-pending 202s

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Backend final verification

- [ ] **Step 1: Full build**

Run: `dotnet build -c Release`
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Full unit test suite**

Run: `dotnet test tests/ONEVO.Tests.Unit -c Release`
Expected: all pass. Paste the real pass count.

- [ ] **Step 3: Architecture test suite**

Run: `dotnet test tests/ONEVO.Tests.Architecture -c Release` (adjust the path/project name if it differs - check `find tests -iname "*Architecture*"` first)
Expected: all pass.

- [ ] **Step 4: Grep for anything left using the old direct-mutation shape**

Run: `grep -rn "Only this milestone's head can add members\|Only this milestone's head can remove members" src/`
Expected: no matches (both messages were replaced in Task 5/6; if either old project knowledge reference turns up elsewhere, e.g. frontend error-string matching, note it - don't silently change unrelated code).

---

## Part 2 — Frontend

### Task 9: DTOs and API service

**Files:**
- Modify: `src/app/modules/work/models/dto/milestone.dto.ts:85-95`
- Modify: `src/app/modules/work/data-access/objective-member-api.service.ts:22-24`

No new spec file for this task - pure type/interface changes with no runtime branching to test in isolation. Covered by Task 10's store spec.

- [ ] **Step 1: Extend `AddObjectiveMemberOutcomeDto`, add `RemoveObjectiveMemberOutcomeDto`**

In `src/app/modules/work/models/dto/milestone.dto.ts`, replace lines 85-88:

```typescript
export interface AddObjectiveMemberOutcomeDto {
  applied: boolean;
  alreadyMember: boolean;
  /** Set when the add was sent for approval instead of applied. */
  approvalRequestId?: string | null;
  invitation: import('./objective-invitation.dto').ObjectiveInvitationDto | null;
}

export interface RemoveObjectiveMemberOutcomeDto {
  applied: boolean;
  /** Set when the remove was sent for approval instead of applied. */
  approvalRequestId?: string | null;
}
```

- [ ] **Step 2: Update `removeMember`'s return type**

In `src/app/modules/work/data-access/objective-member-api.service.ts`, update the import and the method:

```typescript
import { AddObjectiveMemberOutcomeDto, RemoveObjectiveMemberOutcomeDto, TransferObjectiveHeadOutcomeDto } from '../models/dto/milestone.dto';
```

```typescript
  removeMember(objectiveId: string, employeeId: string): Observable<HttpResponse<RemoveObjectiveMemberOutcomeDto>> {
    return this.http.delete<RemoveObjectiveMemberOutcomeDto>(`${this.base}/${objectiveId}/members/${employeeId}`, { observe: 'response' });
  }
```

- [ ] **Step 3: Build**

Run: `ng build` (from `Hrms--Web-application---front-end---v1/`)
Expected: FAIL - `project-detail.store.ts` still reads `res.body!.alreadyMember` without the new fields and `removeMember`'s old `void` body assumptions; that's Task 10 next.

- [ ] **Step 4: Commit together with Task 10** (see Task 10's commit step).

---

### Task 10: Store methods

**Files:**
- Modify: `src/app/modules/work/state/project-detail.store.ts:232-252`
- Modify: `src/app/modules/work/state/project-detail.store.spec.ts` (find the existing spec file for this store - check `find src/app/modules/work/state -iname "project-detail.store.spec.ts"` - and add/update tests there; if no spec file exists for this store today, skip the spec step and note it in the final report)

**Interfaces:**
- Produces: `addObjectiveMember(objectiveId, employeeId): Promise<{ success: boolean; alreadyMember: boolean; applied: boolean; pending: boolean }>`; `removeObjectiveMember(objectiveId, employeeId): Promise<{ success: boolean; applied: boolean; pending: boolean }>`.

- [ ] **Step 1: Check for an existing spec file**

Run: `find src/app/modules/work/state -iname "project-detail.store.spec.ts"`

If found, read it to see how `editMilestone`/`transferObjectiveHead` are tested there (mock `ObjectiveMemberApiService`/`WorkObjectiveApiService`, assert on the returned `{ success, pending }` shape) and add two tests for `addObjectiveMember`/`removeObjectiveMember` returning `pending: true` on a 202 with no `invitation`/`applied:false`, following that exact pattern. If no such spec file exists, skip to Step 2 and say so in the final report - this plan doesn't introduce a new test file for a store that has none today (that would be unrelated scope).

- [ ] **Step 2: Update `addObjectiveMember`**

Replace lines 232-241 in `src/app/modules/work/state/project-detail.store.ts`:

```typescript
    async addObjectiveMember(objectiveId: string, employeeId: string): Promise<{ success: boolean; alreadyMember: boolean; applied: boolean; pending: boolean }> {
      try {
        const res = await firstValueFrom(memberApi.addMember(objectiveId, employeeId));
        patchState(store, { milestoneActionError: null });
        const body = res.body!;
        return { success: true, alreadyMember: body.alreadyMember, applied: body.applied, pending: !body.alreadyMember && !body.applied };
      } catch (err) {
        patchState(store, { milestoneActionError: extractError(err, 'Failed to add member.') });
        return { success: false, alreadyMember: false, applied: false, pending: false };
      }
    },
```

- [ ] **Step 3: Update `removeObjectiveMember`**

Replace lines 243-252:

```typescript
    async removeObjectiveMember(objectiveId: string, employeeId: string): Promise<{ success: boolean; applied: boolean; pending: boolean }> {
      try {
        const res = await firstValueFrom(memberApi.removeMember(objectiveId, employeeId));
        patchState(store, { milestoneActionError: null });
        // 204 = applied (ASP.NET drops a 204's body); 202 = sent for approval.
        const applied = res.status === 204 || !!res.body?.applied;
        return { success: true, applied, pending: !applied };
      } catch (err) {
        patchState(store, { milestoneActionError: extractError(err, 'Failed to remove member.') });
        return { success: false, applied: false, pending: false };
      }
    },
```

- [ ] **Step 4: Build**

Run: `ng build`
Expected: FAIL - `milestone-tree-tab.component.ts`'s `onAddMemberRequested`/`onRemoveMemberRequested` don't read the new return shape yet, but that's additive (not a type error) since they currently just `await` the call without using the result. Re-check: if it builds clean here, that's fine too - proceed to Task 11 regardless.

- [ ] **Step 5: If a spec file exists, run it**

Run: `ng test --include='**/project-detail.store.spec.ts'` (adjust the glob to whatever the repo's existing spec-running convention is for this file)
Expected: all pass.

- [ ] **Step 6: Commit (Tasks 9 and 10 together)**

```bash
git add src/app/modules/work/models/dto/milestone.dto.ts src/app/modules/work/data-access/objective-member-api.service.ts src/app/modules/work/state/project-detail.store.ts
git commit -m "feat(work): surface approval-pending state from member add/remove

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

(If Step 1 found and updated a spec file, `git add` it too in this commit.)

---

### Task 11: Tree-tab pending-approval toast

**Files:**
- Modify: `src/app/modules/work/feature/milestone-tree-tab/milestone-tree-tab.component.ts:617-630`
- Modify: `src/app/modules/work/feature/milestone-tree-tab/milestone-tree-tab.component.spec.ts` (if it covers `onAddMemberRequested`/`onRemoveMemberRequested` today - check first)

- [ ] **Step 1: Check the spec file for existing coverage of these two handlers**

Run: `grep -n "onAddMemberRequested\|onRemoveMemberRequested" src/app/modules/work/feature/milestone-tree-tab/milestone-tree-tab.component.spec.ts`

If matches exist, read the surrounding tests to match their mocking style (how `store.addObjectiveMember`/`removeObjectiveMember` and `notificationService.info` are spied on elsewhere in that file, e.g. around the Sprint "sent for approval" toast) and add two tests asserting `notificationService.info` is called with the pending message when the store call returns `pending: true`, and NOT called when it returns `pending: false`. If no matches, skip the spec step and note it in the final report.

- [ ] **Step 2: Update the two handlers**

Replace lines 617-630 in `src/app/modules/work/feature/milestone-tree-tab/milestone-tree-tab.component.ts`:

```typescript
  // addRequested now emits a real employeeId from EmployeePickerComponent (Task 7).
  async onAddMemberRequested(employeeId: string): Promise<void> {
    const node = this.selectedNode();
    if (!node) return;
    const result = await this.store.addObjectiveMember(node.id, employeeId);
    if (result.pending) this.notificationService.info('Member add sent for approval.');
    await this.store.loadObjectiveMembers(node.id);
  }

  async onRemoveMemberRequested(employeeId: string): Promise<void> {
    const node = this.selectedNode();
    if (!node) return;
    const result = await this.store.removeObjectiveMember(node.id, employeeId);
    if (result.pending) this.notificationService.info('Member removal sent for approval.');
    await this.store.loadObjectiveMembers(node.id);
  }
```

- [ ] **Step 3: Build**

Run: `ng build`
Expected: Build succeeded.

- [ ] **Step 4: Run the component spec**

Run: `ng test --include='**/milestone-tree-tab.component.spec.ts'`
Expected: all pass (existing + any new tests from Step 1).

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/work/feature/milestone-tree-tab/milestone-tree-tab.component.ts
git commit -m "feat(work): toast when member add/remove is sent for approval

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

(If Step 1 found and updated the spec file, `git add` it too in this commit.)

---

### Task 12: Frontend final verification

- [ ] **Step 1: Full build**

Run: `ng build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Full test suite**

Run: `ng test --watch=false`
Expected: all pass, or only the pre-existing unrelated failures already known from the current branch (the WM spec fails from a teammate's work-dropdown migration, per project memory - confirm the count matches what's already on the branch before this plan's changes, don't silently absorb new failures into "pre-existing").

---

## Final Verification Checklist (both repos)

- [ ] Backend: `dotnet build -c Release` - 0 errors
- [ ] Backend: `dotnet test tests/ONEVO.Tests.Unit -c Release` - all pass, paste the real count
- [ ] Backend: `dotnet test tests/ONEVO.Tests.Architecture -c Release` - all pass
- [ ] Frontend: `ng build` - 0 errors
- [ ] Frontend: `ng test --watch=false` - all pass (or only already-known pre-existing failures, confirmed by diffing the count against a clean checkout of the branch before this plan)
- [ ] Manual browser check (see the execution prompt's final report section)
