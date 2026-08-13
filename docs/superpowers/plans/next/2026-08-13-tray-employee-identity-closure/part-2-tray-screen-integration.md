# Tray Employee Identity Closure Part 2: Screen Integration Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Atomically replace identity on activation and use the cached server identity on every Tray screen and successful logout.

**Architecture:** This part consumes `IEmployeeIdentityStore` from Part 1. Activation writes once; Prepare, Review, and Clock In read it; successful logout clears it. Missing identity is shown explicitly and never replaced by the Windows account name.

**Tech Stack:** .NET 10, .NET MAUI, CommunityToolkit.Mvvm, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-08-tray-login-employee-identity-design.md` and roadmap Milestone 2.

Run every command in this part from `C:\HR\tray_app_maui`.

## Required Part 1 Interface

```csharp
public interface IEmployeeIdentityStore
{
    EmployeeDisplayIdentity Read();
    void Replace(string? displayName, string? email, string? employeeNumber);
    void Clear();
}
```

## Global Constraints

- Successful activation replaces all three fields, including clearing a missing new number.
- Do not use `Environment.UserName` as employee identity.
- Do not clear identity after a failed/null logout reply because the Service remains enrolled.
- Leave non-identity work-location and face-status Preferences unchanged.

---

### Task 2: Integrate activation and onboarding screens

**Files:**
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\ViewModels\ConnectWorkspaceViewModel.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\ViewModels\PrepareWorkspaceViewModel.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\ViewModels\ReviewSetupViewModel.cs`
- Modify: `C:\HR\tray_app_maui\tests\ONEVO.Agent.TrayApp.Tests\ViewModels\ConnectWorkspaceViewModelTests.cs`
- Modify: `C:\HR\tray_app_maui\tests\ONEVO.Agent.TrayApp.Tests\ViewModels\PrepareWorkspaceViewModelTests.cs`
- Create: `C:\HR\tray_app_maui\tests\ONEVO.Agent.TrayApp.Tests\ViewModels\ReviewSetupViewModelTests.cs`

**Interfaces:**
- Consumes: `EnrollmentResultPayload.EmployeeName/EmployeeEmail/EmployeeNumber` and Part 1 store.
- Produces: consistent server-derived identity on activation and onboarding screens.

- [ ] **Step 1: Write the stale-number activation test**

```csharp
[Fact]
public async Task VerifyAndConnectCommand_NewNullNumberClearsPriorEmployeeNumber()
{
    var preferences = new FakePreferencesStore();
    var store = new PreferencesEmployeeIdentityStore(preferences);
    store.Replace("First User", "first@test.dev", "EMP-OLD");
    var pipe = new FakeNamedPipeClient
    {
        NextEnrollmentResult = new EnrollmentResultPayload
        {
            Success = true,
            EmployeeName = "Second User",
            EmployeeEmail = "second@test.dev",
            EmployeeNumber = null
        }
    };
    var vm = new ConnectWorkspaceViewModel(pipe, store) { ActivationCode = "ABC123" };
    await vm.VerifyAndConnectCommand.ExecuteAsync(null);
    Assert.Equal(string.Empty, store.Read().EmployeeNumber);
}
```

- [ ] **Step 2: Write Prepare and Review screen tests**

```csharp
var store = new PreferencesEmployeeIdentityStore(new FakePreferencesStore());
store.Replace("Priya Employee", "priya@test.dev", "EMP-0001");

var prepare = new PrepareWorkspaceViewModel(store);
await prepare.LoadAsync(CancellationToken.None);
Assert.Equal("Priya Employee", prepare.EmployeeFullName);
Assert.Equal("priya@test.dev", prepare.EmployeeEmail);
Assert.Equal("EMP-0001", prepare.EmployeeId);

var review = new ReviewSetupViewModel(store);
review.OnAppearing();
Assert.Equal("Priya Employee", review.FullName);
Assert.Equal("priya@test.dev", review.WorkEmail);
Assert.Equal("EMP-0001", review.EmployeeId);
```

- [ ] **Step 3: Run focused tests and confirm failure**

Run: `dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj -c Release --filter "FullyQualifiedName~ConnectWorkspaceViewModelTests|FullyQualifiedName~PrepareWorkspaceViewModelTests|FullyQualifiedName~ReviewSetupViewModelTests"`

Expected: compilation/assertion failure because constructors still use raw or static Preferences.

- [ ] **Step 4: Replace activation caching**

Inject `IEmployeeIdentityStore` into `ConnectWorkspaceViewModel`; after a
successful reply call exactly:

```csharp
_identityStore.Replace(
    result.EmployeeName,
    result.EmployeeEmail,
    result.EmployeeNumber);
```

Delete the three conditional raw-key writes.

Update existing test factories to pass a real store over the fake:

```csharp
private static IEmployeeIdentityStore EmptyIdentityStore() =>
    new PreferencesEmployeeIdentityStore(new FakePreferencesStore());
```

- [ ] **Step 5: Replace onboarding reads**

Inject the store into Prepare and Review. Read once and assign:

```csharp
var identity = _identityStore.Read();
EmployeeFullName = identity.DisplayName;
EmployeeEmail = identity.Email;
EmployeeId = identity.EmployeeNumber;
```

