# ONEVO WorkPulse Secure Clock-In Design

**Date:** 2026-07-25

**Status:** Approved for implementation planning

**Repositories:** `HRMS-Backend-v1` and `One-tary`

**Primary platform:** Windows 10 1809+

## 1. Objective

Build the complete employee WorkPulse tray flow on the existing ONEVO backend:

1. Enroll one approved employee device through the existing web authentication and device-code flow.
2. Capture the employee's current Windows location during setup.
3. Capture an employee-approved reference face photo when required.
4. Allow clock-in only from the employee's currently approved device.
5. Re-evaluate current location at clock-in.
6. Request a face capture only when the resolved Company policy requires one.
7. Block clock-in on a location mismatch and create the correct pending HR approval request.
8. Persist attendance, verification, consent, device, and approval data in PostgreSQL while storing private photos in R2.
9. Provide the five supplied tray screens, matching their visual direction and including restrained animations.

This design extends the existing Clean Architecture, CQRS/MediatR, Agent Gateway, authentication, permission, R2, RLS, and outbox foundations. It does not introduce a parallel backend, authentication system, role system, or photo store.

## 2. Scope

### In scope

- Existing browser login and one-time enrollment code integration
- Employee-to-device binding
- One approved desktop per employee
- HR/Admin-controlled device replacement
- Windows location and camera permissions
- Initial remote location profile capture
- Initial reference face capture and employee consent
- Company office location fields already defined by the canonical schema
- Clock-in policy resolution
- Location and optional face verification at clock-in
- Attendance and presence creation
- Location-mismatch approval request creation
- Backend APIs needed by a future HR web UI
- Windows Service-to-backend integration
- Tray-to-Service local IPC
- Tray onboarding, setup-complete, clock-in, blocked, and pending states
- Migrations, tenant RLS, unit tests, integration tests, architecture tests, and Windows integration verification

### Out of scope

- Building the HR web approval screens
- Continuous GPS tracking
- Camera or location collection outside an active setup or clock action
- A new employee email/password form inside the Tray App
- Multiple simultaneously approved desktops for one employee
- Full Time & Attendance administration UI
- Payroll, overtime, and unrelated attendance functionality
- Mobile or macOS clients
- Certified presentation-attack/liveness detection

The backend approval endpoints and pending records are included so the HR web can consume them later without changing the tray protocol.

## 3. Existing Components to Reuse

| Capability | Existing component | Decision |
|---|---|---|
| Employee web authentication | Tenant cookie authentication, MFA, password-change flow | Reuse unchanged |
| Device enrollment | `/api/v1/agent/enroll/start`, `/confirm`, `/complete` | Extend; do not duplicate |
| Device authentication | Agent JWT scheme | Reuse with active-device validation |
| Employee profile | `/api/v1/users/me`, `IUserProfileRepository` | Reuse employee resolution |
| Device record | `registered_agents` | Reuse as device source of truth |
| Device session | `agent_sessions` | Reuse |
| Agent policy | `agent_policies` | Extend response with setup/runtime requirements |
| Employee work mode | `employee_work_location_settings` | Reuse |
| Reference-photo base | `verification_reference_photos` | Complete to canonical model |
| Consent | `gdpr_consent_records` | Reuse for photo/biometric consent |
| File storage | `IFileStorageService`, R2 adapter, `file_records` | Reuse with a restricted photo purpose |
| Permissions | `attendance:*`, `verification:*`, `agent:*` | Reuse existing permissions |
| Tenant security | tenant context, EF filters, PostgreSQL RLS | Apply to every new tenant-owned table |
| Durable side effects | `outbox_messages` | Reuse for monitoring and notification events |

## 4. Required Corrections to Existing Code

### 4.1 Employee identifier correction

`ConfirmEnrollmentCommandHandler` currently passes `ICurrentUser.UserId` as both the user and employee identifiers. Enrollment must resolve:

`authenticated user id -> employees.user_id -> employees.id`

The real `employees.id` is saved to the challenge, `registered_agents`, `agent_sessions`, reference photo, location profile, verification, and attendance records. Enrollment fails closed when the authenticated user has no active employee profile.

