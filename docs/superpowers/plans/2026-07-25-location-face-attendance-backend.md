# WorkPulse Location, Face, and Attendance Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the tenant-secure backend used by the approved WorkPulse desktop device to capture an explicit location, enroll and compare a private face photo, resolve Company clock-in policy, clock in/out and take breaks, and block location mismatches behind HR approval.

**Architecture:** Extend the existing Clean Architecture/CQRS backend only. Agent controllers derive `agent_id` from the signed Agent JWT and use `ActiveAgentPolicy`; handlers use Application repository/service interfaces; Infrastructure owns EF Core, PostgreSQL/RLS, R2, request-network metadata, image validation, and AWS Rekognition. Location, verification, attendance, idempotency, and outbox rows are committed through the existing unit of work; photo bytes never enter PostgreSQL or logs.

**Tech Stack:** .NET 10, ASP.NET Core, MediatR, EF Core 10, Npgsql/PostgreSQL, existing R2 `IFileStorageService`, `AWSSDK.Rekognition` 4.0.3.30, xUnit, Moq.

## Global Constraints

- Work in `C:\tmp\one backend\HRMS-Backend-v1` on the existing `tray_app` branch.
- Reuse `ActiveAgentPolicy`, `IAgentGatewayRepository`, `IFileStorageService`, `IIdempotencyStore`, `IOutboxWriter`, tenant query filters, and PostgreSQL RLS.
- Never accept tenant id, employee id, reviewer id, or approved-device authority from an agent request body.
- Use server UTC time for clock actions; client capture time is evidence only.
- Capture location/camera only for an explicit setup, clock-in, clock-out, or employee-triggered verification action.
- Store JPEG/PNG only, maximum 5 MiB, exactly one usable face, private R2 objects, and metadata/file ids only in PostgreSQL.
- Do not claim liveness detection. Rekognition performs face quality and comparison only.
- Do not log photo bytes, base64, object keys, raw Wi-Fi BSSID, raw gateway MAC, or local photo paths.
- Use `onsite`, `remote`, `either`, and `field` for expected work area. Existing employee `work_mode = hybrid` maps to fallback `either`.
- A required missing/stale/inaccurate location, missing approved reference photo, provider failure, or policy ambiguity fails closed.
- Every new tenant-owned table implements `ITenantOwnedEntity`, has a tenant-leading index, EF query filter, ENABLE/FORCE RLS policy, and rollback SQL.
- Preserve the known user-owned untracked plan files and `src/ONEVO.Api/ONEVO.Api.slnx`.

## File Map

### Domain

- Modify `src/ONEVO.Domain/Features/OrgStructure/LegalEntity/Entities/LegalEntity.cs` — Company office coordinates, radius, and timezone.
- Modify `src/ONEVO.Domain/Features/Auth/Login/Entities/GdprConsentRecord.cs` — consent notice version and agent capture link.
- Modify `src/ONEVO.Domain/Features/IdentityVerification/Entities/VerificationReferencePhoto.cs` — captured agent, review comment, consent link.
- Create `src/ONEVO.Domain/Features/AgentGateway/Entities/AgentWorkLocationEvidence.cs`.
- Create location entities under `src/ONEVO.Domain/Features/Configuration/Entities/`.
- Create verification entities under `src/ONEVO.Domain/Features/IdentityVerification/Entities/`.
- Create schedule, policy, approval, and attendance entities under `src/ONEVO.Domain/Features/TimeAttendance/Entities/`.

### Application

- Create location primitives/services under `src/ONEVO.Application/Features/AgentGateway/Location/`.
- Create setup commands/queries under `src/ONEVO.Application/Features/AgentGateway/Commands/` and `Queries/`.
- Create verification services/commands under `src/ONEVO.Application/Features/IdentityVerification/`.
- Create `ITimeAttendanceRepository`, schedule/policy resolver, context query, clock commands, and approval commands under `src/ONEVO.Application/Features/TimeAttendance/`.
- Modify `src/ONEVO.Application/Features/Storage/File/Helpers/UploadPurposeCatalog.cs`.
- Modify `src/ONEVO.Application/Features/Storage/File/ServiceInterfaces/IFileStorageService.cs`.
- Modify `src/ONEVO.Application/Common/ServiceInterfaces/IOutboxMessageHandler.cs` with presence event keys.

