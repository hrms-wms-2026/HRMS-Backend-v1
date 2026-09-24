# Management Coverage — Responsible Person Design

**Status:** Approved by user 2026-08-19, ready for implementation planning.

**Origin:** brainstormed live with the user 2026-08-19 via `superpowers:brainstorming`, immediately following the reporting-manager-resolution design (`2026-08-19-reporting-manager-resolution-design.md`). The user proposed extending the existing `ManagementCoverageRecord` feature with a specific-employee resolution, after that design explicitly ruled out reusing `ManagementCoverageRecord` for reporting-manager purposes (it only resolves to a Position, not an Employee). This design closes that exact gap, for `ManagementCoverageRecord`'s own purpose — not reporting-manager resolution, which stays entirely separate (see §2).

**Prerequisite:** depends on `IPositionAssignmentRepository.GetActiveHoldersAsync` (reporting-manager-resolution plan, Task 3) already being implemented — this design reuses it rather than adding a second "who currently holds this position" query. Implementation should happen in the same worktree/branch (`feat/bulk-employee-onboarding`) as that plan, after Task 3 lands.

---

## 1. Goal

`ManagementCoverageRecord` today says "Position P covers Department/Position/Company X at responsibility level N" — but when P is a pooled position (multiple simultaneous holders), nothing says which specific person is actually responsible. Add that: a `ResponsibleEmployeeId` on each coverage record, asked for only when needed (P has more than one current holder), silently resolved automatically otherwise, and exposed as a reusable "who is responsible for X" query for future consumers to build on.

## 2. Relationship to reporting-manager resolution — explicitly separate

These answer two different questions and share no data:

- **Reports-to** (separate design, separate plan): "who does employee E report to on the org chart" — resolved once at onboarding/position-change time via `Position.ReportsToPositionId` + `PositionAssignment.ReportsToEmployeeId`, materialized into `EmployeeHierarchyClosure`.
- **Coverage** (this design): "who currently has responsibility over department/position/company X, for whatever purpose a future consumer defines" — resolved per-lookup via `ManagementCoverageRecord`, independent of anyone's individual manager.

