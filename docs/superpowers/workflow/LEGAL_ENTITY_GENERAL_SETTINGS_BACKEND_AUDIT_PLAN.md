# Legal Entity / Company General Settings — Backend Audit & Implementation Plan (Part 1)

**Scope:** Audit + exact implementation plan only. No code, migrations, or tests were changed to produce this report.
**Repo audited:** `C:\onevoNew\HRMS-Backend-v1`
**Docs audited:** `C:\onevoNew\OneVo-HR`
**Screens covered:** General Settings, Create Company, Delete Company Confirmation (tenant-facing org app, not Developer Platform).

---

## 1. Current Backend State

### 1.1 Domain entity

**File:** `src/ONEVO.Domain/Features/OrgStructure/LegalEntity/Entities/LegalEntity.cs`
**Namespace:** `ONEVO.Domain.Features.OrgStructure.Entities` (verified directly — note the namespace omits the `LegalEntity` folder segment present in the file path; it matches the namespace `Position` also uses, so this is an existing repo convention, not a one-off mistake)

```csharp
public class LegalEntity : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? RegistrationNumber { get; set; }
    public string CountryCode { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public string? AddressJson { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsPrimary { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
```

`ITenantOwnedEntity` (`src/ONEVO.Domain/Common/ITenantOwnedEntity.cs`) only requires `Guid TenantId { get; }`. `LegalEntity` does **not** derive from `BaseEntity` and has **no** `IsDeleted`/`DeletedAt` columns — soft-delete semantics for this entity are `IsActive` only, which matches the OneVo-HR schema doc (also `is_active`-only, no separate delete flag).

### 1.2 EF configuration

**File:** `src/ONEVO.Infrastructure/Persistence/Configurations/DevPlatform/Tenancy/LegalEntityConfiguration.cs`

```csharp
builder.ToTable("legal_entities");
builder.HasKey(l => l.Id);
builder.Property(l => l.Name).HasMaxLength(200).IsRequired();
builder.Property(l => l.RegistrationNumber).HasMaxLength(50);
builder.Property(l => l.CountryCode).HasMaxLength(3).IsRequired();
builder.Property(l => l.CurrencyCode).HasMaxLength(3).IsRequired();
builder.Property(l => l.AddressJson).HasColumnType("jsonb");
builder.HasIndex(l => l.TenantId);
```

No unique constraints (name, registration number are **not** unique at the DB level today). No explicit FK to `tenants` in the fluent config (isolation is enforced via RLS instead — see 1.3).

### 1.3 `legal_entities` schema (from migrations)

Table was created in `20260510103730_SeedPhaseOnePlanModules.cs` (verified directly — `CreateTable(name: "legal_entities", ...)` at line 15, `CreateIndex(name: "ix_legal_entities_tenant_id", ...)` at line 65) and is unchanged at the column level since (confirmed against `ApplicationDbContextModelSnapshot.cs:2785-2846`). Current columns:

| Column | Type | Nullable | Notes |
|---|---|---|---|
| `id` | `uuid` | PK | `pk_legal_entities` |
| `tenant_id` | `uuid` | not null | indexed (`ix_legal_entities_tenant_id`), not unique |
| `name` | `character varying(200)` | not null | |
| `registration_number` | `character varying(50)` | nullable | |
| `country_code` | `character varying(3)` | not null | |
| `currency_code` | `character varying(3)` | not null | |
| `address_json` | `jsonb` | nullable | |
| `is_active` | `boolean` | not null | |
| `is_primary` | `boolean` | not null | |
| `created_at` | `timestamp with time zone` | not null | |
| `updated_at` | `timestamp with time zone` | nullable | |

`legal_entities` is in the RLS-protected table list added by `20260515022320_AddRlsPolicies.cs`, so tenant isolation at the DB layer already exists for this table. No `countries` table exists anywhere in the migration history (confirmed via search) — `CountryCode` is a free string, not FK-validated.

### 1.4 Repository

