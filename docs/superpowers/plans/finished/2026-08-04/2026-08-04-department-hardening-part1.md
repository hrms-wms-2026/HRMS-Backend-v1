# Department Hardening Part 1 (Code Rules, Hierarchy Safety, Archive Wording) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Harden the existing Department backend (Part 2A-2D, already code-complete) with department-code validation rules, DB-level case-insensitive code uniqueness, parent-hierarchy cycle/inactive-parent prevention, and a rename of "delete" to "archive" in public naming/messages — without touching Position APIs, headPositionId exposure, frontend, Postman, or OneVo-HR docs.

**Architecture:** Extend `IDepartmentRepository`/`EfDepartmentRepository` with two new methods (`ExistsByCodeAsync`, `IsDescendantAsync`); tighten `CreateDepartmentCommand`/`UpdateDepartmentCommand` validators and handlers to use them plus a code regex; add one raw-SQL migration for a case-insensitive expression unique index (with a duplicate precheck); rename the `DeleteDepartment` command trio to `ArchiveDepartment` and add a `POST .../archive` route while keeping `DELETE` as a documented compatibility alias to the same command.

**Tech Stack:** .NET / EF Core 10 (Npgsql provider), MediatR, FluentValidation, xUnit + Moq (unit), Testcontainers PostgreSQL (integration), PowerShell/Bash for verification commands.

## Global Constraints

- Repo root for all paths below: `C:\onevoNew\HRMS-Backend-v1` (work only in this repo).
- Do not build Position APIs. Do not modify Position schema.
- Do not expose or accept `headPositionId` in any request contract/command; it stays read-only/response-only.
- Do not accept `tenantId` in any request body; tenant is always resolved server-side from `ICurrentUser.TenantId`.
- Route stays `/api/v1/org/legal-entities/{legalEntityId:guid}/departments` (unchanged).
- Use `org:read` for GET actions, `org:manage` for mutating actions (unchanged).
- Use `IDateTimeProvider`, never `DateTimeOffset.UtcNow` directly, in any Department Application-layer file.
- Keep ASCII only in every touched source/test/report file.
- **Do not commit or push.** Every task ends with a verification step, not a commit step. Leave the working tree as-is for the user to review.
- `EfDepartmentRepository` methods stay block-bodied (no `=>` expression-bodied members) — enforced by `DepartmentPart2AArchitectureTests.EfDepartmentRepository_HasNoExpressionBodiedMembers`.
- Every new/changed `IDepartmentRepository` method that is legal-entity-scoped must have an explicit `tenantId` and `legalEntityId` parameter (matches `DepartmentPart2AArchitectureTests.IDepartmentRepository_EveryReadMethodHasATenantIdParameter` / `..._LegalEntityScopedMethods_HaveALegalEntityIdParameter`).

---

## Task 1: Repository — `ExistsByCodeAsync` (case-insensitive code duplicate check)

**Files:**
- Modify: `src\ONEVO.Application\Features\OrgStructure\Department\RepositoryInterfaces\IDepartmentRepository.cs`
- Modify: `src\ONEVO.Infrastructure\Persistence\Repositories\OrgStructure\Department\EfDepartmentRepository.cs`
- Modify: `tests\ONEVO.Tests.Unit\Features\OrgStructure\Department\EfDepartmentRepositoryTests.cs`
- Modify: `tests\ONEVO.Tests.Architecture\DepartmentPart2AArchitectureTests.cs`

**Interfaces:**
- Produces: `Task<bool> IDepartmentRepository.ExistsByCodeAsync(Guid tenantId, Guid legalEntityId, string code, Guid? excludingDepartmentId, CancellationToken ct = default)` — later tasks (Create/Update handlers) call this.

**Important:** compare with `.ToLower()` on both sides, not `EF.Functions.ILike` (Npgsql-only, breaks the `UseInMemoryDatabase` unit tests) and not `string.Equals(..., StringComparison.OrdinalIgnoreCase)` (not translatable by either provider). `.ToLower()` works on InMemory and translates to `lower(code) = lower(@code)` on Npgsql, which also means the code duplicate check and the DB expression index (Task 5) test the same predicate shape.

- [ ] **Step 1: Write the failing repository unit tests**

Append to `tests\ONEVO.Tests.Unit\Features\OrgStructure\Department\EfDepartmentRepositoryTests.cs`, right after the existing `ExistsAsync_ReturnsFalse_WhenDepartmentBelongsToAnotherLegalEntity` test (before `AddAsync_DoesNotPersistUntilSaveChangesIsCalled`):

```csharp
    [Fact]
    public async Task ExistsByCodeAsync_ReturnsTrue_WhenCodeMatchesCaseInsensitively()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var entity = CreateDepartment(tenantId, legalEntityId, "Operations");
        entity.Code = "OPS";
        db.Departments.Add(entity);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var exists = await repository.ExistsByCodeAsync(
            tenantId, legalEntityId, "ops", excludingDepartmentId: null, ct: CancellationToken.None);

        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsByCodeAsync_ReturnsFalse_WhenSameCodeOnlyExistsInAnotherLegalEntity()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var otherLegalEntityId = Guid.NewGuid();
        var entity = CreateDepartment(tenantId, otherLegalEntityId, "Operations");
        entity.Code = "OPS";
        db.Departments.Add(entity);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var exists = await repository.ExistsByCodeAsync(
            tenantId, legalEntityId, "OPS", excludingDepartmentId: null, ct: CancellationToken.None);

        Assert.False(exists);
    }

    [Fact]
    public async Task ExistsByCodeAsync_ExcludesGivenId_ForUpdateSelfCheck()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var entity = CreateDepartment(tenantId, legalEntityId, "Operations");
        entity.Code = "OPS";
        db.Departments.Add(entity);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var exists = await repository.ExistsByCodeAsync(
            tenantId, legalEntityId, "OPS", excludingDepartmentId: entity.Id, ct: CancellationToken.None);

        Assert.False(exists);
    }

    [Fact]
    public async Task ExistsByCodeAsync_ReturnsFalse_WhenNoDepartmentHasThatCode()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        db.Departments.Add(CreateDepartment(tenantId, legalEntityId, "Operations"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var exists = await repository.ExistsByCodeAsync(
            tenantId, legalEntityId, "ZZZ", excludingDepartmentId: null, ct: CancellationToken.None);

        Assert.False(exists);
    }
```

- [ ] **Step 2: Run tests to verify they fail to compile (method does not exist yet)**

Run: `dotnet build tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --verbosity minimal`
Expected: FAIL — `CS1061 'IDepartmentRepository' does not contain a definition for 'ExistsByCodeAsync'` (or similar).

- [ ] **Step 3: Add the interface method**

In `src\ONEVO.Application\Features\OrgStructure\Department\RepositoryInterfaces\IDepartmentRepository.cs`, add after `ExistsByNameAsync` (before `ExistsAsync`):

```csharp
    Task<bool> ExistsByCodeAsync(
        Guid tenantId,
        Guid legalEntityId,
        string code,
        Guid? excludingDepartmentId,
        CancellationToken ct = default);
```

- [ ] **Step 4: Implement it in `EfDepartmentRepository`**

In `src\ONEVO.Infrastructure\Persistence\Repositories\OrgStructure\Department\EfDepartmentRepository.cs`, add after `ExistsByNameAsync` (before `ExistsAsync`):