### 4.2 Device revocation enforcement

Possession of an unexpired Agent JWT is not sufficient. Every protected agent mutation resolves the `registered_agents` row and verifies:

- tenant matches the JWT tenant;
- agent id matches the JWT subject;
- status is `active`;
- employee binding is present and active;
- the device is the employee's currently approved device.

A revoked or inactive device is rejected immediately even if its JWT has not expired.

### 4.3 Legal entity office location

Complete the existing `LegalEntity` implementation with the canonical fields:

- `office_address_label`
- `office_latitude decimal(10,7)`
- `office_longitude decimal(10,7)`
- `office_allowed_radius_meters`
- `timezone` where it is still absent from the current implementation

No separate office-location table is introduced in Phase 1.

## 5. Architecture Boundaries

### Backend

The existing pattern remains mandatory:

`Controller -> MediatR command/query -> handler -> Application interface -> Infrastructure adapter/repository -> PostgreSQL/R2/provider`

Controllers remain thin. They do not use `ApplicationDbContext`, compare faces, calculate distance, upload directly to R2, or implement policy rules.

### Windows Service

The Service owns:

- Agent JWT and DPAPI-protected credentials
- Backend HTTP calls
- retry/idempotency behavior
- device identity
- policy and setup state
- secure photo forwarding
- monitoring state

### Tray App

The Tray App owns only interactive-session functions:

- UI and animations
- user-triggered location permission and capture
- user-triggered camera permission, preview, capture, retake, and approval
- displaying backend-derived state

The Tray App never stores the Agent JWT and never calls the ONEVO backend directly.

### Shared project

The Shared project owns versioned DTOs, IPC envelopes, enums, and bounded contracts. It contains no MAUI, Windows, HTTP, database, or provider dependencies.

## 6. Authentication and Single Sign-In

### First enrollment

1. Tray asks the Service to start enrollment.
2. Service calls the existing `enroll/start`.
3. Tray opens the returned `auth_url`.
4. The browser reuses an existing ONEVO tenant session when available.
5. Otherwise the employee signs in through the existing web email/password/MFA flow.
6. The authenticated web session confirms the enrollment and returns the one-time authorization code.
7. The code reaches the Service through the loopback callback or manual-code entry.
8. Service completes enrollment and stores the device credential with DPAPI.
9. The employee is not asked to sign in again for each shift.

The Tray App does not accept or store raw ONEVO passwords.

### Enrollment security

- Authorization codes are random, hashed at rest, single use, short lived, and bound to enrollment id and device id.
- Tenant and employee identities come only from the authenticated web session.
- A code cannot enroll a different device from the one that started the challenge.
- Complete-enrollment is idempotent for the same accepted challenge and device.

## 7. Approved Device and Replacement Flow

### First approved device

When the employee has no approved device, successful authenticated enrollment activates that device. The new `registered_agents` row becomes the employee's sole approved device.

### Attempt from another device

When an approved device already exists:

1. The new device may complete identity proof through the normal web enrollment.
2. The candidate `registered_agents` row remains `inactive`.
3. Backend creates an `agent_device_change_requests` row with `pending` status.
4. Candidate receives only enough authenticated access to poll its request/setup status.
5. Login, clock-in, monitoring, policy ingestion, and photo verification are blocked on the candidate device.
6. The old approved device remains active while the request is pending.

### Approval

An HR/Admin user with existing `agent:manage` permission can approve or reject the request.

Approval is one transaction:

1. lock the employee's current and candidate device rows;
2. confirm the request is still pending;
3. revoke the old device and end its active agent session;
4. activate the candidate device;
5. mark the request approved with reviewer and timestamp;
6. add an outbox message to stop/revoke the old agent;
7. commit.

The employee then uses only the newly approved device. Rejection keeps the old device active and the candidate inactive.

### Device-change data

`agent_device_change_requests` contains:

- id, tenant id, employee id
- current agent id, requested agent id
- status: `pending`, `approved`, `rejected`, `cancelled`, `expired`
- requested timestamp and optional employee reason
- reviewed by, reviewed timestamp, and review comment
- created/updated timestamps

