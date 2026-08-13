# Tray Monitoring Completion Roadmap Design

**Status:** Approved in chat on 2026-08-13
**Implementation status:** Pending

## Goal

Complete ONEVO employee monitoring as a Windows TrayApp-first product, one
independently verified milestone at a time. The roadmap finishes biometric
identity and check-in first, then builds meeting detection, role-scoped
employee and manager views, deterministic exceptions, workforce reporting,
and wellness notifications.

## Existing-System Findings

- The workspace contains `HRMS-Backend-v1` and `tray_app_maui`; it does not
  contain a separate web frontend. The monitoring experience will therefore
  be delivered in the Windows TrayApp.
- Keyboard/mouse activity, app usage, device state, inactivity prompts,
  screenshot evidence, daily summary aggregation, and the employee daily
  report already have backend and Tray/Service foundations.
- The inactivity screenshot and daily report automated validation passed on
  2026-08-13. The remaining two-monitor/manual Windows checklist is an
  operational gate, not a reason to rebuild the feature.
- Biometric enrollment software and automated tests are complete. Live AWS
  staging plus real-camera verification remains the first release gate.
- Verified check-in is already decomposed into four plans. Part 1 software is
  present; Parts 2-4 remain the implementation path for strict online,
  employer review/online fallback, and offline recovery.
- Employee identity fields already travel through activation/IPC into the
  TrayApp, but the complete real-environment flow still needs an audit and
  smoke-test closure.
- `MeetingDetection` exists as a policy capability, and daily summaries have
  `TotalMeetingMinutes`, but no meeting-session collector/ingest/timeline is
  implemented.
- No monitoring exception/discrepancy engine is implemented.

## Locked Product Decisions

1. There is no monitoring web frontend in this roadmap.
2. Employee and manager monitoring views live in the Windows MAUI TrayApp.
3. The build proceeds sequentially; a milestone must pass its acceptance gate
   before the next milestone starts.
4. Live AWS staging and a real webcam are available for the first milestone.
5. Meeting detection is implemented locally by the TrayApp; calendar
   integration is not required.
6. Meeting audio, video, participant names, meeting titles, and message content
   are never recorded.
7. The TrayApp never stores or uses the device JWT directly. All backend calls
   flow through typed IPC to the Windows Agent Service.
8. Manager access is derived and enforced by the backend. UI flags and cached
   preferences are never authorization sources.
9. The first exception engine is deterministic and configurable. Predictive or
   AI scoring is out of scope.
10. Wellness reminders use local Windows notifications governed by effective
    backend policy, thresholds, quiet hours, and cooldowns.

## Architecture

### Runtime boundary

```text
TrayApp collectors and UI
    -> versioned typed IPC
Windows Agent Service
    -> device-bound JWT + HTTPS
Backend monitoring APIs
    -> tenant-isolated PostgreSQL/R2
Aggregators and exception engine
    -> role-scoped dashboard DTOs
Windows Agent Service
    -> typed IPC response
TrayApp employee/manager views
```

The TrayApp owns Windows interaction, presentation, and privacy-preserving
signal detection. The Agent Service owns credentials, durable local queues,
ordered synchronization, and backend API calls. The backend owns identity,
authorization, tenant scope, verification verdicts, normalized monitoring
records, aggregate reports, exception state, and review audit.

### TrayApp dashboard

Add a user-initiated Monitoring Dashboard route without replacing the existing
activation, clock-in, active-session, break, clock-out, and onboarding routes.
The tray icon receives an `Open Dashboard` command. Lifecycle changes may force
navigation only for security-critical states such as locked or unenrolled; a
routine status refresh must not eject a user from the dashboard.

The dashboard contains:

- Employee views: My Day, activity/idle timeline, app usage, meeting timeline,
  personal exceptions, daily/weekly/monthly reports, and wellness settings.
- Manager-only views: team overview, authorized employee detail, exception and
  check-in review queues, and team reports.

The dashboard bootstrap response supplies server-derived capabilities. A
manager route is still protected on every request. A `403` removes manager-only
cached data and returns the UI to an employee-safe view.

### Query and credential boundary

Dashboard ViewModels call a focused `IMonitoringDashboardClient`. Its TrayApp
implementation sends versioned IPC requests. The Service translates those
requests into backend API calls with the device JWT, applies timeouts and safe
error mapping, and returns bounded DTOs. The TrayApp must not receive access or
refresh tokens.

