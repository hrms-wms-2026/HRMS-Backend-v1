# Verified Employee Check-In Design

**Date:** 2026-08-13  
**Status:** Approved in chat  
**Implementation status:** Pending  
**Scope:** `C:\HR\HRMS-Backend-v1` and `C:\HR\tray_app_maui`

## Goal

Deliver a Windows-first employee check-in flow that binds one attendance event to:

- the employee and tenant resolved from the activated tray credential;
- the backend-issued tray device registration;
- a fresh live location reading;
- AWS Rekognition Face Liveness and face matching in Mumbai (`ap-south-1`);
- an idempotent attendance-session identifier shared with the completed work session.

The Service must not enter `MonitoringState.Active` until the backend returns an allowed check-in verdict. The default tenant policy is strict verification. Provider-outage and offline fallbacks are explicit tenant options and always create `PendingReview`, never `Verified`.

## Existing-System Findings

- Activation-code exchange is implemented. It consumes a one-time hashed code, registers the Windows device, issues a one-hour access JWT and rotating refresh token, and returns display identity.
- The tray JWT binds `sub = DeviceRegistrationId`, `user_id`, `tenant_id`, and `token_type = tray_device`.
- The Service protects access and refresh tokens with Windows DPAPI. It recovers the backend device ID from the JWT for local bookkeeping.
- Activation resolves employee name, email, and employee number from CoreHR, with an auth-user fallback. These are display values and are correctly excluded from the JWT.
- The existing `EmployeeCheckIn` stores `UserId`, `DeviceRegistrationId`, optional location values, and an optional photo link. It does not store the real CoreHR `Employee.Id`, biometric verdict, or an idempotency/correlation key.
- The backend already exposes submit-check-in and face-photo upload endpoints under `TrayDevicePolicy`.
- The Tray can capture native JPEG photos and real OS geolocation, but saves onboarding location only in Preferences.
- CLOCK IN currently performs a local lifecycle transition. It does not call the backend check-in endpoint.
- A native face photo is submitted as a generic `FacePhoto` record with `Environment.MachineName`. The Service rejects collection before monitoring is Active, and its sync service has no `FacePhoto` upload branch.
- Generic IPC is limited to 65,536 bytes; biometric video or photo bytes must never use that path.
- No Rekognition liveness session, face comparison, biometric profile, or verification-attempt implementation currently exists. The provider catalog entry alone is not an implementation.
- Completed work-session sync is already durable and idempotent by its client session ID.

## Locked Decisions

- Initial supported platform: Windows 10 and Windows 11.
- Capture device: built-in or attached real color laptop webcam; virtual cameras are rejected.
- Provider and region: AWS Rekognition Face Liveness in `ap-south-1` (Mumbai).
- Capture UI: a packaged React `FaceLivenessDetector` module hosted in WebView2 inside the MAUI Tray application.
- Challenge: `FaceMovementAndLightChallenge` by default because accuracy is preferred over speed. A platform release may switch challenge type only after a measured pilot.
- Trusted reference: a successful onboarding liveness reference, not an HR profile photo.
- Daily check-in: liveness plus `CompareFaces` against the active onboarding reference.
- Default fallback: strict block. A tenant may separately enable provider-outage, location-failure, and full-offline fallback.
- Spoof detection or face mismatch is never eligible for fallback.
- Maximum automatic attempts: three fresh, single-use AWS sessions.
- Backend is the verification and attendance authority. Service is the device credential and local lifecycle authority. Tray owns interactive UI and camera capture only.

## Identity Contract

The following identifiers remain distinct:

| Identifier | Meaning | Source |
|---|---|---|
| `UserId` | Authentication account GUID | signed tray JWT |
| `EmployeeId` | Real CoreHR employee GUID | backend lookup by tenant and user |
| `EmployeeNumber` | Human-readable value such as `ONEVO1234` | CoreHR; display/query join only |
| `DeviceRegistrationId` | Backend-issued enrolled device GUID | signed tray JWT `sub` |
| `AttendanceSessionId` | Client-generated correlation/idempotency GUID | Service at CLOCK IN |