Only one pending request per employee is allowed. The table receives an EF tenant filter, RLS policy, indexes, and optimistic concurrency protection.

## 8. Tray Application States and Screens

The supplied five reference screens are the visual source of truth.

### Screen 1: Sign in / device enrollment

- ONEVO branding
- primary “Open browser to sign in” action
- manual authorization-code fallback
- enrollment expiry and retry state
- device-change-pending state when this is not the approved device

### Screen 2: Setup hub

- employee identity and approved-device status
- Location card
- Face Scan card
- each card shows required, pending, completed, or failed
- Continue remains disabled until all Company-required setup steps pass

### Screen 3: Location

- clear Windows permission explanation
- user-triggered permission request
- current capture progress
- captured accuracy and a human-readable success state
- retry when accuracy is outside the accepted policy
- no background or continuous location collection

### Screen 4: Face scan

- consent notice before camera activation
- live camera preview
- face-position guide and scanning animation
- Capture, Retake, and Approve actions
- Approve records employee consent and submits the chosen frame

### Screen 5: Setup complete

- animated success confirmation
- location, face, and device completion summary
- transition into the normal tray dashboard

### Normal tray dashboard

- employee and approved-device identity
- current presence state
- Clock In / Clock Out
- policy requirements for the next action
- monitoring stopped/active/paused state
- device-change, location-change, or HR-approval pending message

### Motion and accessibility

- screen transitions: approximately 180-240 ms
- camera scan line/pulse only while the camera is active
- progress animation only during real work
- success animation runs once
- Windows reduced-motion preference disables nonessential motion
- keyboard navigation, visible focus, semantic labels, and sufficient contrast are required

## 9. Location Design

### Captured signals

For a user-triggered setup or clock action, the Tray captures:

- Windows latitude and longitude
- OS-reported accuracy in meters
- capture timestamp
- permission state

The Service adds available device/network evidence:

- public IP as observed by the server
- local network classification
- hashed Wi-Fi BSSID when available
- hashed gateway MAC when available
- VPN detection
- registered agent id

Raw Wi-Fi and gateway identifiers are not stored; tenant-scoped keyed hashes are stored.

### Expected location

- `onsite`: the active Company's `legal_entities.office_*` values
- `remote`: the employee's active `employee_remote_work_profiles` row
- `either`: Company office or approved remote profile, following policy choice
- `field`: no strict office/remote match unless a later Company policy explicitly enables it

### Distance

Backend calculates distance with a deterministic geodesic/Haversine service. The client never decides match or mismatch.

The effective allowed range comes from Clock-in Policy when configured; otherwise the Company office radius is used for onsite. The captured OS accuracy is included in the decision and audit result. Missing, stale, invalid, or implausibly inaccurate coordinates fail closed when location verification is required.

### Initial remote profile

The first remote setup creates `employee_remote_work_profiles` from the authenticated approved device. It includes the permitted coarse location and network evidence and links to a successful verification record/reference capture when policy requires it.

Only one active remote profile per employee is allowed. A replacement uses `remote_work_location_change_requests`; it never silently overwrites the approved profile.

### Privacy

Location is captured only during explicit setup, clock-in, or clock-out actions. No continuous GPS trail is stored.

## 10. Face Capture and Verification

### Reference enrollment

1. Backend policy states whether a reference photo is required.
2. Tray displays the current consent notice/version.
3. Employee grants Windows camera permission.
4. Employee previews, captures, retakes if needed, and explicitly approves one image.
5. Service submits the approved image and notice version.
6. Backend atomically records consent, private file metadata, and reference-photo metadata.
7. Policy determines whether a trusted authenticated enrollment becomes approved immediately or remains pending for manual review.

Employee approval is consent to use the chosen photo. It is not by itself a server-side identity-match result.

### Clock-in face challenge

The resolved verification/clock-in policy decides whether the current action needs a face capture. It may require a photo for onsite, remote, either, field, clock-in, clock-out, or none.

