# Inbox Standard + HR / Leave / Attendance / Monitoring / Calendar Integration — Plan (Phase 2)

- **Date:** 2026-10-13
- **Builds on:** `../2026-10-13-unified-inbox.md` (Phase 1, implemented: Inbox router + WM provider + SignalR group chat).
- **Repos:** `HRMS-Backend-v1` (BE), `Hrms--Web-application---front-end---v1` (FE)
- **Source documents (from the user, 2026-10-13):** `TIME-OFF-MODULE.md`, `INVESTIGATION-INBOX-HANDOFF.md`, `CALENDAR-INVESTIGATION-INBOX.md`. Their content is summarised here. Where this plan and those docs disagree, **this plan wins** (the decisions below were made with the user).
- **Files in this plan:**
  - `README.md`: decisions, the Inbox Standard, architecture, order.
  - `backend.md`: BE parts S1–S5, L1, A1–A2, X1, E1–E2, C0–C3, W1, BZ.
  - `frontend.md`: FE parts FS1–FS3, FL, FA, FX, FE, FC, FW, FZ.

---

## 0. Goal

Every request, approval, alert and escalation in the product appears in the **one Inbox**, and it **behaves the same**: the same list, filters, detail card, steps/history, actions, editable approval, 30-minute revert and investigation group chat. This holds whichever module owns the item.

**Who decides "who approves / who sees"** stays with the module that owns the item:
- **Work Management** items follow the **WM hierarchy** (Module tree, creator position, `IWorkApprovalEngine`).
- **HR-side** items (Leave, Attendance, Monitoring exceptions, Employee alerts, Calendar cases) follow the **HR hierarchy** (`IEmployeeAuthorityResolver` and the resolvers built on it: `ILeaveApproverResolver`, `IExceptionScopeResolver`).

The Inbox never re-implements those rules. It routes to them.

## 1. Decisions (confirmed with the user, 2026-10-13)

| # | Decision |
|---|---|
| P1 | **All sources in this plan:** WM project requests + WM alert escalations (exist; aligned to the standard), **Leave requests**, **Attendance requests** (correction, location change, work-mode = `WorkAreaChange`, device change), **Monitoring exception cases**, **Employee attendance alerts** (absent/late/early/short-hours/off-schedule/location/idle, with the 21→20 resolution model), and **Calendar** (Phase 0 notification fixes + the meeting-exception engine). |
| P2 | **30-minute revert for every reversible kind.** The person who decided can undo within 30 minutes. The module's effects are rolled back (leave balance, attendance record, work mode, location, device registration, exception status, alert resolution, converted leave). A revert is **blocked** when something now depends on the decision (§3.4). |
| P3 | **Editable approval** (the approver changes values, then approves): WM (exists), **attendance correction** times/break, **work-mode change** target mode, and **leave StartAt/EndAt** (shorten or shift). An edited leave **re-runs the full `LeaveRequestSubmissionEvaluator`** (balance, overlap, blackout, policy) for the edited range before approving. Leave type is not editable. Location and device requests are not editable. |
| P4 | **Old approver screens are removed and their routes redirect into the Inbox**, pre-filtered. Deep links (`?requestId=`, `?alertId=`) keep working. The affected screens: Time Off → Pending Approvals, the Attendance team-approvals view (correction / work-area / location / device), Attendance → Alerts. **Employee self-service screens stay** (my requests, my balances, wizards, cancel). |
| P5 | **Employee alerts** are resolved by the **attendance approver** (`IEmployeeAuthorityResolver`, `attendance:approve`, the same as corrections). There are **3 outcomes**: **Excused** (the count drops), **Confirmed violation** (stays counted, marked reviewed), and **Convert to leave** (absence only; an approver-only backdated leave that bypasses `AllowBackdatedRequests` but runs every other leave rule). |
| P6 | **Escalation is a per-type policy.** Calendar cases: reporting manager → manager's manager → *log only*, never HR (calendar doc D8). Existing monitoring exceptions keep today's routing: manager → HR fallback, escalate to HR. WM alerts: creator-position holder → next ancestor Module owner → root ⇒ log only. Every policy comes from **one registry**. |
| P7 | **Chat moderation is standard.** Base participants can't be removed and can't leave. An added member can be removed by any base participant or by whoever added them, and can leave at any time. Authors can edit/delete their own messages at any time; the chat shows "edited" or a "message deleted" placeholder. **Every revision and delete is kept in an audit table**: the chat is investigation evidence. |
| P8 | **The Inbox rail entry is for every employee.** It is user-oriented: everyone sends requests and gets added to chats, and the providers decide what each person sees. The current WM-modules-only gate on the nav item is removed. |
| P9 | **Conversation features added now:** remove member, leave, edit/delete messages, **chat attachments**. |
| P10 | The group chat's main purpose is **investigation**. Alert and case items therefore include the **subject employee** as a base participant: their justification is a chat message (calendar doc D6). Privacy rule: the subject sees meeting/attendance facts only, never app usage or activity samples. |

