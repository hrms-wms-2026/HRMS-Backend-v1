# Management Coverage Responsible Person Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let `ManagementCoverageRecord` resolve to a specific employee, not just a Position — asked for only when the owner position has multiple current holders, silently auto-resolved otherwise — and expose that resolution as a reusable query, without inventing any approval-consumer that doesn't exist yet.

**Architecture:** One nullable column on `management_coverage_records` (no FK, same time-varying-validity rationale as `PositionAssignment.ReportsToEmployeeId`). A new resolution query walks matching coverage records in `OwnerOrder` sequence, resolving each via the already-implemented `IPositionAssignmentRepository.GetActiveHoldersAsync`. `AddManualCoverageRecordCommand`/`UpdateManualCoverageRecordCommand` gain the same required-when-ambiguous validation already built for reporting-manager. The Position Coverage modal gains a picker, shown only when needed.

**Tech Stack:** Same as the reporting-manager-resolution plan: .NET 10/EF Core backend (branch `local/reporting-manager-run`, currently checked out at `C:\onevoNew\HRMS-Backend-v1` — **run every backend command from that directory**), Angular 21 frontend (`C:\onevoNew\Hrms--Web-application---front-end---v1`, branch `feature/employee-management-phase1-foundation`).

## Global Constraints

- **Prerequisite, already satisfied and verified**: `IPositionAssignmentRepository.GetActiveHoldersAsync(Guid tenantId, Guid positionId, CancellationToken ct)` returning `IReadOnlyList<PositionActiveHolder>` where `PositionActiveHolder(Guid EmployeeId, string FirstName, string LastName, string WorkEmail, Guid? AvatarFileId)` exists on the current branch (verified by independent build + test run before this plan was written: `dotnet build` clean, 2406 unit + 596 architecture tests passing). Note the field is named `WorkEmail` on the record but is actually populated from `Employee.Email` — don't assume `Employee` itself has a `WorkEmail` property.
- `responsible_employee_id` is a plain nullable `uuid` column, **no FK constraint** — same rationale as `PositionAssignment.ReportsToEmployeeId`: validity ("is this employee a current active holder of the owner position") is time-varying, enforced in application code.
- Never auto-substitute a different employee (e.g. fall through to the next `OwnerOrder` level) when a `ResponsibleEmployeeId` becomes stale — mark that level `Incomplete` and require a human to re-pick (user-confirmed).
- This design does not build any approval-routing consumer (no leave approval, no notification routing exists in this codebase today) — only the resolver primitive and the modal UI to manage it.
- Do not touch `OwnerOrder`'s existing semantics, the existing conflict-detection (`HasActiveCoverageConflictAsync`), or the modal's existing Primary/Backup level UI — all already work and are out of scope.
- Existing frontend method `PositionApiService.listCoverageByTarget` calls `GET .../positions/coverage/by-target`, which **has no backend controller action anywhere in the API project** (confirmed via full-project grep — this is a pre-existing gap, unrelated to this feature). Do not fix it as part of this plan; it's out of scope. Do not confuse it with this plan's new resolution endpoint, which answers a different question (an actual employee, not a list of competing owner records).
- Migration commands run from the backend repo root: `dotnet ef migrations add <Name> --project src\ONEVO.Infrastructure --startup-project src\ONEVO.Api`.

---

## Task 1: `ManagementCoverageRecord.ResponsibleEmployeeId` column + entity

**Files:**
- Modify: `src/ONEVO.Domain/Features/OrgStructure/Position/Entities/ManagementCoverageRecord.cs`
- Create (via `dotnet ef migrations add`): new migration in `src/ONEVO.Infrastructure/Migrations/`

**Interfaces:**
- Produces: `ManagementCoverageRecord.ResponsibleEmployeeId` (`Guid?`) — consumed by Task 2 (resolution), Task 3/4 (validation + persistence).

- [ ] **Step 1: Add the property**

In `ManagementCoverageRecord.cs`, add `public Guid? ResponsibleEmployeeId { get; set; }` alongside the existing properties.

- [ ] **Step 2: Generate and review the migration**

```bash
dotnet ef migrations add AddManagementCoverageRecordResponsibleEmployeeId --project src\ONEVO.Infrastructure --startup-project src\ONEVO.Api
```

Confirm the generated `Up()` is exactly:
```csharp
migrationBuilder.AddColumn<Guid>(
    name: "responsible_employee_id",
    table: "management_coverage_records",
    type: "uuid",
    nullable: true);
```
Remove any auto-generated index/FK — this column is intentionally unconstrained (Global Constraints).

- [ ] **Step 3: Apply and verify**

Run: `dotnet ef database update --project src\ONEVO.Infrastructure --startup-project src\ONEVO.Api`
Expected: clean apply.

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: succeeds. (No isolated test for a bare column add — Task 3 covers the behavior.)

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Domain/Features/OrgStructure/Position/Entities/ManagementCoverageRecord.cs src/ONEVO.Infrastructure/Migrations/
git commit -m "feat: add ManagementCoverageRecord.ResponsibleEmployeeId column"
```

---

## Task 2: Coverage resolution query

**Files:**
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Queries/GetCoverageResolution/GetCoverageResolutionQuery.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Queries/GetCoverageResolution/GetCoverageResolutionQueryHandler.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Responses/CoverageResolutionLevelResponse.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/OrgStructure/PositionsController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/Queries/GetCoverageResolution/GetCoverageResolutionQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `IPositionAssignmentRepository.GetActiveHoldersAsync`, `IPositionRepository.ListActiveCoverageByCoveredTargetAsync` (existing method backing `GetCoverageByTargetQueryHandler` — reused here, not duplicated).
- Produces: `GET api/v1/org/legal-entities/{legalEntityId}/positions/coverage/resolve` → `IReadOnlyList<CoverageResolutionLevelResponse>`. This is the entire "reusable primitive" a future approval consumer would call — no consumer built here.

- [ ] **Step 1: Write the failing test**

```csharp
public class GetCoverageResolutionQueryHandlerTests
{
    private readonly Mock<IPositionRepository> _positions = new();
    private readonly Mock<IPositionAssignmentRepository> _assignments = new();
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();

