# DEV4 Task 2: Windows Monitoring Agent Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the ONEVO Windows Monitoring Agent — a Windows Service + MAUI TrayApp that enrolls a device, collects activity/app/idle data, and syncs it to the HRMS backend via the Agent Gateway API.

**Architecture:** Four components: `ONEVO.Agent.Shared` (common types/IPC), `ONEVO.Agent.Service` (Windows Service, collectors, SQLite buffer, HTTP sync), `ONEVO.Agent.TrayApp` (MAUI system tray app, Named Pipe IPC client), and `ONEVO.Agent.Tests`. The Service and TrayApp communicate via a Named Pipe (`onevo-agent-ipc`). Device credentials are stored with DPAPI. A minimal backend change adds `RedirectUri` to the enrollment challenge and makes `enroll/confirm` redirect back to the agent's local callback server.

**Tech Stack:** .NET 10, Windows Service (IHostedService), MAUI (net10.0-windows10.0.19041.0), SQLite (Microsoft.Data.Sqlite), System.Text.Json, Named Pipes (System.IO.Pipes), DPAPI (System.Security.Cryptography.ProtectedData), P/Invoke (user32.dll, kernel32.dll), MediaCapture/OpenCV for photos, xUnit + Moq for tests.

---

## File Map

### Backend changes (HRMS-Backend-v1)
- Modify: `src/ONEVO.Domain/Features/AgentGateway/Entities/AgentEnrollmentChallenge.cs` — add `RedirectUri` nullable string
- Create: `src/ONEVO.Infrastructure/Persistence/Migrations/<timestamp>_AddRedirectUriToEnrollmentChallenge.cs`
- Modify: `src/ONEVO.Application/Features/AgentGateway/Commands/ConfirmEnrollment/ConfirmEnrollmentCommandHandler.cs` — return `ConfirmEnrollmentResult` with RedirectUri
- Create: `src/ONEVO.Application/Features/AgentGateway/Commands/ConfirmEnrollment/ConfirmEnrollmentResult.cs` — new DTO
- Modify: `src/ONEVO.Api/Controllers/Tenant/AgentGateway/AgentGatewayController.cs` — redirect to RedirectUri when present
- Modify: `src/ONEVO.Application/Features/AgentGateway/Commands/StartEnrollment/StartEnrollmentCommandHandler.cs` — persist RedirectUri from command
- Modify: `src/ONEVO.Application/Features/AgentGateway/Commands/StartEnrollment/StartEnrollmentCommand.cs` — add RedirectUri property

### New solution (C:\tmp\one backend\HRMS-Agent\)
- `HRMS-Agent.sln`
- `src/ONEVO.Agent.Shared/ONEVO.Agent.Shared.csproj`
  - `Constants.cs` — pipe name, API paths, file paths
  - `Models/PolicyModel.cs` — deserialized policy JSON
  - `IPC/IpcMessage.cs` — base message + all typed message records
  - `IPC/IpcMessageSerializer.cs` — JSON newline serializer/deserializer
- `src/ONEVO.Agent.Service/ONEVO.Agent.Service.csproj`
  - `Program.cs` — DI wiring, Windows Service host
  - `Infrastructure/SqliteBuffer.cs` — SQLite data buffer
  - `Infrastructure/DeviceTokenStore.cs` — DPAPI device JWT storage
  - `Infrastructure/AgentGatewayClient.cs` — HTTP client wrapper
  - `Infrastructure/LocalAuthCallbackServer.cs` — local HTTP listener for enrollment callback
  - `Infrastructure/NamedPipeServer.cs` — IPC server (BackgroundService)
  - `Collectors/ActivityCollector.cs` — keyboard/mouse hooks, counts per interval
  - `Collectors/AppTracker.cs` — foreground window polling
  - `Collectors/IdleDetector.cs` — GetLastInputInfo P/Invoke
  - `Services/EnrollmentService.cs` — enrollment flow orchestration
  - `Services/DataSyncService.cs` — batch upload from SQLite buffer
  - `Services/HeartbeatService.cs` — periodic heartbeat pings
  - `Services/PolicyService.cs` — policy fetch and cache
  - `Services/CollectorOrchestrator.cs` — starts/stops collectors based on policy
- `src/ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj`
  - `MauiProgram.cs` — MAUI host builder
  - `App.xaml / App.xaml.cs` — application entry
  - `Services/TrayIconService.cs` — system tray icon lifecycle
  - `Services/NamedPipeClient.cs` — IPC client (connects to Service pipe)
  - `Windows/LoginWindow.xaml / .xaml.cs` — enrollment UI
  - `Windows/StatusPopup.xaml / .xaml.cs` — status/logout popup
- `tests/ONEVO.Agent.Tests/ONEVO.Agent.Tests.csproj`
  - `SqliteBufferTests.cs`
  - `DeviceTokenStoreTests.cs`
  - `IpcMessageSerializerTests.cs`

---

### Task 1: Backend — Add RedirectUri to Enrollment Challenge

**Files:**
- Modify: `src/ONEVO.Domain/Features/AgentGateway/Entities/AgentEnrollmentChallenge.cs`
- Modify: `src/ONEVO.Application/Features/AgentGateway/Commands/StartEnrollment/StartEnrollmentCommand.cs`
- Modify: `src/ONEVO.Application/Features/AgentGateway/Commands/StartEnrollment/StartEnrollmentCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/AgentGateway/Commands/ConfirmEnrollment/ConfirmEnrollmentResult.cs`
- Modify: `src/ONEVO.Application/Features/AgentGateway/Commands/ConfirmEnrollment/ConfirmEnrollmentCommandHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/AgentGateway/AgentGatewayController.cs`
- Create: migration via `dotnet ef migrations add`

- [ ] **Step 1: Read current entity and command files**

```bash
# Read these files before editing:
# src/ONEVO.Domain/Features/AgentGateway/Entities/AgentEnrollmentChallenge.cs
# src/ONEVO.Application/Features/AgentGateway/Commands/StartEnrollment/StartEnrollmentCommand.cs
# src/ONEVO.Application/Features/AgentGateway/Commands/StartEnrollment/StartEnrollmentCommandHandler.cs
# src/ONEVO.Application/Features/AgentGateway/Commands/ConfirmEnrollment/ConfirmEnrollmentCommandHandler.cs
# src/ONEVO.Api/Controllers/Tenant/AgentGateway/AgentGatewayController.cs
```

- [ ] **Step 2: Add `RedirectUri` to domain entity**

In `AgentEnrollmentChallenge.cs`, add after `ExpiresAt`:
```csharp
public string? RedirectUri { get; set; }
```

- [ ] **Step 3: Add `RedirectUri` to StartEnrollment command and handler**

In `StartEnrollmentCommand.cs`, add:
```csharp
public string? RedirectUri { get; init; }
```

In `StartEnrollmentCommandHandler.cs`, when creating `AgentEnrollmentChallenge`, add:
```csharp
RedirectUri = request.RedirectUri,
```

- [ ] **Step 4: Create `ConfirmEnrollmentResult.cs`**

```csharp
namespace ONEVO.Application.Features.AgentGateway.Commands.ConfirmEnrollment;

public sealed record ConfirmEnrollmentResult(
    string AuthorizationCode,
    string? RedirectUri);
```

- [ ] **Step 5: Update `ConfirmEnrollmentCommandHandler` to return `ConfirmEnrollmentResult`**

Change handler signature from `Result<string>` to `Result<ConfirmEnrollmentResult>` and return:
```csharp
return Result<ConfirmEnrollmentResult>.Success(new ConfirmEnrollmentResult(
    AuthorizationCode: authCode,
    RedirectUri: challenge.RedirectUri));
```

- [ ] **Step 6: Update `AgentGatewayController.ConfirmEnrollment` action**

Read the controller. Find the `ConfirmEnrollment` action. After getting the result, add redirect logic:
```csharp
if (!string.IsNullOrEmpty(result.Value.RedirectUri))
{
    var redirectUrl = $"{result.Value.RedirectUri}?code={Uri.EscapeDataString(result.Value.AuthorizationCode)}";
    return Redirect(redirectUrl);
}
return Ok(new { authorization_code = result.Value.AuthorizationCode });
```

- [ ] **Step 7: Generate EF Core migration**

```bash
cd "C:\tmp\one backend\HRMS-Backend-v1"
dotnet ef migrations add AddRedirectUriToEnrollmentChallenge \
  --project src/ONEVO.Infrastructure \
  --startup-project src/ONEVO.Api \
  -c AppDbContext
```

Expected output: `Done. To undo this action, use 'ef migrations remove'`

- [ ] **Step 8: Build backend to verify no compile errors**

```bash
cd "C:\tmp\one backend\HRMS-Backend-v1"
dotnet build -c Release --no-restore 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 9: Apply migration**

```bash
cd "C:\tmp\one backend\HRMS-Backend-v1"
dotnet ef database update \
  --project src/ONEVO.Infrastructure \
  --startup-project src/ONEVO.Api \
  -c AppDbContext