### Infrastructure/API

- Add EF configurations, DbSets, migrations, RLS, repositories, image validation, network context, photo read support, and Rekognition adapter.
- Modify `src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj` and DI registration.
- Extend `AgentGatewayController`; create `TimeAttendanceController` and `AttendanceApprovalController`.

---

### Task 1: Deterministic Location Rules and Company Office Fields

**Files:**

- Modify: `src/ONEVO.Domain/Features/OrgStructure/LegalEntity/Entities/LegalEntity.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/DevPlatform/Tenancy/LegalEntityConfiguration.cs`
- Create: `src/ONEVO.Application/Features/AgentGateway/Location/LocationCapture.cs`
- Create: `src/ONEVO.Application/Features/AgentGateway/Location/LocationMatchResult.cs`
- Create: `src/ONEVO.Application/Features/AgentGateway/Location/ILocationVerificationService.cs`
- Create: `src/ONEVO.Application/Features/AgentGateway/Location/LocationVerificationService.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/AgentGateway/LocationVerificationServiceTests.cs`
- Generate: migration `AddCompanyOfficeLocation`

**Interfaces:**

- Produces `LocationCapture(decimal Latitude, decimal Longitude, decimal AccuracyMeters, DateTimeOffset CapturedAt, string PermissionState)`.
- Produces `LocationTarget(Guid SourceId, string Source, decimal Latitude, decimal Longitude, int AllowedRadiusMeters)`.
- Produces `LocationMatchResult(bool IsValid, bool IsMatch, decimal? DistanceMeters, string FailureCode)`.
- Produces `ILocationVerificationService.Evaluate(LocationCapture capture, LocationTarget target, DateTimeOffset serverNow)`.

- [x] **Step 1: Write failing distance and fail-closed tests**

Cover exact-coordinate match, outside-radius mismatch, latitude/longitude bounds, permission denied, capture older than two minutes, accuracy above 250 metres, and Haversine distance between two known points.

```csharp
var result = service.Evaluate(
    new LocationCapture(6.927079m, 79.861244m, 12m, now, "granted"),
    new LocationTarget(Guid.NewGuid(), "company_office", 6.927079m, 79.861244m, 100),
    now);

Assert.True(result.IsValid);
Assert.True(result.IsMatch);
Assert.InRange(result.DistanceMeters!.Value, 0m, 0.5m);
```

