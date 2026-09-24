# `roles:manage` Bypass for Sensitive-Position Approval Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A `roles:manage` holder who onboards or transfers someone into a sensitive position (its active `PositionAccessTemplate.RequiresApproval == true`) completes the action immediately instead of queuing it, while an `Approved`-stamped `AccessGrantRequest` audit row is still written.

**Architecture:** Both entry points (`ChangeEmployeePositionCommandHandler` for transfer, `OnboardingDraftWriteService.FinalizeImmediatelyAsync` for onboarding) gain a single-user permission check (`IPermissionRepository.UserHasPermissionCodeAsync`, already exists) that, when true, routes the sensitive-position branch through the same "activate seat / assign role now" code the non-sensitive path already uses, instead of the pending-approval branch — then additionally writes an `AccessGrantRequest` pre-stamped `Approved` for audit history. No schema change, no DI change, no new repository method.

**Tech Stack:** .NET / C#, MediatR, EF Core (Npgsql), xUnit + Moq for unit tests, Testcontainers (Postgres) for integration tests.

**Spec:** `docs/superpowers/specs/next/2026-08-19-sensitive-position-approval-bypass-design.md`

## Global Constraints

- Promotion is out of scope — no promotion workflow exists in this codebase; only onboarding and position-change ("transfer") are touched.
- Self-transfer stays blocked regardless of `roles:manage` — do not touch `ChangeEmployeePositionCommandHandler`'s existing `employee.UserId == _currentUser.UserId` check (line 88-89); it runs unconditionally, before the bypass logic is ever reached.
- Reuse existing methods only: `IPermissionRepository.UserHasPermissionCodeAsync(Guid userId, string permissionCode, DateTimeOffset now, CancellationToken ct)` and `AccessGrantRequest.DecisionNote` (`string?`) already exist — no new repository methods, no new entity fields, no EF migration.
- The audit `DecisionNote` text is exactly `"Self-authorized: requester holds roles:manage."` in both flows — keep it identical so a reviewer/reporting query can match on it verbatim.
- `OnboardingDraftWriteService.cs` and `FinalizeOnboardingDraftCommandHandlerTests.cs` currently have unrelated uncommitted local changes on this branch (a transactional refactor of `FinalizeImmediatelyAsync` from other in-progress work). This plan's line references and code blocks are written against that current on-disk state — read the actual file before editing, don't assume it matches a clean `git show HEAD`.

---

### Task 1: `ChangeEmployeePositionCommandHandler` — `roles:manage` bypass for transfer

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/Employee/Commands/ChangeEmployeePosition/ChangeEmployeePositionCommandHandler.cs:112-185` (sensitive branch), and its closing brace region near the bottom (`PositionAtCapacityException` class, line 246)
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/ChangeEmployeePositionCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IPermissionRepository.UserHasPermissionCodeAsync(Guid userId, string permissionCode, DateTimeOffset now, CancellationToken ct)` (existing); `IPositionAssignmentRepository.ActivatePlannedAsync(Guid tenantId, Guid positionAssignmentId, CancellationToken ct)` (existing, already used by `ApproveAccessGrantRequestCommandHandler`).
- Produces: no new public surface — `ChangeEmployeePositionResponse(PendingApproval: false)` is returned on a successful bypass, same shape the non-sensitive path already returns.

- [ ] **Step 1: Write the failing tests**

Add these three tests to `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/ChangeEmployeePositionCommandHandlerTests.cs` (inside the existing `ChangeEmployeePositionCommandHandlerTests` class, after `Handle_SensitivePosition_DuplicatePending_ReturnsConflict_WithoutReserving`):

