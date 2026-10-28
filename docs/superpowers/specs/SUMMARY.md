# specs/ — Summary

**Purpose:** Approved designs, produced by `superpowers:brainstorming`, that a `docs/superpowers/plans/` implementation plan is then built from. This folder had no `SUMMARY.md` until 2026-08-06, when it was also split into `finished/`/`next/` (mirroring the `plans/` restructure — see `docs/superpowers/rules/FILE_CREATION_RULES.md`).

**Last updated:** 2026-09-22

## Layout

- `finished/` — the design's corresponding plan in `plans/finished/` has been executed and reviewed clean. Further split into one `YYYY-MM-DD/` subfolder per date, mirroring `plans/finished/` (see `docs/superpowers/rules/FILE_CREATION_RULES.md`).
- `next/` — the design is approved, but its plan is either still `pending` in `plans/next/`, or no plan has been written yet. Stays flat, no date subfolders.

Every file here says `**Status:** Approved...` at the top — that field is the *design-approval* status (brainstorm sign-off), not implementation status. Finished/next here tracks implementation status, taken from cross-referencing `plans/SUMMARY.md`.

## Files

| Design | Status | Corresponding plan |
|---|---|---|
| `finished/2026-08-03/2026-08-03-doc-audit-and-process-setup-design.md` | finished | `plans/finished/2026-08-03/2026-08-03-doc-audit-and-process-setup.md` |
| `finished/2026-08-03/2026-08-03-work-management-foundation-design.md` | finished | `plans/finished/2026-08-03/2026-08-03-work-management-foundation.md` |
| `finished/2026-08-04/2026-08-04-work-management-projects-edit-delete-view-design.md` | finished | `plans/finished/2026-08-04/2026-08-04-work-management-projects-edit-delete-view.md` |
| `finished/2026-08-04/2026-08-04-work-management-milestone-hierarchy-design.md` | finished | `plans/finished/2026-08-04/2026-08-04-work-management-milestone-hierarchy.md` |
| `finished/2026-08-08/2026-08-06-work-management-milestone-membership-and-achieve-design.md` | finished | `plans/finished/2026-08-08/2026-08-06-work-management-milestone-membership-and-achieve.md` (18/18 tasks, executed 2026-08-08) |
| `next/2026-08-03-platform-users-list-design.md` | pending | none written yet — design-approved 2026-08-03, no plan file exists in `plans/` under either `finished/` or `next/` |
| `next/2026-08-07-work-management-objective-subtree-design.md` | **drift** | its plan, `plans/next/2026-08-07-work-management-objective-subtree.md`, already shipped (git commits `2a77bbc`..`b900ec5`, 2026-08-07) but neither the plan nor this design were ever moved to `finished/` — flagged, not fixed, during the 2026-08-08 pass (out of that session's scope) |
| `next/2026-08-12-objective-viewmodel-owner-fields-fix-design.md` | pending | none written yet — approved 2026-08-12, companion to the frontend repo's `2026-08-12-milestone-tree-mockup-redesign-design.md` |
| `finished/2026-08-08/2026-08-08-work-management-my-project-milestones-design.md` | finished | `plans/finished/2026-08-08/2026-08-08-work-management-my-project-milestones.md` (5/5 tasks, executed 2026-08-08) |
| `finished/2026-08-10/2026-08-10-milestone-ownership-and-subtree-access-design.md` | finished | `plans/finished/2026-08-10/2026-08-10-milestone-ownership-and-subtree-access.md` (2/2 tasks, executed 2026-08-10) |
| `finished/2026-08-16/2026-08-16-multi-legal-entity-employment-foundation-design.md` | finished | `plans/finished/2026-08-16/2026-08-16-multi-legal-entity-employment-foundation/` (3/3 parts, executed 2026-08-16) |
| `finished/2026-09-21/2026-09-21-task-subtasks-design.md` | backend finished | `plans/finished/2026-09-21/2026-09-21-task-subtasks-backend.md` (6/6 tasks, executed 2026-09-21) |
| `finished/2026-08-16/2026-08-16-employee-detail-screen-backend-design.md` | pending | `plans/finished/2026-08-16/2026-08-16-employee-detail-screen-backend.md` (plan written, not yet implemented) |
| `next/2026-09-22-centralized-file-upload-design.md` | pending | none written yet — approved 2026-09-22, companion to the frontend repo's same-named design doc |
| `next/2026-09-30-employee-overview-signals-and-period-correctness-design.md` | pending | `plans/next/2026-09-30-employee-overview-signals-backend/` (2 parts) + frontend repo `plans/next/2026-09-30-employee-overview-signals-frontend/` (3 parts), written 2026-09-30 |

## Open items

- `platform-users-list-design.md` has been sitting approved-but-unplanned since 2026-08-03 with no corresponding plan file anywhere in `plans/` — worth checking with the user whether it's still wanted, or was superseded/abandoned silently.
- `2026-08-07-work-management-objective-subtree-design.md`/its plan need a finished/next status sync — see the "drift" row above.
- Two loose design files sit directly in `specs/` root, not yet filed into `next/`/`finished/` per the folder/status split this table otherwise follows: `2026-08-06-invite-platform-manager-design.md` and `2026-08-07-legal-entity-logo-upload-design.md`. Checked 2026-08-10: both designs are fully implemented in code already (their plans just have unticked checkboxes) — this is a bookkeeping-only gap, not in-flight work. Not fixed here, out of this session's scope; flagged so it isn't lost again.

## 2026-10-28 migration of loose files

21 files had been added loose in `specs/`, against FILE_CREATION_RULES rule 1. They were moved with `git mv` on 2026-10-28. Status was decided from the code, not from the file: **finished** means the feature's endpoints/components exist in `src/`; **pending** means they do not. Finished files sit under `finished/<date>/` using the date in the file name (the plan/design date; the completion date was not recorded).

| File | Location | Status | Note |
|---|---|---|---|
| Invite Platform Manager — Design (v2, supersedes the 2026-08-06 v1 in git history) | `finished/2026-08-06/2026-08-06-invite-platform-manager-design.md` | finished |  |
| Legal Entity Logo Upload — Design | `finished/2026-08-07/2026-08-07-legal-entity-logo-upload-design.md` | finished |  |
| Inactivity Screenshot and Daily Report Design | `finished/2026-08-10/2026-08-10-inactivity-screenshot-daily-report-design.md` | finished |  |
| Attendance list pagination — design | `finished/2026-08-25/2026-08-25-attendance-list-pagination-design.md` | finished |  |
| Tray App Employee Access Toggle — Backend Design | `next/2026-08-25-tray-app-employee-access-toggle-backend-design.md` | pending | backend endpoint POST /employees/{id}/tray-app-access was never built (only this plan was committed) |
| Calendar Core + Google/Outlook External Sync — Implementation Spec | `finished/2026-08-28/2026-08-28-calendar-core-external-sync-design.md` | finished |  |
| Calendar Invites, RSVP, and Conflict Detection Design | `finished/2026-08-31/2026-08-31-calendar-invites-rsvp-conflicts-design.md` | finished |  |
| Calendar R1: Recurrence Engine + Frontend Design | `finished/2026-08-31/2026-08-31-calendar-r1-recurrence-and-frontend-design.md` | finished |  |
| Calendar Timezone Correctness and Unsaved-Changes Guard Design | `finished/2026-09-01/2026-09-01-calendar-timezone-and-unsaved-guard-design.md` | finished |  |
| Module / Sub-Module Creation Screen — Assets, Members, Date Range Picker | `finished/2026-10-28/2026-09-02-module-creation-assets-members-design.md` | finished | members built earlier; assets built 2026-10-28 (upload, delete, download endpoints + assets on Get Objective) |
| Calendar Small Fixes — Design Spec | `finished/2026-09-04/2026-09-04-calendar-small-fixes-design.md` | finished |  |
| Landing Page "Go to Workspace" Shortcut | `finished/2026-09-04/2026-09-04-landing-page-workspace-shortcut-design.md` | finished |  |
| Task Attachments, Text Highlighting & Inline Description Images — Design Spec | `finished/2026-09-14/2026-09-14-task-attachments-rich-description-design.md` | finished |  |
| Sprint Lifecycle Redesign Design | `finished/2026-09-18/2026-09-18-sprint-lifecycle-redesign-design.md` | finished |  |
| ClickUp-style Task Status Groups Design | `finished/2026-09-18/2026-09-18-task-status-groups-design.md` | finished |  |
| Task Comments — Design Spec | `finished/2026-09-22/2026-09-22-task-comments-design.md` | finished |  |
| Microsoft Teams / Zoom Meeting Integration — Implementation Spec | `finished/2026-09-23/2026-09-23-teams-zoom-meeting-integration-design.md` | finished |  |
| Independent Sprints — Design | `finished/2026-09-23/2026-09-23-wm-independent-sprints-design.md` | finished |  |
| Employee Detail Screen Redesign — Design | `finished/2026-09-30/2026-09-30-employee-detail-redesign-design.md` | finished |  |
| Leave approvals: forward to my manager + approval history | `finished/2026-10-06/2026-10-06-leave-approval-forward-and-history-design.md` | finished |  |
| Customizable Grid Dashboard (My Work + My Team) — Design | `finished/2026-10-20/2026-10-20-customizable-dashboard-design.md` | finished |  |

## 2026-10-28 status correction

Moved from `next/` to `finished/<date>/`: the backend for these is in `src/` (verified by the entity, query and endpoint names the documents define), and the matching frontend plans were already finished.

- `plans/finished/2026-08-15/2026-08-15-employee-self-service-profile-backend.md`
- `plans/finished/2026-08-16/2026-08-16-employee-detail-screen-backend.md`
- `plans/finished/2026-08-17/2026-08-17-sensitive-position-approval-backend.md`
- `specs/finished/2026-08-15/2026-08-15-employee-self-service-profile-backend-design.md`
- `specs/finished/2026-08-16/2026-08-16-employee-detail-screen-backend-design.md`
- `specs/finished/2026-08-17/2026-08-17-employee-offboarding-execution-backend-design.md`
- `specs/finished/2026-08-17/2026-08-17-sensitive-position-approval-backend-design.md`
- `specs/finished/2026-08-18/2026-08-18-bulk-employee-onboarding-backend-design.md`