For Review, assign `FullName`, `WorkEmail`, and `EmployeeId` instead. Leave its
work-location and face-status Preferences unchanged.

- [ ] **Step 6: Verify and commit Task 2**

Run the Step 3 command, then the full TrayApp test project; expected PASS.

```powershell
git add ONEVO.Agent.TrayApp/ViewModels tests/ONEVO.Agent.TrayApp.Tests/ViewModels
git commit -m "fix(tray): replace and display real employee identity"
```

### Task 3: Integrate Clock In and logout

**Files:**
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\ViewModels\ClockInViewModel.cs`
- Modify: `C:\HR\tray_app_maui\tests\ONEVO.Agent.TrayApp.Tests\ViewModels\ClockInViewModelTests.cs`
- Modify: `C:\HR\tray_app_maui\tests\ONEVO.Agent.TrayApp.Tests\Fakes\FakeNamedPipeClient.cs`

**Interfaces:**
- Consumes: `IEmployeeIdentityStore.Read()` and `Clear()`.
- Produces: server-derived greeting and testable logout cleanup.

- [ ] **Step 1: Write failing ClockIn tests**

```csharp
[Fact]
public void Constructor_UsesCachedServerIdentity()
{
    var store = BuildIdentityStore("Priya Employee", "priya@test.dev", "EMP-0001");
    using var vm = new ClockInViewModel(new FakeNamedPipeClient(), store);
    Assert.Equal("Priya Employee", vm.EmployeeName);
}

[Fact]
public void Constructor_WithNoCachedIdentity_DoesNotUseWindowsUsername()
{
    using var vm = new ClockInViewModel(
        new FakeNamedPipeClient(),
        new PreferencesEmployeeIdentityStore(new FakePreferencesStore()));
    Assert.Equal("Identity unavailable", vm.EmployeeName);
}

[Fact]
public async Task SignOutCommand_OnSuccess_ClearsIdentity()
{
    var store = BuildIdentityStore("Priya Employee", "priya@test.dev", "EMP-0001");
    using var vm = new ClockInViewModel(new FakeNamedPipeClient(), store);
    await vm.SignOutCommand.ExecuteAsync(null);
    Assert.False(store.Read().IsAvailable);
}

[Fact]
public async Task SignOutCommand_OnFailure_KeepsIdentity()
{
    var store = BuildIdentityStore("Priya Employee", "priya@test.dev", "EMP-0001");
    var pipe = new FakeNamedPipeClient
    {
        NextLogoutResult = new LogoutResultPayload(false, "SERVICE_UNAVAILABLE")
    };
    using var vm = new ClockInViewModel(pipe, store);
    await vm.SignOutCommand.ExecuteAsync(null);
    Assert.True(store.Read().IsAvailable);
}

[Fact]
public async Task SignOutCommand_WithNoServiceReply_KeepsIdentity()
{
    var store = BuildIdentityStore("Priya Employee", "priya@test.dev", "EMP-0001");
    var pipe = new FakeNamedPipeClient { ReturnNullLogoutResult = true };
    using var vm = new ClockInViewModel(pipe, store);
    await vm.SignOutCommand.ExecuteAsync(null);
    Assert.True(store.Read().IsAvailable);
}
```

Add this fake behavior so null replies are distinguishable from the fake's
default success:

```csharp
public bool ReturnNullLogoutResult { get; set; }

public Task<LogoutResultPayload?> SendLogoutAsync(CancellationToken ct)
{
    SentEnvelopes.Add(new IpcEnvelope { Type = IpcMessageTypes.LogoutRequest });
    if (ReturnNullLogoutResult) return Task.FromResult<LogoutResultPayload?>(null);
    return Task.FromResult<LogoutResultPayload?>(
        NextLogoutResult ?? new LogoutResultPayload(true, null));
}
```

```csharp
private static IEmployeeIdentityStore BuildIdentityStore(
    string name, string email, string number)
{
    var store = new PreferencesEmployeeIdentityStore(new FakePreferencesStore());
    store.Replace(name, email, number);
    return store;
}
```

- [ ] **Step 2: Run focused tests**

Run: `dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj -c Release --filter FullyQualifiedName~ClockInViewModelTests`

Expected: failure because the constructor lacks the store and uses static Preferences.

- [ ] **Step 3: Implement Clock In identity behavior**

Inject and retain `IEmployeeIdentityStore`; initialize with:

```csharp
var identity = _identityStore.Read();
EmployeeName = identity.IsAvailable ? identity.DisplayName : "Identity unavailable";
```

After a successful `SendLogoutAsync`, call `_identityStore.Clear()` instead of
three static `Preferences.Remove` calls.

- [ ] **Step 4: Verify and commit Task 3**

Run focused and full TrayApp tests. Confirm
`rg -n "Environment.UserName|onevo.employee_" ONEVO.Agent.TrayApp/ViewModels`
returns no match. Then commit:

```powershell
git add ONEVO.Agent.TrayApp/ViewModels/ClockInViewModel.cs tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ClockInViewModelTests.cs tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeNamedPipeClient.cs
git commit -m "fix(tray): use server identity for clock-in and logout"
```