```csharp
[Fact]
public async Task Handle_SensitivePosition_ActorHasRolesManage_BypassesApprovalAndActivatesImmediately()
{
    var tenantId = Guid.NewGuid();
    var employeeId = Guid.NewGuid();
    var positionId = Guid.NewGuid();
    var oldAssignmentId = Guid.NewGuid();
    var reservedAssignmentId = Guid.NewGuid();
    SetupNonSelfCaller(tenantId, employeeId);
    var accessTemplate = new PositionAccessTemplate { Id = Guid.NewGuid(), RequiresApproval = true, RoleId = Guid.NewGuid() };
    _positions.Setup(p => p.GetByIdForLegalEntityAsync(tenantId, It.IsAny<Guid>(), positionId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Position { Id = positionId, TenantId = tenantId, IsActive = true, DepartmentId = Guid.NewGuid() });
    _positions.Setup(p => p.GetAccessTemplateByPositionAsync(tenantId, positionId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(accessTemplate);
    _permissionRepository
        .Setup(p => p.UserHasPermissionCodeAsync(It.IsAny<Guid>(), "roles:manage", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(true);
    _assignments
        .Setup(a => a.GetActivePrimaryAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment
        {
            Id = oldAssignmentId,
            TenantId = tenantId,
            EmployeeId = employeeId,
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
        });
    _assignments
        .Setup(a => a.TryReservePositionAssignmentAsync(tenantId, employeeId, positionId, It.IsAny<DateOnly>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(reservedAssignmentId);
    _assignments
        .Setup(a => a.EndActiveAsync(tenantId, oldAssignmentId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(true);
    _assignments
        .Setup(a => a.ActivatePlannedAsync(tenantId, reservedAssignmentId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(true);

    AccessGrantRequest? addedRequest = null;
    _accessGrantRequestRepository.Setup(r => r.AddAsync(It.IsAny<AccessGrantRequest>(), It.IsAny<CancellationToken>()))
        .Callback<AccessGrantRequest, CancellationToken>((r, _) => addedRequest = r)
        .Returns(Task.CompletedTask);

    var handler = CreateHandler();
    var result = await handler.Handle(
        new ChangeEmployeePositionCommand(employeeId, positionId, DateOnly.FromDateTime(DateTime.UtcNow), "Transfer"),
        CancellationToken.None);

    Assert.True(result.IsSuccess);
    Assert.False(result.Value!.PendingApproval);
    _assignments.Verify(a => a.EndActiveAsync(tenantId, oldAssignmentId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Once);
    _assignments.Verify(a => a.ActivatePlannedAsync(tenantId, reservedAssignmentId, It.IsAny<CancellationToken>()), Times.Once);
    Assert.NotNull(addedRequest);
    Assert.Equal("Approved", addedRequest!.ApprovalStatus);
    Assert.NotNull(addedRequest.DecidedByUserId);
    Assert.NotNull(addedRequest.DecidedAt);
    Assert.Equal("Self-authorized: requester holds roles:manage.", addedRequest.DecisionNote);
    _outboxWriter.Verify(w => w.EnqueueAsync(
        OutboxMessageTypes.PositionChangeApprovalRequestEmail,
        It.IsAny<PositionChangeApprovalRequestEmailPayload>(),
        It.IsAny<Guid?>(),
        It.IsAny<CancellationToken>()), Times.Never);
}

[Fact]
public async Task Handle_TargetIsCallersOwnEmployee_EvenWithRolesManage_ReturnsForbidden()
{
    var tenantId = Guid.NewGuid();
    var userId = Guid.NewGuid();
    var employeeId = Guid.NewGuid();
    _currentUser.Setup(c => c.TenantId).Returns(tenantId);
    _currentUser.Setup(c => c.UserId).Returns(userId);
    _employees
        .Setup(e => e.GetTrackedByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = employeeId, TenantId = tenantId, UserId = userId, LegalEntityId = Guid.NewGuid() });
    _permissionRepository
        .Setup(p => p.UserHasPermissionCodeAsync(userId, "roles:manage", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(true);

    var handler = CreateHandler();
    var result = await handler.Handle(
        new ChangeEmployeePositionCommand(employeeId, Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), "Promotion"),
        CancellationToken.None);

    Assert.False(result.IsSuccess);
    Assert.Equal(403, result.StatusCode);
    Assert.Equal("You cannot change your own position.", result.Error);
}

[Fact]
public async Task Handle_SensitivePosition_BypassApproval_ActivationFails_ReturnsConflict()
{
    var tenantId = Guid.NewGuid();
    var employeeId = Guid.NewGuid();
    var positionId = Guid.NewGuid();
    var reservedAssignmentId = Guid.NewGuid();
    SetupNonSelfCaller(tenantId, employeeId);
    _positions.Setup(p => p.GetByIdForLegalEntityAsync(tenantId, It.IsAny<Guid>(), positionId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Position { Id = positionId, TenantId = tenantId, IsActive = true, DepartmentId = Guid.NewGuid() });
    _positions.Setup(p => p.GetAccessTemplateByPositionAsync(tenantId, positionId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new PositionAccessTemplate { Id = Guid.NewGuid(), RequiresApproval = true, RoleId = Guid.NewGuid() });
    _permissionRepository
        .Setup(p => p.UserHasPermissionCodeAsync(It.IsAny<Guid>(), "roles:manage", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(true);
    _assignments
        .Setup(a => a.GetActivePrimaryAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
        .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment?)null);
    _assignments
        .Setup(a => a.TryReservePositionAssignmentAsync(tenantId, employeeId, positionId, It.IsAny<DateOnly>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(reservedAssignmentId);
    _assignments
        .Setup(a => a.ActivatePlannedAsync(tenantId, reservedAssignmentId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(false);

    var handler = CreateHandler();
    var result = await handler.Handle(
        new ChangeEmployeePositionCommand(employeeId, positionId, DateOnly.FromDateTime(DateTime.UtcNow), "Transfer"),
        CancellationToken.None);

    Assert.False(result.IsSuccess);
    Assert.Equal(409, result.StatusCode);
    _accessGrantRequestRepository.Verify(r => r.AddAsync(It.IsAny<AccessGrantRequest>(), It.IsAny<CancellationToken>()), Times.Never);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ChangeEmployeePositionCommandHandlerTests"`
