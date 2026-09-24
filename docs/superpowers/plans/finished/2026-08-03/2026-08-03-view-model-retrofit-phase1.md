# View Model Retrofit — Phase 1 (Auth Session Helper + Work Management Projects) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Retrofit the highest-leverage tenant-facing endpoints (the shared Auth session-response path used by 6 controllers, and Work Management's `ProjectsController`) to map Application-layer response DTOs into `Contracts/` view models before returning them, per the convention just documented in `ONEVO_Backend_Architecture_Document.md` §2.1.1.1 — without changing any JSON actually sent to clients.

**Architecture:** Pure `ONEVO.Api` layer change. New `ViewModel` record types + a static `{Feature}ViewModelMapper` extension-method class per feature under `src/ONEVO.Api/Contracts/`; controllers call `.ToViewModel()` on the Application DTO instead of returning it directly. No Domain/Application/Infrastructure changes, no migration.

**Tech Stack:** ASP.NET Core, System.Text.Json (`JsonPropertyName` attributes), xUnit/dotnet test for verification.

## Global Constraints

- View model type names end in `ViewModel` (never `Response`/`Dto`, to avoid confusion with same-shaped Application types).
- Mapping lives only in a static `{Feature}ViewModelMapper` class under `Contracts/{Feature}/`, as `ToViewModel()` extension methods — controllers never construct a view model inline.
- The JSON actually sent to clients must not change: every property name, `JsonPropertyName` value, casing, nesting, and nullability must exactly match what the endpoint emits today. This is a type relocation, not a wire-format change.
- Do not touch `AuthLoginController.cs`'s existing `WorkspaceOptionResponse`/`WorkspaceSelectionRequiredResponse` usage — it already follows this convention; leave its pre-existing `displayName` (camelCase) vs. sibling snake_case inconsistency as-is (fixing it would change the wire format, out of scope here).
- Do not touch `GetInvitation` (`InvitationDetailDto`), `EnableMfa` (`MfaSetupDto`), `ConfirmMfaSetup`/`ForgotPassword`/`ResetPassword` (anonymous objects), or `LegalController`/`LegalDocumentContentController` — queued for a separate follow-up phase.
- Do not touch `ProjectsController.GetById` — it is a `501` placeholder with no body (Slice 2 of Work Management replaces it).

---

### Task 1: Auth session view models

**Files:**
- Create: `src/ONEVO.Api/Contracts/Auth/AuthSessionViewModel.cs`
- Create: `src/ONEVO.Api/Contracts/Auth/TenantSessionExchangeViewModel.cs`
- Create: `src/ONEVO.Api/Contracts/Auth/AuthViewModelMapper.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/Auth/TenantAuthResponseWriter.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/Auth/AuthSessionController.cs`

**Interfaces:**
- Consumes: `ONEVO.Application.Features.Auth.Login.DTOs.Responses.AuthSessionResponseDto`, `.TenantSessionExchangeResponseDto`, `ONEVO.Application.Features.Auth.Legal.Services.PendingLegalDocumentDto`.
- Produces: `AuthSessionViewModel`, `TenantSessionExchangeViewModel`, and `AuthViewModelMapper.ToViewModel(...)` overloads that Task 3's verification exercises end-to-end over real HTTP.

This retrofits 6 controllers at once (`AuthLoginController`, `AuthSessionController`, `AuthInvitationController`, `AuthMfaController`, `AuthPasswordController`, `AuthPendingLegalController`) because all of them only ever return a session response through the shared `TenantAuthResponseWriter.HandleSessionResultAsync` — changing that one method's return points is sufficient; none of those 5 other controller files need editing.

- [x] **Step 1: Write `AuthSessionViewModel.cs`**