```

Expected: `Done.`

- [ ] **Step 10: Commit backend changes**

```bash
cd "C:\tmp\one backend\HRMS-Backend-v1"
git add src/ONEVO.Domain/Features/AgentGateway/Entities/AgentEnrollmentChallenge.cs
git add src/ONEVO.Application/Features/AgentGateway/Commands/
git add src/ONEVO.Api/Controllers/Tenant/AgentGateway/AgentGatewayController.cs
git add src/ONEVO.Infrastructure/Persistence/Migrations/
git commit -m "feat(agent): add redirect_uri to enrollment challenge for agent callback"
```

---

### Task 2: Solution Scaffold + ONEVO.Agent.Shared

**Files:**
- Create: `C:\tmp\one backend\HRMS-Agent\HRMS-Agent.sln`
- Create: `C:\tmp\one backend\HRMS-Agent\src\ONEVO.Agent.Shared\ONEVO.Agent.Shared.csproj`
- Create: `C:\tmp\one backend\HRMS-Agent\src\ONEVO.Agent.Shared\Constants.cs`
- Create: `C:\tmp\one backend\HRMS-Agent\src\ONEVO.Agent.Shared\Models\PolicyModel.cs`
- Create: `C:\tmp\one backend\HRMS-Agent\src\ONEVO.Agent.Shared\IPC\IpcMessage.cs`
- Create: `C:\tmp\one backend\HRMS-Agent\src\ONEVO.Agent.Shared\IPC\IpcMessageSerializer.cs`
- Test: `C:\tmp\one backend\HRMS-Agent\tests\ONEVO.Agent.Tests\IpcMessageSerializerTests.cs`

- [ ] **Step 1: Create solution and Shared project**

```powershell
cd "C:\tmp\one backend\HRMS-Agent"
mkdir src, tests
dotnet new sln -n HRMS-Agent
dotnet new classlib -n ONEVO.Agent.Shared -f net10.0 -o src/ONEVO.Agent.Shared
dotnet sln add src/ONEVO.Agent.Shared/ONEVO.Agent.Shared.csproj
```

- [ ] **Step 2: Create `Constants.cs`**

```csharp
// src/ONEVO.Agent.Shared/Constants.cs
namespace ONEVO.Agent.Shared;

public static class AgentConstants
{
    public const string PipeName = "onevo-agent-ipc";
    public const string BufferDbPath = @"%LOCALAPPDATA%\ONEVO\Agent\agent_buffer.db";
    public const string TokenFilePath = @"%LOCALAPPDATA%\ONEVO\Agent\device_token.bin";
    public const string AgentIdFilePath = @"%LOCALAPPDATA%\ONEVO\Agent\agent_id.txt";
}
```

- [ ] **Step 3: Create `Models/PolicyModel.cs`**

```csharp
// src/ONEVO.Agent.Shared/Models/PolicyModel.cs
using System.Text.Json.Serialization;

namespace ONEVO.Agent.Shared.Models;

public sealed class PolicyModel
{
    [JsonPropertyName("activity_monitoring")]
    public bool ActivityMonitoring { get; set; }

    [JsonPropertyName("application_tracking")]
    public bool ApplicationTracking { get; set; }

    [JsonPropertyName("screenshot_capture")]
    public bool ScreenshotCapture { get; set; }

    [JsonPropertyName("heartbeat_interval_seconds")]
    public int HeartbeatIntervalSeconds { get; set; } = 60;
}
```

- [ ] **Step 4: Create `IPC/IpcMessage.cs` with all message types**

```csharp
// src/ONEVO.Agent.Shared/IPC/IpcMessage.cs
using System.Text.Json.Serialization;
using ONEVO.Agent.Shared.Models;

namespace ONEVO.Agent.Shared.IPC;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(StatusRequestMessage), "status_request")]
[JsonDerivedType(typeof(StatusResponseMessage), "status_response")]
[JsonDerivedType(typeof(EnrollRequestMessage), "enroll_request")]
[JsonDerivedType(typeof(EnrollResponseMessage), "enroll_response")]
[JsonDerivedType(typeof(LogoutRequestMessage), "logout_request")]
[JsonDerivedType(typeof(LogoutResponseMessage), "logout_response")]
[JsonDerivedType(typeof(PolicyUpdatedMessage), "policy_updated")]
[JsonDerivedType(typeof(ErrorMessage), "error")]
public abstract record IpcMessage;

public sealed record StatusRequestMessage : IpcMessage;

public sealed record StatusResponseMessage(
    bool IsEnrolled,
    bool IsOnline,
    string? EmployeeName,
    string? DeviceName,
    PolicyModel? Policy,
    DateTimeOffset? LastSync) : IpcMessage;

public sealed record EnrollRequestMessage(string TenantSubdomain, string BaseUrl) : IpcMessage;

public sealed record EnrollResponseMessage(bool Success, string? ErrorMessage = null) : IpcMessage;

public sealed record LogoutRequestMessage : IpcMessage;

public sealed record LogoutResponseMessage(bool Success) : IpcMessage;

public sealed record PolicyUpdatedMessage(PolicyModel Policy) : IpcMessage;

public sealed record ErrorMessage(string Message) : IpcMessage;
```

- [ ] **Step 5: Create `IPC/IpcMessageSerializer.cs`**

```csharp
// src/ONEVO.Agent.Shared/IPC/IpcMessageSerializer.cs
using System.Text.Json;

namespace ONEVO.Agent.Shared.IPC;

public static class IpcMessageSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string Serialize(IpcMessage message) =>
        JsonSerializer.Serialize(message, Options);

    public static IpcMessage? Deserialize(string json) =>
        JsonSerializer.Deserialize<IpcMessage>(json, Options);
}
```

- [ ] **Step 6: Create test project and write failing tests**

```powershell
dotnet new xunit -n ONEVO.Agent.Tests -f net10.0 -o tests/ONEVO.Agent.Tests
dotnet sln add tests/ONEVO.Agent.Tests/ONEVO.Agent.Tests.csproj
dotnet add tests/ONEVO.Agent.Tests/ONEVO.Agent.Tests.csproj reference src/ONEVO.Agent.Shared/ONEVO.Agent.Shared.csproj
```

Write `tests/ONEVO.Agent.Tests/IpcMessageSerializerTests.cs`:
```csharp
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;

namespace ONEVO.Agent.Tests;

public class IpcMessageSerializerTests
{
    [Fact]
    public void RoundTrip_StatusRequest()
    {
        var msg = new StatusRequestMessage();
        var json = IpcMessageSerializer.Serialize(msg);
        var result = IpcMessageSerializer.Deserialize(json);
        Assert.IsType<StatusRequestMessage>(result);
    }

    [Fact]
    public void RoundTrip_StatusResponse()
    {
        var policy = new PolicyModel { ActivityMonitoring = true, HeartbeatIntervalSeconds = 30 };
        var msg = new StatusResponseMessage(true, true, "John Doe", "LAPTOP-001", policy, DateTimeOffset.UtcNow);
        var json = IpcMessageSerializer.Serialize(msg);
        var result = Assert.IsType<StatusResponseMessage>(IpcMessageSerializer.Deserialize(json));
        Assert.Equal("John Doe", result.EmployeeName);
        Assert.True(result.Policy?.ActivityMonitoring);
    }

    [Fact]
    public void RoundTrip_EnrollRequest()
    {
        var msg = new EnrollRequestMessage("acme", "http://acme.localhost:6000");
        var json = IpcMessageSerializer.Serialize(msg);
        var result = Assert.IsType<EnrollRequestMessage>(IpcMessageSerializer.Deserialize(json));
        Assert.Equal("acme", result.TenantSubdomain);
    }
}
```

- [ ] **Step 7: Run tests (expect fail — project not built yet)**

```bash
cd "C:\tmp\one backend\HRMS-Agent"
dotnet test tests/ONEVO.Agent.Tests/ -v minimal 2>&1 | tail -10
```

Expected: build succeeds (types exist), 3 tests pass.

- [ ] **Step 8: Run tests**

```bash
dotnet test tests/ONEVO.Agent.Tests/ --filter "IpcMessageSerializerTests" -v minimal
```

Expected: `Passed! - 3 passed`

- [ ] **Step 9: Commit**

```bash
cd "C:\tmp\one backend\HRMS-Agent"
git init
git add .
git commit -m "feat: scaffold HRMS-Agent solution with ONEVO.Agent.Shared IPC types"
```

---

### Task 3: SqliteBuffer + DeviceTokenStore (DPAPI)

**Files:**
- Create: `src/ONEVO.Agent.Service/ONEVO.Agent.Service.csproj`
- Create: `src/ONEVO.Agent.Service/Infrastructure/SqliteBuffer.cs`
- Create: `src/ONEVO.Agent.Service/Infrastructure/DeviceTokenStore.cs`
- Test: `tests/ONEVO.Agent.Tests/SqliteBufferTests.cs`
- Test: `tests/ONEVO.Agent.Tests/DeviceTokenStoreTests.cs`

- [ ] **Step 1: Create Service project with NuGet packages**

```powershell
cd "C:\tmp\one backend\HRMS-Agent"
dotnet new worker -n ONEVO.Agent.Service -f net10.0-windows -o src/ONEVO.Agent.Service
dotnet sln add src/ONEVO.Agent.Service/ONEVO.Agent.Service.csproj
dotnet add src/ONEVO.Agent.Service/ONEVO.Agent.Service.csproj reference src/ONEVO.Agent.Shared/ONEVO.Agent.Shared.csproj
dotnet add src/ONEVO.Agent.Service/ONEVO.Agent.Service.csproj package Microsoft.Data.Sqlite
dotnet add src/ONEVO.Agent.Service/ONEVO.Agent.Service.csproj package Microsoft.Extensions.Hosting.WindowsServices
```

- [ ] **Step 2: Write failing `SqliteBufferTests.cs`**

```csharp
// tests/ONEVO.Agent.Tests/SqliteBufferTests.cs
using Microsoft.Data.Sqlite;
using ONEVO.Agent.Service.Infrastructure;