Expected: the three new tests FAIL (bypass never happens yet — `PendingApproval` comes back `true`, `ActivatePlannedAsync`/`EndActiveAsync` are never called), existing tests still PASS.

- [ ] **Step 3: Implement the bypass**

In `src/ONEVO.Application/Features/CoreHr/Employee/Commands/ChangeEmployeePosition/ChangeEmployeePositionCommandHandler.cs`, replace the sensitive branch (currently lines 112-185) with:

```csharp
        var accessTemplate = await _positionRepository.GetAccessTemplateByPositionAsync(tenantId, position.Id, ct);
        if (accessTemplate is { RequiresApproval: true })
        {
            if (position.DepartmentId is null)
                return Result<ChangeEmployeePositionResponse>.UnprocessableEntity(
                    "The selected position has no department and cannot be used.");

            var hasPendingChange = await _accessGrantRequestRepository.AnyPendingByEmployeeAsync(
                tenantId, employee.Id, ct);
            if (hasPendingChange)
                return Result<ChangeEmployeePositionResponse>.Conflict(
                    "A position change for this employee is already awaiting approval.");

            var bypassApproval = await _permissionRepository.UserHasPermissionCodeAsync(
                _currentUser.UserId, "roles:manage", _clock.UtcNow, ct);

            if (bypassApproval)
                return await CompleteSensitiveChangeBypassingApprovalAsync(
                    tenantId, employee, position, accessTemplate, request, ct);

            var approverUserIds = await _permissionRepository.ListUserIdsWithPermissionCodeAsync(
                tenantId, "roles:manage", _clock.UtcNow, ct);
            if (approverUserIds.Count == 0)
                return Result<ChangeEmployeePositionResponse>.UnprocessableEntity(
                    "No one currently holds the permission required to approve this request.");

            var tenantSlug = (await _tenantRepository.GetByIdAsync(tenantId, ct))?.Slug;

            try
            {
                await _unitOfWork.ExecuteInTransactionAsync(async txnCt =>
                {
                    var reservedAssignmentId = await _positionAssignmentRepository.TryReservePositionAssignmentAsync(
                        tenantId, employee.Id, position.Id, request.EffectiveFrom, _currentUser.UserId, request.ReportsToEmployeeId, txnCt);
                    if (reservedAssignmentId is null)
                        throw new PositionAtCapacityException();

                    var grantRequest = new AccessGrantRequest
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        EmployeeId = employee.Id,
                        ActionType = AccessGrantActionType.PositionChange,
                        TargetPositionId = position.Id,
                        TargetDepartmentId = position.DepartmentId.Value,
                        PositionAccessTemplateId = accessTemplate.Id,
                        RequestedRoleId = accessTemplate.RoleId,
                        ApprovalStatus = "Pending",
                        RequestedByUserId = _currentUser.UserId,
                        RequestedAt = _clock.UtcNow,
                        EffectiveFrom = new DateTimeOffset(request.EffectiveFrom.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                        ReservedPositionAssignmentId = reservedAssignmentId,
                        ChangeReason = request.ChangeReason,
                    };
                    await _accessGrantRequestRepository.AddAsync(grantRequest, txnCt);

                    foreach (var approverUserId in approverUserIds)
                        await EnqueuePositionChangeApprovalEmailAsync(
                            tenantId, approverUserId, grantRequest, employee, position, tenantSlug, txnCt);

                    await _accessGrantRequestRepository.SaveChangesAsync(txnCt);
                    return true;
                }, ct);
            }
            catch (PositionAtCapacityException)
            {
                return Result<ChangeEmployeePositionResponse>.Conflict("This position has reached its capacity.");
            }
            catch (ConcurrencyConflictException)
            {
                return Result<ChangeEmployeePositionResponse>.Conflict(
                    "This request was just updated by someone else. Please refresh and try again.");
            }
            catch (UniqueConstraintConflictException)
            {
                return Result<ChangeEmployeePositionResponse>.Conflict(
                    "This employee's position was just changed by someone else. Please refresh and try again.");
            }

            return Result<ChangeEmployeePositionResponse>.Success(new ChangeEmployeePositionResponse(PendingApproval: true));
        }
```