```csharp
using System.Text.Json.Serialization;

namespace ONEVO.Api.Contracts.Auth;

public record CurrentUserViewModel(
    [property: JsonPropertyName("email")] string Email
);

public record WorkspaceViewModel(
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("display_name")] string DisplayName
);

public record PendingLegalDocumentViewModel(
    [property: JsonPropertyName("document_type")] string DocumentType,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("effective_at")] DateTimeOffset? EffectiveAt,
    [property: JsonPropertyName("content_url")] string? ContentUrl,
    [property: JsonPropertyName("content_endpoint")] string ContentEndpoint,
    [property: JsonPropertyName("content_hash")] string ContentHash
);

public record AuthSessionViewModel(
    [property: JsonPropertyName("authenticated")] bool Authenticated,
    [property: JsonPropertyName("user")] CurrentUserViewModel? User,
    [property: JsonPropertyName("permissions")] IReadOnlyList<string> Permissions,
    [property: JsonPropertyName("active_modules")] IReadOnlyList<string> ActiveModules,
    [property: JsonPropertyName("must_change_password")] bool MustChangePassword,
    [property: JsonPropertyName("mfa_required")] bool MfaRequired,
    [property: JsonPropertyName("legal_acceptance_required")] bool LegalAcceptanceRequired = false,
    [property: JsonPropertyName("pending_legal_documents")] IReadOnlyList<PendingLegalDocumentViewModel>? PendingLegalDocuments = null,
    [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt = null,
    [property: JsonPropertyName("continue_url")] string? ContinueUrl = null,
    [property: JsonPropertyName("workspace")] WorkspaceViewModel? Workspace = null
);
```

This exactly mirrors `ONEVO.Application.Features.Auth.Login.DTOs.Responses.AuthSessionResponseDto` (same property names, `JsonPropertyName` values, defaults, and nullability) — read that file first if anything here looks ambiguous, it is the source of truth for exact parity.

- [x] **Step 2: Write `TenantSessionExchangeViewModel.cs`**

```csharp
using System.Text.Json.Serialization;

namespace ONEVO.Api.Contracts.Auth;

public record TenantSessionExchangeUserViewModel(
    [property: JsonPropertyName("email")] string Email
);

public record TenantSessionExchangeViewModel(
    [property: JsonPropertyName("authenticated")] bool Authenticated,
    [property: JsonPropertyName("redirect_required")] bool RedirectRequired,
    [property: JsonPropertyName("user")] TenantSessionExchangeUserViewModel User,
    [property: JsonPropertyName("workspace")] WorkspaceViewModel Workspace,
    [property: JsonPropertyName("continue_url")] string ContinueUrl,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt
);
```

Mirrors `ONEVO.Application.Features.Auth.Login.DTOs.Responses.TenantSessionExchangeResponseDto`. Reuses `WorkspaceViewModel` from Step 1 (same namespace, same file tree branch).

- [x] **Step 3: Write `AuthViewModelMapper.cs`**

```csharp
using ONEVO.Application.Features.Auth.Legal.Services;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;

namespace ONEVO.Api.Contracts.Auth;

public static class AuthViewModelMapper
{
    public static AuthSessionViewModel ToViewModel(this AuthSessionResponseDto dto) => new(
        dto.Authenticated,
        dto.User is null ? null : new CurrentUserViewModel(dto.User.Email),
        dto.Permissions,
        dto.ActiveModules,
        dto.MustChangePassword,
        dto.MfaRequired,
        dto.LegalAcceptanceRequired,
        dto.PendingLegalDocuments?.Select(ToViewModel).ToList(),
        dto.ExpiresAt,
        dto.ContinueUrl,
        dto.Workspace is null ? null : new WorkspaceViewModel(dto.Workspace.Slug, dto.Workspace.DisplayName)
    );

    public static PendingLegalDocumentViewModel ToViewModel(this PendingLegalDocumentDto dto) => new(
        dto.DocumentType,
        dto.Version,
        dto.Title,
        dto.EffectiveAt,
        dto.ContentUrl,
        dto.ContentEndpoint,
        dto.ContentHash
    );

    public static TenantSessionExchangeViewModel ToViewModel(this TenantSessionExchangeResponseDto dto) => new(
        dto.Authenticated,
        dto.RedirectRequired,
        new TenantSessionExchangeUserViewModel(dto.User.Email),
        new WorkspaceViewModel(dto.Workspace.Slug, dto.Workspace.DisplayName),
        dto.ContinueUrl,
        dto.ExpiresAt
    );
}
```

- [x] **Step 4: Modify `TenantAuthResponseWriter.cs`**

Add `using ONEVO.Api.Contracts.Auth;` to the top of the file (alongside the existing usings).

Change all 5 return points inside `HandleSessionResultAsync`:

```csharp
        if (dto.RequiresPasswordChange)
            return controller.StatusCode(202, controller.WithContinueUrl(dto, dto.ToSessionResponse()).ToViewModel());

        if (dto.RequiresMfa)
        {
            controller.SetMfaChallengeCookie(dto.MfaChallenge, env);
            return controller.StatusCode(202, controller.WithContinueUrl(dto, dto.ToSessionResponse()).ToViewModel());
        }

        if (dto.RequiresLegalAcceptance)
        {
            controller.SetLegalPendingCookies(dto.LegalChallenge, dto.LegalCsrfToken, env);
            return controller.StatusCode(202, controller.WithContinueUrl(dto, dto.ToSessionResponse()).ToViewModel());
        }

        // Every gate cleared on the base host: hand off to the tenant host instead of signing in
        // here. No onevo_session/onevo_csrf are ever set on the base host.
        if (dto.RequiresTenantSessionExchange)
            return controller.StatusCode(202, dto.ToTenantSessionExchangeResponse().ToViewModel());

        await controller.SignInAsync(dto, env);
        return controller.Ok(dto.ToSessionResponse().ToViewModel());
```

Each existing `.ToSessionResponse()` / `.ToTenantSessionExchangeResponse()` call (Application-DTO-to-Application-DTO, defined on `LoginResponseDto` itself — unchanged) now has `.ToViewModel()` chained onto its result (the new Application-DTO-to-Contracts-view-model step from Step 3).

- [x] **Step 5: Modify `AuthSessionController.cs`**

In the `Me` action, change:

```csharp
        return Ok(result.Value);
```

to:

```csharp
        return Ok(result.Value!.ToViewModel());
```

Add `using ONEVO.Api.Contracts.Auth;` if not already present (the file already has `using ONEVO.Api.Contracts.Auth;` for `TenantSessionExchangeRequest` — confirm before adding a duplicate using).

- [x] **Step 6: Verify build**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj`
Expected: 0 errors.

- [x] **Step 7: Commit**

```bash
git add src/ONEVO.Api/Contracts/Auth/AuthSessionViewModel.cs src/ONEVO.Api/Contracts/Auth/TenantSessionExchangeViewModel.cs src/ONEVO.Api/Contracts/Auth/AuthViewModelMapper.cs src/ONEVO.Api/Controllers/Tenant/Auth/TenantAuthResponseWriter.cs src/ONEVO.Api/Controllers/Tenant/Auth/AuthSessionController.cs
git commit -m "refactor(api): map Auth session responses through Contracts view models"
```

---

### Task 2: Work Management Project view models

**Files:**
- Create: `src/ONEVO.Api/Contracts/WorkManagement/Projects/ProjectCreationViewModel.cs`
- Create: `src/ONEVO.Api/Contracts/WorkManagement/Projects/ProjectViewModelMapper.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ProjectsController.cs`

**Interfaces:**
- Consumes: `ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses.ProjectCreationResponse` and its 7 nested DTOs.
- Produces: `ProjectCreationViewModel` and `ProjectViewModelMapper.ToViewModel(this ProjectCreationResponse response)`, called by `ProjectsController.Create`.

- [x] **Step 1: Write `ProjectCreationViewModel.cs`**

```csharp
namespace ONEVO.Api.Contracts.WorkManagement.Projects;

public sealed record ProjectViewModel(
    Guid Id, string Name, string Identifier, Guid CategoryId, string? Description,
    Guid LeadId, DateOnly StartDate, DateOnly TargetDate, string? Color,
    decimal? ActualHours, decimal AllocatedHours, decimal CompletedHours,
    bool IsActive, DateTimeOffset CreatedAt);

public sealed record ObjectiveViewModel(
    Guid Id, Guid ProjectId, bool IsDefault, string Title, Guid OwnerId,
    DateOnly StartDate, DateOnly EndDate, decimal AllocatedHours, decimal CompletedHours);

public sealed record ProjectVersionViewModel(Guid Id, string Name, int StatusId, string StatusCode);

public sealed record ReleaseReminderViewModel(Guid Id, Guid VersionId, DateOnly ScheduledDate, string ReminderType);

public sealed record LabelViewModel(Guid Id, string Name, string Color);

public sealed record ProjectMembershipViewModel(Guid Id, Guid ObjectiveId, Guid UserId, string MembershipSource);

public sealed record ProjectLogoViewModel(Guid FileRecordId, string OriginalFileName);

