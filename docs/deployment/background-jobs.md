# Background jobs

Task 13 (`docs/Improvements/person-4-deployment-and-architecture/task-13 Background jobs safe for more than one instance.md`).
Every hosted service in the API, how often it runs, what keeps two instances from doing the same work twice, and
what still stops the API from running as a separate web and worker instance.

## How a job runs now

- **One switch.** `BackgroundJobs__Enabled` (default `true`) decides whether the recurring jobs below are
  registered. With `false` the same image runs as a web-only instance. Start-up checks and seeders always run.
  Code: `src/ONEVO.Infrastructure/Configuration/BackgroundJobsRegistration.cs`.
- **One lease per job.** Each recurring job runs its work through `ExclusiveJobRunner`
  (`src/ONEVO.Infrastructure/Services/SharedPlatform/Jobs/ExclusiveJobRunner.cs`), which takes the job's row in
  `job_leases` first. An instance that finds the lease held skips that run.
  - The lease lasts the job's **time limit** plus 2 minutes, and the run is cancelled at the time limit. So the
    lease cannot run out while the job is still allowed to work.
  - After a successful run the lease is **kept for most of the interval** (90 % of it; 20 hours for daily jobs).
    Across all instances a job then runs about once per interval, not once per instance.
  - A failed or timed-out run releases the lease at once, so the next tick on any instance retries.
- **One log line per run:** `Job <name> completed in <ms> ms[, <n> rows processed]` (Information; Debug when 0
  rows or for the 10-second poller), `Job <name> stopped at its time limit …` or `Job <name> failed after <ms> ms`
  (Warning). Rows are logged only where the job already counted them.
- **Daily jobs** (leave credit, activity summary, exception detection) remember the last day they ran in memory.
  An instance that finds the daily lease held treats the day as done, because another instance is running or ran
  it. A restart therefore no longer repeats the day's run while the lease is held.

## Recurring jobs

"Safe twice" = what happens if the work runs twice at the same time or twice in a row, without the lease.

| Job | Lease name | Every | Time limit | What it does | Safe twice without the lease? |
|---|---|---|---|---|---|
| `LeaveCreditJob` | `leave-credit` | daily after 01:00 UTC | 30 min | Monthly leave credits, carry-forward expiry, year close | In a row: yes. Credits are the difference from the audit rows; year close and expiry skip work already done. At the same time: no, two first-time inserts of one entitlement collide |
| `ActivityDailySummaryJob` | `activity-daily-summary` | daily after 23:00 UTC | 30 min | Aggregates yesterday's activity snapshots | Not checked; now leased |
| `ExceptionDetectionJob` | `exception-detection` | daily after 23:30 UTC | 30 min | Multi-day monitoring exception patterns | In a row: yes (`HasUnresolvedAsync` stops duplicates). At the same time: no |
| `CalendarIncidentJob` | `calendar-incident` | 24 h | 30 min | Nightly calendar meeting-exception pass | Not checked; now leased |
| `AttendanceAlertMaterializerJob` | `attendance-alert-materializer` | 1 h | 30 min | Materialises attendance alerts per employee | Not checked; now leased |
| `InboxEscalationSweepJob` | `inbox-escalation-sweep` | 1 h | 30 min | Escalates overdue inbox items (SLA) | Not checked; now leased |
| `ProjectMonitorJob` | `project-monitor` | 1 h | 30 min | Project problems into `wm_monitor_alerts`, notifies creators | Not checked; now leased |
| `LoginWorkspaceSelectionChallengeCleanupService` | `login-workspace-challenge-cleanup` | 1 h | 10 min | Deletes old login challenges (counts rows) | Yes (delete) |
| `MonitoringRetentionPurgeJob` | `monitoring-retention-purge` | 6 h | 1 h | Purges raw activity buffer and expired screenshots | Yes (delete), but wasteful |
| `CalendarSyncJob` | `calendar-sync` | 15 min | 15 min | Syncs external calendar connections | Not checked; at best duplicate provider calls. Now leased |
| `TeamsAttendanceSyncJob` | `teams-attendance-sync` | 15 min | 15 min | Pulls Teams attendance for ended meetings | Not checked; at the same time both would pull the same report. Now leased |
| `ZoomAttendanceSyncJob` | `zoom-attendance-sync` | 15 min | 15 min | Pulls Zoom attendance for ended meetings | As Teams |
| `NotificationTrayExpiryJob` | `notification-tray-expiry` | 15 min | 15 min | Marks uncollected tray deliveries Expired | Yes (one update) |
| `CalendarEventReminderJob` | `calendar-event-reminder` | 5 min | 5 min | Sends due meeting start reminders | Not checked; at the same time reminders could go out twice. Now leased |
| `WellnessRuleEvaluatorJob` | `wellness-rule-evaluator` | 5 min | 5 min | Break and idle alerts for monitored employees | Not checked; now leased |
| `LocationRuleEvaluatorJob` | `location-rule-evaluator` | 5 min | 5 min | Outside-work-location alerts | Not checked; now leased |
| `AgentCommandExpiryJob` | `agent-command-expiry` | 2 min | 2 min | Expires tray commands nobody picked up | Yes (one update) |
| `BulkOnboardingBatchProcessor` | `bulk-onboarding` | 10 s | 2 h | Creates and finalises drafts for bulk onboarding batches | No: both instances would take the oldest pending batch. Released after each pass (no gap) |
| `OfficeProjectReconciliationStartupService` | `office-project-reconciliation` | once, 30 s after start | 30 min | Backfills each tenant's Office project | Not checked; now leased and kept 30 min, so instances started together run it once |
| `OutboxProcessor` | none | 1 s poll (setting) | none | Dispatches outbox messages; hourly idempotency purge | Yes: messages are claimed atomically (`OutboxClaimStore`, `UPDLOCK`/`READPAST`), tested in `OutboxClaimLockingTests` and `OutboxClaimOwnershipIntegrationTests` |
| `MeetingOccurrenceGeneratorJob` | `meetings.occurrence-generator` (its own) | 1 h | lease 10 min, renewed | Generates meeting occurrences | Already leased before task 13; unchanged |
| `MeetingAttendanceJob` | `meetings.attendance` (its own) | 15 min | lease 10 min, renewed | Syncs meeting attendance | Already leased before task 13; unchanged |

