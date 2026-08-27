# Work Area Change Request Backend Part 1 — Final Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Correct four gaps in the already-implemented Work Area Change Request backend: (A) reviewer identity in the generic approval-inbox authority resolver is caller-supplied instead of server-derived; (B) the approval inbox resolves "which company" via a login-time default-selection heuristic instead of the session's actual active company; (C) the inbox's exact-eligibility scan is an O(N) per-candidate loop with no batching; (D)/(E) direct unit coverage for the generic resolver method and PostgreSQL partial-unique-index enforcement don't exist yet.

**Architecture:** No new tables, no new routes, no new claims. `EmployeeApprovalInboxScopeRequest` drops its caller-supplied `ReviewerUserId`; `EmployeeAuthorityResolver` derives reviewer + tenant from the `ICurrentUser` it already has injected. `WorkAreaChangeRequestWorkflow.ResolveEmployeeContextAsync` starts preferring `Session.ActiveEmployeeId` (read via the already-Application-layer `ISessionRepository`, the same mechanism `SwitchActiveCompanyCommandHandler` already uses) and only falls back to the existing `GetDefaultForUserAsync` heuristic when no session-active-employee is available. `ResolveApprovalInboxScopeAsync`'s internals are rewritten from an N+1 loop into a constant-number-of-queries batch read followed by an in-memory replay of `ResolveApproverAsync`'s exact priority walk (position coverage → department coverage → reporting line), so per-candidate results are provably identical to calling `ResolveApproverAsync` per candidate.

**Tech Stack:** .NET / C# 12, EF Core (PostgreSQL/Npgsql), MediatR, xUnit + Moq, Testcontainers.

## Global Constraints

- Work only inside `C:\onevoNew\HRMS-Backend-v1`. Never touch the frontend repo.
- Do not `git add`, `git commit`, or `git push` anything, at any point, for any reason. Every task below ends with a **test-run gate**, not a commit — this deviates from the `writing-plans` skill's normal template on purpose.
- Preserve: onsite/remote-only request targets; existing routes (no new `legalEntityId` route/query param anywhere); the position → department → reporting-line priority order; primary + N-level backup coverage; coverage owners outside the subject's reporting line remaining eligible; subject/subordinate/cross-tenant/cross-legal-entity/inactive-holder/no-permission never becoming an approver; approve/reject re-resolving the route at mutation time; `Employee.WorkModeId` never being written to; reuse of the existing `INotificationDispatcher`; tenant context always server-derived; no request body ever accepting `tenantId` or `legalEntityId`.
- Do not touch: notification `LegalEntityId`/`ReviewerDisplayName` (always-null dead fields), the `either`/`field` request-target restriction, `shift_assignment_id`, the unused `IWorkAreaChangeRequestRepository.GetByIdAsync`, the redundant `ix_work_area_change_requests_tenant_employee_date` index, the hardcoded `"Approver"` display name in `DecideAsync`, or `PositionsController.cs` (pre-existing unrelated work, confirmed by `WORK_AREA_CHANGE_REQUEST_BACKEND_PART1_REPORT.md` lines 11-16/114 — leave it exactly as found).
- `dev-server-restart.log` and `docs/superpowers/plans/2026-08-25-attendance-list-pagination.md` are pre-existing untracked files unrelated to this work — never modify or delete them.

---

### Task 1: Remove `ReviewerUserId` from the approval-inbox scope contract; derive reviewer from `ICurrentUser`

**Files:**
- Modify: `src\ONEVO.Application\Features\CoreHr\EmployeeAuthority\Models\EmployeeApprovalInboxScopeRequest.cs`
- Modify: `src\ONEVO.Application\Features\CoreHr\EmployeeAuthority\ServiceInterfaces\IEmployeeAuthorityResolver.cs`
- Modify: `src\ONEVO.Application\Features\CoreHr\EmployeeAuthority\Services\EmployeeAuthorityResolver.cs`
- Modify: `src\ONEVO.Application\Features\TimeAttendance\Commands\WorkAreaChangeRequests\WorkAreaChangeRequestWorkflow.cs`
- Modify: `tests\ONEVO.Tests.Unit\Features\CoreHr\EmployeeAuthority\EmployeeAuthorityTestGraph.cs`
- Modify: `tests\ONEVO.Tests.Unit\Features\TimeAttendance\WorkAreaChangeRequestWorkflowTests.cs`

**Interfaces:**
- Produces: `EmployeeApprovalInboxScopeRequest(Guid LegalEntityId, string RequiredPermission, EmployeeAuthorityPurpose Purpose, IReadOnlyCollection<Guid> CandidateEmployeeIds)` — 4 fields, no `ReviewerUserId`.
- Produces: `EmployeeAuthorityResolver.ResolveApprovalInboxScopeAsync` fails closed (returns `Array.Empty<Guid>()`) when unauthenticated, `UserId == Guid.Empty`, `TenantId == Guid.Empty`, the reviewer has no active employee row in `request.LegalEntityId`, the reviewer lacks `request.RequiredPermission`, or `request.CandidateEmployeeIds.Count == 0`.
- Produces: `EmployeeAuthorityTestGraph.BuildResolver(Guid? currentTenantId = null, Guid? currentUserId = null, bool isAuthenticated = true)` — new optional params so Part D tests can set a real reviewer `UserId` and toggle authentication.

- [ ] **Step 1: Write failing characterization tests for the new fail-closed guards**

  Add to `tests\ONEVO.Tests.Unit\Features\CoreHr\EmployeeAuthority\EmployeeAuthorityResolverTests.cs` (new `[Fact]` methods, appended after the existing 35):

  ```csharp
  [Fact]
  public async Task InboxScope_UnauthenticatedReviewer_FailsClosed()
  {
      var graph = new EmployeeAuthorityTestGraph();
      var legalEntityId = Guid.NewGuid();
      var candidate = graph.AddEmployee(legalEntityId).Id;
      var resolver = graph.BuildResolver(isAuthenticated: false);

      var result = await resolver.ResolveApprovalInboxScopeAsync(
          new EmployeeApprovalInboxScopeRequest(legalEntityId, "attendance:approve",
              EmployeeAuthorityPurpose.WorkAreaChangeApproval, new[] { candidate }),
          CancellationToken.None);

      result.Should().BeEmpty();
  }

  [Fact]
  public async Task InboxScope_ReviewerWithoutActiveEmployeeInLegalEntity_FailsClosed()
  {
      var graph = new EmployeeAuthorityTestGraph();
      var legalEntityId = Guid.NewGuid();
      var candidate = graph.AddEmployee(legalEntityId).Id;
      var reviewerUserId = Guid.NewGuid();
      var resolver = graph.BuildResolver(currentUserId: reviewerUserId);

      var result = await resolver.ResolveApprovalInboxScopeAsync(
          new EmployeeApprovalInboxScopeRequest(legalEntityId, "attendance:approve",
              EmployeeAuthorityPurpose.WorkAreaChangeApproval, new[] { candidate }),
          CancellationToken.None);

      result.Should().BeEmpty();
  }

  [Fact]
  public async Task InboxScope_ReviewerWithoutRequiredPermission_FailsClosed()
  {
      var graph = new EmployeeAuthorityTestGraph();
      var legalEntityId = Guid.NewGuid();
      var candidate = graph.AddEmployee(legalEntityId).Id;
      var reviewerUserId = Guid.NewGuid();
      graph.AddEmployee(legalEntityId, userId: reviewerUserId); // active employee, no permission granted
      var resolver = graph.BuildResolver(currentUserId: reviewerUserId);

      var result = await resolver.ResolveApprovalInboxScopeAsync(
          new EmployeeApprovalInboxScopeRequest(legalEntityId, "attendance:approve",
              EmployeeAuthorityPurpose.WorkAreaChangeApproval, new[] { candidate }),
          CancellationToken.None);

      result.Should().BeEmpty();
  }
  ```

