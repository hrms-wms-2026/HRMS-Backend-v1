# Tenant-Host Password Login Retirement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fully remove the dead tenant-host email/password login application path (`LoginCommand`/`LoginCommandHandler`/`LoginCommandValidator`) from HRMS-Backend-v1, leaving base-domain credential-first login as the only email/password login entry point, while every other login-adjacent flow (workspace selection, Google login, invitations, password reset, MFA, legal acceptance, tenant-host authenticated APIs) keeps working unchanged.

**Architecture:** `AuthLoginController.Login` currently branches on `ITenantContext.ContextMode`: `Tenant` mode sends `LoginCommand` (direct tenant-host password login — the dead path), anything else sends `BaseLoginCommand` (the live credential-first path). This plan deletes the `Tenant` branch's command/handler/validator entirely and replaces it with a safe 400 rejection, then updates/removes every test whose subject is the deleted path, adds two architecture guards to keep it dead, and fixes two tests (one integration, one E2E) that currently assert the dead path succeeds.

**Tech Stack:** .NET (C#), MediatR, FluentValidation, xUnit, FluentAssertions, Moq, ASP.NET Core integration testing (WebApplicationFactory), Testcontainers/PostgreSQL.

## Global Constraints

- Work only in `C:\onevoNew\HRMS-Backend-v1`.
- Do not touch OneVo-HR docs.
- Do not change database schema, migrations, RLS policies, provider config, email, payment, MFA setup, legal document schema, tenant provisioning logic, or appsettings/.env files.
- Do not edit Postman collections (report only).
- Do not commit or push.
- Rejection response detail text must be exactly: `"Tenant-host password login is not supported."` — status 400. No wording like "main login page".
- Do not weaken/remove `ILoginContinuationService`, `LoginContinuationService`, `LoginSessionMaterialFactory`, `TenantAuthResponseWriter`, MFA/legal challenge flow, or tenant context switching.

---

### Task 1: Delete the dead LoginCommand application path

**Files:**
- Delete: `src/ONEVO.Application/Features/Auth/Login/Commands/Login/LoginCommand.cs`
- Delete: `src/ONEVO.Application/Features/Auth/Login/Commands/Login/LoginCommandHandler.cs`
- Delete: `src/ONEVO.Application/Features/Auth/Login/Commands/Login/LoginCommandValidator.cs`

**Interfaces:**
- Produces: nothing (these types cease to exist). Task 2 removes the only caller.

- [ ] **Step 1: Delete the three files and the now-empty `Commands/Login` directory**

```bash
rm "src/ONEVO.Application/Features/Auth/Login/Commands/Login/LoginCommand.cs"
rm "src/ONEVO.Application/Features/Auth/Login/Commands/Login/LoginCommandHandler.cs"
rm "src/ONEVO.Application/Features/Auth/Login/Commands/Login/LoginCommandValidator.cs"
rmdir "src/ONEVO.Application/Features/Auth/Login/Commands/Login"
```

(Leave this uncompiled until Task 2 removes the caller — do not run a build yet.)

---

### Task 2: Update AuthLoginController to reject tenant-host password login

**Files:**
- Modify: `src/ONEVO.Api/Controllers/Tenant/Auth/AuthLoginController.cs`

**Interfaces:**
- Consumes: `ITenantContext.ContextMode` (existing), `BaseLoginCommand` (existing, unchanged), `ControllerBase.Problem(string, int)` (ASP.NET Core built-in).
- Produces: `Login()` now returns, for `TenantContextMode.Tenant`, `Problem("Tenant-host password login is not supported.", statusCode: 400)` without any `_mediator.Send` call.

- [ ] **Step 1: Replace the tenant-context branch in `Login()`**

Current code (lines 40-44 of the file as read):
```csharp
        if (_tenantContext.ContextMode == TenantContextMode.Tenant)
        {
            var tenantResult = await _mediator.Send(new LoginCommand(request.Email, request.Password, ip, ua), ct);
            return await this.HandleSessionResultAsync(tenantResult, _env);
        }
```

Replace with:
```csharp
        if (_tenantContext.ContextMode == TenantContextMode.Tenant)
            return Problem("Tenant-host password login is not supported.", statusCode: 400);
```

- [ ] **Step 2: Update the XML summary above `Login()`**

Current:
```csharp
    /// <summary>Login with email + password. Tenant host: direct tenant login. Base host: credential-first resolver.</summary>
```

Replace with:
```csharp
    /// <summary>Base-host credential-first email + password login only. Tenant-host password login is not supported.</summary>
```

- [ ] **Step 3: Remove the now-unused `using` for the deleted command namespace**

The file currently has no explicit `using ONEVO.Application.Features.Auth.Login.Commands.Login;` (it referenced `LoginCommand` via implicit same-folder resolution — verify by checking the `using` list at the top of the file after the edit; there should be no dangling reference). Confirm no other reference to `LoginCommand` remains in this file (`ip`/`ua` variables stay — they're still used by `BaseLoginCommand`, `SelectWorkspace`, and `LoginWithGoogle`, so do not remove or duplicate them).

- [ ] **Step 4: Build the API project to confirm the controller compiles**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal`
Expected: succeeds (this will still fail at this point if any test project references are checked as part of the same build — that's fine, this step only builds the API csproj).

---

### Task 3: Rewrite TenantLoginControllerTests to prove rejection, not success

**Files:**
- Modify: `tests/ONEVO.Tests.Unit/Features/Auth/TenantLoginControllerTests.cs`

**Interfaces:**
- Consumes: `AuthLoginController` (Task 2's new behavior), `Mock<ITenantContext>` with `ContextMode == TenantContextMode.Tenant`, `Mock<IMediator>`.

- [ ] **Step 1: Replace the entire file contents**

Both existing tests (`Login_WithStaleInvalidSessionCookie_ClearsCookiesAndContinuesLogin`, `Login_WhenMfaIsRequired_StoresChallengeInHttpOnlyCookieOnly`) prove the deleted success path and must go. Replace the whole file with:

```csharp
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using MediatR;
using Moq;
using ONEVO.Api.Contracts.Auth;
using ONEVO.Api.Controllers.Tenant.Auth;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class TenantLoginControllerTests
{
    [Fact]
    public async Task Login_OnTenantHost_ReturnsSafeRejection_AndNeverCallsMediator()
    {
        var mediator = new Mock<IMediator>();
        var controller = CreateController(mediator.Object);

        var result = await controller.Login(
            new LoginRequest("owner@acme.test", "Password123!"),
            CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(400);
        var problemDetails = problem.Value.Should().BeOfType<ProblemDetails>().Subject;
        problemDetails.Detail.Should().Be("Tenant-host password login is not supported.");
        problemDetails.Detail.Should().NotContain("main login page");

        mediator.Verify(
            instance => instance.Send(It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "tenant-host password login must not reach MediatR at all - no command, no session, no side effect");

        var setCookie = controller.Response.Headers.SetCookie.ToString();
        setCookie.Should().NotContain("onevo_session=");
        setCookie.Should().NotContain("onevo_mfa=");
    }

    private static AuthLoginController CreateController(IMediator mediator)
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.Setup(instance => instance.EnvironmentName).Returns(Environments.Development);

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(instance => instance.ContextMode).Returns(TenantContextMode.Tenant);

        return new AuthLoginController(mediator, environment.Object, tenantContext.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }
}
```

- [ ] **Step 2: Run the unit test project to verify this file compiles and passes**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --no-build --verbosity minimal --filter "FullyQualifiedName~TenantLoginControllerTests"`

(This step will only fully succeed after Task 1/2's build succeeds and other broken test files in the same project are fixed — Tasks 4-6 — since `dotnet build`/`test` compiles the whole project. Run it as a compile sanity check now; a project-wide red is expected until Task 6 is done, then re-run the full suite in Task 9.)

---

### Task 4: Delete tests whose sole subject is the deleted LoginCommandHandler

**Files:**
- Delete: `tests/ONEVO.Tests.Unit/Features/Auth/LoginTenantScopeTests.cs`
- Delete: `tests/ONEVO.Tests.Unit/Features/Auth/CrossTenantLeakageTests.cs`

**Interfaces:** none — both files construct `LoginCommandHandler` directly and exclusively; with the handler deleted these files cannot compile and have no remaining subject.

- [ ] **Step 1: Delete both files**

```bash
rm "tests/ONEVO.Tests.Unit/Features/Auth/LoginTenantScopeTests.cs"
rm "tests/ONEVO.Tests.Unit/Features/Auth/CrossTenantLeakageTests.cs"
```

Note: `CrossTenantLeakageTests.cs` tested cross-tenant email isolation for the deleted tenant-host handler specifically (two tenants, same email, `GetByTenantAndEmailAsync` scoping). The equivalent guarantee for the live base-domain path is already covered by `BaseDomainLoginIntegrationTests.SameNormalizedEmail_DifferentTenants_IsAllowed` and `MultipleMatches_Returns202_WithSafeWorkspaceListOnly` (integration) plus `IBaseLoginCandidateRepository` going through the allowlisted `auth_lookup_base_login_candidates` function (enforced by `BaseLoginArchitectureTests.EfBaseLoginCandidateRepository_QueriesOnlyTheAllowlistedFunction`). No new test is needed to replace this coverage.

---

### Task 5: Retarget the Step1LoginBlockingNonEnforcementGuardTests architecture guard

**Files:**
- Modify: `tests/ONEVO.Tests.Architecture/Security/Step1LoginBlockingNonEnforcementGuardTests.cs`

**Interfaces:**
- Consumes: `BaseLoginCommandHandler.cs` source text (verified in inspection to already contain `ILoginContinuationService` / `_continuation.ContinueAsync` and no `_sessionMaterialFactory.PrepareAsync`).

- [ ] **Step 1: Replace the `Step2_LoginHandlersNowRouteThroughLoginContinuationServiceForLegalBlocking` test**

Current test (lines 38-65) reads `src/ONEVO.Application/Features/Auth/Login/Commands/Login/LoginCommandHandler.cs`, which no longer exists after Task 1. Replace the whole method with:

```csharp
    [Fact]
    public void Step2_LoginHandlersRouteThroughLoginContinuationServiceForLegalBlocking()
    {
        // Supersedes the former Step1_LoginHandlersDoNotEnforceSessionBlockingYet guard, then later
        // retargeted off the retired tenant-host LoginCommandHandler (deleted along with direct
        // tenant-host password login - see TENANT_HOST_PASSWORD_LOGIN_RETIREMENT_REPORT.md).
        // BaseLoginCommandHandler/SelectWorkspaceCommandHandler must not call
        // ILoginSessionMaterialFactory directly - they delegate through the continuation service,
        // which owns the legal check and the final PrepareAsync call.
        var srcDir = FindSrcDirectory();
        var handlerFiles = new[]
        {
            Path.Combine(srcDir, "ONEVO.Application", "Features", "Auth", "Login", "Commands", "BaseLogin", "BaseLoginCommandHandler.cs"),
            Path.Combine(srcDir, "ONEVO.Application", "Features", "Auth", "Login", "Commands", "SelectWorkspace", "SelectWorkspaceCommandHandler.cs")
        };

        foreach (var handlerFile in handlerFiles)
        {
            File.Exists(handlerFile).Should().BeTrue();
            var code = File.ReadAllText(handlerFile);

            code.Should().NotContain("_sessionMaterialFactory.PrepareAsync",
                $"{Path.GetFileName(handlerFile)}: session issuance is owned exclusively by ILoginContinuationService");
            code.Should().Contain("ILoginContinuationService");
            code.Should().Contain("_continuation.ContinueAsync");
        }

        var deletedLoginCommandHandler = Path.Combine(
            srcDir, "ONEVO.Application", "Features", "Auth", "Login", "Commands", "Login", "LoginCommandHandler.cs");
        File.Exists(deletedLoginCommandHandler).Should().BeFalse(
            "the tenant-host password-login LoginCommandHandler was retired and must not be recreated");
    }
```

- [ ] **Step 2: Verify the file still compiles**

Run: `dotnet build tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --verbosity minimal`
Expected: succeeds once Tasks 1-2 are done. If it fails on this file specifically, re-check the replacement method body was inserted correctly (braces balanced, `FindSrcDirectory()` already exists lower in the same class — do not redefine it).

---

### Task 6: Add architecture guards preventing LoginCommand from coming back

**Files:**
- Create: `tests/ONEVO.Tests.Architecture/TenantHostPasswordLoginRetirementArchitectureTests.cs`

**Interfaces:**
- Consumes: same `FindRepositoryRoot()`-style directory-walk pattern already used in `BaseLoginArchitectureTests.cs` (duplicated here rather than shared, matching this test project's existing convention of each architecture test file being self-contained).

- [ ] **Step 1: Write the guard test file**

```csharp
using FluentAssertions;

namespace ONEVO.Tests.Architecture;

public sealed class TenantHostPasswordLoginRetirementArchitectureTests
{
    [Fact]
    public void LoginCommandFilesDoNotExist()
    {
        var srcRoot = Path.Combine(FindRepositoryRoot(), "src");
        var deletedDir = Path.Combine(
            srcRoot, "ONEVO.Application", "Features", "Auth", "Login", "Commands", "Login");

        Directory.Exists(deletedDir).Should().BeFalse(
            "the tenant-host password-login command folder (LoginCommand/LoginCommandHandler/LoginCommandValidator) was retired and must not be recreated");
    }

    [Fact]
    public void NoProductionSourceReferencesLoginCommand()
    {
        var offenders = ScanProductionSourceFor("LoginCommand")
            .Where(f => !Path.GetFileName(f).StartsWith("BaseLoginCommand", StringComparison.Ordinal)
                && !Path.GetFileName(f).StartsWith("AdminLoginCommand", StringComparison.Ordinal)
                && !Path.GetFileName(f).StartsWith("AdminGoogleLoginCommand", StringComparison.Ordinal)
                && !Path.GetFileName(f).StartsWith("BaseGoogleLoginCommand", StringComparison.Ordinal))
            .ToList();

        offenders.Should().BeEmpty(
            "no production code under src may reference the retired tenant-host LoginCommand type");
    }

    [Fact]
    public void AuthLoginControllerDoesNotReferenceLoginCommand()
    {
        var controllerPath = FindSourceFile(
            "ONEVO.Api", "Controllers", "Tenant", "Auth", "AuthLoginController.cs");
        var text = File.ReadAllText(controllerPath);

        text.Should().NotContain("new LoginCommand(");
        text.Should().Contain("Tenant-host password login is not supported.");
    }

    [Fact]
    public void NoProductionSourceClaimsTenantHostPasswordLoginIsSupported()
    {
        var forbiddenPhrases = new[]
        {
            "main login page",
            "tenant-host login is supported",
            "tenant-host password login is supported",
            "direct tenant login",
            "Tenant host: direct tenant login"
        };

        var sourceRoot = Path.Combine(FindRepositoryRoot(), "src");
        var binSegment = $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";
        var objSegment = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";
        var allFiles = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(binSegment, StringComparison.OrdinalIgnoreCase)
                && !f.Contains(objSegment, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var phrase in forbiddenPhrases)
        {
            var offenders = allFiles
                .Where(f => File.ReadAllText(f).Contains(phrase, StringComparison.OrdinalIgnoreCase))
                .ToList();

            offenders.Should().BeEmpty(
                $"no production source may claim or imply tenant-host password login is supported (found phrase: \"{phrase}\")");
        }
    }

    private static IReadOnlyList<string> ScanProductionSourceFor(string token)
    {
        var sourceRoot = Path.Combine(FindRepositoryRoot(), "src");
        var binSegment = $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";
        var objSegment = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";

        return Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(binSegment, StringComparison.OrdinalIgnoreCase)
                && !f.Contains(objSegment, StringComparison.OrdinalIgnoreCase))
            .Where(f => File.ReadAllText(f).Contains(token, StringComparison.Ordinal))
            .ToList();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "ONEVO.Api")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate the repository root walking up from {AppContext.BaseDirectory}");
    }

    private static string FindSourceFile(string project, params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidateRoot = Path.Combine(directory.FullName, "src", project);
            if (Directory.Exists(candidateRoot))
            {
                var fullPath = Path.Combine(new[] { candidateRoot }.Concat(relativeSegments).ToArray());
                File.Exists(fullPath).Should().BeTrue($"expected {fullPath} to exist");
                return fullPath;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate src/{project} walking up from {AppContext.BaseDirectory}");
    }
}
```

Note on `NoProductionSourceReferencesLoginCommand`: the raw substring `"LoginCommand"` also matches `BaseLoginCommand`, `AdminLoginCommand`, `AdminGoogleLoginCommand`, `BaseGoogleLoginCommand` (all legitimate, unrelated commands) — the filter excludes files whose name starts with those prefixes so only genuine `LoginCommand`/`LoginCommandHandler`/`LoginCommandValidator` references (which won't exist as files, but could appear inline in some other file) trip the guard.

- [ ] **Step 2: Build and run the architecture test project**

Run: `dotnet build tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --verbosity minimal`
Run: `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --no-build --verbosity minimal --filter "FullyQualifiedName~TenantHostPasswordLoginRetirementArchitectureTests"`
Expected: build succeeds, all 4 new tests pass.

---

### Task 7: Fix the integration test that proves tenant-host password login succeeds

**Files:**
- Modify: `tests/ONEVO.Tests.Integration/Auth/BaseDomainLoginIntegrationTests.cs`

**Interfaces:**
- Consumes: existing `SeedActiveUserAsync`, `SeedVerifiedMfaAsync`, `ExtractCookieValue` helpers (unchanged).

- [ ] **Step 1: Replace `TenantHostPasswordLogin_MfaVerify_StillCompletesOnTenantHost` (lines 238-276)**

This test currently proves the retired path succeeds (POSTs password credentials to a `{slug}.localhost` host and expects `202 Accepted` continuing into MFA). Replace it with a test proving rejection and no session/side effect:

```csharp
    [Fact]
    public async Task TenantHostPasswordLogin_IsRejected_AndCreatesNoSession()
    {
        var user = await SeedActiveUserAsync(
            "tenant-reject-host",
            "tenant-reject-host@test.onevo.dev",
            "CorrectPass1!");

        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login");
        loginRequest.Headers.Host = "tenant-reject-host.localhost";
        loginRequest.Content = JsonContent.Create(new
        {
            email = user.Email,
            password = "CorrectPass1!"
        });
        var response = await _client.SendAsync(loginRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Tenant-host password login is not supported.");
        body.Should().NotContain("main login page");

        response.Headers.TryGetValues("Set-Cookie", out var setCookies).Should().BeFalse(
            "a rejected tenant-host password login must not set onevo_session, onevo_csrf, or onevo_mfa");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasSession = await db.Set<ONEVO.Domain.Features.InfrastructureModule.Entities.TenantSession>()
            .AnyAsync(s => s.UserId == user.UserId);
        hasSession.Should().BeFalse("no session row may be created by a rejected tenant-host password login");
    }
```

Before finalizing, verify the actual session entity/DbSet name and namespace (it may not be literally `TenantSession` / `InfrastructureModule.Entities.TenantSession` — check `ApplicationDbContext` for the session table's `DbSet<T>` property, e.g. by looking at how `LoginContinuationService`/`LoginSessionMaterialFactory` persist a session, or search the same file's usings and other integration tests for the session entity type actually in use). Adjust the type name and property (`UserId` vs `Id`/`SessionUserId`) to match reality — do not guess if the grep/read shows a different shape.

- [ ] **Step 2: Confirm base-domain MFA+legal coverage still exists elsewhere**

`BasePasswordLogin_MfaThenLegal_CompletesOnRootHost` (already in this file, unchanged) covers MFA continuation for the live base-domain path, so no coverage is lost by replacing the tenant-host test above.

- [ ] **Step 3: Run this test class (requires Docker/Testcontainers)**

Run: `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --no-build --verbosity minimal --filter "FullyQualifiedName~BaseDomainLoginIntegrationTests"`
Expected: all tests pass, including the new `TenantHostPasswordLogin_IsRejected_AndCreatesNoSession`. If Docker is unavailable, note this as a skipped verification in the final report (per instruction G) rather than guessing at the result.

---

### Task 8: Fix the E2E test that logs the owner in via tenant-host password login

**Files:**
- Modify: `tests/ONEVO.Tests.Integration/E2E/TenantProvisioningE2ETests.cs`

**Interfaces:**
- Consumes: existing `SendAsync`, `ReadJsonAsync`, `ParseSetCookies` helpers (unchanged). `Tenancy:RootDomain` is already `"localhost"` in `E2ETestFactory.cs:47`, so a bare `Host: localhost` request resolves to base/root context, matching the pattern already proven in `BaseDomainLoginIntegrationTests.PostLoginAsync`.

- [ ] **Step 1: Replace step 6 (lines 136-147)**

Current:
```csharp
        // ── 6. Owner logs in on the tenant host ─────────────────────────────────
        // Invite completion already appended the current required legal records before issuing
        // its session, so a later tenant-host login can issue a session directly.
        var loginResponse = await SendAsync(HttpMethod.Post, TenantHost, "/api/v1/auth/login",
            new { email = OwnerEmail, password = OwnerPassword });
        var loginBody = await ReadJsonAsync(loginResponse);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK, loginBody.ToString());
        var cookies = ParseSetCookies(loginResponse);
```

Replace with:
```csharp
        // ── 6. Owner logs in via base-domain credential-first login ────────────
        // Tenant-host password login is retired; the base host resolves the single eligible
        // workspace from verified credentials. Invite completion already appended the current
        // required legal records before issuing its session, so this login can issue a session
        // directly (no legal/MFA challenge in between).
        const string BaseHost = "localhost";
        var rejectedTenantHostLogin = await SendAsync(HttpMethod.Post, TenantHost, "/api/v1/auth/login",
            new { email = OwnerEmail, password = OwnerPassword });
        rejectedTenantHostLogin.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var rejectedBody = await rejectedTenantHostLogin.Content.ReadAsStringAsync();
        rejectedBody.Should().Contain("Tenant-host password login is not supported.");
        rejectedTenantHostLogin.Headers.Contains("Set-Cookie").Should().BeFalse(
            "a rejected tenant-host password login must not issue any session cookie");

        var loginResponse = await SendAsync(HttpMethod.Post, BaseHost, "/api/v1/auth/login",
            new { email = OwnerEmail, password = OwnerPassword });
        var loginBody = await ReadJsonAsync(loginResponse);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK, loginBody.ToString());
        var cookies = ParseSetCookies(loginResponse);
```

Everything from the old line 148 onward (`loginBody.GetProperty("authenticated")...` through the end of the test) is unchanged — it already only depends on `loginBody`/`cookies`, not on which host issued them, and subsequent steps 7-10 continue to exercise tenant-host authenticated APIs (`TenantHost`) with the session cookie, proving tenant-host application APIs still work post-login.

- [ ] **Step 2: Run this test (requires Docker/Testcontainers)**

Run: `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --no-build --verbosity minimal --filter "FullyQualifiedName~TenantProvisioningE2ETests"`
Expected: `Full_tenant_provisioning_flow` passes end to end. If Docker is unavailable, note this as a skipped verification in the final report.

---

### Task 9: Full verification sweep

**Files:** none (verification only)

- [ ] **Step 1: Build the API project**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal`
Expected: 0 errors.

- [ ] **Step 2: Run unit tests**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --no-build --verbosity minimal`
Expected: all pass (build the project first if `--no-build` fails because Tasks 1-6 changed it: `dotnet build tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --verbosity minimal`).

- [ ] **Step 3: Run architecture tests**

Run: `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --no-build --verbosity minimal`
Expected: all pass (build first if needed, same as Step 2).

- [ ] **Step 4: Run integration tests if Docker is available**

Check Docker: `docker info` (or equivalent). If available:
Run: `dotnet build tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --verbosity minimal`
Run: `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --no-build --verbosity minimal`
Expected: all pass. If Docker is unavailable, record this as a skipped verification with the exact reason (no Docker daemon) in the final report — do not fabricate a pass/fail.

- [ ] **Step 5: Grep sweep for leftover references**

Run:
```bash
rg -n "new LoginCommand|LoginCommandHandler|LoginCommandValidator|Direct tenant login|main login page|tenant-host password login is supported|Tenant host: direct tenant login" src tests
```
Expected: only matches inside `tests/ONEVO.Tests.Architecture/Step1LoginBlockingNonEnforcementGuardTests.cs`'s and `TenantHostPasswordLoginRetirementArchitectureTests.cs`'s guard-check literal strings (the phrases exist as string literals being asserted *against*, not as active claims) and Postman's already-existing "Rejected Direct Tenant Login" folder name (out of scope, not touched). Any match inside `src/` (excluding these guard test literals which live in `tests/`) is a bug — investigate and fix before proceeding.

- [ ] **Step 6: Check for trailing whitespace / no-newline-at-eof issues from the edits**

Run: `git diff --check`
Expected: no output (clean).

---

### Task 10: Write the final report

**Files:**
- Create: `TENANT_HOST_PASSWORD_LOGIN_RETIREMENT_REPORT.md` (repository root of `HRMS-Backend-v1`, matching the existing sibling reports like `TENANT_SESSION_RLS_CONTEXT_FIX_REPORT.md`)

- [ ] **Step 1: Write the report**

Include, with concrete file paths and actual command output/exit codes captured from Task 9 (not paraphrased):
- Files inspected (the list from the inspection phase: `LoginCommand`/`Handler`/`Validator`, `AuthLoginController.cs`, `TenantLoginControllerTests.cs`, `LoginTenantScopeTests.cs`, `CrossTenantLeakageTests.cs`, `Step1LoginBlockingNonEnforcementGuardTests.cs`, `BaseLoginArchitectureTests.cs`, `BaseDomainLoginIntegrationTests.cs`, `TenantProvisioningE2ETests.cs`, `BaseLoginCommandHandler.cs`, Postman's rejected-tenant-login folder).
- Files deleted: the 3 `Commands/Login/*.cs` files + directory, `LoginTenantScopeTests.cs`, `CrossTenantLeakageTests.cs`.
- Files modified: `AuthLoginController.cs`, `TenantLoginControllerTests.cs`, `Step1LoginBlockingNonEnforcementGuardTests.cs`, `BaseDomainLoginIntegrationTests.cs`, `TenantProvisioningE2ETests.cs`.
- Files created: `TenantHostPasswordLoginRetirementArchitectureTests.cs`, this report.
- Tests deleted/rewritten/added: list each with a one-line reason.
- Proof base-domain login still works: cite `BaseDomainLoginIntegrationTests.ExactOneMatch_LogsIn_...` and `MultipleMatches_Returns202_...` pass results (or unit-level `BaseLoginCommandHandlerTests` if integration was skipped).
- Proof workspace selection still works: cite `WorkspaceSelection_CompletesLogin_AndSetsSessionCookie` pass result.
- Proof base Google login still works: cite `BaseGoogleLogin_MfaThenLegal_CompletesOnRootHost` pass result.
- Proof direct tenant-host password login is rejected: cite the new `TenantHostPasswordLogin_IsRejected_AndCreatesNoSession` and `TenantLoginControllerTests.Login_OnTenantHost_ReturnsSafeRejection_AndNeverCallsMediator` pass results.
- Proof no session is created: cite the DB assertion in the rewritten integration test.
- Proof invitation/reset/MFA/legal continuation untouched: list the unchanged test files/methods still passing (`AcceptInvitationDirectoryTests`, `ForcePasswordChangeLegalTests`, `VerifyMfaCommandHandlerTests`, etc. — do not re-verify their internals, just confirm they still compile/pass in the Task 9 run).
- Any skipped verification and exact reason (e.g., "Docker unavailable in this environment, integration suite not run — commands provided for the user to run").
- Postman note (per instruction F): state that "02. Auth - Rejected Direct Tenant Login" already exists and expects 400 — recommend it either stay as the negative/security folder it already is, or be confirmed as such; do not create a "normal login" Postman entry for tenant-host password login. No Postman files were edited in this task.

---

## Self-Review Notes

- Spec coverage: A (inspection, done above) / B (Task 1) / C (Task 2) / D (Task 2, unchanged continuation service) / E1-E5 (Tasks 3, 4, 6, 7) / F (Task 10 report note) / G (Task 9) / H (Task 10) all have a task.
- Task 7's session-entity type name is flagged as needing verification against the real `ApplicationDbContext` shape rather than guessed — the executor must check before writing that assertion.
- Task 8 adds an extra rejection check inline (not strictly asked for in the E2E file specifically, but directly required by top-level requirement 5 "must return a safe 400/404-style response" and is the natural place to prove rejection + successful base-login coexist in one real request flow) — this is in scope, not scope creep, since requirement E4 explicitly asks for exactly this integration coverage.
