# Notification templates & channel mapping — rollout notes

## Ownership and cutover

Each catalog event has exactly one delivery owner:

- **Legacy** — the producer calls `INotificationDispatcher.SendTemplatedAsync` (directly or via the
  `work_notification` outbox). In-app only. Wording still comes from the platform editor: publishing a
  version updates the template's in-app columns the dispatcher reads.
- **Orchestrator** — the producer calls `INotificationOrchestrator.EnqueueAsync` inside its business
  transaction. Routing follows the tenant's Notification Settings (in-app / email / tray).

Ownership is the `OrchestratorOwnedEvents` set in `NotificationEventCatalog`. Cut over one event per change:
add it to the set **and** switch its producer in the same commit, so the old and new path are never both on.

| Event | Owner | Since |
|---|---|---|
| `work_approval_requested` | orchestrator | this branch |
| every other catalog event | legacy | — |

### In-flight work at cutover

`work_notification` messages already queued for a cut-over event are still handled by
`WorkNotificationOutboxHandler` (in-app, legacy copy). New occurrences go through the orchestrator only.
No double delivery: an occurrence is enqueued by exactly one path.

### Rollback

Remove the event from `OrchestratorOwnedEvents` (the producer falls back to the legacy payload) and deploy.
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
- **Not yet run**: PostgreSQL integration tests (Testcontainers needs Docker, which was unavailable),
  applying the migration to a database, and an end-to-end send through a running API.
