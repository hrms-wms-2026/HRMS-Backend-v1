# Admin Client Logs Endpoint Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `POST /admin/v1/logs` so the platform-administration frontend's `LoggerService` can
report client-side errors into the backend's existing Serilog stream instead of losing them in the
browser console.

**Architecture:** A single new MediatR command (`RecordClientLogCommand`) behind a thin controller
action, following this codebase's standard CQRS-over-HTTP pattern exactly. No new database table —
the handler writes a structured log entry via `ILogger<T>`, matching how
`IngestActivitySnapshotsCommandHandler` already logs structured data in this codebase.

**Tech Stack:** ASP.NET Core (MediatR, FluentValidation), `ILogger<T>` (Serilog-backed), xUnit,
NSubstitute, FluentAssertions.

## Global Constraints

- Auth: `[Authorize(Policy = "AdminPolicy")]` only — no `[RequirePlatformPermission(...)]`. Any
  authenticated admin can report their own client errors; this isn't a permission-gated resource.
- Do not use `ICurrentUser` for the reporting admin's identity — read `User.FindFirstValue(...)`
  directly in the controller instead (see rationale in Task 1).
- Controller must live under `ONEVO.Api.Controllers.Admin.DevPlatform.*` (enforced by
  `ControllerArchitectureTests`).
- Response body: none. `204 No Content` on success.
- Never commit `src/ONEVO.Infrastructure/Persistence/Seeders/ModuleCatalogSeeder.cs` (local-only,
  currently has uncommitted changes) or `ops/dev-setup-resend-key.ps1` /
  `ops/dev-switch-to-sendgrid.ps1` (untracked, local-only) — pre-existing artifacts unrelated to
  this feature.

---

### Task 1: Command, validator, and handler

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/ClientLogs/Commands/RecordClientLog/RecordClientLogCommand.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/ClientLogs/Commands/RecordClientLog/RecordClientLogCommandValidator.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/ClientLogs/Commands/RecordClientLog/RecordClientLogCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/ClientLogs/RecordClientLogCommandValidatorTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/ClientLogs/RecordClientLogCommandHandlerTests.cs`

**Interfaces:**
- Consumes: nothing from other tasks (first task).
- Produces: `RecordClientLogCommand(string AdminUserId, string AdminEmail, string Level, string
  Message, IReadOnlyDictionary<string, object?>? Context, DateTimeOffset ClientTimestamp) :
  IRequest<Result>`. Task 2's controller constructs and sends this exact command.

- [ ] **Step 1: Write the failing validator tests**

Create `tests/ONEVO.Tests.Unit/Features/Monitoring/ClientLogs/RecordClientLogCommandValidatorTests.cs`:

```csharp
using FluentValidation.TestHelper;
using ONEVO.Application.Features.Monitoring.ClientLogs.Commands.RecordClientLog;

namespace ONEVO.Tests.Unit.Features.Monitoring.ClientLogs;

public class RecordClientLogCommandValidatorTests
{
    private readonly RecordClientLogCommandValidator _sut = new();

    private static RecordClientLogCommand ValidCommand(string level = "error", string message = "Something broke") =>
        new(
            AdminUserId: Guid.NewGuid().ToString(),
            AdminEmail: "admin@example.com",
            Level: level,
            Message: message,
            Context: null,
            ClientTimestamp: DateTimeOffset.UtcNow);

