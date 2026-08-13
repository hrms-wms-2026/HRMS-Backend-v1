# Future Feature: Milestone (Objective) In-Charge Role & Permission System — SUPERSEDED

**Status:** Designed. This raw-context capture (originally written 2026-08-04) has been fully brainstormed and superseded by an approved design — see `docs/superpowers/specs/2026-08-04-work-management-milestone-hierarchy-design.md`. That design covers: the module-wide `projects:access`/`projects:read` permission model, `Objective.HeadUserId`/`ReportingManagerId` schema, the fully-recursive hardcoded tree-authorization rule, the `objective_change_requests` approval workflow for delete/conflicting-edit/transfer on a Head's own node, and the Objective CRUD + approval endpoint list. Kept here only as a pointer so this section's original "not yet designed" status isn't mistaken as still current.

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

No approval entity/table/handler exists for Projects. `role_assignments` (`phase1-table-inventory.md` line ~545, `PendingApproval` status) and other tenant-scoped approval-status patterns already in this schema are the closest existing precedent worth reviewing before designing a new one from scratch — **and now so is `objective_change_requests`** (`docs/superpowers/specs/2026-08-04-work-management-milestone-hierarchy-design.md` §3), a request/approval table built for a different trigger (Objective-level delete/conflicting-edit/transfer, approved by the Objective's Reporting Manager) but a possibly-reusable shape for this Project-level baseline-change approval. They are not the same feature and should not be silently merged — read that design before assuming a shared table works here.

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
