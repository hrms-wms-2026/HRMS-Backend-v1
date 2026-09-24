# Admin Subscription Plan Detail Endpoint Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `GET /admin/v1/subscription-plans/{id}` so the frontend can fetch a single subscription plan's full detail (module keys, override pricing, trial/grace days) for an edit screen.

**Architecture:** Thin controller action wiring to the already-existing, already-tested `GetSubscriptionPlanQuery`/`GetSubscriptionPlanQueryHandler` — no Application-layer changes needed, matching the codebase's established `[HttpPatch("{id:guid}")]`/`[HttpDelete("{id:guid}")]` pattern already on this controller.

**Tech Stack:** ASP.NET Core, MediatR, xUnit + reflection-based controller tests.

## Global Constraints

- Repo root: `C:\Users\User\OneDrive\Desktop\HRMS-Backend-v1`. Branch `feature/admin-subscription-plan-detail-endpoint` already created off up-to-date `origin/development`.
- Never commit `src/ONEVO.Infrastructure/Persistence/Seeders/ModuleCatalogSeeder.cs` (local-only, has uncommitted changes) or `ops/dev-setup-resend-key.ps1` / `ops/dev-switch-to-sendgrid.ps1` (untracked, local-only).
- `SubscriptionPlansControllerTests.cs` does **not** exist yet on `development` (confirmed via search — only handler-level tests exist for this feature area: `CreateSubscriptionPlanCommandHandlerTests.cs`, `UpdateSubscriptionPlanCommandHandlerTests.cs`, `ArchiveSubscriptionPlanCommandHandlerTests.cs`, `GetSubscriptionPlanQueryHandlerTests.cs`, `ListSubscriptionPlansQueryHandlerTests.cs`). This plan creates that controller test file fresh, covering the full controller (Create/Update/Archive/GetById) since none of those have controller-level attribute coverage yet on `development`.

---

### Task 1: GetById controller action + controller test file

**Files:**
- Modify: `src/ONEVO.Api/Controllers/Admin/DevPlatform/Subscriptions/SubscriptionPlansController.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Subscription/SubscriptionPlansControllerTests.cs`

**Interfaces:**
- Consumes: `GetSubscriptionPlanQuery(Guid PlanId)` → `Result<SubscriptionPlanDetailDto>` (already exists, unchanged).
- Produces: `GET /admin/v1/subscription-plans/{id}` → `200 OK` with `SubscriptionPlanDetailDto` body, or `404`/other per `Result.StatusCode`.

- [ ] **Step 1: Write the failing controller test**

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
    public void Controller_HasRoute()
    {
        var type = typeof(SubscriptionPlansController);

        var routeAttr = type.GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(routeAttr);
        Assert.Equal("admin/v1/subscription-plans", routeAttr.Template);
    }

    [Theory]
    [InlineData(nameof(SubscriptionPlansController.GetById), PlatformPermissionCatalog.SubscriptionsRead)]
    [InlineData(nameof(SubscriptionPlansController.Create), PlatformPermissionCatalog.SubscriptionsManage)]
    [InlineData(nameof(SubscriptionPlansController.Update), PlatformPermissionCatalog.SubscriptionsManage)]
    [InlineData(nameof(SubscriptionPlansController.Archive), PlatformPermissionCatalog.SubscriptionsManage)]
    public void Endpoint_RequiresCorrectPlatformPermission(string methodName, string expectedPermission)
    {
        var method = typeof(SubscriptionPlansController).GetMethod(methodName);
        Assert.NotNull(method);

        var authAttr = method!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttr);
        Assert.Equal("AdminPolicy", authAttr!.Policy);

        var attr = method.GetCustomAttribute<RequirePlatformPermissionAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(expectedPermission, attr!.Permission);
    }

    [Fact]
    public void GetById_HasHttpGetAttribute_WithIdTemplate()
    {
        var method = typeof(SubscriptionPlansController).GetMethod(nameof(SubscriptionPlansController.GetById));
        Assert.NotNull(method);

        var httpGet = method!.GetCustomAttribute<HttpGetAttribute>();
        Assert.NotNull(httpGet);
        Assert.Equal("{id:guid}", httpGet!.Template);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`
Expected: FAIL to compile — `SubscriptionPlansController.GetById` does not exist yet (`nameof(SubscriptionPlansController.GetById)` is a compile error).

- [ ] **Step 3: Add the GetById action**

In `src/ONEVO.Api/Controllers/Admin/DevPlatform/Subscriptions/SubscriptionPlansController.cs`, add the missing using and the new action.

Find:

```csharp
using ONEVO.Application.Features.DevPlatform.Subscription.Commands.ArchiveSubscriptionPlan;
using ONEVO.Application.Features.DevPlatform.Subscription.Commands.CreateSubscriptionPlan;
using ONEVO.Application.Features.DevPlatform.Subscription.Commands.UpdateSubscriptionPlan;
using ONEVO.Application.Features.DevPlatform.Subscription.DTOs.Requests;
```

Replace with:

```csharp
using ONEVO.Application.Features.DevPlatform.Subscription.Commands.ArchiveSubscriptionPlan;
using ONEVO.Application.Features.DevPlatform.Subscription.Commands.CreateSubscriptionPlan;
using ONEVO.Application.Features.DevPlatform.Subscription.Commands.UpdateSubscriptionPlan;
using ONEVO.Application.Features.DevPlatform.Subscription.DTOs.Requests;
using ONEVO.Application.Features.DevPlatform.Subscription.Queries.GetSubscriptionPlan;
```

Find:

```csharp
    public SubscriptionPlansController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
```

Replace with:

```csharp
    public SubscriptionPlansController(IMediator mediator) => _mediator = mediator;

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SubscriptionsRead)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSubscriptionPlanQuery(id), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost]
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj && dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~SubscriptionPlansControllerTests"`
Expected: PASS (6 test cases: `Controller_HasRoute`, 4x `Endpoint_RequiresCorrectPlatformPermission`, `GetById_HasHttpGetAttribute_WithIdTemplate`)

- [ ] **Step 5: Run full unit + architecture test suites**

```bash
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj
dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj
```
Expected: PASS, no regressions (controller stays under `Controllers.Admin.DevPlatform.*`, satisfying `ControllerArchitectureTests.cs`'s existing rules — this action doesn't move the file or change its namespace).

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Api/Controllers/Admin/DevPlatform/Subscriptions/SubscriptionPlansController.cs tests/ONEVO.Tests.Unit/Features/DevPlatform/Subscription/SubscriptionPlansControllerTests.cs
git commit -m "feat: add GET /admin/v1/subscription-plans/{id} endpoint"
```

---

## Final Step

After Task 1, use superpowers:finishing-a-development-branch to verify the full test suite one more time and present merge/PR/keep options.