namespace ONEVO.Agent.Tests;

public class SqliteBufferTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.db");
    private readonly SqliteBuffer _buffer;

    public SqliteBufferTests()
    {
        _buffer = new SqliteBuffer(_dbPath);
        _buffer.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _buffer.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [Fact]
    public async Task Insert_And_GetBatch_Returns_Inserted_Row()
    {
        await _buffer.InsertActivityAsync("2026-07-23T10:00:00Z", 120, 45, 300, false);
        var batch = await _buffer.GetUnsentBatchAsync(10);
        Assert.Single(batch);
        Assert.Equal(120, batch[0].KeystrokeCount);
    }

    [Fact]
    public async Task MarkAsSent_Removes_From_Unsent_Batch()
    {
        await _buffer.InsertActivityAsync("2026-07-23T10:00:00Z", 10, 5, 60, false);
        var batch = await _buffer.GetUnsentBatchAsync(10);
        await _buffer.MarkAsSentAsync(batch.Select(r => r.Id).ToList());
        var after = await _buffer.GetUnsentBatchAsync(10);
        Assert.Empty(after);
    }
}
```

- [ ] **Step 3: Run test (expect compile failure — SqliteBuffer doesn't exist yet)**

```bash
dotnet build tests/ONEVO.Agent.Tests 2>&1 | grep "error"
```

Expected: `error CS0246: The type or namespace name 'SqliteBuffer'`

- [ ] **Step 4: Create `Infrastructure/SqliteBuffer.cs`**

```csharp
// src/ONEVO.Agent.Service/Infrastructure/SqliteBuffer.cs
using Microsoft.Data.Sqlite;

namespace ONEVO.Agent.Service.Infrastructure;

public sealed class SqliteBuffer : IDisposable
{
    private readonly string _dbPath;
    private SqliteConnection? _connection;

    public SqliteBuffer(string dbPath)
    {
        _dbPath = dbPath;
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        await _connection.OpenAsync();

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS activity_buffer (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                interval_start TEXT NOT NULL,
                keystroke_count INTEGER NOT NULL,
                mouse_click_count INTEGER NOT NULL,
                active_seconds INTEGER NOT NULL,
                is_idle INTEGER NOT NULL DEFAULT 0,
                sent_at TEXT,
                retry_count INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );
            CREATE TABLE IF NOT EXISTS agent_config (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS sync_state (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                last_synced_at TEXT,
                last_heartbeat_at TEXT
            );
            INSERT OR IGNORE INTO sync_state (id) VALUES (1);
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task InsertActivityAsync(
        string intervalStart, int keystrokeCount, int mouseClickCount,
        int activeSeconds, bool isIdle)
    {
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            INSERT INTO activity_buffer (interval_start, keystroke_count, mouse_click_count, active_seconds, is_idle)
            VALUES ($start, $keys, $clicks, $secs, $idle)
            """;
        cmd.Parameters.AddWithValue("$start", intervalStart);
        cmd.Parameters.AddWithValue("$keys", keystrokeCount);
        cmd.Parameters.AddWithValue("$clicks", mouseClickCount);
        cmd.Parameters.AddWithValue("$secs", activeSeconds);
        cmd.Parameters.AddWithValue("$idle", isIdle ? 1 : 0);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<ActivityRecord>> GetUnsentBatchAsync(int batchSize)
    {
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT id, interval_start, keystroke_count, mouse_click_count, active_seconds, is_idle
            FROM activity_buffer
            WHERE sent_at IS NULL AND retry_count < 10
            ORDER BY id
            LIMIT $size
            """;
        cmd.Parameters.AddWithValue("$size", batchSize);

        var results = new List<ActivityRecord>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new ActivityRecord(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5) == 1));
        }
        return results;
    }

    public async Task MarkAsSentAsync(IEnumerable<long> ids)
    {
        var idList = string.Join(",", ids);
        if (string.IsNullOrEmpty(idList)) return;

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = $"UPDATE activity_buffer SET sent_at = datetime('now') WHERE id IN ({idList})";
        await cmd.ExecuteNonQueryAsync();
    }

    public void Dispose() => _connection?.Dispose();
}

public sealed record ActivityRecord(
    long Id,
    string IntervalStart,
    int KeystrokeCount,
    int MouseClickCount,
    int ActiveSeconds,
    bool IsIdle);
```

- [ ] **Step 5: Run SqliteBuffer tests**

```bash
cd "C:\tmp\one backend\HRMS-Agent"
dotnet test tests/ONEVO.Agent.Tests/ --filter "SqliteBufferTests" -v minimal
```

Expected: `Passed! - 2 passed`

- [ ] **Step 6: Write failing `DeviceTokenStoreTests.cs`**

```csharp
// tests/ONEVO.Agent.Tests/DeviceTokenStoreTests.cs
using ONEVO.Agent.Service.Infrastructure;

namespace ONEVO.Agent.Tests;

public class DeviceTokenStoreTests : IDisposable
{
    private readonly string _tokenPath = Path.Combine(Path.GetTempPath(), $"test_token_{Guid.NewGuid()}.bin");
    private readonly string _agentIdPath = Path.Combine(Path.GetTempPath(), $"test_agentid_{Guid.NewGuid()}.txt");
    private readonly DeviceTokenStore _store;

    public DeviceTokenStoreTests()
    {
        _store = new DeviceTokenStore(_tokenPath, _agentIdPath);
    }

    public void Dispose()
    {
        if (File.Exists(_tokenPath)) File.Delete(_tokenPath);
        if (File.Exists(_agentIdPath)) File.Delete(_agentIdPath);
    }

    [Fact]
    public void Save_And_Load_Token_Roundtrips()
    {
        const string token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.test";
        _store.SaveToken(token);
        var loaded = _store.LoadToken();
        Assert.Equal(token, loaded);
    }

    [Fact]
    public void LoadToken_Returns_Null_When_No_File()
    {
        var result = _store.LoadToken();
        Assert.Null(result);
    }

    [Fact]
    public void Save_And_Load_AgentId_Roundtrips()
    {
        var id = Guid.NewGuid();
        _store.SaveAgentId(id);
        var loaded = _store.LoadAgentId();
        Assert.Equal(id, loaded);
    }
}
```

- [ ] **Step 7: Create `Infrastructure/DeviceTokenStore.cs`**

```csharp
// src/ONEVO.Agent.Service/Infrastructure/DeviceTokenStore.cs
using System.Security.Cryptography;
using System.Text;

namespace ONEVO.Agent.Service.Infrastructure;

public sealed class DeviceTokenStore
{
    private readonly string _tokenPath;
    private readonly string _agentIdPath;

    public DeviceTokenStore(string tokenPath, string agentIdPath)
    {
        _tokenPath = tokenPath;
        _agentIdPath = agentIdPath;
    }