Employee queries are always scoped to the activated employee resolved from the
token. Manager queries are limited to employees the authenticated user is
allowed to monitor; a client-supplied employee ID is only a requested resource,
never proof of access.

## Meeting Detection

### Local signals

The TrayApp adds an isolated meeting collector that samples supported native
conference applications and privacy-safe local browser meeting indicators.
Supported providers begin with Microsoft Teams, Zoom, Google Meet, and Webex.
Provider recognition occurs before privacy scrubbing, on-device only.

The collector emits only:

- provider category;
- normalized start and end timestamps;
- duration;
- detection confidence/reason code;
- work-session and idempotency correlation IDs.

Raw window titles, URLs, meeting names, participants, audio, video, screenshots,
and transcript content are neither persisted nor transmitted.

### Meeting state machine

- Require consecutive positive samples before opening a session.
- Require consecutive negative samples before closing it.
- Merge a bounded short reconnect gap into the same meeting.
- Stop or suspend collection on clock-out, break, effective-policy disablement,
  or lifecycle lock.
- Persist the normalized meeting event through the Service durable queue.
- Use a stable event ID so retries cannot create duplicate sessions.

Backend ingest stores tenant/employee/device/work-session scope, validates
timestamps and duration, and updates `TotalMeetingMinutes`. Employee and manager
timeline endpoints return normalized sessions without sensitive local signals.

## Exception Management

The exception engine consumes normalized activity, idle, meeting, device,
check-in, and sync-health data after those sources are stable. The first rule
set includes low-activity duration, excessive idle duration, missing expected
activity, check-in discrepancies, repeated verification failures, device
offline gaps, and unusual meeting/activity overlap.

Each rule is tenant-configurable with an enabled flag, threshold, severity,
cooldown, and target population. Evaluation produces an immutable occurrence
plus a mutable workflow state: `Open`, `Acknowledged`, `Resolved`, or
`Dismissed`. Every manager action records actor, timestamp, reason, and previous
state. Reprocessing the same source window is idempotent.

Employees can see exceptions explicitly configured as employee-visible.
Managers can see and act only within their authorized employee scope. Exception
details reference supporting normalized records; they do not expose biometric
media, screenshot URLs, raw window titles, or exact GPS values by default.

## Reports and Analytics

Daily summaries remain the base fact. Weekly and monthly workforce aggregates
are rebuilt from daily summaries plus normalized meeting and exception counts.
The API returns bounded, timezone-aware periods and makes incomplete/current
periods explicit.

Employee reports contain only the activated employee's data. Manager reports
are scoped to authorized employees. Executive views require a distinct backend
capability and return workforce aggregates; drill-down continues to enforce
employee scope. CSV/export delivery is deferred unless requested in the
individual report design.

## Wellness Notifications

Wellness evaluation runs locally so reminders remain timely during intermittent
connectivity. Effective backend policy supplies reminder enablement, inactivity
and continuous-focus thresholds, break duration, quiet hours, and cooldown.
Initial nudges are break reminders, prolonged-focus reminders, inactivity
check-ins, and end-of-day/clock-out reminders.

Notifications are advisory. They must not silently pause monitoring, alter an
attendance record, or create an exception merely because an employee dismisses
one. Notification outcomes may be recorded as bounded reason codes when policy
requires analytics; notification body text is not logged.

## Failure and Recovery Rules

- Collectors fail independently; one collector failure cannot stop unrelated
  monitoring collectors or the main Tray process.
- Service queues are durable, ordered, bounded, and crash-safe. Retries use
  exponential backoff and stable idempotency keys.
- Dashboard data may be shown read-only from a bounded cache with a visible
  `Last synced` timestamp. Cached manager data is removed on `403`, identity
  change, device revocation, or logout/reset.
- Corrupt local queue items are quarantined with non-sensitive diagnostics;
  following valid records continue where ordering rules allow.
- Biometric/provider/location failures use explicit user-facing states. Strict
  mode never creates verified attendance without a backend verification verdict.
- All server time windows are timezone-aware and validated against tenant
  reporting timezone.

## Privacy and Security

- Never log raw keystrokes, raw window titles, meeting content, face bytes, AWS
  session credentials, permanent screenshot URLs, exact GPS, device JWTs, or
  refresh tokens.
- Biometric and screenshot media use their dedicated secure transfer/storage
  paths and never enter generic monitoring JSON or ordinary Service SQLite.
- All read, review, policy, and exception actions enforce tenant isolation and
  backend authorization.