When required:

1. Tray captures a fresh image.
2. Service securely forwards it to the backend.
3. Backend performs image validation and quality checks.
4. Backend compares it with the employee's active approved reference photo through the server-side Rekognition adapter.
5. Backend saves a `verification_records` row and restricted `verification_evidence_assets` metadata.
6. Clock-in proceeds only when the policy result is accepted.

The implementation performs photo quality and face comparison. It must not claim certified liveness detection.

### File handling

- JPEG/PNG only, with strict magic-byte, dimensions, and size validation
- dedicated `verification_reference_photo` and `verification_evidence` upload purposes
- private R2 objects through the existing file-storage service
- database stores metadata and object references, never photo bytes
- no photo bytes, base64, object keys, or local paths in logs
- local image is deleted immediately after confirmed upload or terminal failure cleanup
- retention and deletion use existing file-record lifecycle controls

## 11. Clock-In Policy and Attendance

### Policy

Implement the canonical `clock_in_policies` behavior needed by this flow:

- legal entity and scope/effective dates
- location verification required
- allowed radius
- per-work-area tray source enabled
- per-work-area photo requirement
- either-day source rule
- active state

Existing permissions are used:

- employee self clock action: module auto-grant `attendance:write-own`
- HR attendance/location approval: `attendance:approve`
- verification review: `verification:review`
- policy configuration: `verification:configure` and existing attendance/configuration ownership

### Expected work area

Resolve in canonical order:

1. approved one-day work area change
2. roster override when the Roster module supplies one
3. shift override when the Shifts module supplies one
4. schedule-day work area
5. `employee_work_location_settings.work_mode` fallback

This delivery implements `work_schedules`, `work_schedule_days`, `schedule_assignments`, and `work_schedule_holidays` for the weekly working-day, time, holiday, and work-area decision. Full roster planning and shift-management CRUD are outside this tray scope. The resolver exposes roster and shift lookups as optional application interfaces: if those modules have no applicable row, resolution continues to schedule-day and employee fallback. When no valid working-day and source policy can be resolved, the backend fails closed rather than guessing.

### Clock-in context

Before showing or starting Clock In, Service requests a server-derived context containing:

- eligible/not eligible and reason
- expected work area
- approved agent id
- location requirement and radius
- face requirement
- active reference/profile readiness
- current presence state

The response contains no secret provider configuration.

### Clock-in command

The clock-in mutation:

- uses the Agent JWT subject to resolve agent, tenant, and employee;
- accepts an idempotency key;
- uses server time as the authoritative clock-in time;
- validates the approved device;
- validates schedule/source policy;
- validates current location when required;
- validates face verification when required;
- creates/updates `attendance_records`;
- creates the active attendance/presence state;
- writes a `PresenceSessionStarted` outbox message in the same transaction.

Only after commit may Agent Gateway deliver `StartMonitoring`. No monitoring starts before a successful clock-in.

Clock-out commits the end state and `PresenceSessionEnded`; breaks commit pause/resume events. Monitoring is silent before clock-in, during breaks/approved Time Off, and after clock-out.

## 12. Location Mismatch and HR Approval

Location mismatch always blocks the current clock-in when location verification is required.

The backend creates the canonical request type:

- wrong planned work area for the day -> `work_area_change_requests`
- changed permanent remote location -> `remote_work_location_change_requests`

The response returns `clock_in_status = blocked_pending_approval`, request id, request type, and a safe employee-facing reason.

The Tray displays the pending state and does not start monitoring. Approval does not fabricate an earlier clock-in timestamp. After approval, the employee retries Clock In and the backend re-evaluates device, current location, policy, and optional face requirements.

Future HR web screens use backend list/detail/approve/reject endpoints protected by `attendance:approve`. Collection endpoints are paginated and tenant/scoping rules apply.

## 13. Local IPC and Photo Transfer

### Control channel

Use versioned Windows Named Pipe messages for:

- status and setup state
- start enrollment
- submit manual code
- request location capture
- request face capture
- clock-in/out commands
- progress and result events

Control messages stay below the existing 64 KB limit.