    public void SaveToken(string token)
    {
        var plainBytes = Encoding.UTF8.GetBytes(token);
        var encrypted = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(_tokenPath)!);
        File.WriteAllBytes(_tokenPath, encrypted);
    }

    public string? LoadToken()
    {
        if (!File.Exists(_tokenPath)) return null;
        var encrypted = File.ReadAllBytes(_tokenPath);
        var plainBytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plainBytes);
    }

    public void ClearToken()
    {
        if (File.Exists(_tokenPath)) File.Delete(_tokenPath);
    }

    public void SaveAgentId(Guid agentId)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_agentIdPath)!);
        File.WriteAllText(_agentIdPath, agentId.ToString());
    }

    public Guid? LoadAgentId()
    {
        if (!File.Exists(_agentIdPath)) return null;
        var text = File.ReadAllText(_agentIdPath).Trim();
        return Guid.TryParse(text, out var id) ? id : null;
    }

    public void ClearAll()
    {
        ClearToken();
        if (File.Exists(_agentIdPath)) File.Delete(_agentIdPath);
    }
}
```

- [ ] **Step 8: Run DeviceTokenStore tests**

```bash
dotnet test tests/ONEVO.Agent.Tests/ --filter "DeviceTokenStoreTests" -v minimal
```

Expected: `Passed! - 3 passed` (DPAPI works on Windows)

- [ ] **Step 9: Commit**

```bash
cd "C:\tmp\one backend\HRMS-Agent"
git add .
git commit -m "feat(service): add SqliteBuffer and DeviceTokenStore with DPAPI"
```

---

### Task 4: AgentGatewayClient (HTTP client wrapper)

**Files:**
- Create: `src/ONEVO.Agent.Service/Infrastructure/AgentGatewayClient.cs`
- Create: `src/ONEVO.Agent.Service/Infrastructure/AgentApiModels.cs`

- [ ] **Step 1: Create `Infrastructure/AgentApiModels.cs`**

```csharp
// src/ONEVO.Agent.Service/Infrastructure/AgentApiModels.cs
using System.Text.Json.Serialization;
using ONEVO.Agent.Shared.Models;

namespace ONEVO.Agent.Service.Infrastructure;

public sealed record EnrollStartRequest(
    [property: JsonPropertyName("device_id")] string DeviceId,
    [property: JsonPropertyName("device_name")] string DeviceName,
    [property: JsonPropertyName("os_version")] string OsVersion,
    [property: JsonPropertyName("agent_version")] string AgentVersion,
    [property: JsonPropertyName("redirect_uri")] string? RedirectUri);

public sealed record EnrollStartResponse(
    [property: JsonPropertyName("enrollment_id")] Guid EnrollmentId,
    [property: JsonPropertyName("user_code")] string UserCode,
    [property: JsonPropertyName("verification_url")] string VerificationUrl,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt);

public sealed record EnrollCompleteRequest(
    [property: JsonPropertyName("enrollment_id")] Guid EnrollmentId,
    [property: JsonPropertyName("device_id")] string DeviceId,
    [property: JsonPropertyName("authorization_code")] string AuthorizationCode);

public sealed record EnrollCompleteResponse(
    [property: JsonPropertyName("agent_id")] Guid AgentId,
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("employee_id")] Guid EmployeeId,
    [property: JsonPropertyName("employee_name")] string EmployeeName,
    [property: JsonPropertyName("device_token")] string DeviceToken,
    [property: JsonPropertyName("token_expires_at")] DateTimeOffset TokenExpiresAt,
    [property: JsonPropertyName("policy_json")] string PolicyJson);

public sealed record AgentLoginRequest(
    [property: JsonPropertyName("agent_id")] Guid AgentId);

public sealed record AgentLoginResponse(
    [property: JsonPropertyName("employee_id")] Guid EmployeeId,
    [property: JsonPropertyName("employee_name")] string EmployeeName,
    [property: JsonPropertyName("policy_json")] string PolicyJson);

public sealed record HeartbeatRequest(
    [property: JsonPropertyName("agent_id")] Guid AgentId,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("is_active")] bool IsActive,
    [property: JsonPropertyName("active_app")] string? ActiveApp);

public sealed record ActivityIngestItem(
    [property: JsonPropertyName("interval_start")] string IntervalStart,
    [property: JsonPropertyName("keystroke_count")] int KeystrokeCount,
    [property: JsonPropertyName("mouse_click_count")] int MouseClickCount,
    [property: JsonPropertyName("active_seconds")] int ActiveSeconds,
    [property: JsonPropertyName("is_idle")] bool IsIdle);

public sealed record PolicyResponse(
    [property: JsonPropertyName("agent_id")] Guid AgentId,
    [property: JsonPropertyName("policy_json")] string PolicyJson);
```

- [ ] **Step 2: Create `Infrastructure/AgentGatewayClient.cs`**

```csharp
// src/ONEVO.Agent.Service/Infrastructure/AgentGatewayClient.cs
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ONEVO.Agent.Service.Infrastructure;

