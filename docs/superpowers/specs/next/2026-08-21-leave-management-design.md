# Leave Management (Backend) — Design

**Status:** Approved 2026-08-21, built directly from the complete product spec at `C:\HR\leave-management-complete.md` (not a brainstormed doc — the user supplied a finished, comprehensive spec covering permissions, all 5 stored records + product-only fields, 9 screens, calculations, and the request lifecycle). This design exists to lock the schema/vocabulary decisions the spec leaves implicit before Phase 1 starts, and to record what already exists vs. what's net-new.

**Supersedes:** the stale pointer in the frontend repo's `docs/superpowers/specs/next/2026-08-17-attendance-leave-management-design.md`, which names a companion file `HRMS-Backend-v1/docs/superpowers/specs/next/2026-08-17-attendance-leave-management-design.md` — **that file does not exist in this repo** (verified by directory listing; only the frontend-side doc was ever written, matching the "Known drift" pattern already flagged once before in this repo's `plans/next/SUMMARY.md` for an unrelated missing file). No backend Leave design existed before this one.

## What already exists (verified, do not re-build)

- **Permission codes** — already seeded in `PermissionSeeder.cs:101-106`: `leave:read`, `leave:read-team`, `leave:create`, `leave:approve`, `leave:manage`. Comment marks them "Legacy compatibility alias — to be migrated to time_off" but nothing has migrated them and the frontend's `nav-items.config.ts` already keys off the `leave` **module**, so this plan keeps `leave:*` permission codes as-is. Do not invent `time_off:*` codes.
- **`leave:read-own`** — already defined in `ModuleAutoGrants.cs:10` (`["leave"] = ["leave:read-own"]`), auto-granted to every employee of a tenant subscribed to the `leave` module. This is exactly the spec's "universal, never assigned via a role" permission (§1). **No new code needed for this** — Phase 0 only needs to *use* it on the self-service endpoints (Balance, My Leave, Submit, Cancel own).
- **Nav entry** — `nav-items.config.ts:73-79` already has `{ id: 'time-off', path: '/time-off', requiredModules: ['leave'] }`, unrouted (placeholder).
- **No leave tables exist yet** — confirmed via `Migrations/` directory search. Schema is genuinely from scratch.

## Real gap found: HR Manager role template can't manage Leave yet

`RoleTemplateSeeder.cs:46-58`, the system `HR Manager` template (`id f0000000-0000-4000-8000-000000000001`), grants `["attendance:read","employees:read","leave:approve","leave:read"]` — **`leave:manage` is missing**, so even a fully-built Leave Types/Policies admin screen would 403 for the one role meant to configure it. The seeder is insert-only (`AnyAsync(x => x.Name == t.Name)` skip-if-exists, `RoleTemplateSeeder.cs:76-78`), so editing the C# array alone won't reach any environment where `HR Manager` was already seeded (every existing dev DB). Phase 0 must ship **both**: the array edit (fresh DBs) and a data-patch EF migration (`UPDATE role_templates SET permission_codes_json = ...`) for existing rows. See Part 1, Task 9.

## Schema decisions (locked here so every later phase inherits one vocabulary)

Per the spec's own §8 warning — "if you build only the five records, [product behaviours] have nowhere to live" — every entity below carries the full product surface, not just the module-schema subset.

### 1. Half-day representation
`LeaveRequest.HalfDayPeriod` (`string?`, nullable) using a string-constant class `LeaveHalfDayPeriods { None, Am, Pm }` — matches this repo's existing convention for small closed vocabularies (see `TaskStatusVisibilities` in Work Management: `public const string Public = "public"`, not a C# enum). `TotalDays` stays the single source of truth for calculation (`decimal(5,1)`, half-day = `0.5`); `HalfDayPeriod` is display/input metadata only, not re-derived from `TotalDays`.

### 2. Approval mode + delegate
`LeavePolicy.ApprovalMode` (`string`, `LeaveApprovalModes { AnyOne, AllMustApprove, InOrder }`) is the policy-level default. Each submitted request materializes one `LeaveRequestApprover` row per assigned approver (`EmployeeId`, `SequenceOrder`, `Status` via `LeaveRequestApproverStatuses { Pending, Approved, Rejected, Skipped }`, `Comment`, `DecidedAt`). `InOrder` mode means row 2 stays `Pending` (not visible to that approver as actionable) until row 1's status leaves `Pending`. Delegation is a separate `LeaveApprovalDelegate` table (`ApproverId`, `DelegateId`, `StartDate`, `EndDate`) resolved at approver-assignment time; the audit (spec §8, Screen 8) shows both names by storing `DelegatedFromApproverId` on the `LeaveRequestApprover` row when a delegate acted.