An employee's direct manager and their department's coverage owner are frequently different people (a Junior Engineer reports to their Team Lead, but Engineering's HR-coverage owner might be a completely unrelated person in HR). Neither mechanism queries the other. The only thing they share is the interaction pattern (§4) and, at the data layer, the same `GetActiveHoldersAsync` "who currently holds this position" primitive.

## 3. Scope

**In scope:**
- `ManagementCoverageRecord.ResponsibleEmployeeId` (nullable, no FK — see §5).
- Validation: required only when `OwnerPositionId` has >1 active holders; must itself be a current active holder of `OwnerPositionId`.
- A resolution query returning, for a given covered target, the ordered list (by `OwnerOrder`) of coverage levels with each one's resolved employee or an "incomplete" marker (§6).
- Position Coverage modal: a "Responsible person" picker, shown only when the owner position is ambiguous; inline display of the resolved name on existing records; a warning state for records that became incomplete (owner position's occupancy changed since the record was set).

**Out of scope:**
- Any actual approval-routing consumer (leave approval, etc.) — none exists in the codebase today (verified: `ManagementCoverageRecord`'s only current consumers are its own CRUD/list queries and the management modal). This design builds the resolver primitive only; wiring a real consumer to it is separate, future work.
- Automatic backup fallback logic (e.g. "if Primary is on leave, try Backup 1") — the resolver returns the ordered list; deciding how many levels to try and why is a future consumer's job, not this design's.
- Changing `OwnerOrder`'s existing semantics (Primary Manager = 1, Backup Manager N = order N+1) or the modal's existing conflict-prevention (one owner per level per target) — both already work and are untouched.

## 4. Interaction pattern (shared with reporting-manager, not shared data)

Same rule as the reporting-manager design: never show a picker when there's nothing to disambiguate.
- Owner position has exactly 1 active holder → resolves silently, no field shown, `ResponsibleEmployeeId` stays null even if previously set (a position that shrinks to one holder doesn't need the field anymore, but a previously-set value is left alone rather than cleared — see §5's staleness handling, which already covers this case generically).
- Owner position has >1 active holders → picker required, populated from current holders (via `GetActiveHoldersAsync`).

## 5. Data model and staleness

`ManagementCoverageRecord` gains:

| Column | Type | Notes |
|---|---|---|
| `responsible_employee_id` | uuid, nullable | No FK — same rationale as `PositionAssignment.ReportsToEmployeeId`: "is this employee a *current* holder of the owner position" is time-varying, not a static referential-integrity fact a DB constraint can express. |

**Staleness (user-confirmed):** if the employee referenced by `ResponsibleEmployeeId` later stops being an active holder of `OwnerPositionId` (position change, offboarding, or the owner position's occupancy otherwise changes), the record is **not** auto-corrected and does **not** silently fall through to the next `OwnerOrder` level. The resolution query (§6) marks that level `Incomplete` until someone with manage permission re-picks a current holder. This is the same "flag it, don't guess" choice made for reporting-manager, applied consistently — an approval or notification routed to the wrong person silently is worse than a visible gap that gets fixed once.

## 6. Resolution query

New query, e.g. `GetCoverageResolutionQuery(legalEntityId, coveredTargetType, coveredPositionId?, coveredDepartmentId?)`, returning an ordered list:

```
[
  { ownerOrder: 1, ownerPositionId, ownerPositionName, status: Resolved, employeeId, employeeName },
  { ownerOrder: 2, ownerPositionId, ownerPositionName, status: Incomplete, employeeId: null },
  ...
]
```

Resolution per record, in `OwnerOrder` sequence:
1. Look up `OwnerPositionId`'s current active `PrimaryEmployment` holders via `GetActiveHoldersAsync`.
2. Exactly one holder → `Resolved`, that employee, regardless of `ResponsibleEmployeeId` (mirrors reporting-manager's unique-target auto-resolution).
3. More than one holder → if `ResponsibleEmployeeId` is set and is among the current holders, `Resolved` with that employee; otherwise `Incomplete`.
4. Zero holders (owner position currently vacant) → `Incomplete` (nothing to resolve to — same as reporting-manager's vacant-position case).

This query is the entire "reusable primitive" this design commits to. No consumer calls it yet (§3); its shape is designed to make a future "try Resolved levels in order until one accepts" consumer trivial to add without touching this query.

## 7. UI changes (Position Coverage modal)

- Add/edit coverage form: after `Covered Target` + `Responsibility Level` are chosen, if the *current* `position` (the modal's owner — recall the modal is opened per-owner-position, per existing `@Input() position`) has more than one active holder, show a "Responsible person" `SelectComponent` populated from `GetActiveHoldersAsync(position.id)`. Required to submit when shown; omitted (and ignored server-side per §5's unique-holder rule) otherwise.
- Existing coverage rows: when `responsibleEmployeeName` is present, display it inline (e.g. "Alice — HR Manager" per the user's suggested copy) next to the existing target/level display. When the owner position is ambiguous and no responsible person is resolved (either never set, or stale per §5), show a warning row state — visually consistent with the existing `isSelfCoverage` warning treatment already in the modal, reusing that pattern rather than inventing a new one.
- Label: "Responsible person" (per user's stated preference over "Responsible Employee" / "Coverage Owner" — plain language a non-technical HR user reads unambiguously).

## 8. Testing

- **Unit**: resolution query — single-holder auto-resolve, multi-holder resolve-via-`ResponsibleEmployeeId`, stale-reference → `Incomplete`, vacant-owner → `Incomplete`, multi-level ordering. `AddManualCoverageRecordCommandHandler`/`UpdateManualCoverageRecordCommandHandler` — required-when-ambiguous validation, must-be-current-holder validation, ignored-when-unique passthrough (same four-test shape as the reporting-manager `SaveOnboardingDraftCommand` tests).
- **Integration**: `responsible_employee_id` round-trips and is tenant-isolated (same RLS-table family as `ManagementCoverageRecord` already is); end-to-end add-coverage-with-responsible-person → resolution query returns it.
- **Frontend**: modal shows/hides the picker based on owner-position holder count; warning state renders for incomplete records; existing conflict-detection/level-management tests remain green (this design doesn't touch `OwnerOrder` semantics).

## 9. Open items for the plan to resolve

- Exact route/permission for the new resolution query — should follow `GetPositionCoverageQuery`'s existing convention (`org:read`, same controller family) rather than being guessed here; plan should verify against the actual `PositionsController` (or wherever `GetPositionCoverageQueryHandler` is registered) before finalizing.
- Whether `GetActiveHoldersAsync` needs a small extension (e.g. accepting a legal-entity-scoped position lookup consistent with how the modal already scopes `availablePositions`) — implementation detail, verify against its Task 3 signature once that task has actually landed.
- Migration naming/sequencing relative to the reporting-manager-resolution plan's migrations, since both land on the same worktree branch — plan should sequence this after that plan's Task 1 migration, not concurrently, to avoid two independent `dotnet ef migrations add` runs racing on the same model snapshot.
