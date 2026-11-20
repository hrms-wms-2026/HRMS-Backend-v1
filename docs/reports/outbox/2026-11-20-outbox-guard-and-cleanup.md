# Outbox guard and clean-up (2026-11-20)

Task 31 (`docs/Improvements/person-4-deployment-and-architecture/task-31 Outbox mapping guard and clean-up.md`),
following the [2026-11-15 mapping check](2026-11-15-outbox-mapping-check.md).

## What changed

| Task item | Done | Where |
|---|---|---|
| 1. Every type has exactly one handler, every handler a known type | Yes | `OutboxHandlerMappingTests` resolves the handlers from the real `AddApplication` + `AddInfrastructure` registrations and compares them with the `OutboxMessageTypes` constants (read by reflection, so a new constant is checked automatically) |
| 2. Payload round trip per handler | Yes, for the seven handlers that had none | `OutboxPayloadRoundTripTests`: password reset, admin password reset and changed, platform manager invite, employee onboarding invite, invoice, Outlook push (push and delete). The other 13 real handlers already had one |
| 3. Duplicate handler fails start-up, naming both | Yes | `OutboxHandlerMap.Build` (used by `OutboxProcessor` every batch) throws with the type and both class names; `OutboxHandlerStartupCheck` runs it at start-up on every instance, web-only ones included |
| 4. Delete the two unregistered no-op leave handlers | Yes | `NoOpLeaveApprovalSideEffectOutboxHandler`, `NoOpLeaveCancellationSideEffectOutboxHandler` and the test of the second one. Nothing else referenced them |
| 5. Stale comment | Yes | `OutboxMessageTypes` now says the position and security types have no-op handlers |
| 6. The five no-op types | **Team decision**, options below. Behaviour unchanged |
| 7. Purge published rows | Yes | `OutboxRetentionPurgeJob`, every 6 h under its lease: deletes `published` rows older than `Outbox:PublishedRetentionDays` (default 14), 1,000 per statement, logs the count. `failed` and `pending` rows are kept |
| 8. Metric and alert for failed and oldest pending | Metrics existed (task 17); alerts added | `crownus.outbox.failed` > 0 and `crownus.outbox.oldest_pending_age` > 10 min in `docs/deployment/observability.md` |

"Done when" from the task:

- Removing one handler registration makes a test fail: `Mapping_Fails_WhenAHandlerRegistrationIsRemoved` removes the
  password reset handler and shows exactly that type unhandled, which is what `EveryMessageType_HasExactlyOneHandler`
  fails on.
- A second handler for the same type fails start-up with both names:
  `StartupCheck_Fails_WhenATypeIsRegisteredTwice_NamingBothHandlers`.
- Old published rows are removed and the count is logged: `OutboxRetentionPurgeIntegrationTests` (SQL Server, as
  the `crownus_app` login under row-level security), covering tenant and tenant-less rows and showing that recent
  published, failed and pending rows stay.

## The five no-op types: options for the team

`position_created`, `position_updated`, `position_archived`, `position_restored` (one write per position change)
and `employee_security_updated` (password change, MFA disable). Each is encrypted, stored, claimed, "handled" by a
no-op and marked published. Nothing reads them.

| Option | What it means | Cost |
|---|---|---|
| A. Stop writing them (recommended until a consumer exists) | Remove the five `EnqueueAsync` calls; keep the constants and no-op handlers for one release so rows already queued drain, then delete them | Smallest. Loses nothing today, since nobody reads them. A future consumer adds the calls back |
| B. Keep them, name a consumer | Write down who will consume each (for example search index, audit export, SIEM) and by when | Keeps the extra writes until then |
| C. Move security changes to the audit log | `employee_security_updated` is really an audit fact; write it to the audit log, which is read, instead of the outbox | Small; the audit table already exists |

Whatever is chosen, the mapping test keeps the rule: a type that stays must keep exactly one handler.

## Tests run

| Suite | Result |
|---|---|
| New unit tests (`Infrastructure.Outbox`) | 15 passed |
| Outbox integration tests (`SharedPlatform.Outbox*`, SQL Server container) | 26 passed, the 2 new purge tests included |
| Full unit suite (Release) | 8,162 passed (two counts in `BackgroundJobsRegistrationTests` updated for the new job and check) |
| Architecture suite | 713 passed |

## Not covered

- The purge has no index on `processed_at`; with the 14-day window and 1,000-row batches that is fine for current
  volumes. Add an index on `(status, processed_at)` if the table grows past a few hundred thousand rows.
- The alerts are documented, not created; they need the UAT Application Insights resource (task 17).
