# Team Planner and Plan Requests: Design

**Date:** 2026-11-03
**Status:** Draft for approval
**Builds on:**
- Planner = Calendar (2026-10-27)
- Planner create popover (`2026-11-03-planner-create-popover-design.md`)

**Decided by the product owner:**
- A reporting manager can see their team's planner.
- Someone who may assign tasks to an employee can drag time on that employee's planner. The drag becomes a **request**, and the **employee approves it** from the Inbox.
- The view does **not** follow today's People → Employee profile → "Today's plan" journey. It lives on the **My Team dashboard** and as a **Team option inside the Planner tab**.

Everything else here is the designer's call. Each one is marked *(call)* so it can be challenged.

---

## 1. Why

Managers plan work for their people, but the Planner only shows *my* day.

Today a manager can see one person's single day: People → profile → Today's plan tab. That is slow and not where planning happens, and nothing lets a manager put time on someone's plan.

The result is that work gets assigned with no time attached, and nobody can see who is overloaded before the day starts.

**Success looks like this:**
- A manager opens the Planner, switches to **My team**, and sees within seconds who is full, who is free and who is on leave this week.
- They drag a slot on one person's row or week, pick the work item, and send it.
- The person gets an Inbox item, accepts it, and the block appears on their Planner. The manager sees the outcome.

## 2. Goals / non-goals / anti-goals

**Goals**
1. A **team view** of the Planner: every team member's planned time against their capacity, plus leave and meetings, for a day or a week.
2. A **person view**: one team member's week in the same grid as my own Planner, read-only.
3. **Plan requests**: drag on a team member's planner, choose a work item, and send. The employee accepts or declines in the Inbox (or on their own Planner). Accepting assigns the item if needed and creates the block.
4. Two entry points: the **Planner tab** (Me / My team switch) and a **My Team dashboard widget**.

**Non-goals (v1)**
- A manager moving, resizing or deleting an employee's blocks directly. The plan stays the employee's.
- People outside the caller's reporting line, even when they share a module.
- Capacity forecasting, auto-scheduling, or "find a free slot for the team".
- Changing the People → Employee profile → Today's plan tab. It stays as it is.
- Requests for meetings or time off. Meetings already invite people, and time off is the employee's own.

**Anti-goals (a failure even if it "works")**
- An employee's plan changes without the employee saying yes.
- A manager sees private calendar details (titles of private events) through this view.
- Approving a request silently overrides the employee's own planner rules (overlap, past day, 28-day horizon).
- A manager can put work on someone they could not assign that work to.

## 3. Who sees whom, and who can request

**Team** *(call)*
- The caller's **direct reports**, using the existing reporting line (`IEmployeeRepository.ListDirectReportEmployeeIdsAsync`).
- A report who leads people shows a "View team" drill-down to their own reports, and so on down the line.
- This matches the existing view rule: the planner day read already allows anyone in the employee's **ancestor chain** (`ListAncestorChainEmployeeIdsAsync`).

**View**
- Allowed for the employee themself and anyone above them in the reporting line. Same rule as `GET /planner/employees/{id}` today.
- Private calendar events show as **Busy**, with no title (existing `maskPrivate`).

**Request**
The caller must pass **both** checks:
1. The employee is in the caller's reporting line (the view rule).
2. The caller may assign the chosen work item: they are an effective manager of the item's module (`IMilestoneMembershipCoordinator.IsEffectiveManagerAsync`). This is the same rule as `AssignTaskCommandHandler`.

If the caller manages no module the employee could be given work in, the drag shows "You can view this plan but not add work to it" *(call)*.

**The Team option** appears only when the caller has at least one direct report. The My Team widget follows the existing My Team capability flags, using a new `CanViewTeamPlan` flag.

## 4. The views (frontend)

