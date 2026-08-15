# Verified Employee Check-In — Offline Fallback and Rollout Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add opt-in, encrypted, review-only offline check-in recovery and prove the complete Windows check-in system is safe and observable for tenant-by-tenant production rollout.

**Architecture:** Service permits offline fallback only from an unexpired DPAPI-protected backend policy cache, stores evidence in a dedicated ACL-restricted DPAPI spool, and synchronizes it idempotently before ordinary work-session completion. Backend retention jobs remove temporary evidence and revoked references; rollout uses feature flags and measured pilot acceptance.

**Tech Stack:** Parts 1–3, .NET 10 Windows Service, DPAPI LocalMachine, restricted Windows ACLs, SQLite metadata without image bytes, ASP.NET Core hosted jobs, PostgreSQL/RLS, private R2, structured metrics, xUnit.

**Spec:** `C:\HR\HRMS-Backend-v1\docs\superpowers\specs\next\2026-08-13-verified-employee-check-in-design.md`

## Global Constraints

- Offline fallback is disabled by default and cannot be enabled before Parts 1–3 pass production acceptance.
- Cached policy must be authenticated, DPAPI-protected, unexpired, and explicitly allow offline fallback.
- Offline evidence never produces `Verified`; after sync it remains `PendingReview` until an employer decision.
- Evidence bytes never enter ordinary `ActivityRecordBuffer` JSON or logs.
- Local files use DPAPI LocalMachine plus ACL restricted to the Service identity and administrators.
- Local evidence quota is 256 MB and maximum age is 72 hours; backend pending-review retention is 30 days.
- Same `AttendanceSessionId` and immutable evidence checksum make every retry idempotent.
- Sync ordering prevents final WorkSession upload from overtaking an earlier offline check-in from the same attendance session.

---

### Task 14: Implement secure cached policy and encrypted biometric outbox

**Files:**
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.Service\CheckIn\Offline\CachedBiometricPolicyStore.cs`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.Service\CheckIn\Offline\IBiometricEvidenceStore.cs`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.Service\CheckIn\Offline\DpapiBiometricEvidenceStore.cs`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.Service\CheckIn\Offline\BiometricOutboxRepository.cs`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.Service\CheckIn\Offline\BiometricOutboxRecord.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.Service\Policy\PolicyCache.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.Service\Program.cs`
- Create: `C:\HR\tray_app_maui\tests\ONEVO.Agent.Service.Tests\CheckIn\Offline\CachedBiometricPolicyStoreTests.cs`
- Create: `C:\HR\tray_app_maui\tests\ONEVO.Agent.Service.Tests\CheckIn\Offline\DpapiBiometricEvidenceStoreTests.cs`
- Create: `C:\HR\tray_app_maui\tests\ONEVO.Agent.Service.Tests\CheckIn\Offline\BiometricOutboxRepositoryTests.cs`

**Interfaces:**

```csharp
public interface IBiometricEvidenceStore
{
    Task<StoredBiometricEvidence> ProtectAndStoreAsync(
        Guid attendanceSessionId, ReadOnlyMemory<byte> bytes,
        string expectedSha256, CancellationToken ct);
    Task<Stream> OpenDecryptedAsync(Guid evidenceId, CancellationToken ct);
    Task DeleteAsync(Guid evidenceId, CancellationToken ct);
}

public sealed record BiometricOutboxRecord(
    Guid Id, Guid AttendanceSessionId, Guid EvidenceId,
    string Reason, string Sha256, long ByteCount,
    DateTimeOffset CapturedAt, DateTimeOffset ExpiresAt,
    int AttemptCount, DateTimeOffset? NextAttemptAt);
