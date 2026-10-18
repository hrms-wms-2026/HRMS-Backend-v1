# Notification templates & channel mapping — rollout notes

## Ownership and cutover

Each catalog event has exactly one delivery owner:

- **Legacy** — the producer calls `INotificationDispatcher.SendTemplatedAsync` (directly or via the
  `work_notification` outbox). In-app only. Wording still comes from the platform editor: publishing a
  version updates the template's in-app columns the dispatcher reads.
- **Orchestrator** — the producer calls `INotificationOrchestrator.EnqueueAsync` inside its business
  transaction. Routing follows the tenant's Notification Settings (in-app / email / tray).

Channel support follows ownership, so the settings screen never offers a switch nothing reads:
legacy shared events advertise in-app only and monitoring alerts tray only (both read-only in tenant
settings, `configurable: false`, PUT/DELETE return 422); orchestrator-owned events advertise in-app + email.
Orchestrator-owned events also offer tray: the tray service polls
`api/v1/monitoring/tray/notifications/deliveries/pending`, forwards each delivery to the tray process over
IPC, and acks `.../deliveries/{id}/ack` only after the tray process confirms it accepted the toast
(`client_accepted`). Each tray delivery also enqueues a post-commit `notification_tray_push` that sends the
same `NotificationsPending` hub signal legacy alerts use, so the tray fetches immediately instead of on its
15-minute fallback poll. `NotificationTrayExpiryJob` marks uncollected tray deliveries `expired` every 15 minutes.
Old tray builds never call the generic routes and keep receiving legacy monitoring alerts unchanged.

Ownership is the `OrchestratorOwnedEvents` set in `NotificationEventCatalog`. Cut over one event per change:
add it to the set **and** switch its producer in the same commit, so the old and new path are never both on.

| Module (one line each in `OrchestratorOwnedEvents`) | Owner |
|---|---|
| Work Management, Conversations, Time & Attendance, Leave, Calendar, Monitoring reviewer notices | orchestrator |
| 9 producer-less seeded codes | legacy (nothing raises them) |
| Monitoring tray alerts | legacy monitoring tray transport (excluded in v1) |

Direct producers are cut over through `NotificationDispatcher` (the compatibility adapter): an owned event goes to
the orchestrator, everything else to the legacy insert - never both. The in-app row is written in the producer's
transaction exactly as before. Events whose producer still sends its own email (6 leave, 5 calendar) do not offer
an orchestrator Email channel, so a tenant can never cause a second email.

### In-flight work at cutover

`work_notification` messages already queued for a cut-over event are still handled by
`WorkNotificationOutboxHandler` (in-app, legacy copy). New occurrences go through the orchestrator only.
No double delivery: an occurrence is enqueued by exactly one path.

### Rollback

Remove the module's line (or a single event) from `OrchestratorOwnedEvents` and deploy: the dispatcher and the
Work engine fall back to the legacy path for it. Already-written inbox rows stay; queued email/tray work for it
drains from its captured content.
Queued `notification_delivery` messages keep processing from their captured content. Never run both owners.
No schema rollback after live writes: the new tables are additive.

## Delivery statuses (`notification_deliveries.status`)

| Status | Meaning — and what it does **not** mean |
|---|---|
| `pending` | Reserved, not yet delivered (tray: waiting for the tray client to collect it). |
| `available_in_inbox` | In-app row written. Not "read" — read state stays on `notifications.is_read`. |
| `provider_accepted` | Email provider accepted the message. Not delivered to the mailbox, not opened. |
| `forwarded_to_tray` | Legacy tray client acknowledged receipt from the service. Not shown, not read. |
| `client_accepted` | New tray client confirmed it accepted the toast. Not read. |
| `skipped` | Channel could not apply (e.g. `no_email_address`). See `status_reason`. |
| `expired` | Tray alert not collected before `expires_at` (24 h). |
| `failed` | Gave up (`render_failed`, `no_published_template`, or the email provider failed 8 times). |

## Guarantees and known limits

- One delivery per (tenant, event, recipient, channel): unique index `ix_notification_deliveries_identity`.
- Content (and template version) is captured at enqueue; later publishes never change a queued message.
- Channels are independent: a failing email retries alone; a delivered in-app row is never replayed.
- Email is **at-least-once**: if the provider accepts but the status save fails, or the provider times out
  after accepting, the retry may send a second copy. The providers in use expose no idempotency key here.
- Delivery records are retained; a cleanup job is a follow-up.
- Tray display is at-least-once: if the tray shows the toast but crashes before confirming, the next poll
  re-sends it; the tray suppresses repeats with a bounded on-disk list of shown delivery ids.
- Tray toast clicks open the portal dashboard (bell), not the record: the web app has no route the tray can
  deep-link to yet. The portal URL is the tray's existing hard-coded `WorkspaceLinks.PortalUrl`.
- Emails reuse the template copy; `open_related` is not yet turned into a link in the email body.

## Inspecting failures

```sql
SELECT event_key, channel, status, status_reason, attempt_count, created_at
FROM notification_deliveries
WHERE status IN ('failed', 'skipped') ORDER BY created_at DESC LIMIT 50;
```

Outbox side: `outbox_messages` rows of type `notification_delivery` with `status = 'failed'`.

## Verification status (this branch)

- Unit tests: catalog, renderer, versions/seeder, management/settings handlers, orchestrator, channel handlers.
- Architecture tests: RLS policy coverage for `tenant_notification_rules` and `notification_deliveries`.
- DI: `NotificationDependencyRegistrationTests` checks the new types against the real `AddApplication`/`AddInfrastructure` registrations.
- Migration regression: `NotificationEventMigrationTests` (all migrated events, seeded copy validates).
- **Not yet run**: PostgreSQL integration tests (Testcontainers needs Docker, which was unavailable),
  applying the migration to a database, and an end-to-end send through a running API.
