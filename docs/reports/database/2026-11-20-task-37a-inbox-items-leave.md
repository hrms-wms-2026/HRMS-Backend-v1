# Shared inbox header, leave first (task 37a, 2026-11-20)

Task 37 (`docs/Improvements/person-4-deployment-and-architecture/task-37 Shared approval request header table.md`),
split in two as the task allows. **37a (this report): the table, leave requests writing it, leave badge counts read
from it, backfill and a nightly check.** 37b (below) is the other modules.

## Decisions

| Question | Decision | Why |
|---|---|---|
| Which modules first | Leave only | Attendance alerts (most rows locally) have no fixed recipient: who sees an alert is worked out when the inbox is read, from the viewer's approval scope (reporting lines plus HR coverage), and there is no "reviewers of employee X" lookup. Stored recipients would go stale on every reporting-line or coverage change. Exceptions route the same way. Both wait for their owners (37b) |
| Several approvers | Child table `inbox_item_assignees`, one row per person in the chain | Keeps the task's unique key on `(tenant_id, source, kind, subject_id)`; each person has their own state |
| Base branch | The task chain (task 31), not task 00a | 00a is pushed but not merged. 37a adds a small `IInboxHeaderCountSource` with the same `InboxSourceCounts` shape 00a gives every provider, so after both merge, leave's `CountAsync` is this query |

## What was built

**Tables** (migration `20261120141304_AddInboxItems`, both under the RLS Tenant rule and in `RlsTableRegistry`):

| Table | Columns | Indexes and constraints |
|---|---|---|
| `inbox_items` | id, tenant_id, source, kind, subject_id, category, subject_employee_id, requested_by_employee_id, status, title, summary, created_at, updated_at, decided_at, decided_by_employee_id | unique `(tenant_id, source, kind, subject_id)`; sent: `(tenant_id, requested_by_employee_id, status, updated_at DESC)`; checks on status (pending, approved, rejected, cancelled, resolved) and category (request, alert, invitation) |
| `inbox_item_assignees` | id, tenant_id, inbox_entry_id (cascade), employee_id, source, kind, status, sequence_order, updated_at | unique `(inbox_entry_id, employee_id)`; received: `(tenant_id, employee_id, status, updated_at DESC)` including source, kind, inbox_entry_id; check on status (pending = must act now, waiting, approved, rejected, skipped, cancelled) |

The task's single `assigned_to_employee_id` column became the assignees table: the "received" index lives there, and
source and kind are copied onto it so the badge count needs no join.

**Write path.** `LeaveInboxEntrySync` builds the header from the leave approval state. Who must act uses
`LeavePendingApprovals.IsActionable`, the rule the inbox and the pending-approvals screen already use, so they cannot
disagree. It is staged on the unit of work before the command's own `SaveChanges`, so it commits in the same
transaction. Covered write paths:

| Path | Where |
|---|---|
| Submit (including no-approval auto-finalise) | `SubmitLeaveRequestCommandHandler` |
| Approve, reject, change decision (both ways), hand over, finalise no-approval | `LeaveApprovalDecisionService` (6 saves) |
| Cancel (employee and HR, full and partial) | `CancelLeaveRequestCommandHandler` |
| Undo a decision from the inbox | `LeaveDecisionReverter` |
| Approver-created backdated leave (from an attendance alert) | `CreateApproverBackdatedLeaveCommandHandler` |

A sweep for every assignment of a leave request status and every approver row add or remove found no other path.
Seeders write leave rows directly; the nightly check fills those in.

**Read path.** `GET /inbox/counts` now counts leave with one grouped query on `inbox_item_assignees` (still only with
`leave:approve`, checked at read time). The router no longer builds leave's inbox rows for the counts. The other
sources are unchanged.

**Backfill and nightly check.** `InboxEntryConsistencyJob` (daily, leased, `inbox-entry-consistency`) compares every
leave request's expected header with the stored one on status and the assignee chain, logs each difference, and with
`Inbox:EntryCheck:Repair` (default true) writes the expected header. That is the backfill, and it corrects rows written
outside the commands. Headers whose request no longer exists are logged and removed. Each tenant is saved on its own.

## Done when (task)

| Requirement | Status |
|---|---|
| Inbox list and counts for the converted modules are single indexed queries on `inbox_items` | **Counts: yes** for leave. **List: not converted.** The frontend no longer calls `GET /inbox` (its inbox is the chat list); the list's leave part still loads each pending request. Paging the list in the database is task 00c |
| Created, decided or reverted request updates its header in the same transaction (test) | Yes: `LeaveInboxEntryIntegrationTests` (SQL Server, app login under RLS): create, decide, revert; a rolled-back change leaves the header unchanged; another tenant sees nothing |
| Consistency check reports zero differences on the dev database | Yes. First run on the local database: 245 leave requests, 245 missing, 245 repaired. Second run: "245 leave requests, no differences". 29 assignees must act now, equal to the 29 pending approver rows on pending requests |

## Tests

| Suite | Result |
|---|---|
| Unit (Release) | 8,172 passed. New: `LeaveInboxEntrySyncTests` (in-order and any-one chains, decided, cancelled, no mode, count source permission) and a router test that leave counts skip the leave rows |
| Architecture | 713 passed (RLS coverage of the two new tables) |
| `LeaveInboxEntryIntegrationTests` | 3 passed |
| `dotnet ef migrations has-pending-model-changes` | none |

## 37b: what is left

1. **Attendance alerts and exceptions.** Their owners decide how a recipient is defined: store the reviewers at write
   time (with a repair when reporting lines or coverage change), or keep read-time routing and convert only the
   counts with a set-based query.
2. **The other request tables** (attendance corrections, device, location and work-area change requests, offboarding
   bypass, seat increase, project invitations, tray authorisations, work-management approvals). All empty locally;
   each follows the leave pattern: a `Build` from the module state and a stage call before each save.
3. **The work-management approver chain** fits the assignees table as leave does.
4. **The list.** When 00c pages the list in the database, read "received" and "sent" from the header indexes.

## Not covered

- The live `/inbox/counts` response was not called with a signed-in user; parity was checked in the database (29 =
  29) and by the shared `IsActionable` rule.
- A request whose employee or leave type was deleted after submission keeps its header; the old count dropped it.
  The nightly check skips such requests rather than flagging them.
- The migration is applied to the local Docker database only. Apply it to UAT through the deploy pipeline; the first
  nightly run there does the backfill.