public sealed class AgentGatewayClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AgentGatewayClient(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    public void SetDeviceToken(string token)
    {
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public void ClearDeviceToken()
    {
        _http.DefaultRequestHeaders.Authorization = null;
    }

    public async Task<EnrollStartResponse?> EnrollStartAsync(EnrollStartRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("api/v1/agent/enroll/start", request, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<EnrollStartResponse>(JsonOptions, ct);
    }

    public async Task<EnrollCompleteResponse?> EnrollCompleteAsync(EnrollCompleteRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("api/v1/agent/enroll/complete", request, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<EnrollCompleteResponse>(JsonOptions, ct);
    }

    public async Task<AgentLoginResponse?> AgentLoginAsync(AgentLoginRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("api/v1/agent/login", request, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<AgentLoginResponse>(JsonOptions, ct);
    }

    public async Task HeartbeatAsync(HeartbeatRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("api/v1/agent/heartbeat", request, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task IngestActivityAsync(IEnumerable<ActivityIngestItem> items, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("api/v1/agent/ingest", new { events = items }, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<PolicyResponse?> GetPolicyAsync(Guid agentId, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"api/v1/agent/policy/{agentId}", ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<PolicyResponse>(JsonOptions, ct);
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        await _http.PostAsync("api/v1/agent/logout", null, ct);
    }
}
```

- [ ] **Step 3: Build to verify no errors**

```bash
cd "C:\tmp\one backend\HRMS-Agent"
dotnet build src/ONEVO.Agent.Service -c Release 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add .
git commit -m "feat(service): add AgentGatewayClient HTTP wrapper and API models"
```

---

### Task 5: LocalAuthCallbackServer + EnrollmentService

**Files:**
- Create: `src/ONEVO.Agent.Service/Infrastructure/LocalAuthCallbackServer.cs`
- Create: `src/ONEVO.Agent.Service/Services/EnrollmentService.cs`

- [ ] **Step 1: Create `Infrastructure/LocalAuthCallbackServer.cs`**

This starts a local HTTP listener, waits for the browser to redirect back with `?code=...`, then shuts down.

```csharp
// src/ONEVO.Agent.Service/Infrastructure/LocalAuthCallbackServer.cs
using System.Net;

namespace ONEVO.Agent.Service.Infrastructure;

public sealed class LocalAuthCallbackServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly int _port;

    public LocalAuthCallbackServer()
    {
        _port = FindFreePort();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/callback/");
        _listener.Start();
    }

    public string CallbackUri => $"http://127.0.0.1:{_port}/callback/";

    public async Task<string> WaitForCodeAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var ctx = await _listener.GetContextAsync().WaitAsync(ct);
            var code = ctx.Request.QueryString["code"];

            var responseBody = code is not null
                ? "<html><body><h2>Enrollment complete! You may close this tab.</h2></body></html>"
                : "<html><body><h2>Enrollment failed. Please try again.</h2></body></html>";

            var buffer = System.Text.Encoding.UTF8.GetBytes(responseBody);
            ctx.Response.ContentLength64 = buffer.Length;
            ctx.Response.ContentType = "text/html";
            await ctx.Response.OutputStream.WriteAsync(buffer, ct);
            ctx.Response.Close();

            if (code is not null) return code;
        }
        throw new OperationCanceledException(ct);
    }

    public void Dispose() => _listener.Stop();

    private static int FindFreePort()
    {
        using var sock = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        sock.Start();
        var port = ((IPEndPoint)sock.LocalEndpoint).Port;
        sock.Stop();
        return port;
    }
}
```

- [ ] **Step 2: Create `Services/EnrollmentService.cs`**

```csharp
// src/ONEVO.Agent.Service/Services/EnrollmentService.cs
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using ONEVO.Agent.Service.Infrastructure;

namespace ONEVO.Agent.Service.Services;

public sealed class EnrollmentService
{
    private readonly AgentGatewayClient _client;
    private readonly DeviceTokenStore _tokenStore;
    private readonly ILogger<EnrollmentService> _logger;

    public EnrollmentService(
        AgentGatewayClient client,
        DeviceTokenStore tokenStore,
        ILogger<EnrollmentService> logger)
    {
        _client = client;
        _tokenStore = tokenStore;
        _logger = logger;
    }

    public async Task<bool> EnrollAsync(string tenantSubdomain, string baseUrl, CancellationToken ct)
    {
        using var callbackServer = new LocalAuthCallbackServer();

        var deviceId = GetOrCreateDeviceId();
        var deviceName = Environment.MachineName;
        var osVersion = RuntimeInformation.OSDescription;
        var agentVersion = "1.0.0";

        _logger.LogInformation("Starting enrollment for device {DeviceId} on tenant {Tenant}", deviceId, tenantSubdomain);

        var startReq = new EnrollStartRequest(
            DeviceId: deviceId,
            DeviceName: deviceName,
            OsVersion: osVersion,
            AgentVersion: agentVersion,
            RedirectUri: callbackServer.CallbackUri);

        var startResp = await _client.EnrollStartAsync(startReq, ct);
        if (startResp is null)
        {
            _logger.LogError("Failed to start enrollment");
            return false;
        }

        _logger.LogInformation("Enrollment started. User code: {Code}. Opening browser...", startResp.UserCode);

        // Open browser at verification URL
        OpenBrowser(startResp.VerificationUrl);

        // Wait for callback with authorization code
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(startResp.ExpiresAt - DateTimeOffset.UtcNow);

        string code;
        try
        {
            code = await callbackServer.WaitForCodeAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Enrollment timed out or was cancelled.");
            return false;
        }

        _logger.LogInformation("Received authorization code. Completing enrollment...");

        var completeReq = new EnrollCompleteRequest(
            EnrollmentId: startResp.EnrollmentId,
            DeviceId: deviceId,
            AuthorizationCode: code);

        var completeResp = await _client.EnrollCompleteAsync(completeReq, ct);
        if (completeResp is null)
        {
            _logger.LogError("Failed to complete enrollment");
            return false;
        }

        _tokenStore.SaveToken(completeResp.DeviceToken);
        _tokenStore.SaveAgentId(completeResp.AgentId);
        _client.SetDeviceToken(completeResp.DeviceToken);

        _logger.LogInformation("Enrollment complete. Agent {AgentId} for employee {Name}",
            completeResp.AgentId, completeResp.EmployeeName);

        return true;
    }

    private static string GetOrCreateDeviceId()
    {
        var path = Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\ONEVO\Agent\device_id.txt");
        if (File.Exists(path)) return File.ReadAllText(path).Trim();

        var id = Guid.NewGuid().ToString();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, id);
        return id;
    }

    private static void OpenBrowser(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
```

- [ ] **Step 3: Build**

```bash
cd "C:\tmp\one backend\HRMS-Agent"
dotnet build src/ONEVO.Agent.Service -c Release 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add .
git commit -m "feat(service): add LocalAuthCallbackServer and EnrollmentService"
```

---

### Task 6: NamedPipeServer + IPC Message Handlers

**Files:**
- Create: `src/ONEVO.Agent.Service/Infrastructure/NamedPipeServer.cs`

- [ ] **Step 1: Create `Infrastructure/NamedPipeServer.cs`**

The server runs as a `BackgroundService`, accepts multiple IPC connections concurrently, and dispatches messages to registered handlers.

```csharp
// src/ONEVO.Agent.Service/Infrastructure/NamedPipeServer.cs
using System.IO.Pipes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Agent.Shared;
using ONEVO.Agent.Shared.IPC;

namespace ONEVO.Agent.Service.Infrastructure;

public sealed class NamedPipeServer : BackgroundService
{
    private readonly ILogger<NamedPipeServer> _logger;
    private readonly Func<IpcMessage, Task<IpcMessage?>> _messageHandler;

    public NamedPipeServer(ILogger<NamedPipeServer> logger, Func<IpcMessage, Task<IpcMessage?>> messageHandler)
    {
        _logger = logger;
        _messageHandler = messageHandler;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Named pipe server starting on pipe '{Pipe}'", AgentConstants.PipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(
                AgentConstants.PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken);
                _ = HandleClientAsync(pipe, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting pipe connection");
                await pipe.DisposeAsync();
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        await using (pipe)
        {
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

            try
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) return;

                var message = IpcMessageSerializer.Deserialize(line);
                if (message is null) return;

                var response = await _messageHandler(message);
                if (response is not null)
                {
                    await writer.WriteLineAsync(IpcMessageSerializer.Serialize(response));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error handling pipe message");
                try
                {
                    var error = IpcMessageSerializer.Serialize(new ErrorMessage(ex.Message));
                    await writer.WriteLineAsync(error);
                }
                catch { }
            }
        }
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build src/ONEVO.Agent.Service -c Release 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add .
git commit -m "feat(service): add NamedPipeServer BackgroundService for IPC"
```

---

### Task 7: Core Collectors (ActivityCollector, AppTracker, IdleDetector)

**Files:**
- Create: `src/ONEVO.Agent.Service/Collectors/ActivityCollector.cs`
- Create: `src/ONEVO.Agent.Service/Collectors/AppTracker.cs`
- Create: `src/ONEVO.Agent.Service/Collectors/IdleDetector.cs`
- Create: `src/ONEVO.Agent.Service/Services/CollectorOrchestrator.cs`

- [ ] **Step 1: Create `Collectors/IdleDetector.cs`**

```csharp
// src/ONEVO.Agent.Service/Collectors/IdleDetector.cs
using System.Runtime.InteropServices;

namespace ONEVO.Agent.Service.Collectors;

public static class IdleDetector
{
    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo plii);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint cbSize;
        public uint dwTime;
    }

    public static TimeSpan GetIdleTime()
    {
        var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf(typeof(LastInputInfo)) };
        GetLastInputInfo(ref info);
        return TimeSpan.FromMilliseconds(Environment.TickCount - info.dwTime);
    }

    public static bool IsIdle(TimeSpan threshold) => GetIdleTime() >= threshold;
}
```

- [ ] **Step 2: Create `Collectors/AppTracker.cs`**

```csharp
// src/ONEVO.Agent.Service/Collectors/AppTracker.cs
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ONEVO.Agent.Service.Collectors;

public sealed class AppTracker
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    public sealed record ActiveAppInfo(string ProcessName, string WindowTitleHash);

    public ActiveAppInfo? GetActiveApp()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;

        GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            var process = Process.GetProcessById((int)pid);
            var titleBuilder = new StringBuilder(256);
            GetWindowText(hwnd, titleBuilder, 256);
            var titleHash = HashTitle(titleBuilder.ToString());
            return new ActiveAppInfo(process.ProcessName, titleHash);
        }
        catch
        {
            return null;
        }
    }

    private static string HashTitle(string title)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(title));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}
```

- [ ] **Step 3: Create `Collectors/ActivityCollector.cs`**

Uses a low-level keyboard hook to count keystrokes per interval:

```csharp
// src/ONEVO.Agent.Service/Collectors/ActivityCollector.cs
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace ONEVO.Agent.Service.Collectors;

public sealed class ActivityCollector : IDisposable
{
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;

    private IntPtr _keyHook = IntPtr.Zero;
    private IntPtr _mouseHook = IntPtr.Zero;
    private HookProc? _keyHookProc;
    private HookProc? _mouseHookProc;

    private int _keystrokeCount;
    private int _mouseClickCount;
    private readonly ILogger<ActivityCollector> _logger;

    public ActivityCollector(ILogger<ActivityCollector> logger)
    {
        _logger = logger;
    }

    public void Start()
    {
        _keyHookProc = KeyboardHookCallback;
        _mouseHookProc = MouseHookCallback;

        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        var moduleHandle = GetModuleHandle(curModule.ModuleName);

        _keyHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyHookProc, moduleHandle, 0);
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseHookProc, moduleHandle, 0);
        _logger.LogDebug("Activity collector hooks installed");
    }

    public (int Keystrokes, int MouseClicks) ConsumeAndReset()
    {
        var keys = Interlocked.Exchange(ref _keystrokeCount, 0);
        var clicks = Interlocked.Exchange(ref _mouseClickCount, 0);
        return (keys, clicks);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == WM_KEYDOWN)
            Interlocked.Increment(ref _keystrokeCount);
        return CallNextHookEx(_keyHook, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == WM_LBUTTONDOWN)
            Interlocked.Increment(ref _mouseClickCount);
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_keyHook != IntPtr.Zero) UnhookWindowsHookEx(_keyHook);
        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
    }
}
```

- [ ] **Step 4: Create `Services/CollectorOrchestrator.cs`**

```csharp
// src/ONEVO.Agent.Service/Services/CollectorOrchestrator.cs
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Agent.Service.Collectors;
using ONEVO.Agent.Service.Infrastructure;
using ONEVO.Agent.Shared.Models;

namespace ONEVO.Agent.Service.Services;

public sealed class CollectorOrchestrator : BackgroundService
{
    private readonly ActivityCollector _activity;
    private readonly AppTracker _appTracker;
    private readonly SqliteBuffer _buffer;
    private readonly ILogger<CollectorOrchestrator> _logger;
    private PolicyModel _policy = new();
    private static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CollectInterval = TimeSpan.FromSeconds(60);

    public CollectorOrchestrator(
        ActivityCollector activity,
        AppTracker appTracker,
        SqliteBuffer buffer,
        ILogger<CollectorOrchestrator> logger)
    {
        _activity = activity;
        _appTracker = appTracker;
        _buffer = buffer;
        _logger = logger;
    }

