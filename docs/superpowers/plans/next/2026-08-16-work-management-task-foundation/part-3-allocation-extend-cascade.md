# Work Management — Task Foundation, Part 3: Allocation-Extend Cascade Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the `extend_allocation` request type to the existing `objective_change_requests` table, with the conditional-approval rule from spec §4: an approver can only approve if their own Objective already has enough slack; otherwise they must first submit their own extend-allocation request up the chain.

**Architecture:** Reuses `objective_change_requests`/`ObjectiveChangeRequest` exactly as-is (Part 1 Task 1's reference reads already covered this entity/config in full) — this plan adds one enum member and new Commands/Queries, no schema migration beyond a comment-only note (the column is already `varchar(20)`, wide enough for `extend_allocation`, 17 characters).

**Tech Stack:** Same as Parts 1-2.

**Spec:** `docs/superpowers/specs/next/2026-08-16-work-management-task-foundation-design.md` §4.

## Global Constraints

- Prerequisite: Part 1 (for `IObjectiveAllocationSlackCalculator`) must be implemented first. Part 2 is not a dependency of Part 3.
- No new table, no new migration — `objective_change_requests` already has every column this needs (`PayloadJson` carries `{ requestedAdditionalHours, reason }`).
- The existing partial-unique index `ix_objective_change_requests_one_pending_per_objective` already blocks a second pending request of *any* type on the same Objective — `extend_allocation` shares that slot deliberately (spec §4), do not add a type-specific exception to that constraint.
- Root case (Objective with `ReportingManagerId == null`, i.e. the per-project Default Objective): no `objective_change_requests` row — extend is a direct `PATCH` on the Project by its `lead_id`. This plan does not touch `ProjectsController`/`EditProjectCommandHandler` beyond the one field addition in Task 4.

---

### Task 1: Add the `extend_allocation` request type + payload record

**Files:**
- Modify: `src/ONEVO.Domain/Features/WorkManagement/ObjectiveChangeRequests/Entities/ObjectiveChangeRequest.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/DTOs/ExtendAllocationRequestPayload.cs`

**Interfaces:**
- Produces: `ObjectiveChangeRequestTypes.ExtendAllocation = "extend_allocation"`, `ExtendAllocationRequestPayload(decimal RequestedAdditionalHours, string Reason)`.

- [ ] **Step 1: No new failing test for this step — it's a one-line enum addition. Instead, add the assertion to Task 2's test (below), which exercises the full flow.**

- [ ] **Step 2: Add the constant**

```csharp
// src/ONEVO.Domain/Features/WorkManagement/ObjectiveChangeRequests/Entities/ObjectiveChangeRequest.cs
public static class ObjectiveChangeRequestTypes
{
    public const string Delete = "delete";
    public const string Edit = "edit";
    public const string Transfer = "transfer";
    public const string Achieve = "achieve";
    public const string Unachieve = "unachieve";
    public const string ExtendAllocation = "extend_allocation"; // new
}
```

- [ ] **Step 3: Write the payload DTO**

```csharp
// src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/DTOs/ExtendAllocationRequestPayload.cs
namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs;

public sealed record ExtendAllocationRequestPayload(decimal RequestedAdditionalHours, string Reason);
```

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Domain/Features/WorkManagement/ObjectiveChangeRequests/Entities/ObjectiveChangeRequest.cs src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/DTOs/ExtendAllocationRequestPayload.cs
git commit -m "feat(work): add extend_allocation request type and payload"
```

### Task 2: `RequestAllocationExtension` command

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Commands/RequestAllocationExtension/{RequestAllocationExtensionCommand,RequestAllocationExtensionCommandHandler,RequestAllocationExtensionCommandValidator}.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/ObjectiveChangeRequests/RequestAllocationExtensionCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IObjectiveChangeRequestRepository` (existing, read `src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/RepositoryInterfaces/IObjectiveChangeRequestRepository.cs` before writing this task to confirm its exact method names — Part 1's research pass did not cover this file).
- Produces: `RequestAllocationExtensionCommand(Guid ObjectiveId, decimal RequestedAdditionalHours, string Reason) : IRequest<Result<ObjectiveChangeRequestResponse>>` (reuses the existing `ObjectiveChangeRequestResponse` DTO — read `src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/DTOs/Responses/` to confirm its exact name/shape before writing this task).

- [ ] **Step 1: Read `IObjectiveChangeRequestRepository.cs` and the existing `ObjectiveChangeRequestResponse` DTO in full — do not guess their shapes; every prior handler in this codebase (Part 1 Task 1's reference reads) uses `GetByIdForTenantAsync`/`AddAsync`-style names, but confirm before proceeding.**

- [ ] **Step 2: Write the failing test — the root-objective-rejection case is the interesting one to cover explicitly, since spec §4 point 3 says root Objectives never get a row here:**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.RequestAllocationExtension;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.ObjectiveChangeRequests;

public class RequestAllocationExtensionCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ReportingManagerId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private (RequestAllocationExtensionCommandHandler Handler, Mock<IObjectiveChangeRequestRepository> Requests) Build(Guid? reportingManagerId)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(EmployeeId);

        var objective = new Objective
        {
            Id = ObjectiveId, TenantId = TenantId, OwnerId = EmployeeId, ReportingManagerId = reportingManagerId,
            IsActive = true, AllocatedHours = 60m, CreatedAt = DateTimeOffset.UtcNow
        };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var requests = new Mock<IObjectiveChangeRequestRepository>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Result<Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs.Responses.ObjectiveChangeRequestResponse>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs.Responses.ObjectiveChangeRequestResponse>>> op, CancellationToken ct) => op(ct));

        var handler = new RequestAllocationExtensionCommandHandler(currentUser.Object, identity.Object, objectives.Object, requests.Object, unitOfWork.Object);
        return (handler, requests);
    }

    [Fact]
    public async Task Handle_HasReportingManager_CreatesPendingRequest()
    {
        var (handler, requests) = Build(reportingManagerId: ReportingManagerId);
        var command = new RequestAllocationExtensionCommand(ObjectiveId, 20m, "Need more hours for the new scope");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        requests.Verify(x => x.AddAsync(
            It.Is<Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities.ObjectiveChangeRequest>(
                r => r.RequestType == Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities.ObjectiveChangeRequestTypes.ExtendAllocation
                     && r.ReportingManagerId == ReportingManagerId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_RootObjectiveNoReportingManager_ReturnsBadRequest()
    {
        var (handler, requests) = Build(reportingManagerId: null);
        var command = new RequestAllocationExtensionCommand(ObjectiveId, 20m, "reason");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        requests.Verify(x => x.AddAsync(It.IsAny<Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities.ObjectiveChangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 3: Run test, verify FAIL.**

- [ ] **Step 4: Write the command, validator, handler.** Adapt exact repository/DTO method and constructor names once Step 1's read is done — the shapes below match the codebase's established naming convention (`AddAsync`, `Result<T>`-returning constructor pattern) but confirm before finalizing:

```csharp
// src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Commands/RequestAllocationExtension/RequestAllocationExtensionCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.RequestAllocationExtension;

public sealed record RequestAllocationExtensionCommand(
    Guid ObjectiveId, decimal RequestedAdditionalHours, string Reason
) : IRequest<Result<ObjectiveChangeRequestResponse>>;
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Commands/RequestAllocationExtension/RequestAllocationExtensionCommandValidator.cs
using FluentValidation;

namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.RequestAllocationExtension;

public class RequestAllocationExtensionCommandValidator : AbstractValidator<RequestAllocationExtensionCommand>
{
    public RequestAllocationExtensionCommandValidator()
    {
        RuleFor(x => x.ObjectiveId).NotEqual(Guid.Empty);
        RuleFor(x => x.RequestedAdditionalHours).GreaterThan(0).WithMessage("Requested additional hours must be positive.");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("A reason is required.");
    }
}
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Commands/RequestAllocationExtension/RequestAllocationExtensionCommandHandler.cs
using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;

namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.RequestAllocationExtension;

public class RequestAllocationExtensionCommandHandler : IRequestHandler<RequestAllocationExtensionCommand, Result<ObjectiveChangeRequestResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IObjectiveChangeRequestRepository _requests;
    private readonly IUnitOfWork _unitOfWork;

    public RequestAllocationExtensionCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IObjectiveChangeRequestRepository requests, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _requests = requests;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ObjectiveChangeRequestResponse>> Handle(RequestAllocationExtensionCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveChangeRequestResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<ObjectiveChangeRequestResponse>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<ObjectiveChangeRequestResponse>.NotFound("Objective not found.");

        if (objective.OwnerId != callerEmployeeId.Value)
            return Result<ObjectiveChangeRequestResponse>.Forbidden("Only this milestone's owner can request an allocation extension.");

        if (objective.ReportingManagerId is null)
            return Result<ObjectiveChangeRequestResponse>.Failure(
                "This milestone has no Reporting Manager to route to - it is a top-level milestone. Edit the Project directly instead.", 400);

        var payload = new ExtendAllocationRequestPayload(request.RequestedAdditionalHours, request.Reason.Trim());

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;
            var entity = new ObjectiveChangeRequest
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ObjectiveId = objective.Id,
                RequestType = ObjectiveChangeRequestTypes.ExtendAllocation,
                RequestedById = _currentUser.UserId, ReportingManagerId = objective.ReportingManagerId.Value,
                Status = ObjectiveChangeRequestStatuses.Pending, PayloadJson = JsonSerializer.Serialize(payload),
                CreatedById = _currentUser.UserId, CreatedAt = now
            };

            await _requests.AddAsync(entity, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<ObjectiveChangeRequestResponse>.Success(ObjectiveChangeRequestResponseFrom(entity));
        }, ct);
    }

    // NOTE: replace with the existing mapper once Step 1's read confirms its real name/location
    // (this codebase almost certainly already has one, e.g. an ObjectiveChangeRequestMapper - do
    // not hand-roll a second mapping if so).
    private static ObjectiveChangeRequestResponse ObjectiveChangeRequestResponseFrom(ObjectiveChangeRequest entity)
        => throw new NotImplementedException("Wire to the existing ObjectiveChangeRequestResponse mapper - see Step 1 note.");
}
```

- [ ] **Step 5: Before running tests, resolve the `NotImplementedException` placeholder above by locating and calling the codebase's real existing mapper (Step 1) — this plan cannot specify its exact call without that file read, since it wasn't covered by this plan's own research pass. This is the one legitimate "confirm against real code before finishing" gap in this plan; do not leave the `NotImplementedException` in place.**

- [ ] **Step 6: Run tests, verify PASS. Step 7: Commit.**

```bash
git add src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Commands/RequestAllocationExtension/ tests/ONEVO.Tests.Unit/Features/WorkManagement/ObjectiveChangeRequests/RequestAllocationExtensionCommandHandlerTests.cs
git commit -m "feat(work): RequestAllocationExtension command - extend_allocation request creation"
```

### Task 3: Conditional approval — `ApproveObjectiveChangeRequestCommandHandler`'s `extend_allocation` branch

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Commands/ApproveObjectiveChangeRequest/ApproveObjectiveChangeRequestCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/ObjectiveChangeRequests/ApproveObjectiveChangeRequestCommandHandlerTests.cs` (modify existing file — add new test methods, do not replace existing ones)

**Interfaces:**
- Consumes: `IObjectiveAllocationSlackCalculator` (Part 1 Task 6) — newly injected into this handler.
- Produces: the existing `Handle` method gains one new `switch` case; existing cases (`Delete`/`Edit`/`Transfer`/`Achieve`/`Unachieve`) are unchanged.

**This is the single most important task in Part 3 — it implements spec §4's core rule: "the approve action is rejected with 409 if the approver's own slack is insufficient; the original request stays pending, untouched."**

- [ ] **Step 1: Write the failing tests — both branches of the conditional**

```csharp
// add to the EXISTING tests/ONEVO.Tests.Unit/Features/WorkManagement/ObjectiveChangeRequests/ApproveObjectiveChangeRequestCommandHandlerTests.cs
// (read the existing file first to match its existing Build()-helper pattern and constructor
// parameter order exactly - it will need one new mock parameter added: IObjectiveAllocationSlackCalculator)

[Fact]
public async Task Handle_ExtendAllocation_ApproverHasEnoughSlack_IncreasesChildAllocationOnly()
{
    // Approver's own objective: allocated 100, direct children currently sum to 60 (this
    // pending-request child's CURRENT AllocatedHours, before the increase, is part of that 60)
    // plus no direct tasks -> approver's own slack = 40. Requested +20 fits within that 40.
    var childObjective = ChildObjective(allocatedHours: 60m); // the objective the request is FOR
    var approverObjective = ApproverObjective(allocatedHours: 100m); // the approver's OWN objective
    var (handler, objectives) = BuildWithSlack(
        changeRequest: ExtendAllocationRequest(childObjective.Id, requestedAdditionalHours: 20m),
        childObjective, approverObjective,
        approverSlack: 40m);

    var result = await handler.Handle(new ApproveObjectiveChangeRequestCommand(ChangeRequestId), CancellationToken.None);

    Assert.True(result.IsSuccess);
    objectives.Verify(x => x.Update(It.Is<Domain.Features.WorkManagement.Objectives.Entities.Objective>(o => o.Id == childObjective.Id && o.AllocatedHours == 80m)), Times.Once);
    // Approver's own AllocatedHours must NOT change - spec §4 point 2: "the approver's own
    // allocated_hours is unchanged (the N hours simply come out of the approver's existing slack)".
    objectives.Verify(x => x.Update(It.Is<Domain.Features.WorkManagement.Objectives.Entities.Objective>(o => o.Id == approverObjective.Id)), Times.Never);
}

[Fact]
public async Task Handle_ExtendAllocation_ApproverInsufficientSlack_ReturnsConflictAndLeavesRequestPending()
{
    // Approver's own slack = 10 (allocated 100, children sum 90, no tasks); requested +20 exceeds it.
    var childObjective = ChildObjective(allocatedHours: 60m);
    var approverObjective = ApproverObjective(allocatedHours: 100m);
    var (handler, objectives) = BuildWithSlack(
        changeRequest: ExtendAllocationRequest(childObjective.Id, requestedAdditionalHours: 20m),
        childObjective, approverObjective,
        approverSlack: 10m);

    var result = await handler.Handle(new ApproveObjectiveChangeRequestCommand(ChangeRequestId), CancellationToken.None);

    Assert.False(result.IsSuccess);
    Assert.Equal(409, result.StatusCode);
    objectives.Verify(x => x.Update(It.IsAny<Domain.Features.WorkManagement.Objectives.Entities.Objective>()), Times.Never);
    // The pending request itself must be untouched - spec §4: "the original child request stays
    // pending throughout - untouched". Confirmed by NOT calling _changeRequests.Update in this branch.
}
```

**Implementer note:** `ChildObjective`, `ApproverObjective`, `ExtendAllocationRequest`, `BuildWithSlack`, `ChangeRequestId` are new test helpers to add alongside the existing file's helpers — follow the existing file's `ParentObjective`/`BuildHandler` helper style exactly (read the file fully before adding these, per Task 1's note). `BuildWithSlack` must construct a real `ObjectiveAllocationSlackCalculator` (not a mock of the calculator itself) wired with mocked `IObjectiveRepository.GetTrackedActiveDirectChildrenAsync` returning direct children summing to `approverSlack`'s complement, exactly mirroring Part 1 Task 6's test pattern — this keeps the slack formula itself under real test coverage here too, not just mocked away.

- [ ] **Step 2: Run the new tests, verify FAIL.**

- [ ] **Step 3: Modify `ApproveObjectiveChangeRequestCommandHandler`** — inject `IObjectiveAllocationSlackCalculator`, add one `switch` case:

```csharp
// add IObjectiveAllocationSlackCalculator to the constructor + field (alongside the existing
// ICurrentUser, ICallerIdentityResolver, IObjectiveChangeRequestRepository, IObjectiveRepository,
// IMilestoneMembershipCoordinator, IUnitOfWork already there)

// inside the existing switch (changeRequest.RequestType) block, add:
case ObjectiveChangeRequestTypes.ExtendAllocation:
    var extendPayload = JsonSerializer.Deserialize<ExtendAllocationRequestPayload>(changeRequest.PayloadJson!)!;

    // "objective" here is the variable already fetched earlier in Handle() via
    // changeRequest.ObjectiveId - that is the CHILD objective the extension is FOR, not the
    // approver's own objective. Fetch the approver's own objective (the reporting manager's,
    // i.e. the caller's) separately:
    var approverObjective = await _objectives.GetByIdForTenantAsync(tenantId, callerEmployeeId.Value, innerCt);
    // ^ WRONG on purpose to flag: GetByIdForTenantAsync takes an Objective id, not an Employee
    // id. The approver's OWN objective must be looked up by "the Objective this caller (as
    // Employee) currently owns that is the PARENT of changeRequest.ObjectiveId" - i.e.
    // objective.ParentObjectiveId, fetched via _objectives.GetByIdForTenantAsync(tenantId,
    // objective.ParentObjectiveId!.Value, innerCt). Correct version:
    var approverOwnObjective = await _objectives.GetByIdForTenantAsync(tenantId, objective.ParentObjectiveId!.Value, innerCt);
    if (approverOwnObjective is null)
        return Result.Failure("Approver's own milestone could not be resolved.", 422);

    var approverSlack = await _slack.CalculateAsync(tenantId, approverOwnObjective, ct: innerCt);
    if (extendPayload.RequestedAdditionalHours > approverSlack)
        return Result.Conflict(
            "You don't have enough allocation yourself to approve this. Request more from your own reporting manager first, then return to approve this request.");

    objective.AllocatedHours += extendPayload.RequestedAdditionalHours;
    objective.UpdatedAt = now;
    _objectives.Update(objective);
    break;
```

**Critical correctness note for the implementer:** the deliberately-wrong intermediate line above (`GetByIdForTenantAsync(tenantId, callerEmployeeId.Value, ...)`) is included in this plan as a worked-through reasoning trail, not something to type — write only the corrected `approverOwnObjective` line. The key invariant this task must get right: **only the child (`changeRequest.ObjectiveId`)'s `AllocatedHours` changes on success. The approver's own objective's `AllocatedHours` is read (to compute slack) but never written** — matching spec §4 point 2 exactly and the two tests in Step 1.

- [ ] **Step 4: Update the DI registration if `ApproveObjectiveChangeRequestCommandHandler` isn't already covered by assembly-scanned MediatR registration (it should be, since it's an existing handler — this step is a no-op verification, not a new registration).**

- [ ] **Step 5: Run all `ApproveObjectiveChangeRequestCommandHandlerTests` (existing + new), verify PASS — existing `Delete`/`Edit`/`Transfer`/`Achieve`/`Unachieve` tests must still pass unmodified, confirming this change didn't regress the other five branches.**

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Commands/ApproveObjectiveChangeRequest/ tests/ONEVO.Tests.Unit/Features/WorkManagement/ObjectiveChangeRequests/ApproveObjectiveChangeRequestCommandHandlerTests.cs
git commit -m "feat(work): conditional slack-checked approval for extend_allocation requests"
```

### Task 4: Root-case direct edit + Controller wiring + Postman docs

**Files:**
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs`
- Create: `docs/postman-request/Work Management/Request Allocation Extension.md`

**Interfaces:** none new — wires Task 2's command to a route; the root case (spec §4 point 3) needs **no new endpoint at all**, since `PATCH /api/v1/work/projects/{id}` (existing `EditProjectCommandHandler`) already lets the Project's `lead_id` change `allocated_hours` directly — confirm this by reading `EditProjectCommandHandler.cs`'s existing field list; if `allocated_hours` is already editable there, this task needs zero changes to Projects for the root case. If it is not currently editable there, add it as a one-line addition to that existing command/handler/validator (not a new endpoint).

- [ ] **Step 1: Read `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/EditProject/EditProjectCommand.cs` and its handler. Confirm whether `AllocatedHours` is already a settable field on Project edit.**

- [ ] **Step 2: If not already settable, add `AllocatedHours` to `EditProjectCommand`, its validator (`GreaterThanOrEqualTo(0)`, matching `phase1-table-inventory.md`'s "non-negative" rule), its handler's field-assignment block, and the corresponding `EditProjectRequest` contract + `TasksController`/`ProjectsController` wiring — mirroring exactly how `EditObjectiveCommand` already exposes `AllocatedHours` (Part 1 Task 1's reference read of `CreateObjectiveCommand.cs` shows the same field pattern). Write a test asserting the field updates. If already settable, skip to Step 3 with a note in the commit message that this step was a no-op verification.**

- [ ] **Step 3: Add the new route to `ObjectivesController`:**

```csharp
[HttpPost("{id:guid}/allocation-requests")]
[RequirePermission("projects:access")]
public async Task<IActionResult> RequestAllocationExtension(Guid id, [FromBody] RequestAllocationExtensionRequest request, CancellationToken ct)
{
    var result = await _mediator.Send(new RequestAllocationExtensionCommand(id, request.RequestedAdditionalHours, request.Reason), ct);

    return result.IsSuccess
        ? StatusCode(202, result.Value!.ToViewModel())
        : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}
```

(Reuses the existing `ApproveChangeRequest`/`RejectChangeRequest`/`ListMyChangeRequests` routes already on `ObjectivesController` unmodified — `extend_allocation` requests flow through those same three endpoints, no new approve/reject/list routes needed, since Task 3 only added a branch to the existing approve handler.)

- [ ] **Step 4: Write `RequestAllocationExtensionRequest` contract + `.ToViewModel()` extension, following Part 1 Task 11's Contracts pattern.**

- [ ] **Step 5: Write the Postman doc for `POST .../allocation-requests`, including a worked numeric example matching spec §4's scenario, and update `docs/postman-request/README.md`.**

- [ ] **Step 6: Run the full Work Management test suite, verify PASS. Step 7: Commit.**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Projects/ src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs src/ONEVO.Api/Contracts/WorkManagement/ docs/postman-request/Work\ Management/Request\ Allocation\ Extension.md docs/postman-request/README.md tests/
git commit -m "feat(work): wire allocation-extension request route, confirm/add Project allocated-hours edit"
```

## Part 3 complete