public sealed record ProjectCreationViewModel(
    ProjectViewModel Project,
    ObjectiveViewModel DefaultObjective,
    ProjectVersionViewModel DefaultVersion,
    ReleaseReminderViewModel ReleaseReminder,
    IReadOnlyList<LabelViewModel> Labels,
    ProjectMembershipViewModel CreatorMembership,
    ProjectLogoViewModel? Logo);
```

No `JsonPropertyName` attributes anywhere, matching `ProjectCreationResponse` and its nested DTOs in `src/ONEVO.Application/Features/WorkManagement/Projects/DTOs/Responses/ProjectCreationResponse.cs` (none of them have any either — the endpoint currently serializes with ASP.NET Core's default camelCase policy, and this must stay unchanged).

- [x] **Step 2: Write `ProjectViewModelMapper.cs`**

```csharp
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;

namespace ONEVO.Api.Contracts.WorkManagement.Projects;

public static class ProjectViewModelMapper
{
    public static ProjectCreationViewModel ToViewModel(this ProjectCreationResponse response) => new(
        response.Project.ToViewModel(),
        response.DefaultObjective.ToViewModel(),
        response.DefaultVersion.ToViewModel(),
        response.ReleaseReminder.ToViewModel(),
        response.Labels.Select(ToViewModel).ToList(),
        response.CreatorMembership.ToViewModel(),
        response.Logo?.ToViewModel()
    );

    public static ProjectViewModel ToViewModel(this ProjectSummaryDto dto) => new(
        dto.Id, dto.Name, dto.Identifier, dto.CategoryId, dto.Description,
        dto.LeadId, dto.StartDate, dto.TargetDate, dto.Color,
        dto.ActualHours, dto.AllocatedHours, dto.CompletedHours,
        dto.IsActive, dto.CreatedAt);

    public static ObjectiveViewModel ToViewModel(this ObjectiveSummaryDto dto) => new(
        dto.Id, dto.ProjectId, dto.IsDefault, dto.Title, dto.OwnerId,
        dto.StartDate, dto.EndDate, dto.AllocatedHours, dto.CompletedHours);

    public static ProjectVersionViewModel ToViewModel(this ProjectVersionSummaryDto dto) => new(
        dto.Id, dto.Name, dto.StatusId, dto.StatusCode);

    public static ReleaseReminderViewModel ToViewModel(this ReleaseReminderSummaryDto dto) => new(
        dto.Id, dto.VersionId, dto.ScheduledDate, dto.ReminderType);

    public static LabelViewModel ToViewModel(this LabelSummaryDto dto) => new(dto.Id, dto.Name, dto.Color);

    public static ProjectMembershipViewModel ToViewModel(this ProjectMembershipSummaryDto dto) => new(
        dto.Id, dto.ObjectiveId, dto.UserId, dto.MembershipSource);

    public static ProjectLogoViewModel ToViewModel(this ProjectLogoSummaryDto dto) => new(
        dto.FileRecordId, dto.OriginalFileName);
}
```

- [x] **Step 3: Modify `ProjectsController.cs`**

Add `using ONEVO.Api.Contracts.WorkManagement.Projects;` to the usings.

Change:

```csharp
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value!.Project.Id }, result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
```

to:

```csharp
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value!.Project.Id }, result.Value.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
```

- [x] **Step 4: Verify build**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj`
Expected: 0 errors.

- [x] **Step 5: Commit**

```bash
git add src/ONEVO.Api/Contracts/WorkManagement/Projects/ProjectCreationViewModel.cs src/ONEVO.Api/Contracts/WorkManagement/Projects/ProjectViewModelMapper.cs src/ONEVO.Api/Controllers/Tenant/WorkManagement/ProjectsController.cs
git commit -m "refactor(api): map Work Management Project creation response through a Contracts view model"
```

---

### Task 3: Verify no wire-format change end-to-end

**Files:**
- None created/modified — verification only.

**Interfaces:**
- Consumes: everything from Tasks 1-2.

- [x] **Step 1: Run the full unit test suite**

Run: `dotnet test tests/ONEVO.Tests.Unit`
Expected: same pass count as before this plan (1125 passed per the prior session) — unit tests exercise Application-layer handlers only, never the `Contracts/` view models, so this must be a no-op change for them.