    public void UpdatePolicy(PolicyModel policy)
    {
        _policy = policy;
        if (policy.ActivityMonitoring)
            _activity.Start();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Collector orchestrator started");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(CollectInterval, stoppingToken);

            if (!_policy.ActivityMonitoring) continue;

            var intervalStart = DateTimeOffset.UtcNow.ToString("O");
            var (keystrokes, clicks) = _activity.ConsumeAndReset();
            var isIdle = IdleDetector.IsIdle(IdleThreshold);
            var activeSeconds = isIdle ? 0 : (int)CollectInterval.TotalSeconds;

            await _buffer.InsertActivityAsync(intervalStart, keystrokes, clicks, activeSeconds, isIdle);
            _logger.LogDebug("Collected: keys={K} clicks={C} idle={I}", keystrokes, clicks, isIdle);
        }
    }
}
```

- [ ] **Step 5: Build**

```bash
dotnet build src/ONEVO.Agent.Service -c Release 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
git add .
git commit -m "feat(service): add ActivityCollector, AppTracker, IdleDetector, CollectorOrchestrator"
```

---

### Task 8: DataSyncService + HeartbeatService + PolicyService

**Files:**
- Create: `src/ONEVO.Agent.Service/Services/DataSyncService.cs`
- Create: `src/ONEVO.Agent.Service/Services/HeartbeatService.cs`
- Create: `src/ONEVO.Agent.Service/Services/PolicyService.cs`

- [ ] **Step 1: Create `Services/PolicyService.cs`**

```csharp
// src/ONEVO.Agent.Service/Services/PolicyService.cs
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ONEVO.Agent.Service.Infrastructure;
using ONEVO.Agent.Shared.Models;

namespace ONEVO.Agent.Service.Services;

public sealed class PolicyService
{
    private readonly AgentGatewayClient _client;
    private readonly ILogger<PolicyService> _logger;
    private PolicyModel _cached = new();

    public PolicyService(AgentGatewayClient client, ILogger<PolicyService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public PolicyModel Current => _cached;

    public async Task<PolicyModel> FetchAndCacheAsync(Guid agentId, CancellationToken ct)
    {
        try
        {
            var resp = await _client.GetPolicyAsync(agentId, ct);
            if (resp is not null)
            {
                _cached = JsonSerializer.Deserialize<PolicyModel>(resp.PolicyJson) ?? _cached;
                _logger.LogDebug("Policy refreshed: activityMonitoring={A}", _cached.ActivityMonitoring);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch policy, using cached version");
        }
        return _cached;
    }
}
```

- [ ] **Step 2: Create `Services/HeartbeatService.cs`**

```csharp
// src/ONEVO.Agent.Service/Services/HeartbeatService.cs
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Agent.Service.Collectors;
using ONEVO.Agent.Service.Infrastructure;

namespace ONEVO.Agent.Service.Services;

public sealed class HeartbeatService : BackgroundService
{
    private readonly AgentGatewayClient _client;
    private readonly DeviceTokenStore _tokenStore;
    private readonly AppTracker _appTracker;
    private readonly PolicyService _policy;
    private readonly ILogger<HeartbeatService> _logger;

    public HeartbeatService(
        AgentGatewayClient client,
        DeviceTokenStore tokenStore,
        AppTracker appTracker,
        PolicyService policy,
        ILogger<HeartbeatService> logger)
    {
        _client = client;
        _tokenStore = tokenStore;
        _appTracker = appTracker;
        _policy = policy;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = TimeSpan.FromSeconds(_policy.Current.HeartbeatIntervalSeconds);
            await Task.Delay(interval, stoppingToken);

            var agentId = _tokenStore.LoadAgentId();
            if (agentId is null) continue;

            var activeApp = _appTracker.GetActiveApp()?.ProcessName;
            var isIdle = IdleDetector.IsIdle(TimeSpan.FromMinutes(5));

            try
            {
                await _client.HeartbeatAsync(new HeartbeatRequest(
                    AgentId: agentId.Value,
                    Timestamp: DateTimeOffset.UtcNow,
                    IsActive: !isIdle,
                    ActiveApp: activeApp), stoppingToken);
                _logger.LogDebug("Heartbeat sent");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Heartbeat failed");
            }
        }
    }
}
```

- [ ] **Step 3: Create `Services/DataSyncService.cs`**

```csharp
// src/ONEVO.Agent.Service/Services/DataSyncService.cs
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Agent.Service.Infrastructure;

namespace ONEVO.Agent.Service.Services;

public sealed class DataSyncService : BackgroundService
{
    private readonly AgentGatewayClient _client;
    private readonly SqliteBuffer _buffer;
    private readonly ILogger<DataSyncService> _logger;
    private static readonly TimeSpan SyncInterval = TimeSpan.FromSeconds(30);
    private const int BatchSize = 50;

    public DataSyncService(
        AgentGatewayClient client,
        SqliteBuffer buffer,
        ILogger<DataSyncService> logger)
    {
        _client = client;
        _buffer = buffer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(SyncInterval, stoppingToken);
            await SyncBatchAsync(stoppingToken);
        }
    }

    private async Task SyncBatchAsync(CancellationToken ct)
    {
        try
        {
            var batch = await _buffer.GetUnsentBatchAsync(BatchSize);
            if (batch.Count == 0) return;

            var items = batch.Select(r => new ActivityIngestItem(
                IntervalStart: r.IntervalStart,
                KeystrokeCount: r.KeystrokeCount,
                MouseClickCount: r.MouseClickCount,
                ActiveSeconds: r.ActiveSeconds,
                IsIdle: r.IsIdle)).ToList();

            await _client.IngestActivityAsync(items, ct);
            await _buffer.MarkAsSentAsync(batch.Select(r => r.Id));

            _logger.LogInformation("Synced {Count} activity records", batch.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Data sync failed, will retry");
        }
    }
}
```

- [ ] **Step 4: Build**

```bash
dotnet build src/ONEVO.Agent.Service -c Release 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat(service): add DataSyncService, HeartbeatService, PolicyService"
```

---

### Task 9: ONEVO.Agent.Service Program.cs (DI wiring + Windows Service host)

**Files:**
- Modify: `src/ONEVO.Agent.Service/Program.cs`

- [ ] **Step 1: Write `Program.cs`**

```csharp
// src/ONEVO.Agent.Service/Program.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Agent.Service.Collectors;
using ONEVO.Agent.Service.Infrastructure;
using ONEVO.Agent.Service.Services;
using ONEVO.Agent.Shared;
using ONEVO.Agent.Shared.IPC;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.UseWindowsService(options =>
    options.ServiceName = "ONEVO Agent Service");

// Configuration
var baseUrl = builder.Configuration["Agent:BaseUrl"] ?? "http://acme.localhost:6000";

// Infrastructure singletons
var bufferPath = Environment.ExpandEnvironmentVariables(AgentConstants.BufferDbPath);
var tokenPath = Environment.ExpandEnvironmentVariables(AgentConstants.TokenFilePath);
var agentIdPath = Environment.ExpandEnvironmentVariables(AgentConstants.AgentIdFilePath);

var buffer = new ONEVO.Agent.Service.Infrastructure.SqliteBuffer(bufferPath);
await buffer.InitializeAsync();

var tokenStore = new DeviceTokenStore(tokenPath, agentIdPath);
var gatewayClient = new AgentGatewayClient(baseUrl);

var existingToken = tokenStore.LoadToken();
if (existingToken is not null)
    gatewayClient.SetDeviceToken(existingToken);

builder.Services.AddSingleton(buffer);
builder.Services.AddSingleton(tokenStore);
builder.Services.AddSingleton(gatewayClient);
builder.Services.AddSingleton<AppTracker>();
builder.Services.AddSingleton<PolicyService>();
builder.Services.AddSingleton<EnrollmentService>();
builder.Services.AddSingleton<CollectorOrchestrator>();

// ActivityCollector needs to be singleton so hook lifetime matches process
builder.Services.AddSingleton<ActivityCollector>();

// IPC handler wiring
builder.Services.AddSingleton<Func<IpcMessage, Task<IpcMessage?>>>(sp =>
{
    var enrollment = sp.GetRequiredService<EnrollmentService>();
    var tokenSt = sp.GetRequiredService<DeviceTokenStore>();
    var policyService = sp.GetRequiredService<PolicyService>();
    var client = sp.GetRequiredService<AgentGatewayClient>();
    var logger = sp.GetRequiredService<ILogger<NamedPipeServer>>();

    return async message =>
    {
        switch (message)
        {
            case StatusRequestMessage:
                var agentId = tokenSt.LoadAgentId();
                return new StatusResponseMessage(
                    IsEnrolled: agentId is not null,
                    IsOnline: true,
                    EmployeeName: null,
                    DeviceName: Environment.MachineName,
                    Policy: policyService.Current,
                    LastSync: null);

            case EnrollRequestMessage enroll:
                var success = await enrollment.EnrollAsync(enroll.TenantSubdomain, enroll.BaseUrl, CancellationToken.None);
                return new EnrollResponseMessage(success);

            case LogoutRequestMessage:
                try { await client.LogoutAsync(); } catch { }
                tokenSt.ClearAll();
                client.ClearDeviceToken();
                return new LogoutResponseMessage(true);

            default:
                logger.LogWarning("Unknown IPC message: {Type}", message.GetType().Name);
                return new ErrorMessage($"Unknown message type: {message.GetType().Name}");
        }
    };
});

// Background services
builder.Services.AddHostedService<NamedPipeServer>();
builder.Services.AddHostedService<CollectorOrchestrator>();
builder.Services.AddHostedService<HeartbeatService>();
builder.Services.AddHostedService<DataSyncService>();

var host = builder.Build();
await host.RunAsync();
```

- [ ] **Step 2: Add `appsettings.json` for local config**

```json
{
  "Agent": {
    "BaseUrl": "http://acme.localhost:6000"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  }
}
```

- [ ] **Step 3: Build**

```bash
dotnet build src/ONEVO.Agent.Service -c Release 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add .
git commit -m "feat(service): wire up DI and BackgroundServices in Program.cs"
```

---

### Task 10: ONEVO.Agent.TrayApp — MAUI Scaffold + NamedPipeClient + TrayIconService

**Files:**
- Create: `src/ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj`
- Create: `src/ONEVO.Agent.TrayApp/MauiProgram.cs`
- Create: `src/ONEVO.Agent.TrayApp/App.xaml / App.xaml.cs`
- Create: `src/ONEVO.Agent.TrayApp/Services/NamedPipeClient.cs`
- Create: `src/ONEVO.Agent.TrayApp/Services/TrayIconService.cs`

- [ ] **Step 1: Create MAUI project**

```powershell
cd "C:\tmp\one backend\HRMS-Agent"
dotnet new maui -n ONEVO.Agent.TrayApp -f net10.0-windows10.0.19041.0 -o src/ONEVO.Agent.TrayApp
dotnet sln add src/ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj
dotnet add src/ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj reference src/ONEVO.Agent.Shared/ONEVO.Agent.Shared.csproj
```

- [ ] **Step 2: Edit `.csproj` to target Windows only**

In `ONEVO.Agent.TrayApp.csproj`, change `<TargetFrameworks>` to:
```xml
<TargetFrameworks>net10.0-windows10.0.19041.0</TargetFrameworks>
```

Remove Android/iOS/macOS target frameworks.

- [ ] **Step 3: Create `Services/NamedPipeClient.cs`**

```csharp
// src/ONEVO.Agent.TrayApp/Services/NamedPipeClient.cs
using System.IO.Pipes;
using ONEVO.Agent.Shared;
using ONEVO.Agent.Shared.IPC;

namespace ONEVO.Agent.TrayApp.Services;

public sealed class NamedPipeClient
{
    public async Task<IpcMessage?> SendAsync(IpcMessage message, CancellationToken ct = default)
    {
        using var pipe = new NamedPipeClientStream(".", AgentConstants.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        await pipe.ConnectAsync(cts.Token);

        await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, leaveOpen: true);

        await writer.WriteLineAsync(IpcMessageSerializer.Serialize(message));

        var response = await reader.ReadLineAsync(cts.Token);
        return response is not null ? IpcMessageSerializer.Deserialize(response) : null;
    }
}
```

- [ ] **Step 4: Create `Services/TrayIconService.cs`**

```csharp
// src/ONEVO.Agent.TrayApp/Services/TrayIconService.cs
using ONEVO.Agent.Shared.IPC;

namespace ONEVO.Agent.TrayApp.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NamedPipeClient _pipe;
    private NotifyIcon? _notifyIcon;
    private ContextMenuStrip? _menu;