Employee name and number are not copied into the JWT. On every backend mutation, tenant, user, and device come only from the validated JWT. The backend resolves `EmployeeId`; it never accepts employee or device identity from the request body. `Environment.MachineName` is informational only and cannot be an authorization identity.

## Windows Camera Compatibility Gate

Before the production biometric subsystem is implemented, run a disposable integration probe using the real target stack:

1. Package a minimal React `FaceLivenessDetector` build into MAUI.
2. Serve packaged assets through a WebView2 virtual HTTPS origin such as `https://biometric.onevo.local`; do not rely on `file://` for camera APIs.
3. Handle `CoreWebView2.PermissionRequested`. Allow only `Camera` for the exact biometric origin and deny unrelated origins and permission kinds.
4. Open a built-in laptop camera through `getUserMedia`, enumerate cameras, and prefer the front/built-in camera.
5. Record effective resolution and frame rate without saving video.
6. Complete a real staging Face Liveness session in `ap-south-1` and retrieve its result through the backend.
7. Test camera denial, camera occupied by Teams/Zoom, no camera, slow network, low light, external webcam, cancellation, and app restart.
8. Test Windows 10 and 11 on at least three to five representative laptop models before the main rollout.

AWS requires a front-facing color camera, no virtual camera, at least 15 FPS, at least 480x640 recording capability, a 60 Hz display, a four-inch screen, and at least 100 kbps bandwidth. The gate fails closed when these requirements or WebView2 support are unavailable. A normal 720p/30 FPS laptop webcam should pass, but the pilot is the release evidence.

Official references:

- <https://docs.aws.amazon.com/rekognition/latest/dg/face-liveness-requirements.html>
- <https://docs.aws.amazon.com/rekognition/latest/dg/face-liveness.html>
- <https://ui.docs.amplify.aws/react/connected-components/liveness>
- <https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/overview-features-apis>

## Target Online Flow

```text
Employee presses CLOCK IN
  -> Service creates AttendanceSessionId
  -> Tray captures fresh GPS and accuracy
  -> typed IPC sends only location and workflow metadata
  -> Service uses Device JWT to create a backend check-in attempt
  -> backend resolves EmployeeId and tenant/device policy
  -> backend calls CreateFaceLivenessSession in ap-south-1
  -> backend returns ONEVO attempt ID, AWS session ID, region,
     and short-lived StartFaceLivenessSession credentials
  -> Service sends the capture contract to Tray over restricted typed IPC
  -> WebView2 FaceLivenessDetector streams the short selfie directly to AWS
  -> Tray sends metadata-only completion to Service
  -> Service asks backend to complete the attempt
  -> backend calls GetFaceLivenessSessionResults
  -> backend checks liveness threshold and spoof result
  -> backend fetches the active onboarding reference from private R2
  -> backend uses CompareFaces against the current AWS reference frame
  -> backend creates or returns the idempotent EmployeeCheckIn
  -> Service starts PresenceSession and transitions Stopped -> Active
  -> Tray opens the active-work screen and timer
```

The Tray never sends an `isLive` or `faceMatched` boolean that the backend trusts. AWS sessions are single use. Every retry creates a new ONEVO attempt and AWS session while retaining the same `AttendanceSessionId`.

## Enrollment Flow

The existing onboarding face screen becomes biometric enrollment:

1. Service creates an enrollment attempt after activation, employee resolution, consent, and location setup.
2. WebView2 runs the same AWS liveness capture with purpose `enrollment`.
3. Backend obtains the AWS confidence result and reference image.
4. On success, the reference image is uploaded to private R2 through the existing storage abstraction.
5. Backend creates one active employee biometric profile.
6. Re-enrollment marks the previous profile `Superseded` and queues its object for policy-aware deletion.

The legacy `MonitoringFaceScan` photo table and endpoint remain for historical compatibility but are not used by the verified flow.

## Backend Data Model

### `EmployeeBiometricProfile`

- `Id`, `TenantId`, `EmployeeId`, and the source `UserId`.
- private R2 file record/reference object ID.
- provider (`aws_rekognition`) and region (`ap-south-1`).
- status: `Active`, `Superseded`, `Revoked`, or `Deleted`.
- consent version and acceptance time.
- enrollment attempt/device IDs and timestamps.
- one active profile per tenant and employee.

