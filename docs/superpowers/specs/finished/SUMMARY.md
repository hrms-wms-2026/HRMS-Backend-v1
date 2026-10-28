# specs/finished/ — Summary

**Purpose:** Approved designs whose corresponding plan in `plans/finished/` is done. Split into one `YYYY-MM-DD/` subfolder per date, mirroring `plans/finished/` (see `docs/superpowers/rules/FILE_CREATION_RULES.md`). Dates here match the design's own filename date, except `2026-08-08/` (below) which uses the *completion* date, matching `plans/finished/`'s convention — the design itself is dated 2026-08-06.

**Last updated:** 2026-09-21

## Files by date

**`2026-09-21/`** (1)
- `2026-09-21-task-subtasks-design.md` — backend plan `plans/finished/2026-09-21/2026-09-21-task-subtasks-backend.md` finished all 6 tasks on 2026-09-21; frontend companion remains tracked in the frontend repository.

**`2026-08-21/`** (1)
- `2026-08-20-work-management-project-page-redesign-design.md` — matching plan `plans/finished/2026-08-21/2026-08-20-work-management-project-page-redesign/`.

**`2026-08-03/`** (2)
- `2026-08-03-doc-audit-and-process-setup-design.md`
- `2026-08-03-work-management-foundation-design.md`

**`2026-08-04/`** (2)
- `2026-08-04-work-management-projects-edit-delete-view-design.md`
- `2026-08-04-work-management-milestone-hierarchy-design.md`

**`2026-08-08/`** (2)
- `2026-08-06-work-management-milestone-membership-and-achieve-design.md` — its plan, `plans/finished/2026-08-08/2026-08-06-work-management-milestone-membership-and-achieve.md`, finished all 18 tasks 2026-08-08.
- `2026-08-08-work-management-my-project-milestones-design.md` — its plan, `plans/finished/2026-08-08/2026-08-08-work-management-my-project-milestones.md`, finished all 5 tasks 2026-08-08 (same day, both design and plan dated 2026-08-08 — no completion-date/filename-date mismatch for this one).

**`2026-08-10/`** (1)
- `2026-08-10-milestone-ownership-and-subtree-access-design.md` — its plan, `plans/finished/2026-08-10/2026-08-10-milestone-ownership-and-subtree-access.md`, finished both tasks 2026-08-10 (same day). Backend half of a cross-repo feature — frontend half in `Hrms--Web-application---front-end---v1`.

**`2026-08-16/`** (1)
- `2026-08-16-multi-legal-entity-employment-foundation-design.md` — its plan, `plans/finished/2026-08-16/2026-08-16-multi-legal-entity-employment-foundation/`, finished all 3 parts 2026-08-16. Unique index on `employees` is `(user_id, legal_entity_id)`.

## Open items

None.

## Added 2026-10-28 (migrated from loose `specs/` files)

- `2026-08-06/2026-08-06-invite-platform-manager-design.md` — Invite Platform Manager — Design (v2, supersedes the 2026-08-06 v1 in git history)
- `2026-08-07/2026-08-07-legal-entity-logo-upload-design.md` — Legal Entity Logo Upload — Design
- `2026-08-10/2026-08-10-inactivity-screenshot-daily-report-design.md` — Inactivity Screenshot and Daily Report Design
- `2026-08-25/2026-08-25-attendance-list-pagination-design.md` — Attendance list pagination — design
- `2026-08-28/2026-08-28-calendar-core-external-sync-design.md` — Calendar Core + Google/Outlook External Sync — Implementation Spec
- `2026-08-31/2026-08-31-calendar-invites-rsvp-conflicts-design.md` — Calendar Invites, RSVP, and Conflict Detection Design
- `2026-08-31/2026-08-31-calendar-r1-recurrence-and-frontend-design.md` — Calendar R1: Recurrence Engine + Frontend Design
- `2026-09-01/2026-09-01-calendar-timezone-and-unsaved-guard-design.md` — Calendar Timezone Correctness and Unsaved-Changes Guard Design
- `2026-09-04/2026-09-04-calendar-small-fixes-design.md` — Calendar Small Fixes — Design Spec
- `2026-09-04/2026-09-04-landing-page-workspace-shortcut-design.md` — Landing Page "Go to Workspace" Shortcut
- `2026-09-14/2026-09-14-task-attachments-rich-description-design.md` — Task Attachments, Text Highlighting & Inline Description Images — Design Spec
- `2026-09-18/2026-09-18-sprint-lifecycle-redesign-design.md` — Sprint Lifecycle Redesign Design
- `2026-09-18/2026-09-18-task-status-groups-design.md` — ClickUp-style Task Status Groups Design
- `2026-09-22/2026-09-22-task-comments-design.md` — Task Comments — Design Spec
- `2026-09-23/2026-09-23-teams-zoom-meeting-integration-design.md` — Microsoft Teams / Zoom Meeting Integration — Implementation Spec
- `2026-09-23/2026-09-23-wm-independent-sprints-design.md` — Independent Sprints — Design
- `2026-09-30/2026-09-30-employee-detail-redesign-design.md` — Employee Detail Screen Redesign — Design
- `2026-10-06/2026-10-06-leave-approval-forward-and-history-design.md` — Leave approvals: forward to my manager + approval history
- `2026-10-20/2026-10-20-customizable-dashboard-design.md` — Customizable Grid Dashboard (My Work + My Team) — Design

## 2026-10-28/

- `2026-09-02-module-creation-assets-members-design.md` — Module / Sub-Module Creation Screen — Assets, Members, Date Range Picker. Assets part built 2026-10-28 (upload, delete, download endpoints + assets on Get Objective).
