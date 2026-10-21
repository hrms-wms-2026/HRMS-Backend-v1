# Meetings as Tasks — Phase 2 release runbook (Release A)

Spec: [`docs/superpowers/specs/2026-10-18-meeting-task-phase2-design.md`](../superpowers/specs/2026-10-18-meeting-task-phase2-design.md) §8, §9.
Plan: [`docs/superpowers/plans/2026-10-18-meeting-task-phase2-A-foundation.md`](../superpowers/plans/2026-10-18-meeting-task-phase2-A-foundation.md).

## What Release A ships

Schema and primitives only. **No meeting behaviour is switched on.**

| Piece | What it is |
|---|---|
| Migration `AddMeetingWorkItemsFoundation` | The 7 meeting tables + `job_leases`; owner columns on `calendar_event_participants`, `calendar_event_guests`, `calendar_event_meetings`, `calendar_event_meeting_attendances`, `external_calendar_event_links`; tenant-composite FKs; one-owner CHECKs; the occurrence key `uq_meeting_details_series_original_start` (NOT DEFERRABLE); deferred pairing and placement triggers; forced RLS on the meeting tables |
| `ExcludeMeetings` query filter | Ordinary task queries never see a meeting task |
| `IMeetingOccurrenceWriter` | Concurrency-safe occurrence insert and remap (spec §5.6, verified on PostgreSQL) |
| `IJobLeaseService` | One instance at a time for singleton background jobs |
| `Meetings` settings | All flags `false`; attendance timings validated at startup |

## Release order

```
P-1 (outbox claim fix)  →  Release A (this)  →  Release B (meeting core, Plan B)
```

- **P-1** is branch `fix/outbox-concurrency`, migration `AddOutboxClaimLease`. It may ship before or with A. Release A itself does not need it.
- **`Meetings:BulkInvite` needs P-1.** With the flag on and P-1 missing (no `outbox_messages.locked_by`), the app refuses to start: `Meetings:BulkInvite needs the outbox claim fix (P-1)`.
- Release B only starts after A is live everywhere.
- **Release B = Plans B + C together.** Plan B (meeting core: `/meetings` API, approval, splits, moves, the 7-day generator)
  sends no invites, links or attendance; Plan C adds them. Keep `Meetings:Create` **off** until Plan C is deployed too.
  Plan B adds migration `AddProjectMeetingNumber` (`projects.next_meeting_number`, existing projects start at 1).
- `Meetings:Background` turns on the hourly generator. It runs on one instance at a time (`job_leases`) and can be
  switched on before `Meetings:Create`: with no meetings it does nothing.

## Before deploying

1. **Run the preflight, read-only, as the migrator.**

   ```bash
   psql "$MIGRATION_CONNECTION" -v ON_ERROR_STOP=1 -f ops/meetings/phase2-preflight.sql
   ```

   **Every count must be 0.** Non-zero means the migration's `VALIDATE CONSTRAINT` step would fail; fix that data first. The script runs inside `BEGIN READ ONLY … ROLLBACK`, so it never changes anything.

2. **Check the settings.** Every `Meetings:*` flag stays `false` in A: `Create`, `BulkInvite`, `Background`, `ReadOnly`. Leave `Meetings:Attendance` at the appsettings defaults (30m / 2h / 24h checkpoints, 1h match window, 7d recheck, 3d retry). A bad value stops the app at startup, and the error names the key.

## Deploy

The normal deploy applies the migration (`DatabaseMigrationRunner`, as `onevo_migrator`).

- **It runs in one transaction, and every lock is held until it commits.**
  - The five calendar child tables (`calendar_event_participants`, `calendar_event_guests`, `calendar_event_meetings`, `calendar_event_meeting_attendances`, `external_calendar_event_links`) are altered first, under **ACCESS EXCLUSIVE**. While the migration runs, **reads and writes on them are blocked**: the calendar is unavailable.
  - The unique indexes on `tasks` and `objectives` are built without `CONCURRENTLY`, so **task and module writes are blocked** until commit.
  - Duration grows with the size of `tasks` and `objectives`. Deploy off-peak.
- **`lock_timeout` is 10 s.** If a long-running reader holds one of those tables, the migration fails fast with `55P03 lock_not_available` instead of queueing every request behind it. Nothing is applied in that case. Retry the deploy when the reader is gone.

After the deploy:
- `SELECT migration_id FROM "__EFMigrationsHistory" ORDER BY migration_id DESC LIMIT 1;` returns `…_AddMeetingWorkItemsFoundation`.
- Re-running the preflight now reports `already applied`.

## Release B (Plans B + C)

Ships the meeting behaviour behind the same flags. Deploy Plans B and C together; switch `Meetings:Create` on only after both are live.

