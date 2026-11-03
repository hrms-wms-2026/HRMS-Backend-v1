# Employee Detail Screen Redesign — Design

**Status:** Approved in conversation 2026-09-30 (Options A + C chosen). Identical copies live in `Hrms--Web-application---front-end---v1/docs/superpowers/specs/` and `HRMS-Backend-v1/docs/superpowers/specs/`.

**Reference designs:** `C:\onevoNew\final-scope\employee-detail-screen\` (Overview, Employment, Work & Activity, Personal).

**Supersedes nothing.** Builds on `2026-08-16-employee-detail-screen-{frontend,backend}-design.md` (the current `/detail`-backed screen, which stays in place).

## 1. Goal

Turn the employee detail screen into a tabbed, widget-based page where every data widget is fed by its own endpoint, so widgets can be filtered by period and fail independently. Answer honestly which widgets have a data foundation and which do not.

## 2. Scope of this spec vs. delivery

The full design covers Overview + Employment tabs (rollout steps 1-5 in §6). It is delivered as **separate plans, one per shippable step**:

- **Plan 1 (written):** shared plumbing + Employment tab (Work Network graph). Backend + frontend.
- **Plans 2-4 (not yet written):** Overview widgets. Each is planned only after its data sources are verified, because several depend on facts not yet checked.

## 3. Foundation audit (verified against the codebase 2026-09-29/30)

| Widget | Foundation |
|---|---|
| Header, Job & Org, Personal, Emergency, Payroll | Ready — `GET /employees/{id}/detail` |
| Employment History (positions) | Ready — `GET /employees/{id}/position-history` |
| Employment History (manager/status/probation events) | **Missing** — no event source; not shown until one exists |
| Checklists | Ready — `GET /employees/{id}/checklist-tasks` (Overview plan) |
| Work Network graph | Data ready: `ProjectMember(ProjectId, ObjectiveId, EmployeeId)`, `Objective.OwnerId`, `TaskAssignment(TaskId, EmployeeId)`, `TaskStatus.Category` ∈ {not_started, active, done}. New endpoint needed. |
| Attendance / Time Off / Approvals / Insights / Upcoming | Partial — per-employee variants of existing queries needed (Overview plans) |
| Over-break, location violations | Signals exist (`over_break`, `outside_work_location` in attendance/monitoring rules); only count location when the employee's location tracking is enabled |
| Recent Activity ("every action") | **No foundation.** `AuditLog` (Auth module) is written by one Work Management handler only; no task history table. Delivered as union-read now, events table later (Option C). |
| Work Insight Index, story points, Team Memberships | Removed/deferred (formula undefined / replaced by graph) |
| Work Arrangement: time zone, schedule | Deferred — no per-employee source verified. Card shows work mode, legal entity and primary location (from address) only. |

## 4. Interpretations and decisions

1. "Modules" = **Objectives**. Project membership is stored per (project, objective) in `ProjectMember`.
2. A project's **default objective** (`IsDefault`) is the project itself, not a module: it is never drawn as a module node. Membership in it still makes the project appear. Tasks in a default objective link straight to the project.
3. Nested objectives (`ParentObjectiveId`) are drawn flat: every non-default objective the employee owns, is a member of, or has an open task in is a module node linked to its project. No parent-child module links.
4. Graph task nodes are the employee's assigned tasks whose status category is `not_started` or `active`, capped at **60**; the response carries `hiddenTaskCount`.
5. Access: `employees:read` + the existing management-coverage visibility check (`EmployeeVisibilityScope`; `org:manage` = unrestricted). A caller who passes it sees that employee's project/module/task footprint, including Work Management items the caller is not a member of. This is a deliberate HR-coverage visibility decision, called out for reviewer sign-off.
6. Existing endpoints (`/detail`, `/position-history`) are **not modified**.
7. No `rxResource`/`httpResource` exists in the codebase; widgets follow the existing pattern (service returns `Observable`, component subscribes into signals).

## 5. Work Graph contract

`GET /api/v1/employees/{id}/work-graph` → `EmployeeWorkGraphResponse`:

```json
{
  "nodes": [
    { "id": "employee:{guid}", "kind": "employee", "label": "Saif Ahamed", "sublabel": "Watercraft Engineer" },
    { "id": "project:{guid}", "kind": "project", "label": "Website", "projectId": "guid" },
    { "id": "module:{guid}", "kind": "module", "label": "Checkout", "role": "owner|member|contributor", "projectId": "guid", "objectiveId": "guid" },
    { "id": "task:{guid}", "kind": "task", "label": "Fix cart total", "sublabel": "WEB-12", "status": "not_started|active", "projectId": "guid", "objectiveId": "guid", "taskId": "guid" }
  ],
  "links": [ { "source": "employee:..", "target": "project:..", "kind": "works_on|contains|owns|member_of|has_task" } ],
  "hiddenTaskCount": 0
}
```

`role: contributor` = the employee is neither owner nor member of the module but has an open task in it (no employee→module link is drawn).

## 6. Rollout

1. Shared plumbing + Employment tab — **Plan 1**
2. Overview: attendance + time-off widgets
3. Overview: work + approvals + insights widgets
4. Overview: upcoming + recent activity (union read; events table later behind the same endpoint)

Period plumbing (`from`/`to` parsing, 12-month cap, period store) is deferred to Plan 2 — nothing in Plan 1 consumes it.

## 7. Frontend structure (Plan 1)

- Tabs: **Employment**, **Personal** (Overview joins in Plan 2). Employment is default.
- Personal tab holds today's personal info, emergency contacts, and payroll (still gated on `employees:read:sensitive` via the response's `payroll` field).
- Header follows the new design: avatar, name, employee number, position, department • company, email; key facts (manager, employment type, work mode, hire date); existing Transfer/Promote/Offboarding popover (gated as today) and Resend invitation. No "Edit Employee" button (no destination exists). The existing "Attendance today" strip is kept.
- New: `WidgetCardComponent` (title, loading/error/empty/no-access states), `WorkNetworkGraphComponent` (`d3-force` layout, Angular-owned SVG), Employment Record and Work Arrangement cards.
- New dependency: `d3-force` (+ `@types/d3-force`). No chart library.

## 8. Risks

- Recent Activity will be partial until the events table exists (UI will say so).
- Graph exposes WM footprint to coverage viewers (§4.5).
- Heavy assignees are capped (§4.4).