- Dashboard DTOs are explicit allowlists; domain entities are not serialized
  directly.
- Local caches containing employee or manager data have bounded retention and
  are cleared when the activated identity changes.

## Sequential Implementation Roadmap

### Milestone 1 - Live AWS biometric enrollment E2E

Deploy the backend to staging with the Mumbai Rekognition/KMS/IAM boundary, run
the real Windows camera flow, complete enrollment, retrieve the active profile,
repeat enrollment, and record the hardware/AWS result. Production enrollment
remains disabled until the live gate passes.

### Milestone 2 - Real employee Tray identity closure

Audit the approved identity plan against current code, remove any remaining
hardcoded identity fallback that can mask a server failure, verify exchange and
refresh behavior, verify identity reset, and complete a real activation smoke
test across Backend, Service, and TrayApp.

### Milestone 3 - Strict online verified check-in

Execute Verified Employee Check-In Part 2: correlation/idempotency persistence,
backend-only verdict, typed IPC, Service coordinator, fresh GPS, liveness, face
comparison, and CLOCK IN lifecycle integration.

### Milestone 4 - Employer review and online fallback

Execute Part 3: tenant policy, permission-protected list/detail/review, immutable
review audit, and review-only fallback for provider/location unavailability.
Spoof or face mismatch outcomes cannot use fallback.

### Milestone 5 - Offline check-in and rollout hardening

Execute Part 4: protected policy cache, encrypted biometric outbox, ordered
sync, retention, safe observability, full fake-provider E2E, hardware pilot, and
tenant-by-tenant rollout flags.

### Milestone 6 - Meetings timeline

Design and implement the Tray meeting collector, shared payloads, Service queue
and sync, backend persistence/ingest/aggregation, employee timeline API, manager
timeline API, and TrayApp timeline UI.

### Milestone 7 - Employee monitoring views

Add dashboard bootstrap, My Day overview, activity/idle/app/meeting timelines,
personal exception surface, loading/empty/error/offline states, and safe cached
read-only data.

### Milestone 8 - Manager monitoring views

Add backend-derived capabilities, scoped team overview, employee detail,
check-in/exception review entry points, pagination/filtering, `403` cache purge,
and manager dashboard UI.

### Milestone 9 - Exception and discrepancy engine

Add rule configuration, evaluator jobs, idempotent occurrences, workflow/audit,
employee visibility policy, manager queue/detail/actions, and TrayApp exception
views.

### Milestone 10 - Weekly and monthly analytics

Add timezone-aware period aggregates, completeness indicators, comparison
metrics, employee analytics endpoints, and employee TrayApp report views.

### Milestone 11 - Manager and executive reports

Add permission-separated team and workforce aggregates, filters, drill-down
scope checks, and manager/executive TrayApp report views.

### Milestone 12 - Wellness notifications

Add policy contracts, local focus/break/inactivity evaluators, quiet hours,
cooldowns, Windows notifications, preferences UI, and bounded outcome metrics.

### Milestone 13 - Production hardening and rollout

Run full backend and Tray suites, tenant-isolation/security tests, offline and
upgrade recovery, resource/performance soak tests, Windows hardware matrix,
privacy log review, operational alerts/runbooks, and staged tenant rollout.

## Milestone Acceptance Gate

Every milestone must produce all applicable evidence before the next starts:

1. focused unit tests;
2. IPC and API contract tests;
3. backend integration and tenant-isolation tests;
4. Tray collector/ViewModel tests;
5. offline, retry, ordering, and idempotency tests;
6. full Backend and Tray test suites;
7. Windows manual smoke result;
8. live AWS/webcam result when biometric behavior changes;
9. privacy/security checklist;
10. dated verification record and documentation sync.

## Planning Boundary

This document locks the master order and cross-cutting boundaries. It is not a
single implementation plan for all thirteen milestones. Each milestone receives
its own focused design/specification where needed, followed by a self-contained
TDD implementation plan. Existing approved Verified Check-In plans are reused
instead of rewritten.

## Out of Scope

- A web monitoring frontend.
- Calendar-provider integration for meeting detection.
- Meeting audio/video recording, transcription, participant capture, or content
  inspection.
- Raw keystroke or raw window-title storage.
- AI/predictive employee-risk scoring in the first exception release.
- Cross-tenant manager access.
- Replacing the existing dedicated biometric and screenshot security paths with
  generic monitoring transport.