| Piece | What it is |
|---|---|
| Migration `AddProjectMeetingNumber` | `projects.next_meeting_number` (short ids `{Identifier}-M{n}`) |
| Migration `AddExternalLinkPushedFingerprint` | `external_calendar_event_links.pushed_fingerprint` (nullable): the organizer's external calendar copy of an occurrence is re-pushed only when it changes |
| `MeetingOccurrenceGeneratorJob` | Hourly, lease `meetings.occurrence-generator` |
| `MeetingAttendanceJob` | Every 15 min, lease `meetings.attendance`: per-occurrence attendance at 30m / 2h / 24h after each occurrence, one retry 72 h after an unavailable/failed last check; also cancels parked links nothing needs any more |
| Notification catalog | `calendar_event_meeting_link_unknown`, `calendar_event_meeting_unresolved`, `calendar_event_meeting_cancel_failed` are orchestrator-owned calendar events (in-app; tenants may route them) |

**Provider scopes - before switching `Meetings:Create` on.**

- **Zoom:** add `meeting:read:list_past_instances` and `meeting:read:list_past_participants` to the Zoom Marketplace app
  (both are in the platform's default Zoom scopes). Existing Zoom connections must **reconnect** to get them. Until a user
  reconnects, their occurrences' attendance ends `sync_failed` with `lastError: "missing_scope"`; meetings and links work.
- **Microsoft:** no new scope. Outlook copies of the organizer's occurrences use the existing `Calendars.ReadWrite`.

**Known limitation - one provider meeting per series.** A series reuses one Teams/Zoom meeting, scheduled at its next
occurrence. Zoom expires a scheduled meeting's ID 30 days after its scheduled time or last start, so a series with gaps
longer than 30 days (monthly, quarterly) can end up with a link Zoom no longer accepts while ONEVO still shows it active.
Weekly and fortnightly series keep it alive. Workaround: the organizer regenerates the link
(`POST …/link` with `regenerate: true`). Teams meeting links expire only after long inactivity.

**Flags.**

- `Meetings:Background` runs both jobs (one instance at a time).
- `Meetings:ReadOnly` also blocks every link write (create/regenerate, retry, remove, cleanup-confirmed) and the
  attendance recheck with 409 `meetings_read_only`; reads keep working. Background attendance sync keeps running.

**Checks after the deploy.**

- `SELECT migration_id FROM "__EFMigrationsHistory" ORDER BY migration_id DESC LIMIT 1;` returns `…_AddExternalLinkPushedFingerprint`.
- With `Meetings:Background` on, the logs show `MeetingAttendanceJob` runs; an error-level line
  `Meeting attendance sync for link … failed 3 times in a row` needs a look (usually a lost connection or scope).

**Rollback of B.** `AddExternalLinkPushedFingerprint` is additive (nullable column); its `Down()` drops it. Once
meetings exist, recover with `Meetings:ReadOnly` and fix forward, as for A.

## Pacing calendar invite emails (`Email:MaxPerSecond`)

Plan D Task 3. A bulk invite fans one event out to many people, and each of them gets a calendar email.
`Email:MaxPerSecond` caps how many of those emails may **start** within any rolling second.

| Setting | Default | Meaning |
|---|---|---|
| `Email:MaxPerSecond` | `0` | At most this many calendar invite emails per rolling second. `0` = unlimited. |

- **The cap is per app instance, not cluster-wide.** Each instance keeps its own sliding window, so the total
  rate against the provider is the limit times the instance count. Set it to the **provider's limit divided by
  the instance count** - e.g. a provider limit of 100/s on three instances means `Email:MaxPerSecond` of 33.
- The setting is read at startup, like every other `Email:*` setting: **a change takes effect after a restart.**
- A **negative** value stops the app at startup, and the error names `Email:MaxPerSecond`. It is not clamped
  to 0, because "unlimited" is the opposite of what a negative number was meant to say.
- `0` (unlimited) is the shipped default. It is a deployment setting, not a code default, because only the
  operator knows the provider's limit and the instance count.
- The limit applies to **calendar invite emails** (the invite, update, cancellation and removal emails the
  calendar outbox handler sends). It does not pace password resets, onboarding invites or invoices.
- When a send is over the limit the outbox handler waits before sending; the message stays pending in the
  outbox and is not lost. A restart during the wait simply re-sends it later.

## Rollback rules

**Never roll back below Release A once meeting rows exist.**

- `Down()` of `AddMeetingWorkItemsFoundation` raises and aborts if any of these exist:
  - a meeting series;
  - a meeting task;
  - a series-owned participant, guest or link;
  - a meeting push link.
- Dropping those tables would lose meetings.
- **Never work around the guard by deleting meeting rows.**
- A rollback of A is possible only while no meeting data exists, e.g. right after deploying A, since every flag is off.
- **Recovery once meetings exist:** switch `Meetings:ReadOnly` on. That keeps meeting data visible and blocks writes. Then fix forward. The other flags are switched off independently.
  - **Flag changes take effect after an app restart.** The `Meetings` options are read at startup, not reloaded.
- **Code-only rollback of A** (redeploying the previous build against A's schema) is safe **only while no meeting rows exist**: the new columns are nullable or defaulted, and legacy rows keep their legacy owner. Once Release B writes series-owned rows, a pre-A build reads their null `event_id` as a non-null id and fails. From then on, roll forward only.