**Interface:** `src/ONEVO.Application/Features/DevPlatform/Tenancy/RepositoryInterfaces/ILegalEntityRepository.cs`
```csharp
public interface ILegalEntityRepository
{
    Task AddAsync(LegalEntity legalEntity, CancellationToken ct = default);
    Task<LegalEntity?> GetPrimaryByTenantIdAsync(Guid tenantId, CancellationToken ct = default);
}
```
**Implementation:** `src/ONEVO.Infrastructure/Persistence/Repositories/DevPlatform/Tenancy/EfLegalEntityRepository.cs` — implements exactly those two methods directly against `_db.LegalEntities`. Does **not** inherit `BaseRepository<T>` (that base class requires `T : BaseEntity` with `IsDeleted`, which `LegalEntity` doesn't have). No `GetByIdAsync`, `ListByTenantAsync`, `GetByNameForTenantAsync`, `UpdateAsync`, or any pagination/filter method exists today.

**Note the folder location:** although the domain entity lives under `OrgStructure`, the repository interface/impl live under `DevPlatform/Tenancy`. See §8 (Risks) — this is an architectural inconsistency the plan must resolve before Part 2.

### 1.5 Existing APIs

**None.** There is no tenant-facing Legal Entity / Company controller anywhere in `src/ONEVO.Api/Controllers`. Full controller inventory confirmed by directory listing — tenant controllers exist only for Auth, Billing, Integrations, Legal, Permissions, Roles. `LegalEntity` is touched in exactly two places, both **Developer Platform / admin-only**:

- `CreateTenantCommandHandler` (`Features/DevPlatform/Tenancy/Commands/CreateTenant/CreateTenantCommandHandler.cs:111-121`) — creates one primary `LegalEntity` as a side effect of tenant provisioning (admin-only), setting `Name`, `RegistrationNumber`, `CountryCode`, `CurrencyCode`, `IsPrimary = true`.
- `GetTenantByIdQueryHandler` (`Features/DevPlatform/Tenancy/Queries/GetTenantById/GetTenantByIdQueryHandler.cs`) — reads the primary legal entity via `GetPrimaryByTenantIdAsync` to surface `LegalEntityName`, `RegistrationNumber`, `Country`, `Currency` on the admin tenant-detail DTO.

The `OrgStructure` **Application** feature folder is essentially empty: the only file is `Features/OrgStructure/Position/RepositoryInterfaces/IPositionRepository.cs` (an interface stub with no implementation, no commands/queries/handlers). This entire area — Legal Entity, Position, Department — is greenfield on the tenant-facing side.

### 1.6 Existing tests

No test references `LegalEntity` directly by name except incidentally through tenant-provisioning tests: `tests/ONEVO.Tests.Unit/Features/Tenancy/CreateTenantCommandHandlerTests.cs`, `CreateTenantCommandValidatorTests.cs`, `SubscriptionTrialAndGracePeriodTests.cs`. There are **no** LegalEntity-specific unit, integration, or architecture tests.

### 1.7 Patterns confirmed for reuse (from analogous, already-implemented features)

- **Controller pattern** (`Controllers/Tenant/Roles/RolesController.cs`, `Controllers/Tenant/Permissions/PermissionsController.cs`): `[ApiController]`, `[Route("api/v1/...")]`, class-level `[Authorize(Policy = "TenantPolicy")]`, per-action `[RequirePermission("resource:action")]`, `IMediator`, returns `Ok`/`NoContent`/`CreatedAtAction` on success or `Problem(result.Error, statusCode: result.StatusCode ?? 400)` on failure.
- **`RequirePermissionAttribute`** (`Api/Filters/RequirePermissionAttribute.cs`): `IAuthorizationFilter`; 401 if `ICurrentUser.IsAuthenticated` is false; RFC7807-shaped 403 body (`type`, `title`, `status`, `detail`) if `ICurrentUser.HasPermission(permission)` is false.
- **`Result<T>` / `Result`** (`Application/Common/Models/Result.cs`): `Success`, `Failure(msg, statusCode=400)`, `NotFound(msg)` → 404, `Forbidden(msg)` → 403, `Conflict(msg)` → 409.
- **Handler shape** (e.g. `ListRolesQueryHandler`, `CreateRoleCommandHandler`): manually checks `_currentUser.IsAuthenticated` and `_currentUser.TenantId != Guid.Empty` at the top of `Handle`, then uses `_currentUser.TenantId` for every repository call — **tenant id is never read from the request/body**.
- **Duplicate-name check pattern** (`CreateRoleCommandHandler.cs:58-60`): `var existing = await _roles.GetByNameForTenantAsync(tenantId, name, ct); if (existing is not null) return Result<T>.Conflict($"A role named '{name}' already exists.");` — this is the model to replicate for duplicate company name/code/registration-number checks.
- **Validator pattern** (`CreateTenantCommandValidator.cs`, `UpdateTenantCommandValidator.cs`): FluentValidation `AbstractValidator<TCommand>`, `RuleFor(x => x.Field).NotEmpty().MaximumLength(n)`, `.Matches(regex)`, conditional `.When(x => x.Field is not null)` for PATCH-style partial updates. `EmailAddress()` is a used built-in rule elsewhere (`InviteTenantAdminCommandValidator.cs` and others).
- **Audit/history pattern to replicate** — `TenantStatusHistory` (`Domain/Features/InfrastructureModule/Tenancy/Entities/TenantStatusHistory.cs`): `Id, TenantId, FromStatus, ToStatus, Reason, ChangedById (Guid?), ChangedAt`. Written by `ChangeTenantStatusCommandHandler.cs` via `ITenantStatusHistoryRepository.AddAsync(...)` **in the same `SaveChangesAsync` call** as the entity mutation — no outbox message is used for this particular flow. This is the most direct precedent in the codebase for a `legal_entity_change_histories`-style table, per project memory ("ChangeTenantStatusCommandHandler now writes tenant_status_histories rows").
- **Architecture-test pattern to replicate** — `tests/ONEVO.Tests.Architecture/TenantStatusHistoryArchitectureTests.cs`: reflection-based assertion of the exact property/column inventory on an entity, plus assertions that a migration touches only the documented tables/FKs and never invents extra tables. Recommended model for a `LegalEntityArchitectureTests.cs` guarding the new columns.
- **Error-code convention**: snake_case string constants in a static `*ErrorCodes` class mapped to explicit HTTP codes, e.g. `StorageQuotaErrorCodes.QuotaExceeded = "storage_quota_exceeded"` (`Features/Storage/Quota/Helpers/StorageQuotaErrorCodes.cs`).
- **File upload pattern** (`Application/Features/Storage/File/ServiceInterfaces/IFileStorageService.cs`): single reusable entry point — `UploadAsync(tenantId, userId, originalFileName, contentType, purpose, Stream, ct) -> Result<FileRecordDto>`. No feature is allowed to touch object storage or `file_records` directly. OneVo-HR's `infrastructure/file-storage.md` confirms the convention for a logo: **direct FK column** (`logo_file_id`) on the owning entity — not `entity_assets` (that's reserved for owners with no dedicated column or multi-file cases).