"Not checked" means the job's code was not reviewed for duplicate effects. The lease makes the question moot as
long as only the lease decides who runs.

## Always run (not behind the switch)

| Hosted service | Why it stays |
|---|---|
| `ConfigurationStartupValidator` | Logs missing settings at start |
| `MeetingsStartupCheck` | Refuses to start when a Meetings flag lacks what it needs |
| `ReferenceSeedingLock.Acquire` / `.Release` | Session lock around the seeders (below) |
| `PermissionSeeder`, `RoleTemplateSeeder`, `LookupDataSeeder`, `NotificationTemplateSeeder`, `PlatformAccessSeeder`, `PositionTemplatePackSeeder`, `ModuleCatalogSeeder`, `PlatformOAuthProviderMetadataSeeder` | Reference data every instance needs. They read what exists and insert what is missing |
| `DevSmokeTestTenantSeeder`, `WorkManagementDapiDemoSeeder`, `DapiOrgStructureSeeder`, `DapiLeaveSampleSeeder`, `DapiAttendanceDemoSeeder`, `DapiHistorySeeder`, `WorkManagementSampleDataSeeder` | Demo data, Development/Test only |

**Seeders and two instances.** The reference seeders read existing rows and insert the missing ones without a
lock, so two instances starting together could insert the same row and one would stop on the unique key. Hosted
services start one at a time in registration order, so `ReferenceSeedingLock.Acquire` (before the first seeder)
takes a session-level `sp_getapplock` on `crownus:reference-seeding` and `ReferenceSeedingLock.Release` (after the
last) gives it back. A second instance waits, up to 5 minutes, then finds every row present. If an instance dies
while seeding, SQL Server frees the lock with its session (`ReferenceSeedingLockIntegrationTests`).

## Not done in this task

- **Seeders still run on every start.** The task suggests moving reference seeding into the deploy step
  (task 09). That pipeline has never run, so moving seeding into it now could leave a release with a new
  permission and no seeding step. Follow-up `task-13b`: move it after the first successful pipeline run, with the
  lock above as the safety net meanwhile.
- **A worker instance needs the Redis backplane.** Some jobs reach browsers and tray devices through SignalR. Task 21a
  added a Redis backplane ([shared-state.md](shared-state.md)), but UAT has no Redis yet. Without it, messages from a
  worker reach only clients connected to the worker:
  - `OutboxProcessor` runs `InboxChangedOutboxHandler` (inbox live updates) and `NotificationTrayPushOutboxHandler`
    (tray notifications);
  - `WellnessRuleEvaluatorJob` pushes to trays directly (`ITrayPushNotifier`).

  Until Redis is configured, run every instance with `BackgroundJobs__Enabled=true` (the default), or run exactly
  one instance. The leases already make several job-running instances safe.
- **Bulk onboarding can create drafts twice after an interrupted pass.** Row statuses are saved at the end of a
  pass, while each draft is saved as it is created. A pass stopped half way (shutdown, or the 2-hour limit)
  creates those drafts again next time. This was true before task 13 for shutdowns; it needs a save per row.
- **Time limits are first estimates.** Read the `Job … completed in … ms` lines on UAT and adjust.

## Tests

| What | Where |
|---|---|
| Lease, gap, time limit, failure, shutdown, log line | `tests/ONEVO.Tests.Unit/Infrastructure/Jobs/ExclusiveJobRunnerTests.cs` |
| Two instances on SQL Server run the work once; leave credit runs once per day across two instances | `tests/ONEVO.Tests.Integration/SharedPlatform/ExclusiveJobRunnerIntegrationTests.cs` |
| `BackgroundJobs:Enabled=false` registers no recurring job and keeps checks and seeders | `tests/ONEVO.Tests.Unit/Infrastructure/Jobs/BackgroundJobsRegistrationTests.cs` |
| The API boots in Production with jobs off, against SQL Server, and seeds the same reference data | `ReferenceDataParityTests` (runs with `BackgroundJobs__Enabled=false`) |
| Second instance waits for the first one's seeding; a dead instance frees the lock | `tests/ONEVO.Tests.Integration/SharedPlatform/ReferenceSeedingLockIntegrationTests.cs` |
| Outbox claims never hand one message to two instances | `OutboxClaimLockingTests`, `OutboxClaimOwnershipIntegrationTests` (existing) |