    public TrayIconService(NamedPipeClient pipe)
    {
        _pipe = pipe;
    }

    public void Initialize()
    {
        _menu = new ContextMenuStrip();
        _menu.Items.Add("Status", null, OnStatusClicked);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Logout", null, OnLogoutClicked);
        _menu.Items.Add("Exit", null, OnExitClicked);

        _notifyIcon = new NotifyIcon
        {
            Text = "ONEVO Agent",
            Icon = SystemIcons.Application,
            ContextMenuStrip = _menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += OnStatusClicked;
    }

    private void OnStatusClicked(object? sender, EventArgs e)
    {
        // TODO Task 12: open StatusPopup
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var resp = await _pipe.SendAsync(new LogoutRequestMessage());
        var msg = resp is LogoutResponseMessage { Success: true }
            ? "Logged out successfully."
            : "Logout failed or timed out.";
        MessageBox.Show(msg, "ONEVO Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OnExitClicked(object? sender, EventArgs e)
    {
        Application.Exit();
    }

    public void Dispose()
    {
        _notifyIcon?.Dispose();
        _menu?.Dispose();
    }
}
```

- [ ] **Step 5: Update `MauiProgram.cs` to use WinForms tray and register services**

```csharp
// src/ONEVO.Agent.TrayApp/MauiProgram.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using ONEVO.Agent.TrayApp.Services;

namespace ONEVO.Agent.TrayApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddSingleton<NamedPipeClient>();
        builder.Services.AddSingleton<TrayIconService>();

        return builder.Build();
    }
}
```

- [ ] **Step 6: Update `App.xaml.cs` to initialize tray without main window**

```csharp
// src/ONEVO.Agent.TrayApp/App.xaml.cs
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Agent.TrayApp.Services;

namespace ONEVO.Agent.TrayApp;

public partial class App : Application
{
    public App(IServiceProvider services)
    {
        InitializeComponent();
        MainPage = new ContentPage(); // hidden, required by MAUI
        services.GetRequiredService<TrayIconService>().Initialize();
    }
}
```

- [ ] **Step 7: Build TrayApp**

```bash
dotnet build src/ONEVO.Agent.TrayApp -c Release 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 8: Commit**

```bash
git add .
git commit -m "feat(trayapp): scaffold MAUI TrayApp with NamedPipeClient and TrayIconService"
```

---

### Task 11: TrayApp LoginWindow (Enrollment UI)

**Files:**
- Create: `src/ONEVO.Agent.TrayApp/Windows/LoginWindow.xaml`
- Create: `src/ONEVO.Agent.TrayApp/Windows/LoginWindow.xaml.cs`

- [ ] **Step 1: Create `Windows/LoginWindow.xaml`**

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             x:Class="ONEVO.Agent.TrayApp.Windows.LoginWindow"
             Title="ONEVO Agent — Enroll Device"
             WidthRequest="400"
             HeightRequest="450">
    <VerticalStackLayout Padding="32" Spacing="16">
        <Label Text="ONEVO Agent Setup"
               FontSize="22"
               FontAttributes="Bold"
               HorizontalOptions="Center"/>

        <Label Text="Enter your company's workspace URL to connect this device."
               HorizontalOptions="Center"
               HorizontalTextAlignment="Center"/>

        <Label Text="Workspace URL (e.g. acme.onevo.app)"/>
        <Entry x:Name="TenantEntry"
               Placeholder="yourcompany.onevo.app"
               Keyboard="Url"/>

        <Button x:Name="EnrollButton"
                Text="Connect Device"
                Clicked="OnEnrollClicked"/>

        <ActivityIndicator x:Name="Spinner" IsVisible="false" IsRunning="false"/>

        <Label x:Name="StatusLabel" IsVisible="false" HorizontalOptions="Center"/>
    </VerticalStackLayout>
</ContentPage>
```

- [ ] **Step 2: Create `Windows/LoginWindow.xaml.cs`**

```csharp
// src/ONEVO.Agent.TrayApp/Windows/LoginWindow.xaml.cs
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Services;

namespace ONEVO.Agent.TrayApp.Windows;

public partial class LoginWindow : ContentPage
{
    private readonly NamedPipeClient _pipe;

    public LoginWindow(NamedPipeClient pipe)
    {
        InitializeComponent();
        _pipe = pipe;
    }

