# Planner create popover — Design

**Status:** draft for approval (2026-11-03)
**Repos:** frontend (all UI). Backend: one additive change for work item requests (see "Backend").
**Builds on:**
- `2026-10-27-planner-calendar-merge-design.md`: Planner = Calendar, blocks, Task queue.
- `2026-10-18-meeting-task-phase2-design.md`: a meeting is a work item.
- The Planner redesign (FE #135) and meetings from the work item form (FE #134).

**Mockup the user approved:** `planner-create-tabs.html`. It shows four tabs side by side; it is copied next to this document as `2026-11-03-planner-create-popover-mockup.html`.

## Why

Today a click or drag on an empty Planner time opens the calendar **event form**. To make a meeting the user must press "Make it a meeting" inside it, and that opens a second, separate modal. A work item can't be made from the Planner at all; it has to be made on Board/Backlog/Tree and then dragged in from the Task queue. Leave is made on the Leave pages only.

The user wants one quick create surface on the Planner, like a modern calendar: pick a time, then choose **what** to make from tabs, without leaving the Planner or juggling modals.

## Goals

1. A click on an empty slot, a drag over a time range, or the **New event** button opens one **create popover** with four tabs, in this order:
   **Event · Meeting · Work item · Time off**.
2. The time picked on the grid fills every tab, and stays when the user switches tabs. The title typed carries across tabs too.
3. Each tab holds only the most used fields. **More options** on each tab closes the popover and opens the existing full form for that kind, prefilled with what the popover had.
4. **Work item** creates the item **and** plans it on the caller's Planner at the picked time in one action ("Create & plan").
5. **Time off** submits a leave request for the picked time; while pending (and after approval) it shows on the caller's own Planner.

## Non-goals (this design)

- **A manager working on someone else's Planner.** Viewing a report's Planner as a grid, dragging time there, and sending a "work item request" the employee approves in the Inbox was discussed and is **deferred to the next design** (user decision, 2026-11-03). In this design every tab acts for the caller only. Work item assignee is fixed to **Me**.
- "Focus time" and "OOO" tabs (seen in the reference product). Not requested.
- AI or "@ for people / @@ for tasks" quick syntax in the title.
- Editing existing items. Clicking an existing event/meeting/block keeps today's behaviour (event form, meeting drawer, task form).
- Any change to approval rules, leave policy, meeting rules or planner rules.

## Anti-goals (would make this a failure even if it works)

- A plain task or event that silently gets the meeting category or behaves like a meeting. A meeting is only ever made through the meeting engine.
- A work item created but **not planned without the user being told** (or planned but not created).
- Leave submitted without the policy check, or a leave type that needs a document going through without one.
- Two different create paths drifting apart: the popover must call the same APIs, with the same validation and error messages, as the full forms.

## The popover

**Only while the Planner is on** *(my call, builder check Q2)*. With `Planner:Enabled` off, `/calendar` is the plain calendar and keeps today's behaviour exactly: slot click/drag and New event open the event form with "Make it a meeting". The popover, the Work item tab and the leave layer belong to the Planner and need its data (editable days, blocks). If the Planner range fails to load, slots stay as today (event form) until it loads.

**Opens from:** an empty-slot click (start = that hour, 1h by default as today), a drag over a range (start and end = the range), or **New event** (today's next half hour, 1h). Week and Day views only, on days the user may plan (today + 28 days); on read-only past days a slot click keeps today's behaviour. Month/Agenda views keep today's behaviour.

**Layout** (per the mockup): a tab bar with the four tabs and a close (✕) button; the tab's fields; a footer with **More options** (secondary) and the tab's primary action. Closing, Escape, or a click outside closes it without saving. It anchors near the picked slot on desktop; on a narrow screen it opens as a bottom sheet.

**Tab availability:**
| Tab | Shown when |
|---|---|
| Event | always (calendar access, as today) |
| Meeting | meetings can be written (`MeetingsAvailabilityService.canCreate()`), same gate as the meeting option in the work item form |
| Work item | the Planner is on (`Planner:Enabled`) — the popover only exists on the Planner page |
| Time off | the user can make their own leave request (the leave module is on for the tenant and the user has the permission the Leave pages use for "New request") |

A hidden tab is simply absent; the remaining tabs keep their order. Default tab: **Event**.

**Carried across tabs:** title, start, end, all-day. Each tab keeps its own other fields while the popover is open.

**More options keeps everything** (review, 2026-11-03): opening the full form must carry **every field the popover supports** for that tab, not just title and time. Nothing the user typed is lost.

| Tab | Carried into the full form |
|---|---|
| Event | title, start, end, all-day, timezone, repeat, location, description |
| Meeting | title, start, end, all-day, timezone, repeat, project/module, platform (Teams/Zoom), participants, agenda |
| Work item | title, project/module, category, priority, estimate, due date, and the plan slot (start/end) used after create |
| Time off | leave type, from, to, reason |

Where a full form can't take a field today (for example the event form has only `prefillStart`/`prefillEnd`, the leave form only `initialStartDate`/`initialEndDate`), that form gets the extra prefill inputs. Each mapping has a test.

### 1 · Event (a personal time block)

Fields: title; date, start → end, duration; **All day · Timezone · Repeat**; **Location**; Description.
- Location is new on the **event form** too (the backend already stores and returns `Location`; the form never showed it).
- A note: "People or a video call? → **Meeting** tab". Clicking it switches to the Meeting tab with the title and time kept. This replaces today's "Make it a meeting" hand-off for new events.
- Primary: **Save** (same create call as the event form today).
- More options → the existing calendar **event form**, prefilled.

### 2 · Meeting (a work item; hours count)

Fields: title; date/time/duration; All day · Timezone · Repeat; **Project / Module** (default **Office** when left empty, as the meeting form does); **Add Teams / Zoom**; **participants** with the **availability warnings** (leave, holiday, outside hours, busy) the meeting form already shows; agenda.
- Primary: **Create meeting** (same meeting create call; same rules, e.g. start must be in the future, per FE #138).
- After create, the new meeting's drawer opens, as after the meeting form today.
- If the module needs approval, the result is a pending request, reported the same way the meeting form reports it.
- More options → the existing **New meeting** form, prefilled (title, description/agenda, time, project/module, participants, platform). Private, custom repeat rule, bulk invite, location live there.

### 3 · Work item ("Create & plan")

Fields: title; **Plan** (date, start → end, e.g. "9:00–11:00 · 2h"); **Project / Module**; **Type** (the category, with its kind icon/colour from `KIND_META`; the system "meeting" category is **not** offered here); Priority; **Estimate (total effort)**; Due date; **Assignee: Me** (read-only in this design).

**Estimate is not the planned time** (review, 2026-11-03):
- *Estimate* is the work item's total effort. *Plan* is the time booked for it in this one slot. They are separate values on separate records (the task's `EstimatedHours` vs the block's start/end).
- For a new item the Estimate field **starts** at the slot length (2h slot → 2h), because that is the best guess the user has given. The user may change it (e.g. 8h effort, 2h planned today).
- After creation they never drive each other: moving, resizing or removing the block never changes the estimate, and editing the estimate never changes any block. (This is already the Planner rule: "a block never touches the task".)
- The labels make the difference visible: "Plan 9:00–11:00 · 2h" and "Estimate (total effort)". A plan longer than the estimate is allowed.

- Primary: **Create & plan**:
  1. Create the work item with the same API and rules as **Create work item**. The create request carries no assignee (`CreateTaskRequest` has none), so, exactly like the full form, the caller is then assigned with the existing assign call. Only a direct create (201) reaches this step, and a direct create means the caller is at or above the module, so the assign is allowed.
  2. If it was **created** (201) and assigned: create a Planner block for it at the picked start/end on the caller's Planner. (A block needs the task assigned to the caller.)
  3. If it was **sent for approval** (202, the caller doesn't own the module): nothing is created or planned yet. The request **carries the caller as the assignee and the picked slot** (user decision, 2026-11-03, option (a)). When the module owner approves it:
     - the work item is created **and assigned to the requester** (today an approved create is assigned to nobody, so it never reached the requester's Task queue);
     - a Planner block is created for the requester at the picked slot, under the normal planner rules **at approval time**. If the slot can no longer be used (the day has passed, it overlaps a block made since, beyond 28 days, the Planner is off), the item is still created and assigned, just not planned; it waits in the requester's Task queue. A failed block never fails the approval.
     - The requester already gets the existing "approved"/"rejected" notification; nothing new is added there.
     - The approver sees, in the request, who it will be assigned to and the planned slot.
     The popover tells the user: "Sent to <owner> for approval. Once approved it's yours and planned for Tue 10:00–12:00."
  4. If the block fails after the item was created (e.g. it now overlaps another block, or the day became read-only): keep the item, and tell the user plainly that the item was created but not planned, with the reason. Never roll the item back silently.
- Before submit, the popover warns (doesn't block) when the picked time overlaps one of the caller's existing blocks, since the block would be refused.

**Create & plan safeguards** (review, 2026-11-03). The action is two calls, so:
- **One submission at a time.** The primary button is disabled and shows progress from the first click until the outcome is known. Double-clicks, Enter-repeat and a second click while waiting send nothing.
- **The item is created at most once.** Once the create call has returned the new item's id, nothing in the popover re-sends the create; every later step works on that id.
- **Created, not planned → Retry.** If step 4 happens, the popover stays open in a "Created <ID>, not planned" state with the reason and two actions: **Retry planning** (re-sends only the block call for the same item and time) and **Pick another time** (opens the existing block picker for that item). Closing it leaves the item in the Task queue, unplanned.
- **Lost response on create.** If the create call fails without a usable answer (network drop, timeout), the popover does **not** retry the create on its own, because the item may already exist. It says so and points to the Task queue; the user decides.
- **Tests** cover: success (item + block); 202 pending (no block now; on approval: assigned to the requester and planned at the slot; slot no longer usable → assigned, not planned, approval still succeeds); block refused after create (item kept, Retry planning sends only the block call, then succeeds); double-click / repeated submit (one create call); create network failure (no automatic second create); Retry planning after a second failure (still one item).

- More options → the existing **Create work item** form, prefilled with every field above. The form itself is not given a new mode *(my call, builder check Q3)*: after it closes, the popover flow plans the **first** item it created directly, at the picked time, as in steps 2–4 with the same safeguards, **only if that item ended up assigned to the caller**. If the user changed the assignee, or the form sent a request, nothing is planned and the user is told why ("Created, not planned: assigned to Priya" / "Sent for approval").

### 4 · Time off (leave request)

Fields: **Leave type** (with remaining hours, as the leave form shows), **From / To** (date + time, from the picked range), Reason; the **policy check** preview (hours, balance after, who approves) the leave form already runs.
- Primary: **Submit request** (same leave request API and validation).
- If the chosen leave type needs a **supporting document** for this length, **Submit** is disabled with a note pointing to **More options** (the full form has the upload).
- More options → the existing **New leave request** form, prefilled (type, from, to, reason).
- **On the Planner** (review, 2026-11-03): the caller's own leave requests that are **pending** or **approved** show on the Planner over their time. Cancelled and rejected ones don't show. Nobody else's leave is shown here.
  - **Pending and approved must look different at a glance:** *Pending leave* is outlined/hatched and lighter, labelled "Pending leave"; *Leave* (approved) is solid, labelled "Leave". Colour is not the only cue (the label differs too).
  - **Data:** `GET /api/v1/leave/requests/my` with the visible range (`fromDate`/`toDate`), keeping pending and approved.
  - **Keeping it current after a decision:** the backend does **not** push leave decisions in real time (the Leave feature publishes no `inboxChanged`). So the Planner re-reads the caller's leave: on load, on a range change, right after a Time off submit, when the browser tab becomes visible or focused again, and every 60 seconds while the Planner is visible. An approval therefore shows within a minute, or at once when the user comes back to the tab. *(My call; a real-time push would need a small backend change, see "Backend".)*

## Backend

**One additive backend change** (user decision, 2026-11-03, option (a)): a work item create that goes for approval can carry **"assign to the requester"** and an optional **planned slot** (start, end), both optional and absent for every existing caller.
- `TaskCreateApplier`, on approval: create the task as today; if the flag is set, assign the requester (the approval stands in for the module-owner assign check; the requester must still be an active member of the module, as the applier already checks); if a slot is given, create the requester's Planner block for it with the normal planner rules, and if that fails keep the task and skip the block.
- The approval detail/snapshot shows the requested assignee and slot to the approver.
- Only the popover's Work item tab sends these fields in this design. The full **Create work item** form and its "Create request" on Board/Backlog/Tree keep today's behaviour (a separate fix if wanted).
- Its own backend PR, stacked on the backend Planner stack (#200 → #204 → #207), since the 28-day planning rule lives there. *(My call.)*

Everything else already exists. Checked in the repository (2026-11-03):

| Need | Where it already is |
|---|---|
| Event location | `CalendarEvent.Location` (entity) and `Location` in the create/update/response contracts (`CalendarContracts.cs`) |
| Create a work item, 201 created / 202 sent for approval | `POST /objectives/{objectiveId}/tasks` (FE `TaskApiService.createTask`) |
| Plan a block | `POST /api/v1/planner/me/blocks` (`PlannerController`); refuses a task not assigned to the caller (`task_not_plannable`), an overlap (409), a past day, beyond 28 days |
| Meeting create + availability warnings | meeting create API used by the meeting form; `POST /api/v1/calendar/check-conflicts` |
| Leave request: create, preview (policy), document upload | `POST /leave/requests`, `/leave/requests/preview`, `/leave/requests/documents` |
| The caller's leave for the Planner | `GET /leave/requests/my?status&fromDate&toDate` (`leave:read-own`) |

**Not available, and not required by this design:** a real-time push when a leave request is decided. If the team later wants the Planner to update instantly, the smallest change is for the leave decision handlers to publish the existing `inboxChanged` event to the requester; the Planner would then re-read on that event. That would be its own backend PR.

If the builder still finds a gap, the smallest additive backend change is acceptable and must be called out in its own PR.

## Constraints

- Reuse the existing forms' validation and API services; the popover is a second, smaller face over the same calls.
- Keep the existing full forms working unchanged for every other entry point (Board/Backlog/Tree, Leave pages, editing).
- Frontend rules of the repo: standalone components, signals, OnPush, Vitest specs, warning-free build (`scripts/check-no-warnings.mjs`).
- The FE Planner code this builds on is in the open stack (#132 → #135 → #139). The work branches from the top of that stack, `feature/category-kind-explicit`, and becomes the next stacked PR.
- FE #138 (a new meeting starts at the next half hour; a past start is caught) is not in that stack. The new branch **merges `fix/meeting-form-start-in-future` in first** *(my call, builder check Q4)*, so the Meeting tab reuses that rule instead of duplicating it. #138 already merges cleanly with #134's embedded meeting form.

## Implementation acceptance criteria (review, 2026-11-03)

The design is approved; the build is accepted only when these hold, each with a test:

1. **Approval after the slot has passed:** the item is created and assigned to the requester and waits in their Task queue; no block; the approval succeeds.
2. **Lost create response:** no automatic second create from the popover (see "Create & plan safeguards"). *Future, not in this build:* a backend idempotency key on work item create would remove the duplicate risk completely; worth its own change.
3. **More options → full work item form:** the task to plan is taken from the form's `created` output (it emits the new task's id on a direct create, and `''` when the request went for approval). Closing the form (`closed`) never counts as a create. The builder confirms in code that `created` fires only after the assignee is saved, and plans only on a non-empty id.
4. **Verified in code before relying on them**, with tests: the my-call points Q2 (Planner off → today's behaviour), Q3 (no new mode on the full form) and Q4 (FE #138 merged in first); that the approval applier may assign the requester although the requester is not the module owner; and the branch bases (FE on `feature/category-kind-explicit` + #138; BE on the #200 → #204 → #207 stack).

## Left to the builder

- Component split (one popover shell + one component per tab, or otherwise), and how prefill reaches the full forms.
- Exact anchoring/positioning and the narrow-screen sheet.
- How "Pending leave" is drawn on the grid (band vs block), within the existing Planner visual language.
- Copy for messages, keeping the meanings above.
