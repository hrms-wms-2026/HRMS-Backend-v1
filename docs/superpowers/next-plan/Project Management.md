# Future Feature: Milestone (Objective) In-Charge Role & Permission System

**Status:** Not started. Captured from user context during brainstorming for the Work Management Slice 2/3 endpoints (Edit/Delete Project, Get/List Projects) on 2026-08-04, so this isn't lost when that session ends. Not yet brainstormed/spec'd — this file is raw context for that future brainstorm, not a design.

## The idea, as described by the user

Every Project has a Default Objective (a "Milestone" in frontend/user language — the backend name stays `objectives`). The Default Objective always has a **Milestone-in-charge** (an owner) — at creation this is the Project's creator/lead.

From there it's a **tree structure**:

- A Milestone-in-charge (owner of a given Objective) can create any number of **sub-milestones** under their milestone.
- When creating a sub-milestone, the owner can assign a Milestone-in-charge to it. If they don't assign one, **the creator becomes the sub-milestone's in-charge** by default.
- For a sub-milestone, its Milestone-in-charge's **reporting manager is automatically the parent milestone's in-charge**. This reporting chain mirrors the objective tree.
- Business rule: whatever happens inside a sub-milestone (its deadline, its allocated work hours) **must not conflict with its parent milestone's** deadline / allocated hours. (Today, `projects`/`objectives` hour indicators are warning-only per `phase1-table-inventory.md` — unclear yet whether this new parent/child constraint should also be warning-only or a hard block; needs deciding when this is actually designed.)

## The permission angle

The user wants project/milestone-scoped **capabilities** tied to a member's position in this tree — not just tenant-wide RBAC permission codes (`projects:read`/`projects:write`/etc. from `PermissionSeeder.cs`). Examples named explicitly: view milestone, create milestone, create task, edit milestone, delete milestone — "etc." (the user's word), implying this list isn't exhaustive and needs to be drawn out properly in a real brainstorm.

## Why this is out of scope for the current Work Management endpoint work (2026-08-04)

- `project_members` (see `docs/superpowers/project_ core/phase1-table-inventory.md`, `### project_members`) explicitly lists **"Forbidden: `role`"** — "permission/business-scope checks are separate from membership." Today there is no role/capability column on membership at all. This feature needs a schema change.
- Milestone (Objective) CRUD endpoints (create/edit/delete sub-objective, assign in-charge) don't exist yet — only the Default Objective gets created, implicitly, inside `POST /api/v1/work/projects`.
- Task creation/CRUD doesn't exist yet either (separate future Work Management phase, "Task Management + Worklogs" per the table inventory's pillar breakdown).
- The reporting-manager chain and deadline/hours-conflict-with-parent validation are net-new business rules not reflected anywhere in the current schema or handlers.

Given all of the above, this needs its own schema design (a role/capability model scoped to `project_members` or `objectives`), its own brainstorm, and depends on Objective CRUD + Task CRUD existing first (or being designed alongside it). It's a genuinely separate feature, not an extension of the Edit/Delete/Get/List Project endpoints being built right now.

## Suggested next step (when picked up)

Run `superpowers:brainstorming` fresh on this topic. Good opening questions to work through:
- Is the reporting-manager chain purely informational (for display/notifications) or does it gate anything (e.g., approvals)?
- Full list of capabilities needed per the user's "etc." — view/create/edit/delete milestone, create/edit/delete task, others?
- Are capabilities assigned automatically by tree position (in-charge vs. not) or is there ever manual override?
- Warning-only vs. hard-block for child-vs-parent deadline/hours conflicts (matches or diverges from the existing warning-only pattern on `projects`/`objectives` hours)?
- Does this replace or sit alongside the existing tenant-wide `projects:*` permission codes?

---

# Future Feature: Project Lifecycle Workflow, Approval Pipeline, Archive/Restore, and Progress Calculation