### 4.1 Planner tab: Me / My team switch
- The Planner header gets a segmented switch: **Me · My team**. The choice is remembered per browser (localStorage) *(call)*.
- **My team, Day** (default) *(approved 2026-11-03, "TeamDay" artboard on the Team Week View Ideas canvas)*:
  - **One column per person**, hours top → bottom: the same grid as my own Planner, so dragging works the same way.
  - Each column header shows name, role and today's load bar ("6h / 8h", in the week heatmap's colours).
  - Each column shows:
    - their planned blocks (work item title, colour by project);
    - meetings;
    - Busy (private, no title);
    - leave (pending and approved), filling the hours;
    - my waiting requests (dashed);
    - their off-work hours, hatched.
  - Times are drawn in **my** zone. A person in another zone shows it under their name (e.g. "Dubai · 1h 30m behind").
  - With more than ~7 people, the time column stays fixed and the people columns scroll sideways.
- **My team, Week** *(approved 2026-11-03, "A · Heatmap" artboard)*:
  - One row per person, one **coloured load cell** per day. The cell shows hours and %, the colour runs from not planned through under 50 %, 50–89 % and full to over capacity, and leave is yellow.
  - A "1 req" marker shows on days with my pending request.
  - Each row ends with the week total.
  - Header chips show how many people are over capacity, under-planned and on leave. A legend sits underneath.
  - No individual blocks. Click a cell to open that person's day.
- **Person view**: click a name to open their week in the normal week grid, read-only, with a header such as "Ravi's plan · Back to team".
- Pending requests I sent show on their grid as **dashed ghost blocks** labelled "Waiting for Ravi". Clicking one gives **Cancel request**.

### 4.2 My Team dashboard widget: "Team plan today"
- A compact list: avatar, name, today's load bar, plus an "On leave" or "Not planned" chip.
- A header link **Open team planner** goes to the Planner tab in My team mode, on today.
- Clicking a person opens their person view on today.
- Sizes follow the existing My Team widget grid (2×2 compact, larger shows more rows).

### 4.3 Plan request popover (drag on someone else's planner)
Dragging a range on a team member's day or person view opens a popover modelled on the create popover, with **one tab**:

- **Work item**: search the work items I can assign, filtered to modules I manage. Show the ones already assigned to this person first. Then:
  - **Plan**: the dragged time, shown as "Tue 09:00–11:00 · 2h" in *their* zone.
  - **Note** (optional, e.g. "Please pick this up before the demo").
  - **Send request**.
- **New work item** inside the popover *(call)*: offered only where I can create directly (module owner). It creates the item now, unassigned, and the request carries it. Members who would need approval to create pick an existing item instead. This avoids a request chained to another approval.
- **Warnings before sending** (advisory, never blocking): the slot overlaps their planned blocks, meetings or leave; the day is beyond their 28-day horizon; or the day is already over capacity.
- **Hard blocks** (the button is disabled, with the reason):
  - the day is in the past for them;
  - all day;
  - I cannot assign the item;
  - they already have a pending request from me for the same item and an overlapping time.

### 4.4 Employee side
- **Inbox** (Work source, new kind **Plan request**). The item shows:
  - who asked, the item (with project and module) and the slot in my zone;
  - the note;
  - a live conflict line ("Overlaps your 10:00 block 'API review'").

  Actions:
  - **Accept**: assigns the item to me if it isn't already, then plans the block.
  - **Decline**: optional reason.
- **My Planner**: pending requests to me show as dashed ghost blocks with Accept / Decline on click. This is the same request, so acting in either place updates both.
- If Accept fails a planner rule (overlap, past day, horizon), the error is shown, the request **stays pending**, and the employee can move their own block and try again, or decline.

### 4.5 Requester side
- My **Inbox → Sent** shows the request and its outcome: Accepted, Declined with the reason, Cancelled or Expired.
- The ghost block on their planner turns into their real block, or disappears.

## 5. Backend

### 5.1 New entity: `PlannerBlockRequest` *(call: its own entity, not a `WorkApprovalRequest`)*
The work approval engine routes approvers **up the module tree**, with an HR fallback. A plan request has a **fixed approver: the employee**. Module invitations already work the same way: the invitee accepts in the Inbox through `WorkInboxSourceProvider`. A separate entity keeps the engine's routing intact.

**Fields:**
- `Id`, `TenantId`
- `RequestedByEmployeeId`, `EmployeeId`, `TaskId`
- `StartAt`, `EndAt` (UTC)
- `Note`
- `Status`: `pending | accepted | declined | cancelled | expired`
- `DecidedAt`, `DeclineReason`
- `PlannedBlockId` (once accepted)
- `CreatedAt`, `xmin` (concurrency)