    private GetCoverageResolutionQueryHandler CreateHandler() =>
        new(_positions.Object, _assignments.Object, _legalEntities.Object, _currentUser.Object);

    private void SetupAuth()
    {
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _currentUser.Setup(u => u.TenantId).Returns(_tenantId);
        _legalEntities.Setup(l => l.GetByIdForTenantAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity { Id = _legalEntityId, TenantId = _tenantId });
    }

    [Fact]
    public async Task Handle_Resolves_Single_Holder_Owner_Automatically_Ignoring_ResponsibleEmployeeId()
    {
        SetupAuth();
        var ownerPositionId = Guid.NewGuid();
        var soleHolderId = Guid.NewGuid();
        var deptId = Guid.NewGuid();

        _positions.Setup(p => p.ListActiveCoverageByCoveredTargetAsync(
                _tenantId, _legalEntityId, "Department", null, deptId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManagementCoverageRecord>
            {
                new() { OwnerPositionId = ownerPositionId, OwnerOrder = 1, ResponsibleEmployeeId = null, CoveredTargetType = "Department", CoveredDepartmentId = deptId, Status = ManagementCoverageRecord.StatusActive }
            });
        _assignments.Setup(a => a.GetActiveHoldersAsync(_tenantId, ownerPositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PositionActiveHolder> { new(soleHolderId, "A", "One", "a@acme.test", null) });

        var result = await CreateHandler().Handle(
            new GetCoverageResolutionQuery(_legalEntityId, "Department", null, deptId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Single().Status.Should().Be("Resolved");
        result.Value.Single().EmployeeId.Should().Be(soleHolderId);
    }

    [Fact]
    public async Task Handle_Resolves_Pooled_Owner_Via_ResponsibleEmployeeId()
    {
        SetupAuth();
        var ownerPositionId = Guid.NewGuid();
        var chosenId = Guid.NewGuid();
        var deptId = Guid.NewGuid();

        _positions.Setup(p => p.ListActiveCoverageByCoveredTargetAsync(
                _tenantId, _legalEntityId, "Department", null, deptId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManagementCoverageRecord>
            {
                new() { OwnerPositionId = ownerPositionId, OwnerOrder = 1, ResponsibleEmployeeId = chosenId, CoveredTargetType = "Department", CoveredDepartmentId = deptId, Status = ManagementCoverageRecord.StatusActive }
            });
        _assignments.Setup(a => a.GetActiveHoldersAsync(_tenantId, ownerPositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PositionActiveHolder>
            {
                new(chosenId, "A", "One", "a@acme.test", null),
                new(Guid.NewGuid(), "B", "Two", "b@acme.test", null),
            });

        var result = await CreateHandler().Handle(
            new GetCoverageResolutionQuery(_legalEntityId, "Department", null, deptId), CancellationToken.None);

        result.Value.Single().Status.Should().Be("Resolved");
        result.Value.Single().EmployeeId.Should().Be(chosenId);
    }

    [Fact]
    public async Task Handle_Marks_Incomplete_When_ResponsibleEmployeeId_Is_Stale()
    {
        SetupAuth();
        var ownerPositionId = Guid.NewGuid();
        var staleId = Guid.NewGuid(); // no longer among current holders
        var deptId = Guid.NewGuid();

        _positions.Setup(p => p.ListActiveCoverageByCoveredTargetAsync(
                _tenantId, _legalEntityId, "Department", null, deptId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManagementCoverageRecord>
            {
                new() { OwnerPositionId = ownerPositionId, OwnerOrder = 1, ResponsibleEmployeeId = staleId, CoveredTargetType = "Department", CoveredDepartmentId = deptId, Status = ManagementCoverageRecord.StatusActive }
            });
        _assignments.Setup(a => a.GetActiveHoldersAsync(_tenantId, ownerPositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PositionActiveHolder>
            {
                new(Guid.NewGuid(), "A", "One", "a@acme.test", null),
                new(Guid.NewGuid(), "B", "Two", "b@acme.test", null),
            });

        var result = await CreateHandler().Handle(
            new GetCoverageResolutionQuery(_legalEntityId, "Department", null, deptId), CancellationToken.None);

        result.Value.Single().Status.Should().Be("Incomplete");
        result.Value.Single().EmployeeId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Marks_Incomplete_When_Owner_Position_Is_Vacant()
    {
        SetupAuth();
        var ownerPositionId = Guid.NewGuid();
        var deptId = Guid.NewGuid();

        _positions.Setup(p => p.ListActiveCoverageByCoveredTargetAsync(
                _tenantId, _legalEntityId, "Department", null, deptId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManagementCoverageRecord>
            {
                new() { OwnerPositionId = ownerPositionId, OwnerOrder = 1, ResponsibleEmployeeId = null, CoveredTargetType = "Department", CoveredDepartmentId = deptId, Status = ManagementCoverageRecord.StatusActive }
            });
        _assignments.Setup(a => a.GetActiveHoldersAsync(_tenantId, ownerPositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PositionActiveHolder>());

        var result = await CreateHandler().Handle(
            new GetCoverageResolutionQuery(_legalEntityId, "Department", null, deptId), CancellationToken.None);

        result.Value.Single().Status.Should().Be("Incomplete");
    }
}
```

(Adjust `ManagementCoverageRecord`'s object-initializer to include any other required properties the entity actually has — check the current full entity shape from Task 1 before finalizing. Match `Result<T>`'s actual `IsSuccess`/`.Value` API to whatever the codebase's `Result<T>` exposes, consistent with other handler tests already in this codebase.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetCoverageResolutionQueryHandlerTests"`
Expected: FAIL — compile error, types don't exist yet.

- [ ] **Step 3: Add the query and response types**

`GetCoverageResolutionQuery.cs`:
```csharp
public record GetCoverageResolutionQuery(
    Guid LegalEntityId,
    string CoveredTargetType,
    Guid? CoveredPositionId,
    Guid? CoveredDepartmentId) : IRequest<Result<IReadOnlyList<CoverageResolutionLevelResponse>>>;
```

`CoverageResolutionLevelResponse.cs`:
```csharp
public record CoverageResolutionLevelResponse(
    int OwnerOrder,
    Guid OwnerPositionId,
    string? OwnerPositionName,
    string Status, // "Resolved" or "Incomplete"
    Guid? EmployeeId,
    string? EmployeeName);
```

- [ ] **Step 4: Implement the handler**

```csharp
public class GetCoverageResolutionQueryHandler
    : IRequestHandler<GetCoverageResolutionQuery, Result<IReadOnlyList<CoverageResolutionLevelResponse>>>
{
    private readonly IPositionRepository _positions;
    private readonly IPositionAssignmentRepository _assignments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;

    public GetCoverageResolutionQueryHandler(
        IPositionRepository positions, IPositionAssignmentRepository assignments,
        ILegalEntityRepository legalEntities, ICurrentUser currentUser)
    {
        _positions = positions;
        _assignments = assignments;
        _legalEntities = legalEntities;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<CoverageResolutionLevelResponse>>> Handle(
        GetCoverageResolutionQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<CoverageResolutionLevelResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<CoverageResolutionLevelResponse>>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<IReadOnlyList<CoverageResolutionLevelResponse>>.NotFound("Legal entity not found.");

        var records = await _positions.ListActiveCoverageByCoveredTargetAsync(
            tenantId, request.LegalEntityId, request.CoveredTargetType,
            request.CoveredPositionId, request.CoveredDepartmentId, excludingRecordId: null, ct);

        var ownerIds = records.Select(r => r.OwnerPositionId).Distinct().ToList();
        var owners = await _positions.GetByIdsAsync(tenantId, ownerIds, ct);
        var ownerNamesById = owners.ToDictionary(p => p.Id, p => p.Name);

        var levels = new List<CoverageResolutionLevelResponse>();
        foreach (var record in records.OrderBy(r => r.OwnerOrder))
        {
            var holders = await _assignments.GetActiveHoldersAsync(tenantId, record.OwnerPositionId, ct);
            var ownerName = ownerNamesById.GetValueOrDefault(record.OwnerPositionId);

            PositionActiveHolder? resolved = holders.Count switch
            {
                1 => holders[0],
                > 1 when record.ResponsibleEmployeeId is { } chosenId =>
                    holders.FirstOrDefault(h => h.EmployeeId == chosenId),
                _ => null,
            };

            levels.Add(resolved is not null
                ? new CoverageResolutionLevelResponse(record.OwnerOrder, record.OwnerPositionId, ownerName, "Resolved", resolved.EmployeeId, $"{resolved.FirstName} {resolved.LastName}")
                : new CoverageResolutionLevelResponse(record.OwnerOrder, record.OwnerPositionId, ownerName, "Incomplete", null, null));
        }

        return Result<IReadOnlyList<CoverageResolutionLevelResponse>>.Success(levels);
    }
}
```

(`IPositionRepository.GetByIdsAsync` and `ListActiveCoverageByCoveredTargetAsync` already exist — confirmed via `GetCoverageByTargetQueryHandler.cs`'s existing usage of both; reused here verbatim, not reimplemented.)

- [ ] **Step 5: Add the controller endpoint**

In `PositionsController.cs`, add (matching the file's existing action conventions):

```csharp
[HttpGet("coverage/resolve")]
[RequirePermission("org:read")]
public async Task<IActionResult> ResolveCoverage(
    Guid legalEntityId,
    [FromQuery] string coveredTargetType,
    [FromQuery] Guid? coveredPositionId,
    [FromQuery] Guid? coveredDepartmentId,
    CancellationToken ct = default)
{
    var result = await _mediator.Send(
        new GetCoverageResolutionQuery(legalEntityId, coveredTargetType, coveredPositionId, coveredDepartmentId),
        ct);

    return result.IsSuccess
        ? Ok(result.Value)
        : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetCoverageResolutionQueryHandlerTests"`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/OrgStructure/Position/Queries/GetCoverageResolution/ src/ONEVO.Application/Features/OrgStructure/Position/Responses/CoverageResolutionLevelResponse.cs src/ONEVO.Api/Controllers/Tenant/OrgStructure/PositionsController.cs tests/
git commit -m "feat: add coverage resolution query and endpoint"
```

---

## Task 3: `AddManualCoverageRecordCommand` validates and persists `ResponsibleEmployeeId`

**Files:**
- Modify: `src/ONEVO.Application/Features/OrgStructure/Position/Commands/AddManualCoverageRecord/AddManualCoverageRecordCommand.cs`
- Modify: `src/ONEVO.Application/Features/OrgStructure/Position/Commands/AddManualCoverageRecord/AddManualCoverageRecordCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/OrgStructure/Position/Responses/ManagementCoverageRecordResponse.cs`
- Modify: `src/ONEVO.Application/Features/OrgStructure/Position/Mappers/ManagementCoverageRecordMapper.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/Commands/AddManualCoverageRecord/AddManualCoverageRecordCommandHandlerTests.cs` (find and extend if it exists, else create following this file's own request/response shape)

**Interfaces:**
- Consumes: `IPositionAssignmentRepository.GetActiveHoldersAsync`.
- Produces: `AddManualCoverageRecordCommand.ResponsibleEmployeeId`; `ManagementCoverageRecordResponse.ResponsibleEmployeeId`/`ResponsibleEmployeeName`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task Handle_Requires_ResponsibleEmployeeId_When_Owner_Position_Is_Pooled()
{
    var ownerId = Guid.NewGuid();
    _positions.Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, ownerId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Position { Id = ownerId, IsActive = true });
    _assignments.Setup(a => a.GetActiveHoldersAsync(_tenantId, ownerId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<PositionActiveHolder>
        {
            new(Guid.NewGuid(), "A", "One", "a@acme.test", null),
            new(Guid.NewGuid(), "B", "Two", "b@acme.test", null),
        });

    var command = new AddManualCoverageRecordCommand(_legalEntityId, ownerId, "Department", null, Guid.NewGuid(), 1, ResponsibleEmployeeId: null);

    var result = await CreateHandler().Handle(command, CancellationToken.None);

    result.IsUnprocessableEntity.Should().BeTrue();
}

[Fact]
public async Task Handle_Rejects_ResponsibleEmployeeId_Not_A_Current_Holder()
{
    var ownerId = Guid.NewGuid();
    _positions.Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, ownerId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Position { Id = ownerId, IsActive = true });
    _assignments.Setup(a => a.GetActiveHoldersAsync(_tenantId, ownerId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<PositionActiveHolder> { new(Guid.NewGuid(), "A", "One", "a@acme.test", null) });

    var command = new AddManualCoverageRecordCommand(_legalEntityId, ownerId, "Department", null, Guid.NewGuid(), 1, ResponsibleEmployeeId: Guid.NewGuid());

    var result = await CreateHandler().Handle(command, CancellationToken.None);

    result.IsUnprocessableEntity.Should().BeTrue();
}

[Fact]
public async Task Handle_Persists_ResponsibleEmployeeId_When_Valid()
{
    var ownerId = Guid.NewGuid();
    var chosenId = Guid.NewGuid();
    _positions.Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, ownerId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Position { Id = ownerId, IsActive = true });
    _assignments.Setup(a => a.GetActiveHoldersAsync(_tenantId, ownerId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<PositionActiveHolder> { new(chosenId, "A", "One", "a@acme.test", null) });

    var command = new AddManualCoverageRecordCommand(_legalEntityId, ownerId, "Department", null, Guid.NewGuid(), 1, ResponsibleEmployeeId: chosenId);

    var result = await CreateHandler().Handle(command, CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    result.Value.ResponsibleEmployeeId.Should().Be(chosenId);
}

[Fact]
public async Task Handle_Ignores_ResponsibleEmployeeId_When_Owner_Position_Has_Single_Holder()
{
    var ownerId = Guid.NewGuid();
    _positions.Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, ownerId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Position { Id = ownerId, IsActive = true });
    _assignments.Setup(a => a.GetActiveHoldersAsync(_tenantId, ownerId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<PositionActiveHolder> { new(Guid.NewGuid(), "A", "One", "a@acme.test", null) });

    var command = new AddManualCoverageRecordCommand(_legalEntityId, ownerId, "Department", null, Guid.NewGuid(), 1, ResponsibleEmployeeId: null);

    var result = await CreateHandler().Handle(command, CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
}
```

(Match the existing `_positions`/`_departments`/`_legalEntities`/`_currentUser`/`_dateTimeProvider` mock field names and `CreateHandler()` factory to whatever the existing test setup in this codebase's sibling handler tests already uses — e.g. mirror `SaveOnboardingDraftCommandHandlerTests`'s structure. If no existing test file exists for this handler yet, create one following that same structural pattern, including a `_currentUser`/tenant-auth happy-path default setup in a constructor or shared setup method.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~AddManualCoverageRecordCommandHandlerTests"`
Expected: FAIL — compile error.

- [ ] **Step 3: Add the field to the command, response, and mapper**

`AddManualCoverageRecordCommand.cs` — append as the last positional parameter:
```csharp
public record AddManualCoverageRecordCommand(
    Guid LegalEntityId,
    Guid PositionId,
    string CoveredTargetType,
    Guid? CoveredPositionId,
    Guid? CoveredDepartmentId,
    int OwnerOrder,
    Guid? ResponsibleEmployeeId) : IRequest<Result<ManagementCoverageRecordResponse>>;
```

`ManagementCoverageRecordResponse.cs` — append two fields:
```csharp
public record ManagementCoverageRecordResponse(
    Guid Id,
    Guid OwnerPositionId,
    string? OwnerPositionName,
    string CoveredTargetType,
    Guid? CoveredPositionId,
    string? CoveredPositionName,
    Guid? CoveredDepartmentId,
    string? CoveredDepartmentName,
    int OwnerOrder,
    string Source,
    bool IsLocked,
    string Status,
    Guid? ResponsibleEmployeeId,
    string? ResponsibleEmployeeName);
```

`ManagementCoverageRecordMapper.cs`'s `ToResponse` — add two parameters and thread them through:
```csharp
public static ManagementCoverageRecordResponse ToResponse(
    ManagementCoverageRecord entity,
    string? ownerPositionName,
    string? coveredPositionName,
    string? coveredDepartmentName,
    string? responsibleEmployeeName)
{
    return new ManagementCoverageRecordResponse(
        entity.Id, entity.OwnerPositionId, ownerPositionName, entity.CoveredTargetType,
        entity.CoveredPositionId, coveredPositionName, entity.CoveredDepartmentId, coveredDepartmentName,
        entity.OwnerOrder, entity.Source, entity.IsLocked, entity.Status,
        entity.ResponsibleEmployeeId, responsibleEmployeeName);
}
```

Fix every other `ToResponse(...)` call site (search `grep -rn "ManagementCoverageRecordMapper.ToResponse"`) to pass a `responsibleEmployeeName` argument — for callers that already have the owner's active holders loaded (like this handler will, per Step 4), resolve the name from that list; for callers that don't need it (e.g. a list view that doesn't care), pass `null`.

- [ ] **Step 4: Add validation and persistence in the handler**

In `AddManualCoverageRecordCommandHandler.cs`, after the existing owner-position lookup (before building the `CoverageRecordEntity`):

```csharp
var activeHolders = await _positionAssignments.GetActiveHoldersAsync(tenantId, owner.Id, ct);
string? responsibleEmployeeName = null;

if (activeHolders.Count > 1)
{
    if (request.ResponsibleEmployeeId is not { } chosenId)
        return Result<ManagementCoverageRecordResponse>.UnprocessableEntity(
            "This position has multiple current holders — select who is responsible for this coverage.");

    var matched = activeHolders.FirstOrDefault(h => h.EmployeeId == chosenId);
    if (matched is null)
        return Result<ManagementCoverageRecordResponse>.UnprocessableEntity(
            "The selected responsible person is not a current holder of this position.");

    responsibleEmployeeName = $"{matched.FirstName} {matched.LastName}";
}
else if (activeHolders.Count == 1)
{
    responsibleEmployeeName = $"{activeHolders[0].FirstName} {activeHolders[0].LastName}";
}
```

Add `IPositionAssignmentRepository _positionAssignments` to the constructor's injected dependencies.

Set `ResponsibleEmployeeId = activeHolders.Count > 1 ? request.ResponsibleEmployeeId : null` on the constructed `CoverageRecordEntity` (unique-holder case stores null — nothing to disambiguate, matches Task 2's resolution logic which ignores the stored value anyway when there's exactly one holder).

Update the final `ManagementCoverageRecordMapper.ToResponse(...)` call to pass `responsibleEmployeeName`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~AddManualCoverageRecordCommandHandlerTests"`
Expected: PASS

- [ ] **Step 6: Fix other broken callers**

Run: `dotnet build`. Fix any other `new AddManualCoverageRecordCommand(...)` or `ManagementCoverageRecordMapper.ToResponse(...)` call sites the compiler flags.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/OrgStructure/Position/Commands/AddManualCoverageRecord/ src/ONEVO.Application/Features/OrgStructure/Position/Responses/ManagementCoverageRecordResponse.cs src/ONEVO.Application/Features/OrgStructure/Position/Mappers/ManagementCoverageRecordMapper.cs tests/
git commit -m "feat: validate and persist ResponsibleEmployeeId on AddManualCoverageRecord"
```

---

## Task 4: `UpdateManualCoverageRecordCommand` allows changing `ResponsibleEmployeeId`

**Files:**
- Modify: `src/ONEVO.Application/Features/OrgStructure/Position/Commands/UpdateManualCoverageRecord/UpdateManualCoverageRecordCommand.cs`
- Modify: `src/ONEVO.Application/Features/OrgStructure/Position/Commands/UpdateManualCoverageRecord/UpdateManualCoverageRecordCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/Commands/UpdateManualCoverageRecord/UpdateManualCoverageRecordCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IPositionAssignmentRepository.GetActiveHoldersAsync`.

- [ ] **Step 1: Write the failing tests**

Mirror Task 3's four test shapes (require-when-ambiguous, reject-non-holder, persist-when-valid, ignore-when-unique), adapted to `UpdateManualCoverageRecordCommand`'s existing pattern of loading the record via `GetCoverageRecordByIdAsync` first. Additionally:

```csharp
[Fact]
public async Task Handle_Allows_Clearing_ResponsibleEmployeeId_Back_To_Requiring_A_Pick()
{
    // Arrange an existing record with ResponsibleEmployeeId already set, owner position still has >1 holders.
    var command = new UpdateManualCoverageRecordCommand(_legalEntityId, _ownerId, _recordId, OwnerOrder: 1, ResponsibleEmployeeId: null);

    var result = await CreateHandler().Handle(command, CancellationToken.None);

    result.IsUnprocessableEntity.Should().BeTrue(); // still ambiguous, still required — clearing it isn't the same as "no longer needed"
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~UpdateManualCoverageRecordCommandHandlerTests"`
Expected: FAIL.

- [ ] **Step 3: Add the field to the command**

```csharp
public record UpdateManualCoverageRecordCommand(
    Guid LegalEntityId,
    Guid PositionId,
    Guid CoverageId,
    int OwnerOrder,
    Guid? ResponsibleEmployeeId) : IRequest<Result<ManagementCoverageRecordResponse>>;
```

- [ ] **Step 4: Add the same validation block as Task 3, applied to the loaded `record`**

In `UpdateManualCoverageRecordCommandHandler.cs`, after the existing lock/source check (before the conflict check), insert the identical validation shape from Task 3 Step 4, using `record.OwnerPositionId` (the coverage record's own owner, which doesn't change on update — only `OwnerOrder`/`ResponsibleEmployeeId` do) instead of `owner.Id`. Set `record.ResponsibleEmployeeId = activeHolders.Count > 1 ? request.ResponsibleEmployeeId : null;` alongside the existing `record.OwnerOrder = request.OwnerOrder;` line. Update the final mapper call to pass `responsibleEmployeeName`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~UpdateManualCoverageRecordCommandHandlerTests"`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/OrgStructure/Position/Commands/UpdateManualCoverageRecord/ tests/
git commit -m "feat: validate and persist ResponsibleEmployeeId on UpdateManualCoverageRecord"
```

---

## Task 5: Thread `responsibleEmployeeId` through the API contracts

**Files:**
- Modify: `src/ONEVO.Api/Contracts/OrgStructure/Positions/AddCoverageRecordRequest.cs`
- Modify: `src/ONEVO.Api/Contracts/OrgStructure/Positions/UpdateCoverageRecordRequest.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/OrgStructure/PositionsController.cs`
- Test: any existing controller-level test for `AddCoverage`/`UpdateCoverage` (find via `grep -rln "AddCoverage\|UpdateCoverage" tests/`)

**Interfaces:**
- Produces: `AddCoverageRecordRequest.ResponsibleEmployeeId`, `UpdateCoverageRecordRequest.ResponsibleEmployeeId` — the wire shape the frontend (Task 6+) sends.

- [ ] **Step 1: Add the field to both contracts**

```csharp
public record AddCoverageRecordRequest(
    string CoveredTargetType,
    Guid? CoveredPositionId,
    Guid? CoveredDepartmentId,
    int OwnerOrder,
    Guid? ResponsibleEmployeeId);
```

```csharp
public record UpdateCoverageRecordRequest(int OwnerOrder, Guid? ResponsibleEmployeeId);
```

(Check whether these are actually `record` types with positional params or plain classes with settable properties — match whatever the existing file shows; adjust the constructor-call sites in Step 2 accordingly.)

- [ ] **Step 2: Update the controller call sites**

In `PositionsController.AddCoverage`, add `request.ResponsibleEmployeeId` as the new trailing argument to the `AddManualCoverageRecordCommand` construction. In `UpdateCoverage`, same for `UpdateManualCoverageRecordCommand`.

- [ ] **Step 3: Build and run any existing controller tests**

Run: `dotnet build && dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~PositionsController"`
Expected: PASS (or no matching tests found, in which case this step just confirms the build).

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Api/Contracts/OrgStructure/Positions/ src/ONEVO.Api/Controllers/Tenant/OrgStructure/PositionsController.cs
git commit -m "feat: expose ResponsibleEmployeeId on coverage add/update API contracts"
```

---

## Task 6: Frontend models + API methods

**Files:**
- Modify: `Hrms--Web-application---front-end---v1/src/app/modules/organization/models/position.model.ts`
- Modify: `Hrms--Web-application---front-end---v1/src/app/modules/organization/data-access/position-api.service.ts`
- Test: `position-api.service.spec.ts`

**Interfaces:**
- Produces: `ManagementCoverageRecordResponse.responsibleEmployeeId`/`responsibleEmployeeName`; `AddCoverageRecordRequest.responsibleEmployeeId`; `UpdateCoverageRecordRequest.responsibleEmployeeId`; `ActiveHolder` interface (mirrors the one added in the reporting-manager plan's Task 14, but in the `organization` module — this codebase keeps per-module API services separate, so this is a small intentional duplication, not an oversight); `PositionApiService.getActiveHolders(legalEntityId, positionId)`; `PositionApiService.resolveCoverage(legalEntityId, coveredTargetType, coveredPositionId, coveredDepartmentId)`.

- [ ] **Step 1: Write the failing test**

```typescript
it('getActiveHolders requests the position active-holders endpoint', () => {
  service.getActiveHolders('legal-entity-1', 'position-1').subscribe();
  const req = httpMock.expectOne(`${environment.apiUrl}/org/legal-entities/legal-entity-1/positions/position-1/active-holders`);
  expect(req.request.method).toBe('GET');
  req.flush([]);
});

it('resolveCoverage requests the coverage resolution endpoint with query params', () => {
  service.resolveCoverage('legal-entity-1', 'Department', null, 'dept-1').subscribe();
  const req = httpMock.expectOne((r) =>
    r.url === `${environment.apiUrl}/org/legal-entities/legal-entity-1/positions/coverage/resolve` &&
    r.params.get('coveredTargetType') === 'Department' &&
    r.params.get('coveredDepartmentId') === 'dept-1'
  );
  expect(req.request.method).toBe('GET');
  req.flush([]);
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `npx ng test --include='**/position-api.service.spec.ts'`
Expected: FAIL.

- [ ] **Step 3: Update the models**

In `position.model.ts`:
```typescript
export interface ManagementCoverageRecordResponse {
  readonly id: string;
  readonly ownerPositionId: string;
  readonly ownerPositionName: string | null;
  readonly coveredTargetType: 'Position' | 'Department';
  readonly coveredPositionId: string | null;
  readonly coveredPositionName: string | null;
  readonly coveredDepartmentId: string | null;
  readonly coveredDepartmentName: string | null;
  readonly ownerOrder: number;
  readonly source: 'ReportingStructure' | 'Manual';
  readonly isLocked: boolean;
  readonly status: 'active' | 'inactive';
  readonly responsibleEmployeeId: string | null;
  readonly responsibleEmployeeName: string | null;
}

export interface AddCoverageRecordRequest {
  coveredTargetType: 'Position' | 'Department';
  coveredPositionId?: string | null;
  coveredDepartmentId?: string | null;
  ownerOrder: number;
  responsibleEmployeeId?: string | null;
}

export interface UpdateCoverageRecordRequest {
  ownerOrder: number;
  responsibleEmployeeId?: string | null;
}

export interface ActiveHolder {
  employeeId: string;
  firstName: string;
  lastName: string;
  workEmail: string;
  avatarFileId: string | null;
}

export interface CoverageResolutionLevel {
  ownerOrder: number;
  ownerPositionId: string;
  ownerPositionName: string | null;
  status: 'Resolved' | 'Incomplete';
  employeeId: string | null;
  employeeName: string | null;
}
```

- [ ] **Step 4: Add the API methods**

In `position-api.service.ts`:
```typescript
getActiveHolders(legalEntityId: string, positionId: string): Observable<readonly ActiveHolder[]> {
  return this.http.get<readonly ActiveHolder[]>(
    `${environment.apiUrl}/org/legal-entities/${legalEntityId}/positions/${positionId}/active-holders`
  );
}

resolveCoverage(
  legalEntityId: string,
  coveredTargetType: 'Position' | 'Department',
  coveredPositionId: string | null,
  coveredDepartmentId: string | null
): Observable<readonly CoverageResolutionLevel[]> {
  let params = new HttpParams().set('coveredTargetType', coveredTargetType);
  if (coveredPositionId) params = params.set('coveredPositionId', coveredPositionId);
  if (coveredDepartmentId) params = params.set('coveredDepartmentId', coveredDepartmentId);

  return this.http.get<readonly CoverageResolutionLevel[]>(
    `${environment.apiUrl}/org/legal-entities/${legalEntityId}/positions/coverage/resolve`,
    { params }
  );
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `npx ng test --include='**/position-api.service.spec.ts'`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/app/modules/organization/models/position.model.ts src/app/modules/organization/data-access/position-api.service.ts src/app/modules/organization/data-access/position-api.service.spec.ts
git commit -m "feat: add ActiveHolder/CoverageResolutionLevel models and API methods"
```

---

## Task 7: Position store — active-holders state for the add/edit form

**Files:**
- Modify: `Hrms--Web-application---front-end---v1/src/app/modules/organization/state/position.store.ts`
- Test: `position.store.spec.ts`

**Interfaces:**
- Consumes: `PositionApiService.getActiveHolders` (Task 6).
- Produces: `PositionStore.coverageOwnerHolders` (signal, `ActiveHolder[]`), `PositionStore.loadCoverageOwnerHolders(legalEntityId, positionId)` method — consumed by Task 8's modal.

- [ ] **Step 1: Write the failing test**

```typescript
it('loadCoverageOwnerHolders populates coverageOwnerHolders', async () => {
  positionApiSpy.getActiveHolders.and.returnValue(of([
    { employeeId: 'a', firstName: 'A', lastName: 'One', workEmail: 'a@acme.test', avatarFileId: null },
    { employeeId: 'b', firstName: 'B', lastName: 'Two', workEmail: 'b@acme.test', avatarFileId: null },
  ]));

  await store.loadCoverageOwnerHolders('legal-entity-1', 'position-1');

  expect(store.coverageOwnerHolders().length).toBe(2);
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npx ng test --include='**/position.store.spec.ts'`
Expected: FAIL.

- [ ] **Step 3: Add the state**

In `position.store.ts`, following the file's existing `coverageRecords`/`coverageLoading` state shape, add `coverageOwnerHolders: readonly ActiveHolder[]` to the store's state interface and initial state (empty array), and a method:

```typescript
async loadCoverageOwnerHolders(
  store: /* whatever the store's internal type param is called in this file's withMethods signature */,
  api: PositionApiService,
  legalEntityId: string,
  positionId: string
): Promise<void> {
  try {
    const holders = await firstValueFrom(api.getActiveHolders(legalEntityId, positionId));
    patchState(store, { coverageOwnerHolders: holders });
  } catch {
    patchState(store, { coverageOwnerHolders: [] });
  }
}
```

(Match this exactly to the file's actual `withMethods`/`patchState` structure and naming conventions — the sketch above shows the logic, not the file's precise NgRx Signal Store method-registration syntax, which should mirror the existing `loadCoverage` method right above it.)

- [ ] **Step 4: Run the test to verify it passes**

Run: `npx ng test --include='**/position.store.spec.ts'`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/organization/state/position.store.ts src/app/modules/organization/state/position.store.spec.ts
git commit -m "feat: add coverageOwnerHolders state to PositionStore"
```

---

## Task 8: Position Coverage modal — "Responsible person" picker and display

**Files:**
- Modify: `Hrms--Web-application---front-end---v1/src/app/modules/organization/ui/position-coverage-modal/position-coverage-modal.component.ts`
- Modify: `Hrms--Web-application---front-end---v1/src/app/modules/organization/ui/position-coverage-modal/position-coverage-modal.component.html`
- Test: `position-coverage-modal.component.spec.ts`

**Interfaces:**
- Consumes: `ActiveHolder[]` via a new `@Input() ownerHolders` (populated by the parent from `PositionStore.coverageOwnerHolders`, following this component's existing pattern of receiving all data as `@Input`s rather than injecting the store directly — matches how `coverageRecords`/`targetOccupancy` already arrive).

- [ ] **Step 1: Write the failing tests**

```typescript
it('shows the Responsible person picker only when the owner position has multiple holders', () => {
  component.ownerHolders = [
    { employeeId: 'a', firstName: 'A', lastName: 'One', workEmail: 'a@acme.test', avatarFileId: null },
    { employeeId: 'b', firstName: 'B', lastName: 'Two', workEmail: 'b@acme.test', avatarFileId: null },
  ];
  component.toggleAddForm();
  fixture.detectChanges();

  expect(fixture.nativeElement.querySelector('[data-testid="responsible-person-picker"]')).toBeTruthy();
});

it('hides the Responsible person picker when the owner position has a single holder', () => {
  component.ownerHolders = [{ employeeId: 'a', firstName: 'A', lastName: 'One', workEmail: 'a@acme.test', avatarFileId: null }];
  component.toggleAddForm();
  fixture.detectChanges();

  expect(fixture.nativeElement.querySelector('[data-testid="responsible-person-picker"]')).toBeFalsy();
});

it('includes responsibleEmployeeId in the emitted addCoverage payload when the picker is shown and a person is chosen', () => {
  component.ownerHolders = [
    { employeeId: 'a', firstName: 'A', lastName: 'One', workEmail: 'a@acme.test', avatarFileId: null },
    { employeeId: 'b', firstName: 'B', lastName: 'Two', workEmail: 'b@acme.test', avatarFileId: null },
  ];
  component.toggleAddForm();
  component.addForm.controls.coveredDepartmentId.setValue('dept-1');
  component.onResponsiblePersonChange('a');
  fixture.detectChanges();

  const emitted: AddCoverageRecordRequest[] = [];
  component.addCoverage.subscribe((e: AddCoverageRecordRequest) => emitted.push(e));
  component.onAddSubmit();

  expect(emitted[0].responsibleEmployeeId).toBe('a');
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `npx ng test --include='**/position-coverage-modal.component.spec.ts'`
Expected: FAIL.

- [ ] **Step 3: Add the input, form control, and computed to the component**

In `position-coverage-modal.component.ts`:

```typescript
@Input() ownerHolders: readonly ActiveHolder[] = [];

get needsResponsiblePersonPick(): boolean {
  return this.ownerHolders.length > 1;
}
```

Extend `addForm` with a new control:
```typescript
readonly addForm = this.fb.group({
  coveredTargetType: ['Position' as 'Position' | 'Department', [Validators.required]],
  coveredPositionId: [''],
  coveredDepartmentId: [''],
  ownerOrder: [1, [Validators.required]],
  responsibleEmployeeId: ['']
});
```

Add a handler:
```typescript
onResponsiblePersonChange(employeeId: string): void {
  this.addForm.controls.responsibleEmployeeId.setValue(employeeId);
}
```

In `onAddSubmit()`, add a guard before the existing `usedOrders` check:
```typescript
if (this.needsResponsiblePersonPick && !this.addForm.controls.responsibleEmployeeId.value) {
  this.addForm.controls.responsibleEmployeeId.markAsTouched();
  return;
}
```

And include it in the emitted payload:
```typescript
this.addCoverage.emit({
  coveredTargetType: type === 'Department' ? 'Department' : 'Position',
  coveredPositionId: type === 'Position' ? posId : null,
  coveredDepartmentId: type === 'Department' ? deptId : null,
  ownerOrder,
  responsibleEmployeeId: this.needsResponsiblePersonPick ? this.addForm.controls.responsibleEmployeeId.value : null
});
```

Reset `responsibleEmployeeId` to `''` alongside the other fields in the existing `ngOnChanges`'s success-path `addForm.reset({...})` call.

- [ ] **Step 4: Add the picker and display to the template**

In `position-coverage-modal.component.html`, inside the add form's `pcm-form-grid`, after the `Responsibility Level` field:

```html
@if (needsResponsiblePersonPick) {
  <div class="pcm-field pcm-field--full" data-testid="responsible-person-picker">
    <app-select
      label="Responsible person *"
      [options]="ownerHolders | keyvalue" 
      [value]="addForm.controls.responsibleEmployeeId.value || ''"
      (valueChange)="onResponsiblePersonChange($event)"
    />
    <p class="pcm-field-hint">Multiple people hold this position — choose who is responsible for this coverage.</p>
  </div>
}
```

(Replace the `[options]` binding with a proper `SelectOption[]` mapping — e.g. add a small `get responsiblePersonOptions(): SelectOption[]` computed on the component returning `this.ownerHolders.map(h => ({ value: h.employeeId, label: \`${h.firstName} ${h.lastName}\` }))`, and bind `[options]="responsiblePersonOptions"` instead of the `keyvalue` pipe sketch above, which doesn't fit `ActiveHolder[]`.)

For existing coverage rows, in the manual-records card (`pcm-card`), add inline display near the existing `pcm-responsibility-chip`:

```html
@if (cov.responsibleEmployeeName) {
  <span class="pcm-responsible-person">{{ cov.responsibleEmployeeName }}</span>
} @else if (isAmbiguousOwner(cov)) {
  <span class="pcm-self-warning" title="Multiple people hold this position and no responsible person is set.">
    Needs a responsible person
  </span>
}
```

Add the small helper referenced above:
```typescript
isAmbiguousOwner(record: ManagementCoverageRecordResponse): boolean {
  return !record.responsibleEmployeeName && this.ownerHolders.length > 1 && record.ownerPositionId === this.position.id;
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `npx ng test --include='**/position-coverage-modal.component.spec.ts'`
Expected: PASS

- [ ] **Step 6: Wire the parent to fetch and pass `ownerHolders`**

Find wherever `PositionCoverageModalComponent` is instantiated (its parent — likely `position-management.component.ts`, per earlier research) and add a call to `store.loadCoverageOwnerHolders(legalEntityId, position.id)` alongside the existing `store.loadCoverage(...)` call already made when the modal opens, then bind `[ownerHolders]="store.coverageOwnerHolders()"` on the `<app-position-coverage-modal>` element.

- [ ] **Step 7: Commit**

```bash
git add src/app/modules/organization/ui/position-coverage-modal/ src/app/modules/organization/feature/position-management/
git commit -m "feat: add Responsible person picker to Position Coverage modal"
```

---

## Self-Review Notes

- **Spec coverage**: §5 (data model + staleness) → Task 1, Task 3/4's "ignore when unique" tests. §6 (resolution query) → Task 2. §7 (UI) → Task 8. §3's explicit out-of-scope items (no approval consumer, no backup-fallback automation) — no task builds either; the resolution query returns a flat ordered list and stops there, per design.
- **Corrections applied during planning**: confirmed via direct code reads (not the design doc's assumptions) that `ListActiveCoverageByCoveredTargetAsync` and `GetByIdsAsync` already exist on `IPositionRepository` (backing the existing `GetCoverageByTargetQueryHandler`) and are reused directly in Task 2 rather than reimplemented. Also discovered and documented (Global Constraints) that the frontend's existing `listCoverageByTarget` call has no backend implementation anywhere — a pre-existing, unrelated gap, explicitly left alone.
- **Type consistency**: `ResponsibleEmployeeId` (C#) / `responsibleEmployeeId` (TS/wire) used consistently. `PositionActiveHolder` (Task 2/3/4, repository layer, already exists) vs. `ActiveHolder` (Task 6, frontend model) are intentionally distinct types across the layer boundary, matching this repo's existing Domain/Contracts/frontend-model separation (same pattern documented in the reporting-manager plan's self-review).
- **Dependency note for whoever executes this**: Task 2's controller route (`coverage/resolve`) and Task 5-8 both assume the reporting-manager plan's Task 7 endpoint (`.../positions/{id}/active-holders`) is already merged — verified present on the current branch before this plan was written (see Global Constraints).