- [x] **Step 2: Run the Work Management integration tests**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter FullyQualifiedName~CreateProjectEndpointTests`
Expected: all 3 tests still PASS — `Create_ValidRequest_Returns201WithDefaultObjectiveVersionAndMembership` specifically asserts on `body.GetProperty("defaultObjective").GetProperty("isDefault")` and `body.GetProperty("defaultVersion").GetProperty("statusId")`, which only stay valid if `ProjectCreationViewModel`'s camelCase JSON shape is identical to `ProjectCreationResponse`'s.

- [x] **Step 3: Run one Auth integration test that exercises the session-response JSON shape**

First find the fastest existing integration test that logs in and inspects the session response body (search `tests/ONEVO.Tests.Integration/Auth/` and `tests/ONEVO.Tests.Integration/E2E/` for one that asserts on fields like `authenticated`, `permissions`, or `continue_url`). Run just that file, e.g.:

Run: `dotnet test tests/ONEVO.Tests.Integration --filter FullyQualifiedName~<the file's test class name found above>`
Expected: PASS — proves `AuthSessionViewModel`/`TenantSessionExchangeViewModel` serialize identically to the old `AuthSessionResponseDto`/`TenantSessionExchangeResponseDto` over real HTTP, not just in isolated review.

- [x] **Step 4: If no existing integration test covers this, note it rather than skip verification**

If Step 3 finds no suitable existing test, do not silently skip — at minimum run `tests/ONEVO.Tests.Integration/OrgStructure/LegalEntity/LegalEntitiesIntegrationTests.cs`'s `ProvisionAndLoginOwnerAsync` path indirectly by running that whole file (it performs a full login + session-exchange during `InitializeAsync` and will fail loudly if the session response shape broke), since every integration test file in this repo provisions tenants through that same login flow:

Run: `dotnet test tests/ONEVO.Tests.Integration --filter FullyQualifiedName~LegalEntitiesIntegrationTests`
Expected: all tests PASS (this file's `InitializeAsync` depends on parsing `continue_url` from the login response and cookies from the session-exchange response — both flow through the code changed in Task 1).

---

**Deviation recorded during execution (2026-08-03, Task 3):** The full unit suite initially failed with 6 failures after Tasks 1-2 — `LoginWorkspaceResponseTests.cs` (5 assertions) and `TenantSessionExchangeEndpointTests.cs` (1 assertion) assert the exact CLR type returned by the controller action (`.BeOfType<AuthSessionResponseDto>()` / `.BeOfType<TenantSessionExchangeResponseDto>()`), not just the JSON. This is expected fallout from a genuine type relocation, not a JSON-shape regression — both files already had `using ONEVO.Api.Contracts.Auth;`, so the fix was updating each assertion to the new view model type (`AuthSessionViewModel` / `TenantSessionExchangeViewModel`); every subsequent property access in those tests compiled unchanged since the view models mirror the DTOs' property names exactly.

## Self-Review

**Spec coverage:** Every one of the 6 controllers named in the Global Constraints/Task 1 intro is covered without individually editing 5 of them (via the shared `TenantAuthResponseWriter`); `AuthSessionController.Me` is covered explicitly since it does not go through that shared helper. Work Management's only real response body (`ProjectsController.Create`) is covered in Task 2. Explicit exclusions (`WorkspaceOptionResponse` precedent, `InvitationDetailDto`, `MfaSetupDto`, anonymous-object actions, `LegalController`/`LegalDocumentContentController`, `ProjectsController.GetById`) are named and left untouched, matching the user-confirmed Phase 1 scope.

**Placeholder scan:** No TBD/TODO; every step has complete, real code; verification steps have exact commands and expected outcomes, with a fallback (Step 4) if the ideal test (Step 3) isn't found rather than skipping verification.

**Type consistency:** `AuthViewModelMapper.ToViewModel(this AuthSessionResponseDto dto)` produces `AuthSessionViewModel` (Task 1 Step 1's exact type), consumed by `TenantAuthResponseWriter.cs` (Task 1 Step 4) and `AuthSessionController.cs` (Task 1 Step 5) identically. `ProjectViewModelMapper.ToViewModel(this ProjectCreationResponse response)` produces `ProjectCreationViewModel` (Task 2 Step 1's exact type), consumed by `ProjectsController.cs` (Task 2 Step 3). Every nested `ToViewModel()` overload's input/output type matches what its caller expects.
