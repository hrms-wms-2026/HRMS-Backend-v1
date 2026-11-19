# Task 31: Outbox mapping guard and clean-up

| | |
|---|---|
| Priority | 31 of 34 (lower number = build first). Added 2026-11-15 |
| Owner folder | person-4-deployment-and-architecture |
| Size | 1 person, 1 day |
| Area | Architecture, Code quality |
| Depends on | nothing |

## What we found

The outbox mapping was checked on 2026-11-15 (`docs/reports/outbox/2026-11-15-outbox-mapping-check.md`). All 25 message types map to one registered handler with the right payload, 75 unit tests pass, and the local table has no failed or stuck rows. The mapping is correct today.

It is correct by care, not by design:

- No test checks that every constant in `OutboxMessageTypes` has exactly one registered `IOutboxMessageHandler`. A missing registration fails only at run time, after about 8 retries. Two handlers for one type make `ToDictionary` throw in `OutboxProcessor.ProcessBatchAsync`, which stops all outbox processing.
- `NoOpLeaveApprovalSideEffectOutboxHandler` and `NoOpLeaveCancellationSideEffectOutboxHandler` exist but are not registered. The second claims `leave_request_cancelled`, which `LeaveApprovalEmailOutboxHandler` already owns.
- The comment above the position types in `OutboxMessageTypes` says no handler is registered. No-op handlers are registered now.
- Five types (`position_created`, `position_updated`, `position_archived`, `position_restored`, `employee_security_updated`) are encrypted, stored and then handled by a no-op.
- Nothing deletes published rows from `outbox_messages`.

## What to do

1. Add a unit test that builds the real service collection and asserts: every `OutboxMessageTypes` constant has exactly one registered handler, and every registered handler's `Type` is one of the constants.
2. Add a test per real handler that serialises its payload class with the writer's settings and feeds it to `HandleAsync` with fake services, so a renamed property is caught.
3. Make the processor safe against a duplicate: build the dictionary with a clear error that names both handlers, and fail start-up instead of failing every batch.
4. Delete the two unregistered no-op leave handlers after confirming nothing references them.
5. Fix the stale comment.
6. For the five no-op types decide with the team: stop enqueuing them until a real consumer exists, or keep them and write down who will consume them. If they stay, do not encrypt and store a payload nobody reads.
7. Add a purge for rows in status `published` older than an agreed age (suggest 14 days), in small batches. Keep `failed` rows until someone has looked at them. This belongs with task 30.
8. Add a metric and an alert for rows in `failed` and for the oldest `pending` row (ties in with task 17).

## Done when

- Removing one handler registration makes a test fail.
- Registering a second handler for the same type fails at start-up with a message that names both.
- Published rows older than the agreed age are removed and the purge is logged with a row count.