```

- [ ] **Step 1: Write encryption, ACL, expiry, quota, and crash-recovery tests**

Use temporary directories and injectable data-protection/ACL abstractions. Assert stored bytes differ from JPEG input, decrypt round-trip works, wrong checksum is rejected, 256 MB quota rejects new evidence without deleting old records, expired records are deleted, and an interrupted temporary write never creates a valid outbox row.

- [ ] **Step 2: Run tests and confirm the stores are missing**

```powershell
dotnet test tests/ONEVO.Agent.Service.Tests/ONEVO.Agent.Service.Tests.csproj --filter "FullyQualifiedName~CachedBiometricPolicyStoreTests|FullyQualifiedName~DpapiBiometricEvidenceStoreTests|FullyQualifiedName~BiometricOutboxRepositoryTests"
```

- [ ] **Step 3: Implement protected policy cache**

Store serialized effective policy with `Version`, `ValidUntil`, and fetch time using DPAPI. On load, reject decryption errors, absent biometric policy, expired `ValidUntil`, and `OfflineFallbackEnabled == false`. Do not extend server validity locally.

- [ ] **Step 4: Implement atomic encrypted evidence store**

Write encrypted bytes to `%ProgramData%\ONEVO\Agent\BiometricEvidenceSpool\<id>.bin.tmp`, flush, then atomically rename. Apply directory/file ACLs and verify them. SQLite stores only metadata/path/checksum/status. Delete local bytes immediately after acknowledged backend sync or expiry.

- [ ] **Step 5: Run tests and commit**

```powershell
dotnet test tests/ONEVO.Agent.Service.Tests/ONEVO.Agent.Service.Tests.csproj --filter "FullyQualifiedName~CachedBiometricPolicyStoreTests|FullyQualifiedName~DpapiBiometricEvidenceStoreTests|FullyQualifiedName~BiometricOutboxRepositoryTests"
git add ONEVO.Agent.Service/CheckIn/Offline ONEVO.Agent.Service/Policy/PolicyCache.cs ONEVO.Agent.Service/Program.cs tests/ONEVO.Agent.Service.Tests/CheckIn/Offline
git commit -m "feat(agent): protect offline biometric evidence"
```

---

### Task 15: Synchronize offline check-ins and enforce retention

**Files:**
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.Service\CheckIn\Offline\BiometricOutboxSyncService.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.Service\CheckIn\CheckInCoordinator.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.Service\AgentWorker.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.Service\Sync\ActivitySyncService.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.Service\Program.cs`
- Create: `C:\HR\tray_app_maui\tests\ONEVO.Agent.Service.Tests\CheckIn\Offline\BiometricOutboxSyncServiceTests.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\Services\Monitoring\Biometrics\BiometricRetentionJob.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Biometrics\ServiceInterfaces\IBiometricRetentionService.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\Services\Monitoring\Biometrics\BiometricRetentionService.cs`
- Extend: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Biometrics\RepositoryInterfaces\IBiometricRepository.cs`
- Extend: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\Persistence\Repositories\Monitoring\Biometrics\EfBiometricRepository.cs`
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\DependencyInjection.cs`
- Create: `C:\HR\HRMS-Backend-v1\tests\ONEVO.Tests.Unit\Features\Monitoring\Biometrics\BiometricRetentionServiceTests.cs`
- Create: `C:\HR\HRMS-Backend-v1\tests\ONEVO.Tests.Integration\Monitoring\Biometrics\BiometricRetentionIntegrationTests.cs`

**Interfaces:**
- Offline sync posts to Task 13 fallback endpoint with reason `offline_policy_authorized` and immutable attendance/checksum metadata.
- A work-session record for an attendance ID is not eligible to upload while that attendance ID has pending biometric outbox evidence.

- [ ] **Step 1: Write sync ordering and retry tests**

Cover network failure with exponential backoff/jitter, `401` one controlled refresh, matching `409` as idempotent success, conflicting `409` quarantine, local deletion only after success, Service restart recovery, and work-session deferral until check-in acknowledgement.

- [ ] **Step 2: Run sync tests and confirm missing service**

```powershell
dotnet test C:/HR/tray_app_maui/tests/ONEVO.Agent.Service.Tests/ONEVO.Agent.Service.Tests.csproj --filter FullyQualifiedName~BiometricOutboxSyncServiceTests
```

- [ ] **Step 3: Implement high-priority ordered sync**

Process oldest eligible biometric record first. Stream decrypted evidence into multipart without placing it in a string/base64 payload. Reuse the current JWT and retry policy. On acknowledgement mark metadata complete, delete file, then allow the associated WorkSession record to flush.

- [ ] **Step 4: Write backend retention tests**

Assert successful daily current frames do not exist in R2/DB, pending evidence older than 30 days is deleted, attempt metadata older than 90 days is removed/anonymized according to audit policy, superseded/revoked references are deleted after grace period, and legal hold skips deletion.

- [ ] **Step 5: Implement retention service and hosted schedule**

Query bounded batches, mark deletion intent, delete via the storage/object deletion abstraction, then mark completion. Use stable retry state so R2 failure cannot leave a database row falsely claiming deletion. Register the hosted job with a configurable daily UTC schedule.

- [ ] **Step 6: Run tests and commit each repository**

```powershell
dotnet test C:/HR/tray_app_maui/tests/ONEVO.Agent.Service.Tests/ONEVO.Agent.Service.Tests.csproj --filter "FullyQualifiedName~BiometricOutboxSyncServiceTests|FullyQualifiedName~ActivitySyncServiceTests"
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter FullyQualifiedName~BiometricRetentionServiceTests
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter FullyQualifiedName~BiometricRetentionIntegrationTests
```

Commit Agent sync and backend retention independently.

---

### Task 16: Add observability, security verification, and staged rollout

**Files:**
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Biometrics\ServiceInterfaces\IBiometricMetrics.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\Services\Monitoring\Biometrics\BiometricMetrics.cs`
- Modify: biometric handlers under `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Biometrics\`
- Create: `C:\HR\HRMS-Backend-v1\tests\ONEVO.Tests.Architecture\BiometricSecurityArchitectureTests.cs`
- Create: `C:\HR\HRMS-Backend-v1\tests\ONEVO.Tests.Integration\Monitoring\Biometrics\VerifiedCheckInEndToEndTests.cs`
- Create: `C:\HR\tray_app_maui\tests\ONEVO.Agent.Service.Tests\Security\BiometricSecretLoggingTests.cs`
- Create: `C:\HR\tray_app_maui\docs\testing\verified-check-in-pilot-runbook.md`
- Create: `C:\HR\HRMS-Backend-v1\docs\workflow\verified-employee-check-in.md`
- Modify: `C:\HR\HRMS-Backend-v1\docs\superpowers\project_ core\ONEVO_Backend_Architecture_Document.md`
- Modify: `C:\HR\HRMS-Backend-v1\docs\superpowers\project_ core\phase1-table-inventory.md`

**Interfaces:**
- Metrics expose counts/latencies by tenant-safe dimensions: result class, provider operation, challenge, and fallback class. They never label by employee, AWS session, face score, exact coordinate, or credential.

- [ ] **Step 1: Write architecture/security tests**

Scan source/log templates/serialized contracts to forbid AWS access-key property logging, biometric byte columns, generic `FacePhoto` use in verified clock-in, client-supplied employee/device IDs, non-Mumbai region configuration, and direct `Active` transition before backend verdict.

- [ ] **Step 2: Write full fake-provider E2E test**

Exercise:

```text
activation -> employee/device JWT -> enrollment -> fresh location
-> create check-in attempt -> fake liveness -> fake face match
-> one EmployeeCheckIn -> Service attendance correlation
-> one EmployeeWorkSession -> employer list/detail/review visibility
```

Add negative flows for spoof, mismatch, expired policy, cross-tenant/device, duplicate requests, provider outage strict, provider outage pending review, and offline reconnect.

- [ ] **Step 3: Add safe metrics and operational alerts**

Record attempt created/completed/rejected/provider-error, liveness and comparison latency histograms, pending-review age/count, outbox age/bytes, retention failures, and camera compatibility failures. Alerts cover elevated provider error rate, pending-review backlog, outbox older than policy, and retention deletion failure.

- [ ] **Step 4: Run full automated verification**

From backend:

```powershell
dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj
```

From Agent:

```powershell
dotnet test ONEVO.Agent.slnx
dotnet build ONEVO.Agent.slnx
```

Expected: all pass; no secret/media scan findings.

- [ ] **Step 5: Execute real Windows/AWS pilot gates**

Use an internal test tenant and record:

- Windows 10 and 11 on at least three-to-five laptop models.
- At least 10–20 employees across representative lighting, glasses, skin tones, and camera quality.
- Built-in and external real webcams; virtual camera rejection.
- Strict success rate, false rejects, retry rate, provider latency, and support cases.
- Camera denied/occupied, offline, location failure, AWS outage simulation, Service/Tray restart, and clock-out sync.
- Proof that success media is not retained and pending evidence follows 30-day policy.

Threshold changes require documented pilot evidence and cannot be tenant-editable.

- [ ] **Step 6: Roll out by controlled flags**

Enable in this order:

1. internal biometric enrollment;
2. strict online verified check-in;
3. employer review;
4. provider/location fallback for selected tenants;
5. offline fallback only after security acceptance;
6. tenant-by-tenant production expansion with rollback to strict block.

Rollback disables new attempts but preserves existing audit/history and allows pending outbox sync/retention cleanup.

- [ ] **Step 7: Sync documentation and commit**

Write the code-verified workflow report, update architecture/table inventory, and ensure every new endpoint has its `docs/postman-request/` Markdown file. Commit docs only after all automated and pilot evidence is attached to the runbook.