**Defaults chosen by Claude.** These were open in the source docs; override any of them before execution:
- **D-a:** Today's *live* employee-list badges (`not_clocked_in`, `over_break` today, …) stay as list badges only. Inbox alert items are for **completed days** (yesterday and earlier) plus notification-backed alerts (location, idle). The list row also gets an **"N open alerts"** chip from the new alert table.
- **D-b:** `camera_verification_skipped` raises an **`IdentityAnomaly` case**, at most one open case per employee per company day (the same dedup as face mismatch), so it can be investigated.
- **D-c:** Device-change requests with a null `LegalEntityId` are scoped through the **employee's** legal entity.
- **D-d:** Leave **request-info / respond-info** stay Leave workflow actions, exposed as Inbox actions. Each one **also posts its text into the item's conversation**, tagged `info_request` or `info_response`, so the chat stays the single thread. Old `leave_request_info_messages` are not migrated; they still show in Steps.
- **D-e:** Calendar open decisions:
  - resolver = `IEmployeeAuthorityResolver` with purpose `ExceptionAlertReview`;
  - retention = platform standard, no new purge job, except unattached incidents, which are purged after 90 days;
  - a **separate** My Team source `calendar_meeting_exceptions`;
  - employee note = the chat (P10), not a new column;
  - permission = the existing `exceptions:view` / ManagerReview.
- **D-f:** The Leave approver **delegate** management UI (Pending Approvals → delegate modal) moves to an Inbox toolbar menu, "Delegate my approvals", shown when the caller holds `leave:approve`.
- **D-g:** **Bulk actions** come from the standard. Any kind that declares the `bulk` capability gets multi-select approve/reject/resolve: Leave (existing bulk commands), attendance requests, alert resolution, exception resolve.

**Out of scope:** tenant-configurable calendar thresholds, email digests, mobile push, AI summarisation of chats, payroll effects of "confirmed violation".

---

## 2. The Inbox Standard (what every source must provide)

Every `(source, kind)` registers an **`InboxKindDescriptor`**: what the kind is and what it can do. The FE renders entirely from the descriptor and the item data. There is no per-module page code.

```csharp
public sealed record InboxKindDescriptor(
    string Source, string Kind, string Label, string Category,          // request | alert
    InboxCapabilities Capabilities,                                       // flags below
    string? DetailRenderer = null);                                       // FE renderer key, e.g. "leave-request", "attendance-evidence"

[Flags] public enum InboxCapabilities {
    None = 0, Chat = 1, EditBeforeApprove = 2, Revert = 4, Bulk = 8, Escalate = 16,
    Resolve = 32, Cancel = 64, Forward = 128, RequestInfo = 256, SubjectParticipates = 512 }
```

| Standard | Contract | Notes |
|---|---|---|
| **List / filters** | `IInboxSourceProvider.ListAsync` + `GetFilterFieldsAsync` (exist) | Every item sets `ContextType`/`ContextId`: WM uses `project`; HR-side items use **`legal_entity`**, plus the facets `employee`, `department`, `kind`. |
| **Detail + Steps** | `GetDetailAsync` returns `Steps`; the **standard steps builder** merges the module steps + **decision journal** + conversation system events | One timeline per item = "request steps explanation". |
| **History** | New `inbox_decision_journal` table, written by every provider on every decision | Feeds Steps, the Inbox **History** tab, and Revert. |
| **Actions** | `AvailableActions` + `ExecuteActionAsync`, mapped to the **module's existing commands** | The Inbox never duplicates a business rule. |
| **Editable approval** | `InboxChange.Editable=true` + `Kind` (`text\|date\|datetime\|time\|hours\|select\|employee`) + `Options?`; `approve` accepts `editedPayloadJson` | The FE builds a generic edit form from the change set. |
| **Revert (30 min)** | `InboxRevertWindow.Minutes = 30` (the WM `ApprovalRevertWindow` delegates to it) + one `IInboxDecisionReverter` per `(source, kind)` | Standard rules in §3.4. |
| **Chat** | `IConversationSubjectResolver` (exists) + new `CanViewAsync` | Base participants = requester/subject + current deciders. Scoped viewers (HR, escalation targets) join on first open as `viewer`. |
| **Escalation** | `IInboxEscalationPolicy` registry | Manual `escalate` action + SLA sweep. |
| **Realtime** | Every module decision path enqueues `InboxChanged` through the existing `InboxChangedOutboxHandler` | Counts and lists refresh live for everyone affected. |
| **Notifications** | Each module keeps its own templates. A new item's notification has `RelatedEntityType = inbox:{source}:{kind}` so the bell opens the Inbox item | Bell navigation: one rule for all. |
| **Deep link** | `/inbox/{source}/{kind}/{id}` | Old routes redirect here. |

