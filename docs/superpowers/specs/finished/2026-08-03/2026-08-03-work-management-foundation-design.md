# Design: Work Management — Foundation Slice (Projects, Objectives, Creation Transaction)

**Date:** 2026-08-03
**Status:** Approved
**Slice:** 1 of 6 (Foundation → Reads → Updates → Members → Invitations → Version Status Movement)
**Source spec:** user-provided `ONEVO Work Management Project Backend Implementation` prompt (locked business decisions), reconciled against the actual repository via a code-conventions research pass.

## 1. Goal

Stand up the database schema, Domain entities, and the single `POST /api/v1/work/projects` creation transaction for Work Management: a Project always creates exactly one Default Objective, one creator membership, one Default Version (`planned`), one release reminder, optional Labels, and an optional logo — atomically, reusing all existing infrastructure (storage, outbox, idempotency, RLS, permissions).

## 2. What changed vs. the original spec (grounded in actual repo conventions)

Confirmed by a dedicated research pass (see chat history 2026-08-03) before writing this design:

| Area | Original spec assumption | Actual repo reality | Decision |
|---|---|---|---|
| Base entity | Generic "aggregate root" | `BaseEntity` (`Id`, `TenantId`, `CreatedAt`, `UpdatedAt`, `CreatedById`, `IsDeleted`, `DeletedAt`, domain events) + `ITenantOwnedEntity` | New entities inherit `BaseEntity` |
| Result type | Generic "Result" | `Result`/`Result<T>` with `Success`/`Failure`/`NotFound`/`Forbidden`/`Conflict` static factories; **no** `ToActionResult()` — controllers use an inline `result.IsSuccess ? Ok(...) : Problem(...)` ternary | Follow the inline ternary exactly |
| Permission codes | Dot notation suggested | Colon `resource:action`; `projects:read/write/create` and `okr:read/write` **already seeded** under a `work_management` module | Reuse seeded codes for Projects/Objectives; add `members:read`, `members:manage`, `invitations:manage`, `invitations:respond`, `versions:write`, `labels:manage` in the same style |
| Idempotency | "Require and reuse" | `[Idempotent]` filter + `IIdempotencyStore` fully built, currently used by exactly one Admin endpoint | Reuse as-is on `POST /projects` and `POST .../invitations` |
| Outbox | "Use existing Outbox Pattern" | `IOutboxWriter.EnqueueAsync<TPayload>(type, payload, tenantId, ct)`, payload encrypted via `IEncryptionService`, caller commits via `SaveChangesAsync`/`IUnitOfWork` | Reuse as-is for invitation-created events (Slice 5) |
| Logo/file linkage | "Use existing `entity_assets` mechanism" | **`entity_assets` does not exist in code** — only documented in `phase1-table-inventory.md`. Real convention elsewhere is a direct `*_file_id` FK column | User decision (2026-08-03): **build `entity_assets` now**, scoped to `owner_type = 'project'` / `asset_purpose = 'project_cover'` only. Do not build other owner types speculatively. |
| Concurrency | "xmin or explicit token" | No generic convention exists anywhere (`IsConcurrencyToken()` used exactly once, for an unrelated single column); **`.UseXminAsConcurrencyToken()` does not exist in the installed Npgsql 10.0.2 package** (confirmed during Task 2 execution by inspecting the assembly — no `Xmin` identifier present) | **Deferred**, not adopted, for this slice: `projects`/`objectives`/`versions` are insert-only here (no UPDATE path exists yet), so no concurrency token is added. Whichever slice first adds an update endpoint (Slice 3/6) must research the current correct API — a naive `HasColumnName("xmin")` shadow property risks a migration that tries to `ALTER TABLE ... ADD COLUMN xmin`, which Postgres rejects as a reserved system column. |
| Controller folder | Architecture doc says `Controllers/Customer/{Feature}/{SubFeature}/` | Not implemented anywhere; every live tenant controller is flat under `Controllers/Tenant/{Feature}/` | Build under `Controllers/Tenant/WorkManagement/{Feature}Controller.cs`, matching actual practice, not the aspirational doc |
| Transaction pattern | N/A | Two coexisting patterns: repository-owned `SaveChangesAsync`, or handler-injected `IUnitOfWork` | Use `IUnitOfWork` directly in `CreateProjectCommandHandler` — this transaction spans 6+ new entities in one repository/`SaveChangesAsync` call, which the `IUnitOfWork` pattern (used by `RequestPasswordResetCommandHandler` for a similar multi-write+outbox case) fits better than a single-repository owned save |