### Binary capture channel

Camera bytes do not enter the control envelope. A separate bounded, authenticated local binary Named Pipe transfers one capture at a time:

- current interactive user only through Windows pipe ACLs
- correlation id and declared length
- strict maximum size
- timeout and cancellation
- checksum validation
- no diagnostic payload logging

The Service streams the image to the backend and disposes buffers promptly. It does not persist a reusable shared photo file.

### IPC trust

The Service validates pipe client identity/session, message version, allowed state transition, correlation id, and size. Tray-supplied tenant, employee, or approved-device claims are ignored.

## 14. Backend API Surface

Existing enrollment routes remain.

New or extended Agent routes:

- `GET /api/v1/agent/setup/status`
- `POST /api/v1/agent/setup/location`
- `POST /api/v1/agent/setup/reference-photo`
- `GET /api/v1/agent/device-change/status`
- `GET /api/v1/agent-fleet/device-change-requests`
- `PUT /api/v1/agent-fleet/device-change-requests/{id}/approve`
- `PUT /api/v1/agent-fleet/device-change-requests/{id}/reject`

Time & Attendance routes:

- `GET /api/v1/time-attendance/presence/current`
- `GET /api/v1/time-attendance/clock-in/context`
- `POST /api/v1/time-attendance/clock-in`
- `POST /api/v1/time-attendance/clock-out`
- `POST /api/v1/time-attendance/breaks/start`
- `POST /api/v1/time-attendance/breaks/end`
- paginated work-area and remote-location approval endpoints

All customer browser mutations use CSRF protection. Agent routes use Agent JWT plus active-approved-device validation. Tenant ids and employee ids are never accepted as authority from request bodies.

## 15. Data Changes

Complete or add only canonical/focused data:

- extend `legal_entities` with canonical Company office fields
- correct employee ids in enrollment records
- add `agent_device_change_requests`
- implement `employee_remote_work_profiles`
- implement `remote_work_location_change_requests`
- complete `verification_reference_photos`
- implement `verification_policies`
- implement `verification_records`
- implement `verification_evidence_assets`
- implement the required `clock_in_policies`
- implement `work_schedules`, `work_schedule_days`, `schedule_assignments`, and `work_schedule_holidays`
- implement `attendance_records`, `presence_sessions`, `break_records`, and `device_sessions`
- implement `work_area_change_requests`
- add restricted file upload purposes

Every tenant-owned table receives:

- `tenant_id`
- EF global tenant query filter
- PostgreSQL RLS policy using the existing tenant setting
- tenant-leading indexes and unique constraints
- foreign keys to tenant-owned parent rows
- migration rollback behavior

No photos are stored in PostgreSQL.

## 16. Failure and Recovery Behavior

| Failure | Required behavior |
|---|---|
| Browser enrollment expires | return to sign-in with retry; no partial active device |
| Employee has another approved device | create/preserve one pending replacement request; block candidate |
| Device revoked after JWT issue | reject immediately through database status check |
| Location permission denied | show Windows Settings guidance; block when required |
| Location unavailable/inaccurate | bounded retry; fail closed when required |
| Camera permission denied | block only actions whose policy requires a photo |
| No approved reference photo | block face-required clock-in with setup/review state |
| Image invalid or no single usable face | allow retake; do not create successful verification |
| Rekognition/provider unavailable | retry with resilience policy; do not falsely accept |
| Network offline | no provisional clock-in; preserve UI state and retry safely |
| Duplicate clock-in request | return the idempotent original result |
| Concurrent clock-in | one active session wins; other request receives conflict/current state |
| Location mismatch | block and return/create the single pending canonical request |
| Photo upload fails | retry securely, then clear local bytes and show retry state |
| Outbox delivery fails | attendance remains committed; delivery retries idempotently |

## 17. Security and Privacy Requirements

