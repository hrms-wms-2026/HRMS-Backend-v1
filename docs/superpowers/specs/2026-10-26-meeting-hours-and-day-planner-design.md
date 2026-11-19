# Meeting Hours and Day Planner — Design

**Status:** draft for approval (2026-10-26)

**Builds on:**
- `2026-10-13-meeting-task-phase0-1-design.md`: B2, B3 (Office project), B12 (daily planner).
- `2026-10-18-meeting-task-phase2-design.md`: meetings as tasks.

**Parts:** this design has two independent parts, and each gets its own implementation plan. Part A ships first; Part B uses Part A's meeting hours.

## Why

The CEO model is that a meeting is work, so it uses the project time it happens in. Meetings that belong to no project must not eat into any project's budget. Each employee also plans their own day, and the company compares planned hours against actual hours.

Today none of this happens:
- Meeting tasks carry no hours.
- `ExcludeMeetingTasksFromModuleHourSummaries` deliberately removes meetings from the module hour summaries.
- No planner exists (B12 was deferred to "Phase 3").

## Part A — Meeting hours count against the module

### Decided (user)

- **Hours per occurrence = meeting duration × number of attendees** (person-hours).
- **Counted only after the meeting ends.** It is not based on attendance join times.
- **Every invited attendee who did not decline counts for the full duration.** This holds even if they joined late or left early (confirmed 2026-10-26).
- **Later, not now:** partial attendance (joining late or leaving early) will be flagged through the exception engine. It does not reduce the hours.
- **Team Progress:** meetings add to the **hours** columns only. They stay out of task **counts** (`task_count`, `unestimated_task_count`). The rest of the counts-only exclusion from `ExcludeMeetingTasksFromModuleHourSummaries` stays.
- A meeting with no project lives in the **Office** project. Its hours land on the Office project only and never on another project (B2, B9). This already holds today through `MeetingPlacementResolver`.

### My calls (change them if wrong)

- **Who counts as an attendee:**
  - Counted: the organizer plus every internal employee attendee whose response is not `rejected` when the occurrence is stamped. Stamping happens within an hour after it ends (amended 2026-10-26 after review: the hourly job reads the attendee list then, not as of the exact end time). That covers the series participants not removed for that occurrence, plus per-occurrence adds.
  - Not counted: external guests.
  - Example: 5 invited, 1 declined, 1 external guest, 1 hour → **3 h**.
- **Frozen at stamping:** RSVP or attendee changes after the occurrence has been stamped do not change its hours.
- **All-day meetings:** each attendee counts one 8-hour workday per calendar day, not 24 hours (added 2026-10-26 after review).
- **Cancelled occurrences count 0.** This includes those cancelled with their series.
- **Where the hours go:** when an occurrence ends, its meeting task gets `EstimatedHours = CompletedHours = person-hours`. Hours then flow through the same paths as a completed task:
  - the summary's `completed_hours`;
  - the module's and project's `CompletedHours` roll-up, using the same delta logic `MoveTaskStatusCommandHandler` uses.
  - Before the end, the task contributes 0 h.
- **When it is stamped:** the existing hourly meeting background pass (`Meetings:Background`) stamps occurrences whose `end_at` has passed. It must be idempotent: stamping twice changes nothing.
- **A virtual occurrence that was never materialized** is materialized at stamping time, so it gets a task to carry the hours.
- **Monitoring:** meetings have no clock-in (D12), so "clocked over allocated" / "clocked over estimate" are unchanged. The module-allocation view must include meeting hours, because it reads the summary or the completed hours.

### Constraints

- Forward migration only. The summary SQL `wm_refresh_module_hour_summary` must count meeting tasks' hours but not their counts. Update its tests: `ModuleHourSummaryMeetingExclusionIntegrationTests` currently asserts that hours are excluded, which flips for hours.
- No change to meeting visibility (the access tiers) or to the task lists' `ExcludeMeetings` filter.

## Part B — Day Planner (web only)

### Decided (user)

- **Web only.** Not in the tray app.
- **Today only.** An employee plans and edits only today's plan. Earlier days are read-only history. No planning ahead in this scope.
- **The employee edits their own plan.** Their **manager can view it** but does not approve it. No approval step.

### What it is (from B12 plus this conversation)

One screen for "my day". The timeline shows:
- today's meetings, automatically;
- **planned task blocks**: a task placed in a time slot, without changing its due date;
- tasks due today or overdue, as a list to drag into the timeline.

For each task and for the day, the screen shows **planned hours vs actual hours**.

### My calls (change them if wrong)

**Data:**
- New table `planned_task_blocks`: tenant, employee, task, `start_at`, `end_at`, `created_at` / `updated_at`.
- `start_at` and `end_at` must fall on today in the employee's own timezone (`display_timezone`, or the profile zone the calendar uses).
- Blocks for the same employee may not overlap each other. A block may overlap a meeting, and the screen marks it as a conflict.

**Which tasks can be planned:** tasks the employee is assigned to, in any project including Office. Not meeting tasks: meetings appear automatically.

**Actual hours:**
- For a task, actual = the overlap of today's `TaskClockingSession` rows for that employee and task with today. A session still open counts up to now.
- For a meeting, actual = Part A's hours share for that employee, which is the duration if they did not decline, once the meeting has ended.
- Day totals: planned = the sum of the blocks plus the meetings; actual = the sum of the actuals.
- The attendance clocked total for the day appears alongside it, for reference.

**Manager view:**
- A manager is anyone in the employee's reporting-line ancestor chain, the same rule as the D16 attendance view (`GetAncestorChainEmployeeIdsAsync`).
- The manager sees the employee's today and earlier days, read-only.
- Private meetings are masked as "Private meeting", the same rule as D17.
- Others get 404.

**Feature flag:** `Planner:Enabled`, off by default, like the meetings flags.

**API** (sketch, for the builder to finalize):
- `GET /api/v1/planner/me?date=` returns the day (blocks, meetings, due tasks, totals).
- `POST`, `PATCH`, `DELETE /api/v1/planner/me/blocks` work on today only.
- `GET /api/v1/planner/employees/{id}?date=` is the manager view.

**Web:**
- A "Planner" page in the left navigation: a timeline for today, a side list of due or overdue tasks to drag in, and planned vs actual per row plus a day total.
- The meeting form can be opened from the planner, because the shared form was meant for this (phase 0-1 spec §shared form).
- The manager reaches it from the employee profile or My Team, as a read-only "Today's plan" view.

### Non-goals

- Planning future days, recurring plans, templates.
- Approval of plans.
- The tray app.
- Google/Outlook busy overlay (B11), which stays a later item.
- Changing task estimates from the planner.

### Anti-goals (a failure even if it works)

- A plan that silently changes a task's due date or estimate.
- A manager seeing a private meeting's title.
- A block saved on any day other than today.

## Left to the builder

- Indexes, exact DTO shapes, page layout details, and drag-and-drop library choice. The frontend already uses CDK drag-drop on the board.
- Whether Part A stamping runs inside the existing generator job or as a sibling job, as long as it is hourly, leased (`job_leases`) and idempotent.