`docs/superpowers/project_ core/phase1-table-inventory.md` has already been updated (2026-08-03) to reflect the resulting schema — see that file for the authoritative column list; this design does not repeat it in full.

## 3. Scope of this slice

**In:** `entity_assets`, `version_statuses` (seeded), `project_categories`, `projects`, `objectives`, `project_members`, `versions`, `release_calendar`, `labels` — Domain entities, EF configurations + migration + RLS, repositories, the `CreateProjectCommand` vertical slice, and `POST /api/v1/work/projects`.

**Out (later slices):** list/detail/category read endpoints (Slice 2), project/objective edit endpoints (Slice 3), member management endpoints (Slice 4), invitations (Slice 5), version status movement (Slice 6). `project_member_invitations` table is created now (needed for the FK shape) but its endpoints are Slice 5.

**Out (separate future phases, not touched):** Task Management, Sprint Planning, Collaboration/Wiki, GitHub Integration — their tables still reference the now-removed `workspaces` table; that dangling reference is noted in the tables doc and deferred to when those phases are planned.

## 4. Project-creation transaction (the core of this slice)

`POST /api/v1/work/projects`, `multipart/form-data`, `[Idempotent]`, permission `projects:create`, `[Authorize(Policy = "TenantPolicy")]`.

Sequence (one `IUnitOfWork.SaveChangesAsync` at the end):

1. Resolve trusted `tenantId`/`userId` from `ICurrentUser`; resolve the current `Employee` and active legal-entity context from existing services — never from the request body.
2. Validate request (FluentValidation via `ValidationBehavior`): name, identifier format, dates (`targetDate >= startDate`), category exists/active/tenant-owned, hours non-negative, labels non-empty/no in-request duplicates, logo (if present) is an image within existing size/MIME rules.
3. Normalize `identifier` (trim, uppercase); check tenant-uniqueness.
4. If a logo file is present: `IFileStorageService.UploadAsync(tenantId, userId, fileName, contentType, purpose: UploadPurposeCatalog.ProjectCover, stream, ct)` → returns a `FileRecordDto`. This call already handles quota reservation/commit and R2 upload; no direct storage/quota code in the handler.
5. Construct in memory (no early partial saves): `Project` (`BaseEntity`, `CreatedById = userId`, `LeadId = userId`), `Objective` (`IsDefault = true`, `ParentObjectiveId = null`, `ProjectId = project.Id`, fields mirrored from the Project per the tables-doc mirroring rules, `OwnerId = userId`), `ProjectMember` (`ObjectiveId = defaultObjective.Id`, `MembershipSource = "system"`, `IsActive = true`), `Version` (`Name` per repository naming convention, `StatusId = 1`), `ReleaseCalendar` (`RecipientUserId = userId`, `ReminderType = "project_release"`, `ScheduledDate` from request), zero-or-more `Label` rows, and (if a logo was uploaded) an `EntityAsset` row (`OwnerType = EntityAssetOwnerTypes.Project`, `AssetPurpose = "project_cover"`, `IsPrimary = true`).
6. Add all entities through their respective repositories; call `_unitOfWork.SaveChangesAsync(ct)` once.
7. Write the `project.created` audit event through the existing auditing boundary.
8. On any failure **after** the logo upload but **before** the commit succeeds: call the existing storage compensation path to release the reservation/delete the uploaded object (per `IFileStorageService`'s documented compensation contract) — do not leave an orphaned R2 object.
9. Return `201 Created`, `Location: /api/v1/work/projects/{projectId}`, and the composed response (Project + Default Objective + Default Version + release reminder + labels + creator membership + logo metadata).

## 5. Out of scope for this slice, explicitly

- No read endpoints (list/detail/categories) — Slice 2.
- No update endpoints — Slice 3.
- No member/invitation management endpoints beyond what creation itself produces — Slices 4/5.
- No version status movement endpoint — Slice 6.
- No `entity_assets` owner types other than `project`.
- No fix for the dangling `workspace_id` references in unrelated future-phase tables.

## 6. Self-review

- No placeholders; every schema decision traces to either the locked spec or a confirmed repo convention.
- Internally consistent with the already-updated `phase1-table-inventory.md`.
- Scoped to one vertical slice — small enough for one implementation plan.
- Ambiguity resolved: `entity_assets` build-now decision, `xmin` concurrency adoption, and controller folder placement were all explicit user/derived decisions, not left open.