**Rules:**
- One **pending** request per (requester, employee, task, overlapping time). A second one returns 409 `plan_request_duplicate`.
- Requests whose `StartAt` has passed become `expired`. This happens in a sweep, and is also checked on read and decide.

### 5.2 Endpoints *(call: under `api/v1/planner`)*

| Method | Path | Purpose |
|---|---|---|
| GET | `team?date=` | Day view: my direct reports (or `?managerId=` for drill-down, view rule enforced) with blocks, meetings (masked), leave, planned/working minutes, each in their own zone |
| GET | `team/range?from=&to=` | Week view: per person, per day load + leave (no blocks) |
| GET | `employees/{id}/range?from=&to=` | Person view: the same shape as `me/range`, read-only (`editable=false`), with private items masked |
| GET | `employees/{id}/assignable-tasks?q=` | Work items the caller may assign to this employee (modules the caller manages), assigned-to-them first |
| POST | `employees/{id}/requests` | Send a plan request `{ taskId, startAt, endAt, note }` |
| DELETE | `requests/{id}` | Requester cancels while pending |
| POST | `requests/{id}/accept` | Employee accepts: assign if needed, then `IPlannerBlockWriter.PlanForAsync` for the employee, in one transaction |
| POST | `requests/{id}/decline` | Employee declines `{ reason? }` |
| GET | `me/requests?from=&to=` | Pending requests to me, for ghost blocks on my Planner |

**Reuse:**
- Accept uses the **existing** planner rules through `IPlannerBlockWriter.PlanForAsync` (built for the create popover). The employee's own rules always apply.
- Assign uses the same membership upsert as `TaskCreateApplier`.

### 5.3 Inbox
- `WorkInboxSourceProvider` gains kind `plan_request`:
  - **List**: received (pending to me) and sent.
  - **Detail**: the item, slot, note and conflict line.
  - **Actions**: `accept`, `decline` (received) and `cancel` (sent).
- Every state change publishes `inboxChanged` to both people, so the badge and ghost blocks update in real time.

### 5.4 Capability
`MyTeamCapabilitiesResponse` gains `CanViewTeamPlan`: Planner enabled and the caller has at least one direct report. This is a cheap existence probe, per the My Team spec §7.3.

## 6. Edge cases (expected behaviour)

| Case | Behaviour |
|---|---|
| The employee leaves the reporting line or the module before deciding | Accept re-checks both. If either fails, the result is "This request can no longer be accepted", and the request becomes `cancelled` |
| The task is completed or deleted before deciding | The request becomes `cancelled`, and both sides see the reason |
| The employee takes leave over the slot after the request | Accept still works only if planner rules allow it. The conflict line shows the leave |
| Different time zones | The popover shows the slot in the *employee's* zone, and the Inbox shows it in the reader's zone |
| The manager is also the module owner and the employee is unassigned | Accept assigns (`AssignedById` = requester) and plans |
| The same person is reached through two reporting lines | They are shown once |
| Planner disabled (feature flag) | The Team switch, the widget and the endpoints are all off, matching the existing planner flag behaviour |

## 7. Phasing *(call)*
1. **Phase A, Team view (read-only):**
   - BE: `team`, `team/range`, `employees/{id}/range`, and the `CanViewTeamPlan` capability.
   - FE: the Me / My team switch, the team day and week views, the person view, and the dashboard widget.
   - Delivers value alone.
2. **Phase B, Plan requests:**
   - BE: the entity and migration, the request endpoints, the inbox kind and realtime events.
   - FE: the request popover, ghost blocks on both sides, and the Inbox detail and actions.

Each phase ships with its own PRs (BE + FE), stacked on the create popover PRs.

## 8. Left to the builder
- The exact visual design of rows, load bars and ghost blocks. Reuse the hour grid and the leave band styles.
- Paging or virtualisation when a manager has many reports *(suggest: >25 rows → virtual scroll)*.
- Sweep cadence for expiring requests (suggest: hourly, plus on-read checks).
- Notification email for a new plan request. Off in v1; Inbox and realtime only *(call)*.