- [x] **Step 2: Run the test and verify RED**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter FullyQualifiedName~LocationVerificationServiceTests
```

Expected: compile failure because the location types do not exist.

- [x] **Step 3: Implement validation and Haversine matching**

Use Earth radius `6_371_000m`, decimal inputs converted to radians, maximum capture age two minutes, maximum accepted OS accuracy 250m, and match rule:

```csharp
distanceMeters <= target.AllowedRadiusMeters + capture.AccuracyMeters
```

Reject radius outside `25..50_000`, non-`granted` permission, future capture beyond 30 seconds, NaN-equivalent/out-of-range coordinates, stale capture, and inaccurate capture with stable failure codes.

- [x] **Step 4: Add Company office fields and migration**

Add:

```csharp
public string? OfficeAddressLabel { get; set; }
public decimal? OfficeLatitude { get; set; }
public decimal? OfficeLongitude { get; set; }
public int? OfficeAllowedRadiusMeters { get; set; }
public string Timezone { get; set; } = "UTC";
```

Configure decimal `(10,7)`, max lengths `255` and `50`, and check constraints for coordinate/radius ranges. Generate `AddCompanyOfficeLocation`; do not create a separate office table.

- [x] **Step 5: Verify and commit**

Run focused tests, API build, migration drift check, and:

```powershell
git commit -m "feat(location): add deterministic Company geofencing"
```

---

### Task 2: Location and Verification Persistence Foundation

**Files:**

- Create: `src/ONEVO.Domain/Features/AgentGateway/Entities/AgentWorkLocationEvidence.cs`
- Create: `src/ONEVO.Domain/Features/Configuration/Entities/EmployeeRemoteWorkProfile.cs`
- Create: `src/ONEVO.Domain/Features/Configuration/Entities/RemoteWorkLocationChangeRequest.cs`
- Create: `src/ONEVO.Domain/Features/IdentityVerification/Entities/VerificationPolicy.cs`
- Create: `src/ONEVO.Domain/Features/IdentityVerification/Entities/VerificationRecord.cs`
- Create: `src/ONEVO.Domain/Features/IdentityVerification/Entities/VerificationEvidenceAsset.cs`
- Modify: `src/ONEVO.Domain/Features/IdentityVerification/Entities/VerificationReferencePhoto.cs`
- Modify: `src/ONEVO.Domain/Features/Auth/Login/Entities/GdprConsentRecord.cs`
- Add matching EF configuration files and DbSets.
- Modify: `src/ONEVO.Application/Features/AgentGateway/RepositoryInterfaces/IAgentGatewayRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/AgentGateway/EfAgentGatewayRepository.cs`
- Create: `src/ONEVO.Application/Features/IdentityVerification/RepositoryInterfaces/IVerificationRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/IdentityVerification/EfVerificationRepository.cs`
- Test: `tests/ONEVO.Tests.Architecture/TenantIsolationArchitectureTests.cs`
- Generate: migration `AddLocationAndVerificationFoundation`

**Interfaces:**

- `AgentWorkLocationEvidence` stores server public IP, double-hashed network identifiers, location JSON, match result/source, agent, employee, and optional presence id.
- `EmployeeRemoteWorkProfile` supports `pending_capture`, `active`, `archived`, `rejected`; one active per employee.
- `RemoteWorkLocationChangeRequest` supports `pending`, `approved`, `rejected`, `captured`, `expired`; one pending per employee.
- `IVerificationRepository` exposes active policy/reference, verification/evidence adds, remote profile/change-request reads/adds, consent/reference adds, and tracked review reads.

- [ ] **Step 1: Add failing tenant/RLS model tests**

Add explicit entity sanity assertions and rely on the generic architecture tests to require filters and migration policy coverage.

```csharp
Assert.True(typeof(ITenantOwnedEntity).IsAssignableFrom(typeof(VerificationRecord)));
Assert.True(typeof(ITenantOwnedEntity).IsAssignableFrom(typeof(EmployeeRemoteWorkProfile)));
Assert.True(typeof(ITenantOwnedEntity).IsAssignableFrom(typeof(AgentWorkLocationEvidence)));
```

- [ ] **Step 2: Run and verify RED**

Run the three focused architecture tests. Expected: missing type compile failures.

- [ ] **Step 3: Add exact entities and configurations**

Use the canonical columns from:

- `OneVo-HR/database/schemas/configuration.md`
- `OneVo-HR/database/schemas/identity-verification.md`
- `OneVo-HR/database/schemas/agent-gateway.md`

Complete `VerificationReferencePhoto` with `CapturedDeviceId`, `ReviewComment` max 255, and `LegalAcceptanceRecordId` pointing to the existing consent record implementation. Extend consent with `NoticeVersion` max 50 and `CapturedAgentId`.

Add unique filtered indexes:

```sql
(tenant_id, employee_id) WHERE status = 'active'
(tenant_id, employee_id) WHERE status = 'pending'
(tenant_id, employee_id) WHERE is_active = true
```

Use `uint Version`/`IsRowVersion()` on both approval request aggregates.

- [ ] **Step 4: Add repository contracts and implementations**

Required signatures include:

```csharp
Task<VerificationPolicy?> GetActivePolicyAsync(CancellationToken ct);
Task<VerificationReferencePhoto?> GetActiveReferencePhotoAsync(Guid employeeId, CancellationToken ct);
Task<VerificationRecord?> GetVerificationRecordAsync(Guid id, CancellationToken ct);
Task<EmployeeRemoteWorkProfile?> GetActiveRemoteProfileAsync(Guid employeeId, CancellationToken ct);
Task<RemoteWorkLocationChangeRequest?> GetPendingRemoteChangeAsync(Guid employeeId, CancellationToken ct);
Task AddVerificationRecordAsync(VerificationRecord record, CancellationToken ct);
Task AddEvidenceAssetAsync(VerificationEvidenceAsset asset, CancellationToken ct);
Task AddReferencePhotoAsync(VerificationReferencePhoto photo, CancellationToken ct);
Task AddConsentAsync(GdprConsentRecord consent, CancellationToken ct);
Task AddRemoteProfileAsync(EmployeeRemoteWorkProfile profile, CancellationToken ct);
Task AddRemoteChangeRequestAsync(RemoteWorkLocationChangeRequest request, CancellationToken ct);
Task AddWorkLocationEvidenceAsync(AgentWorkLocationEvidence evidence, CancellationToken ct);
```

- [ ] **Step 5: Generate migration and add RLS**

Generate `AddLocationAndVerificationFoundation`. Add ENABLE/FORCE RLS and `tenant_isolation` WITH CHECK for every new table. Add FK restrictions to employee, agent, user, file record, verification, and presence targets that exist at this phase.

- [ ] **Step 6: Verify and commit**

Run focused architecture tests, full architecture suite, model-drift check, and:

```powershell
git commit -m "feat(verification): persist location and face evidence"
```

---

### Task 3: Time & Attendance Schedule, Policy, and Session Persistence

**Files:**

- Create entities: `WorkSchedule`, `WorkScheduleDay`, `WorkScheduleHoliday`, `ScheduleAssignment`, `ClockInPolicy`, `AttendanceRecord`, `PresenceSession`, `BreakRecord`, `DeviceSession`, `WorkAreaChangeRequest`.
- Create EF configurations under `src/ONEVO.Infrastructure/Persistence/Configurations/TimeAttendance/`.
- Add DbSets to `ApplicationDbContext`.
- Create `src/ONEVO.Application/Features/TimeAttendance/RepositoryInterfaces/ITimeAttendanceRepository.cs`.
- Create `src/ONEVO.Infrastructure/Persistence/Repositories/TimeAttendance/EfTimeAttendanceRepository.cs`.
- Modify Infrastructure DI.
- Test tenant/RLS coverage.
- Generate: migration `AddTimeAttendanceClocking`

**Interfaces:**

- Produces the exact Phase 1 canonical tables required for weekly schedule resolution, daily attendance, one daily presence row, multiple device sessions, breaks, policy, and one-day work-area approval.
- Repository exposes tracked active attendance/session rows and no-tracking resolver reads.

- [ ] **Step 1: Write failing model/index tests**

Assert all ten types are tenant owned. Add model assertions for:

```text
presence_sessions unique (tenant_id, employee_id, date)
attendance_records unique (tenant_id, employee_id, date)
work_schedule_days unique (tenant_id, work_schedule_id, day_of_week)
one open device_session per agent
one open break per employee
one pending work_area_change_request per employee/date
```

- [ ] **Step 2: Run and verify RED**

Run focused model tests. Expected: missing type failures.

- [ ] **Step 3: Implement canonical focused entities**

Use every field defined in the approved design’s required flow from `database/schemas/time-attendance.md`. Do not add overtime, roster, shift CRUD, attendance corrections, or payroll behavior. Add `Version` xmin tokens to approval and active-session aggregates.

- [ ] **Step 4: Add repository contract**

Include:

```csharp
Task<WorkAreaChangeRequest?> GetApprovedWorkAreaChangeAsync(Guid employeeId, DateOnly date, CancellationToken ct);
Task<ScheduleAssignment?> ResolveScheduleAssignmentAsync(Employee employee, DateOnly date, CancellationToken ct);
Task<WorkSchedule?> GetScheduleAsync(Guid id, CancellationToken ct);
Task<WorkScheduleDay?> GetScheduleDayAsync(Guid scheduleId, short dayOfWeek, CancellationToken ct);
Task<WorkScheduleHoliday?> GetScheduleHolidayAsync(Guid scheduleId, DateOnly date, CancellationToken ct);
Task<ClockInPolicy?> ResolveClockInPolicyAsync(Employee employee, Guid legalEntityId, DateOnly date, CancellationToken ct);
Task<AttendanceRecord?> GetAttendanceAsync(Guid employeeId, DateOnly date, CancellationToken ct);
Task<PresenceSession?> GetPresenceAsync(Guid employeeId, DateOnly date, CancellationToken ct);
Task<DeviceSession?> GetOpenDeviceSessionAsync(Guid agentId, CancellationToken ct);
Task<BreakRecord?> GetOpenBreakAsync(Guid employeeId, CancellationToken ct);
Task AddAttendanceAsync(AttendanceRecord record, CancellationToken ct);
Task AddPresenceAsync(PresenceSession session, CancellationToken ct);
Task AddDeviceSessionAsync(DeviceSession session, CancellationToken ct);
Task AddBreakAsync(BreakRecord record, CancellationToken ct);
Task<WorkAreaChangeRequest?> GetPendingWorkAreaChangeAsync(Guid employeeId, DateOnly date, CancellationToken ct);
Task AddWorkAreaChangeAsync(WorkAreaChangeRequest request, CancellationToken ct);
```

Schedule assignment precedence is employee, position (temporarily matched through current `Employee.JobTitleId`), department, then full company.

- [ ] **Step 5: Generate migration and RLS**

Generate `AddTimeAttendanceClocking`; add check constraints for enums/ranges, Restrict FKs, tenant-leading indexes, filtered unique indexes, and ENABLE/FORCE RLS for all tables.

- [ ] **Step 6: Verify and commit**

Run model/RLS tests and:

```powershell
git commit -m "feat(attendance): add clocking persistence foundation"
```

---

### Task 4: Agent Setup Status and Explicit Location Capture

**Files:**

- Create: `IRequestNetworkContext`, `INetworkEvidenceHasher`, and Infrastructure implementations.
- Create: `GetAgentSetupStatusQuery` and handler.
- Create: `CaptureSetupLocationCommand` and handler.
- Modify: `AgentGatewayController`.
- Test: setup/location handler and controller authorization tests.

**Interfaces:**

- `GetAgentSetupStatusQuery(Guid AgentId)` returns work mode, location requirement/readiness, reference requirement/readiness, and overall setup state.
- `CaptureSetupLocationCommand(Guid AgentId, LocationCapture Capture, string? LocalNetworkClass, string? WifiBssidHash, string? GatewayMacHash, bool VpnDetected)` returns stored evidence id, match state, and remote profile state.
- Public IP comes from `IRequestNetworkContext`, never the body.

- [ ] **Step 1: Write failing handler tests**

Cover onsite match, onsite mismatch, first remote profile, stale/denied capture, and inactive agent rejection. Verify raw network values are not assigned to the entity.

- [ ] **Step 2: Run and verify RED**

Expected: missing commands/services.

- [ ] **Step 3: Implement network evidence protection**

`INetworkEvidenceHasher` accepts already locally hashed identifiers and HMACs them again with tenant id plus the configured encryption master key. Validate incoming hashes as `32..128` hex characters; reject raw MAC-like strings containing `:` or `-`.

- [ ] **Step 4: Implement setup handlers**

Resolve agent and employee only through repository rows. For onsite, require Company office coordinates and use `ILocationVerificationService`. For first remote setup, create `pending_capture` when policy requires an approved reference/verification; otherwise create the first `active` profile. Never overwrite an active profile; create a pending `RemoteWorkLocationChangeRequest` on mismatch/change.

- [ ] **Step 5: Add active-agent endpoints**

```text
GET  /api/v1/agent/setup/status
POST /api/v1/agent/setup/location
```

Both use `ActiveAgentPolicy`; request bodies contain no tenant/employee/agent authority.

- [ ] **Step 6: Verify and commit**

Run focused tests, AgentGateway tests, controller tests, then:

```powershell
git commit -m "feat(agent): capture approved setup location"
```

---

### Task 5: Private Reference Photo and Rekognition Verification

**Files:**

- Modify: `UploadPurposeCatalog.cs`, `IFileStorageService.cs`, `FileStorageService.cs`.
- Create: `IImageContentValidator`, `ImageContentValidator`.
- Create: `IFaceComparisonService`, `FaceComparisonResult`, `AwsRekognitionFaceComparisonService`.
- Modify: `ONEVO.Infrastructure.csproj` with `AWSSDK.Rekognition` 4.0.3.30.
- Create: `EnrollReferencePhotoCommand` and `VerifyFaceCaptureCommand`.
- Modify: DI and `AgentGatewayController`.
- Test image validation, command handlers, and Rekognition response mapping.

**Interfaces:**

- Add upload purposes `verification_reference_photo` and `verification_evidence`, JPEG/PNG only, 5 MiB.
- Add `IFileStorageService.OpenReadAsync(Guid tenantId, Guid fileRecordId, CancellationToken ct)`.
- `IImageContentValidator.ValidateAsync(Stream, string contentType, CancellationToken)` returns detected format, width, height, and SHA-256; requires `320..4096` pixels each side and exactly valid JPEG/PNG structure.
- `IFaceComparisonService.CompareAsync(Stream reference, Stream candidate, decimal threshold, CancellationToken)` returns face counts, quality accepted, similarity, and accepted flag.

- [ ] **Step 1: Write failing validation and handler tests**

Cover MIME spoof, bad magic bytes, oversize, dimensions out of range, zero/multiple faces, provider failure, consent notice missing, trusted auto-approval, manual pending review, accepted comparison, and below-threshold failure.

- [ ] **Step 2: Run and verify RED**

Expected: missing validation/comparison/upload contracts.

- [ ] **Step 3: Extend secure storage**

Add the two purposes and read-by-file-id support without exposing object keys. The storage service resolves tenant-filtered `FileRecord` internally before opening R2 content.

- [ ] **Step 4: Implement AWS adapter**

Use `DetectFacesAsync` with `Attributes = ALL` for candidate quality/single-face validation, then `CompareFacesAsync` with the tenant policy threshold. Pass cancellation tokens. Convert AWS/network errors to `provider_unavailable`; never accept on exception and never log image content.

- [ ] **Step 5: Implement reference enrollment**

`POST /api/v1/agent/setup/reference-photo` is multipart, uses `ActiveAgentPolicy`, requires non-empty notice version, validates first, uploads privately, then saves consent and reference metadata. `trusted_sso_auto_approve` creates an approved active reference; manual mode creates pending review.

- [ ] **Step 6: Implement fresh face verification**

`POST /api/v1/agent/verification/face` accepts multipart image plus trigger `clock_in` or `clock_out`. It resolves the active reference, compares, uploads restricted evidence, saves `VerificationRecord`/`VerificationEvidenceAsset`, and returns only record id, status, and rounded confidence.

- [ ] **Step 7: Verify and commit**

Run verification/storage tests and:

```powershell
git commit -m "feat(verification): compare private WorkPulse face captures"
```

---

### Task 6: Schedule and Clock-In Context Resolver

**Files:**

- Create: `ResolvedClockInContext.cs`
- Create: `IClockInContextResolver.cs`
- Create: `ClockInContextResolver.cs`
- Create: optional `IRosterWorkAreaReader` and `IShiftWorkAreaReader` null implementations.
- Create: `GetClockInContextQuery` and handler.
- Create: `TimeAttendanceController`.
- Test resolver precedence and endpoint security.

**Interfaces:**

- `ResolvedClockInContext` contains employee, agent, work date/timezone, schedule/day, expected work area, active policy, location target/radius, face requirement, reference/profile readiness, current presence state, eligibility, and safe reason.
- Resolver order: approved one-day request, optional roster, optional shift, schedule day, employee work mode fallback.

- [ ] **Step 1: Write failing resolver tests**

Cover each precedence level, hybrid-to-either fallback, holiday/off-day, inactive policy, tray source disabled for each work area, missing office/remote profile, either source rule, and photo requirement.

- [ ] **Step 2: Run and verify RED**

Expected: missing resolver types.

- [ ] **Step 3: Implement resolver**

Use Company timezone with `TimeZoneInfo`; derive day `1=Monday..7=Sunday`. Require an active legal entity, applicable schedule/day or explicit safe fallback, and applicable active policy. Never use client time/work area.

- [ ] **Step 4: Add endpoint**

```text
GET /api/v1/time-attendance/clock-in/context
```

Use `ActiveAgentPolicy`, derive agent id from `sub`, and return no provider secrets or storage identifiers.

- [ ] **Step 5: Verify and commit**

Run TimeAttendance resolver/controller tests and:

```powershell
git commit -m "feat(attendance): resolve WorkPulse clock-in context"
```

---

### Task 7: Idempotent Clock-In and Location-Mismatch Blocking

**Files:**

- Create: `ClockInCommand`, handler, response DTO.
- Create: `ClockActionRequestHasher`.
- Modify: `TimeAttendanceController`.
- Modify: `OutboxMessageTypes`.
- Test clock-in success, mismatch, face required, idempotent replay, and concurrency.

**Interfaces:**

- Request requires `Idempotency-Key`.
- Body contains current `LocationCapture?`, protected network evidence, and `VerificationRecordId?`; no authority ids.
- Response status values: `clocked_in`, `already_clocked_in`, `blocked_pending_approval`, `blocked_setup_required`, `blocked_verification_failed`.

- [ ] **Step 1: Write failing clock-in tests**

Success must add/update `AttendanceRecord`, `PresenceSession`, `DeviceSession`, location evidence, and enqueue `PresenceSessionStarted`, then call `SaveChangesAsync` once.

Mismatch tests assert no attendance/session/outbox and exactly one pending canonical request:

```csharp
Assert.Equal("blocked_pending_approval", result.Value!.ClockInStatus);
Assert.NotNull(result.Value.ApprovalRequestId);
uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
repo.Verify(x => x.AddPresenceAsync(It.IsAny<PresenceSession>(), It.IsAny<CancellationToken>()), Times.Never);
```

- [ ] **Step 2: Run and verify RED**

Expected: missing command/handler.

- [ ] **Step 3: Implement idempotency boundary**

Validate key length `8..128`. Hash normalized request content. Use scope `time_attendance.clock_in` and requester `agent:{agentId}`. Replay stored 200/409 response for same key/hash, reject hash mismatch/in-flight, abandon only on unexpected 5xx.

- [ ] **Step 4: Implement policy/location/face gates**

Re-resolve context at mutation time. Validate a face record belongs to the same tenant/employee/agent, is `verified`, trigger `clock_in`, and is at most two minutes old. Store every attempted required location evaluation.

For mismatch:

- expected remote and changed remote target -> single pending `RemoteWorkLocationChangeRequest`;
- planned work area differs from detected valid area -> single pending `WorkAreaChangeRequest`;
- return blocked response and do not fabricate attendance.

- [ ] **Step 5: Implement successful atomic write**

Use server time, create daily attendance/presence and open device session, enqueue encrypted outbox type `presence_session_started`, save once, then complete idempotency response.

- [ ] **Step 6: Add endpoint and verify**

```text
POST /api/v1/time-attendance/clock-in
```

Use `ActiveAgentPolicy`. Run focused, full unit, architecture tests, then:

```powershell
git commit -m "feat(attendance): enforce secure idempotent clock-in"
```

---

### Task 8: Clock-Out, Breaks, and Current Presence

**Files:**

- Create commands: `ClockOut`, `StartBreak`, `EndBreak`.
- Create query: `GetCurrentPresence`.
- Modify `TimeAttendanceController` and outbox type keys.
- Test all state transitions.

**Interfaces:**

- Clock-out reuses explicit location/face requirements resolved for `clock_out`.
- Break endpoints never accept employee/session ids.
- Outbox keys: `presence_session_ended`, `presence_break_started`, `presence_break_ended`.

- [ ] **Step 1: Write failing transition tests**

Cover no active session conflict, start one break, duplicate start returns current state, end without break conflict, clock-out during open break closes the break, calculates minutes, closes device/presence/attendance, and enqueues events.

- [ ] **Step 2: Run and verify RED**

Expected: missing handlers.

- [ ] **Step 3: Implement handlers with one save each**

Use server time and tracked repository entities. During breaks, monitoring state is represented only through committed break/outbox state. Clock-out uses the same idempotency pattern with scope `time_attendance.clock_out`.

- [ ] **Step 4: Add endpoints**

```text
GET  /api/v1/time-attendance/presence/current
POST /api/v1/time-attendance/clock-out
POST /api/v1/time-attendance/breaks/start
POST /api/v1/time-attendance/breaks/end
```

All use `ActiveAgentPolicy`.

- [ ] **Step 5: Verify and commit**

```powershell
git commit -m "feat(attendance): add clock-out and break lifecycle"
```

---

### Task 9: HR Work-Area and Remote-Location Approval APIs

**Files:**

- Create pending list, approve, and reject query/command handlers for both request aggregates.
- Create `AttendanceApprovalController`.
- Extend repository methods for paginated tracked requests.
- Test transitions, stale conflicts, pagination, permission attributes, and reviewer source.

**Interfaces:**

- Browser policy `TenantPolicy`.
- Permission `attendance:approve`.
- Reviewer always `ICurrentUser.UserId`.
- Approval does not create a historical clock-in; employee must retry.

- [ ] **Step 1: Write failing approval tests**

Work-area approval sets status/reviewer only. Remote approval marks request `approved`; the next verified capture archives the old profile, activates the new profile, and marks request `captured`. Non-pending/xmin stale requests return 409.

- [ ] **Step 2: Run and verify RED**

Expected: missing handlers.

- [ ] **Step 3: Add repository methods and handlers**

Cap page size at 100; order oldest pending first; validate tenant/employee/profile binding before transition; call one save.

- [ ] **Step 4: Add protected endpoints**

```text
GET /api/v1/time-attendance/approvals/work-area
PUT /api/v1/time-attendance/approvals/work-area/{id}/approve
PUT /api/v1/time-attendance/approvals/work-area/{id}/reject
GET /api/v1/time-attendance/approvals/remote-location
PUT /api/v1/time-attendance/approvals/remote-location/{id}/approve
PUT /api/v1/time-attendance/approvals/remote-location/{id}/reject
```

- [ ] **Step 5: Verify and commit**

Run focused approval/controller tests and:

```powershell
git commit -m "feat(attendance): approve WorkPulse location changes"
```

---

### Task 10: Backend Security and End-to-End Verification

**Files:**

- Modify only implementation/tests needed by failures.
- Update this plan’s checkboxes and verification note.

**Interfaces:**

- Produces the stable API contract consumed by the Windows Service/IPC plan.

- [ ] **Step 1: Add endpoint authorization architecture tests**

Assert all agent setup/verification/clock endpoints use `ActiveAgentPolicy`; only device-change status uses basic `AgentPolicy`; HR approvals require `TenantPolicy` plus `attendance:approve`.

- [ ] **Step 2: Add privacy/logging and migration tests**

Source-scan request/response DTOs and logging calls to prevent `PhotoBytes`, `Base64`, object keys, raw BSSID/MAC, and local paths. Assert every new table has RLS coverage.

- [ ] **Step 3: Run format, build, and suites**

```powershell
dotnet format src/ONEVO.Api/ONEVO.Api.csproj --verify-no-changes --no-restore
dotnet build src/ONEVO.Api/ONEVO.Api.csproj --no-restore
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --no-restore
dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --no-restore
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --no-build
dotnet ef migrations has-pending-model-changes --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api --no-build
```

Report unavailable Docker/secrets as environment blockers with exact counts; do not weaken tests or commit secrets.

- [ ] **Step 4: Inspect idempotent migration SQL**

Verify Company office fields, all tables/indexes/FKs, filtered unique constraints, ENABLE/FORCE RLS, policies, and no photo byte column.

- [ ] **Step 5: Commit verification fixes**

```powershell
git commit -m "test(attendance): verify secure WorkPulse clocking"
```

## Self-Review

### Spec coverage

- Company office coordinates and server geofence: Task 1.
- Tenant/RLS location, remote profile, verification, schedules, attendance, presence, device sessions, breaks, and approval records: Tasks 2-3.
- Explicit Windows location evidence/setup API: Task 4.
- Consent, private R2 photos, strict image validation, reference approval, and Rekognition comparison: Task 5.
- Five-level work-area and Company policy resolution: Task 6.
- Approved-device-only, location/face-gated, idempotent clock-in with mismatch blocking and outbox: Task 7.
- Clock-out, breaks, and current presence: Task 8.
- HR web-ready approval APIs with existing permission model: Task 9.
- Security, privacy, RLS, migration, and complete verification: Task 10.

### Placeholder scan

The plan contains no deferred implementation markers. Roster and shift are explicit optional null readers; their CRUD remains outside this tray delivery. Integration infrastructure failures are reported, not hidden.

### Type consistency

- Agent authority is always `RegisteredAgent.Id` from JWT `sub`.
- `DeviceSession.DeviceId` stores `RegisteredAgent.Id`, while legacy `AgentSession.DeviceId` continues to store the installation string.
- `VerificationRecordId` is the only face-verification proof accepted by a clock command.
- Work-area values are `onsite`, `remote`, `either`, `field`; employee `hybrid` maps only to fallback `either`.
- Persistent approval statuses match the canonical aggregates; clock response statuses are separate safe response values.
