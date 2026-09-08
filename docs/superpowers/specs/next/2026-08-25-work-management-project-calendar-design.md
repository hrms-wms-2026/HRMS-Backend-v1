# Work Management: Project Calendar (backend) — design

**Status:** next. **Depends on:** cascading Objective ownership
(`2026-08-21-work-management-cascading-objective-ownership-design.md`) — this feature reuses that
machinery, it does not reimplement it.

## 1. What this is

A project-wide calendar view: every Objective ("module") in a Project rendered as a start/end date bar on
a timeline. Users can drag a bar's edges to adjust an Objective's dates (this reuses the **existing**
Objective edit path — no new write endpoint for dates). Users can also group several Objectives into a
colored "Event" purely for visual correlation on the calendar; grouping does not change any dates.

Two calendar types were discussed with the user. **Only the project calendar (this spec) is in scope.** A
personal per-user calendar is explicitly deferred — the schema below must not need to change to add it
later, but nothing is built for it now.

## 2. Terminology

- "Module" (user's word) = **Objective** (`ONEVO.Domain.Features.WorkManagement.Objectives.Entities.Objective`).
  Objectives form a tree via `ParentObjectiveId`; a top-level Objective is also called a "Milestone" in the
  UI/domain (see `MilestoneMembershipCoordinator`).
- "Parent objective members can access child module" = the existing **cascading ownership** rule: rights on
  a child Objective are inherited from any ancestor Objective's `OwnerId` or active `ProjectMember` row.
  Already implemented — see §4.

## 3. Data model (new tables)

Two new tables, no changes to `Objective` or `Project`. An Objective's calendar color is **derived**, never
stored on the Objective itself.

```
CalendarEvent
  Id                Guid PK
  TenantId          Guid
  ProjectId         Guid FK -> Project
  Name              string, required, e.g. "Q3 Launch"
  Color             string, required, hex (#RRGGBB) — same free-text-hex convention as Project.Color
  Status            string, "active" | "archived"  (matches the string-status convention used by
                     ObjectiveChangeRequestStatuses / MembershipSource elsewhere in this module — do not
                     use a C# enum, this codebase's WM tables use string status columns)
  CreatedById       Guid (Employee)
  CreatedAt         DateTimeOffset
  ArchivedById      Guid? (Employee), null while active
  ArchivedAt        DateTimeOffset?, null while active

CalendarEventObjective   (join table)
  Id                Guid PK
  CalendarEventId   Guid FK -> CalendarEvent
  ObjectiveId       Guid FK -> Objective
  AddedAt           DateTimeOffset
```

**Invariant enforced at the application layer (not a DB constraint — this codebase's join tables don't use
partial unique indexes elsewhere, e.g. `ProjectMember` doesn't either):** an Objective may belong to at
most one **active** event's membership at a time. Before adding an Objective to a new/existing active
event, check whether it's already a member of a different active event and reject (409) if so — a bar can
only show one color. Membership rows under an **archived** event are left in place (history), so the same
Objective can freely join a new active event later without conflicting with its old archived membership
rows.

**Migration:** two new tables via `dotnet ef migrations add AddCalendarEvents`, following the shape of the
most recent WM migrations (`20260823172054_AddTaskCategories.cs` is the closest precedent — a new
tenant-scoped entity table plus a join/FK). **Do not apply this migration** — see the hard rules in the
execution prompt.

## 4. Read: project calendar endpoint

New `CalendarController`, `GET /api/v1/work/projects/{projectId:guid}/calendar`, `[RequirePermission
("projects:access")]`.

**This handler is a near-copy of `GetMyProjectMilestonesQueryHandler`
(`Objectives/Queries/GetMyProjectMilestones/GetMyProjectMilestonesQueryHandler.cs`) with one deliberate
difference: it must NOT filter.** That handler's `relevant` list only includes Objectives the caller has
some relationship to; the calendar must return **every** Objective in the project (full visibility is the
whole point — "parent objective members ... full modules show, but only accessible modules can be
adjusted"). Reuse:
- `IObjectiveRepository.GetAllByProjectIdAsync` for the full tree.
- `IProjectMemberRepository.ListForEmployeeInProjectAsync` for the caller's membership rows.
- The exact same in-memory `IsEffectiveManager` cascade walk (do **not** call
  `MilestoneMembershipCoordinator.IsEffectiveManagerAsync` per-objective here — that hits the DB per call;
  `GetMyProjectMilestonesQueryHandler` already proves the in-memory walk against a pre-loaded project tree
  is the right pattern for a whole-project listing).

Additionally join in each Objective's active `CalendarEventObjective`/`CalendarEvent` row (if any) to
attach `calendarEventId`/`calendarEventColor` to the response.

Response DTO (`ProjectCalendarItemResponse`, new — this is a new shape, not a reuse of
`MyProjectMilestoneResponse`, because it needs the event fields and is unfiltered):
```
ObjectiveId, ProjectId, ParentObjectiveId, Title, StartDate, EndDate, IsActive, IsAchieved,
CanEdit (bool = IsEffectiveManager),
CalendarEventId (Guid?), CalendarEventColor (string?)
```

`CanEdit` is `false` (never editable via drag) when: the caller is not an effective manager, OR
`IsAchieved` is true, OR the Objective `IsDefault` — mirroring `EditObjectiveCommandHandler`'s own guards
exactly, so the frontend's gray/disabled state never promises an edit the backend would reject anyway.

## 5. Write: adjusting dates via drag — no new endpoint

Dragging a bar calls the **existing** `PUT /api/v1/work/objectives/{id}` (`EditObjectiveCommand`)
immediately per drag (user's explicit choice — see design discussion). Nothing new needed backend-side.
Two behaviors the frontend must handle, already implemented by `EditObjectiveCommandHandler` — **do not
add new validation, it's already there**:

1. **Parent-containment.** `ObjectiveParentConstraintChecker.Conflicts` rejects (routes to
   `ObjectiveChangeRequest` approval instead of applying) any new date range outside the parent's
   `[StartDate, EndDate]`, unless the caller is the Objective's own `CreatedById`. The response's
   `Applied` flag (`ObjectiveEditOutcomeResponse.Applied`) tells the caller whether the drag actually took
   effect or went pending — the frontend must branch on this, not assume success from a 200.
2. `EditObjectiveCommand` requires `Title`, `Description`, `AllocatedHours` in the body alongside the
   dates — it's a full update, not a date-only patch. The frontend must already have the current
   Objective's full field set in memory (from the §4 read) and echo the unchanged fields back on every
   drag-save call.

No changes to `EditObjectiveCommandHandler` are in scope for this feature — it already does exactly what's
needed.

## 6. Write: CalendarEvent CRUD

Same `CalendarController` as §4, all under `[RequirePermission("projects:access")]`. Grouping objectives
into an Event is **non-destructive** (no dates change), so — unlike editing an Objective's own dates — it
does **not** require `IsEffectiveManager` on every member Objective. Any caller with project access may
create/edit an Event and choose which visible Objectives it colors.

- `POST /api/v1/work/projects/{projectId:guid}/calendar-events` — body `{Name, Color, ObjectiveIds[]}`.
  Validate every `ObjectiveId` belongs to `projectId` (404/400 otherwise) and isn't already an active
  member of a different event (409, per §3's invariant — return which Objective(s) conflicted so the
  frontend can tell the user).
- `PATCH /api/v1/work/calendar-events/{id:guid}` — body `{Name?, Color?, ObjectiveIds?}`. Replacing
  `ObjectiveIds` diffs against current active membership (add new rows, mark removed ones — don't delete
  rows, matches this module's soft-removal convention seen in `ProjectMember.RemovedAt`, though here it's
  simpler: membership rows are scoped to one event and get superseded, not soft-removed within the same
  event).
- `POST /api/v1/work/calendar-events/{id:guid}/close` — sets `Status = archived`, `ArchivedById`,
  `ArchivedAt`. No body. This is the user's chosen "archive, keep history" behavior — the record and its
  membership rows stay queryable, just excluded from the §4 response's active-color join and from any
  "active events" list endpoint.
- Command/handler/validator shape: mirror `CreateObjective`/`EditObjective` (Commands/DTOs/Mappers folders
  under a new `CalendarEvents` feature folder), same CQRS layering as the rest of this module.

## 7. Testing

- Handler tests for `GetProjectCalendarQueryHandler`: returns every Objective (including ones the caller
  has no membership on), `CanEdit` correctly reflects the cascade + achieved/default guards, event color
  join is correct, cross-project/cross-tenant exclusion.
- Handler tests for Create/Patch/Close CalendarEvent: the one-active-event-per-Objective conflict check,
  archived events excluded from active color joins, cross-project Objective rejected.
- No new tests needed for the drag-save path — `EditObjectiveCommandHandlerTests` already covers the
  containment/pending-approval behavior this feature relies on unchanged.
- Repository tests for any new `ICalendarEventRepository` methods, following this module's existing
  `Ef*RepositoryTests` pattern.

## 8. Explicit non-goals

- Personal (per-user) calendar — deferred, not built.
- WorkTask-level bars — `WorkTask` has no `StartDate` today; only Objectives appear on the calendar.
- Any change to `EditObjectiveCommandHandler`, `ObjectiveParentConstraintChecker`, or
  `MilestoneMembershipCoordinator` — all reused as-is.