### 1.8 Permissions

**File:** `src/ONEVO.Infrastructure/Persistence/Seeders/PermissionSeeder.cs`

```csharp
// Organization
Perm("org:read", "View org structure, departments, hierarchy.", "org"),
Perm("org:manage", "Create and edit org structure, departments.", "org"),
```

**Both `org:read` and `org:manage` already exist** — no new permission needs to be seeded. `settings:admin` and `settings:branding` also exist but are unrelated per OneVo-HR docs (see §2/§8 — `settings:branding`'s seeder description text is stale relative to the current doc-confirmed model).

---

## 2. Gap Analysis Against Screen Designs

| Screen field | Backend status | Notes |
|---|---|---|
| Company logo | **Missing** | No `logo_file_id` column. Docs confirm the design (`legal_entities.logo_file_id` → `file_records`). |
| Company name | **Exists** | `Name` / `name varchar(200) NOT NULL`. No DB uniqueness today. |
| Company code | **Missing, undocumented** | No OneVo-HR doc mentions a company/legal-entity "code" (unlike `departments.code` / `positions.code`, which are documented). |
| Company registration number | **Exists** | `RegistrationNumber` / `registration_number varchar(50)` nullable. **Doc conflict:** `legal-entity-setup.md` states registration number "belongs to the Add Company flow and is not part of this screen unless a later decision makes it editable." Screen designs require it on General Settings — see §8. |
| Tax registration number | **Missing** | Docs define one generic `tax_identifier varchar(80)` field, not a "tax registration number" specifically named. |
| VAT/GST number | **Missing, undocumented as a separate field** | Docs only mention the single `tax_identifier` field — no separate VAT/GST concept documented. See §8. |
| Company email | **Missing, undocumented** | No doc reference to a company-level email field. |
| Phone number | **Missing, undocumented** | No doc reference to a company-level phone field. |
| Website | **Missing, undocumented** | No doc reference to a company-level website field. |
| Status active/inactive | **Exists** | `IsActive` / `is_active boolean`. |
| Country | **Exists, representation conflict** | Code: `CountryCode varchar(3)` free string, no lookup table. Docs: `country_id uuid FK -> countries`, and a `countries` table (`id, name, code, phone_code, currency_code`) is documented in `database/schemas/infrastructure.md` — **but that table does not exist anywhere in the actual migrations.** See §8. |
| Currency | **Exists** | `CurrencyCode varchar(3)` ISO 4217 — matches docs exactly. |
| Timezone | **Missing** | Docs: `legal_entities.timezone varchar(50)` (IANA). Required on the General Settings screen per `Userflow/Configuration/tenant-settings.md`, but not part of the documented Create Company payload. |
| Financial year start month | **Missing, undocumented anywhere** | No OneVo-HR doc mentions this concept for `legal_entities` or elsewhere. |
| First day of week | **Missing** | Docs: `week_start_day smallint`, described as "1-7, **implementation-defined mapping**" — i.e. even the docs don't pin down whether 1=Sunday or 1=Monday. Must be decided in Part 2. |
| Standard working days | **Missing at this scope** | A **different**, tenant-wide table (`tenant_settings.work_week_days_json`, module `Configuration`) already carries something conceptually similar, but scoped to the whole tenant, not per Company. No company-level equivalent is documented. See §8. |
| Default language | **Missing** | Docs: `legal_entities.default_language varchar(10)`. |
| Date format | **Missing** | Docs: `legal_entities.date_format varchar(20)`. |
| Time format | **Missing, undocumented anywhere** | No OneVo-HR doc mentions a company-level time-format field. |
| Registered business address | **Exists** | `AddressJson` / `address_json jsonb`. Docs confirm this is the business/legal address only (not an attendance/geofence location) — matches existing column exactly. |
| Parent company | **Missing** | Docs: `parent_legal_entity_id uuid FK -> legal_entities`, self-referencing, nullable. **Doc conflict:** the field is described only as part of the *Create Company* modal ("optional parent Company relationship"); it is **not** listed among the Company General Settings edit fields in `Userflow/Configuration/tenant-settings.md`. Screen designs (per this task) expect it as a general-settings field too — see §8. |

---

## 3. Schema Decision

All new fields are proposed as columns on the existing `legal_entities` table (per the instruction to evolve, not fork). No code/doc evidence justifies a separate table for any of these fields — they are all 1:1 attributes of a single Company record.

| Field | Column | Type | Null | Default | Index/Unique | Already exists under another name? |
|---|---|---|---|---|---|---|
| Company logo | `logo_file_id` | `uuid` | nullable | — | FK → `file_records(id)`; EF auto-creates FK index | No |
| Company code | `code` | `varchar(20)` | nullable | — | Partial unique `(tenant_id, code) WHERE code IS NOT NULL` | No — **undocumented field, confirm with product before Part 2** |
| Tax registration number | `tax_identifier` | `varchar(80)` | nullable | — | none | No — but **name matches documented `tax_identifier`**; recommend reusing this exact name for "tax registration number" rather than inventing a new column |
| VAT/GST number | `vat_gst_number` | `varchar(50)` | nullable | — | none | No — **undocumented as distinct from `tax_identifier`; confirm semantics before Part 2** |
| Company email | `email` | `varchar(254)` | nullable | — | none | No |
| Phone number | `phone` | `varchar(20)` | nullable | — | none | No (matches the `varchar(20)` convention used for `employees.phone` per model snapshot) |
| Website | `website` | `varchar(255)` | nullable | — | none | No |
| Country | *(no change recommended for Part 2)* | — | — | — | — | `country_code varchar(3)` already exists; do **not** introduce `country_id`/`countries` FK in this phase — see §8 |
| Timezone | `timezone` | `varchar(50)` | nullable | — | none | No |
| Financial year start month | `financial_year_start_month` | `smallint` | nullable | none (do not default silently — confirm with product) | CHECK `financial_year_start_month BETWEEN 1 AND 12` | No — **undocumented, confirm before Part 2** |
| First day of week | `week_start_day` | `smallint` | nullable | none until convention is picked | CHECK `week_start_day BETWEEN 1 AND 7` | No — column name matches docs, but **numbering convention must be decided** (recommend ISO-8601: 1=Monday…7=Sunday) before Part 2 |
| Standard working days | `standard_working_days_json` | `jsonb` | nullable | — | none | No — **scope conflict with tenant-wide `tenant_settings.work_week_days_json`; confirm precedence before Part 2** |
| Default language | `default_language` | `varchar(10)` | nullable | — | none | No |
| Date format | `date_format` | `varchar(20)` | nullable | — | none | No |
| Time format | `time_format` | `varchar(10)` | nullable | — | CHECK `time_format IN ('12h','24h')` (recommended) | No — **undocumented, confirm allowed values before Part 2** |
| Registered business address | *(no change)* | — | — | — | — | Already `address_json jsonb` |
| Parent company | `parent_legal_entity_id` | `uuid` | nullable | — | self-referencing FK → `legal_entities(id)` `ON DELETE RESTRICT`; index for cycle-detection lookups | No |
| (defense-in-depth, optional) Duplicate name guard | — | — | — | — | Optional partial unique `(tenant_id, lower(name))` — **note:** the existing analogous feature (Roles) relies on **app-level** duplicate checks only, no DB constraint. Recommend adding the DB constraint here anyway since Company identity is higher-stakes, but flag as a deviation from the established pattern for Part 2 sign-off. | — |
| (validation-plan requirement) Duplicate registration number guard | — | — | — | — | Partial unique `(tenant_id, registration_number) WHERE registration_number IS NOT NULL` — **note:** this exceeds current OneVo-HR doc coverage (docs only list "duplicate name" as an error scenario), but is explicitly required by this task's validation plan. | — |

---

## 4. API Contract Plan

All routes sit under the tenant-facing org app, authenticated via the existing `[Authorize(Policy = "TenantPolicy")]` scheme (same as `RolesController`/`PermissionsController`). Tenant id is **never** accepted from the request body or route — it comes from `ICurrentUser.TenantId`, exactly like every existing tenant handler.

### `GET /api/v1/org/legal-entities`
- **Permission:** `org:read`
- **Purpose:** Company selector / list.
- **Response 200:**
```json
{
  "items": [
    {
      "id": "guid",
      "name": "string",
      "code": "string|null",
      "isActive": true,
      "isPrimary": true,
      "logoUrl": "string|null"
    }
  ]
}
```
- Only companies for the current tenant (RLS + explicit tenant filter). Default view excludes inactive companies unless `includeInactive=true` is passed (mirrors the "inactive company does not appear in default selector" requirement in §7).

### `GET /api/v1/org/legal-entities/{id}/general-settings`
- **Permission:** `org:manage` (per explicit instruction: General Settings screen requires `org:manage`, not `org:read`, for any purpose including viewing it — OneVo-HR docs confirm: *"`org:manage` is the only permission for this flow... Do not define a read-only permission variation for this screen."*)
- **Response 200:**
```json
{
  "id": "guid",
  "name": "string",
  "code": "string|null",
  "registrationNumber": "string|null",
  "taxIdentifier": "string|null",
  "vatGstNumber": "string|null",
  "email": "string|null",
  "phone": "string|null",
  "website": "string|null",
  "isActive": true,
  "countryCode": "string",
  "currencyCode": "string",
  "timezone": "string|null",
  "financialYearStartMonth": "int|null",
  "weekStartDay": "int|null",
  "standardWorkingDays": [1,2,3,4,5],
  "defaultLanguage": "string|null",
  "dateFormat": "string|null",
  "timeFormat": "string|null",
  "address": { "line1": "...", "line2": "...", "city": "...", "state": "...", "postalCode": "...", "country": "..." },
  "parentLegalEntityId": "guid|null",
  "parentLegalEntityName": "string|null",
  "logoFileId": "guid|null",
  "logoUrl": "string|null",
  "createdAt": "datetimeoffset",
  "updatedAt": "datetimeoffset|null"
}
```
- 404 if `{id}` doesn't belong to the current tenant (never leak cross-tenant existence).

### `POST /api/v1/org/legal-entities`
- **Permission:** `org:manage`
- **Request:**
```json
{
  "name": "string (required)",
  "registrationNumber": "string|null",
  "taxIdentifier": "string|null",
  "countryCode": "string (required, ISO 3166-1 alpha-3)",
  "currencyCode": "string|null (ISO 4217; required until a countries lookup table exists to auto-default it — see §8)",
  "address": { "...": "..." } ,
  "parentLegalEntityId": "guid|null"
}
```
- **Response 201:** same shape as the General Settings GET response, minus fields not collected at creation (timezone, financial year month, week start day, standard working days, default language, date/time format, logo, code, VAT/GST, email/phone/website remain null and are filled in later via General Settings, matching the doc's statement that Create Company only captures identity + legal basics).
- `Location` header → `GET .../{id}/general-settings`.

### `PUT /api/v1/org/legal-entities/{id}/general-settings`
- **Permission:** `org:manage`
- **Request:** full field set from the GET response (name, code, registrationNumber, taxIdentifier, vatGstNumber, email, phone, website, isActive, countryCode, currencyCode, timezone, financialYearStartMonth, weekStartDay, standardWorkingDays, defaultLanguage, dateFormat, timeFormat, address, parentLegalEntityId).
- **Response 200:** updated General Settings DTO.
- Setting `isActive = false` here is the deactivation path (see Delete below) — OR keep deactivation exclusively on the DELETE endpoint and make `isActive` in this payload read-only/ignored. **Decision needed for Part 2** — recommend: `isActive` toggle via this PUT is allowed for reactivation, but *deactivation* only happens through the dedicated DELETE endpoint so the confirm-name safety check always applies uniformly.

### `DELETE /api/v1/org/legal-entities/{id}`
- **Permission:** `org:manage`
- **Request:**
```json
{ "confirmName": "string (required)" }
```
- **Validation:** `confirmName` must exactly match the company's current `name` (case-sensitive exact match, no trimming beyond a single leading/trailing whitespace trim — mirrors how names are trimmed on create/update elsewhere in the codebase, e.g. `request.LegalEntityName.Trim()` in `CreateTenantCommandHandler`).
- **Business rule:** reject with 409 if this is the tenant's last **active** company (`is_active = true` count == 1 and this is it).
- **Behavior:** **soft delete** (`is_active = false`), not physical delete. Nothing in OneVo-HR docs requires physical deletion, and `departments`/`positions` in the same module both use logical deletion (`is_active = false`) with history preserved — this is the established pattern to follow. Physical `DELETE FROM legal_entities` is **not** recommended: departments, positions, employee assignments, calendars, etc. all resolve through `legal_entity_id`, so hard-deleting would orphan or cascade-destroy unrelated history.
- **Response:** 204 No Content on success; 404 if not found/not in tenant; 409 for confirm-name mismatch or last-active-company.
- **Note:** `confirmName` mismatch and "last active company" are two distinct failure reasons and must return distinguishable error messages (see §5).

### `PUT /api/v1/org/legal-entities/{id}/logo`
- **Permission:** `org:manage`
- **Request:** `multipart/form-data` file upload, routed through `IFileStorageService.UploadAsync(tenantId, userId, fileName, contentType, purpose: "legal_entity_logo", stream, ct)` — never touch `file_records`/object storage directly, per `infrastructure/file-storage.md`.
- **Response 200:** `{ "logoFileId": "guid", "logoUrl": "string" }`.
- Replaces any existing `logo_file_id` (Upload/Replace are the same operation per the docs: *"Upload, Replace, and Remove actions"*).

### `DELETE /api/v1/org/legal-entities/{id}/logo`
- **Permission:** `org:manage`
- Sets `logo_file_id = NULL`. Response 204. (Whether the underlying `file_records` row is also deleted/GC'd is an `IFileStorageService` concern, not this feature's — do not reach into file storage internals from this handler.)

---

## 5. Validation Plan

| Rule | Trigger | Exact error message | HTTP |
|---|---|---|---|
| Required name | Create / Update | `"Company name is required."` | 400 |
| Duplicate name inside tenant | Create / Update | `"Company name already exists."` (verbatim string from `legal-entity-setup.md` / `tenant-settings.md` error tables) | 409 |
| Required registration number | Create *(if product confirms it's mandatory at creation — docs don't say it's required, only that it's collected)* | `"Registration number is required."` | 400 |
| Duplicate registration number inside tenant | Create / Update | `"A company with this registration number already exists."` | 409 |
| Duplicate company code inside tenant | Create / Update | `"A company with this code already exists."` | 409 |
| Invalid email | Update | `"Email address is invalid."` | 400 |
| Invalid phone | Update | `"Phone number is invalid."` | 400 |
| Invalid website | Update | `"Website URL is invalid."` | 400 |
| Unsupported country | Create / Update | `"Country is not supported."` (verbatim from docs error table) | 422 |
| Unsupported currency | Create / Update | `"Currency is not supported."` (verbatim from docs error table) | 422 |
| Invalid timezone | Update | `"Invalid timezone selected."` (verbatim from docs error table) | 422 |
| Invalid financial year start month | Update | `"Financial year start month must be between 1 and 12."` | 400 |
| Empty standard working days | Update *(if product decides it becomes required — currently undocumented, treat as optional unless product says otherwise)* | `"At least one standard working day is required."` | 400 |
| Invalid weekday values | Update | `"Standard working days must contain valid weekday values."` | 400 |
| Parent company not found | Create / Update | `"Parent company not found."` | 404 |
| Parent belongs to another tenant | Create / Update | `"Parent company not found."` (same message as not-found — never confirm cross-tenant existence) | 404 |
| Parent cycle | Create / Update | `"This parent relationship would create a cycle."` (verbatim from docs error table) | 422 |
| Delete confirmation mismatch | Delete | `"Company name confirmation does not match."` | 409 |
| Delete last active company | Delete | `"Cannot delete or deactivate the last active company in the tenant."` | 409 |

Country/currency validation note: since no `countries` lookup table exists yet (§1.3, §8), "unsupported country" cannot be checked against a real table in Part 2 unless that table is built first. Until then, this rule can only validate ISO 3166-1 alpha-3 **format** (3 uppercase letters), not real-world support — flag this explicitly to the caller of Part 2's plan; it is a materially weaker check than the docs imply.

---

## 6. Implementation File Plan (Part 2 — not created now)

Following the folder convention documented in `OneVo-HR/CLAUDE.md` (`{Layer}/Features/{Feature}/{SubFeature}/...`), with `Feature = OrgStructure`, `SubFeature = LegalEntity`:

**Domain**
- Modify: `ONEVO.Domain/Features/OrgStructure/LegalEntity/Entities/LegalEntity.cs` — add all new properties from §3.
- Create (if the history-table decision in §8 is approved): `ONEVO.Domain/Features/OrgStructure/LegalEntity/Entities/LegalEntityChangeHistory.cs`.

**Infrastructure**
- Modify: `ONEVO.Infrastructure/Persistence/Configurations/DevPlatform/Tenancy/LegalEntityConfiguration.cs` — add column mappings, unique/partial indexes, FKs (`logo_file_id`, `parent_legal_entity_id`). *(Relocate to `Configurations/OrgStructure/LegalEntity/` only if the folder-ownership decision in §8 is made — otherwise modify in place.)*
- Modify: `ONEVO.Infrastructure/Persistence/Repositories/DevPlatform/Tenancy/EfLegalEntityRepository.cs` — add `GetByIdForTenantAsync`, `ListByTenantAsync`, `GetByNameForTenantAsync`, `GetByRegistrationNumberForTenantAsync`, `GetByCodeForTenantAsync`, `UpdateAsync` (or rely on EF change tracking + `IUnitOfWork.SaveChangesAsync`), `CountActiveByTenantAsync`.
- Create: EF migration adding the new `legal_entities` columns + FKs/indexes (Part 2, not this phase).
- Create (if history table approved): `Configurations/.../LegalEntityChangeHistoryConfiguration.cs`, `Repositories/.../EfLegalEntityChangeHistoryRepository.cs`.

**Application**
- Modify: `ONEVO.Application/Features/DevPlatform/Tenancy/RepositoryInterfaces/ILegalEntityRepository.cs` — add the methods above. *(Relocate to `Features/OrgStructure/LegalEntity/RepositoryInterfaces/` only if the folder-ownership decision in §8 is made.)*
- Create: `Features/OrgStructure/LegalEntity/Commands/CreateLegalEntity/{CreateLegalEntityCommand,CreateLegalEntityCommandHandler,CreateLegalEntityCommandValidator}.cs`
- Create: `Features/OrgStructure/LegalEntity/Commands/UpdateLegalEntityGeneralSettings/{...Command,...Handler,...Validator}.cs`
- Create: `Features/OrgStructure/LegalEntity/Commands/DeleteLegalEntity/{...Command,...Handler,...Validator}.cs`
- Create: `Features/OrgStructure/LegalEntity/Commands/UpdateLegalEntityLogo/{...Command,...Handler}.cs`
- Create: `Features/OrgStructure/LegalEntity/Commands/RemoveLegalEntityLogo/{...Command,...Handler}.cs`
- Create: `Features/OrgStructure/LegalEntity/Queries/ListLegalEntities/{...Query,...Handler}.cs`
- Create: `Features/OrgStructure/LegalEntity/Queries/GetLegalEntityGeneralSettings/{...Query,...Handler}.cs`
- Create: `Features/OrgStructure/LegalEntity/DTOs/Requests/{CreateCompanyRequest,UpdateCompanyGeneralSettingsRequest,DeleteCompanyRequest}.cs`
- Create: `Features/OrgStructure/LegalEntity/DTOs/Responses/{CompanyListItemDto,CompanyGeneralSettingsDto}.cs`
- Create: `Features/OrgStructure/LegalEntity/Mappings/LegalEntityMapper.cs` (manual mapping, no AutoMapper per project convention)
- Create: `Features/OrgStructure/LegalEntity/Helpers/LegalEntityErrorCodes.cs` (if structured error codes are wanted beyond plain messages)

**API**
- Create: `ONEVO.Api/Controllers/Tenant/OrgStructure/LegalEntitiesController.cs` implementing the 7 endpoints from §4.

**Tests**
- Create: `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/CreateLegalEntityCommandHandlerTests.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/UpdateLegalEntityGeneralSettingsCommandHandlerTests.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/DeleteLegalEntityCommandHandlerTests.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/*ValidatorTests.cs`
- Create: `tests/ONEVO.Tests.Architecture/LegalEntityArchitectureTests.cs` (modeled on `TenantStatusHistoryArchitectureTests.cs`)
- Create: `tests/ONEVO.Tests.Integration/OrgStructure/LegalEntity/LegalEntitiesControllerTests.cs`

---

## 7. Test Plan (Part 2+ — not written now)

**Unit tests**
- Create succeeds with valid country/currency/name.
- Duplicate name / code / registration number inside tenant rejected.
- Invalid email/phone/website format rejected.
- Invalid financial-year-start-month / weekday values rejected.
- Parent not found, parent in another tenant (must read as not-found, not forbidden), parent cycle all rejected.
- Delete: confirm-name mismatch rejected; last-active-company rejected; valid delete soft-deactivates (`is_active=false`) and does not physically remove the row.
- No handler accepts `tenantId` from the request/command payload — every handler sources it from `ICurrentUser.TenantId` only (assert by inspecting command shape / constructor params in a unit test or architecture test).

**Architecture tests**
- `LegalEntity` still implements `ITenantOwnedEntity`.
- Reflection-based exact column/property inventory check (guards against silent drift), modeled on `TenantStatusHistoryArchitectureTests.TenantStatusHistory_HasExactlyTheInventoryColumns`.
- New migration touches only `legal_entities` (+ history table if approved) — no unexpected `CreateTable`/`DropTable`.
- `LegalEntitiesController` actions have `[RequirePermission(...)]` on every action; `GET /legal-entities` uses `org:read`; every other action uses `org:manage` — assert this by reflecting over the controller's method attributes, not by hand-inspection.

**Integration tests (real Postgres via Testcontainers, per OneVo-HR test-coverage doc)**
- `tenant_id` is never accepted from the request body — a POST/PUT with a foreign `tenantId` field in the JSON body has no effect (ignored/unbound), and the created/updated row always belongs to the authenticated tenant.
- `org:manage` required for create/update/delete/logo endpoints — a user with only `org:read` gets 403.
- `org:read` is sufficient for the list endpoint but not for General Settings GET/PUT (per docs, `org:manage` gates General Settings entirely, including viewing).
- RLS / tenant isolation: a user from Tenant A cannot read, update, or delete Tenant B's legal entity (404, not 403, to avoid existence leakage).
- Cannot delete/deactivate the last active company in a tenant (409).
- Duplicate name/code/registration-number checks are tenant-scoped (same name is fine across two different tenants, rejected within one tenant).
- Parent cycle rejected (A→B→A) and rejected across a longer chain (A→B→C→A).
- Deactivated/deleted company does not appear in the default `GET /legal-entities` list (only visible with an explicit `includeInactive=true`, if that flag is built).
- Update to Company X does not affect Company Y in the same tenant or any company in another tenant (isolation at the row level, not just the endpoint level).

---

## 8. Risks / Open Questions

These are explicit conflicts or unknowns found during the audit. None are guessed at — each is either a direct code/doc mismatch or a documented gap.

1. **Folder/namespace ownership inconsistency (blocking, must resolve before Part 2 file plan is final).** The `LegalEntity` domain entity lives under `OrgStructure`, but its EF configuration, repository interface, and repository implementation all live under `DevPlatform/Tenancy` (`Application/Features/DevPlatform/Tenancy/RepositoryInterfaces/ILegalEntityRepository.cs`, `Infrastructure/Persistence/{Configurations,Repositories}/DevPlatform/Tenancy/...`). Per `OneVo-HR/CLAUDE.md`, `DevPlatform` is explicitly reserved for "tenant management, subscription, provisioning, billing, and role templates" — Legal Entity / Company is none of those; it's Org Structure. **Recommendation:** move the repository interface + implementation + EF config into `Features/OrgStructure/LegalEntity/...` as part of Part 2, and have `CreateTenantCommandHandler`/`GetTenantByIdQueryHandler` (DevPlatform) reference the relocated interface. This is a mechanical move with no behavior change, best done as the *first* task of Part 2 before adding new methods, so the diff for the new methods is clean.

2. **Country representation conflict.** Code stores `CountryCode varchar(3)` as a free string with no lookup table. OneVo-HR docs (`legal-entities/overview.md`, `database/schemas/org-structure.md`, `database/schemas/infrastructure.md`) describe `country_id uuid FK -> countries` backed by a `countries` table (`id, name, code, phone_code, currency_code`). **That `countries` table does not exist anywhere in the actual migration history.** Building it is a larger, cross-cutting effort (touches phone-code lookups, currency defaulting on create, etc.) than this feature alone. **Recommendation:** keep `country_code varchar(3)` for Part 2 (matches what `CreateTenantCommandHandler` already does today) and treat introducing `countries`/`country_id` as a separate, explicitly-scoped follow-up — do not fold it into this feature silently.

3. **Currency auto-default from country is undeliverable without the countries table.** `end-to-end-logic.md` states step 2 of Create Company is "Default currency from country when omitted" — this requires a country→currency lookup, which doesn't exist without the `countries` table from point 2. **Recommendation:** require `currencyCode` explicitly on create in Part 2, and drop the auto-default behavior until the lookup table exists (or hardcode a small static ISO mapping as a stopgap — product decision).

4. **Screen-required fields with no OneVo-HR documentation at all:** company code, VAT/GST number (as distinct from `tax_identifier`), company email, phone number, website, financial year start month, time format. None of these appear in any doc under `modules/org-structure/`, `Userflow/Org-Structure/`, or `database/schemas/`. They are not "wrong" — they're simply undocumented. **Recommendation:** confirm exact semantics/validation with product before Part 2 builds them, rather than inventing rules. Do not block Part 2 on this — add the columns as nullable with minimal validation and let a product/doc update follow.

5. **Registration number scope conflict.** `Userflow/Org-Structure/legal-entity-setup.md` explicitly states registration number "belongs to the Add Company flow and is not part of this screen [General Settings] unless a later decision makes it editable." The screen designs behind this task require it on General Settings. **Recommendation:** treat the screen design as the current source of truth (it's newer than the doc) and make it editable via `PUT .../general-settings`, but flag the doc as needing an update — this report should not be read as license to silently override docs elsewhere without the same flag.

6. **Parent company scope conflict.** Same shape of conflict as #5 — docs put "optional parent Company relationship" only in the Create Company modal, not in the General Settings edit-field table. Screen designs require it in both. **Recommendation:** same as #5 — allow editing via General Settings PUT, flag doc as stale.

7. **"First day of week" numbering is explicitly undefined even in the docs** (`week_start_day smallint, "1-7, implementation-defined mapping"`). This must be pinned down (recommend ISO-8601, 1=Monday) before the column is built, or every consumer (Angular date-format-mask logic, backend defaults) will drift independently.

8. **Standard working days — table/scope collision.** A tenant-wide `tenant_settings.work_week_days_json` already exists (module `Configuration`, different table entirely) for "work week days." The screen design wants this at the **Company** level. Nothing in the docs says how a per-company value should relate to the tenant-wide one (override? tenant-wide is just the seed default for new companies? tenant-wide becomes dead once companies are created?). **Recommendation:** do not touch `tenant_settings` in this feature; add `legal_entities.standard_working_days_json` as an independent company-level column, and raise the precedence question to product as a separate decision — implementing a fallback/inheritance rule now would be guessing.

9. **Audit pattern conflict: outbox vs. history table.** OneVo-HR's `company-profile/end-to-end-logic.md` and `overview.md` prescribe an **outbox pattern** (`LegalEntityCreated`, `LegalEntityUpdated`, `LegalEntityCountrySet` → `outbox_messages`, consumed by e.g. the Calendar module). The closest actual precedent in the codebase (`ChangeTenantStatusCommandHandler` → `tenant_status_histories`) uses a **plain history-table row written in the same `SaveChangesAsync` call, no outbox at all**. `20260703155455_AddOutboxAndIdempotency` confirms an outbox mechanism does exist in the codebase generally, but nothing in this area currently uses it. **Recommendation:** implement both, matching each to its actual purpose — write an `outbox_messages` row for `LegalEntityCreated`/`LegalEntityCountrySet` only if a real consumer needs it in Part 2 scope (e.g. Calendar holiday sync, which is a different module/team's responsibility and may be out of scope for Part 2); write a simple change-history row (mirroring `TenantStatusHistory`) for the general audit trail regardless. Do not build outbox plumbing speculatively for a consumer that doesn't exist yet in this codebase.

10. **`settings:branding`'s stale description.** `PermissionSeeder.cs`'s comment text for `settings:branding` says "Manage company logo, colors, and custom domain" — but OneVo-HR's `permissions-reference.md` explicitly states: *"`branding:manage` / `settings:branding` — Removed from the Phase 1 customer app. Company logo/avatar is managed in Settings > General with `org:manage`; no Branding page or tenant-wide colors are defined."* The seeded permission still exists in the DB/enum (presumably retained for other still-active uses, e.g. Developer Platform white-labeling) but its description text is misleading for this feature. This confirms the task's instruction to use `org:manage` (not `settings:admin`/`settings:branding`) is correct — do not act on the stale seeder comment.

11. **DB-level uniqueness is a deviation from the established pattern.** The one directly analogous feature (Roles) enforces "duplicate name" purely at the application layer (`GetByNameForTenantAsync` + `Conflict(...)`), with **no** DB unique constraint. This report recommends adding real partial-unique indexes for company name/code/registration-number anyway (§3), since a race condition on company creation is arguably higher-stakes than on role creation — but this is a deliberate deviation from precedent, not a mechanical copy, and should get explicit sign-off before Part 2.

---

## Part 2 Recommendation

**Part 1 (this audit) is complete. Part 2 can start**, scoped strictly to **schema/entity/repository only**, in this order:

1. Resolve the folder-ownership question (§8-1) — move `ILegalEntityRepository`/`EfLegalEntityRepository`/`LegalEntityConfiguration` from `DevPlatform/Tenancy` to `OrgStructure/LegalEntity`, no behavior change, update the two existing DevPlatform callers' `using` statements.
2. Extend the `LegalEntity` domain entity with the columns from §3 that have no open question (`logo_file_id`, `tax_identifier`, `email`, `phone`, `website`, `timezone`, `default_language`, `date_format`, `parent_legal_entity_id`).
3. Hold the columns flagged as needing a product decision first — `code`, `vat_gst_number`, `financial_year_start_month`, `week_start_day` (numbering convention), `standard_working_days_json` (precedence vs. `tenant_settings`), `time_format` (allowed values) — until §8 items 4, 7, 8 are answered, to avoid a migration rename/rework later.
4. Write the EF configuration changes and the migration (columns + partial unique indexes + FKs), plus the `LegalEntityArchitectureTests.cs` guard, in the same PR.
5. Extend `ILegalEntityRepository`/`EfLegalEntityRepository` with the new query/lookup methods from §6, still with no controller/handler work — that's Part 3.

Do not start the Application/API layer (commands, handlers, controller) until the Part 2 schema is merged, since the DTOs and validators in §4/§5 depend on final column names and the resolved open questions from §8.