```csharp
    public async Task<bool> ExistsByCodeAsync(
        Guid tenantId,
        Guid legalEntityId,
        string code,
        Guid? excludingDepartmentId,
        CancellationToken ct = default)
    {
        var normalizedCode = code.ToLower();

        var query = _db.Departments
            .AsNoTracking()
            .Where(department =>
                department.TenantId == tenantId
                && department.LegalEntityId == legalEntityId
                && department.Code != null
                && department.Code.ToLower() == normalizedCode);

        if (excludingDepartmentId is not null)
        {
            query = query.Where(department => department.Id != excludingDepartmentId.Value);
        }

        var exists = await query.AnyAsync(ct);
        return exists;
    }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~ExistsByCodeAsync" --verbosity minimal`
Expected: PASS, 4/4.

- [ ] **Step 6: Add architecture-test coverage for the new method's scoping parameters**

In `tests\ONEVO.Tests.Architecture\DepartmentPart2AArchitectureTests.cs`, add `[InlineData(nameof(IDepartmentRepository.ExistsByCodeAsync))]` to the `[Theory]` list above `IDepartmentRepository_LegalEntityScopedMethods_HaveALegalEntityIdParameter` (alongside the existing `ListByLegalEntityAsync`/`GetByIdForLegalEntityAsync`/`ExistsByNameAsync`/`ExistsAsync` entries).

- [ ] **Step 7: Run full unit + architecture suites**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --verbosity minimal`
Run: `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --verbosity minimal`
Expected: PASS, both suites green, no regressions. Leave the working tree as-is (no commit).

---

## Task 2: Repository — `IsDescendantAsync` (recursive-CTE cycle check)

**Files:**
- Modify: `src\ONEVO.Application\Features\OrgStructure\Department\RepositoryInterfaces\IDepartmentRepository.cs`
- Modify: `src\ONEVO.Infrastructure\Persistence\Repositories\OrgStructure\Department\EfDepartmentRepository.cs`
- Modify: `tests\ONEVO.Tests.Architecture\DepartmentPart2AArchitectureTests.cs`
- Modify (later, Task 8): `tests\ONEVO.Tests.Integration\OrgStructure\Department\DepartmentsIntegrationTests.cs` (real-DB proof of the recursive walk)

**Interfaces:**
- Produces: `Task<bool> IDepartmentRepository.IsDescendantAsync(Guid tenantId, Guid legalEntityId, Guid departmentId, Guid possibleDescendantId, CancellationToken ct = default)` — returns `true` when `possibleDescendantId` is anywhere in the subtree rooted at `departmentId`'s children. Task 4's `UpdateDepartmentCommandHandler` calls this as `IsDescendantAsync(tenantId, legalEntityId, existing.Id, proposedParentId, ct)` to reject "move this department under its own descendant."

**Note:** this uses `WITH RECURSIVE`, which `UseInMemoryDatabase` cannot execute — there is no InMemory unit test for this method's SQL. It is proven at the handler level via a Moq mock (Task 4) and at the real-database level via the integration test added in Task 8, exactly as the task brief allows ("If using EF InMemory unit tests, also add handler-level mocked tests and at least one integration test for actual DB recursion").

- [ ] **Step 1: Add the interface method**

In `src\ONEVO.Application\Features\OrgStructure\Department\RepositoryInterfaces\IDepartmentRepository.cs`, add after `ExistsAsync` (before `AddAsync`):

```csharp
    Task<bool> IsDescendantAsync(
        Guid tenantId,
        Guid legalEntityId,
        Guid departmentId,
        Guid possibleDescendantId,
        CancellationToken ct = default);