### 2.1 Kind catalogue

| Source | Kind | Category | Decider (resolver) | Capabilities |
|---|---|---|---|---|
| `work` | `approval`, `invitation` | request | WM approval engine (position) | Chat, EditBeforeApprove, Revert, Cancel |
| `work` | `monitor_alert` | alert | WM creator position → ancestors | Chat, Escalate, SubjectParticipates |
| `leave` | `request` | request | `ILeaveApproverResolver` + delegates + approval mode | Chat, EditBeforeApprove, Revert, Bulk, Cancel, Forward, RequestInfo |
| `attendance` | `correction` | request | `IEmployeeAuthorityResolver` `attendance:approve` | Chat, EditBeforeApprove, Revert, Bulk, Cancel |
| `attendance` | `work_mode_change` (`WorkAreaChangeRequest`) | request | same | Chat, EditBeforeApprove, Revert, Bulk, Cancel |
| `attendance` | `location_change` | request | same | Chat, Revert, Bulk, Cancel |
| `attendance` | `device_change` | request | same (LE via the employee, D-c) | Chat, Revert, Bulk (no Cancel; there is no cancel command) |
| `attendance` | `alert` (absent/late/early/short/off-schedule/location/idle) | alert | `IEmployeeAuthorityResolver` `attendance:approve` | Chat, Resolve, Revert, Bulk, SubjectParticipates |
| `monitoring` | `exception_case` | alert | `IExceptionScopeResolver` | Chat, Resolve, Escalate, Revert, Bulk, SubjectParticipates |
| `calendar` | `meeting_exception` | alert | `IEmployeeAuthorityResolver` `ExceptionAlertReview` | Chat, Resolve, Escalate, Revert, SubjectParticipates |

---

## 3. Architecture

```
            ┌──────────────── Inbox API (exists) ────────────────┐
FE /inbox ─▶│ IInboxRouter → IInboxSourceProvider[]              │
            │   work · leave · attendance · monitoring · calendar│
            │ InboxKindRegistry (descriptors)                    │
            │ IInboxDecisionJournal ─▶ inbox_decision_journal    │
            │ IInboxDecisionReverter[(source,kind)]              │
            │ IInboxEscalationPolicy[(source,kind|type)]         │
            └───────┬────────────────────────────────────────────┘
                    │ calls existing module commands only
   WM engine ◀──────┼──────▶ Leave decision service / Attendance workflows /
                    │        Exception commands / AlertResolution / Calendar cases
Conversations (exists + v2: remove/leave/edit/delete/attachments/viewers)
SignalR /hubs/inbox (exists): messageCreated|Updated|Deleted, participantAdded|Removed, inboxChanged
```

### 3.1 Provider placement
Each provider lives **inside its module**, next to the commands it calls. This is the same placement as `Features/WorkManagement/Inbox`:
- `Features/Leave/Inbox/LeaveInboxSourceProvider.cs`
- `Features/TimeAttendance/Inbox/AttendanceInboxSourceProvider.cs`: request kinds + the `alert` kind
- `Features/Monitoring/Exceptions/Inbox/ExceptionInboxSourceProvider.cs`
- `Features/Calendar/Inbox/CalendarInboxSourceProvider.cs`

Each provider also has its `…ConversationSubjectResolver`, `…DecisionReverter`s, and its `InboxKindDescriptor` registrations.

### 3.2 Reuse the My Team action-source pattern
`ITeamActionSource` (`Dashboard/Team`) already knows each module's pending items for an approver. Providers **share their query code with those sources**: extract the repository query each source uses into a method both can call. Don't build a second aggregator. The My Team widget keeps working unchanged. Its deep links move to `/inbox/...`.

### 3.3 Decision journal (`inbox_decision_journal`)

| column | notes |
|---|---|
| id, tenant_id | RLS covered |
| source, kind, item_id | |
| action | `approve`, `reject`, `forward`, `request_info`, `resolve:{outcome}`, `acknowledge`, `escalate`, `cancel`, `revert` |
| actor_employee_id | |
| before_json, after_json | the minimal snapshot the reverter needs (e.g. the attendance record times before the correction) |
| comment | |
| decided_at | |
| reverted_by_journal_id | set on the original row when reverted |