### `BiometricVerificationAttempt`

- ONEVO attempt ID and tenant, employee, user, device IDs.
- purpose: `Enrollment` or `CheckIn`.
- `AttendanceSessionId` for check-in attempts.
- AWS session ID, region, challenge type, and session expiry.
- state: `Created`, `Capturing`, `Verifying`, `Verified`, `Rejected`, `ProviderError`, or `Expired`.
- liveness and match confidence, stable failure code, and timestamps.
- no AWS credentials, raw video, or image bytes in PostgreSQL.

### `EmployeeCheckIn` changes

- retain independent server primary key.
- add real `EmployeeId`.
- add unique tenant-scoped `AttendanceSessionId`.
- add `BiometricAttemptId` and verification status: `Verified`, `PendingReview`, or `Rejected`.
- retain JWT-bound `UserId` and `DeviceRegistrationId`.
- add location code, accuracy, captured time, and fallback reason.

`EmployeeWorkSession` retains its independent primary key and stores the same `AttendanceSessionId`. The shared correlation key, not shared primary keys, links attendance and work-session records.

## Backend Components and APIs

Application defines `IBiometricVerificationProvider`; Infrastructure implements it with the AWS SDK. Controllers continue to use MediatR and Application interfaces. EF tenant filters and PostgreSQL RLS apply to every new tenant-owned table.

Tray-device APIs:

- `POST /api/v1/monitoring/biometrics/enrollment-attempts`
- `POST /api/v1/monitoring/biometrics/enrollment-attempts/{id}/complete`
- `GET /api/v1/monitoring/biometrics/profile`
- `POST /api/v1/monitoring/biometrics/check-in-attempts`
- `POST /api/v1/monitoring/biometrics/check-in-attempts/{id}/complete`
- `GET /api/v1/monitoring/biometrics/check-in-attempts/{id}`
- a separate provider-outage fallback submission endpoint.

Employer APIs under cookie authentication and a monitoring attendance permission:

- `GET /api/v1/monitoring/check-ins`
- `GET /api/v1/monitoring/check-ins/{id}`
- `POST /api/v1/monitoring/check-ins/{id}/approve`
- `POST /api/v1/monitoring/check-ins/{id}/reject`

Review mutations are idempotent and audited with reviewer, reason, time, old status, and new status. Rejecting a pending attendance event marks attendance invalid and notifies the employee. It does not retroactively terminate a local monitoring session.

## AWS Credentials and Security

- Backend compute uses an IAM role, not static keys in source or appsettings.
- Backend permissions cover creating/getting liveness results and face comparison.
- The capture client receives credentials valid for at most 15 minutes and allowed only to call `rekognition:StartFaceLivenessSession` in `ap-south-1`.
- Because AWS documents `StartFaceLivenessSession` with `Resource: "*"`, short duration, region restrictions where supported, dedicated role isolation, backend attempt binding, and monitoring provide the compensating controls.
- Credentials exist only in memory. They are excluded from Preferences, SQLite, files, crash reports, and logs.
- The Service-to-Tray named pipe is ACL-restricted; secret-bearing messages are typed and correlation-bound.
- No biometric image or video crosses generic IPC or the ordinary activity SQLite queue.
- The backend uses KMS encryption for the liveness session and does not configure AWS S3 output. The enrollment reference is copied to private R2 only after successful liveness.

## Service and Tray Boundaries

`ONEVO.Agent.Service.CheckIn.CheckInCoordinator` owns this workflow:

```text
Idle -> GettingLocation -> CreatingAttempt -> CapturingLiveness
     -> Verifying -> Verified/PendingReview/Failed -> ActivatingMonitoring
```

`AgentWorker` only routes typed messages and applies the final allowed lifecycle transition. It does not absorb the biometric orchestration.

Shared IPC includes start request, attempt-created, begin-capture, capture-completed, status-changed, cancelled, and failed contracts. Messages carry attempt/session/correlation IDs, location metadata, expiry, and temporary credentials where required. They never carry biometric media.