```

- [ ] **Step 2: Implement it in `EfDepartmentRepository` with a recursive CTE**

In `src\ONEVO.Infrastructure\Persistence\Repositories\OrgStructure\Department\EfDepartmentRepository.cs`, add after `ExistsAsync` (before `AddAsync`):

```csharp
    public async Task<bool> IsDescendantAsync(
        Guid tenantId,
        Guid legalEntityId,
        Guid departmentId,
        Guid possibleDescendantId,
        CancellationToken ct = default)
    {
        var descendantIds = _db.Database.SqlQuery<Guid>($@"
            WITH RECURSIVE descendants AS (
                SELECT id FROM departments
                WHERE tenant_id = {tenantId} AND legal_entity_id = {legalEntityId}
                    AND parent_department_id = {departmentId}
                UNION ALL
                SELECT d.id FROM departments d
                INNER JOIN descendants ON d.parent_department_id = descendants.id
                WHERE d.tenant_id = {tenantId} AND d.legal_entity_id = {legalEntityId}
            )
            SELECT id FROM descendants
        ");

        var isDescendant = await descendantIds.AnyAsync(id => id == possibleDescendantId, ct);
        return isDescendant;
    }
```

- [ ] **Step 3: Build to confirm it compiles**

Run: `dotnet build src\ONEVO.Infrastructure\ONEVO.Infrastructure.csproj --no-restore --verbosity minimal`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Add architecture-test coverage for the new method's scoping parameters**

In `tests\ONEVO.Tests.Architecture\DepartmentPart2AArchitectureTests.cs`, add `[InlineData(nameof(IDepartmentRepository.IsDescendantAsync))]` to the same `[Theory]` list from Task 1 Step 6.

- [ ] **Step 5: Run architecture suite**

Run: `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --verbosity minimal`
Expected: PASS, no regressions. Leave the working tree as-is (no commit) — behavioral proof of the recursion comes in Tasks 4 and 8.

---

## Task 3: Code validation — regex, trim, empty-to-null (validators + handlers)

**Files:**
- Modify: `src\ONEVO.Application\Features\OrgStructure\Department\Commands\CreateDepartment\CreateDepartmentCommandValidator.cs`
- Modify: `src\ONEVO.Application\Features\OrgStructure\Department\Commands\UpdateDepartment\UpdateDepartmentCommandValidator.cs`
- Modify: `src\ONEVO.Application\Features\OrgStructure\Department\Commands\CreateDepartment\CreateDepartmentCommandHandler.cs`
- Modify: `src\ONEVO.Application\Features\OrgStructure\Department\Commands\UpdateDepartment\UpdateDepartmentCommandHandler.cs`
- Modify: `tests\ONEVO.Tests.Unit\Features\OrgStructure\Department\DepartmentApplicationUnitTests.cs`

**Interfaces:**
- Consumes: nothing new from Tasks 1-2 yet (that's Task 4).
- Produces: normalized `code` (`string.Trim()`'d, empty/whitespace becomes `null`) is what Task 4 passes into `ExistsByCodeAsync`.

**Rule:** allowed pattern is `^[A-Za-z0-9_-]{1,20}$`, applied to the **trimmed** value (matches the codebase's `Matches("^[A-Za-z0-9 _-]+$")` convention already used in `CreateRoleCommandValidator`/`UpdateRoleCommandValidator`). Casing is preserved — never uppercased.

- [ ] **Step 1: Write the failing validator/handler tests**

In `tests\ONEVO.Tests.Unit\Features\OrgStructure\Department\DepartmentApplicationUnitTests.cs`, add inside the `#region CreateDepartment` block, right after `CreateDepartment_RejectsParentFromDifferentLegalEntity`:

```csharp
    [Theory]
    [InlineData("bad code")]
    [InlineData("bad@code")]
    [InlineData("this-code-is-way-too-long-for-limit")]
    public void CreateDepartmentCommandValidator_RejectsInvalidCodeCharacters(string invalidCode)
    {
        var validator = new CreateDepartmentCommandValidator();
        var command = new CreateDepartmentCommand(Guid.NewGuid(), "Finance", invalidCode, null);

        var validationResult = validator.Validate(command);

        Assert.False(validationResult.IsValid);
        Assert.Contains(validationResult.Errors, e => e.PropertyName == nameof(CreateDepartmentCommand.Code));
    }

    [Fact]
    public async Task CreateDepartment_TrimsCode()
    {
        _departmentRepoMock
            .Setup(d => d.ExistsByNameAsync(_tenantId, _legalEntityId, "Finance", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _departmentRepoMock
            .Setup(d => d.ExistsByCodeAsync(_tenantId, _legalEntityId, "FIN", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = new CreateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new CreateDepartmentCommand(_legalEntityId, "Finance", "  FIN  ", null);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("FIN", result.Value!.Code);
    }

    [Fact]
    public async Task CreateDepartment_ConvertsWhitespaceCodeToNull()
    {
        _departmentRepoMock
            .Setup(d => d.ExistsByNameAsync(_tenantId, _legalEntityId, "Finance", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = new CreateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new CreateDepartmentCommand(_legalEntityId, "Finance", "   ", null);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Code);
        _departmentRepoMock.Verify(
            d => d.ExistsByCodeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~CreateDepartment_TrimsCode|FullyQualifiedName~CreateDepartment_ConvertsWhitespaceCodeToNull|FullyQualifiedName~RejectsInvalidCodeCharacters" --verbosity minimal`
Expected: FAIL — regex rule doesn't exist yet, so invalid-code cases currently pass validation; whitespace code is not yet converted to `null` (currently `string.IsNullOrEmpty` only, not `IsNullOrWhiteSpace`).

- [ ] **Step 3: Update `CreateDepartmentCommandValidator`**

Replace the whole file `src\ONEVO.Application\Features\OrgStructure\Department\Commands\CreateDepartment\CreateDepartmentCommandValidator.cs` with:

```csharp
using System.Text.RegularExpressions;
using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Commands.CreateDepartment;

public class CreateDepartmentCommandValidator : AbstractValidator<CreateDepartmentCommand>
{
    private static readonly Regex CodePattern = new("^[A-Za-z0-9_-]{1,20}$", RegexOptions.Compiled);

    public CreateDepartmentCommandValidator()
    {
        RuleFor(x => x.LegalEntityId)
            .NotEmpty().WithMessage("Legal entity ID is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Department name is required.")
            .MaximumLength(100).WithMessage("Department name cannot exceed 100 characters.");

        RuleFor(x => x.Code)
            .MaximumLength(20).WithMessage("Department code cannot exceed 20 characters.")
            .Must(code => CodePattern.IsMatch(code!.Trim()))
            .WithMessage("Department code may only contain letters, numbers, hyphens, and underscores.")
            .When(x => !string.IsNullOrWhiteSpace(x.Code));
    }
}
```

- [ ] **Step 4: Update `UpdateDepartmentCommandValidator`** the same way

Replace the whole file `src\ONEVO.Application\Features\OrgStructure\Department\Commands\UpdateDepartment\UpdateDepartmentCommandValidator.cs` with:

```csharp
using System.Text.RegularExpressions;
using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Commands.UpdateDepartment;

public class UpdateDepartmentCommandValidator : AbstractValidator<UpdateDepartmentCommand>
{
    private static readonly Regex CodePattern = new("^[A-Za-z0-9_-]{1,20}$", RegexOptions.Compiled);

    public UpdateDepartmentCommandValidator()
    {
        RuleFor(x => x.LegalEntityId)
            .NotEmpty().WithMessage("Legal entity ID is required.");

        RuleFor(x => x.DepartmentId)
            .NotEmpty().WithMessage("Department ID is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Department name is required.")
            .MaximumLength(100).WithMessage("Department name cannot exceed 100 characters.");

        RuleFor(x => x.Code)
            .MaximumLength(20).WithMessage("Department code cannot exceed 20 characters.")
            .Must(code => CodePattern.IsMatch(code!.Trim()))
            .WithMessage("Department code may only contain letters, numbers, hyphens, and underscores.")
            .When(x => !string.IsNullOrWhiteSpace(x.Code));

        RuleFor(x => x)
            .Must(x => x.ParentDepartmentId == null || x.ParentDepartmentId != x.DepartmentId)
            .WithMessage("Department cannot be its own parent.");
    }
}
```

- [ ] **Step 5: Update `CreateDepartmentCommandHandler`'s code normalization**

In `src\ONEVO.Application\Features\OrgStructure\Department\Commands\CreateDepartment\CreateDepartmentCommandHandler.cs`, replace:

```csharp
        var name = request.Name.Trim();
        var code = request.Code?.Trim();
```

with:

```csharp
        var name = request.Name.Trim();
        var trimmedCode = request.Code?.Trim();
        var code = string.IsNullOrEmpty(trimmedCode) ? null : trimmedCode;
```

and replace the entity initializer line `Code = string.IsNullOrEmpty(code) ? null : code,` with `Code = code,` (it is now already normalized).

- [ ] **Step 6: Update `UpdateDepartmentCommandHandler`'s code normalization**

In `src\ONEVO.Application\Features\OrgStructure\Department\Commands\UpdateDepartment\UpdateDepartmentCommandHandler.cs`, replace:

```csharp
        var name = request.Name.Trim();
        var code = request.Code?.Trim();
```

with:

```csharp
        var name = request.Name.Trim();
        var trimmedCode = request.Code?.Trim();
        var code = string.IsNullOrEmpty(trimmedCode) ? null : trimmedCode;
```

and replace `existing.Code = string.IsNullOrEmpty(code) ? null : code;` with `existing.Code = code;`.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --verbosity minimal`
Expected: PASS, full suite green (existing `CreateDepartment_Succeeds_...` and `UpdateDepartment_Succeeds_...` tests still pass since `"FIN"`/`"SWE"` already match the regex).

---

## Task 4: Case-insensitive duplicate-code rejection + active-parent lookup (Create/Update handlers)

**Files:**
- Modify: `src\ONEVO.Application\Features\OrgStructure\Department\Commands\CreateDepartment\CreateDepartmentCommandHandler.cs`
- Modify: `src\ONEVO.Application\Features\OrgStructure\Department\Commands\UpdateDepartment\UpdateDepartmentCommandHandler.cs`
- Modify: `tests\ONEVO.Tests.Unit\Features\OrgStructure\Department\DepartmentApplicationUnitTests.cs`

**Interfaces:**
- Consumes: `IDepartmentRepository.ExistsByCodeAsync` (Task 1), `IDepartmentRepository.IsDescendantAsync` (Task 2).
- Produces: handlers now return `Result<DepartmentResponse>.Conflict(...)` (409) for duplicate code, inactive parent, and circular parent; `Result<DepartmentResponse>.NotFound(...)` (404) for a missing parent — same as before, just sourced from `GetByIdForLegalEntityAsync` instead of `ExistsAsync`.

This task also replaces the parent-existence check (`_departments.ExistsAsync(...)`) with `_departments.GetByIdForLegalEntityAsync(...)` so the handler gets the full `Department` (needed to read `IsActive`) in one round trip instead of two. `IDepartmentRepository.ExistsAsync` itself is **not removed** — it stays in the interface/implementation because `DepartmentPart2AArchitectureTests` pins it by name via `[InlineData(nameof(IDepartmentRepository.ExistsAsync))]`; it simply becomes unused by these two handlers. Note this in the Part E report as intentional, not dead-code oversight.

- [ ] **Step 1: Write the failing handler tests**

In `tests\ONEVO.Tests.Unit\Features\OrgStructure\Department\DepartmentApplicationUnitTests.cs`:

1. Replace the existing `CreateDepartment_RejectsParentFromDifferentLegalEntity` test body so it mocks `GetByIdForLegalEntityAsync` instead of `ExistsAsync` (same intent, matching the handler's new call):

```csharp
    [Fact]
    public async Task CreateDepartment_RejectsParentFromDifferentLegalEntity()
    {
        var invalidParentId = Guid.NewGuid();

        _departmentRepoMock
            .Setup(d => d.ExistsByNameAsync(_tenantId, _legalEntityId, "DevOps", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, invalidParentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Domain.Features.OrgStructure.Entities.Department?)null);

        var handler = new CreateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new CreateDepartmentCommand(_legalEntityId, "DevOps", "DEV", invalidParentId);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
```

2. Add these new tests at the end of the `#region CreateDepartment` block:

```csharp
    [Fact]
    public async Task CreateDepartment_RejectsDuplicateCodeCaseInsensitivelyInSameLegalEntity()
    {
        _departmentRepoMock
            .Setup(d => d.ExistsByNameAsync(_tenantId, _legalEntityId, "Operations", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _departmentRepoMock
            .Setup(d => d.ExistsByCodeAsync(_tenantId, _legalEntityId, "ops", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = new CreateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new CreateDepartmentCommand(_legalEntityId, "Operations", "ops", null);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task CreateDepartment_AllowsSameCodeInDifferentLegalEntity()
    {
        _legalEntityRepoMock
            .Setup(l => l.GetByIdForTenantAsync(_tenantId, _otherLegalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Domain.Features.OrgStructure.Entities.LegalEntity { Id = _otherLegalEntityId, TenantId = _tenantId, Name = "Other Corp" });

        _departmentRepoMock
            .Setup(d => d.ExistsByNameAsync(_tenantId, _otherLegalEntityId, "Operations", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _departmentRepoMock
            .Setup(d => d.ExistsByCodeAsync(_tenantId, _otherLegalEntityId, "OPS", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = new CreateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new CreateDepartmentCommand(_otherLegalEntityId, "Operations", "OPS", null);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("OPS", result.Value!.Code);
    }
```

3. Add these new tests at the end of the `#region UpdateDepartment` block:

```csharp
    [Fact]
    public async Task UpdateDepartment_RejectsDuplicateCodeCaseInsensitivelyExcludingSelf()
    {
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Eng");

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _departmentRepoMock
            .Setup(d => d.ExistsByNameAsync(_tenantId, _legalEntityId, "Eng", existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _departmentRepoMock
            .Setup(d => d.ExistsByCodeAsync(_tenantId, _legalEntityId, "ops", existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = new UpdateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new UpdateDepartmentCommand(_legalEntityId, existing.Id, "Eng", "ops", null);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task UpdateDepartment_RejectsInactiveParentDepartment()
    {
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Eng");
        var inactiveParent = CreateDepartment(_tenantId, _legalEntityId, "Legacy");
        inactiveParent.IsActive = false;

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _departmentRepoMock
            .Setup(d => d.ExistsByNameAsync(_tenantId, _legalEntityId, "Eng", existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, inactiveParent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inactiveParent);

        var handler = new UpdateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new UpdateDepartmentCommand(_legalEntityId, existing.Id, "Eng", "ENG", inactiveParent.Id);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task UpdateDepartment_RejectsDescendantParentSelection()
    {
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Eng");
        var descendant = CreateDepartment(_tenantId, _legalEntityId, "Eng Sub");
        descendant.ParentDepartmentId = existing.Id;

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _departmentRepoMock
            .Setup(d => d.ExistsByNameAsync(_tenantId, _legalEntityId, "Eng", existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, descendant.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(descendant);
        _departmentRepoMock
            .Setup(d => d.IsDescendantAsync(_tenantId, _legalEntityId, existing.Id, descendant.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = new UpdateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new UpdateDepartmentCommand(_legalEntityId, existing.Id, "Eng", "ENG", descendant.Id);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~DepartmentApplicationUnitTests" --verbosity minimal`
Expected: FAIL — handlers don't call `ExistsByCodeAsync`/`IsDescendantAsync` yet and still call `ExistsAsync` for the parent, so the new mocks are never hit and the old behavior differs.

- [ ] **Step 3: Rewrite `CreateDepartmentCommandHandler.Handle`**

In `src\ONEVO.Application\Features\OrgStructure\Department\Commands\CreateDepartment\CreateDepartmentCommandHandler.cs`, replace the body from `if (await _departments.ExistsByNameAsync(...` through the `var entity = new DepartmentEntity` block's `Code = ...,` line with:

```csharp
        if (await _departments.ExistsByNameAsync(tenantId, request.LegalEntityId, name, excludingDepartmentId: null, ct))
            return Result<DepartmentResponse>.Conflict("Department name already exists in this legal entity.");

        if (code is not null
            && await _departments.ExistsByCodeAsync(tenantId, request.LegalEntityId, code, excludingDepartmentId: null, ct))
        {
            return Result<DepartmentResponse>.Conflict("Department code already exists in this legal entity.");
        }

        if (request.ParentDepartmentId is { } parentId)
        {
            var parent = await _departments.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, parentId, ct);
            if (parent is null)
                return Result<DepartmentResponse>.NotFound("Parent department not found.");
            if (!parent.IsActive)
                return Result<DepartmentResponse>.Conflict("Parent department is inactive.");
        }

        var entity = new DepartmentEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = request.LegalEntityId,
            Name = name,
            Code = code,
```

(the rest of the initializer — `ParentDepartmentId`, `IsActive`, `CreatedAt` — is unchanged).

- [ ] **Step 4: Rewrite `UpdateDepartmentCommandHandler.Handle`**

In `src\ONEVO.Application\Features\OrgStructure\Department\Commands\UpdateDepartment\UpdateDepartmentCommandHandler.cs`, replace the body from `if (await _departments.ExistsByNameAsync(...` through `existing.ParentDepartmentId = request.ParentDepartmentId;` with:

```csharp
        if (await _departments.ExistsByNameAsync(
                tenantId, request.LegalEntityId, name, excludingDepartmentId: request.DepartmentId, ct))
        {
            return Result<DepartmentResponse>.Conflict("Department name already exists in this legal entity.");
        }

        if (code is not null
            && await _departments.ExistsByCodeAsync(
                tenantId, request.LegalEntityId, code, excludingDepartmentId: request.DepartmentId, ct))
        {
            return Result<DepartmentResponse>.Conflict("Department code already exists in this legal entity.");
        }

        if (request.ParentDepartmentId is { } parentId)
        {
            var parent = await _departments.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, parentId, ct);
            if (parent is null)
                return Result<DepartmentResponse>.NotFound("Parent department not found.");
            if (!parent.IsActive)
                return Result<DepartmentResponse>.Conflict("Parent department is inactive.");

            var parentIsDescendant = await _departments.IsDescendantAsync(
                tenantId, request.LegalEntityId, existing.Id, parentId, ct);
            if (parentIsDescendant)
                return Result<DepartmentResponse>.Conflict("Cannot set parent: would create a circular hierarchy.");
        }

        // Mutate the fetched entity directly; do not construct a detached replacement.
        // HeadPositionId remains untouched.
        existing.Name = name;
        existing.Code = code;
        existing.ParentDepartmentId = request.ParentDepartmentId;
```

(leave the trailing `existing.UpdatedAt = _dateTimeProvider.UtcNow;` and everything after it unchanged.)

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --verbosity minimal`
Expected: PASS, full suite green.

---

## Task 5: DB migration — case-insensitive unique expression index on department code

**Files:**
- Modify: `src\ONEVO.Infrastructure\Persistence\Configurations\OrgStructure\Department\DepartmentConfiguration.cs`
- Create: `src\ONEVO.Infrastructure\Migrations\<timestamp>_AddDepartmentCodeCaseInsensitiveUniqueIndex.cs` (+ `.Designer.cs`, auto-generated)
- Modify (auto, via tooling): `src\ONEVO.Infrastructure\Migrations\ApplicationDbContextModelSnapshot.cs`
- Modify: `tests\ONEVO.Tests.Architecture\DepartmentPart2AArchitectureTests.cs`

The existing `ix_departments_tenant_id_legal_entity_id_code` unique index (added in `20260803085109_AddDepartments`) is **case-sensitive** — `"OPS"` and `"ops"` can both exist today at the DB layer even though Task 1-4's application-level check now blocks it. This task closes that gap at the DB layer too, using a raw-SQL expression index (Npgsql/EF Core has no fluent-API support for `lower(code)` expression indexes), per the task brief's explicit allowance for raw SQL "if needed."

- [ ] **Step 1: Remove the case-sensitive Code unique index from the EF fluent model**

In `src\ONEVO.Infrastructure\Persistence\Configurations\OrgStructure\Department\DepartmentConfiguration.cs`, delete this block entirely (it sits between the Name unique index and the ParentDepartmentId index):

```csharp
        // Code is optional; only enforce uniqueness among rows that have one set.
        builder.HasIndex(d => new { d.TenantId, d.LegalEntityId, d.Code })
            .IsUnique()
            .HasFilter("code IS NOT NULL")
            .HasDatabaseName("ix_departments_tenant_id_legal_entity_id_code");
```

Replace it with a comment (no fluent call — the constraint now lives in raw SQL, outside EF's declarative model):

```csharp
        // Case-insensitive Code uniqueness (scoped by tenant_id + legal_entity_id, ignoring
        // NULL) is enforced by a raw-SQL expression index on lower(code) — see migration
        // AddDepartmentCodeCaseInsensitiveUniqueIndex. Not modeled here: EF Core / Npgsql has
        // no fluent-API support for expression indexes, and this index intentionally is not
        // part of EF's declarative model.
```

- [ ] **Step 2: Generate the migration scaffold**

Run (from repo root, PowerShell):
```
$env:ConnectionStrings__MigrationConnection = "Host=localhost;Database=onevo_scaffold;Username=postgres;Password=postgres"
dotnet ef migrations add AddDepartmentCodeCaseInsensitiveUniqueIndex --project src\ONEVO.Infrastructure --startup-project src\ONEVO.Api
```
Expected: a new `<timestamp>_AddDepartmentCodeCaseInsensitiveUniqueIndex.cs` + `.Designer.cs` pair is created, and `ApplicationDbContextModelSnapshot.cs`'s Department block loses the `ix_departments_tenant_id_legal_entity_id_code` `HasIndex` call (verify this — don't hand-edit the snapshot). The generated `Up()` should contain a single auto-generated `migrationBuilder.DropIndex(name: "ix_departments_tenant_id_legal_entity_id_code", table: "departments");` and the generated `Down()` should contain the matching `migrationBuilder.CreateIndex(...)` that restores it.

- [ ] **Step 3: Add the precheck + expression index to `Up()`, and the reverse to `Down()`**

Open the newly generated migration file. After the auto-generated `DropIndex` call in `Up()`, add:

```csharp
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    dup_count integer;
                BEGIN
                    SELECT COUNT(*) INTO dup_count
                    FROM (
                        SELECT tenant_id, legal_entity_id, lower(code)
                        FROM departments
                        WHERE code IS NOT NULL
                        GROUP BY tenant_id, legal_entity_id, lower(code)
                        HAVING COUNT(*) > 1
                    ) duplicates;

                    IF dup_count > 0 THEN
                        RAISE EXCEPTION 'Cannot add case-insensitive unique department code index: % duplicate (tenant_id, legal_entity_id, lower(code)) group(s) exist. Resolve duplicate codes before retrying this migration.', dup_count;
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ux_departments_tenant_legal_entity_code_lower
                ON departments (tenant_id, legal_entity_id, lower(code))
                WHERE code IS NOT NULL;
            ");
```

In `Down()`, **before** the auto-generated `CreateIndex` call that restores `ix_departments_tenant_id_legal_entity_id_code`, add:

```csharp
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ux_departments_tenant_legal_entity_code_lower;");
```

- [ ] **Step 4: Add an architecture test asserting the migration's SQL shape**

In `tests\ONEVO.Tests.Architecture\DepartmentPart2AArchitectureTests.cs`, add this test after `AddDepartmentsMigration_DoesNotModifyLegalEntitiesOrPositionsOrEmployeesColumns`:

```csharp
    [Fact]
    public void CodeUniqueIndexMigration_PrechecksDuplicatesAndCreatesCaseInsensitiveExpressionIndex()
    {
        var source = ReadMigrationSourceContaining("ux_departments_tenant_legal_entity_code_lower");

        Assert.Contains("RAISE EXCEPTION", source);
        Assert.Contains("lower(code)", source);
        Assert.Contains("CREATE UNIQUE INDEX ux_departments_tenant_legal_entity_code_lower", source);
        Assert.Contains("WHERE code IS NOT NULL", source);
        Assert.Contains("DROP INDEX IF EXISTS ux_departments_tenant_legal_entity_code_lower", source);
    }
```

(This uses the distinctive new index name as the sole search fragment passed to `ReadMigrationSourceContaining`, so it locates exactly this migration file — the helper returns the *first* file matching all fragments, and no other migration mentions this name.)

- [ ] **Step 5: Build and run architecture + unit suites**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal`
Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --verbosity minimal`
Run: `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --verbosity minimal`
Expected: PASS across all three. If Task 1's `EfDepartmentRepositoryTests` still reference the old plain index anywhere, they don't — that test file only asserts `HeadPositionId` FK metadata, not the Code index, so no fallout is expected.

- [ ] **Step 6: Verify the generated SQL against a real Postgres instance (deferred to Task 9's full verification pass)**

Do not run `dotnet ef migrations script` yet — that happens once in Task 9 after all schema-adjacent work in this plan is done, so the script reflects the final state in one pass.

---

## Task 6: Rename `DeleteDepartment` to `ArchiveDepartment` (command/handler/validator)

**Files:**
- Create: `src\ONEVO.Application\Features\OrgStructure\Department\Commands\ArchiveDepartment\ArchiveDepartmentCommand.cs`
- Create: `src\ONEVO.Application\Features\OrgStructure\Department\Commands\ArchiveDepartment\ArchiveDepartmentCommandHandler.cs`
- Create: `src\ONEVO.Application\Features\OrgStructure\Department\Commands\ArchiveDepartment\ArchiveDepartmentCommandValidator.cs`
- Delete: `src\ONEVO.Application\Features\OrgStructure\Department\Commands\DeleteDepartment\DeleteDepartmentCommand.cs`
- Delete: `src\ONEVO.Application\Features\OrgStructure\Department\Commands\DeleteDepartment\DeleteDepartmentCommandHandler.cs`
- Delete: `src\ONEVO.Application\Features\OrgStructure\Department\Commands\DeleteDepartment\DeleteDepartmentCommandValidator.cs`

**Interfaces:**
- Produces: `ArchiveDepartmentCommand(Guid LegalEntityId, Guid DepartmentId) : IRequest<Result<bool>>` in namespace `ONEVO.Application.Features.OrgStructure.Commands.ArchiveDepartment` — Task 7's controller sends this from both the new `Archive` action and the retained `Delete` compatibility action.

This is a pure rename (folder, filenames, class names, namespace segment `DeleteDepartment` -> `ArchiveDepartment`); the handler's soft-deactivate logic (`IsActive = false`, `UpdatedAt = _dateTimeProvider.UtcNow`) is unchanged.

- [ ] **Step 1: Create the new `ArchiveDepartment` command trio**

Create `src\ONEVO.Application\Features\OrgStructure\Department\Commands\ArchiveDepartment\ArchiveDepartmentCommand.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.OrgStructure.Commands.ArchiveDepartment;

public record ArchiveDepartmentCommand(
    Guid LegalEntityId,
    Guid DepartmentId) : IRequest<Result<bool>>;
```

Create `src\ONEVO.Application\Features\OrgStructure\Department\Commands\ArchiveDepartment\ArchiveDepartmentCommandHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Commands.ArchiveDepartment;

public class ArchiveDepartmentCommandHandler
    : IRequestHandler<ArchiveDepartmentCommand, Result<bool>>
{
    private readonly IDepartmentRepository _departments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ArchiveDepartmentCommandHandler(
        IDepartmentRepository departments,
        ILegalEntityRepository legalEntities,
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider)
    {
        _departments = departments;
        _legalEntities = legalEntities;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<bool>> Handle(
        ArchiveDepartmentCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<bool>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<bool>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<bool>.NotFound("Legal entity not found.");

        var existing = await _departments.GetByIdForLegalEntityAsync(
            tenantId, request.LegalEntityId, request.DepartmentId, ct);
        if (existing == null)
            return Result<bool>.NotFound("Department not found.");

        // Archive is a soft-deactivation, never a physical delete: audit history and
        // reporting-hierarchy references to this row remain intact.
        existing.IsActive = false;
        existing.UpdatedAt = _dateTimeProvider.UtcNow;

        _departments.Update(existing);
        await _departments.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}
```

Create `src\ONEVO.Application\Features\OrgStructure\Department\Commands\ArchiveDepartment\ArchiveDepartmentCommandValidator.cs`:

```csharp
using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Commands.ArchiveDepartment;

public class ArchiveDepartmentCommandValidator : AbstractValidator<ArchiveDepartmentCommand>
{
    public ArchiveDepartmentCommandValidator()
    {
        RuleFor(x => x.LegalEntityId)
            .NotEmpty().WithMessage("Legal entity ID is required.");

        RuleFor(x => x.DepartmentId)
            .NotEmpty().WithMessage("Department ID is required.");
    }
}
```

- [ ] **Step 2: Delete the old `DeleteDepartment` folder**

Run: `rm -r "src\ONEVO.Application\Features\OrgStructure\Department\Commands\DeleteDepartment"` (Bash) or `Remove-Item -Recurse -Force "src\ONEVO.Application\Features\OrgStructure\Department\Commands\DeleteDepartment"` (PowerShell).

- [ ] **Step 3: Build (expect failures in dependents — fixed in Tasks 7-8)**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal`
Expected: FAIL — `DepartmentsController.cs` and test files still reference `DeleteDepartmentCommand`/`DeleteDepartmentCommandHandler`. This is expected; Task 7 fixes the controller, Task 8 fixes the tests.

---

## Task 7: Controller — add `POST .../archive`, keep `DELETE` as a documented compatibility alias

**Files:**
- Modify: `src\ONEVO.Api\Controllers\Tenant\OrgStructure\DepartmentsController.cs`

**Interfaces:**
- Consumes: `ArchiveDepartmentCommand` (Task 6).
- Produces: `Archive` action method — Task 8's `DepartmentsControllerArchitectureTests` and `DepartmentsControllerTests` assert against it by name.

- [ ] **Step 1: Replace the `using` for the old command namespace**

In `src\ONEVO.Api\Controllers\Tenant\OrgStructure\DepartmentsController.cs`, replace:

```csharp
using ONEVO.Application.Features.OrgStructure.Commands.DeleteDepartment;
```

with:

```csharp
using ONEVO.Application.Features.OrgStructure.Commands.ArchiveDepartment;
```

- [ ] **Step 2: Replace the `Delete` action and add the new `Archive` action**

Replace the entire existing `Delete` action method (from `/// <summary>Soft-deactivates a department row by setting IsActive = false.</summary>` through its closing `}`) with:

```csharp
    /// <summary>Archives a department by setting IsActive = false. This is a soft
    /// deactivation, not a physical delete - audit history and hierarchy references
    /// remain intact. Prefer this endpoint for new integrations.</summary>
    [HttpPost("{departmentId:guid}/archive")]
    [RequirePermission("org:manage")]
    public async Task<IActionResult> Archive(
        Guid legalEntityId,
        Guid departmentId,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ArchiveDepartmentCommand(legalEntityId, departmentId), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Deprecated compatibility alias for Archive (POST .../archive). Kept so
    /// existing DELETE-based integrations keep working; delegates to the same
    /// ArchiveDepartmentCommand and performs the same soft-deactivation
    /// (IsActive = false) - never a physical delete. Prefer POST .../archive for new
    /// integrations.</summary>
    [HttpDelete("{departmentId:guid}")]
    [RequirePermission("org:manage")]
    public async Task<IActionResult> Delete(
        Guid legalEntityId,
        Guid departmentId,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ArchiveDepartmentCommand(legalEntityId, departmentId), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 3: Build**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal`
Expected: FAIL only in test projects still referencing `DeleteDepartmentCommand` (fixed next in Task 8) — the API project itself should build clean.

---

## Task 8: Update existing tests + architecture tests for the Archive rename

**Files:**
- Modify: `tests\ONEVO.Tests.Unit\Features\OrgStructure\Department\DepartmentApplicationUnitTests.cs`
- Modify: `tests\ONEVO.Tests.Unit\Features\OrgStructure\Department\DepartmentsControllerTests.cs`
- Modify: `tests\ONEVO.Tests.Architecture\DepartmentPart2BArchitectureTests.cs`
- Modify: `tests\ONEVO.Tests.Architecture\DepartmentsControllerArchitectureTests.cs`
- Modify: `tests\ONEVO.Tests.Integration\OrgStructure\Department\DepartmentsIntegrationTests.cs`

- [ ] **Step 1: Rename the Delete tests in `DepartmentApplicationUnitTests.cs`**

Replace the `using ONEVO.Application.Features.OrgStructure.Commands.DeleteDepartment;` line with `using ONEVO.Application.Features.OrgStructure.Commands.ArchiveDepartment;`.

Rename the `#region DeleteDepartment` block to `#region ArchiveDepartment`, and inside it replace both tests:

```csharp
    #region ArchiveDepartment

    [Fact]
    public async Task ArchiveDepartment_DeactivatesDepartmentRow_AndUsesInjectedClockForUpdatedAt()
    {
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Legacy Ops");
        Assert.True(existing.IsActive);

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var handler = new ArchiveDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new ArchiveDepartmentCommand(_legalEntityId, existing.Id);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
        Assert.False(existing.IsActive);

        // Verify injected clock UtcNow was used for UpdatedAt
        Assert.Equal(_fixedTime, existing.UpdatedAt);

        _departmentRepoMock.Verify(d => d.Update(existing), Times.Once);
        _departmentRepoMock.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ArchiveDepartment_ReturnsNotFound_WhenDepartmentDoesNotExist()
    {
        var missingDeptId = Guid.NewGuid();

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, missingDeptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Domain.Features.OrgStructure.Entities.Department?)null);

        var handler = new ArchiveDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new ArchiveDepartmentCommand(_legalEntityId, missingDeptId);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    #endregion
```

- [ ] **Step 2: Update `DepartmentsControllerTests.cs`**

Replace `using ONEVO.Application.Features.OrgStructure.Commands.DeleteDepartment;` with `using ONEVO.Application.Features.OrgStructure.Commands.ArchiveDepartment;`.

Replace the two existing `Delete_...` tests' mock setups/assertions to reference `ArchiveDepartmentCommand` instead of `DeleteDepartmentCommand` (same method calls, `_sut.Delete(...)`, just the mediator message type changes):

```csharp
    [Fact]
    public async Task Delete_SendsCommand_WithRouteIds_AndReturnsNoContent()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ArchiveDepartmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var result = await _sut.Delete(_legalEntityId, _departmentId, CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<ArchiveDepartmentCommand>(c => c.LegalEntityId == _legalEntityId && c.DepartmentId == _departmentId),
            It.IsAny<CancellationToken>()), Times.Once);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Delete_NotFound_ReturnsProblem404()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ArchiveDepartmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.NotFound("Department not found."));

        var result = await _sut.Delete(_legalEntityId, _departmentId, CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(404);
    }
```

Add two new tests for the `Archive` action right after those:

```csharp
    [Fact]
    public async Task Archive_SendsCommand_WithRouteIds_AndReturnsNoContent()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ArchiveDepartmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var result = await _sut.Archive(_legalEntityId, _departmentId, CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<ArchiveDepartmentCommand>(c => c.LegalEntityId == _legalEntityId && c.DepartmentId == _departmentId),
            It.IsAny<CancellationToken>()), Times.Once);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Archive_NotFound_ReturnsProblem404()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ArchiveDepartmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.NotFound("Department not found."));

        var result = await _sut.Archive(_legalEntityId, _departmentId, CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(404);
    }
```

- [ ] **Step 3: Update `DepartmentPart2BArchitectureTests.cs`**

Replace `using ONEVO.Application.Features.OrgStructure.Commands.DeleteDepartment;` with `using ONEVO.Application.Features.OrgStructure.Commands.ArchiveDepartment;`.

In the `CommandAndQueryTypes` array, replace `typeof(DeleteDepartmentCommand)` with `typeof(ArchiveDepartmentCommand)`.

In the `HandlerTypes` array, replace `typeof(DeleteDepartmentCommandHandler)` with `typeof(ArchiveDepartmentCommandHandler)`.

In `CommandFiles_LiveUnderOrgStructureDepartmentFolder`'s `[InlineData]` list, replace:
```csharp
    [InlineData("Commands/DeleteDepartment", "DeleteDepartmentCommand.cs")]
    [InlineData("Commands/DeleteDepartment", "DeleteDepartmentCommandHandler.cs")]
    [InlineData("Commands/DeleteDepartment", "DeleteDepartmentCommandValidator.cs")]
```
with:
```csharp
    [InlineData("Commands/ArchiveDepartment", "ArchiveDepartmentCommand.cs")]
    [InlineData("Commands/ArchiveDepartment", "ArchiveDepartmentCommandHandler.cs")]
    [InlineData("Commands/ArchiveDepartment", "ArchiveDepartmentCommandValidator.cs")]
```

- [ ] **Step 4: Update `DepartmentsControllerArchitectureTests.cs`**

Add `using ONEVO.Application.Features.OrgStructure.Commands.ArchiveDepartment;` to the top of the file.

Add two new tests after `MutatingActions_RequireOrgManagePermission`:

```csharp
    [Fact]
    public void ArchiveRoute_ExistsAsPost_WithOrgManagePermission()
    {
        var archiveMethod = ControllerType.GetMethod(nameof(DepartmentsController.Archive));

        Assert.NotNull(archiveMethod);
        Assert.NotNull(archiveMethod!.GetCustomAttribute<HttpPostAttribute>());
        Assert.Equal("{departmentId:guid}/archive", archiveMethod.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("org:manage", GetPermission(archiveMethod));
    }

    [Fact]
    public void DeleteDepartmentCommand_TypeNoLongerExists_ArchiveWordingUsedInstead()
    {
        var assembly = typeof(ArchiveDepartmentCommand).Assembly;
        var offender = assembly.GetType("ONEVO.Application.Features.OrgStructure.Commands.DeleteDepartment.DeleteDepartmentCommand");

        Assert.Null(offender);
    }
```

- [ ] **Step 5: Run unit + architecture suites (integration tests untouched by this task; the `DELETE` route itself did not change, so `DepartmentsIntegrationTests` should already be green)**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal`
Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --verbosity minimal`
Run: `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --verbosity minimal`
Expected: PASS across all three, no regressions.

---

## Task 9: New integration tests (code rules, hierarchy safety, archive route)

**Files:**
- Modify: `tests\ONEVO.Tests.Integration\OrgStructure\Department\DepartmentsIntegrationTests.cs`

Requires Docker (Testcontainers PostgreSQL), matching the existing suite's setup. If Docker is unavailable in this environment, write the tests anyway and note in the Part E report that they were not executed locally.

- [ ] **Step 1: Add the new tests**

Append these `[Fact]` methods to `DepartmentsIntegrationTests.cs`, in the `// ── CRUD + business rules ...` region, after `HeadPositionId_IsIgnoredOnCreate_NotAcceptedFromRequestBody`:

```csharp
    [Fact]
    public async Task Create_WithCode_Returns201_AndCodeIsPreserved()
    {
        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            new { name = "Operations Dept", code = "OPS" },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await ReadJsonAsync(response);
        json.GetProperty("code").GetString().Should().Be("OPS");
    }

    [Fact]
    public async Task Create_DuplicateCodeCaseInsensitiveInSameLegalEntity_Returns409()
    {
        await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            new { name = "Original Code Dept", code = "DUPCODE" },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);

        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            new { name = "Different Name Dept", code = "dupcode" },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_SameCodeInDifferentLegalEntity_IsAllowed()
    {
        await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            new { name = "Shared Code Dept A", code = "SHARED" },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);

        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantASecondLegalEntityId}/departments",
            new { name = "Shared Code Dept B", code = "SHARED" },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_InvalidCodeCharacters_Returns400()
    {
        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            new { name = "Bad Code Dept", code = "bad code!" },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_ParentIsInactive_Returns409()
    {
        var parent = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Inactive Parent Dept");
        var parentId = parent.GetProperty("id").GetGuid();
        var archiveResponse = await SendAsync(HttpMethod.Delete, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{parentId}",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        archiveResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var child = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Child Of Inactive Parent");
        var childId = child.GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Put, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{childId}",
            new { name = "Child Of Inactive Parent", parentDepartmentId = parentId },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_ParentIsDescendant_Returns409()
    {
        var parent = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Cycle Parent Dept");
        var parentId = parent.GetProperty("id").GetGuid();

        var childResponse = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            new { name = "Cycle Child Dept", parentDepartmentId = parentId },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        childResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var childJson = await ReadJsonAsync(childResponse);
        var childId = childJson.GetProperty("id").GetGuid();

        // Attempt to make the parent report to its own child - must be blocked as a cycle.
        var response = await SendAsync(HttpMethod.Put, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{parentId}",
            new { name = "Cycle Parent Dept", parentDepartmentId = childId },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Archive_Route_SoftDeactivates_AndListExcludesByDefault()
    {
        var created = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Archive Route Dept");
        var id = created.GetProperty("id").GetGuid();

        var archive = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{id}/archive",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        archive.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{id}",
            body: null, cookie: _tenantAOwner.SessionCookie);
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var getJson = await ReadJsonAsync(get);
        getJson.GetProperty("isActive").GetBoolean().Should().BeFalse();

        var defaultList = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments");
        defaultList.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().NotContain(id);

        var inclusiveList = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments?includeInactive=true");
        inclusiveList.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Contain(id);
    }
```

- [ ] **Step 2: Run the integration suite (Docker required)**

Run: `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --filter "FullyQualifiedName~DepartmentsIntegrationTests" --logger "console;verbosity=normal" --blame-hang --blame-hang-timeout 15m`
Expected: PASS, all tests in the class green (18 pre-existing + 7 new = 25).
If Docker is unavailable: record this in the Part E report exactly as Part 2D's report did, and skip to Task 10.

---

## Task 10: Full verification pass + migration SQL sanity check

**Files:** none (verification only).

- [ ] **Step 1: Full build**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal`
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Full unit suite**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --no-build --verbosity minimal`
Expected: all green, 0 failed.

- [ ] **Step 3: Full architecture suite**

Run: `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --no-build --verbosity minimal`
Expected: all green, 0 failed.

- [ ] **Step 4: Full integration suite (if Docker available)**

Run: `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --no-build --filter "FullyQualifiedName~Department" --verbosity minimal`
Expected: all green, 0 failed.

- [ ] **Step 5: Migration SQL sanity check**

Run:
```
$env:ConnectionStrings__MigrationConnection = "Host=localhost;Database=onevo_scaffold;Username=postgres;Password=postgres"
dotnet ef migrations script --project src\ONEVO.Infrastructure --startup-project src\ONEVO.Api
```
Verify in the output: the `AddDepartmentCodeCaseInsensitiveUniqueIndex` migration drops `ix_departments_tenant_id_legal_entity_id_code`, contains the `DO $$ ... RAISE EXCEPTION ...` precheck block, and creates `ux_departments_tenant_legal_entity_code_lower` as `CREATE UNIQUE INDEX ... ON departments (tenant_id, legal_entity_id, lower(code)) WHERE code IS NOT NULL`. Confirm no RLS-related SQL for `departments` was altered or removed (the `tenant_isolation` policy from `AddDepartments` must still be present, untouched, in the full script).

- [ ] **Step 6: Git hygiene + ASCII scan**

Run: `git diff --check`
Expected: exit code 0 (clean, no whitespace conflicts).

Run (PowerShell):
```
Select-String -Path (git diff --name-only | Where-Object { Test-Path $_ }) -Pattern "[^\x00-\x7F]"
```
Expected: 0 matches (pure ASCII across every touched file).

- [ ] **Step 7: Do not commit.** Leave the working tree as-is for the user to review and commit themselves.

---

## Task 11: Write `DEPARTMENT_HARDENING_PART1_CODE_HIERARCHY_ARCHIVE_REPORT.md`

**Files:**
- Create: `DEPARTMENT_HARDENING_PART1_CODE_HIERARCHY_ARCHIVE_REPORT.md` (repo root, alongside the Part 2A-2D reports)

- [ ] **Step 1: Write the report**

Follow the structure of `DEPARTMENT_FOUNDATION_PART2C_CONTROLLER_ENDPOINTS_REPORT.md` (files read/changed tables, route table, verification results block) and cover every point the task brief's Part E requires, using the actual results from Task 10:

- **Files read**: the 6 docs/reports listed at the top of the task brief, plus every source/test file read while building this plan (Department entity, configuration, repository, interface, all Commands/Queries, controller, contracts, all Department-related test files).
- **Files changed**: full list from Tasks 1-9 (new `ArchiveDepartment` files, deleted `DeleteDepartment` files, modified handlers/validators/repository/interface/configuration/controller, new migration, modified test files).
- **Exact API routes before/after**: before = `GET/POST /departments`, `GET/PUT/DELETE /departments/{id}`; after = same four plus new `POST /departments/{id}/archive`.
- **DELETE route disposition**: kept as a documented compatibility alias, delegating to `ArchiveDepartmentCommand` (not removed) — state this explicitly, matching Task 7's controller doc comment.
- **Department code rules**: trim, empty/whitespace -> null, max 20 chars, `^[A-Za-z0-9_-]{1,20}$`, case preserved (never uppercased), case-insensitive duplicate check scoped to tenant+legal entity, same code allowed in another legal entity/tenant, null code may repeat.
- **DB-level uniqueness strategy**: raw-SQL expression unique index `ux_departments_tenant_legal_entity_code_lower` on `lower(code)`, precheck-before-create migration, reasoning for why EF fluent API couldn't express it.
- **Parent hierarchy/cycle prevention strategy**: `GetByIdForLegalEntityAsync`-based active check + `IsDescendantAsync` recursive CTE, both returning 409; self-parenting unchanged (still validator 400 / handler 409-unreachable, per the Part 2D report's finding).
- **Explicit statement**: `headPositionId` remains schema-ready only, not accepted/changed by this task (verify with `rg -n "headPositionId|HeadPositionId" src\ONEVO.Api\Contracts\OrgStructure\Departments` -> 0 matches, same as Part 2C's check).
- **Explicit statement**: Position APIs are not built in this task.
- **Tests added/updated**: enumerate from Tasks 1-2, 3-4, 8-9 with final counts from Task 10's runs.
- **Build/test results**: paste Task 10's actual output (build success, unit/architecture/integration pass counts, migration script excerpt, `git diff --check` exit code, ASCII scan result).
- **Remaining gaps mapped to the requirement doc** (`Onexo_Department_Position_User_Journey_Validation.md`): dependency archive checks (blocking archive when department has employees/positions/children — not implemented, `Archive` still unconditionally deactivates), restore archived department (no endpoint), search/sort/pagination (not implemented), position management (out of scope per task constraints), management scope (out of scope), occupant assignment (out of scope). Also note `IDepartmentRepository.ExistsAsync` is now unused by the Create/Update handlers but intentionally retained (pinned by `DepartmentPart2AArchitectureTests`' `[InlineData]`).

- [ ] **Step 2: ASCII-scan the report itself**

Run (PowerShell): `Select-String -Path DEPARTMENT_HARDENING_PART1_CODE_HIERARCHY_ARCHIVE_REPORT.md -Pattern "[^\x00-\x7F]"`
Expected: 0 matches.

- [ ] **Step 3: Final status check (no commit)**

Run: `git status`
Confirm the file list matches what Task 11 Step 1 documented as "Files changed," then stop. Do not commit or push.
