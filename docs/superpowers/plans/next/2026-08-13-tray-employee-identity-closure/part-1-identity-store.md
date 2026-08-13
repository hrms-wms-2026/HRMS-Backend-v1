# Tray Employee Identity Closure Part 1: Identity Store Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the TrayApp one atomic, testable owner for its cached employee display identity.

**Architecture:** `PreferencesEmployeeIdentityStore` is the only class that knows the three MAUI Preferences keys. `Replace` clears the previous identity before writing present values, preventing a null employee number from retaining another user's number.

**Tech Stack:** .NET 10, .NET MAUI Preferences, xUnit, built-in dependency injection.

**Spec:** `docs/superpowers/specs/2026-08-08-tray-login-employee-identity-design.md` and `docs/superpowers/specs/next/2026-08-13-tray-monitoring-completion-roadmap-design.md`.

Run every command in this part from `C:\HR\tray_app_maui`.

## Global Constraints

- Identity cache values are display-only; backend identity and authorization remain authoritative.
- TrayApp never receives or persists access/refresh tokens.
- `Replace` must clear absent fields instead of retaining stale values.
- Keep the approved backend fallback where a user without an Employee row has name/email but no employee number.

---

### Task 1: Create the atomic employee identity store

**Files:**
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\Services\IEmployeeIdentityStore.cs`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\Services\PreferencesEmployeeIdentityStore.cs`
- Create: `C:\HR\tray_app_maui\tests\ONEVO.Agent.TrayApp.Tests\Services\PreferencesEmployeeIdentityStoreTests.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\Services\IPreferencesStore.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\Services\PreferencesStore.cs`
- Modify: `C:\HR\tray_app_maui\tests\ONEVO.Agent.TrayApp.Tests\Fakes\FakePreferencesStore.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\MauiProgram.cs`

**Interfaces:**
- Consumes: `IPreferencesStore.Get(string, string)`, `Set(string, string)`, and new `Remove(string)`.
- Produces: `EmployeeDisplayIdentity`, `IEmployeeIdentityStore.Read()`, `Replace(string?, string?, string?)`, and `Clear()`.

- [ ] **Step 1: Write the failing identity-store tests**

```csharp
[Fact]
public void Replace_RemovesAStaleEmployeeNumberWhenTheNewValueIsNull()
{
    var preferences = new FakePreferencesStore();
    var store = new PreferencesEmployeeIdentityStore(preferences);
    store.Replace("First User", "first@test.dev", "EMP-OLD");
    store.Replace("Second User", "second@test.dev", null);
    Assert.Equal(new EmployeeDisplayIdentity("Second User", "second@test.dev", ""), store.Read());
}

[Fact]
public void Clear_RemovesEveryIdentityField()
{
    var store = new PreferencesEmployeeIdentityStore(new FakePreferencesStore());
    store.Replace("Priya Employee", "priya@test.dev", "EMP-0001");
    store.Clear();
    Assert.Equal(new EmployeeDisplayIdentity("", "", ""), store.Read());
}
```

- [ ] **Step 2: Run the tests and verify the types are missing**

Run: `dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj -c Release --filter FullyQualifiedName~PreferencesEmployeeIdentityStoreTests`

Expected: compilation fails because the store and identity record do not exist.

- [ ] **Step 3: Add removal to the preferences seam**

```csharp
public interface IPreferencesStore
{
    string Get(string key, string defaultValue);
    void Set(string key, string value);
    void Remove(string key);
}
```

Implement `PreferencesStore.Remove` with guarded `Preferences.Remove(key)` and
`FakePreferencesStore.Remove` with `_values.Remove(key)`.

- [ ] **Step 4: Add the exact identity contract**

```csharp
namespace ONEVO.Agent.TrayApp.Services;

public sealed record EmployeeDisplayIdentity(string DisplayName, string Email, string EmployeeNumber)
{
    public bool IsAvailable =>
        !string.IsNullOrWhiteSpace(DisplayName) && !string.IsNullOrWhiteSpace(Email);
}

public interface IEmployeeIdentityStore
{
    EmployeeDisplayIdentity Read();
    void Replace(string? displayName, string? email, string? employeeNumber);
    void Clear();
}
```

- [ ] **Step 5: Implement Preferences ownership**

```csharp
namespace ONEVO.Agent.TrayApp.Services;

public sealed class PreferencesEmployeeIdentityStore(IPreferencesStore preferences)
    : IEmployeeIdentityStore
{
    private const string NameKey = "onevo.employee_display_name";
    private const string EmailKey = "onevo.employee_email";
    private const string NumberKey = "onevo.employee_id";

    public EmployeeDisplayIdentity Read() => new(
        preferences.Get(NameKey, string.Empty),
        preferences.Get(EmailKey, string.Empty),
        preferences.Get(NumberKey, string.Empty));

    public void Replace(string? displayName, string? email, string? employeeNumber)
    {
        Clear();
        SetWhenPresent(NameKey, displayName);
        SetWhenPresent(EmailKey, email);
        SetWhenPresent(NumberKey, employeeNumber);
    }

    public void Clear()
    {
        preferences.Remove(NameKey);
        preferences.Remove(EmailKey);
        preferences.Remove(NumberKey);
    }

    private void SetWhenPresent(string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) preferences.Set(key, value.Trim());
    }
}
```

- [ ] **Step 6: Register and verify**

Add `builder.Services.AddSingleton<IEmployeeIdentityStore, PreferencesEmployeeIdentityStore>();`
after the `IPreferencesStore` registration. Re-run the focused test; expected PASS.

- [ ] **Step 7: Commit**

```powershell
git add ONEVO.Agent.TrayApp/Services tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakePreferencesStore.cs tests/ONEVO.Agent.TrayApp.Tests/Services/PreferencesEmployeeIdentityStoreTests.cs ONEVO.Agent.TrayApp/MauiProgram.cs
git commit -m "refactor(tray): centralize employee identity cache"
```