    [Fact]
    public void Valid_command_passes()
    {
        var result = _sut.TestValidate(ValidCommand());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Empty_level_fails()
    {
        var result = _sut.TestValidate(ValidCommand(level: ""));
        result.ShouldHaveValidationErrorFor(x => x.Level);
    }

    [Fact]
    public void Empty_message_fails()
    {
        var result = _sut.TestValidate(ValidCommand(message: ""));
        result.ShouldHaveValidationErrorFor(x => x.Message);
    }

    [Fact]
    public void Message_over_4000_chars_fails()
    {
        var result = _sut.TestValidate(ValidCommand(message: new string('x', 4001)));
        result.ShouldHaveValidationErrorFor(x => x.Message);
    }

    [Fact]
    public void Message_at_exactly_4000_chars_passes()
    {
        var result = _sut.TestValidate(ValidCommand(message: new string('x', 4000)));
        result.ShouldNotHaveValidationErrorFor(x => x.Message);
    }
}
```

- [ ] **Step 2: Run the validator tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter FullyQualifiedName~RecordClientLogCommandValidatorTests`
Expected: compile error — `RecordClientLogCommand`/`RecordClientLogCommandValidator` don't exist yet.

- [ ] **Step 3: Write the command**

Create `src/ONEVO.Application/Features/Monitoring/ClientLogs/Commands/RecordClientLog/RecordClientLogCommand.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Monitoring.ClientLogs.Commands.RecordClientLog;

public sealed record RecordClientLogCommand(
    string AdminUserId,
    string AdminEmail,
    string Level,
    string Message,
    IReadOnlyDictionary<string, object?>? Context,
    DateTimeOffset ClientTimestamp) : IRequest<Result>;
```

- [ ] **Step 4: Write the validator**

Create `src/ONEVO.Application/Features/Monitoring/ClientLogs/Commands/RecordClientLog/RecordClientLogCommandValidator.cs`:

```csharp
using FluentValidation;

namespace ONEVO.Application.Features.Monitoring.ClientLogs.Commands.RecordClientLog;

public sealed class RecordClientLogCommandValidator : AbstractValidator<RecordClientLogCommand>
{
    public RecordClientLogCommandValidator()
    {
        RuleFor(x => x.Level).NotEmpty();
        RuleFor(x => x.Message).NotEmpty().MaximumLength(4000);
    }
}
```

- [ ] **Step 5: Run the validator tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter FullyQualifiedName~RecordClientLogCommandValidatorTests`
Expected: PASS (5 tests).

- [ ] **Step 6: Write the failing handler tests**

Create `tests/ONEVO.Tests.Unit/Features/Monitoring/ClientLogs/RecordClientLogCommandHandlerTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Features.Monitoring.ClientLogs.Commands.RecordClientLog;

namespace ONEVO.Tests.Unit.Features.Monitoring.ClientLogs;

public class RecordClientLogCommandHandlerTests
{
    // NullLogger, not a mock: verifying the exact generic Log<TState>() call signature that
    // ILogger's LogError/LogWarning extension methods produce is brittle with NSubstitute (the
    // TState type is an internal framework type you can't name from a test). This codebase has
    // no existing precedent for asserting ILogger call content either - every other test that
    // touches ILogger (e.g. CsrfProtectionMiddlewareTests) only injects it as a stub dependency.
    // The observable contract worth testing here is the returned Result, not the log call shape.
    private readonly RecordClientLogCommandHandler _handler =
        new(NullLogger<RecordClientLogCommandHandler>.Instance);

    private static RecordClientLogCommand Command(string level = "error") => new(
        AdminUserId: Guid.NewGuid().ToString(),
        AdminEmail: "admin@example.com",
        Level: level,
        Message: "Something broke",
        Context: new Dictionary<string, object?> { ["route"] = "/tenants" },
        ClientTimestamp: DateTimeOffset.UtcNow);

