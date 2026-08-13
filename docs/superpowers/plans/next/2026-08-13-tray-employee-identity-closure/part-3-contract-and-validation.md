# Tray Employee Identity Closure Part 3: Contract and Validation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lock the already-shipped Backend-to-Service-to-Tray identity transport and produce automated plus real-Windows evidence for Milestone 2.

**Architecture:** Backend integration tests prove exchange/refresh identity and fallback. Service and Shared tests prove snake_case HTTP parsing and IPC serialization. A real activation/reset smoke proves the installed Service and Tray screens use the contract correctly.

**Tech Stack:** ASP.NET Core 10, PostgreSQL/Testcontainers, .NET 10, xUnit, MAUI Windows.

**Spec:** `docs/superpowers/specs/2026-08-08-tray-login-employee-identity-design.md` and roadmap Milestone 2.

Use the explicit repository working directory stated by each task before running its commands.

## Required Parts 1-2 Outcome

The TrayApp has `IEmployeeIdentityStore`; activation calls `Replace`; Prepare,
Review, and Clock In read it; successful logout calls `Clear`; Clock In shows
`Identity unavailable` instead of `Environment.UserName` when the cache is empty.

## Global Constraints

- Keep the approved no-Employee fallback: auth User name/email and null employee number.
- Do not copy tokens, activation codes, personal emails, or credentials into validation records.
- Milestone 1 live AWS E2E must show PASS before Milestone 2 is marked complete.

---

### Task 4: Lock HTTP and IPC identity contracts

**Files:**
- Modify: `C:\HR\tray_app_maui\tests\ONEVO.Agent.Service.Tests\Api\OnevoApiClientTests.cs`
- Modify: `C:\HR\tray_app_maui\tests\ONEVO.Agent.Shared.Tests\IpcEnvelopeTests.cs`
- Verify: `C:\HR\HRMS-Backend-v1\tests\ONEVO.Tests.Integration\Monitoring\TrayActivation\TrayActivationIntegrationTests.cs`

**Interfaces:**
- Consumes: snake_case `TrayAuthResponseDto` and `EnrollmentResultPayload`.
- Produces: refresh parsing and Service-to-Tray serialization regression proof.

- [ ] **Step 1: Extend refresh identity assertions**

In `RefreshTokenAsync_Success_ReturnsRotatedTokens`, return and assert:

```csharp
employee_name = "Priya Employee",
employee_email = "priya@test.dev",
employee_number = "EMP-0001"
```

Assert the same three values on `result.Auth` after asserting the rotated token.

```csharp
Assert.Equal("Priya Employee", result.Auth.EmployeeName);
Assert.Equal("priya@test.dev", result.Auth.EmployeeEmail);
Assert.Equal("EMP-0001", result.Auth.EmployeeNumber);
```

- [ ] **Step 2: Add an IPC round-trip test**

```csharp
[Fact]
public void EnrollmentResultPayload_RoundTripsEmployeeIdentity()
{
    var value = new EnrollmentResultPayload
    {
        Success = true,
        EmployeeName = "Priya Employee",
        EmployeeEmail = "priya@test.dev",
        EmployeeNumber = "EMP-0001"
    };
    var restored = JsonSerializer.Deserialize<EnrollmentResultPayload>(
        JsonSerializer.Serialize(value));
    Assert.Equal(value.EmployeeName, restored!.EmployeeName);
    Assert.Equal(value.EmployeeEmail, restored.EmployeeEmail);
    Assert.Equal(value.EmployeeNumber, restored.EmployeeNumber);
}
```

- [ ] **Step 3: Run transport tests**

```powershell
dotnet test tests\ONEVO.Agent.Shared.Tests\ONEVO.Agent.Shared.Tests.csproj -c Release
dotnet test tests\ONEVO.Agent.Service.Tests\ONEVO.Agent.Service.Tests.csproj -c Release
```

Expected: both projects PASS.

- [ ] **Step 4: Run Backend identity integration tests**

From `C:\HR\HRMS-Backend-v1` run:

```powershell
dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj -c Release --filter FullyQualifiedName~Monitoring.TrayActivation.TrayActivationIntegrationTests
```

Expected: exchange with Employee returns all fields; no-Employee fallback returns
name/email plus null number; refresh rotates its token and returns identity.

- [ ] **Step 5: Commit contract tests**

```powershell
git add tests/ONEVO.Agent.Shared.Tests/IpcEnvelopeTests.cs tests/ONEVO.Agent.Service.Tests/Api/OnevoApiClientTests.cs
git commit -m "test(agent): lock employee identity transport contracts"
```

### Task 5: Full verification and real activation smoke

**Files:**
- Create: `C:\HR\HRMS-Backend-v1\docs\superpowers\workflow\TRAY_EMPLOYEE_IDENTITY_CLOSURE_VALIDATION.md`
- Modify: `C:\HR\HRMS-Backend-v1\docs\superpowers\workflow\SUMMARY.md`

**Interfaces:**
- Consumes: all Part 1-3 deliverables and Milestone 1 PASS record.
- Produces: dated evidence allowing roadmap Milestone 2 completion.

- [ ] **Step 1: Run full Backend verification**

```powershell
dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj -c Release
dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj -c Release
dotnet build src\ONEVO.Api\ONEVO.Api.csproj -c Release
```

Also retain the focused integration output from Task 4.

- [ ] **Step 2: Run full Agent verification**

```powershell
dotnet test tests\ONEVO.Agent.Shared.Tests\ONEVO.Agent.Shared.Tests.csproj -c Release
dotnet test tests\ONEVO.Agent.Service.Tests\ONEVO.Agent.Service.Tests.csproj -c Release
dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj -c Release
dotnet build ONEVO.Agent.slnx -c Release
```

- [ ] **Step 3: Run the Windows smoke matrix**

Against staging with installed Service and TrayApp:

1. Activate Employee A and verify Prepare, Review, and Clock In show the same server name/email/number.
2. Trigger or wait for refresh and verify the Service remains authenticated.
3. Sign out and verify identity is cleared.
4. Activate Employee B with no employee number and verify Employee A's number never appears.
5. Verify logs contain no tokens, activation codes, identity payload dumps, or credentials.

- [ ] **Step 4: Write the validation record**

Record date, both repo commit hashes, every command/pass count, staging URL,
device/OS, non-sensitive Employee A/B labels, link to Milestone 1 evidence, and
PASS/FAIL for each smoke item. Never record personal email values or secrets.
Add the validation record to `workflow/SUMMARY.md` in the same commit.

- [ ] **Step 5: Commit the record**

```powershell
git add docs/superpowers/workflow/TRAY_EMPLOYEE_IDENTITY_CLOSURE_VALIDATION.md docs/superpowers/workflow/SUMMARY.md
git commit -m "docs(monitoring): validate tray employee identity closure"
```

## Final Acceptance Criteria

- Exchange, refresh, and IPC preserve the approved three-field identity shape.
- Reactivation cannot leak a prior employee number.
- Prepare, Review, and Clock In display cached server identity only.
- Successful logout clears identity; failed logout retains it.
- Focused integration, full Backend unit/architecture, all Agent tests, and both builds pass.
- Real activation/reset smoke passes with a privacy-safe evidence record.