- Browser auth uses the existing secure opaque tenant cookie; device auth uses Agent JWT.
- Agent JWT is DPAPI-protected and owned by the Windows Service.
- Passwords never pass through Tray IPC or Service storage.
- Server derives tenant, employee, and device identity from trusted authentication state.
- RLS and EF filters protect all tenant data.
- Critical mutations use idempotency keys and database transactions.
- Approval transitions use concurrency protection and audit reviewer identity.
- Device/network identifiers are minimized and hashed where required.
- Location and camera operate only after explicit user action and OS permission.
- Photos are private, access controlled, retention controlled, and absent from logs.
- Face thresholds, location evidence, and policy results are server controlled.
- Backend does not trust client distance, face-match, clock time, or approval results.
- Monitoring begins only after policy, consent, approved device, and committed clock-in.

## 18. Testing Strategy

### Backend unit tests

- user id resolves to the correct employee id
- first device activation
- second device creates pending request
- approval atomically revokes old and activates new
- revoked token/device is rejected
- policy and expected-work-area resolution
- distance and accuracy handling
- initial remote profile and replacement rules
- consent/reference-photo state transitions
- face quality/match result mapping
- location mismatch request selection
- clock-in idempotency and active-session conflict
- outbox message creation

### Backend integration tests

- PostgreSQL migrations and constraints
- RLS cross-tenant denial for every new table
- Agent JWT subject/tenant/device binding
- browser permission and CSRF rules
- R2 metadata and restricted photo purpose
- full device enrollment -> setup -> clock-in transaction
- mismatch -> pending approval -> approval -> retry clock-in
- old device denied immediately after replacement approval

### Architecture tests

- controllers do not depend on Infrastructure or DbContext
- Domain does not depend on Application/Infrastructure/API
- Application depends on interfaces, not provider adapters
- provider-specific Rekognition/R2 code remains in Infrastructure

### Windows Service tests

- DPAPI credential lifecycle
- enrollment and manual-code flows
- IPC identity, size, version, timeout, and malformed-message rejection
- binary photo streaming and cleanup
- backend error/state mapping
- revoked/pending device behavior

### Tray tests

- state-machine/view-model tests for every screen and failure state
- permission denied/retry behavior
- camera retake/approve behavior
- reduced-motion behavior
- no token/password/backend client dependency in Tray App

### Hardware-assisted acceptance

On a real Windows device:

1. install and launch Service + Tray;
2. enroll with existing browser session and with email/password fallback;
3. verify Windows location permission and real coordinates;
4. verify Windows camera permission, preview, retake, and approved capture;
5. complete setup and restart Windows/app without signing in again;
6. clock in from the approved device;
7. verify policy-triggered face capture;
8. verify out-of-range clock-in is blocked with pending approval;
9. verify a second device cannot clock in before approval;
10. approve replacement through the backend API and verify old-device denial/new-device success;
11. verify clock-out stops monitoring.

## 19. Delivery Slices

Implementation planning will split the work into independently verifiable vertical slices:

1. Enrollment employee-id fix and approved-device replacement
2. Service/Tray IPC and bootstrap state
3. Windows location setup and remote/office persistence
4. Camera, consent, R2, reference-photo enrollment
5. Verification policy and server-side face comparison
6. Clock-in policy, attendance/presence, and monitoring outbox
7. Location mismatch and HR approval APIs
8. Five-screen Tray UI, dashboard, animations, and accessibility
9. End-to-end security, migration, hardware, and packaging verification

Each slice must leave tests passing and must not bypass the Service/backend boundary to make the UI appear complete.

## 20. Completion Criteria

The feature is complete only when:

- one employee has exactly one approved desktop;
- another desktop is blocked until an audited HR/Admin approval replaces it;
- the backend can prove which approved agent produced every clock event;
- real Windows location and camera permissions are exercised;
- location is rechecked at clock-in;
- face capture occurs only when Company policy requires it;
- mismatch blocks clock-in and creates the correct pending approval;
- attendance/presence commits before monitoring starts;
- private photos reach R2 and are not stored/logged locally or in PostgreSQL;
- all new data is tenant isolated through EF filters and PostgreSQL RLS;
- the supplied five-screen Tray experience and animations are implemented;
- backend, Service, Tray, integration, architecture, and Windows acceptance checks pass.