    [Fact]
    public async Task Handle_ErrorLevel_ReturnsSuccess()
    {
        var result = await _handler.Handle(Command(level: "error"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WarnLevel_ReturnsSuccess()
    {
        var result = await _handler.Handle(Command(level: "warn"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NullContext_ReturnsSuccess()
    {
        var command = Command() with { Context = null };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
```

- [ ] **Step 7: Run the handler tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter FullyQualifiedName~RecordClientLogCommandHandlerTests`
Expected: compile error — `RecordClientLogCommandHandler` doesn't exist yet.

- [ ] **Step 8: Write the handler**

Create `src/ONEVO.Application/Features/Monitoring/ClientLogs/Commands/RecordClientLog/RecordClientLogCommandHandler.cs`:

```csharp
using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Monitoring.ClientLogs.Commands.RecordClientLog;

public sealed class RecordClientLogCommandHandler : IRequestHandler<RecordClientLogCommand, Result>
{
    private readonly ILogger<RecordClientLogCommandHandler> _logger;

    public RecordClientLogCommandHandler(ILogger<RecordClientLogCommandHandler> logger)
    {
        _logger = logger;
    }

    public Task<Result> Handle(RecordClientLogCommand request, CancellationToken ct)
    {
        var logLevel = request.Level.Equals("error", StringComparison.OrdinalIgnoreCase)
            ? LogLevel.Error
            : LogLevel.Warning;

        _logger.Log(
            logLevel,
            "Client log reported. AdminUserId={AdminUserId} AdminEmail={AdminEmail} ClientTimestamp={ClientTimestamp} Message={Message} Context={@Context}",
            request.AdminUserId,
            request.AdminEmail,
            request.ClientTimestamp,
            request.Message,
            request.Context);

        return Task.FromResult(Result.Success());
    }
}
```

- [ ] **Step 9: Run the handler tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter FullyQualifiedName~RecordClientLogCommandHandlerTests`
Expected: PASS (3 tests).

- [ ] **Step 10: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/ClientLogs tests/ONEVO.Tests.Unit/Features/Monitoring/ClientLogs
git commit -m "feat: add RecordClientLogCommand for structured client-error logging"
```

---

### Task 2: Controller endpoint

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/ClientLogs/DTOs/Requests/RecordClientLogRequest.cs`
- Create: `src/ONEVO.Api/Controllers/Admin/DevPlatform/Monitoring/ClientLogsController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/ClientLogs/ClientLogsControllerTests.cs`

**Interfaces:**
- Consumes: `RecordClientLogCommand` (Task 1) — exact constructor signature above.
- Produces: `POST admin/v1/logs` → `204 No Content` on success, or a `Problem` response on
  validation failure. Nothing downstream depends on this — final task.

- [ ] **Step 1: Write the failing controller attribute test**

Create `tests/ONEVO.Tests.Unit/Features/Monitoring/ClientLogs/ClientLogsControllerTests.cs`:

```csharp
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Controllers.Admin.DevPlatform.Monitoring;
using ONEVO.Api.Filters;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.ClientLogs;

public class ClientLogsControllerTests
{
    [Fact]
    public void Controller_HasRoute()
    {
        var type = typeof(ClientLogsController);

        var routeAttr = type.GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(routeAttr);
        Assert.Equal("admin/v1/logs", routeAttr.Template);
    }

    [Fact]
    public void Create_RequiresAdminPolicy_WithNoPlatformPermission()
    {
        var method = typeof(ClientLogsController).GetMethod(nameof(ClientLogsController.Create));
        Assert.NotNull(method);

        var authAttr = method!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttr);
        Assert.Equal("AdminPolicy", authAttr!.Policy);

        var permissionAttr = method.GetCustomAttribute<RequirePlatformPermissionAttribute>();
        Assert.Null(permissionAttr);
    }

    [Fact]
    public void Create_HasHttpPostAttribute_WithNoTemplate()
    {
        var method = typeof(ClientLogsController).GetMethod(nameof(ClientLogsController.Create));
        Assert.NotNull(method);

        var httpPost = method!.GetCustomAttribute<HttpPostAttribute>();
        Assert.NotNull(httpPost);
        Assert.Null(httpPost!.Template);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter FullyQualifiedName~ClientLogsControllerTests`
Expected: compile error — `ClientLogsController` doesn't exist yet.

- [ ] **Step 3: Write the request DTO**

Create `src/ONEVO.Application/Features/Monitoring/ClientLogs/DTOs/Requests/RecordClientLogRequest.cs`:

```csharp
namespace ONEVO.Application.Features.Monitoring.ClientLogs.DTOs.Requests;

public sealed record RecordClientLogRequest(
    string Level,
    string Message,
    IReadOnlyDictionary<string, object?>? Context,
    DateTimeOffset Timestamp);
```

- [ ] **Step 4: Write the controller**

Create `src/ONEVO.Api/Controllers/Admin/DevPlatform/Monitoring/ClientLogsController.cs`:

```csharp
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.ClientLogs.Commands.RecordClientLog;
using ONEVO.Application.Features.Monitoring.ClientLogs.DTOs.Requests;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.Monitoring;

/// <summary>
/// Receives client-side error/log reports from the platform-administration console so they
/// land in the backend's own Serilog stream instead of being lost in the browser console. Any
/// authenticated admin may report their own client errors — this is not a permission-gated
/// resource, so there is deliberately no [RequirePlatformPermission] here.
/// </summary>
[ApiController]
[Route("admin/v1/logs")]
public sealed class ClientLogsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ClientLogsController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    [Authorize(Policy = "AdminPolicy")]
    public async Task<IActionResult> Create([FromBody] RecordClientLogRequest request, CancellationToken ct)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var adminEmail = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;

        var result = await _mediator.Send(
            new RecordClientLogCommand(
                adminUserId,
                adminEmail,
                request.Level,
                request.Message,
                request.Context,
                request.Timestamp),
            ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
```

- [ ] **Step 5: Run the controller tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter FullyQualifiedName~ClientLogsControllerTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Run the full unit test suite**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`
Expected: all tests pass (existing count + 11 new: 5 validator + 3 handler + 3 controller).

- [ ] **Step 7: Run the architecture test suite**

Run: `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --filter FullyQualifiedName~ControllerArchitectureTests`
Expected: PASS — `ClientLogsController` lives under `ONEVO.Api.Controllers.Admin.DevPlatform.Monitoring`
and uses `AdminPolicy` (not `TenantPolicy`) and no `RequirePermissionAttribute`, so it automatically
satisfies every existing rule in that test file without any changes to it.

- [ ] **Step 8: Build the whole solution**

Run: `dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`
Expected: 0 errors.

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/ClientLogs/DTOs src/ONEVO.Api/Controllers/Admin/DevPlatform/Monitoring tests/ONEVO.Tests.Unit/Features/Monitoring/ClientLogs/ClientLogsControllerTests.cs
git commit -m "feat: add POST /admin/v1/logs endpoint for client-side error reporting"
```

---

## Self-Review Notes

- **Spec coverage:** both settled decisions (AdminPolicy-only auth, controller/`User`-claims-based
  identity instead of `ICurrentUser`, Monitoring/ClientLogs folder placement, no DB table) are
  reflected directly in the two tasks above.
- **No placeholders:** every step has literal, complete code.
- **Type consistency:** `RecordClientLogCommand`'s constructor parameter order/types (Task 1) match
  exactly how the controller constructs it in Task 2's `Create` action. `RecordClientLogRequest`
  (Task 2) and `RecordClientLogCommand` (Task 1) share the same `Context`/`Message`/`Level` types
  (`IReadOnlyDictionary<string, object?>?`, `string`, `string`).