### 3. Paid/unpaid split
`LeaveRequest.PaidDays` / `UnpaidDays` (`decimal(5,1)` each, computed once at submit time from remaining balance vs. requested `TotalDays`) — plain fields on the request, not a separate entity. Matches spec §2.4: it's a request-level display/audit field, not a workflow object.

### Other closed vocabularies (all string-constant classes, not C# enums, per repo convention)
- `LeaveTypeCategories { Annual, Sick, Maternity, Paternity, Compassionate, Unpaid, Custom }`
- `LeaveGenderRestrictions { All, Male, Female }`
- `LeaveRequestStatuses { Pending, Approved, Rejected, Cancelled }`
- `LeaveBalanceChangeTypes { Accrual, Deduction, CarryForward, Forfeiture, Adjustment }`
- `LeaveAccrualStarts { Immediately, AfterProbation, AfterNMonths }`
- `LeaveProrationMethods { CalendarDays, WorkingDays }`
- `LeaveEntitlementSources { Auto, Manual }`

## Entities (Feature = `Leave`, folder convention singular per `Department`/`Position`/`LegalEntity` precedent — no namespace collision here since folder names (`Type`, `Policy`, `Entitlement`, `Request`, `BalanceAudit`) never equal their entity class names (`LeaveType`, `LeavePolicy`, ...), so normal full nesting applies, unlike Department's workaround)

| Entity | Folder | Owns |
|---|---|---|
| `LeaveType` | `Features/Leave/Type/` | Screen 1 fields, all of §2.1 |
| `LeavePolicy` + `LeavePolicyLeaveType` (join, multi-type) + `LeavePolicyBlackoutPeriod` + `LeavePolicyLegalEntity` | `Features/Leave/Policy/` | Screen 2, §2.2 |
| `LeaveEntitlement` | `Features/Leave/Entitlement/` | Screens 3 & 4, §2.3 |
| `LeaveRequest` + `LeaveRequestApprover` + `LeaveRequestDocument` + `LeaveApprovalDelegate` | `Features/Leave/Request/` | Screens 5, 6, 8, 9, §2.4 |
| `LeaveBalanceAudit` | `Features/Leave/BalanceAudit/` | append-only, §2.5 |

All implement `ITenantOwnedEntity`. `LeaveRequestDocument.FileRecordId` points at the existing `file_records` registry (R2-backed) per the backend architecture skill's file storage rules — no new storage mechanism.

## Frontend nav decision

No new nav parent/module key. The existing single `time-off` item (`nav-items.config.ts:73-79`, gated on module `leave`) becomes a parent with children, mirroring the `organization` item's `children` pattern:
- `/time-off` — My Leave / Balances (every employee, `leave:read-own`)
- `/time-off/team` — Team Balances + Pending Approvals (`leave:read-team` / `leave:approve`)
- `/time-off/types`, `/time-off/policies`, `/time-off/entitlements`, `/time-off/all` — HR config + all-requests/all-balances (`leave:manage` / `leave:read`)
- `/time-off/calendar` — Team calendar (`calendar:read`, universal)

Visibility per sub-route is permission-gated in the component (nav gating is UX only, per the frontend skill) — the backend is the real boundary.

## Supersession of the 2026-08-17 mocked sketch

The frontend's `2026-08-17-attendance-leave-management-design.md` scoped a much narrower Phase 1 (HR-only, list + approve/reject, mocked data, no apply-for-leave, no type/policy UI) as a placeholder because the real backend design didn't exist yet. That file is not deleted (it documents real history and the Attendance half is untouched by this plan), but its Leave section is superseded by this design and the plan in `plans/next/2026-08-21-leave-management/` — its header has been annotated accordingly. Do not build against its mocked `LeaveRequestResponse` shape; use the DTOs defined per-phase in the plan.

## Phases

See `plans/next/2026-08-21-leave-management/SUMMARY.md` for the full phase breakdown, dependencies, and exit criteria. One-line map:

0. Schema + role-template fix (backend only) → 1. Leave Types → 2. Leave Policies → 3. Entitlements + Balances → 4. Request Submission → 5. Approval workflow → 6. Cancellation → 7. Team Calendar → 8. Balance Audit surfacing + bulk generate + year-end job → 9. Hardening (full test matrix, live dev-DB pass every phase, frontend mock retirement).

## Testing note (carried from this repo's own memory)

Testcontainers connects as the Postgres table owner and bypasses `FORCE ROW LEVEL SECURITY`, so an all-green integration suite has previously hidden a real tenant-isolation bug (System-mode RLS gap, documented elsewhere in this repo's history). Leave is entirely tenant-context (no System-mode entry point), so that specific bug class doesn't apply — but the lesson does: every phase's exit criteria includes a live run against the real dev DB (`DevSmokeTestTenantSeeder`'s acme/dapi tenants), not just a green suite.