- [ ] **Step 2: Run the new tests to confirm they fail**

  ```powershell
  dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --configuration Release --no-restore --filter "FullyQualifiedName~InboxScope_UnauthenticatedReviewer|FullyQualifiedName~InboxScope_ReviewerWithoutActiveEmployeeInLegalEntity|FullyQualifiedName~InboxScope_ReviewerWithoutRequiredPermission"
  ```
  Expected: compile error (`BuildResolver` overload and `EmployeeApprovalInboxScopeRequest` 4-arg constructor don't exist yet) or, once Step 3 below compiles, a failure because the old signature still takes `ReviewerUserId` as the first positional arg.

- [ ] **Step 3: Extend `EmployeeAuthorityTestGraph` fakes — `FakeCurrentUser` and `BuildResolver`**

  In `EmployeeAuthorityTestGraph.cs`, replace the `FakeCurrentUser` class (lines 207-216) and `BuildResolver` (lines 191-205):

  ```csharp
  private sealed class FakeCurrentUser : ICurrentUser
  {
      public FakeCurrentUser(Guid tenantId, Guid userId, bool isAuthenticated)
      {
          TenantId = tenantId;
          UserId = userId;
          IsAuthenticated = isAuthenticated;
      }
      public Guid UserId { get; }
      public Guid TenantId { get; }
      public string Email => "actor@example.com";
      public IReadOnlyList<string> Permissions => Array.Empty<string>();
      public bool HasPermission(string permission) => false;
      public bool IsAuthenticated { get; }
  }
  ```

  ```csharp
  public IEmployeeAuthorityResolver BuildResolver(
      Guid? currentTenantId = null, Guid? currentUserId = null, bool isAuthenticated = true)
  {
      var currentUser = new FakeCurrentUser(
          currentTenantId ?? TenantId, currentUserId ?? Guid.Empty, isAuthenticated);
      var clock = new FakeDateTimeProvider(Now);

      return new EmployeeAuthorityResolver(
          currentUser,
          clock,
          new FakeEmployeeRepository(this),
          new FakePositionAssignmentRepository(this),
          new FakePositionRepository(this),
          new FakeClosureRepository(this),
          new FakeDepartmentRepository(this),
          new FakePermissionRepository(this));
  }
  ```

  This is purely additive/widening (existing callers passing no args, or only `currentTenantId`, keep compiling and keep the old `UserId => Guid.Empty` / `IsAuthenticated => true` defaults) — none of the 35 existing `EmployeeAuthorityResolverTests` change behavior.

- [ ] **Step 4: Update `EmployeeApprovalInboxScopeRequest`**

  Replace the full file contents:

  ```csharp
  namespace ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;

  /// <summary>
  /// Describes a bounded candidate set for an approval inbox. Tenant and reviewer identity are
  /// both derived from ICurrentUser by the resolver - callers never pass either. The result
  /// contains only candidates for which the authenticated reviewer is the current exact approver,
  /// not merely a visible employee.
  /// </summary>
  public sealed record EmployeeApprovalInboxScopeRequest(
      Guid LegalEntityId,
      string RequiredPermission,
      EmployeeAuthorityPurpose Purpose,
      IReadOnlyCollection<Guid> CandidateEmployeeIds);
  ```

- [ ] **Step 5: Update `IEmployeeAuthorityResolver` XML docs**

  In `IEmployeeAuthorityResolver.cs`, add a doc comment directly above `ResolveApprovalInboxScopeAsync` (currently undocumented at the method level, only the interface-level summary mentions tenant derivation):

  ```csharp
      /// <summary>
      /// Narrows a bounded candidate set to exactly the employees for whom the authenticated
      /// reviewer (ICurrentUser.UserId) is the current exact approver - identical per-candidate
      /// results to calling ResolveApproverAsync for each candidate and keeping only those whose
      /// ApproverUserId equals the reviewer. Reviewer and tenant identity are both server-derived
      /// from ICurrentUser; there is no reviewer-identity parameter on the request. Fails closed
      /// (returns an empty collection) when the caller is unauthenticated, has no active employee
      /// record in the requested legal entity, or lacks RequiredPermission.
      /// </summary>
      Task<IReadOnlyCollection<Guid>> ResolveApprovalInboxScopeAsync(
  ```

- [ ] **Step 6: Add the fail-closed guards to `EmployeeAuthorityResolver.ResolveApprovalInboxScopeAsync`**

  Replace the method body (current lines 253-276) — keep the existing per-candidate loop for now (Task 5 batches it later; this step only adds guards and switches the eligibility comparison to `_currentUser.UserId`):

  ```csharp
      public async Task<IReadOnlyCollection<Guid>> ResolveApprovalInboxScopeAsync(
          EmployeeApprovalInboxScopeRequest request, CancellationToken cancellationToken = default)
      {
          if (!_currentUser.IsAuthenticated || _currentUser.UserId == Guid.Empty || _currentUser.TenantId == Guid.Empty)
              return Array.Empty<Guid>();
          if (request.CandidateEmployeeIds.Count == 0)
              return Array.Empty<Guid>();

          var tenantId = _currentUser.TenantId;
          var now = _clock.UtcNow;

          var reviewerEmployee = await _employeeRepository.GetByUserAndLegalEntityAsync(
              tenantId, _currentUser.UserId, request.LegalEntityId, cancellationToken);
          if (reviewerEmployee is null)
              return Array.Empty<Guid>();

          var reviewerHasPermission = await _permissionRepository.UserHasPermissionCodeAsync(
              _currentUser.UserId, request.RequiredPermission, now, cancellationToken);
          if (!reviewerHasPermission)
              return Array.Empty<Guid>();

          // The inbox passes a bounded candidate set and receives the exact same route truth used
          // by approve/reject. The workflow performs this scope resolution before count/pagination;
          // it never pages broad visibility and filters the page afterward.
          var eligible = new List<Guid>();
          foreach (var employeeId in request.CandidateEmployeeIds.Distinct())
          {
              var route = await ResolveApproverAsync(new EmployeeApprovalRouteRequest(
                  employeeId,
                  request.LegalEntityId,
                  request.RequiredPermission,
                  request.Purpose), cancellationToken);

              if (route.IsSuccess && route.Value?.ApproverUserId == _currentUser.UserId)
                  eligible.Add(employeeId);
          }

          return eligible;
      }
  ```

  `_employeeRepository.GetByUserAndLegalEntityAsync` (confirmed in `EfEmployeeRepository.cs:462-473`) already joins to `employment_statuses` and filters `status.Code == "active"`, so this single call satisfies both "reviewer is not an active employee in the requested legal entity" and gives us the reviewer's own employee row for free — no new repository method needed for this guard.

- [ ] **Step 7: Update the one production caller — `WorkAreaChangeRequestWorkflow.ListApprovalsAsync`**

  In `WorkAreaChangeRequestWorkflow.cs`, lines 219-225, remove `currentUser.UserId` as the first constructor arg:

  ```csharp
          var eligibleEmployeeIds = await authority.ResolveApprovalInboxScopeAsync(
              new EmployeeApprovalInboxScopeRequest(
                  value.LegalEntity.Id,
                  ApprovalPermission,
                  EmployeeAuthorityPurpose.WorkAreaChangeApproval,
                  candidateEmployeeIds), ct);
  ```

- [ ] **Step 8: Update the one test caller — `WorkAreaChangeRequestWorkflowTests.cs`**

  Line 156 currently asserts `r.ReviewerUserId == fixture.ApproverUserId` inside `ApprovalInbox_UsesExactApproverScopeBeforeFinalPaging`. Remove that clause (the field no longer exists) and keep the rest:

  ```csharp
          fixture.Authority.Verify(x => x.ResolveApprovalInboxScopeAsync(
              It.Is<EmployeeApprovalInboxScopeRequest>(r =>
                  r.LegalEntityId == fixture.LegalEntityId
                  && r.Purpose == EmployeeAuthorityPurpose.WorkAreaChangeApproval
                  && r.RequiredPermission == "attendance:approve"
                  && r.CandidateEmployeeIds.Count == 2),
              It.IsAny<CancellationToken>()), Times.Once);
  ```

- [ ] **Step 9: Run all tests from Steps 1-3, plus the full EmployeeAuthority and Work Area filters, and confirm green**

  ```powershell
  dotnet build src\ONEVO.Api\ONEVO.Api.csproj --configuration Release --no-restore
  dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --configuration Release --no-restore --filter "FullyQualifiedName~EmployeeAuthority|FullyQualifiedName~WorkAreaChangeRequest"
  ```
  Expected: all pass, including the 3 new guard tests and the updated `ApprovalInbox_UsesExactApproverScopeBeforeFinalPaging`.

---

### Task 2: Make the approval-inbox legal-entity context session-derived, not `GetDefaultForUserAsync`-derived

**Files:**
- Modify: `src\ONEVO.Application\Features\TimeAttendance\Commands\WorkAreaChangeRequests\WorkAreaChangeRequestWorkflow.cs`
- Modify: `tests\ONEVO.Tests.Unit\Features\TimeAttendance\WorkAreaChangeRequestWorkflowTests.cs`

**Interfaces:**
- Consumes: `ISessionRepository.GetByIdAsync(Guid sessionId, CancellationToken ct = default) : Task<Session?>` from `ONEVO.Application.Features.Auth.Login.RepositoryInterfaces` (confirmed Application-layer interface, already registered in DI as a facet of `EfAuthRepository`).
- Consumes: `ICurrentUser.SessionId : Guid?` (already exists, populated from the `session_id` claim by `CurrentUserService`).
- Consumes: `Session.TenantId`, `Session.UserId`, `Session.ActiveEmployeeId : Guid?`, `Session.IsRevoked : bool` (confirmed in `Session.cs`).
- Produces: `WorkAreaChangeRequestWorkflow` constructor gains one new parameter `ISessionRepository sessions` (inserted right after `employees`).

- [ ] **Step 1: Write the failing test proving the workflow now prefers the session's active employee over `GetDefaultForUserAsync`**

  Read `tests\ONEVO.Tests.Unit\Features\TimeAttendance\WorkAreaChangeRequestWorkflowTests.cs` in full first (needed before editing the `Fixture` class). Then add a `Sessions` mock field to `Fixture` alongside the existing `Employees`/`LegalEntities`/etc. mocks (near line 261-269), add a default happy-path setup in the constructor, and add this new test:

  ```csharp
  [Fact]
  public async Task ListApprovals_PrefersSessionActiveEmployeeOverDefaultForUser()
  {
      var fixture = new Fixture(actingAsApprover: true);
      var otherLegalEntityId = Guid.NewGuid();
      var switchedEmployee = new DomainEmployee
      {
          Id = Guid.NewGuid(), TenantId = fixture.TenantId, UserId = fixture.RequesterUserId,
          LegalEntityId = otherLegalEntityId, EmploymentStatusId = 1,
      };
      var switchedLegalEntity = fixture.LegalEntity with { Id = otherLegalEntityId };
      var sessionId = Guid.NewGuid();

      fixture.CurrentUser.Setup(x => x.SessionId).Returns(sessionId);
      fixture.Sessions.Setup(x => x.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
          .ReturnsAsync(new Session
          {
              Id = sessionId, TenantId = fixture.TenantId, UserId = fixture.RequesterUserId,
              ActiveEmployeeId = switchedEmployee.Id, IsRevoked = false, ExpiresAt = DateTimeOffset.MaxValue,
          });
      fixture.Employees.Setup(x => x.GetByIdAsync(fixture.TenantId, switchedEmployee.Id, It.IsAny<CancellationToken>()))
          .ReturnsAsync(switchedEmployee);
      fixture.LegalEntities.Setup(x => x.GetByIdForTenantAsync(fixture.TenantId, otherLegalEntityId, It.IsAny<CancellationToken>()))
          .ReturnsAsync(switchedLegalEntity);
      fixture.Requests.Setup(x => x.ListPendingEmployeeIdsAsync(
              fixture.TenantId, otherLegalEntityId, null, null, It.IsAny<CancellationToken>()))
          .ReturnsAsync(Array.Empty<Guid>());
      fixture.Authority.Setup(x => x.ResolveApprovalInboxScopeAsync(
              It.IsAny<EmployeeApprovalInboxScopeRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(Array.Empty<Guid>());
      fixture.Requests.Setup(x => x.ListApprovalInboxAsync(
              fixture.TenantId, otherLegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(),
              null, null, 0, 20, It.IsAny<CancellationToken>()))
          .ReturnsAsync((Array.Empty<WorkAreaChangeRequest>(), 0));

      var result = await fixture.Workflow.ListApprovalsAsync(
          new ListWorkAreaChangeRequestApprovalsQuery(null, null, new PagedRequest()), CancellationToken.None);

      result.IsSuccess.Should().BeTrue();
      fixture.Employees.Verify(x => x.GetDefaultForUserAsync(
          It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
  }
  ```

  (`DomainEmployee` here means `ONEVO.Domain.Features.CoreHr.Entities.Employee` — use whatever alias/full name the test file already uses for that type; check the file's `using` block before writing the literal.)

- [ ] **Step 2: Run it to confirm it fails**

  ```powershell
  dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --configuration Release --no-restore --filter "FullyQualifiedName~ListApprovals_PrefersSessionActiveEmployeeOverDefaultForUser"
  ```
  Expected: compile error (no `Sessions` field / `Session` type import yet) or, once it compiles, `GetDefaultForUserAsync` still gets called (`Times.Never` assertion fails).

- [ ] **Step 3: Add `Sessions` mock to the test `Fixture` and wire it into the constructor call**

  Add `public Mock<ISessionRepository> Sessions { get; } = new();` next to the other mock fields, add a default constructor setup so every *other* existing test (which doesn't care about sessions) keeps passing — default to "no session id" so they naturally fall through to the existing `GetDefaultForUserAsync` mock they already set up:

  ```csharp
  CurrentUser.Setup(x => x.SessionId).Returns((Guid?)null);
  ```

  Then update the `Workflow = new WorkAreaChangeRequestWorkflow(...)` call (current lines 339-342) to pass `Sessions.Object` in the new constructor position (see Step 4 for the exact new parameter order).

- [ ] **Step 4: Add `ISessionRepository` to the workflow constructor and rewrite `ResolveEmployeeContextAsync`**

  In `WorkAreaChangeRequestWorkflow.cs`, change the primary constructor (lines 19-30):

  ```csharp
  public sealed class WorkAreaChangeRequestWorkflow(
      ICurrentUser currentUser,
      IDateTimeProvider dateTime,
      CoreEmployeeRepository employees,
      ISessionRepository sessions,
      ILegalEntityRepository legalEntities,
      IWorkAreaChangeRequestRepository requests,
      IAttendanceReadRepository attendance,
      IExpectedWorkAreaResolver expectedAreas,
      IEmployeeAuthorityResolver authority,
      IPositionRepository positions,
      INotificationDispatcher notifications,
      IUnitOfWork unitOfWork)
  ```

  Add the using: `using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;`

  Replace `ResolveEmployeeContextAsync` (lines 369-383):

  ```csharp
      private async Task<Result<EmployeeContext>> ResolveEmployeeContextAsync(CancellationToken ct)
      {
          if (!currentUser.IsAuthenticated)
              return Result<EmployeeContext>.Forbidden("Authentication is required.");
          if (currentUser.TenantId == Guid.Empty)
              return Result<EmployeeContext>.Forbidden("Tenant context is missing.");

          var employee = await ResolveActiveEmployeeAsync(ct);
          if (employee?.LegalEntityId is null)
              return Result<EmployeeContext>.NotFound("Current employee record was not found.");
          var legalEntity = await legalEntities.GetByIdForTenantAsync(
              currentUser.TenantId, employee.LegalEntityId.Value, ct);
          return legalEntity is null
              ? Result<EmployeeContext>.NotFound("Company was not found.")
              : Result<EmployeeContext>.Success(new EmployeeContext(employee, legalEntity));
      }

      /// <summary>Prefers the legal entity the user actually switched to via SwitchActiveCompanyCommand
      /// (Session.ActiveEmployeeId, the same field that command writes) over GetDefaultForUserAsync's
      /// login-time "most recent primary employment" heuristic - the two can disagree for a user with
      /// employee rows in more than one legal entity. Falls back to the heuristic only when the current
      /// session has no active employee set (e.g. a session predating the ActiveEmployeeId migration) or
      /// no session id is present at all, matching pre-existing behavior for that edge case exactly.</summary>
      private async Task<ONEVO.Domain.Features.CoreHr.Entities.Employee?> ResolveActiveEmployeeAsync(CancellationToken ct)
      {
          if (currentUser.SessionId is Guid sessionId)
          {
              var session = await sessions.GetByIdAsync(sessionId, ct);
              if (session is not null
                  && !session.IsRevoked
                  && session.TenantId == currentUser.TenantId
                  && session.UserId == currentUser.UserId
                  && session.ActiveEmployeeId is Guid activeEmployeeId)
              {
                  var activeEmployee = await employees.GetByIdAsync(currentUser.TenantId, activeEmployeeId, ct);
                  if (activeEmployee is not null && activeEmployee.UserId == currentUser.UserId)
                      return activeEmployee;
              }
          }

          return await employees.GetDefaultForUserAsync(currentUser.TenantId, currentUser.UserId, ct);
      }
  ```

  This is the one shared private method behind `PreviewAsync`, `CreateAsync`, `CancelAsync`, `ListMyAsync`, and `ListApprovalsAsync` — fixing it here fixes the same defect for self-service preview/create/my/cancel too, not just the approval inbox. Document this explicitly in the Task 8 report update (the spec allows fixing a shared root cause; it only forbids *silently* expanding scope beyond the approval inbox).

- [ ] **Step 5: Run the new test plus the full Work Area filter**

  ```powershell
  dotnet build src\ONEVO.Api\ONEVO.Api.csproj --configuration Release --no-restore
  dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --configuration Release --no-restore --filter "FullyQualifiedName~WorkAreaChangeRequest"
  ```
  Expected: all pass, including `ListApprovals_PrefersSessionActiveEmployeeOverDefaultForUser`.

---

### Task 3: Add batch-read repository methods (Application interfaces)

**Files:**
- Modify: `src\ONEVO.Application\Features\CoreHr\Employee\RepositoryInterfaces\IEmployeeRepository.cs`
- Modify: `src\ONEVO.Application\Features\CoreHr\PositionAssignment\RepositoryInterfaces\IPositionAssignmentRepository.cs`
- Modify: `src\ONEVO.Application\Features\OrgStructure\Position\RepositoryInterfaces\IPositionRepository.cs`
- Modify: `src\ONEVO.Application\Features\CoreHr\EmployeeHierarchyClosure\RepositoryInterfaces\IEmployeeHierarchyClosureRepository.cs`
- Modify: `src\ONEVO.Application\Features\Auth\Permission\RepositoryInterfaces\IPermissionRepository.cs`

**Interfaces:**
- Produces (added to `IEmployeeRepository`):
  `Task<IReadOnlyDictionary<Guid, Employee>> ListByIdsAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default);`
- Produces (added to `IPositionAssignmentRepository`):
  `Task<IReadOnlyDictionary<Guid, PositionAssignment>> GetActivePrimaryByEmployeeIdsAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default);`
  `Task<IReadOnlyDictionary<Guid, IReadOnlyList<PositionActiveHolder>>> GetActiveHoldersByPositionIdsAsync(Guid tenantId, IReadOnlyCollection<Guid> positionIds, CancellationToken ct = default);`
- Produces (added to `IPositionRepository`):
  `Task<IReadOnlyDictionary<Guid, IReadOnlyList<ManagementCoverageRecord>>> ListActivePositionCoverageByCoveredPositionIdsAsync(Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> coveredPositionIds, CancellationToken ct = default);`
  `Task<IReadOnlyDictionary<Guid, IReadOnlyList<ManagementCoverageRecord>>> ListActiveDepartmentCoverageByCoveredDepartmentIdsAsync(Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> coveredDepartmentIds, CancellationToken ct = default);`
- Produces (added to `IEmployeeHierarchyClosureRepository`):
  `Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetAncestorChainsAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default);`
- Produces (added to `IPermissionRepository`):
  `Task<IReadOnlySet<Guid>> ListUserIdsHoldingPermissionAsync(IReadOnlyCollection<Guid> userIds, string permissionCode, DateTimeOffset now, CancellationToken ct = default);`
- Consumes (already exists, reused unchanged): `IPositionRepository.GetByIdsAsync(Guid tenantId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default)`.

- [ ] **Step 1: Add the interface methods**

  `IEmployeeRepository.cs` — insert after `ListActiveEmployeeIdsByIdsAsync` (after line 114):
  ```csharp
      /// <summary>Batch tenant-scoped employee lookup by id, unfiltered by employment status
      /// (mirrors GetByIdAsync's lack of an active filter, deliberately - callers that need only
      /// active rows should intersect with ListActiveEmployeeIdsByIdsAsync). Used by
      /// IEmployeeAuthorityResolver's batch approval-inbox scope resolution to preload subjects and
      /// reporting-line ancestor/holder candidates without one query per id. Ids not found are
      /// simply absent from the result.</summary>
      Task<IReadOnlyDictionary<Guid, ONEVO.Domain.Features.CoreHr.Entities.Employee>> ListByIdsAsync(
          Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default);
  ```

  `IPositionAssignmentRepository.cs` — insert after `GetActiveHoldersAsync` (after line 68):
  ```csharp
      /// <summary>Batched GetActivePrimaryAsync: current active PrimaryEmployment assignment per
      /// employee id, keyed by EmployeeId. Ids with no active primary assignment are absent from
      /// the result - relies on the same partial-unique-active-primary-per-employee database
      /// invariant GetActivePrimaryAsync itself relies on for its single-row FirstOrDefault.</summary>
      Task<IReadOnlyDictionary<Guid, ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment>> GetActivePrimaryByEmployeeIdsAsync(
          Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default);

      /// <summary>Batched GetActiveHoldersAsync: current active PrimaryEmployment holders per owner
      /// position id, keyed by PositionId. Same join shape as GetOccupancyPreviewsAsync above.
      /// Position ids with no active holders are absent from the result.</summary>
      Task<IReadOnlyDictionary<Guid, IReadOnlyList<PositionActiveHolder>>> GetActiveHoldersByPositionIdsAsync(
          Guid tenantId, IReadOnlyCollection<Guid> positionIds, CancellationToken ct = default);
  ```

  `IPositionRepository.cs` — insert after `ListActiveCoverageByCoveredTargetAsync` (after line 115):
  ```csharp
      // Batched variants of ListActiveCoverageByCoveredTargetAsync split by target type (a compound
      // type+position+department equality doesn't translate to a single IN-list predicate) - each
      // groups its results by the covered id, ordered OwnerOrder then Id within each group, same as
      // the single-id version. Used by IEmployeeAuthorityResolver's batch approval-inbox scope
      // resolution.
      Task<IReadOnlyDictionary<Guid, IReadOnlyList<ManagementCoverageRecord>>> ListActivePositionCoverageByCoveredPositionIdsAsync(
          Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> coveredPositionIds, CancellationToken ct = default);

      Task<IReadOnlyDictionary<Guid, IReadOnlyList<ManagementCoverageRecord>>> ListActiveDepartmentCoverageByCoveredDepartmentIdsAsync(
          Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> coveredDepartmentIds, CancellationToken ct = default);
  ```

  `IEmployeeHierarchyClosureRepository.cs` — insert after `GetAncestorChainEmployeeIdsAsync` (after line 30):
  ```csharp
      /// <summary>Batched GetAncestorChainEmployeeIdsAsync: for every id in employeeIds, its full
      /// upward reporting chain ordered nearest-manager-first (Depth ascending), keyed by the
      /// descendant (subject) employee id. Ids with no ancestors are absent from the result.</summary>
      Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetAncestorChainsAsync(
          Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default);
  ```

  `IPermissionRepository.cs` — insert after `UserHasPermissionCodeAsync` (after line 15):
  ```csharp
      /// <summary>Batched UserHasPermissionCodeAsync: the subset of userIds who currently hold
      /// permissionCode via an unexpired UserRole grant.</summary>
      Task<IReadOnlySet<Guid>> ListUserIdsHoldingPermissionAsync(
          IReadOnlyCollection<Guid> userIds, string permissionCode, DateTimeOffset now, CancellationToken ct = default);
  ```

- [ ] **Step 2: Build to confirm the expected compile errors (missing implementations)**

  ```powershell
  dotnet build src\ONEVO.Api\ONEVO.Api.csproj --configuration Release --no-restore
  ```
  Expected: build fails — `EfEmployeeRepository`, `EfPositionAssignmentRepository`, `EfPositionRepository`, `EfEmployeeHierarchyClosureRepository`, and `EfAuthRepository` no longer satisfy their interfaces, and `EmployeeAuthorityTestGraph`'s six `Fake*Repository` classes don't either. This confirms the interface edits took effect before implementing them (Task 4/5).

---

### Task 4: Implement the batch methods (Infrastructure/EF)

**Files:**
- Modify: `src\ONEVO.Infrastructure\Persistence\Repositories\CoreHr\EfEmployeeRepository.cs`
- Modify: `src\ONEVO.Infrastructure\Persistence\Repositories\CoreHr\EfPositionAssignmentRepository.cs`
- Modify: `src\ONEVO.Infrastructure\Persistence\Repositories\OrgStructure\Position\EfPositionRepository.cs`
- Modify: `src\ONEVO.Infrastructure\Persistence\Repositories\CoreHr\EfEmployeeHierarchyClosureRepository.cs`
- Modify: `src\ONEVO.Infrastructure\Persistence\Repositories\Auth\Login\EfAuthRepository.cs`

- [ ] **Step 1: `EfEmployeeRepository.ListByIdsAsync`** — insert after `ListActiveEmployeeIdsByIdsAsync` (after line 597):

  ```csharp
      public async Task<IReadOnlyDictionary<Guid, EmployeeEntity>> ListByIdsAsync(
          Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default)
      {
          if (employeeIds.Count == 0)
              return new Dictionary<Guid, EmployeeEntity>();

          var rows = await _db.Employees.AsNoTracking()
              .Where(e => e.TenantId == tenantId && employeeIds.Contains(e.Id))
              .ToListAsync(ct);
          return rows.ToDictionary(e => e.Id);
      }
  ```

- [ ] **Step 2: `EfPositionAssignmentRepository`** — insert after `GetActiveHoldersAsync` (after line 101):

  ```csharp
      public async Task<IReadOnlyDictionary<Guid, ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment>> GetActivePrimaryByEmployeeIdsAsync(
          Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default)
      {
          if (employeeIds.Count == 0)
              return new Dictionary<Guid, ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment>();

          var rows = await _db.PositionAssignments.AsNoTracking()
              .Where(pa => pa.TenantId == tenantId
                  && employeeIds.Contains(pa.EmployeeId)
                  && pa.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
                  && pa.AssignmentStatus == PositionAssignmentStatus.Active)
              .ToListAsync(ct);
          return rows.ToDictionary(pa => pa.EmployeeId);
      }

      public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<PositionActiveHolder>>> GetActiveHoldersByPositionIdsAsync(
          Guid tenantId, IReadOnlyCollection<Guid> positionIds, CancellationToken ct = default)
      {
          if (positionIds.Count == 0)
              return new Dictionary<Guid, IReadOnlyList<PositionActiveHolder>>();

          var rows = await (
              from pa in _db.PositionAssignments.AsNoTracking()
              join e in _db.Employees.AsNoTracking() on pa.EmployeeId equals e.Id
              where pa.TenantId == tenantId
                  && positionIds.Contains(pa.PositionId)
                  && pa.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
                  && pa.AssignmentStatus == PositionAssignmentStatus.Active
              select new { pa.PositionId, EmployeeId = e.Id, e.FirstName, e.LastName, e.Email, e.AvatarFileId })
              .ToListAsync(ct);

          return rows
              .GroupBy(row => row.PositionId)
              .ToDictionary(
                  g => g.Key,
                  g => (IReadOnlyList<PositionActiveHolder>)g
                      .Select(row => new PositionActiveHolder(row.EmployeeId, row.FirstName, row.LastName, row.Email, row.AvatarFileId))
                      .ToList());
      }
  ```

- [ ] **Step 3: `EfPositionRepository`** — insert after `ListActiveCoverageByCoveredTargetAsync` (after line 421):

  ```csharp
      public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ManagementCoverageRecord>>> ListActivePositionCoverageByCoveredPositionIdsAsync(
          Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> coveredPositionIds, CancellationToken ct = default)
      {
          if (coveredPositionIds.Count == 0)
              return new Dictionary<Guid, IReadOnlyList<ManagementCoverageRecord>>();

          var rows = await _db.ManagementCoverageRecords.AsNoTracking()
              .Where(m => m.TenantId == tenantId
                  && m.LegalEntityId == legalEntityId
                  && m.CoveredTargetType == ManagementCoverageRecord.TargetPosition
                  && m.CoveredPositionId != null && coveredPositionIds.Contains(m.CoveredPositionId!.Value)
                  && m.Status == ManagementCoverageRecord.StatusActive)
              .OrderBy(m => m.OwnerOrder).ThenBy(m => m.Id)
              .ToListAsync(ct);

          return rows.GroupBy(m => m.CoveredPositionId!.Value)
              .ToDictionary(g => g.Key, g => (IReadOnlyList<ManagementCoverageRecord>)g.ToList());
      }

      public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ManagementCoverageRecord>>> ListActiveDepartmentCoverageByCoveredDepartmentIdsAsync(
          Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> coveredDepartmentIds, CancellationToken ct = default)
      {
          if (coveredDepartmentIds.Count == 0)
              return new Dictionary<Guid, IReadOnlyList<ManagementCoverageRecord>>();

          var rows = await _db.ManagementCoverageRecords.AsNoTracking()
              .Where(m => m.TenantId == tenantId
                  && m.LegalEntityId == legalEntityId
                  && m.CoveredTargetType == ManagementCoverageRecord.TargetDepartment
                  && m.CoveredDepartmentId != null && coveredDepartmentIds.Contains(m.CoveredDepartmentId!.Value)
                  && m.Status == ManagementCoverageRecord.StatusActive)
              .OrderBy(m => m.OwnerOrder).ThenBy(m => m.Id)
              .ToListAsync(ct);

          return rows.GroupBy(m => m.CoveredDepartmentId!.Value)
              .ToDictionary(g => g.Key, g => (IReadOnlyList<ManagementCoverageRecord>)g.ToList());
      }
  ```

- [ ] **Step 4: `EfEmployeeHierarchyClosureRepository.GetAncestorChainsAsync`** — insert after `GetAncestorChainEmployeeIdsAsync` (after line 62):

  ```csharp
      public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetAncestorChainsAsync(
          Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default)
      {
          if (employeeIds.Count == 0)
              return new Dictionary<Guid, IReadOnlyList<Guid>>();

          var rows = await _db.EmployeeHierarchyClosures.AsNoTracking()
              .Where(c => c.TenantId == tenantId && employeeIds.Contains(c.DescendantEmployeeId))
              .OrderBy(c => c.Depth)
              .Select(c => new { c.DescendantEmployeeId, c.AncestorEmployeeId })
              .ToListAsync(ct);

          // GroupBy over a list already ordered by Depth preserves each group's element order
          // (LINQ-to-Objects GroupBy is stable), so nearest-manager-first survives the grouping.
          return rows.GroupBy(r => r.DescendantEmployeeId)
              .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(r => r.AncestorEmployeeId).ToList());
      }
  ```

- [ ] **Step 5: `EfAuthRepository.ListUserIdsHoldingPermissionAsync`** — insert after `UserHasPermissionCodeAsync` (after line 363). Confirm the field name used for the `ApplicationDbContext` instance in this class (the existing `UserHasPermissionCodeAsync` body uses `_db.UserRoles` — reuse that same field):

  ```csharp
      public async Task<IReadOnlySet<Guid>> ListUserIdsHoldingPermissionAsync(
          IReadOnlyCollection<Guid> userIds, string permissionCode, DateTimeOffset now, CancellationToken ct = default)
      {
          if (userIds.Count == 0)
              return new HashSet<Guid>();

          var matchingUserIds = await _db.UserRoles
              .Where(ur => userIds.Contains(ur.UserId) && (ur.ExpiresAt == null || ur.ExpiresAt > now))
              .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => new { ur.UserId, rp.PermissionId })
              .Join(_db.Permissions, x => x.PermissionId, p => p.Id, (x, p) => new { x.UserId, p.Code })
              .Where(x => x.Code == permissionCode)
              .Select(x => x.UserId)
              .Distinct()
              .ToListAsync(ct);

          return matchingUserIds.ToHashSet();
      }
  ```

  Note: `EfAuthRepository` implements most `ISessionRepository` members via **explicit interface implementation** (per research, e.g. `async Task<Session?> ISessionRepository.GetByIdAsync(...)`), but `UserHasPermissionCodeAsync`/`IPermissionRepository` members are implemented **implicitly** — add `ListUserIdsHoldingPermissionAsync` the same (implicit) way, matching its sibling `UserHasPermissionCodeAsync`.

- [ ] **Step 6: Build to confirm only the six `Fake*Repository` test doubles still fail**

  ```powershell
  dotnet build src\ONEVO.Api\ONEVO.Api.csproj --configuration Release --no-restore
  ```
  Expected: main solution projects build clean now; only `ONEVO.Tests.Unit` fails to compile (the fakes in `EmployeeAuthorityTestGraph.cs` don't implement the new interface members yet — Task 5 fixes this).

---

### Task 5: Extend `EmployeeAuthorityTestGraph`'s fakes with real (non-throwing) batch implementations

**Files:**
- Modify: `tests\ONEVO.Tests.Unit\Features\CoreHr\EmployeeAuthority\EmployeeAuthorityTestGraph.cs`

**Interfaces:**
- Consumes: the graph's existing private in-memory state (`_employees`, `_positions`, `_assignments`, `_coverage`, `_managerOf`, `_permissions`) and helpers (`AncestorsOf`).

- [ ] **Step 1: `FakeEmployeeRepository.ListByIdsAsync`** — insert after `ListActiveEmployeeIdsByIdsAsync` (after line 267), and delete the old `throw new NotImplementedException()` stub for this member if the interface previously listed it (it didn't — this is new):

  ```csharp
          public Task<IReadOnlyDictionary<Guid, DomainEmployee>> ListByIdsAsync(
              Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default)
          {
              var result = _graph._employees
                  .Where(e => e.TenantId == tenantId && employeeIds.Contains(e.Id))
                  .ToDictionary(e => e.Id);
              return Task.FromResult<IReadOnlyDictionary<Guid, DomainEmployee>>(result);
          }
  ```

- [ ] **Step 2: `FakePositionAssignmentRepository`** — insert after `GetActiveHoldersAsync` (after line 322):

  ```csharp
          public Task<IReadOnlyDictionary<Guid, DomainPositionAssignment>> GetActivePrimaryByEmployeeIdsAsync(
              Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default)
          {
              var result = _graph._assignments
                  .Where(a => a.TenantId == tenantId && employeeIds.Contains(a.EmployeeId)
                      && a.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
                      && a.AssignmentStatus == PositionAssignmentStatus.Active)
                  .ToDictionary(a => a.EmployeeId);
              return Task.FromResult<IReadOnlyDictionary<Guid, DomainPositionAssignment>>(result);
          }

          public Task<IReadOnlyDictionary<Guid, IReadOnlyList<PositionActiveHolder>>> GetActiveHoldersByPositionIdsAsync(
              Guid tenantId, IReadOnlyCollection<Guid> positionIds, CancellationToken ct = default)
          {
              var result = _graph._assignments
                  .Where(a => a.TenantId == tenantId && positionIds.Contains(a.PositionId)
                      && a.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
                      && a.AssignmentStatus == PositionAssignmentStatus.Active)
                  .Join(_graph._employees, a => a.EmployeeId, e => e.Id,
                      (a, e) => new { a.PositionId, Holder = new PositionActiveHolder(e.Id, e.FirstName, e.LastName, e.Email, e.AvatarFileId) })
                  .GroupBy(x => x.PositionId)
                  .ToDictionary(g => g.Key, g => (IReadOnlyList<PositionActiveHolder>)g.Select(x => x.Holder).ToList());
              return Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<PositionActiveHolder>>>(result);
          }
  ```

- [ ] **Step 3: `FakePositionRepository`** — insert after `ListActiveCoverageByCoveredTargetAsync` (after line 391):

  ```csharp
          public Task<IReadOnlyDictionary<Guid, IReadOnlyList<ManagementCoverageRecord>>> ListActivePositionCoverageByCoveredPositionIdsAsync(
              Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> coveredPositionIds, CancellationToken ct = default)
          {
              var result = _graph._coverage
                  .Where(c => c.TenantId == tenantId && c.LegalEntityId == legalEntityId
                      && c.CoveredTargetType == ManagementCoverageRecord.TargetPosition
                      && c.CoveredPositionId is { } pid && coveredPositionIds.Contains(pid)
                      && c.Status == ManagementCoverageRecord.StatusActive)
                  .OrderBy(c => c.OwnerOrder).ThenBy(c => c.Id)
                  .GroupBy(c => c.CoveredPositionId!.Value)
                  .ToDictionary(g => g.Key, g => (IReadOnlyList<ManagementCoverageRecord>)g.ToList());
              return Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<ManagementCoverageRecord>>>(result);
          }

          public Task<IReadOnlyDictionary<Guid, IReadOnlyList<ManagementCoverageRecord>>> ListActiveDepartmentCoverageByCoveredDepartmentIdsAsync(
              Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> coveredDepartmentIds, CancellationToken ct = default)
          {
              var result = _graph._coverage
                  .Where(c => c.TenantId == tenantId && c.LegalEntityId == legalEntityId
                      && c.CoveredTargetType == ManagementCoverageRecord.TargetDepartment
                      && c.CoveredDepartmentId is { } did && coveredDepartmentIds.Contains(did)
                      && c.Status == ManagementCoverageRecord.StatusActive)
                  .OrderBy(c => c.OwnerOrder).ThenBy(c => c.Id)
                  .GroupBy(c => c.CoveredDepartmentId!.Value)
                  .ToDictionary(g => g.Key, g => (IReadOnlyList<ManagementCoverageRecord>)g.ToList());
              return Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<ManagementCoverageRecord>>>(result);
          }
  ```

  Note: `FakePositionRepository.GetByIdsAsync` currently throws `NotImplementedException` (line 398) — the new batch resolver code calls it (Task 6). Replace that stub with a real implementation:

  ```csharp
          public Task<IReadOnlyList<DomainPosition>> GetByIdsAsync(Guid tenantId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
          {
              var result = _graph._positions.Where(p => p.TenantId == tenantId && ids.Contains(p.Id)).ToList();
              return Task.FromResult<IReadOnlyList<DomainPosition>>(result);
          }
  ```

- [ ] **Step 4: `FakeClosureRepository.GetAncestorChainsAsync`** — insert after `GetAncestorChainEmployeeIdsAsync` (after line 468):

  ```csharp
          public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetAncestorChainsAsync(
              Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default)
          {
              var result = employeeIds.ToDictionary(id => id, id => _graph.AncestorsOf(id));
              return Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>>(result);
          }
  ```

- [ ] **Step 5: `FakePermissionRepository.ListUserIdsHoldingPermissionAsync`** — insert after `UserHasPermissionCodeAsync` (after line 533):

  ```csharp
          public Task<IReadOnlySet<Guid>> ListUserIdsHoldingPermissionAsync(
              IReadOnlyCollection<Guid> userIds, string permissionCode, DateTimeOffset now, CancellationToken ct = default)
          {
              var result = userIds.Where(id => _graph._permissions.Contains((id, permissionCode))).ToHashSet();
              return Task.FromResult<IReadOnlySet<Guid>>(result);
          }
  ```

- [ ] **Step 6: Build the test project to confirm it compiles**

  ```powershell
  dotnet build tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --configuration Release --no-restore
  ```
  Expected: builds clean. All 6 `Fake*Repository` classes now fully implement their interfaces.

- [ ] **Step 7: Run the full EmployeeAuthority + Work Area unit filter to confirm no regression before touching the resolver's internals**

  ```powershell
  dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --configuration Release --no-restore --filter "FullyQualifiedName~EmployeeAuthority|FullyQualifiedName~WorkAreaChangeRequest"
  ```
  Expected: all pass (the resolver itself hasn't changed yet in this task — only its test doubles gained new capabilities).

---

### Task 6: Write Part D's direct unit tests and the equivalence test — against the CURRENT per-candidate implementation

Per the reviewer's explicit sequencing advice: write and pass these tests **before** touching `ResolveApprovalInboxScopeAsync`'s internals in Task 7, so they characterize known-correct behavior rather than encoding whatever the batch rewrite happens to produce.

**Files:**
- Modify: `tests\ONEVO.Tests.Unit\Features\CoreHr\EmployeeAuthority\EmployeeAuthorityResolverTests.cs`

**Interfaces:**
- Consumes: `EmployeeAuthorityTestGraph` builder DSL (`AddEmployee`, `AddPosition`, `AddPrimaryAssignment`, `SetManager`, `AddDepartment`, `AddCoverage`, `GrantPermission`, `BuildResolver`).

- [ ] **Step 1: Add the equivalence test** (the one test that actually protects the Task 7 refactor):

  ```csharp
  [Fact]
  public async Task InboxScope_MatchesPerCandidateResolveApproverAsync_OverRichFixture()
  {
      var graph = new EmployeeAuthorityTestGraph();
      var legalEntityId = Guid.NewGuid();
      var reviewerUserId = Guid.NewGuid();
      graph.AddEmployee(legalEntityId, userId: reviewerUserId);
      graph.GrantPermission(reviewerUserId, "attendance:approve");

      var reviewerPosition = graph.AddPosition(legalEntityId);
      var reviewerHolder = graph.AddEmployee(legalEntityId, userId: reviewerUserId);
      graph.AddPrimaryAssignment(reviewerHolder.Id, reviewerPosition.Id);

      var otherUserId = Guid.NewGuid();
      var otherPosition = graph.AddPosition(legalEntityId);
      var otherHolder = graph.AddEmployee(legalEntityId, userId: otherUserId);
      graph.AddPrimaryAssignment(otherHolder.Id, otherPosition.Id);
      graph.GrantPermission(otherUserId, "attendance:approve");

      var department = graph.AddDepartment();

      var candidateIds = new List<Guid>();
      for (var i = 0; i < 12; i++)
      {
          var candidatePosition = graph.AddPosition(legalEntityId);
          var candidate = graph.AddEmployee(legalEntityId, departmentId: i % 3 == 0 ? department : null);
          graph.AddPrimaryAssignment(candidate.Id, candidatePosition.Id);
          candidateIds.Add(candidate.Id);

          switch (i % 4)
          {
              case 0: // position coverage -> reviewer
                  graph.AddCoverage(legalEntityId, reviewerPosition.Id, ManagementCoverageRecord.TargetPosition, candidatePosition.Id, null, ownerOrder: 1);
                  break;
              case 1: // position coverage -> someone else
                  graph.AddCoverage(legalEntityId, otherPosition.Id, ManagementCoverageRecord.TargetPosition, candidatePosition.Id, null, ownerOrder: 1);
                  break;
              case 2: // department coverage -> reviewer
                  graph.AddCoverage(legalEntityId, reviewerPosition.Id, ManagementCoverageRecord.TargetDepartment, null, department, ownerOrder: 1);
                  break;
              case 3: // no coverage, no manager -> unroutable
                  break;
          }
      }

      var resolver = graph.BuildResolver(currentUserId: reviewerUserId);

      var expected = new List<Guid>();
      foreach (var candidateId in candidateIds)
      {
          var route = await resolver.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
              candidateId, legalEntityId, "attendance:approve", EmployeeAuthorityPurpose.WorkAreaChangeApproval),
              CancellationToken.None);
          if (route.IsSuccess && route.Value?.ApproverUserId == reviewerUserId)
              expected.Add(candidateId);
      }

      var actual = await resolver.ResolveApprovalInboxScopeAsync(
          new EmployeeApprovalInboxScopeRequest(legalEntityId, "attendance:approve",
              EmployeeAuthorityPurpose.WorkAreaChangeApproval, candidateIds),
          CancellationToken.None);

      actual.Should().BeEquivalentTo(expected);
  }
  ```

- [ ] **Step 2: Add the 23 Part D scenario tests.** Fixture setups follow the same `EmployeeAuthorityTestGraph` DSL already used by the 35 existing `ResolveApproverAsync`/`ResolveVisibilityAsync` tests in this file — mirror their exact patterns (e.g. `Approval_FallsBackToBackup1_WhenPrimaryLacksPermission`, `Approval_SupportsArbitraryNumberOfBackupLevels`, `Approval_NeverSelectsSubordinate`, `Visibility_ExcludesInactiveEmployees`) rather than inventing new fixture idioms. Write these as real `[Fact]`/`[Theory]` methods, one per scenario below (scenario numbers match Part D of the task spec):

  | # | Test name | Fixture delta from the equivalence-test pattern above | Assertion |
  |---|---|---|---|
  | 1 | `InboxScope_EmptyCandidateSet_ReturnsEmpty` | `CandidateEmployeeIds: Array.Empty<Guid>()` | `result.Should().BeEmpty()` |
  | 2 | (already written in Task 1 Step 1) `InboxScope_UnauthenticatedReviewer_FailsClosed` | — | — |
  | 3 | `InboxScope_ReviewerIdentityComesFromCurrentUser_NotRequest` | Build resolver with `currentUserId: reviewerUserId`; grant only `reviewerUserId` the winning route. Assert eligible set is non-empty — proves identity came from `ICurrentUser`, since the (now-removed) request no longer carries any reviewer field at all. | `result.Should().Contain(candidateId)` |
  | 4 | `InboxScope_PositionCoveragePrimaryOwner_IsIncluded` | One candidate, position coverage owner order 1 = reviewer's position, reviewer holds it, has permission. | `result.Should().ContainSingle().Which.Should().Be(candidateId)` |
  | 5 | `InboxScope_PositionCoverageBackupOwner_IsIncludedWhenEarlierLevelsUnavailable` | Owner order 1 = a position with no active holder; owner order 2 = reviewer's position. | same pattern |
  | 6 | `InboxScope_DepartmentCoverageOwner_IsIncluded` | Candidate has `DepartmentId`; department coverage owner = reviewer's position. | same pattern |
  | 7 | `InboxScope_ManualCoverageOwnerOutsideReportingLine_IsEligible` | Coverage `Source` irrelevant to resolver logic (not read) — set reviewer's position as owner via `AddCoverage` with the candidate NOT in reviewer's `SetManager` chain at all. | same pattern |
  | 8 | `InboxScope_UpwardReportingLineApprover_IsIncluded` | No coverage rows at all; `SetManager(candidate.Id, reviewerHolder.Id)`; reviewer has permission and an active primary assignment. | same pattern |
  | 9 | `InboxScope_DifferentExactApprover_IsExcluded` | Position coverage resolves to `otherUserId`, not the reviewer. | `result.Should().BeEmpty()` |
  | 10 | `InboxScope_MerelyVisibleCandidate_IsExcluded` | Candidate visible via `ResolveVisibilityAsync` (company-wide coverage to reviewer's position) but has no `ResolveApproverAsync` route to the reviewer (e.g. a different, unrelated position covers them for approval purposes). Assert `result` excludes them even though `ResolveVisibilityAsync` would include them — call both to prove the difference. | `result.Should().NotContain(candidateId)` |
  | 11 | `InboxScope_SubjectItself_IsExcluded` | `CandidateEmployeeIds` includes the reviewer's own employee id, with reviewer's own position self-covering. | `result.Should().NotContain(reviewerHolder.Id)` |
  | 12 | `InboxScope_ReviewerWhoIsSubordinateOfSubject_IsExcluded` | `SetManager(reviewerHolder.Id, candidate.Id)` (reviewer reports to candidate) plus position coverage that would otherwise resolve to the reviewer. | `result.Should().BeEmpty()` |
  | 13 | `InboxScope_CrossTenantCandidate_IsExcluded` | `AddEmployee(legalEntityId, tenantIdOverride: Guid.NewGuid())`. | `result.Should().BeEmpty()` |
  | 14 | `InboxScope_CrossLegalEntityCandidate_IsExcluded` | Candidate employee's `LegalEntityId` differs from `request.LegalEntityId`. | `result.Should().BeEmpty()` |
  | 15 | `InboxScope_InactiveCandidate_IsExcluded` | `AddEmployee(legalEntityId, active: false)`, no primary assignment, no department, no manager — so all three tiers naturally find no route (see design note below). | `result.Should().BeEmpty()` |
  | 16 | `InboxScope_InactiveReviewer_ReceivesNoCandidates` | Reviewer's own employee row `active: false` in that legal entity — `GetByUserAndLegalEntityAsync` (active-filtered) returns null, guard fires. | `result.Should().BeEmpty()` |
  | 17 | `InboxScope_ReviewerWithoutPermission_ReceivesNoCandidates` | (already written in Task 1 Step 1) `InboxScope_ReviewerWithoutRequiredPermission_FailsClosed` | — |
  | 18 | `InboxScope_DuplicateCandidateIds_DoNotProduceDuplicateResults` | `CandidateEmployeeIds: new[] { candidateId, candidateId, candidateId }`. | `result.Should().ContainSingle()` |
  | 19 | `InboxScope_PositionCoverage_WinsOverDepartmentCoverage` | Candidate has both a covered position (owner = reviewer) and a covered department (owner = otherUser) that would otherwise also resolve. | `result.Should().Contain(candidateId)` (reviewer wins) |
  | 20 | `InboxScope_DepartmentCoverage_WinsOverReportingLineFallback` | Candidate has department coverage (owner = reviewer) and also reports (via manager chain) to `otherUserId` who also has permission. | `result.Should().Contain(candidateId)` |
  | 21 | `InboxScope_PooledOwnerPositionWithoutResponsibleEmployeeId_PicksNoArbitraryHolder` | Owner position has 2 active holders, `ResponsibleEmployeeId: null` on the coverage row. | `result.Should().BeEmpty()` (falls through to next tier/no route) |
  | 22 | `InboxScope_PooledOwnerPositionWithValidResponsibleEmployeeId_ResolvesCorrectly` | Same as above but `responsibleEmployeeId: reviewerHolder.Id`. | `result.Should().Contain(candidateId)` |
  | 23 | `InboxScope_SupportsArbitraryBackupLevels_NoHardcodedMaximum` | `[Theory]` with `[InlineData(5)]`, `[InlineData(12)]` — owner orders 1..N-1 all unresolvable (no holder), order N = reviewer's position. | `result.Should().Contain(candidateId)` |

  Design note for scenario 15 (write this as an actual code comment in the test, not just in the plan): `ResolveApproverAsync` does not filter the *subject's* own active status directly — only the resolved *holder's* — so an inactive subject is "excluded" here as a natural consequence of having no active primary assignment, no department, and no manager edge (the realistic state of an offboarded employee, whose `EmployeeHierarchyClosure` rows and `PositionAssignment` are also cleared), not via an explicit new filter. Do not add a new subject-active check to the resolver to make this scenario "cleaner" — that would break the Task 6 Step 1 equivalence test and is out of this correction's scope (`ResolveApproverAsync` itself is a **preserve**, not a **fix**, target).

- [ ] **Step 3: Run all Task 6 tests and confirm every one passes against the CURRENT (Task 1-era, still-N+1) implementation**

  ```powershell
  dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --configuration Release --no-restore --filter "FullyQualifiedName~InboxScope"
  ```
  Expected: all pass (24 tests: the equivalence test + 23 scenario tests, 2 of which were already written in Task 1). This is the checkpoint the reviewer's sequencing advice exists to protect — do not proceed to Task 7 until every one is green here, against the unbatched implementation.

---

### Task 7: Rewrite `ResolveApprovalInboxScopeAsync` as a batch operation

**Files:**
- Modify: `src\ONEVO.Application\Features\CoreHr\EmployeeAuthority\Services\EmployeeAuthorityResolver.cs`

**Interfaces:**
- Consumes: all six new batch repository methods from Task 3/4.
- Produces: `ResolveApprovalInboxScopeAsync`'s observable behavior is byte-identical to Task 6's baseline (verified by re-running the exact same Task 6 test suite with zero changes to the tests).

- [ ] **Step 1: Replace the method body** (the guards from Task 1 Step 6 stay; only the block after the permission check changes). Also add a new private static helper `TryResolveFromCoverageInMemory`:

  ```csharp
      public async Task<IReadOnlyCollection<Guid>> ResolveApprovalInboxScopeAsync(
          EmployeeApprovalInboxScopeRequest request, CancellationToken cancellationToken = default)
      {
          if (!_currentUser.IsAuthenticated || _currentUser.UserId == Guid.Empty || _currentUser.TenantId == Guid.Empty)
              return Array.Empty<Guid>();
          if (request.CandidateEmployeeIds.Count == 0)
              return Array.Empty<Guid>();

          var tenantId = _currentUser.TenantId;
          var now = _clock.UtcNow;

          var reviewerEmployee = await _employeeRepository.GetByUserAndLegalEntityAsync(
              tenantId, _currentUser.UserId, request.LegalEntityId, cancellationToken);
          if (reviewerEmployee is null)
              return Array.Empty<Guid>();

          var reviewerHasPermission = await _permissionRepository.UserHasPermissionCodeAsync(
              _currentUser.UserId, request.RequiredPermission, now, cancellationToken);
          if (!reviewerHasPermission)
              return Array.Empty<Guid>();

          var candidateIds = request.CandidateEmployeeIds.Distinct().ToList();

          // Phase 1: subjects + their active primary assignments, one batch each.
          var subjectsById = await _employeeRepository.ListByIdsAsync(tenantId, candidateIds, cancellationToken);
          var subjects = candidateIds
              .Select(id => subjectsById.TryGetValue(id, out var e) ? e : null)
              .Where(e => e is not null && e!.LegalEntityId == request.LegalEntityId)
              .Cast<ONEVO.Domain.Features.CoreHr.Entities.Employee>()
              .ToList();
          if (subjects.Count == 0)
              return Array.Empty<Guid>();

          var subjectPrimaryByEmployeeId = await _positionAssignmentRepository.GetActivePrimaryByEmployeeIdsAsync(
              tenantId, subjects.Select(s => s.Id).ToList(), cancellationToken);

          // Phase 2: coverage rows for every covered position/department the subjects touch.
          var subjectPositionIds = subjectPrimaryByEmployeeId.Values.Select(pa => pa.PositionId).Distinct().ToList();
          var subjectDepartmentIds = subjects.Where(s => s.DepartmentId is not null)
              .Select(s => s.DepartmentId!.Value).Distinct().ToList();

          var positionCoverageByCoveredPositionId = await _positionRepository.ListActivePositionCoverageByCoveredPositionIdsAsync(
              tenantId, request.LegalEntityId, subjectPositionIds, cancellationToken);
          var departmentCoverageByCoveredDepartmentId = await _positionRepository.ListActiveDepartmentCoverageByCoveredDepartmentIdsAsync(
              tenantId, request.LegalEntityId, subjectDepartmentIds, cancellationToken);

          // Phase 3: owner positions, their active holders, and ancestor chains for every subject
          // plus every holder discovered - a holder's ancestor chain tells us whether they are a
          // subordinate of a given subject (holder in descendants(subject) iff subject in
          // ancestors(holder)), avoiding a separate descendant-expansion query entirely.
          var ownerPositionIds = positionCoverageByCoveredPositionId.Values.SelectMany(rows => rows)
              .Concat(departmentCoverageByCoveredDepartmentId.Values.SelectMany(rows => rows))
              .Select(r => r.OwnerPositionId).Distinct().ToList();

          var ownerPositionsById = (await _positionRepository.GetByIdsAsync(tenantId, ownerPositionIds, cancellationToken))
              .ToDictionary(p => p.Id);
          var holdersByOwnerPositionId = await _positionAssignmentRepository.GetActiveHoldersByPositionIdsAsync(
              tenantId, ownerPositionIds, cancellationToken);

          var allHolderEmployeeIds = holdersByOwnerPositionId.Values.SelectMany(h => h)
              .Select(h => h.EmployeeId).Distinct().ToList();
          var ancestorLookupIds = subjects.Select(s => s.Id).Concat(allHolderEmployeeIds).Distinct().ToList();
          var ancestorChainsByEmployeeId = await _closureRepository.GetAncestorChainsAsync(
              tenantId, ancestorLookupIds, cancellationToken);

          // Phase 4: every employee a holder or a reporting-line ancestor could resolve to, plus
          // their active-primary-assignment status (needed for the reporting-line tier's "ancestor
          // must have an active primary assignment" gate) and permission grants - all batched over
          // the full id union discovered so far, one call each, regardless of candidate count.
          var allAncestorIds = ancestorChainsByEmployeeId.Values.SelectMany(chain => chain).Distinct().ToList();
          var resolvableEmployeeIds = allHolderEmployeeIds.Concat(allAncestorIds).Distinct().ToList();

          var activeHolderIdsInLegalEntity = (await _employeeRepository.ListActiveEmployeeIdsByIdsAsync(
              tenantId, request.LegalEntityId, allHolderEmployeeIds, cancellationToken)).ToHashSet();
          var resolvableEmployeesById = await _employeeRepository.ListByIdsAsync(
              tenantId, resolvableEmployeeIds, cancellationToken);
          var ancestorPrimaryByEmployeeId = await _positionAssignmentRepository.GetActivePrimaryByEmployeeIdsAsync(
              tenantId, allAncestorIds, cancellationToken);

          var allPermissionUserIds = resolvableEmployeesById.Values.Select(e => e.UserId).Distinct().ToList();
          var userIdsWithPermission = await _permissionRepository.ListUserIdsHoldingPermissionAsync(
              allPermissionUserIds, request.RequiredPermission, now, cancellationToken);

          // Phase 5: replay ResolveApproverAsync's exact priority walk in memory, per subject - no
          // further database calls from here on.
          var eligible = new List<Guid>();
          foreach (var subject in subjects)
          {
              Guid? approverUserId = null;

              if (subjectPrimaryByEmployeeId.TryGetValue(subject.Id, out var primary)
                  && positionCoverageByCoveredPositionId.TryGetValue(primary.PositionId, out var positionCoverage))
              {
                  approverUserId = TryResolveFromCoverageInMemory(
                      positionCoverage, subject.Id, ownerPositionsById, holdersByOwnerPositionId,
                      ancestorChainsByEmployeeId, activeHolderIdsInLegalEntity, resolvableEmployeesById,
                      userIdsWithPermission);
              }

              if (approverUserId is null && subject.DepartmentId is { } departmentId
                  && departmentCoverageByCoveredDepartmentId.TryGetValue(departmentId, out var departmentCoverage))
              {
                  approverUserId = TryResolveFromCoverageInMemory(
                      departmentCoverage, subject.Id, ownerPositionsById, holdersByOwnerPositionId,
                      ancestorChainsByEmployeeId, activeHolderIdsInLegalEntity, resolvableEmployeesById,
                      userIdsWithPermission);
              }

              if (approverUserId is null && ancestorChainsByEmployeeId.TryGetValue(subject.Id, out var ownAncestors))
              {
                  foreach (var ancestorEmployeeId in ownAncestors)
                  {
                      if (!resolvableEmployeesById.TryGetValue(ancestorEmployeeId, out var ancestorEmployee)
                          || ancestorEmployee.LegalEntityId != request.LegalEntityId
                          || !userIdsWithPermission.Contains(ancestorEmployee.UserId)
                          || !ancestorPrimaryByEmployeeId.ContainsKey(ancestorEmployeeId))
                          continue;

                      approverUserId = ancestorEmployee.UserId;
                      break;
                  }
              }

              if (approverUserId == _currentUser.UserId)
                  eligible.Add(subject.Id);
          }

          return eligible;
      }

      private static Guid? TryResolveFromCoverageInMemory(
          IReadOnlyList<ManagementCoverageRecord> records,
          Guid subjectEmployeeId,
          IReadOnlyDictionary<Guid, ONEVO.Domain.Features.OrgStructure.Entities.Position> ownerPositionsById,
          IReadOnlyDictionary<Guid, IReadOnlyList<PositionActiveHolder>> holdersByOwnerPositionId,
          IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> ancestorChainsByEmployeeId,
          IReadOnlyCollection<Guid> activeHolderIdsInLegalEntity,
          IReadOnlyDictionary<Guid, ONEVO.Domain.Features.CoreHr.Entities.Employee> resolvableEmployeesById,
          IReadOnlyCollection<Guid> userIdsWithPermission)
      {
          foreach (var record in records)
          {
              if (!ownerPositionsById.TryGetValue(record.OwnerPositionId, out var ownerPosition) || !ownerPosition.IsActive)
                  continue;
              if (!holdersByOwnerPositionId.TryGetValue(record.OwnerPositionId, out var holders))
                  continue;

              var resolvedHolder = holders.Count switch
              {
                  0 => null,
                  1 => holders[0],
                  _ => record.ResponsibleEmployeeId is { } chosenId
                      ? holders.FirstOrDefault(h => h.EmployeeId == chosenId)
                      : null,
              };
              if (resolvedHolder is null)
                  continue;
              if (resolvedHolder.EmployeeId == subjectEmployeeId)
                  continue;
              // holder in descendants(subject) iff subject in ancestors(holder).
              if (ancestorChainsByEmployeeId.TryGetValue(resolvedHolder.EmployeeId, out var holderAncestors)
                  && holderAncestors.Contains(subjectEmployeeId))
                  continue;
              if (!activeHolderIdsInLegalEntity.Contains(resolvedHolder.EmployeeId))
                  continue;
              if (!resolvableEmployeesById.TryGetValue(resolvedHolder.EmployeeId, out var candidateEmployee))
                  continue;
              if (!userIdsWithPermission.Contains(candidateEmployee.UserId))
                  continue;

              return candidateEmployee.UserId;
          }
          return null;
      }
  ```

  `PositionActiveHolder` here is a record (existing type) — the `holders.Count switch` returning `null`/`holders[0]`/`FirstOrDefault(...)` compiles as-is since it's a reference type.

- [ ] **Step 2: Run the full Task 6 test suite unchanged and confirm every test still passes**

  ```powershell
  dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --configuration Release --no-restore --filter "FullyQualifiedName~InboxScope"
  ```
  Expected: all 24 pass, with zero test-code changes from Task 6 — this is the proof the batch rewrite is behaviorally identical.

- [ ] **Step 3: Add the performance/call-count regression tests** (50 and 100 candidates, asserting constant — not just "small" — repository call counts):

  ```csharp
  [Theory]
  [InlineData(50)]
  [InlineData(100)]
  public async Task InboxScope_RepositoryCallCountIsConstant_RegardlessOfCandidateCount(int candidateCount)
  {
      var graph = new EmployeeAuthorityTestGraph();
      var legalEntityId = Guid.NewGuid();
      var reviewerUserId = Guid.NewGuid();
      var reviewerPosition = graph.AddPosition(legalEntityId);
      var reviewerHolder = graph.AddEmployee(legalEntityId, userId: reviewerUserId);
      graph.AddPrimaryAssignment(reviewerHolder.Id, reviewerPosition.Id);
      graph.GrantPermission(reviewerUserId, "attendance:approve");

      var candidateIds = new List<Guid>();
      for (var i = 0; i < candidateCount; i++)
      {
          var candidatePosition = graph.AddPosition(legalEntityId);
          var candidate = graph.AddEmployee(legalEntityId);
          graph.AddPrimaryAssignment(candidate.Id, candidatePosition.Id);
          graph.AddCoverage(legalEntityId, reviewerPosition.Id, ManagementCoverageRecord.TargetPosition, candidatePosition.Id, null, ownerOrder: 1);
          candidateIds.Add(candidate.Id);
      }

      var countingGraph = new CallCountingGraphWrapper(graph);
      var resolver = countingGraph.BuildResolver(currentUserId: reviewerUserId);

      var result = await resolver.ResolveApprovalInboxScopeAsync(
          new EmployeeApprovalInboxScopeRequest(legalEntityId, "attendance:approve",
              EmployeeAuthorityPurpose.WorkAreaChangeApproval, candidateIds),
          CancellationToken.None);

      result.Should().HaveCount(candidateCount);
      countingGraph.CallCounts.Values.Should().OnlyContain(count => count <= 2,
          "batch reads must be O(1) per repository method, not proportional to candidate count");
  }
  ```

  This test needs a thin call-counting decorator. Add a small internal helper class in the same test file (not a separate file, to keep the diff local):

  ```csharp
  file sealed class CallCountingGraphWrapper
  {
      private readonly EmployeeAuthorityTestGraph _inner;
      public Dictionary<string, int> CallCounts { get; } = new();
      public CallCountingGraphWrapper(EmployeeAuthorityTestGraph inner) => _inner = inner;

      public IEmployeeAuthorityResolver BuildResolver(Guid? currentUserId = null)
      {
          // Wraps only the six batch methods this task added - every wrapped call increments
          // CallCounts[methodName], proving the resolver issues at most a small constant number
          // of calls to each, independent of candidate count. Delegates everything else straight
          // through to the real fakes built by _inner.BuildResolver(...).
          return new CountingEmployeeAuthorityResolver(_inner.BuildResolver(currentUserId: currentUserId), CallCounts);
      }
  }
  ```

  Given `EmployeeAuthorityResolver`'s repository fields are `private readonly` with no seam for a decorator at the repository level from outside the class, the simplest faithful implementation of this counting wrapper is to **not** wrap `IEmployeeAuthorityResolver` itself, but instead add optional internal call counters directly to the six `Fake*Repository` classes from Task 5 (a `public int CallCount` field incremented at the top of each new batch method, reset per test), and assert on those directly instead of via a wrapper class. Replace the `CallCountingGraphWrapper`/`CountingEmployeeAuthorityResolver` sketch above with that simpler approach: add `public int ListByIdsCallCount { get; private set; }` (and one counter per new batch method) to each relevant `Fake*Repository`, increment in the method body, and have the test read `graph`-exposed counters via small public passthrough properties on `EmployeeAuthorityTestGraph` (e.g. `EmployeeRepositoryListByIdsCallCount`) populated after `BuildResolver` is called once and reused. Implement whichever of these two shapes compiles cleanest given the sealed/private structure already in the file — the assertion target is unchanged: each new batch method is called a small constant number of times (≤ 2, since Phase 4's primary-assignment batch is genuinely called twice — once for subjects in Phase 1, once for ancestors in Phase 4), not once per candidate.

- [ ] **Step 4: Run the performance tests and the full suite one more time**

  ```powershell
  dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --configuration Release --no-restore --filter "FullyQualifiedName~InboxScope"
  dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --configuration Release --no-restore --filter "FullyQualifiedName~EmployeeAuthority|FullyQualifiedName~WorkAreaChangeRequest"
  ```
  Expected: all pass.

---

### Task 8: PostgreSQL integration tests — partial unique index + RLS

**Files:**
- Modify: `tests\ONEVO.Tests.Integration\Features\TimeAttendance\WorkAreaChangeRequestsIntegrationTests.cs`

**Interfaces:**
- Consumes: the existing Testcontainers Postgres fixture already used by this file (read the file first to identify its exact base class / fixture setup and the restricted-role connection pattern already used for the existing RLS tests — reuse it verbatim, do not invent a new one).

- [ ] **Step 1: Read the existing file in full** to find: (a) the fixture/base-class name and connection-string pattern, (b) how `app.current_tenant_id` is set per-connection for the restricted role, (c) the exact migrated column/index names already asserted, so the new tests match the file's established style exactly.

- [ ] **Step 2: Add the 9 partial-unique-index scenario tests**, driving inserts directly against the database (not through the repository, to prove Postgres itself enforces this — per the migration, index `ux_work_area_change_requests_active_employee_date` on `(tenant_id, employee_id, date)` filtered `WHERE status IN ('pending','approved')`):

  ```csharp
  [Fact]
  public async Task ActiveUniqueIndex_FirstPendingRequest_Succeeds()
  {
      var (tenantId, employeeId, legalEntityId) = await SeedTenantEmployeeAsync();
      await InsertWorkAreaChangeRequestAsync(tenantId, employeeId, legalEntityId, DateOnly.FromDateTime(DateTime.UtcNow), "pending");
      // no exception = success; assert the row exists
      var count = await CountRowsAsync(tenantId, employeeId);
      count.Should().Be(1);
  }

  [Fact]
  public async Task ActiveUniqueIndex_SecondPendingSameEmployeeDate_ThrowsUniqueViolation()
  {
      var (tenantId, employeeId, legalEntityId) = await SeedTenantEmployeeAsync();
      var date = DateOnly.FromDateTime(DateTime.UtcNow);
      await InsertWorkAreaChangeRequestAsync(tenantId, employeeId, legalEntityId, date, "pending");

      var act = () => InsertWorkAreaChangeRequestAsync(tenantId, employeeId, legalEntityId, date, "pending");

      var ex = await act.Should().ThrowAsync<PostgresException>();
      ex.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
      ex.Which.ConstraintName.Should().Be("ux_work_area_change_requests_active_employee_date");
  }

  [Fact]
  public async Task ActiveUniqueIndex_ApprovedThenPendingSameEmployeeDate_ThrowsUniqueViolation()
  {
      var (tenantId, employeeId, legalEntityId) = await SeedTenantEmployeeAsync();
      var date = DateOnly.FromDateTime(DateTime.UtcNow);
      await InsertWorkAreaChangeRequestAsync(tenantId, employeeId, legalEntityId, date, "approved");

      var act = () => InsertWorkAreaChangeRequestAsync(tenantId, employeeId, legalEntityId, date, "pending");

      var ex = await act.Should().ThrowAsync<PostgresException>();
      ex.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
  }

  [Fact]
  public async Task ActiveUniqueIndex_PendingThenApprovedSameEmployeeDate_ThrowsUniqueViolation()
  {
      var (tenantId, employeeId, legalEntityId) = await SeedTenantEmployeeAsync();
      var date = DateOnly.FromDateTime(DateTime.UtcNow);
      await InsertWorkAreaChangeRequestAsync(tenantId, employeeId, legalEntityId, date, "pending");

      var act = () => InsertWorkAreaChangeRequestAsync(tenantId, employeeId, legalEntityId, date, "approved");

      var ex = await act.Should().ThrowAsync<PostgresException>();
      ex.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
  }

  [Fact]
  public async Task ActiveUniqueIndex_RejectedThenNewPendingSameEmployeeDate_Succeeds()
  {
      var (tenantId, employeeId, legalEntityId) = await SeedTenantEmployeeAsync();
      var date = DateOnly.FromDateTime(DateTime.UtcNow);
      await InsertWorkAreaChangeRequestAsync(tenantId, employeeId, legalEntityId, date, "rejected");

      await InsertWorkAreaChangeRequestAsync(tenantId, employeeId, legalEntityId, date, "pending");

      var count = await CountRowsAsync(tenantId, employeeId);
      count.Should().Be(2);
  }

  [Fact]
  public async Task ActiveUniqueIndex_CancelledThenNewPendingSameEmployeeDate_Succeeds()
  {
      var (tenantId, employeeId, legalEntityId) = await SeedTenantEmployeeAsync();
      var date = DateOnly.FromDateTime(DateTime.UtcNow);
      await InsertWorkAreaChangeRequestAsync(tenantId, employeeId, legalEntityId, date, "cancelled");

      await InsertWorkAreaChangeRequestAsync(tenantId, employeeId, legalEntityId, date, "pending");

      var count = await CountRowsAsync(tenantId, employeeId);
      count.Should().Be(2);
  }

  [Fact]
  public async Task ActiveUniqueIndex_SameDateDifferentEmployee_Succeeds()
  {
      var (tenantId, employeeId1, legalEntityId) = await SeedTenantEmployeeAsync();
      var employeeId2 = await SeedAdditionalEmployeeAsync(tenantId, legalEntityId);
      var date = DateOnly.FromDateTime(DateTime.UtcNow);
      await InsertWorkAreaChangeRequestAsync(tenantId, employeeId1, legalEntityId, date, "pending");

      await InsertWorkAreaChangeRequestAsync(tenantId, employeeId2, legalEntityId, date, "pending");

      (await CountRowsAsync(tenantId, employeeId1)).Should().Be(1);
      (await CountRowsAsync(tenantId, employeeId2)).Should().Be(1);
  }

  [Fact]
  public async Task ActiveUniqueIndex_SameEmployeeDateDifferentTenant_Succeeds()
  {
      var (tenantId1, employeeId, legalEntityId1) = await SeedTenantEmployeeAsync();
      var (tenantId2, legalEntityId2) = await SeedAdditionalTenantAsync();
      var employeeIdInTenant2 = await SeedAdditionalEmployeeAsync(tenantId2, legalEntityId2);
      var date = DateOnly.FromDateTime(DateTime.UtcNow);
      await InsertWorkAreaChangeRequestAsync(tenantId1, employeeId, legalEntityId1, date, "pending");

      await InsertWorkAreaChangeRequestAsync(tenantId2, employeeIdInTenant2, legalEntityId2, date, "pending");

      (await CountRowsAsync(tenantId1, employeeId)).Should().Be(1);
      (await CountRowsAsync(tenantId2, employeeIdInTenant2)).Should().Be(1);
  }

  [Fact]
  public async Task ActiveUniqueIndex_SameEmployeeDifferentDate_Succeeds()
  {
      var (tenantId, employeeId, legalEntityId) = await SeedTenantEmployeeAsync();
      var date1 = DateOnly.FromDateTime(DateTime.UtcNow);
      var date2 = date1.AddDays(1);
      await InsertWorkAreaChangeRequestAsync(tenantId, employeeId, legalEntityId, date1, "pending");

      await InsertWorkAreaChangeRequestAsync(tenantId, employeeId, legalEntityId, date2, "pending");

      (await CountRowsAsync(tenantId, employeeId)).Should().Be(2);
  }
  ```

  `SeedTenantEmployeeAsync`, `SeedAdditionalEmployeeAsync`, `SeedAdditionalTenantAsync`, `InsertWorkAreaChangeRequestAsync`, and `CountRowsAsync` are small helper methods to add to the test class — implement them using the exact same raw-SQL/Npgsql-connection pattern the existing tests in this file already use for schema/RLS assertions (read Step 1's findings first; do not introduce a second, different DB-access pattern in the same file). `InsertWorkAreaChangeRequestAsync` must insert directly via SQL (or a bare, untracked `DbContext.Database.ExecuteSqlAsync`) — not through `EfWorkAreaChangeRequestRepository` — so the test proves the database constraint itself, per the reviewer's guidance and Part E's "do not bypass the database constraint in these tests."

- [ ] **Step 3: Confirm scenario 8's tenant switch reuses the existing restricted-role RLS test's `app.current_tenant_id` pattern** — read how the existing cross-tenant RLS tests in this file set/switch `app.current_tenant_id` per connection under the restricted role (FORCE ROW LEVEL SECURITY means even inserts by the app role are policy-checked), and reuse that exact mechanism for `SeedAdditionalTenantAsync`/the second insert in scenario 8.

- [ ] **Step 4: Confirm the existing RLS tests are untouched and still present** — own-tenant read succeeds, cross-tenant read returns no rows, missing tenant context returns no rows, cross-tenant update/delete cannot mutate rows, cross-tenant insert is rejected. Do not delete, weaken, or rename any of them.

- [ ] **Step 5: Run the focused integration filter** (Docker/Testcontainers required — if Docker is unavailable, record the exact blocker verbatim in the Task 10 report update; do not claim this passed):

  ```powershell
  dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --configuration Release --no-restore --filter "FullyQualifiedName~WorkAreaChangeRequestsIntegrationTests" --logger "console;verbosity=normal"
  ```
  Expected: all pass (previously 4 tests; +9 new = 13, plus whatever the existing schema/FK/index assertions already numbered).

---

### Task 9: Verify Parts F/G are already satisfied (no code changes expected)

**Files:** none modified — this task is verification-only.

- [ ] **Step 1: Confirm notification metadata typing is already correct** — re-read `NotificationContracts.cs:26-59` and confirm: Attendance Correction events get `AttendanceCorrectionId` set / `WorkAreaChangeRequestId` null; Work Area events get the reverse; neither ever sets the other to `Guid.Empty` (both are `Guid?`, defaulted to `null`, never assigned a literal `Guid.Empty` anywhere in `ResolveDestination`); unrelated notifications (`!isCorrection && !isWorkArea`) return `null` destination entirely — no invented metadata. This is already true today; make no edit.

- [ ] **Step 2: Confirm approval-request vs. decision/cancellation navigability is already correct** — `work_area_change_request_created` → `DestinationKey = "work_area_change_approval"`, `IsNavigable = true`; `work_area_change_request_decided` / `work_area_change_request_cancelled` → `DestinationKey = null`, `IsNavigable = false`, but `WorkAreaChangeRequestId` still populated (request id retained, non-navigable, no invented frontend route). Already true today per `NotificationContracts.cs:43-58`; make no edit.

- [ ] **Step 3: Run the notification navigation regression suite to lock this in**

  ```powershell
  dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --configuration Release --no-restore --filter "FullyQualifiedName~NotificationNavigation"
  ```
  Expected: all pass, unchanged.

- [ ] **Step 4: Confirm Part G persistence/validation rules are untouched** — re-read `WorkAreaChangeRequestValidators.cs` (onsite/remote only, non-empty trimmed reason, no arbitrary length cap, 2000-char review-comment cap) and `WorkAreaChangeRequestWorkflow.cs`'s `PrepareAsync`/`DecideAsync` (server-derived current expected work area, server-derived employee/legal-entity/tenant, `Employee.WorkModeId` never assigned, duplicate guard via `HasActiveForDateAsync` + the DB partial unique index, `IDateTimeProvider` used throughout, no EF import in this Application-layer file). All already true; make no edit. If any check fails on re-read, stop and report the specific discrepancy rather than silently fixing it — it would mean scope has grown beyond this plan.

---

### Task 10: EF/migration verification, full test run, and diff hygiene

**Files:** none modified except the report (Task 11).

- [ ] **Step 1: Confirm no new migration is needed** (none of Tasks 1-9 change the EF model — only Application-layer interfaces, Infrastructure repository method bodies, and test files):

  ```powershell
  dotnet ef migrations list --project src\ONEVO.Infrastructure --startup-project src\ONEVO.Api --configuration Release
  dotnet ef migrations has-pending-model-changes --project src\ONEVO.Infrastructure --startup-project src\ONEVO.Api --configuration Release
  ```
  Expected: `20260825081439_AddWorkAreaChangeRequests` listed as the latest Work Area migration; `has-pending-model-changes` reports clean, or — if blocked on `MigrationConnection` the same way the original Part 1 report recorded — report that exact blocker again, do not hand-edit `ApplicationDbContextModelSnapshot.cs` to force a clean result.

- [ ] **Step 2: Build**

  ```powershell
  dotnet build src\ONEVO.Api\ONEVO.Api.csproj --configuration Release --no-restore
  ```

- [ ] **Step 3: Focused filters**

  ```powershell
  dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --configuration Release --no-restore --filter "FullyQualifiedName~WorkAreaChangeRequest"
  dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --configuration Release --no-restore --filter "FullyQualifiedName~EmployeeAuthority"
  dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --configuration Release --no-restore --filter "FullyQualifiedName~WorkAreaChangeRequest|FullyQualifiedName~NotificationNavigation"
  ```

- [ ] **Step 4: Full unit suite**

  ```powershell
  dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --configuration Release --no-restore
  ```

- [ ] **Step 5: Architecture suites**

  ```powershell
  dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --configuration Release --no-restore --filter "FullyQualifiedName~WorkAreaChangeRequests|FullyQualifiedName~EmployeeAuthority"
  dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --configuration Release --no-restore
  ```
  Expected: the dedicated filter passes fully; the full suite reproduces the exact same pre-existing `TimeTrackingMutationArchitectureTests.AttendanceRepository_UsesTrackedFetchForMutation` failure recorded in the original Part 1 report (same test, same `ArgumentOutOfRangeException` stack trace) — confirm this by comparing the failure output to the original report's §12 row before/after; do not modify that test or its target file.

- [ ] **Step 6: Integration suite** (Task 8's filter, repeated here as part of the full verification pass)

  ```powershell
  dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --configuration Release --no-restore --filter "FullyQualifiedName~WorkAreaChangeRequestsIntegrationTests" --logger "console;verbosity=normal"
  ```

- [ ] **Step 7: Diff hygiene**

  ```powershell
  git diff --check
  git status --short
  git diff --stat
  ```
  Confirm: no new whitespace errors beyond the pre-existing CRLF-normalization warnings already present at the start of this correction; `git status --short` shows only the files this plan actually touched plus the pre-existing untouched items (`PositionsController.cs`, `dev-server-restart.log`, the attendance-list-pagination plan file) — nothing staged.

---

### Task 11: Update the Part 1 report with the final hardening section

**Files:**
- Modify: `WORK_AREA_CHANGE_REQUEST_BACKEND_PART1_REPORT.md`

- [ ] **Step 1: Append a new top-level section** titled exactly `Final authority, multi-company, scalability, and PostgreSQL hardening`, covering (in prose, referencing the exact files/line ranges touched by Tasks 1-10): the four original problems and their exact root causes (caller-supplied reviewer identity; `GetDefaultForUserAsync` used post-login instead of `Session.ActiveEmployeeId`; O(N) per-candidate authority resolution; missing direct/Postgres test coverage); reviewer identity before (`EmployeeApprovalInboxScopeRequest.ReviewerUserId`, caller-supplied) and after (`ICurrentUser.UserId`, server-derived, with the three fail-closed guards); how tenant and reviewer identity are now both server-derived; the multi-company evidence trail (`SwitchActiveCompanyCommandHandler` as the precedent proving Application-layer session access, `Session.ActiveEmployeeId` as the authoritative signal, the exact fallback rule for a null `ActiveEmployeeId`) and the final contract (no route/claim change — self-service and the approval inbox share one corrected `ResolveActiveEmployeeAsync`); the exact approval-inbox eligibility algorithm (five phases, as implemented in Task 7); how batching avoids per-candidate database calls (the six new repository methods, called a small constant number of times each regardless of N, proven by Task 7 Step 3's call-count tests); the exact list of repository interfaces added/changed (Task 3); the 24 direct `EmployeeAuthorityResolverTests` additions plus the equivalence and performance tests (Task 6/7); the 9 partial-unique-index Postgres tests and confirmation the pre-existing RLS tests are untouched (Task 8); confirmation Parts F/G required no code changes (Task 9, with the specific lines re-verified); the files-changed list; the exact commands run and their results (Task 10); exact test counts before/after; any skipped/blocked checks (Docker availability for Task 8, `has-pending-model-changes` connection status); remaining risks; explicit confirmation the frontend was untouched, `Employee.WorkModeId` was never written to, and nothing was staged/committed/pushed at any point.

- [ ] **Step 2: Explicitly state what was scoped out** (per Global Constraints above) so a future reader doesn't mistake omission for oversight: `either`/`field` targets, `shift_assignment_id`, notification `LegalEntityId`/`ReviewerDisplayName`, the redundant index, the hardcoded `"Approver"` display name, and `PositionsController.cs`.

- [ ] **Step 3: Final read-through** — confirm every claim in the new section matches an actual command output captured in Task 10, not an assumption. Do not state the feature is fully end-to-end complete; explicitly restate that runtime application to Today/Clock-In/Clock-Out/history/expected-work-area-display/employee-list-attention-status remains the next backend part (unchanged from the original report's §13).