    private async void OnEnrollClicked(object sender, EventArgs e)
    {
        var workspace = TenantEntry.Text?.Trim();
        if (string.IsNullOrEmpty(workspace))
        {
            StatusLabel.Text = "Please enter a workspace URL.";
            StatusLabel.IsVisible = true;
            return;
        }

        // Parse tenant subdomain and base URL
        // Expected format: "acme" or "acme.onevo.app"
        var subdomain = workspace.Split('.')[0];
        var baseUrl = $"http://{workspace}:6000"; // dev default

        EnrollButton.IsEnabled = false;
        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        StatusLabel.Text = "Opening browser for confirmation...";
        StatusLabel.IsVisible = true;

        try
        {
            var resp = await _pipe.SendAsync(new EnrollRequestMessage(subdomain, baseUrl));
            if (resp is EnrollResponseMessage { Success: true })
            {
                StatusLabel.Text = "Device enrolled successfully!";
                await Task.Delay(1500);
                await Shell.Current.Navigation.PopAsync();
            }
            else
            {
                StatusLabel.Text = "Enrollment failed. Please try again.";
            }
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error: {ex.Message}";
        }
        finally
        {
            EnrollButton.IsEnabled = true;
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
        }
    }
}
```

- [ ] **Step 3: Build**

```bash
dotnet build src/ONEVO.Agent.TrayApp -c Release 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add .
git commit -m "feat(trayapp): add LoginWindow enrollment UI"
```

---

### Task 12: TrayApp StatusPopup

**Files:**
- Create: `src/ONEVO.Agent.TrayApp/Windows/StatusPopup.xaml`
- Create: `src/ONEVO.Agent.TrayApp/Windows/StatusPopup.xaml.cs`

- [ ] **Step 1: Create `Windows/StatusPopup.xaml`**

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             x:Class="ONEVO.Agent.TrayApp.Windows.StatusPopup"
             Title="ONEVO Agent Status"
             WidthRequest="380"
             HeightRequest="320">
    <VerticalStackLayout Padding="24" Spacing="12">
        <Label Text="ONEVO Agent" FontSize="20" FontAttributes="Bold" HorizontalOptions="Center"/>

        <Grid ColumnDefinitions="*,*" RowDefinitions="Auto,Auto,Auto,Auto">
            <Label Grid.Row="0" Grid.Column="0" Text="Status:" FontAttributes="Bold"/>
            <Label x:Name="StatusLabel" Grid.Row="0" Grid.Column="1" Text="Loading..."/>

            <Label Grid.Row="1" Grid.Column="0" Text="Employee:" FontAttributes="Bold"/>
            <Label x:Name="EmployeeLabel" Grid.Row="1" Grid.Column="1" Text="—"/>

            <Label Grid.Row="2" Grid.Column="0" Text="Device:" FontAttributes="Bold"/>
            <Label x:Name="DeviceLabel" Grid.Row="2" Grid.Column="1" Text="—"/>

            <Label Grid.Row="3" Grid.Column="0" Text="Monitoring:" FontAttributes="Bold"/>
            <Label x:Name="MonitoringLabel" Grid.Row="3" Grid.Column="1" Text="—"/>
        </Grid>

        <Button Text="Logout" Clicked="OnLogoutClicked"/>
        <Button Text="Close" Clicked="OnCloseClicked"/>
    </VerticalStackLayout>
</ContentPage>
```

- [ ] **Step 2: Create `Windows/StatusPopup.xaml.cs`**

```csharp
// src/ONEVO.Agent.TrayApp/Windows/StatusPopup.xaml.cs
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Services;

namespace ONEVO.Agent.TrayApp.Windows;

public partial class StatusPopup : ContentPage
{
    private readonly NamedPipeClient _pipe;

    public StatusPopup(NamedPipeClient pipe)
    {
        InitializeComponent();
        _pipe = pipe;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshStatusAsync();
    }

    private async Task RefreshStatusAsync()
    {
        try
        {
            var resp = await _pipe.SendAsync(new StatusRequestMessage());
            if (resp is StatusResponseMessage status)
            {
                StatusLabel.Text = status.IsEnrolled ? (status.IsOnline ? "Online" : "Offline") : "Not enrolled";
                EmployeeLabel.Text = status.EmployeeName ?? "—";
                DeviceLabel.Text = status.DeviceName ?? Environment.MachineName;
                MonitoringLabel.Text = status.Policy?.ActivityMonitoring == true ? "Active" : "Disabled";
            }
        }
        catch
        {
            StatusLabel.Text = "Service unavailable";
        }
    }

    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        var resp = await _pipe.SendAsync(new LogoutRequestMessage());
        if (resp is LogoutResponseMessage { Success: true })
            await Navigation.PopAsync();
    }

    private async void OnCloseClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }
}
```

- [ ] **Step 3: Wire StatusPopup into TrayIconService.OnStatusClicked**

In `TrayIconService.cs`, replace the `OnStatusClicked` stub:
```csharp
private void OnStatusClicked(object? sender, EventArgs e)
{
    // Open StatusPopup via MAUI navigation
    MainThread.BeginInvokeOnMainThread(async () =>
    {
        var popup = new StatusPopup(_pipe);
        await Application.Current!.MainPage!.Navigation.PushAsync(popup);
    });
}
```

- [ ] **Step 4: Build final TrayApp**

```bash
dotnet build src/ONEVO.Agent.TrayApp -c Release 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat(trayapp): add StatusPopup with live status from Named Pipe"
```

---

### Task 13: End-to-End Manual Test

This task validates the full enrollment flow: backend → Service → TrayApp.

- [ ] **Step 1: Start the backend API**

```bash
cd "C:\tmp\one backend\HRMS-Backend-v1"
dotnet run --project src/ONEVO.Api -c Release --no-build &
```

Wait for: `Now listening on: http://localhost:6000`

- [ ] **Step 2: Start the Agent Service in console mode**

```bash
cd "C:\tmp\one backend\HRMS-Agent"
dotnet run --project src/ONEVO.Agent.Service -c Release
```

Expected: `Named pipe server starting on pipe 'onevo-agent-ipc'`

- [ ] **Step 3: Run TrayApp**

```bash
dotnet run --project src/ONEVO.Agent.TrayApp -c Release
```

Expected: ONEVO tray icon appears in system tray

- [ ] **Step 4: Trigger enrollment via TrayApp**

1. Double-click tray icon → LoginWindow opens
2. Type `acme.localhost:6000` in workspace field
3. Click "Connect Device"
4. Browser opens at `http://acme.localhost:6000/enroll/confirm?enrollment_id=...`
5. Log in as `owner@acme.test` / `Password123!`
6. Confirm enrollment in browser
7. Browser redirects to `http://127.0.0.1:{port}/callback?code=...`
8. TrayApp shows "Device enrolled successfully!"

- [ ] **Step 5: Verify status via tray**

1. Double-click tray icon → StatusPopup opens
2. Status: "Online", Employee name shown, Device name shown

- [ ] **Step 6: Verify activity sync**

Wait 60+ seconds, then check backend DB:
```bash
psql -U postgres -d OnevoDb -c "SELECT COUNT(*) FROM agent_activity WHERE tenant_id = 'da810816-3fed-4e71-9a44-f93e9b509bc7';"
```

Expected: count > 0 (activity rows synced)

- [ ] **Step 7: Verify heartbeat**

```bash
psql -U postgres -d OnevoDb -c "SELECT last_seen_at FROM agent_sessions WHERE tenant_id = 'da810816-3fed-4e71-9a44-f93e9b509bc7' ORDER BY created_at DESC LIMIT 1;"
```

Expected: `last_seen_at` recently updated

- [ ] **Step 8: Test logout**

1. Right-click tray icon → "Logout"
2. Confirm logout dialog appears
3. After logout: DPAPI token file deleted, service returns `IsEnrolled: false`

- [ ] **Step 9: Final commit**

```bash
cd "C:\tmp\one backend\HRMS-Agent"
git add .
git commit -m "test: end-to-end enrollment and monitoring verified"
```

---

## Self-Review

### Spec Coverage
- ✅ `enroll/start` with `redirect_uri` — Task 1
- ✅ `enroll/complete` + callback redirect — Tasks 1, 5
- ✅ DPAPI token storage — Task 3
- ✅ SQLite buffer for offline safety — Task 3, 7
- ✅ Named Pipe IPC (Service ↔ TrayApp) — Tasks 6, 10
- ✅ ActivityCollector (keyboard/mouse hooks) — Task 7
- ✅ AppTracker (foreground window) — Task 7
- ✅ IdleDetector (GetLastInputInfo) — Task 7
- ✅ HeartbeatService — Task 8
- ✅ DataSyncService (batch upload) — Task 8
- ✅ PolicyService (policy fetch + cache) — Task 8
- ✅ Windows Service host — Task 9
- ✅ MAUI TrayApp with tray icon — Task 10
- ✅ LoginWindow (enrollment UI) — Task 11
- ✅ StatusPopup (live status) — Task 12
- ✅ Logout via tray — Task 12

### Placeholder Scan
- No "TBD" or "TODO" items (TrayIconService `OnStatusClicked` stub is completed in Task 12 Step 3)
- All code blocks contain complete implementations
- Type names are consistent across tasks (`ActivityRecord`, `EnrollStartRequest`, `IpcMessage` subtypes)

### Type Consistency
- `ActivityRecord` defined in Task 3, used in Task 8 `DataSyncService` ✅
- `IpcMessage` subtypes defined in Task 2, used in Tasks 6, 9, 10, 11, 12 ✅
- `AgentGatewayClient` defined in Task 4, injected in Tasks 8, 9 ✅
- `DeviceTokenStore` defined in Task 3, used in Tasks 5, 8, 9 ✅