**Status:** Not started. Captured 2026-08-04 from a corrections document the user's manager wrote after reviewing the Onexo Workspace Project Management user journey (screens submitted for review, in the frontend repo — not this repo's API). Cross-checked against the currently-approved `docs/superpowers/specs/2026-08-04-work-management-projects-edit-delete-view-design.md` (see that file's section 9) to confirm none of it was silently folded into that already-approved slice. This file captures the backend-relevant subset as raw context, not a design. The UI/UX-only subset (branding, navigation, screen layout, form structure, text corrections) is captured separately in the frontend repo's `docs/superpowers/next-plan/Project Management.md`.

## What the manager's doc asks for (backend-relevant items only)

**1. Workflow status, separate from schedule health.** Two independent dimensions the manager wants tracked per project:
- Workflow status: Draft → Pending approval → Approved → In progress → On hold → Pending completion → Completed / Cancelled → Archived.
- Schedule health (derived, not stored as a free choice): Upcoming, On track, At risk, Overdue, Completed.

Today `projects` has neither — only `is_active: boolean`, and `phase1-table-inventory.md` explicitly **forbids** a free-form `status` string ("superseded by `is_active`"). Any workflow-status column is a locked-schema change, not additive: the existing "forbidden" note would need to be revisited and re-justified, not just overridden.

**2. Approval pipeline.** Two flows named explicitly:
- New-project approval: Draft → Submit for approval → Approver reviews (Approve / Request changes / Reject) → project starts.
- Baseline-change approval: edits to an *already-approved* project's target date, planned effort, or owner should show current-vs-proposed-vs-impact and require approval before taking effect; the currently-approved value stays authoritative until then.

No approval entity/table/handler exists for Projects. `role_assignments` (`phase1-table-inventory.md` line ~545, `PendingApproval` status) and other tenant-scoped approval-status patterns already in this schema are the closest existing precedent worth reviewing before designing a new one from scratch.

**3. Archive/restore, replacing permanent delete.** The current Edit/Delete/GetById/List spec's Delete is already a soft delete (`is_active = false`, no cascade) — that part already avoids the manager's core objection (permanent data loss). Not yet covered by any existing spec:
- Restore endpoint (none exists today).
- Archive reason + optional note, captured on archive.
- Pre-archive dependency check (block archiving if active time entries / pending approvals exist, surface what's blocking) and an impact-summary preview (milestone/task/document/hours counts) before confirming.
- Archived-projects list/filter, "archived by," "archived date," retention info.
- Permanent deletion allowed only for a Draft project with zero linked records (no milestones/tasks/documents/time entries/approvals) — a narrower, explicitly-scoped hard-delete path distinct from Archive.

**4. Progress calculation.** Manager: don't derive project completion from time-elapsed or due-date proximity. Use weighted milestone completion, weighted task completion, deliverable completion, or approved manual progress instead — and show completion, planned effort, actual time, remaining estimate, effort variance, and schedule variance as separate figures, not folded into one number.

This is blocked on the same dependency already noted above for the Milestone-in-charge feature: Objective (Milestone) CRUD and Task CRUD don't exist yet. No progress-calculation design can be finalized before those exist, since "weighted milestone/task completion" needs milestones and tasks to weight.

**5. Overdue handling as an exception, not an auto-close prompt.** A passed target date should surface as a dismissible notification/banner (owner notification, dashboard exception, "Overdue by N days" badge) — never a blocking "Close or Extend?" modal. Extending the date should go through a Date-extension Request (proposed date, reason, business impact, approver) rather than a direct field edit. This is mostly a frontend behavioral change, but the Date-extension Request is a new backend entity/flow if approval is required for it — same shape as the baseline-change approval in point 2.

**6. Completion review.** Before a project can move to Completed, validate: completion criteria met, no open milestones/tasks/issues, deliverables present, planned-vs-actual effort reviewed, and (per point 2) completion itself goes through approval. No completion-criteria field or validation exists on `projects` today.

## Why this is out of scope for the current Work Management slice (2026-08-04)

Same reasoning as the Milestone-in-charge feature above, plus:
- The schema explicitly forbids the free-form status field this requires — that's a locked decision, not an oversight, and reopening it needs its own justification.
- Every backend-relevant item here (approval pipeline, archive/restore, progress calc, date-extension requests, completion review) needs Objective/Task CRUD to exist first, or needs new tables with no existing precedent to extend.
- The currently-approved Edit/Delete/GetById/List spec was scoped and approved *before* this feedback arrived; re-scoping it now would stall an already-ready-to-implement slice for a much bigger, not-yet-designed feature.

## Suggested next step (when picked up)

Run `superpowers:brainstorming` fresh on this topic, likely after (or alongside) the Milestone-in-charge brainstorm above since they share the Objective/Task CRUD dependency. Good opening questions:
- Does workflow status live on `projects` directly, or as a separate `project_lifecycle_state` table (keeping `projects` itself unchanged, given the existing "forbidden: free-form status" note)?
- Is the approval pipeline generic (reusable for other tenant-scoped approvals) or Project-specific? Check `role_assignments`' `PendingApproval` pattern first.
- Single-approver or multi-stage approval chains?
- Does "schedule health" get computed on read (derived from dates + workflow status, never stored) or cached/stored and recomputed on a schedule?
- What exactly counts as a "linked record" that blocks hard-delete of a Draft, and does that list match what blocks Archive too?