The Tray hosts the WebView2 capture module, handles exact-origin camera permission, shows preparation and retry guidance, and translates JavaScript completion/error events into typed IPC. The native `CameraService` may be retained for authorized provider-outage still-photo evidence, but it is not the liveness implementation.

## Policy and Failure Rules

- Default policy requires fresh location, liveness pass, and face match before check-in.
- Thresholds are calibrated ONEVO platform values. Tenants cannot reduce them to unsafe values.
- Face mismatch or spoof: reject, allow up to three fresh sessions, then block and direct the employee to support/manual resolution.
- AWS provider outage with backend online: strict tenant blocks; an enabled tenant collects a native still photo, GPS, and device evidence and creates `PendingReview`.
- Location permission or accuracy failure: strict tenant blocks; an independently enabled tenant creates `PendingReview`.
- Full internet/backend outage: disabled initially. When later enabled, it requires an unexpired DPAPI-protected policy cache and creates a provisional local `PendingReview` event in a dedicated encrypted evidence spool.
- Session cancellation, expiry, camera denial/occupation, and WebView2 failure never start monitoring.
- On restart, the Service queries backend attempt status by immutable IDs and resumes or terminates safely.

## Privacy and Retention

- Explicit biometric consent version and timestamp are required at enrollment.
- The active enrollment reference is private and encrypted in R2.
- Successful daily-check-in video, current reference frame, and audit frames are not retained by ONEVO after the decision.
- Provider-outage or offline pending-review evidence has a default 30-day retention.
- Verification decision metadata has a default 90-day retention.
- Re-enrollment, consent withdrawal, or employment exit revokes the profile and triggers policy/legal-hold-aware deletion.
- Enable the AWS Organizations AI-services opt-out policy where applicable.
- No biometric media, credentials, or complete AWS session secrets enter application logs or ordinary telemetry.
- Because liveness and face comparison are probabilistic, denied/high-impact cases support retry and human review.

## Testing and Acceptance

Backend unit tests cover identity resolution, state transitions, policy decisions, thresholds, idempotency, review concurrency, and provider fakes. PostgreSQL integration tests cover device JWT binding, employee resolution, RLS, migrations, duplicate attendance sessions, multipart fallback evidence, and retention jobs.

Service tests cover coordinator state order, duplicate/out-of-order IPC, crash/restart recovery, token expiry, and proof that monitoring cannot become Active before an allowed backend verdict. IPC tests prove messages fit limits and do not persist or log credentials/media.

Tray tests cover WebView2 bridge events, exact-origin camera permission, cancellation, camera denied/occupied, no camera, slow network, and navigation only after Service confirmation. Staging smoke tests use real AWS Mumbai; CI uses a fake provider.

End-to-end acceptance is:

```text
Activation -> employee/device identity -> onboarding liveness reference
-> fresh CLOCK IN GPS -> AWS liveness -> face match -> backend check-in
-> Service Active -> CLOCK OUT -> work-session sync -> employer review
```

## Implementation Decomposition and Order

The implementation is too large for one executable plan. The approved design will produce four self-contained plans in this order:

1. **Foundation and Windows compatibility:** identity contract, camera/WebView2/AWS Mumbai compatibility gate, IAM/KMS, database model, provider abstraction, and enrollment.
2. **Strict online verified check-in:** fresh location, typed IPC, Service coordinator, backend attempt APIs, WebView capture, idempotent check-in, and work-session correlation.
3. **Employer review and online fallbacks:** list/detail/approve/reject APIs, tenant policy, provider-outage evidence, notifications, privacy jobs, and observability.
4. **Offline fallback and rollout hardening:** encrypted biometric outbox, cached-policy expiry, reconnect reconciliation, pilot testing, thresholds, and tenant feature-flag rollout.

Strict online verified check-in is the first production milestone. Offline fallback cannot block its release and is not enabled until its separate security acceptance passes.

## Out of Scope

- macOS, Linux, mobile, or browser-only clients.
- Continuous facial monitoring after check-in.
- Trusting an HR profile picture as the biometric reference.
- Storing successful daily liveness videos or photos in ONEVO.
- Silent fallback after spoof or face mismatch.
- Replacing the existing activation, refresh-token, work-session, or private R2 abstractions without a requirement from this flow.
