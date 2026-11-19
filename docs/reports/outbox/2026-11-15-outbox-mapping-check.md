# Outbox mapping check, 2026-11-15

Question: is every outbox message type mapped to the right handler and the right payload?

Answer: yes. No broken mapping was found. Three gaps make it easy to break later; they are in `docs/Improvements/person-4-deployment-and-architecture/task-31 ...`.

## How the mapping works

1. A producer calls `IOutboxWriter.EnqueueAsync(type, payload, tenantId)`. The type is a constant from `OutboxMessageTypes`.
2. `OutboxProcessor` loads every registered `IOutboxMessageHandler` and builds a dictionary keyed by `handler.Type`.
3. It decrypts the payload and calls the handler whose `Type` equals the message type.

So three things must line up for each type: a constant, exactly one registered handler with that `Type`, and the same payload class on both sides.

## What was checked

| Check | Result |
|---|---|
| Every constant in `OutboxMessageTypes` has a registered handler | Pass. 25 constants, 25 registrations |
| No type has two registered handlers | Pass. A duplicate would make the dictionary throw and stop the whole processor |
| Every enqueue call uses a constant, not a typed string | Pass. 50 calls, all use `OutboxMessageTypes.*` |
| Every constant has at least one producer | Pass. All 25 are enqueued somewhere |
| Producer payload class equals the class the handler deserialises | Pass for all 20 types with a real handler |
| JSON settings match on both sides | Pass. Writer and handlers both use the default `JsonSerializer` settings |
| Tenant id passed where the handler needs tenant context | Pass. Only the three platform admin emails pass `null`, on purpose |
| Unit tests with "Outbox" in the name | Pass. 75 passed, 0 failed (Release build, 2026-11-15) |
| Rows in the local database | 521 rows, all `inbox_changed`, all `published`. 0 failed, 0 retried, 0 stuck |

## Type to handler table

| Type | Handler | Payload | Producers |
|---|---|---|---|
| `tenant_owner_invite_email` | `TenantOwnerInviteEmailOutboxHandler` | `TenantOwnerInviteEmailPayload` | 1 |
| `password_reset_email` | `PasswordResetEmailOutboxHandler` | `PasswordResetEmailPayload` | 2 |
| `admin_password_reset_email` | `AdminPasswordResetEmailOutboxHandler` | `AdminPasswordResetEmailPayload` | 1 |
| `admin_password_changed_email` | `AdminPasswordChangedEmailOutboxHandler` | `AdminPasswordChangedEmailPayload` | 1 |
| `platform_manager_invite_email` | `PlatformManagerInviteEmailOutboxHandler` | `PlatformManagerInviteEmailPayload` | 1 |
| `employee_onboarding_invite_email` | `EmployeeOnboardingInviteEmailOutboxHandler` | `EmployeeOnboardingInviteEmailPayload` | 3 |
| `position_change_approval_request_email` | `PositionChangeApprovalRequestEmailOutboxHandler` | `PositionChangeApprovalRequestEmailPayload` | 2 |
| `invoice_email` | `InvoiceEmailOutboxHandler` | `InvoiceEmailPayload` | 1 |
| `leave_request_approved` | `LeaveApprovalEmailOutboxHandler` | `LeaveRequestApprovedPayload` | 4 |
| `leave_request_rejected` | `LeaveApprovalEmailOutboxHandler` | `LeaveRequestRejectedPayload` | 2 |
| `leave_request_cancelled` | `LeaveApprovalEmailOutboxHandler` | `LeaveRequestCancelledPayload` | 1 |
| `work_notification` | `WorkNotificationOutboxHandler` | `WorkNotificationPayload` | 8 |
| `notification_delivery` | `NotificationDeliveryOutboxHandler` | `NotificationDeliveryPayload` | 1 |
| `notification_tray_push` | `NotificationTrayPushOutboxHandler` | `NotificationTrayPushPayload` | 1 |
| `inbox_changed` | `InboxChangedOutboxHandler` | `InboxChangedPayload` | 2 |
| `calendar_event_invite_email` | `CalendarEventInviteEmailOutboxHandler` | `CalendarEventInviteEmailPayload` | 3 |
| `calendar_outlook_push` | `CalendarOutlookPushOutboxHandler` | `CalendarOutlookPushPayload` | 2 |
| `calendar_incident_evaluate` | `CalendarIncidentEvaluateOutboxHandler` | `CalendarIncidentEvaluatePayload` | 4 |
| `calendar_meeting_cancel` | `CalendarMeetingCancelOutboxHandler` | `CalendarMeetingCancelPayload` | 3 |
| `meeting_link_provision` | `MeetingLinkProvisionOutboxHandler` | `MeetingLinkProvisionPayload` | 1 |
| `position_created`, `position_updated`, `position_archived`, `position_restored` | `NoOpPositionOutboxHandler` (does nothing) | `PositionOutboxPayload` | 1 each |
| `employee_security_updated` | `NoOpEmployeeSecurityOutboxHandler` (does nothing) | anonymous object | 2 |

## Gaps found

1. **Nothing guards the mapping.** No test checks that every constant has exactly one registered handler. A new type without a registration only fails at run time: the message is retried about 8 times and then parked as failed. Two handlers for one type would stop all outbox processing.
2. **Two handler classes are never registered.** `NoOpLeaveApprovalSideEffectOutboxHandler` and `NoOpLeaveCancellationSideEffectOutboxHandler` exist but are not in DI. The second one claims `leave_request_cancelled`, which the real leave email handler already owns. Registering it by mistake would create the duplicate from point 1.
3. **A comment is out of date.** `OutboxMessageTypes` says no handler is registered for the four position types. No-op handlers are registered now.
4. **Five types are written and then thrown away.** The four position types and `employee_security_updated` are encrypted, stored and "handled" by a no-op. That is extra writes on every position change, password change and MFA change for no effect.
5. **Published rows are never deleted.** There is no purge for `outbox_messages`. The local table has 521 rows from five days of light use, all still stored.
6. **The live data only proves one type.** The local database holds only `inbox_changed` messages. The other 24 types are covered by the code check and unit tests, not by a real run in this check.

## Not checked

- A real send through the email provider for each email type.
- `WorkNotificationPayload.TemplateCode` values against the seeded notification templates. That is a second mapping (template code to template) and deserves its own check.
- Behaviour with two API instances claiming at the same time (covered by unit tests on the claim store, not run against two processes).

## How to change it

All six gaps are covered by one task: [Task 31: Outbox mapping guard and clean-up](<../../Improvements/person-4-deployment-and-architecture/task-31 Outbox mapping guard and clean-up.md>).

| Gap | How it is now | How to change it |
|---|---|---|
| 1 | No test checks type to handler | Add a test that fails when a type has zero or two registered handlers |
| 2 | Two handler classes exist but are not registered | Delete both classes |
| 3 | Comment on the four position types is wrong | Correct the comment |
| 4 | Five types are stored and then handled by a no-op | Stop writing them, or give them a real handler |
| 5 | Published rows stay for ever | Add a purge job with a retention period |
| 6 | Only `inbox_changed` was seen in real data | Run each type once on the UAT server and record the result |