- Index: `(tenant, source, kind, item_id, decided_at)` and `(tenant, actor, decided_at desc)` (History tab).
- Written **in the same transaction** as the module change. The provider calls the module command and then `journal.Record(...)` inside `IUnitOfWork.ExecuteInTransactionAsync`. **Do not** add journal writes inside teammate commands. The single exception: a command whose before-state is otherwise lost (correction apply) gets a small extension that returns the before-snapshot.

### 3.4 Revert rules (standard)
`IInboxDecisionReverter.CanRevertAsync` / `RevertAsync`. Common rules, enforced by a shared base class:
1. Only the journal row's actor, only within 30 minutes of `decided_at`, only once, and only if this is the item's **latest** decision.
2. The module-specific **dependency check** must pass:

| Kind | Revert effect | Blocked when |
|---|---|---|
| leave approve | Used → Pending hours, approver rows restored (approval mode respected), status pending, audit `adjustment` | leave already started; entitlement recalculated since; a later partial cancel |
| leave reject | status pending, Pending hours re-held | the balance can no longer cover it (409 with message) |
| leave forward / request-info | the added row removed, previous approver row back to pending | the forward target has already acted |
| correction approve | the attendance record restored from `before_json`, worked/late minutes recomputed | a newer correction was applied on the same record/day |
| work-mode approve | the previous work mode restored for that date | the date is already past and attendance exists under the new mode |
| location approve | the previous work location restored; `applied` cleared | another location change was applied since |
| device approve | the old registration re-activated, the new one deactivated | the new device has sent check-in or activity since the approval |
| any reject (attendance) | back to pending | — |
| exception acknowledge/resolve/escalate | the previous status + timestamps restored; an escalation notification is followed by a "withdrawn" notification | — |
| alert resolve | the resolution row deleted (count returns); convert-to-leave cancels the created leave | the converted leave was already changed |
| calendar case | same as exception | — |
| WM approval | the existing WM revert (delegated) | the existing WM rules |

### 3.5 Escalation policy registry
```csharp
public interface IInboxEscalationPolicy {
    bool AppliesTo(string source, string kindOrType);
    TimeSpan? Sla { get; }                                   // null = manual only
    Task<EscalationTarget> NextAsync(Guid tenantId, EscalationContext ctx, CancellationToken ct); // Employee(s) | LogOnly
}
```
- `ReportingLineThenHrPolicy`: today's monitoring behaviour, moved out of `ExceptionAlertRouter` unchanged.
- `ReportingLineThenLogPolicy`: calendar. It goes to the current holder's reporting manager; when nobody is above, it is `LogOnly` and HR is never notified.
- `WmHierarchyPolicy`: WM monitor alerts. It goes to the next ancestor Module owner; at the root it is `LogOnly`.
- **One sweep:** `InboxEscalationSweepJob` (hourly) asks each policy with an SLA for its overdue open items and escalates them. The existing 3-day sweep inside `ExceptionDetectionJob` **moves here**. Same threshold, same result for existing types; a test proves it.

### 3.6 Identity & visibility
- Everything keys on **EmployeeId**. The bell uses UserId; reuse the existing mapping.
- **Visibility = can-act or is-subject.** A provider's list must use **exactly the same resolver call** as its action authorisation, so nobody sees an item they can't act on (handoff §7.1). The only exception is `Direction=info` items: the subject employee's own cases/alerts and sent requests.
- **Batched scope.** HR-side providers call `IEmployeeAuthorityResolver.ResolveApprovalInboxScopeAsync` once per legal entity the caller belongs to, never per item.

---

## 4. Order & gates

```
Platform:   S1 → S2 → S3 → S4 → S5
Sources:    L1 (leave)  A1 → A2 (attendance)  X1 (exceptions)  E1 → E2 (alerts)  C0 → C1 → C2 → C3 (calendar)  W1 (WM align)
            all need S1–S3; E2 needs A1; C2 needs X1 + S4
Cleanup:    BZ (backend audit)
Frontend:   FS1 → FS2 → FS3, then FL, FA, FX, FE, FC, FW (each after its BE part), FZ last
```
- **Gate after every part:** BE `dotnet build` + full unit + architecture suites green; FE `ng build` + full vitest green. Counts go in the part report.
- **Migrations:** one per part that changes the schema, each with an **RLS coverage** migration for new tenant-owned tables. Diff `ApplicationDbContextModelSnapshot.cs` after each one.
- **Coordinate with teammate code:** this plan edits Leave / TimeAttendance / Monitoring / Calendar / CoreHr / Dashboard. The user approved this for this plan. Keep the edits minimal and local, and list every non-Inbox file touched in each part report.