(Only two lines changed inside this block versus what's on disk today: the `bypassApproval` check and its `if` branch are inserted right after the `hasPendingChange` guard.)

Then add this new private method directly below `Handle` (before `EnqueuePositionChangeApprovalEmailAsync`):

```csharp
    /// <summary>Completes a sensitive-position change immediately for an actor who holds
    /// roles:manage, instead of routing through the pending-approval queue. Still writes an
    /// AccessGrantRequest so the change remains visible in approval history, pre-stamped
    /// Approved with the actor as both requester and decider.</summary>
    private async Task<Result<ChangeEmployeePositionResponse>> CompleteSensitiveChangeBypassingApprovalAsync(
        Guid tenantId,
        Employee employee,
        Position position,
        PositionAccessTemplate accessTemplate,
        ChangeEmployeePositionCommand request,
        CancellationToken ct)
    {
        var currentAssignment = await _positionAssignmentRepository.GetActivePrimaryAsync(tenantId, employee.Id, ct);

        try
        {
            await _unitOfWork.ExecuteInTransactionAsync(async txnCt =>
            {
                var reservedAssignmentId = await _positionAssignmentRepository.TryReservePositionAssignmentAsync(
                    tenantId, employee.Id, position.Id, request.EffectiveFrom, _currentUser.UserId, request.ReportsToEmployeeId, txnCt);
                if (reservedAssignmentId is null)
                    throw new PositionAtCapacityException();

                if (currentAssignment is not null)
                {
                    var effectiveTo = request.EffectiveFrom.AddDays(-1);
                    if (effectiveTo < currentAssignment.EffectiveFrom)
                        effectiveTo = currentAssignment.EffectiveFrom;
                    var ended = await _positionAssignmentRepository.EndActiveAsync(
                        tenantId, currentAssignment.Id, effectiveTo, txnCt);
                    if (!ended)
                        throw new UniqueConstraintConflictException(
                            new InvalidOperationException("Active primary assignment was already ended."));
                }

                var activated = await _positionAssignmentRepository.ActivatePlannedAsync(
                    tenantId, reservedAssignmentId.Value, txnCt);
                if (!activated)
                    throw new ReservedSeatUnavailableException();

                var grantRequest = new AccessGrantRequest
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    EmployeeId = employee.Id,
                    ActionType = AccessGrantActionType.PositionChange,
                    TargetPositionId = position.Id,
                    TargetDepartmentId = position.DepartmentId!.Value,
                    PositionAccessTemplateId = accessTemplate.Id,
                    RequestedRoleId = accessTemplate.RoleId,
                    ApprovalStatus = "Approved",
                    RequestedByUserId = _currentUser.UserId,
                    DecidedByUserId = _currentUser.UserId,
                    RequestedAt = _clock.UtcNow,
                    DecidedAt = _clock.UtcNow,
                    EffectiveFrom = new DateTimeOffset(request.EffectiveFrom.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                    ReservedPositionAssignmentId = reservedAssignmentId,
                    ChangeReason = request.ChangeReason,
                    DecisionNote = "Self-authorized: requester holds roles:manage.",
                };
                await _accessGrantRequestRepository.AddAsync(grantRequest, txnCt);
                await _accessGrantRequestRepository.SaveChangesAsync(txnCt);
                return true;
            }, ct);
        }
        catch (PositionAtCapacityException)
        {
            return Result<ChangeEmployeePositionResponse>.Conflict("This position has reached its capacity.");
        }
        catch (ReservedSeatUnavailableException)
        {
            return Result<ChangeEmployeePositionResponse>.Conflict(
                "The reserved seat for this request is no longer available.");
        }
        catch (ConcurrencyConflictException)
        {
            return Result<ChangeEmployeePositionResponse>.Conflict(
                "This request was just updated by someone else. Please refresh and try again.");
        }
        catch (UniqueConstraintConflictException)
        {
            return Result<ChangeEmployeePositionResponse>.Conflict(
                "This employee's position was just changed by someone else. Please refresh and try again.");
        }

        return Result<ChangeEmployeePositionResponse>.Success(new ChangeEmployeePositionResponse(PendingApproval: false));
    }
```

Finally, add the new exception class next to the existing `PositionAtCapacityException` at the bottom of the file:

```csharp
    private sealed class PositionAtCapacityException : Exception;

    /// <summary>Thrown inside ExecuteInTransactionAsync so a reserved-but-unactivatable seat
    /// (should not happen — reserve and activate run in the same transaction against a freshly
    /// created row) rolls back cleanly instead of leaving a Planned row with no matching
    /// AccessGrantRequest.</summary>
    private sealed class ReservedSeatUnavailableException : Exception;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ChangeEmployeePositionCommandHandlerTests"`
Expected: all tests in this file PASS (the 3 new ones plus every pre-existing one — the default-`false` `UserHasPermissionCodeAsync` mock return preserves current behavior for every test that doesn't explicitly set it up).

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Employee/Commands/ChangeEmployeePosition/ChangeEmployeePositionCommandHandler.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/ChangeEmployeePositionCommandHandlerTests.cs
git commit -m "feat: roles:manage bypasses sensitive-position approval on transfer"
```

---

### Task 2: `OnboardingDraftWriteService` — `roles:manage` bypass for onboarding

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Services/OnboardingDraftWriteService.cs:364-377` (branch decision) and `:439-440` / `:564` (`FinalizeImmediatelyAsync`, current line numbers on disk — re-read the file first, it has unrelated local edits per Global Constraints)
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/OnboardingDrafts/FinalizeOnboardingDraftCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IPermissionRepository.UserHasPermissionCodeAsync` (same signature as Task 1); `OnboardingDraftWriteService`'s own existing `ToUtcMidnight(DateOnly date)` static helper.
- Produces: `FinalizeImmediatelyAsync` gains a new `bool selfAuthorizedBypass` parameter (inserted before `CancellationToken ct`) — this is a private method, no external caller to update besides the single call site fixed in this same task.

- [ ] **Step 1: Write the failing test**

Add this test to `tests/ONEVO.Tests.Unit/Features/CoreHr/OnboardingDrafts/FinalizeOnboardingDraftCommandHandlerTests.cs`, directly after `Handle_AccessTemplateRequiringApproval_CreatesAccessGrantRequestAndDefersEverythingElse`. First add this using directive alongside the existing ones at the top of the file (needed for the outbox-email assertion):

```csharp
using ONEVO.Application.Features.CoreHr.Onboarding.OutboxHandlers;
```

Then the test:

```csharp
[Fact]
public async Task Handle_AccessTemplateRequiringApproval_ActorHasRolesManage_FinalizesImmediatelyWithApprovedAuditRow()
{
    var positionId = Guid.NewGuid();
    var draft = ValidDraft(positionId: positionId);
    SetupDraft(draft);
    SetupPosition(positionId, departmentId: Guid.NewGuid());
    var roleId = Guid.NewGuid();
    var template = new PositionAccessTemplate { Id = Guid.NewGuid(), TenantId = _tenantId, PositionId = positionId, RoleId = roleId, RequiresApproval = true, IsActive = true };
    _positionRepository
        .Setup(r => r.GetAccessTemplateByPositionAsync(_tenantId, positionId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(template);
    _permissionRepository
        .Setup(r => r.UserHasPermissionCodeAsync(_userId, "roles:manage", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(true);

    UserRole? addedRole = null;
    _userRoleRepository.Setup(r => r.AddAsync(It.IsAny<UserRole>(), It.IsAny<CancellationToken>()))
        .Callback<UserRole, CancellationToken>((r, _) => addedRole = r).Returns(Task.CompletedTask);
    AccessGrantRequest? addedRequest = null;
    _accessGrantRequestRepository.Setup(r => r.AddAsync(It.IsAny<AccessGrantRequest>(), It.IsAny<CancellationToken>()))
        .Callback<AccessGrantRequest, CancellationToken>((r, _) => addedRequest = r).Returns(Task.CompletedTask);

    var result = await CreateHandler().Handle(new FinalizeOnboardingDraftCommand(draft.Id), CancellationToken.None);

    Assert.True(result.IsSuccess);
    Assert.False(result.Value!.PositionApprovalPending);
    Assert.True(result.Value.InvitationQueued);
    Assert.Equal(OnboardingDraftStatus.Finalized, draft.Status);

    Assert.NotNull(addedRole);
    Assert.Equal(roleId, addedRole!.RoleId);
    Assert.Equal(positionId, addedRole.SourcePositionId);

    Assert.NotNull(addedRequest);
    Assert.Equal(AccessGrantActionType.EmployeeOnboarding, addedRequest!.ActionType);
    Assert.Equal("Approved", addedRequest.ApprovalStatus);
    Assert.Equal(_userId, addedRequest.RequestedByUserId);
    Assert.Equal(_userId, addedRequest.DecidedByUserId);
    Assert.NotNull(addedRequest.DecidedAt);
    Assert.Equal("Self-authorized: requester holds roles:manage.", addedRequest.DecisionNote);
    Assert.Equal(draft.Id, addedRequest.OnboardingDraftId);

    _outboxWriter.Verify(w => w.EnqueueAsync(
        OutboxMessageTypes.PositionChangeApprovalRequestEmail,
        It.IsAny<PositionChangeApprovalRequestEmailPayload>(),
        It.IsAny<Guid?>(),
        It.IsAny<CancellationToken>()), Times.Never);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~Handle_AccessTemplateRequiringApproval_ActorHasRolesManage_FinalizesImmediatelyWithApprovedAuditRow"`
Expected: FAIL — today `result.Value.PositionApprovalPending` comes back `true` and nothing else in the assertion list happens yet (the draft goes to `WaitingForPositionApproval` instead of `Finalized`).

- [ ] **Step 3: Implement the bypass**

In `src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Services/OnboardingDraftWriteService.cs`, re-read the file first (per Global Constraints — it has unrelated local edits, so re-confirm current line numbers before editing). Make three changes:

**3a. Branch decision** — find the block that currently reads (originally around line 364-377):

```csharp
        PositionAccessTemplate? accessTemplate = null;
        if (draft.PositionId is not null)
        {
            accessTemplate = await _positionRepository.GetAccessTemplateByPositionAsync(tenantId, draft.PositionId.Value, ct);
        }
        var requiresApproval = accessTemplate is { IsActive: true, RequiresApproval: true };

        if (requiresApproval)
        {
            return await FinalizeWithPendingApprovalAsync(draft, accessTemplate!, position!, actingUserId, ct);
        }

        return await FinalizeImmediatelyAsync(draft, accessTemplate, position, employmentTypeId.Value, actingUserId, ct);
    }
```

Replace with:

```csharp
        PositionAccessTemplate? accessTemplate = null;
        if (draft.PositionId is not null)
        {
            accessTemplate = await _positionRepository.GetAccessTemplateByPositionAsync(tenantId, draft.PositionId.Value, ct);
        }
        var requiresApproval = accessTemplate is { IsActive: true, RequiresApproval: true };
        var bypassApproval = requiresApproval
            && await _permissionRepository.UserHasPermissionCodeAsync(actingUserId, "roles:manage", _clock.UtcNow, ct);

        if (requiresApproval && !bypassApproval)
        {
            return await FinalizeWithPendingApprovalAsync(draft, accessTemplate!, position!, actingUserId, ct);
        }

        return await FinalizeImmediatelyAsync(draft, accessTemplate, position, employmentTypeId.Value, actingUserId, bypassApproval, ct);
    }
```

**3b. `FinalizeImmediatelyAsync` signature** — find:

```csharp
    private async Task<Result<FinalizeOnboardingDraftResponse>> FinalizeImmediatelyAsync(
        OnboardingDraftEntity draft, PositionAccessTemplate? accessTemplate, Position? position, int employmentTypeId, Guid actingUserId, CancellationToken ct)
```

Replace with:

```csharp
    private async Task<Result<FinalizeOnboardingDraftResponse>> FinalizeImmediatelyAsync(
        OnboardingDraftEntity draft, PositionAccessTemplate? accessTemplate, Position? position, int employmentTypeId,
        Guid actingUserId, bool selfAuthorizedBypass, CancellationToken ct)
```

**3c. Role-assignment condition + audit row** — inside `FinalizeImmediatelyAsync`'s transaction, find:

```csharp
                if (position is not null)
                {
                    reservedAssignmentId = await _positionAssignmentRepository.TryReservePositionAssignmentAsync(
                        draft.TenantId, employeeId, position.Id, draft.StartDate, actingUserId, draft.ReportsToEmployeeId, txnCt);
                    if (reservedAssignmentId is null)
                        throw new PositionAtCapacityException();
                }

                // The only role ever assigned here is the position access template's own RoleId -
                // never a hardcoded Owner/Admin default.
                if (accessTemplate is { IsActive: true, RequiresApproval: false })
```

Replace with:

```csharp
                if (position is not null)
                {
                    reservedAssignmentId = await _positionAssignmentRepository.TryReservePositionAssignmentAsync(
                        draft.TenantId, employeeId, position.Id, draft.StartDate, actingUserId, draft.ReportsToEmployeeId, txnCt);
                    if (reservedAssignmentId is null)
                        throw new PositionAtCapacityException();
                }

                if (selfAuthorizedBypass)
                {
                    var grantRequest = new AccessGrantRequest
                    {
                        Id = Guid.NewGuid(),
                        TenantId = draft.TenantId,
                        EmployeeId = employeeId,
                        UserId = user.Id,
                        OnboardingDraftId = draft.Id,
                        ActionType = AccessGrantActionType.EmployeeOnboarding,
                        TargetPositionId = draft.PositionId!.Value,
                        TargetDepartmentId = position!.DepartmentId!.Value,
                        PositionAccessTemplateId = accessTemplate!.Id,
                        RequestedRoleId = accessTemplate.RoleId,
                        ApprovalStatus = "Approved",
                        RequestedByUserId = actingUserId,
                        DecidedByUserId = actingUserId,
                        RequestedAt = _clock.UtcNow,
                        DecidedAt = _clock.UtcNow,
                        EffectiveFrom = ToUtcMidnight(draft.StartDate),
                        ReservedPositionAssignmentId = reservedAssignmentId,
                        DecisionNote = "Self-authorized: requester holds roles:manage.",
                    };
                    await _accessGrantRequestRepository.AddAsync(grantRequest, txnCt);
                }

                // The only role ever assigned here is the position access template's own RoleId -
                // never a hardcoded Owner/Admin default. Also assigned when a roles:manage holder
                // is self-authorizing a bypass past a template that does require approval.
                if (accessTemplate is { IsActive: true } && (!accessTemplate.RequiresApproval || selfAuthorizedBypass))
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~FinalizeOnboardingDraftCommandHandlerTests"`
Expected: all tests in this file PASS, including the new one — every pre-existing test leaves `UserHasPermissionCodeAsync` unconfigured, which Moq defaults to `false`, so `bypassApproval` is `false` and behavior is unchanged for them.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Services/OnboardingDraftWriteService.cs tests/ONEVO.Tests.Unit/Features/CoreHr/OnboardingDrafts/FinalizeOnboardingDraftCommandHandlerTests.cs
git commit -m "feat: roles:manage bypasses sensitive-position approval on onboarding finalize"
```

---

### Task 3: Integration coverage against real Postgres

**Files:**
- Modify: `tests/ONEVO.Tests.Integration/CoreHr/Employee/SensitivePositionChangeApprovalIntegrationTests.cs`

**Interfaces:**
- Consumes: `SeedRoleWithPermissionAsync(Guid tenantId, string permissionCode)` and `SeedUserWithRoleAsync(Guid tenantId, Guid roleId, DateTimeOffset? expiresAt = null)` (both already exist as private helpers in this file — reused as-is, no signature changes), `BuildChangePositionHandler(Guid userId)` (existing).
- Produces: nothing new consumed elsewhere — this is a leaf test file.

- [ ] **Step 1: Write the failing test**

Add this test to `tests/ONEVO.Tests.Integration/CoreHr/Employee/SensitivePositionChangeApprovalIntegrationTests.cs`, after `Employee_CannotChangeOwnPosition_ReturnsForbidden`:

```csharp
[Fact]
public async Task ActorWithRolesManage_BypassesApproval_ActivatesImmediatelyWithApprovedAuditRow()
{
    var bypassRoleId = await SeedRoleWithPermissionAsync(_tenantId, "roles:manage");
    var bypassActorUserId = await SeedUserWithRoleAsync(_tenantId, bypassRoleId);
    var effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow);
    var handler = BuildChangePositionHandler(bypassActorUserId);

    var result = await handler.Handle(
        new ChangeEmployeePositionCommand(_targetEmployeeId, _sensitivePositionId, effectiveFrom, "Transfer"),
        CancellationToken.None);

    Assert.True(result.IsSuccess);
    Assert.False(result.Value!.PendingApproval);

    await using var db = CreateContext(_tenantId, TenantSlug);
    var oldAssignment = await db.PositionAssignments.AsNoTracking()
        .SingleAsync(a => a.Id == _targetAssignmentId);
    Assert.Equal(PositionAssignmentStatus.Ended, oldAssignment.AssignmentStatus);
    Assert.NotNull(oldAssignment.EffectiveTo);

    var newAssignment = await db.PositionAssignments.AsNoTracking()
        .SingleAsync(a => a.EmployeeId == _targetEmployeeId
                          && a.PositionId == _sensitivePositionId
                          && a.AssignmentKind == PositionAssignmentKind.PrimaryEmployment);
    Assert.Equal(PositionAssignmentStatus.Active, newAssignment.AssignmentStatus);
    Assert.Equal(effectiveFrom, newAssignment.EffectiveFrom);

    var grant = await db.AccessGrantRequests.AsNoTracking()
        .SingleAsync(g => g.EmployeeId == _targetEmployeeId && g.ActionType == AccessGrantActionType.PositionChange);
    Assert.Equal("Approved", grant.ApprovalStatus);
    Assert.Equal(bypassActorUserId, grant.RequestedByUserId);
    Assert.Equal(bypassActorUserId, grant.DecidedByUserId);
    Assert.NotNull(grant.DecidedAt);
    Assert.Equal("Self-authorized: requester holds roles:manage.", grant.DecisionNote);
    Assert.Equal(newAssignment.Id, grant.ReservedPositionAssignmentId);
}

[Fact]
public async Task ActorWithRolesManage_CannotBypassSelfTransferBlock()
{
    var bypassRoleId = await SeedRoleWithPermissionAsync(_tenantId, "roles:manage");
    var bypassActorUserId = await SeedUserWithRoleAsync(_tenantId, bypassRoleId);
    var bypassActorEmployee = NewEmployee(_tenantId, bypassActorUserId, "E-BYPASS-SELF", "BypassSelf");

    await using (var seeded = CreateContext())
    {
        seeded.Employees.Add(bypassActorEmployee);
        await seeded.SaveChangesAsync();
    }

    var handler = BuildChangePositionHandler(bypassActorUserId);
    var result = await handler.Handle(
        new ChangeEmployeePositionCommand(
            bypassActorEmployee.Id, _sensitivePositionId, DateOnly.FromDateTime(DateTime.UtcNow), "Transfer"),
        CancellationToken.None);

    Assert.False(result.IsSuccess);
    Assert.Equal(403, result.StatusCode);
    Assert.Equal("You cannot change your own position.", result.Error);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~SensitivePositionChangeApprovalIntegrationTests"`

(Requires Docker running — this test class spins up a real Postgres container via Testcontainers.)

Expected: `ActorWithRolesManage_BypassesApproval_ActivatesImmediatelyWithApprovedAuditRow` FAILS (`result.Value.PendingApproval` is `true` today). `ActorWithRolesManage_CannotBypassSelfTransferBlock` PASSES already (the self-block predates this feature) — that's fine, it's there as a regression guard for Task 1/2, not something this task changes.

- [ ] **Step 3: Confirm Tasks 1 and 2 are already applied**

No new production code in this task — it only exercises the handler changes from Task 1. If Task 1 hasn't been committed yet, do that first.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~SensitivePositionChangeApprovalIntegrationTests"`
Expected: all tests in this file PASS, including both new ones and the three pre-existing ones (`WriterRequestsSensitiveChange_ManagerApproves_EndsOldAndActivatesReserved`, `Requester_CannotApproveOwnRequest_ReturnsForbidden`, `Employee_CannotChangeOwnPosition_ReturnsForbidden`).

- [ ] **Step 5: Commit**

```bash
git add tests/ONEVO.Tests.Integration/CoreHr/Employee/SensitivePositionChangeApprovalIntegrationTests.cs
git commit -m "test: integration coverage for roles:manage sensitive-position bypass"
```
