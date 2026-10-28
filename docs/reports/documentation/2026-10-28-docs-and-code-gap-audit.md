# Docs and code gap audit (2026-10-28)

Covers both repos (`HRMS-Backend-v1`, `Hrms--Web-application---front-end---v1`). Done while restructuring `docs/` so every plan and design sits in `next/` or `finished/<date>/` again (FILE_CREATION_RULES rule 1). Status was decided from the code, not from the documents.

## 1. Restructure summary

| What | Backend | Frontend |
|---|---:|---:|
| Loose plans moved into `plans/finished/<date>/` or `plans/next/` | 44 | 31 |
| Loose designs moved into `specs/finished/<date>/` or `specs/next/` | 21 | 22 |
| Loose reports in `plans/finished/` root moved to `docs/reports/<area>/` | 28 | 19 |
| Dated plans in `plans/finished/` root moved into their date folder | 2 | — |
| `next/` items found already built, moved to `finished/<date>/` | 8 (+ team-attendance plan and design) | — |
| `docs/superpowers/reports/` (duplicate of `docs/reports/`) emptied | 2 files | — |
| `docs/repport zip.zip` removed (33 of 36 entries were byte-identical to `docs/reports/`; the one unique report was extracted to `reports/work-management/`) | 1 | — |
| References to moved files rewritten (docs, code comments) | yes | yes |

Each `SUMMARY.md` that gained or lost files has a dated section listing what moved and why. `.superpowers/sdd/` in both repos was **not** moved: it is gitignored subagent scratch (task briefs and review diffs), not documentation.

## 2. Code gaps found (features documented as built but not in code)

| Severity | Gap | Evidence | Docs now say |
|---|---|---|---|
| **High (UAT bug)** | **Work Management module (objective) file attachments.** The frontend store calls `POST /api/v1/work/objectives/{id}/assets` and `DELETE .../assets/{assetId}` (`project-api.service.ts`, `project-detail.store.ts`), but the backend never built these endpoints; only the upload purpose `objective_asset` is registered. Attaching a file to a module returns 404. | No `UploadObjectiveAsset*` type in any branch's history; only the plan commit `13476ad4`. | Backend plan `plans/next/2026-09-02-objective-asset-attachments-backend.md` and design `specs/next/2026-09-02-module-creation-assets-members-design.md` are **pending**. |
| Low | **Tray app access toggle per employee.** The frontend has `PeopleApiService.setTrayAppAccess()` → `POST /employees/{id}/tray-app-access`, but no UI calls it and the backend endpoint was never built. No user-visible effect. | `SetEmployeeTrayAppAccessCommand` exists only in plan commit `587246f8`. | Backend and frontend plans/designs for `2026-08-25-tray-app-employee-access-toggle-*` are **pending**. |

## 3. Areas with little or no design documentation

Counted by plan/spec/report file names; the code exists and works, but there is no design or plan that explains it. The module maps (`project_ core/MODULE_MAP.md` here, `docs/core/MODULE_MAP.md` in the frontend) now list every area with its code location so these at least have an entry point.

| Area | Code | Docs before this audit |
|---|---|---|
| Device activation (tray agent pairing) | FE `modules/device-activation`, BE `Monitoring/TrayActivation` | none by name |
| Public landing page | FE `modules/public` | 2026-09-04 workspace-shortcut plan only |
| File storage, quotas, entity assets | BE `Storage/` | 1 |
| Conversations (people chats) | BE `Conversations/`, FE `modules/inbox` | 6, mostly inbox plans |
| Monitoring (18 sub-features: activity, screenshots, check-in, exceptions, meetings, work sessions, ...) | BE `Monitoring/` | 6 for a large area |
| Dev Platform / billing / tenancy | BE `DevPlatform/` | mostly in the platform-admin repo |

## 4. Status still unverified

`next/` folders still hold items whose status nobody re-checked in this pass: backend 93 plan files + 30 design files, frontend 63 plan files + 17 design files (multi-part plans count each part). The eight backend items that had a finished frontend counterpart were checked and moved. The rest should be re-verified the same way (look for the types and endpoints the document defines in `src/`) before trusting a "pending" label; `docs/superpowers/plans/next/SUMMARY.md` labels have gone stale before.

## 5. References to documents that were never committed

Every `docs/superpowers/plans|specs/<file>` reference in both repos was resolved to the file's current location (37 files fixed, including references broken by the 2026-08-06 migration). Two point at files that exist in no repo:

| Reference in | Points to | Note |
|---|---|---|
| `docs/notifications/decisions.md` | `C:/HR2/docs/superpowers/plans/2026-10-16-notification-templates-and-channel-mapping.md` | An absolute path on a team member's machine; the plan was never committed. |
| `specs/finished/2026-08-28/2026-08-28-calendar-core-external-sync-design.md` | `specs/2026-08-26-calendar-outlook-google-integration-notes.md` | Never committed. |

Ask the authors to commit these if they still have them.

## 6. Related reports from the same hardening pass

- `docs/reports/security/2026-10-27-uat-hardening-report.md` (backend) and `docs/reports/code-quality/2026-10-27-uat-hardening-report.md` (frontend): code cleaning, security testing, fixes and open items.
- `docs/reports/build-and-ci/2026-10-27-integration-csrf-label-investigation.md`: integration test status.
