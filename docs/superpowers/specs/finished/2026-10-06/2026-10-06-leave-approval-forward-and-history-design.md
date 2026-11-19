# Leave approvals: forward to my manager + approval history

Date: 2026-10-06
Status: approved (user, 2026-10-06)

## Problem

On **Time Off → Pending Approvals** an approver can only Approve, Reject or Request info.

- There is no way to hand a request up to their own manager when it is above their authority
  (e.g. Mathusanth Kumaran wants Nadesh Coomaraswamy to decide Tharmi's leave).
- Once a request is decided it disappears from the page; the approver has no history of what they did.

## 1. Forward to my manager (hand over)

Decision: **hand over**. The forwarding approver steps out; the new approver alone decides.

### Who receives it

The forward target is resolved with the existing `ILeaveApproverResolver.ResolveAsync`, using the
**current approver** as the subject (not the requester):

1. Authority route (position coverage → department coverage → reporting line, each candidate must hold
   `leave:approve`) — i.e. whoever would approve the current approver's own leave.
2. HR fallback (holds `leave:manage` + `leave:approve`, lowest employee number).
3. An active delegation on that person applies, as for normal routing.

The target is rejected (no forward possible) when it is the requester, or someone who already has an
approver row on this request. When there is no valid target, the forward option is not offered.

### Rules

- Only the approver whose turn it is (`IsActionable`) on a non-final request may forward.
- Self-check: same `LoadAsync(requireActionableApprover: true)` path as approve/reject.
- Optional note, max 2000 characters.

### Effect

- Current approver row: `status = forwarded`, `comment = note`, `decided_at = now`.
- New approver row: target employee, same `sequence_order` as the forwarded row, `status = pending`.
  Approval-mode evaluation is unchanged: completion only looks at remaining `pending` rows, so
  `any_one`, `all_must_approve` and `in_order` keep working.
- The request status stays `pending`. The target receives the existing
  `leave_request_next_approval_required` notification.
- No schema migration: `status` is a free `varchar(40)` with no check constraint.

### API

- `POST /api/v1/leave/requests/{id}/forward` — `[RequirePermission("leave:approve")]`,
  body `{ "note": string | null }`, returns `LeaveApprovalDecisionResponse`.
  403 when not the actionable approver, 409 when final or when no forward target exists.
- `LeaveApprovalDetailResponse` gains `ForwardTo { employeeId, name } | null`, filled only when
  `CanDecide` is true and a valid target exists.

## 2. Approval history

- `GET /api/v1/leave/requests/approval-history?fromDate&toDate` — `[RequirePermission("leave:approve")]`.
- Returns the latest 100 requests where the current employee has an approver row with status
  `approved`, `rejected`, `forwarded` or `information_requested`, newest action first.
- Row: request id, employee name, leave type, start/end, total hours, **my action**, **my action date**
  (`decided_at`, falling back to the request's `updated_at`), my comment, current request status.
- Opening a history row uses the existing approver detail endpoint (`/approval`), which already allows
  anyone with an approver row; actions show only when `CanDecide`.

## 3. Frontend

- Pending Approvals gets two tabs: **Waiting** (existing list) and **History** (new table).
- Detail sheet: when `forwardTo` is present, a **"Send to {name}"** action next to Approve / Reject /
  Request more information, with an optional note and a confirm button.
- Approver trail in the detail sheet shows `forwarded` rows with their note.

## Testing

- Unit: forward success (row statuses, new row, notification), forward refused for non-actionable,
  final request, no target, target already on request, target is requester; history query handler.
- Unit: detail `ForwardTo` filled only when the caller can decide.
- Integration: approver forwards → target sees it in pending approvals and approves → request approved;
  forwarding approver sees it in history as `forwarded`.
- Frontend specs: forward action in the sheet, History tab loads and renders rows.

## 4. Change a decision (added 2026-10-06, approved by user)

Decision: an approver may flip their own approve/reject **until the leave starts** (`now < startAt`).
After that the existing Cancel flow handles partial days and attendance.

- **Who:** only the approver whose decision made the request final — the `approved` row matching
  `request.approvedBy`, or the `rejected` row on a rejected request. HR keeps Cancel on All Requests.
- **Approved → Rejected:** reason required. Request `rejected`, `approvedBy/approvedAt` cleared, paid hours
  returned (`usedHours -= paidHours`), `adjustment` balance audit, rejected outbox event + employee notification.
- **Rejected → Approved:** balance re-checked (`remaining >= paidHours`, else 409). Under modes other than
  `any_one`, approvers skipped by the rejection return to `pending` and the approval mode is re-run: if it
  completes, request `approved` with a `deduction` audit, approved outbox event + notification; otherwise the
  request goes back to `pending` and the hours are reserved again.
- Approver comment records the change: `Changed from approved: <reason>` / `Changed from rejected[: comment]`.
- **API:** `POST /api/v1/leave/requests/{id}/change-decision` (`leave:approve`), body
  `{ decision: "approve" | "reject", comment }`. 403 not the decider, 409 started / same decision / no final
  decision / short balance. Detail response gains `canChangeDecision`.
- **UI:** in the detail popup (opened from History), a "Change decision" section with
  "Change to Rejected" (reason) or "Change to Approved" (optional comment).
