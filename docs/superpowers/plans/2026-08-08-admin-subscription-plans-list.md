# Admin Subscription Plans List Endpoint Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `GET /admin/v1/subscription-plans` so the platform-administration Tenants Creation
Wizard can list active subscription plans from an admin session.

**Architecture:** Add a `List` action to the existing admin `SubscriptionPlansController`, gated
by the same `[Authorize(Policy = "AdminPolicy")]` + `[RequirePlatformPermission(...)]` pattern
already used by every other action in that file. The action sends the already-existing,
already-tested `ListSubscriptionPlansQuery` via MediatR — no new query, handler, or DTO.

**Tech Stack:** ASP.NET Core (MediatR), xUnit, reflection-based attribute-presence tests
(existing convention in this codebase).

## Global Constraints

- Route: `admin/v1/subscription-plans` (inherited from the controller's `[Route]` attribute —
  no change needed there).
- Permission: `PlatformPermissionCatalog.SubscriptionsRead` (`"platform.subscriptions.read"`,
  already defined at `src/ONEVO.Application/Features/DevPlatform/PlatformAccess/Helpers/PlatformPermissionCatalog.cs:29`
  and already present in the seeded catalog list at line 69 — no catalog changes needed).
- Response shape: `IReadOnlyList<SubscriptionPlanSummaryDto>` (already exists, untouched).
- Never commit `src/ONEVO.Infrastructure/Persistence/Seeders/ModuleCatalogSeeder.cs` (local-only,
  currently has uncommitted changes) or `ops/dev-setup-resend-key.ps1` /
  `ops/dev-switch-to-sendgrid.ps1` (untracked, local-only) — these are pre-existing artifacts
  unrelated to this feature.

---

### Task 1: Add the List action and its authorization test

**Files:**
- Modify: `src/ONEVO.Api/Controllers/Admin/DevPlatform/Subscriptions/SubscriptionPlansController.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Subscription/SubscriptionPlansControllerTests.cs`

**Interfaces:**
- Consumes: `ListSubscriptionPlansQuery` (no constructor args — `src/ONEVO.Application/Features/DevPlatform/Subscription/Queries/ListSubscriptionPlans/ListSubscriptionPlansQuery.cs`), already registered with MediatR, already unit-tested via `ListSubscriptionPlansQueryHandlerTests.cs`. Its handler returns `Result<IReadOnlyList<SubscriptionPlanSummaryDto>>`.
- Produces: `GET admin/v1/subscription-plans` → `200 OK` with `IReadOnlyList<SubscriptionPlanSummaryDto>` body on success, or a `Problem` response on failure. Nothing downstream in this repo depends on this yet (the frontend wizard task consumes it from the other repo).

- [ ] **Step 1: Write the failing authorization test**

Create `tests/ONEVO.Tests.Unit/Features/DevPlatform/Subscription/SubscriptionPlansControllerTests.cs`:

```csharp
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Controllers.Admin.DevPlatform.Subscriptions;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Subscription;

public class SubscriptionPlansControllerTests
{
    [Fact]
    public void Controller_HasAdminPolicy_AndRoute()
    {
        var type = typeof(SubscriptionPlansController);

        var authAttr = type.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttr);
        Assert.Equal("AdminPolicy", authAttr.Policy);

        var routeAttr = type.GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(routeAttr);
        Assert.Equal("admin/v1/subscription-plans", routeAttr.Template);
    }

    [Theory]
    [InlineData(nameof(SubscriptionPlansController.List), PlatformPermissionCatalog.SubscriptionsRead)]
    [InlineData(nameof(SubscriptionPlansController.Create), PlatformPermissionCatalog.SubscriptionsManage)]
    [InlineData(nameof(SubscriptionPlansController.Update), PlatformPermissionCatalog.SubscriptionsManage)]
    [InlineData(nameof(SubscriptionPlansController.Archive), PlatformPermissionCatalog.SubscriptionsManage)]
    public void Endpoint_RequiresCorrectPlatformPermission(string methodName, string expectedPermission)
    {
        var method = typeof(SubscriptionPlansController).GetMethod(methodName);
        Assert.NotNull(method);

        var attr = method!.GetCustomAttribute<RequirePlatformPermissionAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(expectedPermission, attr!.Permission);
    }

    [Fact]
    public void List_HasHttpGetAttribute_WithNoTemplate()
    {
        var method = typeof(SubscriptionPlansController).GetMethod(nameof(SubscriptionPlansController.List));
        Assert.NotNull(method);

        var httpGet = method!.GetCustomAttribute<HttpGetAttribute>();
        Assert.NotNull(httpGet);
        Assert.Null(httpGet!.Template);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~SubscriptionPlansControllerTests`
Expected: compile error — `SubscriptionPlansController.List` does not exist yet. (The `Create`/`Update`/`Archive` cases in the `Theory` will also fail to resolve until `List` exists, since the whole file fails to compile with a missing member reference.)

- [ ] **Step 3: Add the `List` action**

In `src/ONEVO.Api/Controllers/Admin/DevPlatform/Subscriptions/SubscriptionPlansController.cs`, add
this `using` alongside the existing ones at the top of the file:

```csharp
using ONEVO.Application.Features.DevPlatform.Subscription.Queries.ListSubscriptionPlans;
```

Then add this action as the first method in the class, immediately after the constructor and
before the existing `Create` action:

```csharp
    [HttpGet]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SubscriptionsRead)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await _mediator.Send(new ListSubscriptionPlansQuery(), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~SubscriptionPlansControllerTests`
Expected: PASS (6 test cases: 1 `Controller_HasAdminPolicy_AndRoute` + 4 `Theory` cases + 1 `List_HasHttpGetAttribute_WithNoTemplate`)

- [ ] **Step 5: Run the full unit test suite**

Run: `dotnet test tests/ONEVO.Tests.Unit`
Expected: all tests pass (1448 existing + 6 new = 1454).

- [ ] **Step 6: Run the architecture test suite**

Run: `dotnet test tests/ONEVO.Tests.Architecture --filter FullyQualifiedName~ControllerArchitectureTests`
Expected: PASS — the new `List` action lives under `ONEVO.Api.Controllers.Admin` and uses
`AdminPolicy` + `RequirePlatformPermissionAttribute`, so it automatically satisfies
`AdminControllers_ShouldNotUse_TenantPolicy` and `AdminControllers_ShouldNotUse_RequirePermission`
without any changes to that test file.

- [ ] **Step 7: Build the whole solution**

Run: `dotnet build`
Expected: 0 errors, 0 new warnings.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Api/Controllers/Admin/DevPlatform/Subscriptions/SubscriptionPlansController.cs tests/ONEVO.Tests.Unit/Features/DevPlatform/Subscription/SubscriptionPlansControllerTests.cs
git commit -m "feat: add admin GET /admin/v1/subscription-plans list endpoint"
```

---

## Self-Review Notes

- **Spec coverage:** the spec's "GET /admin/v1/subscription-plans (new)" subsection is fully
  covered — auth policy, permission code, route, response DTO, and the note that this reuses the
  existing query/handler with no new logic.
- **No placeholders:** every step has literal, complete code.
- **Type consistency:** `SubscriptionPlansController`, `ListSubscriptionPlansQuery`, and
  `PlatformPermissionCatalog.SubscriptionsRead` are all pre-existing, unmodified names — nothing
  in this task introduces a name a later task needs to know about, since this is the plan's only
  task.
