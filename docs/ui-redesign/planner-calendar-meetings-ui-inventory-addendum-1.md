# Addendum 1 — Planner, Calendar and Meetings UI inventory

> **Date:** 2026-10-29
>
> **Adds to:** `planner-calendar-meetings-ui-inventory.md`. Read that file first. This addendum only adds; it changes nothing there.

---

## A1. Planning from the backlog: what works today

The word "backlog" covers two different things, and the Planner treats them differently.

| Kind | In the Task queue? | Can it be planned from the Planner? |
|---|---|---|
| **Assigned backlog**: tasks assigned to the user that have no due date, or are due after this week | Yes, in the **"Assigned backlog"** group | Yes. Drag the card or use 📅 Plan. |
| **Project backlog**: tasks in Work → Backlog that are **not assigned to the user**, either unassigned or assigned to others | **No** | **No.** The backend refuses: `400 task_not_plannable`, "Only a task assigned to you can be planned." (`PlannerService.CreateBlockAsync`) |

### The user's path today for a project-backlog task

1. Leave the Planner and go to **Work → Backlog**.
2. Assign the task to themselves.
3. Go back to the **Planner**. The task now shows in the Task queue: under "Assigned backlog", or under Overdue, Due today or Due this week according to its due date.
4. Drag it onto the calendar.

---

## A2. Gap for the redesign

- **The gap:** a user cannot look through the project backlog from the Planner when they plan their day, for example in the morning when deciding what to pick up. They have to go to Work → Backlog and come back.
- **Possible direction** (for the redesign to decide; not built):
  - **Where:** a **"Project backlog"** section in or beside the Task queue.
  - **What it lists:** unassigned tasks in the projects the user is a member of.
  - **How to take one:** drag a card onto the calendar, or press **"Take & plan"**. In one step this
    1. assigns the task to the user, and
    2. creates the planned block.

### Decisions this needs before it can be built

| Question | Why it matters |
|---|---|
| **Who may self-assign?** Any project member, module members only, or module owners only? | Today assignment goes through the task's assignee rules. Self-assign from the Planner must follow the same rules. |
| **Is approval needed?** | Some teams may want a lead to approve before a task is taken. |
| **Which tasks count as "project backlog"?** Unassigned only, or also tasks in no sprint? Exclude subtasks? Exclude checklist tasks? | Sets the list's size and its noise. |
| **How many to show, and in what order?** By priority, due date or module? | The panel is narrow, and the Task queue already shows 5 per group. |
| **Backend:** a list endpoint for "unassigned tasks I may take", and a combined "assign + plan" call (or two calls in a row) | The Planner API today only lists the user's own assigned tasks (`ListFocusTasksAsync`). |
