# Cross-Legal-Entity Invitation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a person who's already an employee in one legal entity be invited into a *different* legal entity under the same tenant, without setting a second password — they accept via the emailed link and keep using their existing account.

**Architecture:** Three onboarding handlers (`SaveOnboardingDraftCommandHandler`, `FinalizeOnboardingDraftCommandHandler`, `ApproveAccessGrantRequestCommandHandler`) currently call `IEmployeeRepository.EmailExistsAsync(tenantId, email, excludeId)`, which blocks a duplicate email **anywhere in the tenant**, regardless of legal entity. This plan narrows that check to be legal-entity-scoped. Once narrowed, the existing (already-implemented, already-working) `_userRepository.GetByTenantAndEmailAsync(...)` reuse logic in `FinalizeOnboardingDraftCommandHandler`/`ApproveAccessGrantRequestCommandHandler` naturally picks up the existing `User` row and attaches a new `Employee` row in the target legal entity to it — no new "reuse" logic needs to be written, only the blocking check needs to move out of the way. The one genuinely new piece is `AcceptEmployeeInvitationCommandHandler`: today it unconditionally resets the user's password on every accept, which would silently overwrite a returning user's real password. This plan makes password-setting conditional on the user not already having active credentials.

**Tech Stack:** .NET (C#), EF Core, MediatR, FluentValidation, xUnit + Moq, Testcontainers.

## Global Constraints

- This plan depends on Part 1 (`part-1-invitation-capacity-lifecycle.md`) being implemented first — it reuses `IPositionAssignmentRepository.TryReservePositionAssignmentAsync`/`ActivatePlannedAsync` and `InvitationToken.PositionAssignmentId` from that plan without modification.
- `Employee.EmployeeNumber` stays tenant-unique (checked via `EmployeeNumberExistsAsync`, unchanged by this plan) — a person invited into a second legal entity still needs a distinct employee number there. This is a deliberate scope limit: renumbering rules are not part of this feature.
- Do not touch `AcceptInvitationPasswordCommandHandler` or `AcceptInvitationGoogleCommandHandler` — those serve the generic tenant-owner/platform invite flow, a different `Purpose`, out of scope.
- Snake_case DB column naming (EF Core convention already configured project-wide).

---

### Task 1: Legal-entity-scoped duplicate-email check

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/Employee/RepositoryInterfaces/IEmployeeRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/EfEmployeeRepository.cs`
- Test: `tests/ONEVO.Tests.Integration/CoreHr/Employee/EmployeeExistsInLegalEntityAsyncTests.cs` (create)

**Interfaces:**
- Produces: `Task<bool> EmployeeExistsInLegalEntityAsync(Guid tenantId, Guid legalEntityId, string email, Guid? excludeId, CancellationToken ct = default)` — same semantics as the existing `EmailExistsAsync`, but scoped to one legal entity instead of the whole tenant.

- [ ] **Step 1: Write the failing integration test**

Create `tests/ONEVO.Tests.Integration/CoreHr/Employee/EmployeeExistsInLegalEntityAsyncTests.cs`. Match this repo's existing integration-test base-class/seeding-helper pattern (read an existing file under `tests/ONEVO.Tests.Integration/CoreHr/Employee/` first, e.g. any existing employee list/detail integration test, and copy its setup shape exactly):

```csharp
using ONEVO.Infrastructure.Persistence.Repositories;
using Xunit;

namespace ONEVO.Tests.Integration.CoreHr.Employee;

public class EmployeeExistsInLegalEntityAsyncTests : IntegrationTestBase // adjust to the real base class name
{
    [Fact]
    public async Task ReturnsTrue_WhenEmployeeWithEmailExistsInThatLegalEntity()
    {
        var tenantId = await SeedTenantAsync();
        var legalEntityId = await SeedLegalEntityAsync(tenantId);
        await SeedEmployeeAsync(tenantId, legalEntityId, email: "person@example.com");

        var repo = new EfEmployeeRepository(Db);
        var exists = await repo.EmployeeExistsInLegalEntityAsync(tenantId, legalEntityId, "person@example.com", excludeId: null);

        Assert.True(exists);
    }

    [Fact]
    public async Task ReturnsFalse_WhenEmployeeWithEmailExistsOnlyInADifferentLegalEntity()
    {
        var tenantId = await SeedTenantAsync();
        var legalEntityA = await SeedLegalEntityAsync(tenantId);
        var legalEntityB = await SeedLegalEntityAsync(tenantId);
        await SeedEmployeeAsync(tenantId, legalEntityA, email: "person@example.com");

        var repo = new EfEmployeeRepository(Db);
        var exists = await repo.EmployeeExistsInLegalEntityAsync(tenantId, legalEntityB, "person@example.com", excludeId: null);

        Assert.False(exists);
    }

    [Fact]
    public async Task IsCaseInsensitive()
    {
        var tenantId = await SeedTenantAsync();
        var legalEntityId = await SeedLegalEntityAsync(tenantId);
        await SeedEmployeeAsync(tenantId, legalEntityId, email: "Person@Example.com");

        var repo = new EfEmployeeRepository(Db);
        var exists = await repo.EmployeeExistsInLegalEntityAsync(tenantId, legalEntityId, "person@example.com", excludeId: null);

        Assert.True(exists);
    }
}
```

Adjust `SeedTenantAsync`/`SeedLegalEntityAsync`/`SeedEmployeeAsync` to this repo's actual helper names — find them in an existing integration test file first.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~EmployeeExistsInLegalEntityAsync"`
Expected: FAIL (build error — method doesn't exist)

- [ ] **Step 3: Add the interface method**

In `IEmployeeRepository.cs`, add directly under the existing `EmailExistsAsync` line:

```csharp
    Task<bool> EmailExistsAsync(Guid tenantId, string email, Guid? excludeId, CancellationToken ct = default);

    /// <summary>Legal-entity-scoped duplicate check, used by onboarding to allow the same person
    /// to be invited into a different legal entity under the same tenant. EmailExistsAsync above
    /// stays tenant-wide and is retained for any caller that genuinely needs that broader check.</summary>
    Task<bool> EmployeeExistsInLegalEntityAsync(
        Guid tenantId, Guid legalEntityId, string email, Guid? excludeId, CancellationToken ct = default);
```

- [ ] **Step 4: Implement it**

First read `EfEmployeeRepository.cs`'s existing `EmailExistsAsync` implementation (around line 218) to match its exact normalization (lowercase/trim) approach, then add immediately after it:

```csharp
    public async Task<bool> EmployeeExistsInLegalEntityAsync(
        Guid tenantId, Guid legalEntityId, string email, Guid? excludeId, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var query = _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId
                && e.LegalEntityId == legalEntityId
                && e.Email.ToLower() == normalized);

        if (excludeId is Guid id)
            query = query.Where(e => e.Id != id);

        return await query.AnyAsync(ct);
    }
```

(If the existing `EmailExistsAsync` uses a different normalization approach — e.g. a case-insensitive collation instead of `.ToLower()` in the query — match that approach instead, for consistency within the same file.)

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~EmployeeExistsInLegalEntityAsync"`
Expected: PASS (all 3 tests)

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Employee/RepositoryInterfaces/IEmployeeRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/EfEmployeeRepository.cs tests/ONEVO.Tests.Integration/CoreHr/Employee/EmployeeExistsInLegalEntityAsyncTests.cs
git commit -m "feat: add legal-entity-scoped employee duplicate-email check"
```

---

### Task 2: Switch the 3 onboarding call sites to the legal-entity-scoped check

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Commands/SaveOnboardingDraft/SaveOnboardingDraftCommandHandler.cs:73`
- Modify: `src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Commands/FinalizeOnboardingDraft/FinalizeOnboardingDraftCommandHandler.cs:184`
- Modify: `src/ONEVO.Application/Features/CoreHr/Onboarding/Commands/ApproveAccessGrantRequest/ApproveAccessGrantRequestCommandHandler.cs:179`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/ApproveAccessGrantRequestCommandHandlerTests.cs`, `tests/ONEVO.Tests.Unit/Features/CoreHr/OnboardingDrafts/FinalizeOnboardingDraftCommandHandlerTests.cs`, and this file's `SaveOnboardingDraft` test counterpart (find it: `tests/ONEVO.Tests.Unit/Features/CoreHr/OnboardingDrafts/SaveOnboardingDraftCommandHandlerTests.cs` or similar)

**Interfaces:**
- Consumes: `IEmployeeRepository.EmployeeExistsInLegalEntityAsync(...)` from Task 1.

- [ ] **Step 1: Write the failing unit test (`ApproveAccessGrantRequestCommandHandler`)**

Add to `ApproveAccessGrantRequestCommandHandlerTests.cs`:

```csharp
    [Fact]
    public async Task Handle_EmployeeExistsInDifferentLegalEntity_DoesNotBlock()
    {
        // Arrange: the draft's WorkEmail already belongs to an Employee in a DIFFERENT
        // legal entity than draft.LegalEntityId. The old EmailExistsAsync(tenantId, email, ...)
        // would have blocked this; the new legal-entity-scoped check must not.
        _employeeRepository
            .Setup(r => r.EmployeeExistsInLegalEntityAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // no existing Employee in *this* target legal entity

        var result = await _handler.Handle(BuildValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _employeeRepository.Verify(
            r => r.EmailExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never); // the old tenant-wide check must no longer be called from this handler
    }

    [Fact]
    public async Task Handle_EmployeeExistsInSameLegalEntity_ReturnsConflict()
    {
        _employeeRepository
            .Setup(r => r.EmployeeExistsInLegalEntityAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _handler.Handle(BuildValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }
```

Repeat the equivalent pair of tests in `FinalizeOnboardingDraftCommandHandlerTests.cs` and the `SaveOnboardingDraft` test file, adjusted to each file's own mock/builder helper names.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ApproveAccessGrantRequestCommandHandlerTests|FullyQualifiedName~FinalizeOnboardingDraftCommandHandlerTests|FullyQualifiedName~SaveOnboardingDraftCommandHandlerTests"`
Expected: FAIL (mock never called — handlers still call the old `EmailExistsAsync`)

- [ ] **Step 3: `ApproveAccessGrantRequestCommandHandler` — replace the check**

Replace:

```csharp
        if (await _employeeRepository.EmailExistsAsync(tenantId, draft.WorkEmail, excludeId: null, ct))
            return Result<ApproveAccessGrantRequestResponse>.Conflict("An employee with this work email already exists.");
```

with:

```csharp
        if (await _employeeRepository.EmployeeExistsInLegalEntityAsync(tenantId, draft.LegalEntityId, draft.WorkEmail, excludeId: null, ct))
            return Result<ApproveAccessGrantRequestResponse>.Conflict("An employee with this work email already exists in this company.");
```

- [ ] **Step 4: `FinalizeOnboardingDraftCommandHandler` — replace the check**

Replace:

```csharp
        if (await _employeeRepository.EmailExistsAsync(tenantId, draft.WorkEmail, excludeId: null, ct))
            return Result<FinalizeOnboardingDraftResponse>.Conflict("An employee with this work email already exists.");
```

with:

```csharp
        if (await _employeeRepository.EmployeeExistsInLegalEntityAsync(tenantId, draft.LegalEntityId, draft.WorkEmail, excludeId: null, ct))
            return Result<FinalizeOnboardingDraftResponse>.Conflict("An employee with this work email already exists in this company.");
```

- [ ] **Step 5: `SaveOnboardingDraftCommandHandler` — replace the check**

Open the file, confirm the field carrying the draft's target legal entity on `request` (it should be `request.LegalEntityId` — verify by reading the command's other usages in this handler before editing). Replace:

```csharp
        if (await _employeeRepository.EmailExistsAsync(_currentUser.TenantId, request.WorkEmail, excludeId: null, ct))
```

with:

```csharp
        if (await _employeeRepository.EmployeeExistsInLegalEntityAsync(_currentUser.TenantId, request.LegalEntityId, request.WorkEmail, excludeId: null, ct))
```

and update the accompanying error message the same way as Steps 3-4 ("...already exists in this company.").

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ApproveAccessGrantRequestCommandHandlerTests|FullyQualifiedName~FinalizeOnboardingDraftCommandHandlerTests|FullyQualifiedName~SaveOnboardingDraftCommandHandlerTests"`
Expected: PASS (all tests — every pre-existing test that mocked `EmailExistsAsync` in these 3 handlers needs its mock switched to `EmployeeExistsInLegalEntityAsync`, or it will now fail since the handler no longer calls the old method)

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Commands/SaveOnboardingDraft/SaveOnboardingDraftCommandHandler.cs src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Commands/FinalizeOnboardingDraft/FinalizeOnboardingDraftCommandHandler.cs src/ONEVO.Application/Features/CoreHr/Onboarding/Commands/ApproveAccessGrantRequest/ApproveAccessGrantRequestCommandHandler.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/ApproveAccessGrantRequestCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/CoreHr/OnboardingDrafts/FinalizeOnboardingDraftCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/CoreHr/OnboardingDrafts/SaveOnboardingDraftCommandHandlerTests.cs
git commit -m "feat: scope onboarding duplicate-email check to the target legal entity"
```

**Note:** no change is needed to the existing `_userRepository.GetByTenantAndEmailAsync(...)`-based user-reuse logic already present in `FinalizeOnboardingDraftCommandHandler` and `ApproveAccessGrantRequestCommandHandler` (both create-or-reuse a `User` row, then always create a fresh `Employee` row for the current legal entity). That logic already does exactly the right thing once the blocking check above is out of the way — it was unreachable for the cross-entity case before this task, not incorrect.

---

### Task 3: Passwordless accept for a returning user

**Files:**
- Modify: `src/ONEVO.Application/Features/Auth/Invite/Commands/AcceptEmployeeInvitation/AcceptEmployeeInvitationCommandValidator.cs`
- Modify: `src/ONEVO.Application/Features/Auth/Invite/Commands/AcceptEmployeeInvitation/AcceptEmployeeInvitationCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Auth/Invite/AcceptEmployeeInvitationCommandHandlerTests.cs`

**Interfaces:**
- No new dependency. Behavior change only: password is set only when the resolved `User` doesn't already have active credentials.

- [ ] **Step 1: Write the failing unit tests**

Add to `AcceptEmployeeInvitationCommandHandlerTests.cs`:

```csharp
    [Fact]
    public async Task Handle_ReturningUserWithExistingCredentials_DoesNotOverwritePassword()
    {
        var existingHash = "already-set-hash";
        var user = BuildActiveUserWithPassword(existingHash); // adjust to this file's user-builder helper; set User.IsActive = true, User.PasswordHash = existingHash directly if no helper exists
        _users.Setup(u => u.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var invitation = BuildValidPendingInvitation(user.Id); // adjust to this file's invitation-builder helper
        _invitations.Setup(i => i.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(invitation);

        var command = new AcceptEmployeeInvitationCommand(
            "raw-token", Password: "", ConfirmPassword: "", Acceptances: new List<LegalAcceptanceItemInput>(),
            IpAddress: "127.0.0.1", UserAgent: "test");
        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(existingHash, user.PasswordHash); // unchanged
    }

    [Fact]
    public async Task Handle_BrandNewUser_NoPasswordSubmitted_ReturnsFailure()
    {
        var user = BuildInactiveUserWithNoPassword(); // adjust to this file's helper; User.IsActive = false, User.PasswordHash = string.Empty
        _users.Setup(u => u.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var invitation = BuildValidPendingInvitation(user.Id);
        _invitations.Setup(i => i.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(invitation);

        var command = new AcceptEmployeeInvitationCommand(
            "raw-token", Password: "", ConfirmPassword: "", Acceptances: new List<LegalAcceptanceItemInput>(),
            IpAddress: "127.0.0.1", UserAgent: "test");
        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("A password is required to accept this invitation.", result.Error);
    }

    [Fact]
    public async Task Handle_BrandNewUser_PasswordSubmitted_SetsPasswordAsToday()
    {
        var user = BuildInactiveUserWithNoPassword();
        _users.Setup(u => u.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var invitation = BuildValidPendingInvitation(user.Id);
        _invitations.Setup(i => i.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(invitation);
        _passwordHasher.Setup(h => h.Hash("NewPassw0rd!")).Returns("new-hash");

        var command = new AcceptEmployeeInvitationCommand(
            "raw-token", Password: "NewPassw0rd!", ConfirmPassword: "NewPassw0rd!",
            Acceptances: new List<LegalAcceptanceItemInput>(), IpAddress: "127.0.0.1", UserAgent: "test");
        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("new-hash", user.PasswordHash);
        Assert.True(user.IsActive);
    }
```

Adjust helper method names to whatever this test file's existing conventions actually are — read the file first.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~AcceptEmployeeInvitationCommandHandlerTests"`
Expected: FAIL (the handler currently always sets the password unconditionally, and never fails on an empty one — the validator would reject an empty password before the handler even runs, since `ApplyPasswordPolicy()` is currently unconditional; fix the validator in Step 3 before these tests can even reach the handler)

- [ ] **Step 3: Relax the validator to allow an empty password conditionally**

In `AcceptEmployeeInvitationCommandValidator.cs`, replace:

```csharp
        RuleFor(x => x.Password).ApplyPasswordPolicy();
        RuleFor(x => x.ConfirmPassword).NotEmpty();
        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.Password)
            .WithMessage("Password and confirmation must match.");
```

with:

```csharp
        // Password is optional at the validator level - a returning user (already has active
        // credentials from a prior invitation/account) accepts without submitting one. The
        // handler enforces "a password IS required" for a brand-new user; that decision needs
        // the loaded User row, which isn't available at validation time. When a password IS
        // submitted, the usual strength/confirmation rules still apply.
        RuleFor(x => x.Password).ApplyPasswordPolicy().When(x => !string.IsNullOrEmpty(x.Password));
        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.Password)
            .WithMessage("Password and confirmation must match.")
            .When(x => !string.IsNullOrEmpty(x.Password));
```

- [ ] **Step 4: Make password-setting conditional in the handler**

In `AcceptEmployeeInvitationCommandHandler.cs`, replace:

```csharp
        user.PasswordHash = _passwordHasher.Hash(request.Password);
        user.IsActive = true;
        user.MustChangePassword = false;
        user.PasswordSetByAdmin = false;
        user.TemporaryPasswordExpiresAt = null;
        user.UpdatedAt = now;
```

with:

```csharp
        var isReturningUser = user.IsActive && !string.IsNullOrEmpty(user.PasswordHash);
        if (!isReturningUser)
        {
            if (string.IsNullOrEmpty(request.Password))
                return Result<LoginResponseDto>.Failure("A password is required to accept this invitation.", 400);

            user.PasswordHash = _passwordHasher.Hash(request.Password);
            user.IsActive = true;
            user.MustChangePassword = false;
            user.PasswordSetByAdmin = false;
            user.TemporaryPasswordExpiresAt = null;
            user.UpdatedAt = now;
        }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~AcceptEmployeeInvitationCommandHandlerTests"`
Expected: PASS (all tests, including every pre-existing one — the pre-existing "happy path" test that submits a real password against a brand-new user must still pass unchanged, since that's the `!isReturningUser` branch preserved verbatim)

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/Auth/Invite/Commands/AcceptEmployeeInvitation/AcceptEmployeeInvitationCommandValidator.cs src/ONEVO.Application/Features/Auth/Invite/Commands/AcceptEmployeeInvitation/AcceptEmployeeInvitationCommandHandler.cs tests/ONEVO.Tests.Unit/Features/Auth/Invite/AcceptEmployeeInvitationCommandHandlerTests.cs
git commit -m "feat: skip password reset when accepting invitation with existing credentials"
```

---

### Task 4: Surface "requires password" on the invitation preview

**Files:**
- Modify: `src/ONEVO.Application/Features/Auth/Invite/Queries/GetInvitationByToken/GetInvitationByTokenQueryHandler.cs`
- Modify: `src/ONEVO.Application/Features/Auth/Invite/Mappers/InviteMapper.cs`
- Test: find and extend this handler's existing unit test file (search `tests/ONEVO.Tests.Unit` for `GetInvitationByTokenQueryHandlerTests`)

**Interfaces:**
- Produces: a new `RequiresPassword` (bool) field on the invitation preview response returned by `GET /api/v1/auth/invitations/{token}` — `true` unless the linked `User` is already active with a set password hash. The frontend's accept page (not part of this plan) uses this to decide whether to render a password field.

- [ ] **Step 1: Read the current preview handler and mapper first**

Open `GetInvitationByTokenQueryHandler.cs` and `InviteMapper.cs`. Confirm the exact name of the response DTO (`InvitationPreviewDto` per earlier investigation, but verify) and whether the handler already loads the linked `User` row (it may only load the `InvitationToken`) — if it doesn't, this task needs to add a `IUserRepository` dependency to the handler, following the same constructor-injection pattern as every other handler in this plan.

- [ ] **Step 2: Write the failing unit test**

Add to the existing `GetInvitationByTokenQueryHandlerTests.cs` (match its existing setup pattern exactly):

```csharp
    [Fact]
    public async Task Handle_ReturningUserWithExistingCredentials_SetsRequiresPasswordFalse()
    {
        // Arrange: invitation's linked User already IsActive with a non-empty PasswordHash.
        // Assert result.Value.RequiresPassword == false.
    }

    [Fact]
    public async Task Handle_BrandNewUser_SetsRequiresPasswordTrue()
    {
        // Arrange: invitation's linked User is IsActive == false (or PasswordHash empty).
        // Assert result.Value.RequiresPassword == true.
    }
```

Fill in the arrange/assert bodies once Step 1's investigation confirms the handler's exact mock/dependency shape — this task cannot be written blind without first reading the file, unlike the earlier tasks in this plan where the surrounding code was already fully read.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~GetInvitationByTokenQueryHandlerTests"`
Expected: FAIL

- [ ] **Step 4: Add the field to the response DTO and compute it**

Add `bool RequiresPassword` to the preview response record in `InviteMapper.cs`'s vicinity (wherever the DTO itself is declared — likely alongside `InvitationPreviewDto`). In the handler, compute it as:

```csharp
var requiresPassword = !(linkedUser.IsActive && !string.IsNullOrEmpty(linkedUser.PasswordHash));
```

using whichever `User` lookup the handler now has (Step 1), and pass it into the mapper call / DTO construction.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~GetInvitationByTokenQueryHandlerTests"`
Expected: PASS

- [ ] **Step 6: Run the full unit suite**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`
Expected: PASS (confirms Part 2 hasn't broken anything elsewhere)

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/Auth/Invite/Queries/GetInvitationByToken/GetInvitationByTokenQueryHandler.cs src/ONEVO.Application/Features/Auth/Invite/Mappers/InviteMapper.cs tests/ONEVO.Tests.Unit/Features/Auth/Invite/GetInvitationByTokenQueryHandlerTests.cs
git commit -m "feat: surface RequiresPassword on the invitation preview response"
```

**Note (explicitly out of scope for this plan):** the accept-invitation page's frontend UI (hiding the password field when `RequiresPassword` is `false`) is not built here — this backend-foundation plan stops at the API contract. Flag it as a small follow-up frontend task once this plan ships.

---

### Task 5: End-to-end integration test for the cross-legal-entity flow

**Files:**
- Create: `tests/ONEVO.Tests.Integration/CoreHr/Onboarding/CrossLegalEntityInvitationIntegrationTests.cs`

**Interfaces:**
- No new production code — this test exercises Tasks 1-4 together against a real (Testcontainers) database, end to end.

- [ ] **Step 1: Write the test**

Follow this repo's existing full-stack integration test pattern (e.g. whatever test class already drives a complete onboarding-draft-to-approval flow — find one under `tests/ONEVO.Tests.Integration/CoreHr/`, and copy its `WebApplicationFactory`/HTTP-client setup exactly):

```csharp
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace ONEVO.Tests.Integration.CoreHr.Onboarding;

public class CrossLegalEntityInvitationIntegrationTests : IntegrationTestBase // adjust to the real base class
{
    [Fact]
    public async Task InviteExistingTenantUserIntoSecondLegalEntity_AcceptsWithoutPassword_TwoEmployeeRowsShareOneUser()
    {
        var tenantId = await SeedTenantAsync();
        var legalEntityA = await SeedLegalEntityAsync(tenantId);
        var legalEntityB = await SeedLegalEntityAsync(tenantId);

        // 1. Onboard and approve a person into legal entity A the normal way (existing flow,
        //    reuse whatever helper an existing full onboarding integration test already uses).
        var (userId, employeeIdA) = await OnboardAndApproveEmployeeAsync(tenantId, legalEntityA, email: "shared@example.com");

        // Mark that first account fully active with a real password, simulating they already
        // accepted their first invitation before this second one is created.
        await ActivateUserWithPasswordAsync(userId, "ExistingPassw0rd!");

        // 2. Onboard the SAME email into legal entity B. Must succeed (no 409), and must resolve
        //    to the SAME userId rather than creating a second User.
        var (secondUserId, employeeIdB) = await OnboardAndApproveEmployeeAsync(tenantId, legalEntityB, email: "shared@example.com");
        Assert.Equal(userId, secondUserId);
        Assert.NotEqual(employeeIdA, employeeIdB);

        // 3. Fetch the raw invite token for the legal-entity-B invitation and confirm the
        //    preview reports RequiresPassword == false.
        var rawToken = await GetLatestInvitationRawTokenAsync(employeeIdB);
        var previewResponse = await Client.GetAsync($"/api/v1/auth/invitations/{rawToken}");
        previewResponse.EnsureSuccessStatusCode();
        var preview = await previewResponse.Content.ReadFromJsonAsync<InvitationPreviewDto>();
        Assert.False(preview!.RequiresPassword);

        // 4. Accept with an empty password - must succeed, and the original password must be
        //    unchanged (verify by logging in with it afterward, or by reading PasswordHash
        //    directly from the DB, whichever this repo's existing test helpers support).
        var acceptResponse = await Client.PostAsJsonAsync(
            $"/api/v1/auth/invitations/{rawToken}/accept-employee",
            new { Password = "", ConfirmPassword = "", Acceptances = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.OK, acceptResponse.StatusCode);

        var stillWorksWithOldPassword = await CanLoginAsync("shared@example.com", "ExistingPassw0rd!", tenantId);
        Assert.True(stillWorksWithOldPassword);
    }
}
```

`OnboardAndApproveEmployeeAsync`/`ActivateUserWithPasswordAsync`/`GetLatestInvitationRawTokenAsync`/`CanLoginAsync` are placeholders for whatever this repo's real integration-test helper methods are named — before writing this file, read at least one existing full onboarding-flow integration test to find (or build, matching its style) equivalents. Do not invent a parallel onboarding path different from what production code actually does.

- [ ] **Step 2: Run it**

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~CrossLegalEntityInvitationIntegrationTests"`
Expected: PASS. If it fails, do not weaken the test to make it pass — the failure is telling you something in Tasks 1-4 is incomplete; go back and fix the handler code.

- [ ] **Step 3: Commit**

```bash
git add tests/ONEVO.Tests.Integration/CoreHr/Onboarding/CrossLegalEntityInvitationIntegrationTests.cs
git commit -m "test: add end-to-end coverage for cross-legal-entity invitation flow"
```

---

## Part 2 done — what's next

Part 3 (`part-3-session-active-entity-permissions.md`) makes the topbar company switcher actually change the caller's effective permission set — meaningful now that Part 2 lets one person hold `Employee` rows (and therefore positions/roles) in more than one legal entity.
